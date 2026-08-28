using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace TFlexDrawingService.Api.Data;

internal static class PricingRequestXlsxBuilder
{
    public static byte[] Build(
        string templatePath,
        PricingSpecification specification,
        UserProject? project,
        PricingCalculationRequest? request)
    {
        var replacements = BuildReplacements(specification, project, request);
        using var buffer = new MemoryStream();
        buffer.Write(File.ReadAllBytes(templatePath));
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            var xmlEntries = archive.Entries.Where(item =>
                item.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).ToArray();
            var emptySharedStringIndexes = new HashSet<int>();

            foreach (var entry in xmlEntries.OrderByDescending(item =>
                         item.FullName.Equals("xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase)))
            {
                XDocument document;
                using (var input = entry.Open())
                {
                    document = XDocument.Load(input, LoadOptions.PreserveWhitespace);
                }

                ReplaceText(document, replacements);
                if (entry.FullName.Equals("xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase))
                {
                    var sharedStrings = document.Root?.Elements()
                        .Where(element => element.Name.LocalName == "si")
                        .ToArray() ?? [];
                    for (var index = 0; index < sharedStrings.Length; index++)
                    {
                        var value = string.Concat(sharedStrings[index].Descendants()
                            .Where(element => element.Name.LocalName == "t")
                            .Select(element => element.Value));
                        if (string.IsNullOrWhiteSpace(value)) emptySharedStringIndexes.Add(index);
                    }
                }
                else if (entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase))
                {
                    ClearBlankSharedStringCells(document, emptySharedStringIndexes);
                }

                using var output = entry.Open();
                output.SetLength(0);
                document.Save(output, SaveOptions.DisableFormatting);
            }
        }

        return buffer.ToArray();
    }

    private static void ClearBlankSharedStringCells(XDocument worksheet, IReadOnlySet<int> blankIndexes)
    {
        foreach (var cell in worksheet.Descendants().Where(element =>
                     element.Name.LocalName == "c" &&
                     string.Equals(element.Attribute("t")?.Value, "s", StringComparison.Ordinal)))
        {
            var value = cell.Elements().FirstOrDefault(element => element.Name.LocalName == "v");
            if (value is null ||
                !int.TryParse(value.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ||
                !blankIndexes.Contains(index))
            {
                continue;
            }

            cell.Attribute("t")?.Remove();
            value.Remove();
        }
    }

    private static IReadOnlyDictionary<string, string> BuildReplacements(
        PricingSpecification specification,
        UserProject? project,
        PricingCalculationRequest? request)
    {
        string Field(params string[] names)
        {
            foreach (var name in names)
            {
                var value = request?.SpecificationFields?.FirstOrDefault(item =>
                    string.Equals(item.Key.Trim(), name, StringComparison.OrdinalIgnoreCase)).Value;
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            return "";
        }

        var options = request?.Options?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray() ?? [];
        var model = FirstText(Field("Model"), specification.Series);
        var stops = request?.Stops.ToString(CultureInfo.InvariantCulture) ?? Field("Stops");
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["projectName"] = FirstText(project?.Name, Field("Project Name")),
            ["negoNo"] = FirstText(project?.FactoryRequestNumber, Field("Contract No")),
            ["country"] = FirstText(Field("Country"), "Россия"),
            ["req_header_1"] = specification.Name,
            ["liftNumbers_1"] = FirstText(Field("Lift No"), specification.Name),
            ["qty_1"] = FirstText(Field("Quantity"), "1"),
            ["type_1"] = model,
            ["capacity_1"] = request?.CapacityKg.ToString(CultureInfo.InvariantCulture) ?? "",
            ["speed_1"] = request?.Speed.ToString("0.##", CultureInfo.InvariantCulture) ?? "",
            ["floors_1"] = FirstText(Field("Floors"), stops),
            ["lobby_1"] = Field("Main Floor"),
            ["rise_1"] = Field("Travel Height", "TR"),
            ["controlSystem_1"] = Field("Control System", "Operation"),
            ["cwtLocation_1"] = Field("CWT Location"),
            ["emergencyExit_1"] = OptionText(options, "Emergency", "EFS"),
            ["hoistwayLighting_1"] = OptionText(options, "HOISTWAY"),
            ["carInside_1"] = JoinDimensions(Field("Car Width", "AA"), Field("Car Depth", "BB"), Field("Car Height", "HL")),
            ["crh_1"] = Field("Car Height", "HL"),
            ["doorOpening_1"] = JoinDimensions(Field("Door Width", "JJ"), Field("Door Height", "HH")),
            ["doorArrangement_1"] = Field("Door Opening", "Door mode"),
            ["hoistway_1"] = Field("Shaft Type"),
            ["hoistwayDim_1"] = JoinDimensions(Field("Shaft Width", "AH"), Field("Shaft Depth", "BH")),
            ["overhead_1"] = Field("Overhead", "OH"),
            ["pit_1"] = Field("Pit", "PD"),
            ["frontFloors_1"] = FirstText(Field("Main Floor"), stops),
            ["rearFloors_1"] = Field("Rear Floors"),
            ["doorType_1"] = Field("Door Opening", "Door mode", "Door type"),
            ["doorSafety_1"] = OptionText(options, "DOOR", "SAFETY"),
            ["fireDoor_1"] = Field("Fire Rating"),
            ["cabinDesign_1"] = Field("Cabin Design", "Car Design"),
            ["wallFront_1"] = Field("Car Wall Material", "Car Design Wall", "Wall"),
            ["wallSide_1"] = Field("Car Wall Material", "Car Design Wall", "Wall"),
            ["wallRear_1"] = Field("Car Wall Material", "Car Design Wall", "Wall"),
            ["carDoor_1"] = Field("Car Door Material", "Car Door"),
            ["ceiling_1"] = Field("Ceiling"),
            ["floor_1"] = FirstText(Field("Floor"), Field("Floor Pattern")),
            ["mirror_1"] = FirstText(Field("Mirror Height"), Field("Mirror")),
            ["handrail_1"] = Field("Handrail"),
            ["copType_1"] = Field("COP"),
            ["copFaceplate_1"] = Field("Car Wall Material", "Wall"),
            ["copButtons_1"] = Field("COP Button"),
            ["cpiType_1"] = Field("COP"),
            ["landingMain_1"] = FirstText(Field("Main Shaft Door"), Field("Main Landing Material")),
            ["landingTypical_1"] = FirstText(Field("Other Shaft Door"), Field("Other Landing Material")),
            ["hallCall_1"] = FirstText(Field("Main LOP"), Field("Other LOP")),
            ["hpiMain_1"] = Field("Main LIP"),
            ["hpiTypical_1"] = Field("Other LIP")
        };

        for (var index = 1; index <= 30; index++)
        {
            values[$"demand{index}_1"] = index <= options.Length ? options[index - 1] : "";
        }

        return values.ToDictionary(item => $"{{{{{item.Key}}}}}", item => item.Value, StringComparer.Ordinal);
    }

    private static void ReplaceText(XDocument document, IReadOnlyDictionary<string, string> replacements)
    {
        var textContainers = document.Descendants().Where(element =>
            element.Name.LocalName is "si" or "is").ToArray();
        foreach (var container in textContainers)
        {
            var textNodes = container.Descendants().Where(element => element.Name.LocalName == "t").ToArray();
            if (textNodes.Length == 0) continue;
            var original = string.Concat(textNodes.Select(node => node.Value));
            var replaced = ReplaceAll(original, replacements);
            if (replaced == original) continue;
            textNodes[0].Value = replaced;
            foreach (var node in textNodes.Skip(1)) node.Value = "";
        }

        foreach (var node in document.DescendantNodes().OfType<XText>())
        {
            node.Value = ReplaceAll(node.Value, replacements);
        }
    }

    private static string ReplaceAll(string value, IReadOnlyDictionary<string, string> replacements)
    {
        foreach (var (placeholder, replacement) in replacements)
        {
            value = value.Replace(placeholder, replacement, StringComparison.Ordinal);
        }
        return value;
    }

    private static string OptionText(IReadOnlyList<string> options, params string[] fragments) =>
        options.FirstOrDefault(option => fragments.All(fragment =>
            option.Contains(fragment, StringComparison.OrdinalIgnoreCase))) ?? "";

    private static string JoinDimensions(params string[] dimensions) =>
        string.Join(" x ", dimensions.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string FirstText(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
}
