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

            using (var output = worksheetEntry.Open())
            {
                output.SetLength(0);
                worksheet.Save(output, SaveOptions.DisableFormatting);
            }
            AddSelectedRequirementsSheet(archive, specification, request);
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

    private static void AddSelectedRequirementsSheet(
        ZipArchive archive, PricingSpecification specification, PricingCalculationRequest? request)
    {
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace documentRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace content = "http://schemas.openxmlformats.org/package/2006/content-types";
        XDocument Read(string path)
        {
            using var input = archive.GetEntry(path)!.Open();
            return XDocument.Load(input);
        }
        void Write(string path, XDocument document)
        {
            var entry = archive.GetEntry(path) ?? archive.CreateEntry(path);
            using var output = entry.Open();
            output.SetLength(0);
            document.Save(output);
        }

        var workbook = Read("xl/workbook.xml");
        var relationships = Read("xl/_rels/workbook.xml.rels");
        var contentTypes = Read("[Content_Types].xml");
        var styles = Read("xl/styles.xml");
        var formats = styles.Root!.Element(main + "cellXfs")!;
        var styleId = formats.Elements().Count();
        formats.Add(new XElement(main + "xf", new XAttribute("fontId", 0), new XAttribute("fillId", 0),
            new XAttribute("borderId", 0), new XAttribute("numFmtId", 0), new XAttribute("applyAlignment", 1),
            new XElement(main + "alignment", new XAttribute("wrapText", 1), new XAttribute("vertical", "top"))));
        formats.SetAttributeValue("count", styleId + 1);

        var sheets = workbook.Root!.Element(main + "sheets")!;
        var sheetId = sheets.Elements().Max(sheet => (int?)sheet.Attribute("sheetId") ?? 0) + 1;
        var fileNumber = sheetId;
        while (archive.GetEntry($"xl/worksheets/sheet{fileNumber}.xml") is not null) fileNumber++;
        var relationshipId = "rIdSmecRequirements";
        while (relationships.Root!.Elements().Any(element => (string?)element.Attribute("Id") == relationshipId)) relationshipId += "1";
        sheets.Add(new XElement(main + "sheet", new XAttribute("name", "Selected requirements"),
            new XAttribute("sheetId", sheetId), new XAttribute(documentRel + "id", relationshipId)));
        relationships.Root!.Add(new XElement(rel + "Relationship", new XAttribute("Id", relationshipId),
            new XAttribute("Type", documentRel.NamespaceName + "/worksheet"), new XAttribute("Target", $"worksheets/sheet{fileNumber}.xml")));
        contentTypes.Root!.Add(new XElement(content + "Override", new XAttribute("PartName", $"/xl/worksheets/sheet{fileNumber}.xml"),
            new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));

        var values = new List<(string Key, string Value)> { ("Specification", specification.Name), ("Model", specification.Series) };
        values.AddRange((request?.SpecificationFields ?? new Dictionary<string, string>())
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value)).Select(pair => (pair.Key, pair.Value)));
        values.AddRange((request?.Options ?? []).Distinct(StringComparer.OrdinalIgnoreCase).Select(option => ("Function", option)));
        var data = new XElement(main + "sheetData");
        for (var index = 0; index < values.Count; index++)
        {
            var (key, value) = values[index];
            var rowNumber = index + 1;
            XElement Cell(string column, string text) => new(main + "c", new XAttribute("r", $"{column}{rowNumber}"),
                new XAttribute("t", "inlineStr"), new XAttribute("s", styleId),
                new XElement(main + "is", new XElement(main + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text)));
            var wrappedLines = value.Split('\n').Sum(line => Math.Max(1, (int)Math.Ceiling(line.Length / 60d)));
            data.Add(new XElement(main + "row", new XAttribute("r", rowNumber),
                new XAttribute("ht", Math.Min(409, Math.Max(30, wrappedLines * 15 + 10))), new XAttribute("customHeight", 1),
                Cell("A", key), Cell("B", value)));
        }
        var worksheet = new XDocument(new XElement(main + "worksheet",
            new XElement(main + "cols",
                new XElement(main + "col", new XAttribute("min", 1), new XAttribute("max", 1), new XAttribute("width", 32), new XAttribute("customWidth", 1)),
                new XElement(main + "col", new XAttribute("min", 2), new XAttribute("max", 2), new XAttribute("width", 65), new XAttribute("customWidth", 1))),
            data,
            new XElement(main + "pageMargins", new XAttribute("left", .3), new XAttribute("right", .3),
                new XAttribute("top", .4), new XAttribute("bottom", .4), new XAttribute("header", .2), new XAttribute("footer", .2)),
            new XElement(main + "pageSetup", new XAttribute("orientation", "portrait"), new XAttribute("paperSize", 9),
                new XAttribute("fitToWidth", 1), new XAttribute("fitToHeight", 0))));
        Write($"xl/worksheets/sheet{fileNumber}.xml", worksheet);
        Write("xl/workbook.xml", workbook);
        Write("xl/_rels/workbook.xml.rels", relationships);
        Write("[Content_Types].xml", contentTypes);
        Write("xl/styles.xml", styles);
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

        foreach (var key in SmecRequirements.Fields) AddLine(lines, key, field([key]));
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
