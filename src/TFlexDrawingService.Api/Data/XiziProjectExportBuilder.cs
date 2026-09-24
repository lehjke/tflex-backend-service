using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

namespace TFlexDrawingService.Api.Data;

internal static class XiziProjectExportBuilder
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace Content = "http://schemas.openxmlformats.org/package/2006/content-types";

    public static byte[] Build(PricingCatalogStore store, IReadOnlyList<PricingSpecification> specifications, UserProject? project)
    {
        if (specifications.Count == 0) throw new ArgumentException("At least one XIZI specification is required.", nameof(specifications));
        var requests = specifications.Select(s => store.BuildPricingRequestXlsx(s, project)).ToArray();
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            Write(zip, "XIZI-request.xlsx", CombineRequests(requests));
            Write(zip, "XIZI-prices.xlsx", BuildPrices(specifications, store.Catalog.Xizi));
        }
        return output.ToArray();
    }

    private static byte[] CombineRequests(IReadOnlyList<byte[]> requests)
    {
        using var output = new MemoryStream();
        output.Write(requests[0]);
        using (var zip = new ZipArchive(output, ZipArchiveMode.Update, true))
        {
            var sheet = ReadXml(zip, "xl/worksheets/sheet1.xml");
            var data = sheet.Root!.Element(Main + "sheetData")!;
            var columns = sheet.Root.Element(Main + "cols")!;
            var originalColumns = columns.Elements().Select(e => new XElement(e)).ToArray();
            for (var i = 0; i < requests.Count; i++)
            {
                using var source = new ZipArchive(new MemoryStream(requests[i]), ZipArchiveMode.Read);
                var sourceSheet = ReadXml(source, "xl/worksheets/sheet1.xml");
                var strings = ReadXml(source, "xl/sharedStrings.xml").Root!.Elements(Main + "si").ToArray();
                foreach (var cell in sourceSheet.Descendants(Main + "c").Where(c => c.Attribute("t")?.Value == "s"))
                {
                    var index = int.Parse(cell.Element(Main + "v")!.Value, CultureInfo.InvariantCulture);
                    cell.SetAttributeValue("t", "inlineStr");
                    cell.ReplaceNodes(new XElement(Main + "is", strings[index].Elements().Select(e => new XElement(e))));
                }
                if (i == 0)
                {
                    data.ReplaceNodes(sourceSheet.Root!.Element(Main + "sheetData")!.Elements().Select(e => new XElement(e)));
                    continue;
                }
                foreach (var row in sourceSheet.Descendants(Main + "row").Where(row => (int)row.Attribute("r")! >= 5))
                {
                    var target = data.Elements().Single(r => (string?)r.Attribute("r") == (string?)row.Attribute("r"));
                    if (row.Attribute("ht") is not null)
                        target.SetAttributeValue("ht", Math.Max((double?)target.Attribute("ht") ?? 15, (double)row.Attribute("ht")!));
                    foreach (var cell in row.Elements(Main + "c").Where(c => c.Attribute("r")!.Value[0] is 'D' or 'E'))
                    {
                        var copy = new XElement(cell);
                        var address = cell.Attribute("r")!.Value;
                        copy.SetAttributeValue("r", Column(address[0] - 'A' + 1 + 2 * i) + address[1..]);
                        target.Add(copy);
                    }
                }
                foreach (var column in new[] { 4, 5 })
                {
                    var definition = originalColumns.First(c => (int)c.Attribute("min")! <= column && (int)c.Attribute("max")! >= column);
                    var copy = new XElement(definition);
                    copy.SetAttributeValue("min", column + 2 * i);
                    copy.SetAttributeValue("max", column + 2 * i);
                    columns.Add(copy);
                }
            }
            var lastColumn = Column(3 + 2 * requests.Count);
            sheet.Root.Element(Main + "dimension")?.SetAttributeValue("ref", $"A1:{lastColumn}83");
            foreach (var row in data.Elements()) row.Attribute("spans")?.Remove();
            var properties = sheet.Root.Element(Main + "sheetPr");
            if (properties is null) { properties = new XElement(Main + "sheetPr"); sheet.Root.AddFirst(properties); }
            properties.SetElementValue(Main + "pageSetUpPr", null);
            properties.Add(new XElement(Main + "pageSetUpPr", new XAttribute("fitToPage", 1)));
            sheet.Root.Add(new XElement(Main + "pageSetup", new XAttribute("orientation", "landscape"),
                new XAttribute("fitToWidth", 1), new XAttribute("fitToHeight", 0)));
            var workbook = ReadXml(zip, "xl/workbook.xml");
            workbook.Root!.Element(Main + "sheets")!.Elements().First().SetAttributeValue("name", "XIZI request");
            var names = workbook.Root.Element(Main + "definedNames");
            if (names is null) { names = new XElement(Main + "definedNames"); workbook.Root.Element(Main + "sheets")!.AddAfterSelf(names); }
            names.Elements().Where(e => (string?)e.Attribute("name") == "_xlnm.Print_Area").Remove();
            names.Add(new XElement(Main + "definedName", new XAttribute("name", "_xlnm.Print_Area"),
                new XAttribute("localSheetId", 0), $"'XIZI request'!$A$1:${lastColumn}$83"));
            WriteXml(zip, "xl/worksheets/sheet1.xml", sheet);
            WriteXml(zip, "xl/workbook.xml", workbook);
        }
        return output.ToArray();
    }

    private static string Column(int number)
    {
        var result = "";
        for (; number > 0; number = (number - 1) / 26) result = (char)('A' + (number - 1) % 26) + result;
        return result;
    }

    private static byte[] BuildPrices(IReadOnlyList<PricingSpecification> specifications, XiziCatalog catalog)
    {
        var rows = new List<object?[]> { new object?[] { "Lift", "Model", "Capacity, kg", "Speed, m/s", "Stops", "Quantity", "Containers per lift", "Containers total", "Unit price FOB, CNY", "Total FOB, CNY", "Container type" } };
        var details = new List<object?[]> { new object?[] { "Lift", "Item", "Quantity per lift", "Unit price, CNY", "Amount per lift, CNY", "Status" } };
        decimal total = 0;
        var quantities = 0;
        var containers = new Dictionary<string, decimal>();
        var missingContainers = false;
        foreach (var s in specifications)
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var request = JsonSerializer.Deserialize<PricingCalculationRequest>(s.RequestJson, options)!;
            var calculation = string.Equals(s.CalculationJson.Trim(), "null", StringComparison.OrdinalIgnoreCase)
                ? null
                : JsonSerializer.Deserialize<PricingCalculationResult>(s.CalculationJson, options);
            var rawQuantity = request.SpecificationFields?.GetValueOrDefault("Quantity");
            var quantity = int.TryParse(rawQuantity, out var count) && count > 0 ? count : 1;
            var name = PricingRequestXlsxBuilder.English(s.Name);
            var container = catalog.Containers.FirstOrDefault(c => c.Capacity == request.CapacityKg && c.Stops == request.Stops);
            decimal? fraction = container?.Fraction is { ValueKind: JsonValueKind.Number } number && number.TryGetDecimal(out var allocation) && allocation >= 0 ? allocation : null;
            if (fraction is not null) containers[container!.Container] = containers.GetValueOrDefault(container.Container) + fraction.Value * quantity;
            else missingContainers = true;
            quantities += quantity;
            rows.Add([name, s.Series, request.CapacityKg, request.Speed, request.Stops, quantity,
                fraction is null ? "Requires confirmation" : fraction, fraction is null ? "Requires confirmation" : fraction * quantity,
                calculation?.TotalCny, calculation is null ? null : calculation.TotalCny * quantity, container?.Container]);
            if (calculation is not null) total += calculation.TotalCny * quantity;
            foreach (var line in calculation?.Lines ?? [])
                details.Add([name, PriceLabel(line, catalog), line.Quantity, line.UnitPriceCny, line.AmountCny, line.Status]);
        }
        rows.Add(["PROJECT TOTAL", null, null, null, null, quantities, null,
            missingContainers ? "Requires confirmation" : containers.Values.Sum(), null, specifications.Any(s => string.Equals(s.CalculationJson.Trim(), "null", StringComparison.OrdinalIgnoreCase)) ? "Incomplete: drawing-only units require pricing confirmation." : total]);
        foreach (var (type, allocation) in containers) rows.Add([$"TOTAL {type}", null, null, null, null, null, null, allocation]);
        rows.Add(["Container allocations are multiplied by lift quantity without rounding. Final shipment consolidation requires confirmation."]);
        rows.Add(["Preliminary XIZI calculation. Currency: CNY."]);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            WriteXml(zip, "[Content_Types].xml", new XDocument(new XElement(Content + "Types",
                new XElement(Content + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(Content + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                Override("/xl/workbook.xml", "sheet.main"), Override("/xl/styles.xml", "styles"), Override("/xl/worksheets/sheet1.xml", "worksheet"), Override("/xl/worksheets/sheet2.xml", "worksheet"))));
            WriteXml(zip, "_rels/.rels", new XDocument(new XElement(PackageRel + "Relationships", Relationship("r1", "officeDocument", "xl/workbook.xml"))));
            WriteXml(zip, "xl/workbook.xml", new XDocument(new XElement(Main + "workbook", new XElement(Main + "sheets",
                new XElement(Main + "sheet", new XAttribute("name", "Prices"), new XAttribute("sheetId", 1), new XAttribute(Rel + "id", "r1")),
                new XElement(Main + "sheet", new XAttribute("name", "Details"), new XAttribute("sheetId", 2), new XAttribute(Rel + "id", "r2"))))));
            WriteXml(zip, "xl/_rels/workbook.xml.rels", new XDocument(new XElement(PackageRel + "Relationships",
                Relationship("r1", "worksheet", "worksheets/sheet1.xml"), Relationship("r2", "worksheet", "worksheets/sheet2.xml"), Relationship("r3", "styles", "styles.xml"))));
            WriteXml(zip, "xl/styles.xml", XDocument.Parse($"""
                <styleSheet xmlns="{Main}"><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border/></borders><cellStyleXfs count="1"><xf/></cellStyleXfs><cellXfs count="3"><xf fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf><xf fontId="1" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment wrapText="1"/></xf><xf numFmtId="4" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>
                """));
            var summary = Sheet(rows, [18, 24, 16, 16, 12, 12, 20, 20, 22, 22, 18]);
            summary.Root!.Add(new XElement(Main + "mergeCells", new XAttribute("count", 2),
                new XElement(Main + "mergeCell", new XAttribute("ref", $"A{rows.Count - 1}:K{rows.Count - 1}")),
                new XElement(Main + "mergeCell", new XAttribute("ref", $"A{rows.Count}:K{rows.Count}"))));
            WriteXml(zip, "xl/worksheets/sheet1.xml", summary);
            WriteXml(zip, "xl/worksheets/sheet2.xml", Sheet(details, [24, 75, 20, 22, 24, 18]));
        }
        return output.ToArray();
    }

    private static string PriceLabel(PricingLine line, XiziCatalog catalog)
    {
        var entry = catalog.Options.Concat(catalog.LocalRequirements).FirstOrDefault(e => line.Label == "Опция " + e.Code || line.Label == "LMR: " + e.Code);
        if (!string.IsNullOrWhiteSpace(entry?.Description)) return entry.Description.Split('/')[0].Trim();
        return line.Label.Replace("Базовая цена", "Base price").Replace("Превышение расчетной высоты", "Extra rise")
            .Replace("Дверь кабины", "Car door").Replace("Вторая дверь проходной кабины", "Second car door")
            .Replace("Дверь шахты, основной этаж", "Landing door, main floor").Replace("Двери шахты, остальные этажи", "Landing doors, other floors")
            .Replace("Стены кабины", "Car walls").Replace("Дизайн кабины", "Cabin design").Replace("Потолок", "Ceiling").Replace("Пол ", "Floor ")
            .Replace("Зеркало", "Mirror").Replace("Поручень", "Handrail").Replace("Кнопки COP", "COP buttons")
            .Replace("основной этаж", "main floor").Replace("остальные этажи", "other floors").Replace("Опция ", "Option ")
            .Replace("Кондиционер", "Air conditioner").Replace(" м", " m");
    }

    private static XDocument Sheet(IReadOnlyList<object?[]> rows, int[] widths) => new(new XElement(Main + "worksheet",
        new XElement(Main + "sheetViews", new XElement(Main + "sheetView", new XAttribute("workbookViewId", 0), new XElement(Main + "pane", new XAttribute("ySplit", 1), new XAttribute("topLeftCell", "A2"), new XAttribute("state", "frozen")))),
        new XElement(Main + "cols", widths.Select((width, i) => new XElement(Main + "col", new XAttribute("min", i + 1), new XAttribute("max", i + 1), new XAttribute("width", width), new XAttribute("customWidth", 1)))),
        new XElement(Main + "sheetData", rows.Select((row, r) => new XElement(Main + "row", new XAttribute("r", r + 1), new XAttribute("ht", r == 0 ? 32 : 42), new XAttribute("customHeight", 1), row.Select((value, c) => Cell($"{(char)('A' + c)}{r + 1}", value, r == 0)))))));

    private static XElement Cell(string address, object? value, bool header)
    {
        var number = value is decimal or int;
        return new XElement(Main + "c", new XAttribute("r", address), new XAttribute("s", header ? 1 : value is decimal ? 2 : 0),
            number ? new XElement(Main + "v", Convert.ToString(value, CultureInfo.InvariantCulture)) : new object[] { new XAttribute("t", "inlineStr"), new XElement(Main + "is", new XElement(Main + "t", value?.ToString() ?? "")) });
    }
    private static XElement Override(string path, string type) => new(Content + "Override", new XAttribute("PartName", path), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml." + type + "+xml"));
    private static XElement Relationship(string id, string type, string target) => new(PackageRel + "Relationship", new XAttribute("Id", id), new XAttribute("Type", Rel.NamespaceName + "/" + type), new XAttribute("Target", target));
    private static XDocument ReadXml(ZipArchive zip, string name) { using var stream = zip.GetEntry(name)!.Open(); return XDocument.Load(stream); }
    private static void WriteXml(ZipArchive zip, string name, XDocument document) { if (zip.Mode != ZipArchiveMode.Create) zip.GetEntry(name)?.Delete(); using var stream = zip.CreateEntry(name).Open(); document.Save(stream); }
    private static void Write(ZipArchive zip, string name, byte[] bytes) { using var stream = zip.CreateEntry(name).Open(); stream.Write(bytes); }
}
