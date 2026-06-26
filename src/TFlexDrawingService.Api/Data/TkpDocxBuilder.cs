using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace TFlexDrawingService.Api.Data;

internal static class TkpDocxBuilder
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    public static byte[] Build(
        PricingSpecification specification,
        UserProject? project,
        PricingCalculationRequest? request,
        PricingCalculationResult? calculation)
    {
        var model = new TkpModel(specification, project, request, calculation);
        var body = new StringBuilder();

        body.Append(Paragraph($"Коммерческое предложение #{model.Number}", "Title"));
        body.Append(Paragraph($"от {model.Date}", "Muted"));
        body.Append(Paragraph(""));
        body.Append(InfoTable(model));
        body.Append(Paragraph(""));

        body.Append(Paragraph($"Стоимость оборудования {model.Manufacturer}:", "Heading1"));
        body.Append(EquipmentTable(model));
        body.Append(Paragraph("Данная цена включает стоимость изготовления, доставки оборудования на площадку монтажа, страховку на период доставки, стоимость документации оборудования на русском языке в соответствии с нормами и правилами, действующими в Российской Федерации."));
        body.Append(Paragraph("В цену включены таможенные платежи на импортное оборудование."));

        body.Append(Paragraph($"Стоимость монтажа {model.Manufacturer}:", "Heading1"));
        body.Append(InstallationTable(model));
        body.Append(Paragraph("Стоимость монтажа приведена справочно и должна быть заполнена после ручной проверки условий объекта."));

        body.Append(Paragraph("Состав заводской цены", "Heading1"));
        body.Append(PriceLinesTable(model));

        if (model.Container is not null)
        {
            body.Append(Paragraph($"Контейнер: {model.Container.Label}", "Callout"));
        }

        if (model.Warnings.Count > 0)
        {
            body.Append(Paragraph("Предупреждения и ручная проверка", "Heading1"));
            foreach (var warning in model.Warnings)
            {
                body.Append(Paragraph(warning, "Warning"));
            }
        }

        body.Append(PageBreak());
        body.Append(Paragraph("ПРИЛОЖЕНИЕ 1. Спецификация оборудования и материалов", "Heading1"));
        body.Append(SpecificationTable(model));

        body.Append(Paragraph("ПРИЛОЖЕНИЕ 2. Условия предложения", "Heading1"));
        foreach (var paragraph in Boilerplate(model.Supplier))
        {
            body.Append(Paragraph(paragraph));
        }

        body.Append("""
            <w:sectPr>
              <w:pgSz w:w="11906" w:h="16838"/>
              <w:pgMar w:top="1134" w:right="850" w:bottom="1134" w:left="850" w:header="708" w:footer="708" w:gutter="0"/>
            </w:sectPr>
            """);

        return CreateDocx(body.ToString());
    }

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

    private sealed class TkpModel
    {
        private readonly PricingCalculationRequest? _request;

        public TkpModel(
            PricingSpecification specification,
            UserProject? project,
            PricingCalculationRequest? request,
            PricingCalculationResult? calculation)
        {
            _request = request;
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

        private SpecificationRow Spec(string group, string label, params string[] fieldNames)
        {
            return new SpecificationRow(
                group,
                label,
                string.Join(" / ", fieldNames.Select(name => Field(name)).Where(value => !string.IsNullOrWhiteSpace(value))));
        }

        private string Field(params string[] names)
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
