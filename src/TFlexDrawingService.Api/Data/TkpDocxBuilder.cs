using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Xml.Linq;

namespace TFlexDrawingService.Api.Data;

internal static class TkpDocxBuilder
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    public static byte[] Build(
        string templatePath,
        string assetsRoot,
        PricingCatalog catalog,
        PricingSpecification specification,
        UserProject? project,
        PricingCalculationRequest? request,
        PricingCalculationResult? calculation)
    {
        var model = new TkpModel(specification, project, request, calculation, catalog);
        var bytes = File.ReadAllBytes(templatePath);
        using var buffer = new MemoryStream();
        buffer.Write(bytes);
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            var images = new ImageRegistry(assetsRoot, model.Supplier);
            var replacements = BuildTemplateReplacements(model);
            foreach (var entry in archive.Entries.Where(item =>
                         item.FullName.StartsWith("word/", StringComparison.Ordinal)
                         && item.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                XDocument document;
                using (var input = entry.Open())
                {
                    document = XDocument.Load(input, LoadOptions.PreserveWhitespace);
                }

                ReplaceParagraphText(document, replacements);
                if (entry.FullName == "word/document.xml")
                {
                    PopulateProposalMetadata(document, model);
                    PopulateCommercialTables(document, model);
                    PopulateTechnicalSpecification(document, model, images);
                }
                else if (entry.FullName == "word/settings.xml")
                {
                    EnableFieldUpdates(document);
                }

                using var output = entry.Open();
                output.SetLength(0);
                document.Save(output, SaveOptions.DisableFormatting);
            }

            images.WriteTo(archive);
            AddImageRelationships(archive, images);
            EnsurePngContentType(archive, images.Count > 0);
        }

        return buffer.ToArray();
    }

    private static IReadOnlyDictionary<string, string> BuildTemplateReplacements(TkpModel model)
    {
        var passengers = model.CapacityKg > 0 ? Math.Max(1, model.CapacityKg / 75) : 0;
        var options = string.Join("; ", model.Options);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{#optionRows} {col1}; {col2}; {col3}; {/optionRows}"] = options,
            ["{managerName}"] = "",
            ["{managerPosition}"] = "",
            ["{managerPhone}"] = "",
            ["{managerEmail}"] = "",
            ["{kpNumber}"] = model.Number,
            ["{kpDate}"] = model.Date,
            ["{supplierName}"] = "МЛТ Лифты",
            ["{supplierAddress}"] = "",
            ["{supplierINN}"] = "",
            ["{buyerName}"] = model.ProjectName,
            ["{buyerAddress}"] = model.ProjectAddress,
            ["{deliveryTime}"] = "",
            ["{eqPay1}"] = "",
            ["{eqPay2}"] = "",
            ["{warranty}"] = "",
            ["{mtPay1}"] = "",
            ["{mtPay2}"] = "",
            ["{mtPay3}"] = "",
            ["{warrantyInstall}"] = "",
            ["{#lifts}"] = "",
            ["{/lifts}"] = "",
            ["{liftNumber}"] = FirstText(model.Field("Lift No"), model.SpecificationName),
            ["{capacity}"] = model.CapacityKg.ToString(CultureInfo.InvariantCulture),
            ["{passengers}"] = passengers.ToString(CultureInfo.InvariantCulture),
            ["{stops}"] = model.Stops.ToString(CultureInfo.InvariantCulture),
            ["{liftType}"] = FirstText(model.Field("Model"), model.Series),
            ["{speed}"] = model.Speed.ToString("0.##", CultureInfo.InvariantCulture),
            ["{travelHeight}"] = model.Field("Travel Height", "TR"),
            ["{doors}"] = model.Doors.ToString(CultureInfo.InvariantCulture),
            ["{controlSystem}"] = model.Field("Control System", "Operation"),
            ["{shaftWidth}"] = model.Field("Shaft Width", "AH"),
            ["{shaftDepth}"] = model.Field("Shaft Depth", "BH"),
            ["{pit}"] = model.Field("Pit", "PD"),
            ["{topFloorHeight}"] = model.Field("Overhead", "OH"),
            ["{cabinType}"] = model.Field("Car Type", "Car Design"),
            ["{cabinDesign}"] = model.Field("Cabin Design", "Car Design"),
            ["{cabinWidth}"] = model.Field("Car Width", "AA"),
            ["{cabinDepth}"] = model.Field("Car Depth", "BB"),
            ["{cabinHeight}"] = model.Field("Car Height", "HL"),
            ["{finishWallFront}"] = model.Field("Car Wall Material", "Car Design Wall", "Wall"),
            ["{finishWallSide}"] = model.Field("Car Wall Material", "Car Design Wall", "Wall"),
            ["{finishWallRear}"] = model.Field("Car Wall Material", "Car Design Wall", "Wall"),
            ["{finishCeiling}"] = model.Field("Ceiling"),
            ["{finishFloor}"] = model.Field("Floor", "Floor Pattern"),
            ["{mirrorType}"] = model.Field("Mirror Height", "Mirror"),
            ["{handrailEnabled}"] = HasText(model.Field("Handrail Position", "Handrail")) ? "Да" : "Нет",
            ["{finishHandrail}"] = model.Field("Handrail"),
            ["{handrailSides}"] = model.Field("Handrail Position"),
            ["{copType}"] = model.Field("COP"),
            ["{copButtons}"] = model.Field("COP Button"),
            ["{#optionRows}"] = "",
            ["{col1}"] = options,
            ["{col2}"] = "",
            ["{col3}"] = "",
            ["{/optionRows}"] = "",
            ["{doorWidth}"] = model.Field("Door Width", "JJ"),
            ["{doorHeight}"] = model.Field("Door Height", "HH"),
            ["{doorType}"] = model.Field("Door Opening", "Door mode", "Door type"),
            ["{cabinDoorMaterialRu}"] = model.Field("Car Door Material", "Car Door"),
            ["{landingMainMatRu}"] = model.Field("Main Shaft Door", "Main Landing Material"),
            ["{landingOtherMatRu}"] = model.Field("Other Shaft Door", "Other Landing Material"),
            ["{fireResistance}"] = model.Field("Fire Rating"),
            ["{lopMain}"] = model.Field("Main LOP"),
            ["{lipMain}"] = model.Field("Main LIP"),
            ["{lopOther}"] = model.Field("Other LOP"),
            ["{lipOther}"] = model.Field("Other LIP")
        };
    }

    private static void ReplaceParagraphText(XDocument document, IReadOnlyDictionary<string, string> replacements)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        foreach (var paragraph in document.Descendants(w + "p"))
        {
            var textNodes = paragraph.Descendants(w + "t").ToArray();
            if (textNodes.Length == 0) continue;
            var text = string.Concat(textNodes.Select(node => node.Value));
            var replaced = text;
            foreach (var (placeholder, value) in replacements)
            {
                replaced = replaced.Replace(placeholder, value, StringComparison.Ordinal);
            }
            if (replaced == text) continue;
            textNodes[0].Value = replaced;
            foreach (var node in textNodes.Skip(1)) node.Value = "";
        }
    }

    private static void PopulateProposalMetadata(XDocument document, TkpModel model)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var proposalParagraph = document.Descendants(w + "p").FirstOrDefault(paragraph =>
            ParagraphText(paragraph, w).StartsWith("Коммерческое предложение #", StringComparison.Ordinal));
        if (proposalParagraph is not null)
        {
            SetParagraphText(proposalParagraph, $"Коммерческое предложение #{model.Number}", w);
        }

        var projectTable = document.Descendants(w + "tbl").FirstOrDefault(table =>
            TableText(table, w).Contains("Проект:", StringComparison.Ordinal)
            && TableText(table, w).Contains("Адрес:", StringComparison.Ordinal));
        if (projectTable is null) return;
        var cells = projectTable.Descendants(w + "tc").ToArray();
        if (cells.Length >= 4)
        {
            SetCellText(cells[1], model.ProjectName, w);
            SetCellText(cells[3], model.ProjectAddress, w);
        }
    }

    private static void PopulateCommercialTables(XDocument document, TkpModel model)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var tables = document.Descendants(w + "tbl").ToArray();
        var equipment = tables.FirstOrDefault(table =>
            TableText(table, w).Contains("Стоимость одной единицы оборудования", StringComparison.Ordinal));
        if (equipment is not null)
        {
            PopulateCommercialTable(equipment, model, model.TotalCny, model.TotalCny * model.Quantity, w);
        }

        var installation = tables.FirstOrDefault(table =>
            TableText(table, w).Contains("Стоимость монтажа одной единицы", StringComparison.Ordinal));
        if (installation is not null)
        {
            PopulateCommercialTable(installation, model, 0, 0, w, installation: true);
        }
    }

    private static void PopulateCommercialTable(
        XElement table,
        TkpModel model,
        decimal unitPrice,
        decimal totalPrice,
        XNamespace w,
        bool installation = false)
    {
        var rows = table.Elements(w + "tr").ToArray();
        if (rows.Length < 2) return;
        var designation = FirstText(model.Field("Lift No"), model.SpecificationName);
        var values = installation
            ? new[]
            {
                "1",
                designation,
                model.EquipmentType,
                model.Quantity.ToString(CultureInfo.InvariantCulture),
                "По отдельному расчету",
                "По отдельному расчету"
            }
            : new[]
            {
                "1",
                designation,
                model.EquipmentType,
                model.Quantity.ToString(CultureInfo.InvariantCulture),
                Money(unitPrice),
                Money(totalPrice)
            };
        SetRowValues(rows[1], values, w);
    }

    private static void PopulateTechnicalSpecification(
        XDocument document,
        TkpModel model,
        ImageRegistry images)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var appendixTitle = document.Descendants(w + "tbl").FirstOrDefault(table =>
            TableText(table, w).Contains("ПРИЛОЖЕНИЕ 1.", StringComparison.Ordinal)
            && TableText(table, w).Contains("Спецификация оборудования и материалов", StringComparison.Ordinal));
        if (appendixTitle is null) return;

        var specificationTable = BuildTechnicalSpecificationTable(model, images, w);
        appendixTitle.AddAfterSelf(specificationTable, PageBreakParagraph(w));
    }

    private static XElement BuildTechnicalSpecificationTable(
        TkpModel model,
        ImageRegistry images,
        XNamespace w)
    {
        var table = new XElement(w + "tbl",
            new XElement(w + "tblPr",
                new XElement(w + "tblW",
                    new XAttribute(w + "w", "9350"),
                    new XAttribute(w + "type", "dxa")),
                new XElement(w + "tblLayout", new XAttribute(w + "type", "fixed")),
                TableBorders(w),
                new XElement(w + "tblCellMar",
                    CellMargin(w, "top", 80),
                    CellMargin(w, "left", 120),
                    CellMargin(w, "bottom", 80),
                    CellMargin(w, "right", 120))),
            new XElement(w + "tblGrid",
                new XElement(w + "gridCol", new XAttribute(w + "w", "4550")),
                new XElement(w + "gridCol", new XAttribute(w + "w", "4800"))));

        foreach (var row in model.TechnicalSpecificationRows)
        {
            table.Add(row.IsSection
                ? BuildSectionRow(row.Value, w)
                : BuildSpecificationRow(row, images, w));
        }

        return table;
    }

    private static XElement BuildSectionRow(string value, XNamespace w)
    {
        return new XElement(w + "tr",
            new XElement(w + "trPr", new XElement(w + "cantSplit")),
            new XElement(w + "tc",
                new XElement(w + "tcPr",
                    new XElement(w + "tcW",
                        new XAttribute(w + "w", "9350"),
                        new XAttribute(w + "type", "dxa")),
                    new XElement(w + "gridSpan", new XAttribute(w + "val", "2")),
                    new XElement(w + "shd",
                        new XAttribute(w + "val", "clear"),
                        new XAttribute(w + "color", "auto"),
                        new XAttribute(w + "fill", "0B2E5B"))),
                SpecificationParagraph(value, w, bold: true, color: "FFFFFF")));
    }

    private static XElement BuildSpecificationRow(TkpSpecificationRow row, ImageRegistry images, XNamespace w)
    {
        var rightCell = new XElement(w + "tc",
            new XElement(w + "tcPr",
                new XElement(w + "tcW",
                    new XAttribute(w + "w", "4800"),
                    new XAttribute(w + "type", "dxa")),
                new XElement(w + "vAlign", new XAttribute(w + "val", "center"))),
            SpecificationParagraph(row.Value, w, bold: row.BoldValue));

        var image = images.Register(row.ImageCode);
        if (image is not null)
        {
            rightCell.Add(CreateImageParagraph(image, w));
        }

        return new XElement(w + "tr",
            new XElement(w + "trPr", new XElement(w + "cantSplit")),
            new XElement(w + "tc",
                new XElement(w + "tcPr",
                    new XElement(w + "tcW",
                        new XAttribute(w + "w", "4550"),
                        new XAttribute(w + "type", "dxa")),
                    new XElement(w + "vAlign", new XAttribute(w + "val", "center"))),
                SpecificationParagraph(row.Label, w, bold: row.BoldLabel)),
            rightCell);
    }

    private static XElement SpecificationParagraph(
        string text,
        XNamespace w,
        bool bold = false,
        string color = "000000")
    {
        var paragraphs = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var paragraph = new XElement(w + "p",
            new XElement(w + "pPr",
                new XElement(w + "spacing",
                    new XAttribute(w + "before", "0"),
                    new XAttribute(w + "after", "0"),
                    new XAttribute(w + "line", "240"),
                    new XAttribute(w + "lineRule", "auto"))));
        for (var index = 0; index < paragraphs.Length; index++)
        {
            if (index > 0)
            {
                paragraph.Add(new XElement(w + "r", new XElement(w + "br")));
            }
            paragraph.Add(new XElement(w + "r",
                new XElement(w + "rPr",
                    new XElement(w + "rFonts",
                        new XAttribute(w + "ascii", "Arial"),
                        new XAttribute(w + "hAnsi", "Arial"),
                        new XAttribute(w + "cs", "Arial")),
                    new XElement(w + "sz", new XAttribute(w + "val", "20")),
                    new XElement(w + "szCs", new XAttribute(w + "val", "20")),
                    new XElement(w + "color", new XAttribute(w + "val", color)),
                    bold ? new XElement(w + "b") : null),
                new XElement(w + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), paragraphs[index])));
        }
        return paragraph;
    }

    private static XElement CreateImageParagraph(EmbeddedImage image, XNamespace w)
    {
        XNamespace wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var (cx, cy) = image.GetExtent();
        var picture = new XElement(pic + "pic",
            new XElement(pic + "nvPicPr",
                new XElement(pic + "cNvPr", new XAttribute("id", "0"), new XAttribute("name", image.Name)),
                new XElement(pic + "cNvPicPr")),
            new XElement(pic + "blipFill",
                new XElement(a + "blip", new XAttribute(r + "embed", image.RelationshipId)),
                new XElement(a + "stretch", new XElement(a + "fillRect"))),
            new XElement(pic + "spPr",
                new XElement(a + "xfrm",
                    new XElement(a + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                    new XElement(a + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy))),
                new XElement(a + "prstGeom", new XAttribute("prst", "rect"), new XElement(a + "avLst"))));
        var inline = new XElement(wp + "inline",
            new XAttribute("distT", "0"), new XAttribute("distB", "0"),
            new XAttribute("distL", "0"), new XAttribute("distR", "0"),
            new XElement(wp + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
            new XElement(wp + "effectExtent",
                new XAttribute("l", "0"), new XAttribute("t", "0"),
                new XAttribute("r", "0"), new XAttribute("b", "0")),
            new XElement(wp + "docPr", new XAttribute("id", image.Id), new XAttribute("name", image.Name)),
            new XElement(wp + "cNvGraphicFramePr",
                new XElement(a + "graphicFrameLocks", new XAttribute("noChangeAspect", "1"))),
            new XElement(a + "graphic",
                new XElement(a + "graphicData",
                    new XAttribute("uri", "http://schemas.openxmlformats.org/drawingml/2006/picture"),
                    picture)));
        return new XElement(w + "p",
            new XElement(w + "pPr",
                new XElement(w + "jc", new XAttribute(w + "val", "center")),
                new XElement(w + "spacing", new XAttribute(w + "before", "80"), new XAttribute(w + "after", "0"))),
            new XElement(w + "r", new XElement(w + "drawing", inline)));
    }

    private static XElement TableBorders(XNamespace w)
    {
        return new XElement(w + "tblBorders",
            Border(w, "top"), Border(w, "left"), Border(w, "bottom"), Border(w, "right"),
            Border(w, "insideH"), Border(w, "insideV"));
    }

    private static XElement Border(XNamespace w, string side)
    {
        return new XElement(w + side,
            new XAttribute(w + "val", "single"),
            new XAttribute(w + "sz", "4"),
            new XAttribute(w + "space", "0"),
            new XAttribute(w + "color", "404040"));
    }

    private static XElement CellMargin(XNamespace w, string side, int width)
    {
        return new XElement(w + side,
            new XAttribute(w + "w", width.ToString(CultureInfo.InvariantCulture)),
            new XAttribute(w + "type", "dxa"));
    }

    private static XElement PageBreakParagraph(XNamespace w)
    {
        return new XElement(w + "p", new XElement(w + "r", new XElement(w + "br", new XAttribute(w + "type", "page"))));
    }

    private static string ParagraphText(XElement paragraph, XNamespace w) =>
        string.Concat(paragraph.Descendants(w + "t").Select(node => node.Value));

    private static string TableText(XElement table, XNamespace w) =>
        string.Concat(table.Descendants(w + "t").Select(node => node.Value));

    private static void SetParagraphText(XElement paragraph, string value, XNamespace w)
    {
        var textNodes = paragraph.Descendants(w + "t").ToArray();
        if (textNodes.Length == 0)
        {
            paragraph.Add(new XElement(w + "r", new XElement(w + "t", value)));
            return;
        }
        textNodes[0].Value = value;
        foreach (var node in textNodes.Skip(1)) node.Value = "";
    }

    private static void EnableFieldUpdates(XDocument settings)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var root = settings.Root;
        if (root is null) return;
        var updateFields = root.Element(w + "updateFields");
        if (updateFields is null)
        {
            root.Add(new XElement(w + "updateFields", new XAttribute(w + "val", "true")));
        }
        else
        {
            updateFields.SetAttributeValue(w + "val", "true");
        }
    }

    private static void AddImageRelationships(ZipArchive archive, ImageRegistry images)
    {
        if (images.Count == 0) return;
        const string relationshipsPath = "word/_rels/document.xml.rels";
        var entry = archive.GetEntry(relationshipsPath);
        XNamespace packageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        XDocument relationships;
        if (entry is null)
        {
            relationships = new XDocument(new XElement(packageRelationships + "Relationships"));
            entry = archive.CreateEntry(relationshipsPath);
        }
        else
        {
            using var input = entry.Open();
            relationships = XDocument.Load(input, LoadOptions.PreserveWhitespace);
        }

        var root = relationships.Root ?? throw new InvalidDataException("DOCX relationships root is missing.");
        foreach (var image in images.Items)
        {
            root.Add(new XElement(packageRelationships + "Relationship",
                new XAttribute("Id", image.RelationshipId),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                new XAttribute("Target", $"media/{image.Name}")));
        }

        using var output = entry.Open();
        output.SetLength(0);
        relationships.Save(output, SaveOptions.DisableFormatting);
    }

    private static void EnsurePngContentType(ZipArchive archive, bool required)
    {
        if (!required) return;
        var entry = archive.GetEntry("[Content_Types].xml")
            ?? throw new InvalidDataException("DOCX content types are missing.");
        XDocument contentTypes;
        using (var input = entry.Open())
        {
            contentTypes = XDocument.Load(input, LoadOptions.PreserveWhitespace);
        }

        XNamespace types = "http://schemas.openxmlformats.org/package/2006/content-types";
        var root = contentTypes.Root ?? throw new InvalidDataException("DOCX content types root is missing.");
        if (!root.Elements(types + "Default").Any(item =>
                string.Equals(item.Attribute("Extension")?.Value, "png", StringComparison.OrdinalIgnoreCase)))
        {
            root.Add(new XElement(types + "Default",
                new XAttribute("Extension", "png"),
                new XAttribute("ContentType", "image/png")));
        }

        using var output = entry.Open();
        output.SetLength(0);
        contentTypes.Save(output, SaveOptions.DisableFormatting);
    }

    private sealed class ImageRegistry(string assetsRoot, string supplier)
    {
        private readonly Dictionary<string, EmbeddedImage?> _byCode = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<EmbeddedImage> _items = [];

        public int Count => _items.Count;
        public IReadOnlyList<EmbeddedImage> Items => _items;

        public EmbeddedImage? Register(string? rawCode)
        {
            var code = ExtractImageCode(rawCode);
            if (string.IsNullOrWhiteSpace(code)) return null;
            if (_byCode.TryGetValue(code, out var cached)) return cached;

            var path = ResolveImagePath(code);
            if (path is null)
            {
                _byCode[code] = null;
                return null;
            }

            var bytes = File.ReadAllBytes(path);
            if (!TryReadPngSize(bytes, out var width, out var height))
            {
                _byCode[code] = null;
                return null;
            }

            var index = _items.Count + 1;
            var image = new EmbeddedImage(
                index,
                $"rIdTkpSpecImage{index}",
                $"tkp-spec-{index}.png",
                bytes,
                width,
                height);
            _items.Add(image);
            _byCode[code] = image;
            return image;
        }

        public void WriteTo(ZipArchive archive)
        {
            foreach (var image in _items)
            {
                var entry = archive.CreateEntry($"word/media/{image.Name}", CompressionLevel.Optimal);
                using var output = entry.Open();
                output.Write(image.Bytes);
            }
        }

        private string? ResolveImagePath(string code)
        {
            var folder = string.Equals(supplier, "XIZI", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(assetsRoot, "xizi-docx")
                : Path.Combine(assetsRoot, "smec");
            if (!Directory.Exists(folder)) return null;

            var normalized = NormalizeImageCode(code);
            var candidates = Directory.EnumerateFiles(folder, "*.png", SearchOption.TopDirectoryOnly)
                .OrderBy(path => Path.GetFileNameWithoutExtension(path).Contains(' ') ? 1 : 0)
                .ToArray();
            return candidates.FirstOrDefault(path =>
            {
                var fileName = Path.GetFileNameWithoutExtension(path);
                if (fileName.StartsWith("Pic_", StringComparison.OrdinalIgnoreCase))
                {
                    fileName = fileName[4..];
                }
                return string.Equals(NormalizeImageCode(fileName), normalized, StringComparison.OrdinalIgnoreCase);
            });
        }

        private static string? ExtractImageCode(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return value.Split([';', ',', '\n', '\r', '·'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
        }

        private static string NormalizeImageCode(string value)
        {
            return new string(value
                .Replace("■", "", StringComparison.Ordinal)
                .Replace("_", "-", StringComparison.Ordinal)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToUpperInvariant)
                .ToArray());
        }

        private static bool TryReadPngSize(byte[] bytes, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (bytes.Length < 24
                || bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47)
            {
                return false;
            }
            width = ReadBigEndianInt32(bytes, 16);
            height = ReadBigEndianInt32(bytes, 20);
            return width > 0 && height > 0;
        }

        private static int ReadBigEndianInt32(byte[] bytes, int offset)
        {
            return (bytes[offset] << 24)
                   | (bytes[offset + 1] << 16)
                   | (bytes[offset + 2] << 8)
                   | bytes[offset + 3];
        }
    }

    private sealed record EmbeddedImage(
        int Id,
        string RelationshipId,
        string Name,
        byte[] Bytes,
        int PixelWidth,
        int PixelHeight)
    {
        public (long Cx, long Cy) GetExtent()
        {
            const long maxWidth = 2_150_000;
            const long maxHeight = 1_900_000;
            var width = maxWidth;
            var height = (long)Math.Round(width * (double)PixelHeight / PixelWidth);
            if (height > maxHeight)
            {
                height = maxHeight;
                width = (long)Math.Round(height * (double)PixelWidth / PixelHeight);
            }
            return (width, height);
        }
    }

    private static void SetRowValues(XElement row, IReadOnlyList<string> values, XNamespace w)
    {
        var cells = row.Elements(w + "tc").ToArray();
        for (var index = 0; index < Math.Min(cells.Length, values.Count); index++)
        {
            SetCellText(cells[index], values[index], w);
        }
    }

    private static void SetCellText(XElement cell, string value, XNamespace w)
    {
        var textNodes = cell.Descendants(w + "t").ToArray();
        if (textNodes.Length == 0)
        {
            var paragraph = cell.Elements(w + "p").FirstOrDefault();
            if (paragraph is null)
            {
                paragraph = new XElement(w + "p");
                cell.Add(paragraph);
            }
            var run = paragraph.Elements(w + "r").FirstOrDefault();
            if (run is null)
            {
                run = new XElement(w + "r");
                paragraph.Add(run);
            }
            run.Add(new XElement(w + "t", value));
            return;
        }
        textNodes[0].Value = value;
        foreach (var node in textNodes.Skip(1)) node.Value = "";
    }

    private static string FirstText(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    private static string InfoTable(TkpModel model)
    {
        var rows = new[]
        {
            Row("Проект", model.ProjectName, "Адрес", model.ProjectAddress),
            Row("Номер запроса на завод", model.FactoryRequestNumber, "Спецификация", model.SpecificationName),
            Row("Поставщик", model.Supplier, "Серия", model.Series),
            Row("Грузоподъемность", $"{model.CapacityKg} кг", "Скорость", $"{model.Speed:0.##} м/с"),
            Row("Остановки / двери", $"{model.Stops} / {model.Doors}", "Статус расчета", model.StatusLabel)
        };
        return Table([.. rows], [2600, 3300, 2600, 3300], 0);
    }

    private static string EquipmentTable(TkpModel model)
    {
        var unit = model.TotalCny;
        var total = model.TotalCny * model.Quantity;
        var rows = new List<IReadOnlyList<Cell>>
        {
            HeaderRow("Поз. №", "Обозначение", "Тип оборудования", "Кол-во, шт.", "Стоимость одной единицы, CNY", "Общая стоимость, CNY"),
            Row(
                "1",
                model.SpecificationName,
                model.EquipmentType,
                model.Quantity.ToString(CultureInfo.InvariantCulture),
                Money(unit),
                Money(total))
        };

        return Table(rows, [950, 2100, 4050, 1250, 2200, 2200], 1);
    }

    private static string InstallationTable(TkpModel model)
    {
        var rows = new List<IReadOnlyList<Cell>>
        {
            HeaderRow("Поз. №", "Обозначение", "Тип оборудования", "Кол-во, шт.", "Стоимость монтажа одной единицы, RUB", "Общая стоимость монтажа, RUB"),
            Row("1", model.SpecificationName, model.EquipmentType, model.Quantity.ToString(CultureInfo.InvariantCulture), "По отдельному расчету", "По отдельному расчету")
        };

        return Table(rows, [950, 2100, 4050, 1250, 2200, 2200], 1);
    }

    private static string PriceLinesTable(TkpModel model)
    {
        var rows = new List<IReadOnlyList<Cell>>
        {
            HeaderRow("Код", "Позиция", "Кол-во", "Цена за ед., CNY", "Сумма, CNY", "Статус")
        };
        rows.AddRange(model.Lines.Select(line => Row(
            line.Code,
            line.Label,
            line.Quantity.ToString(CultureInfo.InvariantCulture),
            line.UnitPriceCny is null ? "Проверка" : Money(line.UnitPriceCny.Value),
            line.AmountCny is null ? "Проверка" : Money(line.AmountCny.Value),
            StatusLabel(line.Status))));

        rows.Add(Row("", "Итого заводская цена", "", "", Money(model.TotalCny), model.StatusLabel, true));

        if (!string.Equals(model.TargetCurrency, "CNY", StringComparison.OrdinalIgnoreCase))
        {
            rows.Add(Row("", $"Пересчет по курсу 1 CNY = {model.ExchangeRate:0.####} {model.TargetCurrency}", "", "", $"{Money(model.TotalConverted)} {model.TargetCurrency}", model.ExchangeRateSource));
        }

        return Table(rows, [1200, 5600, 1100, 2100, 2100, 1500], 1);
    }

    private static string SpecificationTable(TkpModel model)
    {
        var rows = new List<IReadOnlyList<Cell>>
        {
            HeaderRow("Раздел", "Параметр", "Значение")
        };

        foreach (var item in model.SpecificationRows)
        {
            rows.Add(Row(item.Group, item.Label, item.Value));
        }

        if (model.Options.Count > 0)
        {
            rows.Add(Row("Опции", "Выбранные функции", string.Join(", ", model.Options)));
        }

        return Table(rows, [2600, 3900, 6200], 1);
    }

    private static IReadOnlyList<string> Boilerplate(string supplier)
    {
        var supplierName = supplier.Equals("XIZI", StringComparison.OrdinalIgnoreCase)
            ? "Xizi Elevator Co., Ltd."
            : "Shanghai Mitsubishi Elevator Co., Ltd.";
        return
        [
            "Производство начинается после подписания контракта, согласования технических деталей и получения авансового платежа на банковский счет поставщика.",
            "Сроки поставки и монтажа уточняются в момент подписания контракта и составления графика производства работ.",
            "Предлагаемое оборудование соответствует требованиям ТР ТС «Безопасность лифтов» (011/2011), ГОСТ 33984, ГОСТ 53780, ГОСТ Р 52382, а также применимым европейским нормам и правилам.",
            $"Оборудование производится {supplierName}. Окончательная спецификация и цена подлежат проверке ответственным сотрудником перед отправкой заказчику.",
            "Настоящее предложение действительно в течение 30 календарных дней с даты представления и не является офертой в смысле ст. 435 ГК РФ.",
            "Любая информация, передаваемая или получаемая в рамках настоящего предложения, является конфиденциальной и не подлежит передаче третьим лицам без взаимного согласия сторон."
        ];
    }

    private static string Table(IReadOnlyList<IReadOnlyList<Cell>> rows, IReadOnlyList<int> widths, int headerRows)
    {
        var grid = string.Concat(widths.Select(width => $"""<w:gridCol w:w="{width}"/>"""));
        var table = new StringBuilder();
        table.Append($$"""
            <w:tbl>
              <w:tblPr>
                <w:tblW w:w="0" w:type="auto"/>
                <w:tblBorders>
                  <w:top w:val="single" w:sz="6" w:space="0" w:color="CBD5E1"/>
                  <w:left w:val="single" w:sz="6" w:space="0" w:color="CBD5E1"/>
                  <w:bottom w:val="single" w:sz="6" w:space="0" w:color="CBD5E1"/>
                  <w:right w:val="single" w:sz="6" w:space="0" w:color="CBD5E1"/>
                  <w:insideH w:val="single" w:sz="4" w:space="0" w:color="CBD5E1"/>
                  <w:insideV w:val="single" w:sz="4" w:space="0" w:color="CBD5E1"/>
                </w:tblBorders>
                <w:tblCellMar>
                  <w:top w:w="90" w:type="dxa"/>
                  <w:left w:w="120" w:type="dxa"/>
                  <w:bottom w:w="90" w:type="dxa"/>
                  <w:right w:w="120" w:type="dxa"/>
                </w:tblCellMar>
              </w:tblPr>
              <w:tblGrid>{{grid}}</w:tblGrid>
            """);

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            table.Append("<w:tr>");
            var row = rows[rowIndex];
            for (var cellIndex = 0; cellIndex < row.Count; cellIndex++)
            {
                var cell = row[cellIndex];
                var width = widths[Math.Min(cellIndex, widths.Count - 1)];
                var shade = cell.Shade ?? (rowIndex < headerRows ? "EAF0FA" : null);
                var bold = cell.Bold || rowIndex < headerRows;
                table.Append($$"""
                    <w:tc>
                      <w:tcPr>
                        <w:tcW w:w="{{width}}" w:type="dxa"/>
                        {{(shade is null ? "" : $"""<w:shd w:val="clear" w:color="auto" w:fill="{shade}"/>""")}}
                      </w:tcPr>
                      {{Paragraph(cell.Text, bold ? "TableHeader" : "TableText")}}
                    </w:tc>
                    """);
            }
            table.Append("</w:tr>");
        }

        table.Append("</w:tbl>");
        return table.ToString();
    }

    private static IReadOnlyList<Cell> HeaderRow(params string[] values)
    {
        return values.Select(value => new Cell(value, true, "EAF0FA")).ToArray();
    }

    private static IReadOnlyList<Cell> Row(params string[] values)
    {
        return values.Select(value => new Cell(value)).ToArray();
    }

    private static IReadOnlyList<Cell> Row(
        string first,
        string second,
        string third,
        string fourth,
        string fifth,
        string sixth,
        bool total = false)
    {
        return new[]
        {
            new Cell(first, total),
            new Cell(second, total),
            new Cell(third, total),
            new Cell(fourth, total),
            new Cell(fifth, total),
            new Cell(sixth, total)
        };
    }

    private static string Paragraph(string text, string style = "Normal")
    {
        var runProperties = style switch
        {
            "Title" => """<w:b/><w:color w:val="082B57"/><w:sz w:val="32"/>""",
            "Heading1" => """<w:b/><w:color w:val="082B57"/><w:sz w:val="24"/>""",
            "Muted" => """<w:color w:val="667085"/><w:sz w:val="20"/>""",
            "Warning" => """<w:color w:val="9A3412"/><w:sz w:val="19"/>""",
            "Callout" => """<w:b/><w:color w:val="14532D"/><w:sz w:val="20"/>""",
            "TableHeader" => """<w:b/><w:color w:val="17213A"/><w:sz w:val="17"/>""",
            "TableText" => """<w:color w:val="17213A"/><w:sz w:val="17"/>""",
            _ => """<w:color w:val="17213A"/><w:sz w:val="20"/>"""
        };
        var spacing = style switch
        {
            "Title" => """<w:spacing w:after="120"/>""",
            "Heading1" => """<w:spacing w:before="260" w:after="120"/>""",
            "TableHeader" or "TableText" => """<w:spacing w:before="0" w:after="0" w:line="240" w:lineRule="auto"/>""",
            _ => """<w:spacing w:after="120" w:line="276" w:lineRule="auto"/>"""
        };

        return $$"""
            <w:p>
              <w:pPr>{{spacing}}</w:pPr>
              <w:r>
                <w:rPr><w:rFonts w:ascii="Montserrat" w:hAnsi="Montserrat" w:cs="Arial"/>{{runProperties}}</w:rPr>
                <w:t xml:space="preserve">{{Escape(text)}}</w:t>
              </w:r>
            </w:p>
            """;
    }

    private static string PageBreak()
    {
        return """<w:p><w:r><w:br w:type="page"/></w:r></w:p>""";
    }

    private static byte[] CreateDocx(string bodyXml)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            WriteEntry(archive, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
                </Types>
                """);
            WriteEntry(archive, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            WriteEntry(archive, "word/styles.xml", StylesXml());
            WriteEntry(archive, "word/document.xml", $$"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>{{bodyXml}}</w:body>
                </w:document>
                """);
        }

        return output.ToArray();
    }

    private static string StylesXml()
    {
        return """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:docDefaults>
                <w:rPrDefault>
                  <w:rPr><w:rFonts w:ascii="Montserrat" w:hAnsi="Montserrat" w:cs="Arial"/><w:sz w:val="20"/></w:rPr>
                </w:rPrDefault>
                <w:pPrDefault>
                  <w:pPr><w:spacing w:after="120" w:line="276" w:lineRule="auto"/></w:pPr>
                </w:pPrDefault>
              </w:docDefaults>
            </w:styles>
            """;
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content.Trim());
    }

    private static string StatusLabel(string status)
    {
        return status switch
        {
            "ready" => "Готово",
            "warning" => "Требуется проверка",
            "blocked" => "Заблокировано",
            _ => status
        };
    }

    private static string Money(decimal value)
    {
        return value.ToString("N2", RuCulture);
    }

    private static string Escape(string? value)
    {
        return SecurityElement.Escape(value ?? "") ?? "";
    }

    private sealed record Cell(string Text, bool Bold = false, string? Shade = null);

    private sealed record SpecificationRow(string Group, string Label, string Value);

    private sealed record TkpSpecificationRow(
        string Label,
        string Value,
        bool IsSection = false,
        string? ImageCode = null,
        bool BoldLabel = false,
        bool BoldValue = false);

    private sealed class TkpModel
    {
        private readonly PricingCalculationRequest? _request;
        private readonly PricingCatalog _catalog;

        public TkpModel(
            PricingSpecification specification,
            UserProject? project,
            PricingCalculationRequest? request,
            PricingCalculationResult? calculation,
            PricingCatalog catalog)
        {
            _request = request;
            _catalog = catalog;
            SpecificationName = specification.Name;
            ProjectName = FirstText(project?.Name, Field("Project Name"), "Проект");
            ProjectAddress = FirstText(project?.Address, Field("Address"), "__________");
            FactoryRequestNumber = FirstText(project?.FactoryRequestNumber, Field("Contract No"), specification.Id[..Math.Min(8, specification.Id.Length)]);
            Supplier = calculation?.Supplier ?? specification.Supplier;
            Series = calculation?.Series ?? specification.Series;
            Number = FirstText(project?.FactoryRequestNumber, Field("Contract No"), specification.Id[..Math.Min(8, specification.Id.Length)]);
            Date = specification.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy", RuCulture);
            CapacityKg = request?.CapacityKg ?? 0;
            Speed = request?.Speed ?? 0;
            Stops = request?.Stops ?? 0;
            Doors = request?.DoorCount ?? ReadInt(Field("Doors"), 0);
            Quantity = Math.Max(1, ReadInt(Field("Quantity"), 1));
            TotalCny = calculation?.TotalCny ?? specification.TotalCny;
            TargetCurrency = calculation?.TargetCurrency ?? specification.TargetCurrency;
            ExchangeRate = calculation?.ExchangeRate ?? 1;
            ExchangeRateSource = calculation?.ExchangeRateSource ?? "";
            TotalConverted = calculation?.TotalConverted ?? specification.TotalConverted;
            Lines = calculation?.Lines ?? [];
            Warnings = [.. (calculation?.Warnings ?? []), .. (calculation?.Blockers ?? [])];
            Options = request?.Options?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray() ?? [];
            Container = calculation?.Container;
            StatusLabel = TkpDocxBuilder.StatusLabel(calculation?.Status ?? specification.Status);
            Manufacturer = Supplier.Equals("XIZI", StringComparison.OrdinalIgnoreCase)
                ? "Xizi Elevator Co., Ltd."
                : "Shanghai Mitsubishi Elevator Co., Ltd.";
            EquipmentType = BuildEquipmentType();
            SpecificationRows = BuildSpecificationRows();
            TechnicalSpecificationRows = BuildTechnicalSpecificationRows();
        }

        public string SpecificationName { get; }
        public string ProjectName { get; }
        public string ProjectAddress { get; }
        public string FactoryRequestNumber { get; }
        public string Supplier { get; }
        public string Series { get; }
        public string Number { get; }
        public string Date { get; }
        public int CapacityKg { get; }
        public decimal Speed { get; }
        public int Stops { get; }
        public int Doors { get; }
        public int Quantity { get; }
        public decimal TotalCny { get; }
        public string TargetCurrency { get; }
        public decimal ExchangeRate { get; }
        public string ExchangeRateSource { get; }
        public decimal TotalConverted { get; }
        public IReadOnlyList<PricingLine> Lines { get; }
        public IReadOnlyList<string> Warnings { get; }
        public IReadOnlyList<string> Options { get; }
        public ContainerInfo? Container { get; }
        public string StatusLabel { get; }
        public string Manufacturer { get; }
        public string EquipmentType { get; }
        public IReadOnlyList<SpecificationRow> SpecificationRows { get; }
        public IReadOnlyList<TkpSpecificationRow> TechnicalSpecificationRows { get; }

        private IReadOnlyList<TkpSpecificationRow> BuildTechnicalSpecificationRows()
        {
            return Supplier.Equals("XIZI", StringComparison.OrdinalIgnoreCase)
                ? BuildXiziTechnicalRows()
                : BuildSmecTechnicalRows();
        }

        private IReadOnlyList<TkpSpecificationRow> BuildSmecTechnicalRows()
        {
            var carDesign = Field("Car Design");
            var design = _catalog.Smec.CarDesigns.FirstOrDefault(item => CodeMatches(item.Code, carDesign));
            var wallDescription = FirstText(Field("Car Design Wall"), design?.WallDescription, Field("Wall"));
            var doorDescription = FirstText(Field("Car Design Door"), design?.DoorDescription, Field("Car Door"));
            var floor = FormatFloor(Field("Floor Type"), Field("Floor Pattern"));
            var doorType = Field("Door type");
            var options = BuildOptionRows(_catalog.Smec.Functions);
            var power = _catalog.Smec.Power.FirstOrDefault(item =>
                SeriesMatches(item.Series, Series)
                && item.Capacity == CapacityKg
                && SameNumber(item.Speed, Speed))?.Power;

            var rows = new List<TkpSpecificationRow>
            {
                Section($"Лифт {FirstText(Field("Lift No"), SpecificationName)}"),
                Value("Тип лифта", "Пассажирский\nБез машинного помещения"),
                Value("Модель", Series),
                Value("Тип привода", "Безредукторная лебедка с электродвигателем на постоянных магнитах"),
                Value("Тип башмаков", Options.Any(option => option.Contains("Roller guide shoe", StringComparison.OrdinalIgnoreCase)) ? "Роликовые" : "Скольжения"),
                Value("Регламент", "ТР ТС 011/2011\nГОСТ 33984.1-2016 (EN 81-20:2014)"),
                Value("Количество единиц, шт.", Quantity.ToString(CultureInfo.InvariantCulture)),
                Value("Система управления", Field("Operation")),
                Value("Грузоподъемность, кг", CapacityKg.ToString(CultureInfo.InvariantCulture), boldValue: true),
                Value("Скорость, м/с", Speed.ToString("0.##", RuCulture), boldValue: true),
                Value("Количество остановок, шт.", Stops.ToString(CultureInfo.InvariantCulture), boldValue: true),
                Value("Количество дверей шахты, шт.", Doors.ToString(CultureInfo.InvariantCulture), boldValue: true),
                Value("Главный посадочный этаж", Field("Main Floor")),
                Value("Нумерация остальных этажей", Field("Other Floors")),
                Value("Высота подъема, м", MillimetersToMeters(Field("TR")), boldValue: true),
                Value("Размеры кабины (Ш x Г x В), мм", JoinDimensions(Field("AA"), Field("BB"), Field("HL")), boldValue: true),
                Value("Тип кабины", DoorCabinType(doorType)),
                Value("Размеры дверей (Ш x В), мм", JoinDimensions(Field("JJ"), Field("HH")), boldValue: true),
                Value("Тип дверей", TranslateDoorOpening(Field("Door mode"))),
                Value("Огнестойкость дверей шахты", ResolveFireRating()),
                Section("Отделка кабины"),
                Value("Дизайн по каталогу", string.Equals(carDesign, "Customized", StringComparison.OrdinalIgnoreCase) ? "Нет" : carDesign, carDesign),
                Value("Потолок", Field("Ceiling"), Field("Ceiling")),
                Value("Пол", floor, Field("Floor Pattern")),
                Value("Стены кабины", wallDescription, carDesign),
                Value("Двери кабины", doorDescription, Field("Car Door")),
                Value("Зеркало", TranslateCommonValue(JoinValues(Field("Mirror"), Field("Mirror Position")))),
                Value("Поручень", FormatHandrail(Field("Handrail"), Field("Handrail Position")), Field("Handrail")),
                Value("Основная панель приказов (COP)", Field("COP"), Field("COP")),
                Value("Вспомогательная панель приказов (COP 2)", Field("COP 2"), Field("COP 2")),
                Value("Кнопки панели приказов", Field("COP Button"), Field("COP Button")),
                Section("Главный посадочный этаж"),
                Value("Двери шахты", JoinValues(Field("Main Jamb"), Field("Main Landing Door")), Field("Main Landing Door")),
                Value("Панель вызовов (LOP)", Field("Main LOP"), Field("Main LOP")),
                Value("Кнопки панели вызовов", Field("LOP Button"), Field("LOP Button")),
                Value("Вспомогательная панель вызовов", Field("Main Auxiliary LOP"), Field("Main Auxiliary LOP")),
                Section("Остальные посадочные этажи"),
                Value("Двери шахты", JoinValues(Field("Other Jamb"), Field("Other Landing Door")), Field("Other Landing Door")),
                Value("Панель вызовов (LOP)", Field("Other LOP"), Field("Other LOP")),
                Value("Кнопки панели вызовов", Field("Other LOP Button"), Field("Other LOP Button")),
                Value("Вспомогательная панель вызовов", Field("Other Auxiliary LOP"), Field("Other Auxiliary LOP"))
            };
            if (options.Count > 0)
            {
                rows.Add(Section("Стандартные опции"));
                rows.AddRange(options.Select(option => Value("", option)));
            }
            rows.AddRange([
                Value("Источник питания", FirstText(Field("Power Supply"), "380±7% В, 50±2% Гц"), boldLabel: true),
                Value("Мощность, кВт", power?.ToString("0.##", RuCulture) ?? "—", boldLabel: true),
                Value("Размеры шахты (Ш x Г), мм", JoinDimensions(Field("AH"), Field("BH")), boldLabel: true, boldValue: true),
                Value("Высота оголовка, мм", Field("OH"), boldLabel: true, boldValue: true),
                Value("Глубина приямка, мм", Field("PD"), boldLabel: true, boldValue: true),
                Value("Помещение под приямком/ловители противовеса", HasOption("CWT Safety Gear") ? "Ловители противовеса предусмотрены" : "Нет / Без ловителей", boldLabel: true),
                Value("Условия эксплуатации", "(+5...+40) °C", boldLabel: true)
            ]);
            return RemoveEmptyRows(rows);
        }

        private IReadOnlyList<TkpSpecificationRow> BuildXiziTechnicalRows()
        {
            var designCode = Field("Cabin Design");
            var designVisual = FindVisual(_catalog.Xizi.VisualItems, designCode);
            var options = BuildOptionRows(_catalog.Xizi.Options);
            var rows = new List<TkpSpecificationRow>
            {
                Section($"Лифт {FirstText(Field("Lift No"), SpecificationName)}"),
                Value("Тип лифта", FirstText(Field("Elevator Type"), "Пассажирский\nБез машинного помещения")),
                Value("Модель", FirstText(Field("Model"), Series)),
                Value("Регламент", "ТР ТС 011/2011\nГОСТ 33984.1-2016 (EN 81-20:2014)"),
                Value("Количество единиц, шт.", Quantity.ToString(CultureInfo.InvariantCulture)),
                Value("Система управления", Field("Control System")),
                Value("Грузоподъемность, кг", CapacityKg.ToString(CultureInfo.InvariantCulture), boldValue: true),
                Value("Скорость, м/с", Speed.ToString("0.##", RuCulture), boldValue: true),
                Value("Количество остановок, шт.", Stops.ToString(CultureInfo.InvariantCulture), boldValue: true),
                Value("Количество дверей шахты, шт.", Doors.ToString(CultureInfo.InvariantCulture), boldValue: true),
                Value("Главный посадочный этаж", Field("Main Floor")),
                Value("Нумерация остальных этажей", Field("Other Floors")),
                Value("Высота подъема, м", MillimetersToMeters(Field("Travel Height")), boldValue: true),
                Value("Высота оголовка, мм", Field("Overhead"), boldValue: true),
                Value("Глубина приямка, мм", Field("Pit"), boldValue: true),
                Value("Размеры шахты (Ш x Г), мм", JoinDimensions(Field("Shaft Width"), Field("Shaft Depth")), boldValue: true),
                Value("Размеры кабины (Ш x Г x В), мм", JoinDimensions(Field("Car Width"), Field("Car Depth"), Field("Car Height")), boldValue: true),
                Value("Тип кабины", Field("Car Type")),
                Value("Размеры дверей (Ш x В), мм", JoinDimensions(Field("Door Width"), Field("Door Height")), boldValue: true),
                Value("Тип дверей", Field("Door Opening")),
                Value("Огнестойкость дверей шахты", Field("Fire Rating")),
                Value("Помещение под приямком/ловители противовеса", HasOption("CWTSAFETY") ? "Ловители противовеса предусмотрены" : "Нет / Без ловителей"),
                Value("Условия эксплуатации", "(+5...+40) °C"),
                Section("Отделка кабины"),
                Value("Дизайн по каталогу", designCode, designCode),
                Value("Потолок", VisualValue(Field("Ceiling")), Field("Ceiling")),
                Value("Пол", VisualValue(Field("Floor")), Field("Floor")),
                Value("Стены кабины", FirstText(Field("Car Wall Material"), designVisual?.Description), designCode),
                Value("Двери кабины", Field("Car Door Material")),
                Value("Зеркало", JoinValues(Field("Mirror Wall"), Field("Mirror Height"))),
                Value("Поручень", VisualValue(JoinValues(Field("Handrail"), Field("Handrail Position"))), Field("Handrail")),
                Value("Основная панель приказов (COP)", VisualValue(Field("COP")), Field("COP")),
                Value("Кнопки панели приказов", Field("COP Button"), Field("COP Button")),
                Section("Главный посадочный этаж"),
                Value("Двери шахты", Field("Main Shaft Door"), Field("Main Shaft Door")),
                Value("Панель вызовов (LOP)", VisualValue(Field("Main LOP")), Field("Main LOP")),
                Value("Этажный указатель (LIP)", VisualValue(Field("Main LIP")), Field("Main LIP")),
                Section("Остальные посадочные этажи"),
                Value("Двери шахты", Field("Other Shaft Door"), Field("Other Shaft Door")),
                Value("Панель вызовов (LOP)", VisualValue(Field("Other LOP")), Field("Other LOP")),
                Value("Этажный указатель (LIP)", VisualValue(Field("Other LIP")), Field("Other LIP"))
            };
            if (options.Count > 0)
            {
                rows.Add(Section("Опции"));
                rows.AddRange(options.Select(option => Value("", option)));
            }
            return RemoveEmptyRows(rows);
        }

        private string BuildEquipmentType()
        {
            var doorWidth = Field("JJ", "Door Width");
            var doorHeight = Field("HH", "Door Height");
            var doorText = string.IsNullOrWhiteSpace(doorWidth)
                ? ""
                : $", двери {doorWidth}x{doorHeight} мм";
            return $"Лифт пассажирский {Series}, {CapacityKg} кг, {Stops} ост., {Speed:0.##} м/с{doorText}";
        }

        private IReadOnlyList<SpecificationRow> BuildSpecificationRows()
        {
            var rows = Supplier.Equals("XIZI", StringComparison.OrdinalIgnoreCase)
                ? XiziRows()
                : SmecRows();

            return rows
                .Where(row => !string.IsNullOrWhiteSpace(row.Value))
                .ToArray();
        }

        private IReadOnlyList<SpecificationRow> SmecRows()
        {
            return
            [
                Spec("Общее", "Серия оборудования", "Ele Series"),
                Spec("Общее", "Тип проекта", "Project Type"),
                Spec("Общее", "Стандарт изготовления", "Manufacturing Standard"),
                Spec("Общее", "Система управления", "Operation"),
                Spec("Общее", "Этажи", "Floors"),
                Spec("Общее", "Остановки", "Stops"),
                Spec("Общее", "Двери", "Doors"),
                Spec("Шахта", "Ширина шахты AH, мм", "AH"),
                Spec("Шахта", "Глубина шахты BH, мм", "BH"),
                Spec("Шахта", "Высота подъема TR, мм", "TR"),
                Spec("Шахта", "Высота оголовка OH, мм", "OH"),
                Spec("Шахта", "Глубина приямка PD, мм", "PD"),
                Spec("Кабина", "Ширина кабины AA, мм", "AA"),
                Spec("Кабина", "Глубина кабины BB, мм", "BB"),
                Spec("Кабина", "Высота кабины HL, мм", "HL"),
                Spec("Двери", "Тип дверей шахты", "Door type"),
                Spec("Двери", "Дверной режим", "Door mode"),
                Spec("Двери", "Ширина дверей JJ, мм", "JJ"),
                Spec("Двери", "Высота дверей HH, мм", "HH"),
                Spec("Отделка", "Дизайн кабины", "Car Design"),
                Spec("Отделка", "Потолок", "Ceiling"),
                Spec("Отделка", "Пол", "Floor Type", "Floor Pattern"),
                Spec("Отделка", "Стены кабины", "Wall"),
                Spec("Отделка", "Двери кабины", "Car Door"),
                Spec("Отделка", "Зеркало", "Mirror"),
                Spec("Отделка", "Поручень", "Handrail Position", "Handrail"),
                Spec("Панели", "COP", "COP"),
                Spec("Панели", "LOP основной этаж", "Main LOP"),
                Spec("Панели", "LOP остальные этажи", "Other LOP"),
                Spec("Площадки", "Портал основного этажа", "Main Jamb", "Main Landing Material"),
                Spec("Площадки", "Порталы остальных этажей", "Other Jamb", "Other Landing Material"),
                Spec("Прочее", "Прочие требования", "Other Requirements")
            ];
        }

        private IReadOnlyList<SpecificationRow> XiziRows()
        {
            return
            [
                Spec("Общее", "Тип лифта", "Elevator Type"),
                Spec("Общее", "Модель", "Model"),
                Spec("Общее", "Номер лифта", "Lift No"),
                Spec("Общее", "Система управления", "Control System"),
                Spec("Общее", "Этажи / остановки / двери", "Stops", "Doors"),
                Spec("Шахта", "Ширина шахты, мм", "Shaft Width"),
                Spec("Шахта", "Глубина шахты, мм", "Shaft Depth"),
                Spec("Шахта", "Высота подъема, мм", "Travel Height"),
                Spec("Шахта", "Оголовок, мм", "Overhead"),
                Spec("Шахта", "Приямок, мм", "Pit"),
                Spec("Кабина", "Ширина кабины, мм", "Car Width"),
                Spec("Кабина", "Глубина кабины, мм", "Car Depth"),
                Spec("Кабина", "Высота кабины, мм", "Car Height"),
                Spec("Кабина", "Тип кабины", "Car Type"),
                Spec("Двери", "Тип открывания", "Door Opening"),
                Spec("Двери", "Ширина / высота", "Door Width", "Door Height"),
                Spec("Двери", "Огнестойкость", "Fire Rating"),
                Spec("Отделка", "Дизайн кабины", "Cabin Design"),
                Spec("Отделка", "Материал стен", "Car Wall Material"),
                Spec("Отделка", "Материал дверей кабины", "Car Door Material"),
                Spec("Отделка", "Потолок", "Ceiling"),
                Spec("Отделка", "Пол", "Floor"),
                Spec("Отделка", "Зеркало", "Mirror Wall", "Mirror Height"),
                Spec("Отделка", "Поручень", "Handrail Position", "Handrail"),
                Spec("Панели", "COP", "COP", "COP Button"),
                Spec("Площадки", "Двери шахты", "Main Shaft Door", "Other Shaft Door"),
                Spec("Площадки", "LOP", "Main LOP", "Other LOP"),
                Spec("Площадки", "LIP", "Main LIP", "Other LIP"),
                Spec("Прочее", "AC", "AC"),
                Spec("Прочее", "RCC", "RCC")
            ];
        }

        private List<string> BuildOptionRows(IReadOnlyList<PriceEntry> catalogEntries)
        {
            var rows = new List<string>();
            foreach (var option in Options.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (option.StartsWith("CONTAINER_", StringComparison.OrdinalIgnoreCase)) continue;
                var entry = catalogEntries.FirstOrDefault(item => CodeMatches(item.Code, option));
                rows.Add(FirstText(entry?.Description, option));
            }
            return rows;
        }

        private string VisualValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var item = FindVisual(_catalog.Xizi.VisualItems, value);
            return FirstText(item?.Description is null ? null : $"{value}\n{item.Description}", value);
        }

        private static SmecVisualEntry? FindVisual(IReadOnlyList<SmecVisualEntry> items, string? code)
        {
            return items.FirstOrDefault(item => CodeMatches(item.Code, code));
        }

        private static IReadOnlyList<TkpSpecificationRow> RemoveEmptyRows(IEnumerable<TkpSpecificationRow> rows)
        {
            return rows.Where(row => row.IsSection || !string.IsNullOrWhiteSpace(row.Value)).ToArray();
        }

        private static TkpSpecificationRow Section(string title) => new("", title, IsSection: true);

        private static TkpSpecificationRow Value(
            string label,
            string value,
            string? imageCode = null,
            bool boldLabel = false,
            bool boldValue = false) =>
            new(label, NormalizeDisplayValue(value), ImageCode: imageCode, BoldLabel: boldLabel, BoldValue: boldValue);

        private static string NormalizeDisplayValue(string value)
        {
            return value
                .Replace("■-", "-", StringComparison.Ordinal)
                .Replace("■", "", StringComparison.Ordinal)
                .Trim();
        }

        private static string TranslateCommonValue(string value)
        {
            if (string.Equals(value.Trim(), "None", StringComparison.OrdinalIgnoreCase)) return "Нет";
            if (string.Equals(value.Trim(), "rear wall", StringComparison.OrdinalIgnoreCase)) return "По задней стене";
            if (string.Equals(value.Trim(), "front wall", StringComparison.OrdinalIgnoreCase)) return "По передней стене";
            if (string.Equals(value.Trim(), "side wall", StringComparison.OrdinalIgnoreCase)) return "По боковой стене";
            return value;
        }

        private static string FormatHandrail(string handrail, string position)
        {
            if (string.IsNullOrWhiteSpace(handrail)) return "Нет";
            return JoinValues(handrail, TranslateCommonValue(position));
        }

        private static string FormatFloor(string floorType, string floorPattern)
        {
            if (floorType.Contains("concave-down", StringComparison.OrdinalIgnoreCase))
            {
                var depth = floorPattern
                    .Replace("depth", "Глубина", StringComparison.OrdinalIgnoreCase)
                    .Replace("mm", "мм", StringComparison.OrdinalIgnoreCase);
                return JoinValues("Ниша под материал Заказчика", depth);
            }
            return JoinValues(floorPattern, floorType);
        }

        private static string JoinDimensions(params string[] values)
        {
            var parts = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
            return parts.Length == values.Length ? string.Join(" x ", parts) : "";
        }

        private static string JoinValues(params string[] values)
        {
            return string.Join("\n", values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private static string MillimetersToMeters(string value)
        {
            if (!decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var millimeters))
            {
                return value;
            }
            return (millimeters / 1000m).ToString("0.###", RuCulture);
        }

        private bool HasOption(string code)
        {
            return Options.Any(option => CodeMatches(option, code));
        }

        private string ResolveFireRating()
        {
            var value = Field("Fire Rating");
            if (!string.IsNullOrWhiteSpace(value)) return value;
            var requirements = Field("Other Requirements");
            foreach (var candidate in new[] { "EI120", "EI60", "EI30", "E120", "E60", "E30" })
            {
                if (requirements.Contains(candidate, StringComparison.OrdinalIgnoreCase)) return candidate;
            }
            return "—";
        }

        private static string DoorCabinType(string doorType)
        {
            return doorType.Contains("2G", StringComparison.OrdinalIgnoreCase)
                ? $"Проходная ({doorType})"
                : string.IsNullOrWhiteSpace(doorType) ? "" : $"Непроходная ({doorType})";
        }

        private static string TranslateDoorOpening(string value)
        {
            if (value.Contains("central", StringComparison.OrdinalIgnoreCase)) return "Центрального открывания";
            if (value.Contains("side", StringComparison.OrdinalIgnoreCase)
                || value.Contains("telesc", StringComparison.OrdinalIgnoreCase)) return "Телескопического открывания";
            return value;
        }

        private static bool CodeMatches(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
            return string.Equals(NormalizeCode(left), NormalizeCode(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeCode(string value)
        {
            return new string(value.Replace("■", "", StringComparison.Ordinal)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToUpperInvariant)
                .ToArray());
        }

        private static bool SeriesMatches(string value, string expected)
        {
            return string.Equals(value, expected, StringComparison.OrdinalIgnoreCase)
                || value.Contains(expected, StringComparison.OrdinalIgnoreCase)
                || expected.Contains(value, StringComparison.OrdinalIgnoreCase)
                || (expected.StartsWith("ELE-", StringComparison.OrdinalIgnoreCase)
                    && value.Contains("ELENESSA", StringComparison.OrdinalIgnoreCase));
        }

        private static bool SameNumber(decimal left, decimal right) => Math.Abs(left - right) < 0.001m;

        private SpecificationRow Spec(string group, string label, params string[] fieldNames)
        {
            return new SpecificationRow(
                group,
                label,
                string.Join(" / ", fieldNames.Select(name => Field(name)).Where(value => !string.IsNullOrWhiteSpace(value))));
        }

        public string Field(params string[] names)
        {
            if (_request?.SpecificationFields is null)
            {
                return "";
            }

            foreach (var name in names)
            {
                var value = _request.SpecificationFields.FirstOrDefault(item =>
                    string.Equals(item.Key.Trim(), name, StringComparison.OrdinalIgnoreCase)).Value;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return "";
        }

        private static string FirstText(params string?[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
        }

        private static int ReadInt(string? value, int fallback)
        {
            return int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
                ? result
                : fallback;
        }
    }
}
