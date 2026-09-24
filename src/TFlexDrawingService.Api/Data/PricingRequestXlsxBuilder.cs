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
        PricingCalculationRequest? request, IReadOnlyList<PriceEntry>? optionCatalog = null, IReadOnlyList<SmecVisualEntry>? visualItems = null)
    {
        var replacements = BuildReplacements(specification, project, request, optionCatalog, visualItems);
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
                    AddChineseDescriptions(document);
                    XNamespace ns = document.Root!.Name.Namespace;
                    foreach (var row in document.Descendants(ns + "row").Where(e => int.TryParse(e.Attribute("r")?.Value, out var n) && n >= 52 && n <= 81))
                    {
                        row.SetAttributeValue("ht", row.Attribute("r")?.Value == "81" ? Math.Max(48, replacements.GetValueOrDefault("{{demand30_1}}", "").Split('\n').Length * 40) : 48);
                        row.SetAttributeValue("customHeight", 1);
                    }
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
        PricingCalculationRequest? request, IReadOnlyList<PriceEntry>? optionCatalog, IReadOnlyList<SmecVisualEntry>? visualItems)
    {
        if (request is not null) request = PricingCatalogStore.NormalizeXiziOptions(request);
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

        string Hpi(string lipField, string lopField)
        {
            var lip = Field(lipField);
            var lop = Field(lopField);
            var builtInDisplay = (visualItems ?? []).Any(item => item.Code == lop
                && (item.Description?.Contains("Дисплей: LED", StringComparison.Ordinal) == true
                    || item.Description?.Contains("Дисплей: LCD", StringComparison.Ordinal) == true));
            return lip == "Integrated in LOP" || (IsNone(lip) && builtInDisplay) ? lop : lip;
        }

        var options = request?.Options?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray() ?? [];
        var drawingOnly = string.Equals(specification.Status, "drawing-only", StringComparison.OrdinalIgnoreCase);
        var model = specification.Series.Replace("UN-Victior", "UN-Victor", StringComparison.Ordinal);
        var through = Field("Car Type") is "Проходная" or "Through" or "through";
        var descriptions = (optionCatalog ?? []).ToDictionary(e => e.Code, e => e.Description ?? e.Code, StringComparer.OrdinalIgnoreCase);
        descriptions.TryAdd("EFS2", "Fireman operation with safety window / Режим перевозки пожарных подразделений");
        var demands = options.Select(code => descriptions.GetValueOrDefault(code, code.Replace("_", " "))).ToList();
        if (!IsNone(Field("Cabin Design"))) demands.Add("Cabin design: " + English(Field("Cabin Design")));
        if (!IsNone(Field("Shaft Type"))) demands.Add("Hoistway structure: " + English(Field("Shaft Type")));
        if (!IsNone(Field("AC"))) demands.Add("Air conditioner: " + English(Field("AC")));
        if (!IsNone(Field("Mirror Wall"))) demands.Add("Mirror: " + string.Join(", ", new[] { Field("Mirror Wall"), Field("Mirror Height") }.Where(value => !IsNone(value)).Select(English)));
        if (Field("Custom Configuration") == "Yes") demands.Insert(0, "Non-standard configuration: dimensions and price require factory confirmation.");
        var stops = request?.Stops.ToString(CultureInfo.InvariantCulture) ?? Field("Stops");
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["projectName"] = FirstText(project?.Name, Field("Project Name")),
            ["negoNo"] = FirstText(project?.FactoryRequestNumber, Field("Contract No")),
            ["country"] = string.Join(", ", new[] { English(FirstText(Field("Country"), "Russia")), English(FirstText(Field("Address"), project?.Address)) }.Where(v => v.Length > 0)),
            ["req_header_1"] = specification.Name,
            ["liftNumbers_1"] = FirstText(Field("Lift No"), specification.Name),
            ["qty_1"] = FirstText(Field("Quantity"), "1"),
            ["type_1"] = model,
            ["capacity_1"] = request?.CapacityKg.ToString(CultureInfo.InvariantCulture) ?? "",
            ["speed_1"] = request?.Speed.ToString("0.##", CultureInfo.InvariantCulture) ?? "",
            ["floors_1"] = FirstText(Field("Floors"), stops),
            ["lobby_1"] = Field("Main Floor"),
            ["rise_1"] = decimal.TryParse(Field("Travel Height", "TR").Replace(",", "."), NumberStyles.Number, CultureInfo.InvariantCulture, out var rise) ? (rise / 1000m).ToString("0.###", CultureInfo.InvariantCulture) : "",
            ["controlSystem_1"] = Field("Control System", "Operation"),
            ["cwtLocation_1"] = Field("CWT Location"),
            ["emergencyExit_1"] = drawingOnly ? "" : options.Contains("EFS2") || request?.Efs == true ? "Yes" : "No",
            ["hoistwayLighting_1"] = "By XIZI",
            ["carInside_1"] = JoinDimensions(Field("Car Width", "AA"), Field("Car Depth", "BB")),
            ["crh_1"] = Field("Car Height", "HL"),
            ["doorOpening_1"] = JoinDimensions(FirstText(Field("Door Width", "JJ"), request?.DoorWidthMm.ToString(CultureInfo.InvariantCulture)), Field("Door Height", "HH")),
            ["doorArrangement_1"] = through ? "Through" : "Single",
            ["hoistway_1"] = JoinDimensions(Field("Shaft Width", "AH"), Field("Shaft Depth", "BH")),
            ["hoistwayDim_1"] = "Proposed",
            ["overhead_1"] = Field("Overhead", "OH"),
            ["pit_1"] = Field("Pit", "PD"),
            ["frontFloors_1"] = FirstText(Field("Front Doors"), request?.DoorCount.ToString(CultureInfo.InvariantCulture), stops),
            ["rearFloors_1"] = through ? FirstText(Field("Rear Doors", "Rear Floors"), request?.DoorCount.ToString(CultureInfo.InvariantCulture), stops) : "0",
            ["doorType_1"] = FirstText(Field("Door Opening", "Door mode", "Door type"), request?.DoorType == "CO" ? "2P-CO" : request?.DoorType == "2S" ? "2P-SO" : request?.DoorType),
            ["doorSafety_1"] = "IRC",
            ["fireDoor_1"] = Field("Fire Rating"),
            ["cabinDesign_1"] = Field("Cabin Design", "Car Design"),
            ["wallFront_1"] = Field("Car Wall Material", "Car Design Wall", "Wall"),
            ["wallSide_1"] = Field("Car Wall Material", "Car Design Wall", "Wall"),
            ["wallRear_1"] = Field("Car Wall Material", "Car Design Wall", "Wall"),
            ["carDoor_1"] = Field("Car Door Material", "Car Door"),
            ["ceiling_1"] = Field("Ceiling"),
            ["floor_1"] = FirstText(Field("Floor"), Field("Floor Pattern")),
            ["mirror_1"] = FirstText(Field("Mirror Height"), Field("Mirror")),
            ["handrail_1"] = drawingOnly && IsNone(Field("Handrail Position")) ? "" : IsNone(Field("Handrail Position")) ? "None" : Field("Handrail") + " / " + English(Field("Handrail Position")),
            ["copType_1"] = Field("COP"),
            ["copFaceplate_1"] = Field("Car Wall Material", "Wall"),
            ["copButtons_1"] = Field("COP Button"),
            ["cpiType_1"] = FirstText(Field("CPI"), options.FirstOrDefault(v => v.StartsWith("ILED_", StringComparison.Ordinal))?.Replace("ILED_", "TFT ").Replace("_", ".")),
            ["landingMain_1"] = FirstText(Field("Main Shaft Door"), Field("Main Landing Material")),
            ["landingTypical_1"] = FirstText(Field("Other Shaft Door"), Field("Other Landing Material")),
            ["hallCall_1"] = FirstText(Field("Main LOP"), Field("Other LOP")),
            ["hpiMain_1"] = Hpi("Main LIP", "Main LOP"),
            ["hpiTypical_1"] = Hpi("Other LIP", "Other LOP")
        };

        for (var index = 1; index <= 30; index++)
        {
            values[$"demand{index}_1"] = index == 30 ? string.Join("\n", demands.Skip(29)) : index <= demands.Count ? demands[index - 1] : "";
        }

        var replacements = values.ToDictionary(item => $"{{{{{item.Key}}}}}", item => item.Key.StartsWith("demand", StringComparison.Ordinal) ? item.Value : English(item.Value), StringComparer.Ordinal);
        replacements["GB7588-2003(This is equivalent to EN81-1:1998)"] = "GOST 33984.1-2016 (This is equivalent to EN 81-20:2014)";
        return replacements;
    }

    private static bool IsNone(string? value) => string.IsNullOrWhiteSpace(value) || value is "Нет" or "None" or "NONE" or "NO";

    internal static string English(string value)
    {
        var translated = value switch
        {
            "Россия" => "Russia", "Москва" or "г. Москва" => "Moscow", "Санкт-Петербург" => "Saint Petersburg",
            "Да" => "Yes", "Нет" or "NO" => "None", "Одиночная" => "Simplex", "Групповая" => "Group control",
            "Железобетон" => "Reinforced concrete", "Металлокаркас" => "Steel structure",
            "С МП" => "Machine room", "Без МП" => "Machine room-less", "Непроходная" => "Single", "Проходная" => "Through",
            "Центрального открывания" => "2P-CO", "Телескопического открывания" => "2P-SO",
            "Нерж. сталь AISI443" or "aisi-443" => "AISI443 hairline stainless steel",
            "aisi-304" => "AISI304 hairline stainless steel", "ti-gold" => "Ti-gold stainless steel",
            "Окрашенная сталь RAL9006" or "painted-steel" => "Painted steel RAL9006",
            "Задняя стена" => "Rear wall", "Левая стена" => "Left wall", "Правая стена" => "Right wall",
            "1 х Задняя стена" => "1 x rear wall", "2 х Боковые стены" => "2 x side walls", "3 х Все стены" => "3 x all walls",
            "Половина высоты" or "HALF" => "Half height", "Во всю высоту" or "FULL" => "Full height",
            "Охлаждение" => "Cooling", "Охлаждение и нагрев" => "Cooling and heating",
            _ => value
        };
        // Preserve free-text names and addresses in Latin script for the factory.
        const string letters = "абвгдеёжзийклмнопрстуфхцчшщъыьэюя";
        string[] latin = ["a","b","v","g","d","e","yo","zh","z","i","y","k","l","m","n","o","p","r","s","t","u","f","kh","ts","ch","sh","shch","","y","","e","yu","ya"];
        return string.Concat(translated.Select(c =>
        {
            var i = letters.IndexOf(char.ToLowerInvariant(c));
            if (i < 0) return c.ToString();
            var result = latin[i];
            return char.IsUpper(c) && result.Length > 0 ? char.ToUpperInvariant(result[0]) + result[1..] : result;
        }));
    }

    private static void AddChineseDescriptions(XDocument worksheet)
    {
        string[] descriptions = ["项目名称", "合同编号", "国家及城市", "参数说明", "基本信息", "数量", "型号", "载重量（公斤）", "速度（米/秒）", "层/站/门", "基站", "提升高度（米）", "控制方式", "对重位置", "安全窗", "井道照明", "尺寸", "轿厢内部宽×深（毫米）", "轿厢高度（毫米）", "开门宽×高（毫米）", "单入口/贯通", "井道宽×深（毫米）", "井道尺寸确认状态", "顶层高度（毫米）", "底坑深度（毫米）", "门系统", "前门数量", "后门数量", "开门方式", "门安全保护装置", "防火等级", "轿厢装潢", "前壁", "侧壁", "后壁", "吊顶", "地板", "扶手型号及位置", "人机界面", "操纵箱型号", "操纵箱面板", "轿内显示器", "按钮型号", "厅外召唤盒", "基站厅外显示器", "其他楼层显示器", "轿门", "基站层门", "其他楼层层门", "附加功能"];
        XNamespace ns = worksheet.Root!.Name.Namespace;
        for (var row = 2; row <= 51; row++)
        {
            var line = worksheet.Descendants(ns + "row").Single(e => e.Attribute("r")?.Value == row.ToString(CultureInfo.InvariantCulture));
            var cell = line.Elements(ns + "c").FirstOrDefault(e => e.Attribute("r")?.Value == $"B{row}");
            if (cell is null)
            {
                cell = new XElement(ns + "c", new XAttribute("r", $"B{row}"));
                var next = line.Elements(ns + "c").FirstOrDefault(e => string.CompareOrdinal(e.Attribute("r")?.Value, $"B{row}") > 0);
                if (next is null) line.Add(cell); else next.AddBeforeSelf(cell);
            }
            cell.SetAttributeValue("t", "inlineStr"); cell.SetAttributeValue("s", "0");
            cell.ReplaceNodes(new XElement(ns + "is", new XElement(ns + "t", descriptions[row - 2])));
        }
        var cols = worksheet.Root.Element(ns + "cols");
        if (cols is not null)
        {
            cols.ReplaceNodes(new[] { (1, 48), (2, 30), (3, 16), (4, 65), (5, 22) }.Select(v => new XElement(ns + "col", new XAttribute("min", v.Item1), new XAttribute("max", v.Item1), new XAttribute("width", v.Item2), new XAttribute("customWidth", 1))));
        }
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

    private static string JoinDimensions(params string[] dimensions) =>
        string.Join(" x ", dimensions.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string FirstText(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
}
