using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

namespace TFlexDrawingService.Api.Data;

internal static class SmecProjectExportBuilder
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace Content = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static byte[] Build(string templatePath, IReadOnlyList<PricingSpecification> specifications, UserProject? project)
    {
        if (specifications.Count == 0) throw new ArgumentException("At least one SMEC specification is required.", nameof(specifications));
        if (specifications.Any(s => !s.Supplier.Equals("SMEC", StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Only SMEC specifications can be exported.", nameof(specifications));
        using var output = new MemoryStream();
        output.Write(File.ReadAllBytes(templatePath));
        using (var archive = new ZipArchive(output, ZipArchiveMode.Update, true))
        {
            var strings = archive.GetEntry("xl/sharedStrings.xml") is { } stringsEntry ? ReadStrings(stringsEntry) : [];
            var reference = KeepLayout(InlineStrings(Read(archive, "xl/worksheets/sheet1.xml"), strings));
            var quotation = KeepLayout(InlineStrings(Read(archive, "xl/worksheets/sheet10.xml"), strings));
            foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal) || e.FullName is "xl/sharedStrings.xml" or "xl/calcChain.xml").ToArray()) entry.Delete();
            foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith("xl/printerSettings/", StringComparison.Ordinal)).ToArray()) entry.Delete();

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var exported = specifications.Select((specification, index) => Exported(specification, project, names, index)).ToArray();
            var wrapStyle = AddWrapStyle(archive);
            var printAreas = new List<string>();
            for (var index = 0; index < exported.Length; index++)
            {
                var sheet = new XDocument(reference);
                FillSheet(sheet, exported[index], wrapStyle);
                printAreas.Add((string?)sheet.Root!.Element(Main + "dimension")?.Attribute("ref") ?? "A1:I37");
                Write(archive, $"xl/worksheets/sheet{index + 1}.xml", sheet);
            }
            FillQuotation(quotation, exported, project, wrapStyle);
            var requirements = exported.Where(item => item.Request.Series != "K-II" && (Requirements(new Fields(item.Request.SpecificationFields)).Skip(6).Any() || Requirements(new Fields(item.Request.SpecificationFields)).Any(value => value.Length > 90))).ToArray();
            var sheetNames = exported.Select(e => e.SheetName).Append("Quotation").ToList();
            if (requirements.Length > 0)
            {
                sheetNames.Add("Requirements");
                Write(archive, $"xl/worksheets/sheet{exported.Length + 2}.xml", RequirementsSheet(requirements, wrapStyle));
            }
            var quotationFile = exported.Length + 1;
            Write(archive, $"xl/worksheets/sheet{quotationFile}.xml", quotation);
            WriteWorkbook(archive, sheetNames, printAreas.Append($"A1:H{exported.Length + 3}").Concat(requirements.Length > 0 ? [$"A1:B{requirements.Sum(item => Requirements(new Fields(item.Request.SpecificationFields)).Count())}"] : []).ToArray());
        }
        return output.ToArray();
    }

    private static ExportedSpecification Exported(PricingSpecification specification, UserProject? project, HashSet<string> names, int index)
    {
        var request = JsonSerializer.Deserialize<PricingCalculationRequest>(specification.RequestJson, Json);
        if (request is null) throw new InvalidDataException($"Saved SMEC request '{specification.Name}' cannot be read.");
        var calculation = string.IsNullOrWhiteSpace(specification.CalculationJson) ? null : JsonSerializer.Deserialize<PricingCalculationResult>(specification.CalculationJson, Json);
        return new(specification, SmecRequirements.Normalize(request), calculation, project, UniqueSheetName(First(specification.Name, $"SMEC {index + 1}"), names));
    }

    private static XDocument KeepLayout(XDocument document)
    {
        var root = document.Root!;
        foreach (var cell in root.Descendants(Main + "c").Where(c => Column(c.Attribute("r")?.Value) > 8 && (string?)c.Attribute("r") != "I37").ToArray()) cell.Remove();
        root.Descendants(Main + "row").Where(r => (int?)r.Attribute("r") > 40).Remove();
        root.Element(Main + "dataValidations")?.Remove();
        root.Elements(Main + "conditionalFormatting").Remove();
        root.Element(Main + "hyperlinks")?.Remove();
        root.Element(Main + "pageSetup")?.Remove();
        root.Element(Main + "legacyDrawing")?.Remove();
        root.Element(Main + "drawing")?.Remove();
        root.Element(Main + "mergeCells")?.Elements().Where(m => !WithinAtoH((string?)m.Attribute("ref"))).Remove();
        foreach (var column in root.Element(Main + "cols")?.Elements(Main + "col").ToArray() ?? [])
        {
            if ((int?)column.Attribute("min") > 9) column.Remove();
            else if ((int?)column.Attribute("max") > 9) column.SetAttributeValue("max", 9);
        }
        root.Element(Main + "dimension")?.SetAttributeValue("ref", "A1:I37");
        var pageSetup = new XElement(Main + "pageSetup", new XAttribute("paperSize", 9), new XAttribute("orientation", "portrait"), new XAttribute("fitToWidth", 1), new XAttribute("fitToHeight", 0));
        if (root.Element(Main + "headerFooter") is { } headerFooter) headerFooter.AddBeforeSelf(pageSetup);
        else if (root.Element(Main + "pageMargins") is { } pageMargins) pageMargins.AddAfterSelf(pageSetup);
        else root.Add(pageSetup);
        return document;
    }

    private static XDocument InlineStrings(XDocument document, IReadOnlyList<string> strings)
    {
        foreach (var cell in document.Descendants(Main + "c").Where(c => (string?)c.Attribute("t") == "s").ToArray())
        {
            var index = int.TryParse(cell.Element(Main + "v")?.Value, out var value) ? value : -1;
            cell.RemoveNodes(); cell.SetAttributeValue("t", "inlineStr");
            cell.Add(new XElement(Main + "is", new XElement(Main + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), index >= 0 && index < strings.Count ? strings[index] : "")));
        }
        return document;
    }

    private static void FillSheet(XDocument sheet, ExportedSpecification item, string wrapStyle)
    {
        var f = new Fields(item.Request.SpecificationFields);
        var isEscalator = item.Request.Series.Equals("K-II", StringComparison.OrdinalIgnoreCase);
        Clear(sheet, "B3", "E3", "H3", "B4", "E4", "C5", "E5", "E6", "C7", "E7", "H7", "C8", "E8", "G8", "C9", "E9", "G9", "D10", "F10", "C11", "F11", "D12", "F12", "H12", "D13", "F13", "H13", "D14", "F14", "H14", "D15", "F15", "H15", "C16", "C17", "F17", "H17", "C18", "F18", "C19", "F19", "G19", "C20", "F20", "H20", "C21", "F21", "H21", "C23", "E23", "F23", "H23", "C24", "F24", "C25", "F25", "C26", "E26", "F26", "H26", "C27", "E27", "F27", "H27", "C28", "F28", "C29", "F29", "B30", "B31", "B32", "B33", "B34", "B35", "B36", "I37");
        SetWrapped(sheet, "B3", First(item.Project is null ? "" : Join(item.Project.Name, item.Project.FactoryRequestNumber), f.Get("Project name")), wrapStyle);
        Set(sheet, "E3", f.Get("Project Type", "Project type")); Set(sheet, "H3", f.Get("ETD"));
        Set(sheet, "B4", f.Get("Country")); Set(sheet, "E4", First(item.Project?.Address, f.Get("Address")));
        Set(sheet, "C5", f.Get("Ele Series")); Set(sheet, "E5", item.Request.Series); Set(sheet, "E6", f.Get("Manufacturing Standard", "Standard"));
        SetNumber(sheet, "C7", Positive(f.Get("Quantity")) ?? 1); Set(sheet, "E7", First(f.Get("Lift No"), item.Specification.Name)); Set(sheet, "H7", f.Get("Operation", "Control System"));
        if (item.Request.CapacityKg > 0) Set(sheet, "C8", Format(item.Request.CapacityKg, "kg")); if (item.Request.Speed > 0) Set(sheet, "E8", Format(item.Request.Speed, "m/s")); Set(sheet, "G8", f.Get("Decoration Weight"));
        SetNumber(sheet, "C9", Positive(f.Get("Floors")) ?? (item.Request.Stops > 0 ? item.Request.Stops : null)); SetNumber(sheet, "E9", item.Request.Stops > 0 ? item.Request.Stops : null); SetNumber(sheet, "G9", item.Request.DoorCount > 0 ? item.Request.DoorCount : null);
        Set(sheet, "D10", f.Get("Main Floor")); Set(sheet, "F10", f.Get("Other Floors")); Set(sheet, "C11", f.Get("Power Supply")); Set(sheet, "F11", f.Get("Lighting Supply"));
        SetNumber(sheet, "D12", Number(f.Get("AH", "Shaft Width"))); SetNumber(sheet, "F12", Number(f.Get("BH", "Shaft Depth"))); Set(sheet, "H12", f.Get("Door type"));
        SetNumber(sheet, "D13", Number(f.Get("TR", "Travel Height"))); SetNumber(sheet, "F13", Number(f.Get("OH", "Overhead"))); SetNumber(sheet, "H13", Number(f.Get("PD", "Pit")));
        SetNumber(sheet, "D14", Number(f.Get("JJ", "Door Width")) ?? (item.Request.DoorWidthMm > 0 ? item.Request.DoorWidthMm : null)); Set(sheet, "F14", f.Get("Door mode", "Door Opening")); SetNumber(sheet, "H14", Number(f.Get("HH", "Door Height")));
        SetNumber(sheet, "D15", Number(f.Get("AA", "Car Width"))); SetNumber(sheet, "F15", Number(f.Get("BB", "Car Depth"))); SetNumber(sheet, "H15", Number(f.Get("HL", "Car Height")));
        Set(sheet, "C16", f.Get("Car Design")); Set(sheet, "C17", f.Get("Ceiling")); Set(sheet, "F17", f.Get("Floor Type", "Floor")); Set(sheet, "H17", f.Get("Floor Pattern")); Set(sheet, "C18", f.Get("Car Design Wall", "Wall")); Set(sheet, "F18", f.Get("Car Design Door", "Car Door"));
        Set(sheet, "C19", Join(f.Get("Mirror"), f.Get("Mirror Position"))); Set(sheet, "F19", f.Get("Handrail")); Set(sheet, "G19", f.Get("Handrail Position"));
        Set(sheet, "C20", f.Get("COP")); Set(sheet, "F20", f.Get("COP 2")); Set(sheet, "H20", f.Get("COP Button")); Set(sheet, "C21", f.Get("Wheelchair COP")); Set(sheet, "F21", f.Get("Wheelchair COP 2")); Set(sheet, "H21", f.Get("Wheelchair COP Button"));
        Set(sheet, "C23", f.Get("Main Jamb", "Jamb")); Set(sheet, "E23", f.Get("Main Landing Material", "Main Jamb Material", "Jamb Material")); Set(sheet, "F23", f.Get("Other Jamb")); Set(sheet, "H23", f.Get("Other Landing Material", "Other Jamb Material"));
        Set(sheet, "C24", f.Get("Main Sill Bracket", "Main Sill", "Sill")); Set(sheet, "F24", f.Get("Other Sill Bracket", "Other Sill")); Set(sheet, "C25", f.Get("Main Landing Door", "Main Door", "Door")); Set(sheet, "F25", f.Get("Other Landing Door", "Other Door"));
        Set(sheet, "C26", f.Get("Main LOP")); Set(sheet, "E26", f.Get("LOP Button")); Set(sheet, "F26", f.Get("Other LOP")); Set(sheet, "H26", f.Get("Other LOP Button", "LOP Button"));
        Set(sheet, "C27", f.Get("Main Auxiliary LOP", "Auxiliary LOP")); Set(sheet, "E27", f.Get("Auxiliary LOP Button")); Set(sheet, "F27", f.Get("Other Auxiliary LOP", "Auxiliary LOP 2")); Set(sheet, "H27", f.Get("Other Auxiliary LOP Button", "Auxiliary LOP 2 Button")); Set(sheet, "C28", f.Get("Hall Indicator", "Main Hall Indicator")); Set(sheet, "F28", f.Get("Other Hall Indicator", "Hall Indicator 2")); Set(sheet, "C29", f.Get("Hall Lantern", "Main Hall Lantern")); Set(sheet, "F29", f.Get("Other Hall Lantern", "Hall Lantern 2"));
        Set(sheet, "B30", string.Join(", ", item.Request.Options ?? []));
        var requirements = Requirements(f).ToArray();
        for (var index = 0; index < Math.Min(6, requirements.Length); index++)
        {
            var row = 31 + index;
            SetWrapped(sheet, $"B{row}", requirements[index], wrapStyle);
            var requirementRow = sheet.Descendants(Main + "row").Single(value => (string?)value.Attribute("r") == row.ToString(CultureInfo.InvariantCulture));
            requirementRow.SetAttributeValue("ht", Math.Max(18, Math.Min(90, 18 * (int)Math.Ceiling(requirements[index].Length / 45d)))); requirementRow.SetAttributeValue("customHeight", 1);
        }
        if (item.Calculation is not null) SetNumber(sheet, "I37", item.Calculation.TotalCny);
        sheet.Descendants(Main + "row").Single(row => (string?)row.Attribute("r") == "3").SetAttributeValue("ht", 30);
        if (isEscalator)
        {
            Set(sheet, "A1", "ESCALATOR SPECIFICATION");
            var data = sheet.Root!.Element(Main + "sheetData")!;
            data.Elements(Main + "row").Where(row => (int)row.Attribute("r")! >= 8).Remove();
            var merges = sheet.Root.Element(Main + "mergeCells")!;
            merges.Elements().Where(merge => ((string)merge.Attribute("ref")!).Split(':').Any(cell => int.Parse(new string(cell.Where(char.IsDigit).ToArray()), CultureInfo.InvariantCulture) >= 8)).Remove();
            for (var index = 0; index < requirements.Length; index++)
            {
                var row = index + 9;
                data.Add(new XElement(Main + "row", new XAttribute("r", row), new XAttribute("ht", Math.Max(24, 18 * Math.Ceiling(requirements[index].Length / 95d))), new XAttribute("customHeight", 1), Cell($"A{row}", requirements[index], wrapStyle)));
                merges.Add(new XElement(Main + "mergeCell", new XAttribute("ref", $"A{row}:H{row}")));
            }
            merges.SetAttributeValue("count", merges.Elements().Count());
            sheet.Root.Element(Main + "dimension")!.SetAttributeValue("ref", $"A1:H{requirements.Length + 8}");
        }
    }

    private static void FillQuotation(XDocument quotation, IReadOnlyList<ExportedSpecification> items, UserProject? project, string wrapStyle)
    {
        var styles = quotation.Descendants(Main + "c").ToDictionary(c => (string)c.Attribute("r")!, c => (string?)c.Attribute("s") ?? "0");
        quotation.Root!.Element(Main + "sheetData")!.RemoveNodes();
        var data = quotation.Root.Element(Main + "sheetData")!;
        var rows = new List<object?[]> { new object?[] { "Project name", "Unit #", "Capacity", "Speed", "Quantity", "Model", "FOB CNY Price / Unit", "Containers 40GP / Unit" } };
        foreach (var item in items) rows.Add([project?.Name ?? "", First(item.Request.SpecificationFields is null ? "" : new Fields(item.Request.SpecificationFields).Get("Lift No"), item.Specification.Name), item.Request.CapacityKg > 0 ? item.Request.CapacityKg : null, item.Request.Speed > 0 ? item.Request.Speed : null, Positive(new Fields(item.Request.SpecificationFields).Get("Quantity")) ?? 1, item.Request.Series, item.Calculation?.TotalCny, null]);
        var priced = items.Where(i => i.Calculation is not null).ToArray();
        rows.Add(["Total", null, null, null, items.Sum(i => Positive(new Fields(i.Request.SpecificationFields).Get("Quantity")) ?? 1), null, priced.Length == items.Count ? items.Sum(i => i.Calculation!.TotalCny * (Positive(new Fields(i.Request.SpecificationFields).Get("Quantity")) ?? 1)) : null, null]);
        rows.Add(["Preliminary SMEC calculation in CNY. Factory confirmation required.", null, null, null, null, null, null, null]);
        for (var r = 0; r < rows.Count; r++)
        {
            var row = new XElement(Main + "row", new XAttribute("r", r + 1));
            for (var c = 0; c < 8; c++) { var address = $"{(char)('A' + c)}{r + 1}"; row.Add(Cell(address, rows[r][c], r > 0 && c is 0 or 1 ? wrapStyle : styles.GetValueOrDefault($"{(char)('A' + c)}{Math.Min(r + 1, 11)}", "0"))); }
            if (r > 0 && r < rows.Count - 1)
            {
                row.SetAttributeValue("ht", Math.Max(30, 18 * Math.Ceiling(Math.Max(rows[r][0]?.ToString()?.Length ?? 0, rows[r][1]?.ToString()?.Length ?? 0) / 10d)));
                row.SetAttributeValue("customHeight", 1);
            }
            data.Add(row);
        }
        var noteRow = data.Elements(Main + "row").Last();
        noteRow.SetAttributeValue("ht", 30); noteRow.SetAttributeValue("customHeight", 1);
        noteRow.Element(Main + "c")!.SetAttributeValue("s", wrapStyle);
        data.AddAfterSelf(new XElement(Main + "mergeCells", new XAttribute("count", 1), new XElement(Main + "mergeCell", new XAttribute("ref", $"A{rows.Count}:H{rows.Count}"))));
        quotation.Root.Element(Main + "dimension")?.SetAttributeValue("ref", $"A1:H{rows.Count}");
    }

    private static XDocument RequirementsSheet(IReadOnlyList<ExportedSpecification> items, string wrapStyle)
    {
        var data = new XElement(Main + "sheetData"); var row = 1;
        foreach (var item in items) foreach (var requirement in Requirements(new Fields(item.Request.SpecificationFields)))
        {
            data.Add(new XElement(Main + "row", new XAttribute("r", row), new XAttribute("ht", Math.Max(18, Math.Min(90, 18 * (int)Math.Ceiling(requirement.Length / 90d)))), new XAttribute("customHeight", 1), Cell($"A{row}", item.SheetName, wrapStyle), Cell($"B{row}", requirement, wrapStyle))); row++;
        }
        return new XDocument(new XElement(Main + "worksheet", new XElement(Main + "cols", new XElement(Main + "col", new XAttribute("min", 1), new XAttribute("max", 1), new XAttribute("width", 28), new XAttribute("customWidth", 1)), new XElement(Main + "col", new XAttribute("min", 2), new XAttribute("max", 2), new XAttribute("width", 90), new XAttribute("customWidth", 1))), data));
    }

    private static IEnumerable<string> Requirements(Fields fields) => SmecRequirements.Fields.Select(key => (Key: key, Value: fields.Get(key))).Where(x => !string.IsNullOrWhiteSpace(x.Value)).Select(x => $"{x.Key}: {x.Value}").Append(fields.Get("Other Requirements"))
        .SelectMany(value => value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        .SelectMany(line => Enumerable.Range(0, (line.Length + 449) / 450).Select(index => line.Substring(index * 450, Math.Min(450, line.Length - index * 450))));
    private static void WriteWorkbook(ZipArchive archive, IReadOnlyList<string> names, IReadOnlyList<string> printAreas)
    {
        var sheets = new XElement(Main + "sheets", names.Select((name, i) => new XElement(Main + "sheet", new XAttribute("name", name), new XAttribute("sheetId", i + 1), new XAttribute(Rel + "id", $"rId{i + 1}"))));
        var definitions = new XElement(Main + "definedNames", names.Select((name, i) => new XElement(Main + "definedName", new XAttribute("name", "_xlnm.Print_Area"), new XAttribute("localSheetId", i), $"'{name.Replace("'", "''")}'!{AbsoluteRange(printAreas[i])}")));
        var workbook = new XDocument(new XElement(Main + "workbook", sheets, definitions));
        var rels = new XDocument(new XElement(PackageRel + "Relationships", names.Select((_, i) => new XElement(PackageRel + "Relationship", new XAttribute("Id", $"rId{i + 1}"), new XAttribute("Type", Rel.NamespaceName + "/worksheet"), new XAttribute("Target", $"worksheets/sheet{i + 1}.xml"))).Append(new XElement(PackageRel + "Relationship", new XAttribute("Id", $"rId{names.Count + 1}"), new XAttribute("Type", Rel.NamespaceName + "/styles"), new XAttribute("Target", "styles.xml")))));
        var content = Read(archive, "[Content_Types].xml"); content.Root!.Elements(Content + "Override").Where(e => ((string?)e.Attribute("PartName"))?.StartsWith("/xl/worksheets/", StringComparison.Ordinal) == true || (string?)e.Attribute("PartName") is "/xl/calcChain.xml" or "/xl/sharedStrings.xml").Remove();
        foreach (var i in Enumerable.Range(1, names.Count)) content.Root.Add(new XElement(Content + "Override", new XAttribute("PartName", $"/xl/worksheets/sheet{i}.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
        Write(archive, "xl/workbook.xml", workbook); Write(archive, "xl/_rels/workbook.xml.rels", rels); Write(archive, "[Content_Types].xml", content);
    }

    private static string AbsoluteRange(string range) => string.Join(":", range.Split(':').Select(cell => "$" + new string(cell.TakeWhile(char.IsLetter).ToArray()) + "$" + new string(cell.SkipWhile(char.IsLetter).ToArray())));

    private static string AddWrapStyle(ZipArchive archive)
    {
        var styles = Read(archive, "xl/styles.xml"); var formats = styles.Root!.Element(Main + "cellXfs")!; var id = formats.Elements(Main + "xf").Count();
        formats.Add(new XElement(Main + "xf", new XAttribute("fontId", 0), new XAttribute("fillId", 0), new XAttribute("borderId", 0), new XAttribute("xfId", 0), new XAttribute("applyAlignment", 1), new XElement(Main + "alignment", new XAttribute("wrapText", 1), new XAttribute("vertical", "top"))));
        formats.SetAttributeValue("count", id + 1); Write(archive, "xl/styles.xml", styles); return id.ToString(CultureInfo.InvariantCulture);
    }

    private static void Clear(XDocument sheet, params string[] refs) { foreach (var reference in refs) Set(sheet, reference, null); }
    private static void Set(XDocument sheet, string reference, string? value) { var cell = Find(sheet, reference); cell.RemoveNodes(); cell.Attribute("t")?.Remove(); if (!string.IsNullOrWhiteSpace(value)) { cell.SetAttributeValue("t", "inlineStr"); cell.Add(new XElement(Main + "is", new XElement(Main + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value))); } }
    private static void SetWrapped(XDocument sheet, string reference, string? value, string style) { Set(sheet, reference, value); Find(sheet, reference).SetAttributeValue("s", style); }
    private static void SetNumber(XDocument sheet, string reference, decimal? value) { var cell = Find(sheet, reference); cell.RemoveNodes(); cell.Attribute("t")?.Remove(); if (value is not null) cell.Add(new XElement(Main + "v", value.Value.ToString(CultureInfo.InvariantCulture))); }
    private static XElement Find(XDocument sheet, string reference) => sheet.Descendants(Main + "c").Single(c => (string?)c.Attribute("r") == reference);
    private static XElement Cell(string reference, object? value, string style) => new(Main + "c", new XAttribute("r", reference), new XAttribute("s", style), value is decimal or int ? new XElement(Main + "v", Convert.ToString(value, CultureInfo.InvariantCulture)) : new object[] { new XAttribute("t", "inlineStr"), new XElement(Main + "is", new XElement(Main + "t", value?.ToString() ?? "")) });
    private static int Column(string? reference) => reference is null ? 0 : reference.TakeWhile(char.IsLetter).Aggregate(0, (value, letter) => value * 26 + letter - 'A' + 1);
    private static bool WithinAtoH(string? reference) => reference?.Split(':').All(part => Column(part) <= 8) == true;
    private static decimal? Number(string text) => decimal.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : null;
    private static decimal? Positive(string text) => Number(text) is { } value && value > 0 ? value : null;
    private static string Format(decimal value, string unit) => $"{value.ToString(CultureInfo.InvariantCulture)}{unit}";
    private static string Join(params string[] values) => string.Join("; ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
    private static string First(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    private static string UniqueSheetName(string name, HashSet<string> names) { var stem = new string(name.Where(c => !"[]:*?/\\".Contains(c)).ToArray()).Trim().Trim('\''); if (stem.Length == 0 || stem.Equals("Quotation", StringComparison.OrdinalIgnoreCase) || stem.Equals("Requirements", StringComparison.OrdinalIgnoreCase)) stem = "SMEC"; for (var suffix = 1; ; suffix++) { var tail = suffix == 1 ? "" : $" ({suffix})"; var candidate = stem[..Math.Min(stem.Length, 31 - tail.Length)] + tail; if (names.Add(candidate)) return candidate; } }
    private static XDocument Read(ZipArchive archive, string path) { using var stream = archive.GetEntry(path)!.Open(); return XDocument.Load(stream); }
    private static IReadOnlyList<string> ReadStrings(ZipArchiveEntry entry) { using var stream = entry.Open(); return XDocument.Load(stream).Descendants(Main + "si").Select(s => string.Concat(s.DescendantNodes().OfType<XText>().Select(t => t.Value))).ToArray(); }
    private static void Write(ZipArchive archive, string path, XDocument document) { archive.GetEntry(path)?.Delete(); using var stream = archive.CreateEntry(path).Open(); document.Save(stream); }
    private sealed record ExportedSpecification(PricingSpecification Specification, PricingCalculationRequest Request, PricingCalculationResult? Calculation, UserProject? Project, string SheetName);
    private sealed class Fields(IReadOnlyDictionary<string, string>? values) { public string Get(params string[] names) { if (values is null) return ""; foreach (var name in names) foreach (var pair in values) if (string.Equals(pair.Key.Trim(), name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(pair.Value)) return pair.Value.Trim(); return ""; } }
}
