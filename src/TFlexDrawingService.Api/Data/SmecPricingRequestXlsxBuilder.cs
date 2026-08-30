using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace TFlexDrawingService.Api.Data;

internal static class SmecPricingRequestXlsxBuilder
{
    private const string ConfigurationWorksheetPath = "xl/worksheets/sheet5.xml";
    private static readonly string[] ConfigurationColumns = ["E", "G", "I", "K", "M"];

    public static byte[] Build(
        string templatePath,
        PricingSpecification specification,
        UserProject? project,
        PricingCalculationRequest? request)
    {
        using var buffer = new MemoryStream();
        buffer.Write(File.ReadAllBytes(templatePath));
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            var worksheetEntry = archive.GetEntry(ConfigurationWorksheetPath)
                ?? throw new InvalidDataException($"SMEC request template is missing {ConfigurationWorksheetPath}.");
            XDocument worksheet;
            using (var input = worksheetEntry.Open())
            {
                worksheet = XDocument.Load(input, LoadOptions.PreserveWhitespace);
            }

            ClearReferenceConfiguration(worksheet);
            FillConfiguration(worksheet, specification, project, request);

            using var output = worksheetEntry.Open();
            output.SetLength(0);
            worksheet.Save(output, SaveOptions.DisableFormatting);
        }

        return buffer.ToArray();
    }

    private static void ClearReferenceConfiguration(XDocument worksheet)
    {
        foreach (var column in ConfigurationColumns)
        {
            for (var row = 2; row <= 59; row++)
            {
                ClearCell(worksheet, $"{column}{row}");
            }
        }
    }

    private static void FillConfiguration(
        XDocument worksheet,
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

        var quantity = FirstText(Field("Quantity"), "1");
        var reserveNumber = FirstText(project?.FactoryRequestNumber, project?.Name, specification.Name);
        var floors = FirstText(Field("Floors"), request?.Stops.ToString(CultureInfo.InvariantCulture));
        var stops = request?.Stops.ToString(CultureInfo.InvariantCulture) ?? Field("Stops");

        SetCellText(worksheet, "B1", $"项目储备：{reserveNumber}");
        SetCellText(worksheet, "E2", $"{FirstText(Field("Lift No"), specification.Name)}（{quantity}台）");
        SetCellNumber(worksheet, "E3", request?.CapacityKg);
        SetCellNumber(worksheet, "E4", request?.Speed);
        SetCellNumber(worksheet, "E5", MillimetersToMeters(Field("TR", "Travel Height")));
        SetCellText(worksheet, "E6", $"{floors}层/{stops}站");
        SetCellText(worksheet, "E7", JoinLabeledDimensions(
            ("AA", Field("AA", "Car Width")),
            ("BB", Field("BB", "Car Depth")),
            ("HC", Field("HL", "Car Height"))));
        SetCellText(worksheet, "E8", JoinLabeledDimensions(
            ("JJ", Field("JJ", "Door Width")),
            ("HH", Field("HH", "Door Height"))));
        SetCellText(worksheet, "E9", JoinText(" / ",
            Field("Door type"),
            Field("Door mode", "Door Opening")));
        SetCellNumber(worksheet, "E34", ParseNumber(Field("PD", "Pit")));
        SetCellText(worksheet, "E59", BuildEngineeringRequest(specification, request, Field));
    }

    private static string BuildEngineeringRequest(
        PricingSpecification specification,
        PricingCalculationRequest? request,
        Func<string[], string> field)
    {
        var lines = new List<string>();

        AddLine(lines, "Ele Series", field(["Ele Series"]));
        AddLine(lines, "Model", specification.Series);

        var shaft = JoinLabeledDimensions(
            ("AH", field(["AH", "Shaft Width"])),
            ("BH", field(["BH", "Shaft Depth"])));
        var overhead = field(["OH", "Overhead"]);
        if (HasText(shaft) || HasText(overhead))
        {
            lines.Add(JoinText("; ",
                HasText(shaft) ? $"Shaft: {shaft}" : "",
                HasText(overhead) ? $"OH {overhead} mm" : ""));
        }

        AddLine(lines, "Operation", field(["Operation", "Control System"]));

        var floors = JoinText("; ",
            FormatField("Main floor", field(["Main Floor"])),
            FormatField("Other floors", field(["Other Floors"])));
        if (HasText(floors)) lines.Add(floors);

        var power = JoinText("; ",
            FormatField("Power", field(["Power Supply"])),
            FormatField("Lighting", field(["Lighting Supply"])));
        if (HasText(power)) lines.Add(power);

        var options = request?.Options?.Where(HasText).ToArray() ?? [];
        if (options.Length > 0) lines.Add($"Options: {string.Join(", ", options)}");

        AddLine(lines, "Other", field(["Other Requirements"]));
        return string.Join('\n', lines);
    }

    private static void AddLine(List<string> lines, string label, string value)
    {
        if (HasText(value)) lines.Add($"{label}: {value}");
    }

    private static string FormatField(string label, string value) =>
        HasText(value) ? $"{label}: {value}" : "";

    private static string JoinLabeledDimensions(params (string Label, string Value)[] dimensions) =>
        string.Join(" x ", dimensions
            .Where(item => HasText(item.Value))
            .Select(item => $"{item.Label} {item.Value}"));

    private static string JoinText(string separator, params string[] values) =>
        string.Join(separator, values.Where(HasText));

    private static decimal? MillimetersToMeters(string value)
    {
        var millimeters = ParseNumber(value);
        return millimeters is null ? null : decimal.Round(millimeters.Value / 1000m, 3);
    }

    private static decimal? ParseNumber(string value)
    {
        var normalized = value.Trim().Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    private static void SetCellNumber(XDocument worksheet, string reference, int? value) =>
        SetCellNumber(worksheet, reference, value is null ? null : (decimal?)value.Value);

    private static void SetCellNumber(XDocument worksheet, string reference, decimal? value)
    {
        if (value is null)
        {
            ClearCell(worksheet, reference);
            return;
        }

        var cell = FindCell(worksheet, reference);
        RemoveCellValue(cell);
        cell.Attribute("t")?.Remove();
        cell.Add(new XElement(cell.Name.Namespace + "v", value.Value.ToString(CultureInfo.InvariantCulture)));
    }

    private static void SetCellText(XDocument worksheet, string reference, string value)
    {
        if (!HasText(value))
        {
            ClearCell(worksheet, reference);
            return;
        }

        var cell = FindCell(worksheet, reference);
        RemoveCellValue(cell);
        cell.SetAttributeValue("t", "inlineStr");
        var text = new XElement(cell.Name.Namespace + "t", value);
        if (value != value.Trim())
        {
            text.SetAttributeValue(XNamespace.Xml + "space", "preserve");
        }
        cell.Add(new XElement(cell.Name.Namespace + "is", text));
    }

    private static void ClearCell(XDocument worksheet, string reference)
    {
        var cell = FindCell(worksheet, reference);
        RemoveCellValue(cell);
        cell.Attribute("t")?.Remove();
    }

    private static XElement FindCell(XDocument worksheet, string reference) =>
        worksheet.Descendants().Single(element =>
            element.Name.LocalName == "c" &&
            string.Equals(element.Attribute("r")?.Value, reference, StringComparison.Ordinal));

    private static void RemoveCellValue(XElement cell)
    {
        foreach (var child in cell.Elements().Where(element =>
                     element.Name.LocalName is "f" or "v" or "is").ToArray())
        {
            child.Remove();
        }
    }

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    private static string FirstText(params string?[] values) =>
        values.FirstOrDefault(HasText)?.Trim() ?? "";
}
