using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TFlexDrawingService.Api.Data;

public sealed class PricingCatalogStore(IWebHostEnvironment environment, IHttpClientFactory httpClientFactory)
{
    private readonly Lazy<PricingCatalog> _catalog = new(() =>
    {
        var path = Path.Combine(environment.ContentRootPath, "Data", "pricing-catalog.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<PricingCatalog>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new PricingCatalog();
    });

    public PricingCatalog Catalog => _catalog.Value;

    public PricingCatalogSummary GetSummary()
    {
        var catalog = Catalog;
        return new PricingCatalogSummary(
            catalog.Currency,
            catalog.GeneratedAt,
            catalog.Xizi.Series,
            catalog.Smec.Series,
            catalog.Xizi.BasePrices.Select(item => item.Capacity).Distinct().Order().ToArray(),
            catalog.Xizi.BasePrices.Select(item => item.Speed).Distinct().Order().ToArray(),
            catalog.Xizi.Doors.Select(item => item.Width).Distinct().Order().ToArray(),
            catalog.Xizi.Doors.Select(item => item.Manufacturer).Where(HasText).Select(item => item!).Distinct().Order().ToArray(),
            catalog.Xizi.Doors.Select(item => item.DoorType).Where(HasText).Select(item => item!).Distinct().Order().ToArray(),
            catalog.Xizi.Decorations.Where(item => HasText(item.Code)).ToArray(),
            catalog.Xizi.Options.Where(item => HasText(item.Code)).Take(80).ToArray(),
            catalog.Xizi.VisualItems.Where(item => HasText(item.Code)).ToArray(),
            catalog.Xizi.ChoiceGroups,
            catalog.Smec.BasePrices.Select(item => item.Capacity).Distinct().Order().ToArray(),
            catalog.Smec.BasePrices.Select(item => item.Speed).Distinct().Order().ToArray(),
            catalog.Smec.Functions.Where(item => HasText(item.Code)).Take(80).ToArray(),
            catalog.Smec.GroupControl.Where(item => HasText(item.Code)).Take(40).ToArray(),
            catalog.Smec.CarDesigns.Where(item => HasText(item.Code)).ToArray(),
            catalog.Smec.VisualItems.Where(item => HasText(item.Code)).ToArray(),
            catalog.Smec.Power,
            catalog.Smec.SpecFields,
            catalog.Smec.ChoiceGroups,
            catalog.Smec.FloorPatterns);
    }

    public async Task<CurrencyRateResult> GetCurrencyRateAsync(
        string targetCurrency,
        CancellationToken cancellationToken = default)
    {
        var normalized = string.IsNullOrWhiteSpace(targetCurrency)
            ? "RUB"
            : targetCurrency.Trim().ToUpperInvariant();
        if (normalized == "CNY")
        {
            return new CurrencyRateResult("CNY", 1, "base", null);
        }

        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            await using var stream = await client.GetStreamAsync(
                "https://open.er-api.com/v6/latest/CNY",
                cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.TryGetProperty("rates", out var rates)
                && rates.TryGetProperty(normalized, out var rateElement)
                && rateElement.TryGetDecimal(out var rate))
            {
                return new CurrencyRateResult(normalized, rate, "open.er-api.com", null);
            }
        }
        catch
        {
            // The calculation remains valid in CNY. The converted display gets a conservative fallback.
        }

        var fallback = normalized switch
        {
            "USD" => 0.14m,
            "EUR" => 0.13m,
            "RUB" => 12.5m,
            _ => 1m
        };
        return new CurrencyRateResult(normalized, fallback, "fallback", "Курс не удалось подтянуть автоматически. Показан резервный курс.");
    }

    public async Task<PricingCalculationResult> CalculateAsync(
        PricingCalculationRequest request,
        CancellationToken cancellationToken = default)
    {
        var rate = await GetCurrencyRateAsync(request.TargetCurrency ?? "RUB", cancellationToken);
        var lines = new List<PricingLine>();
        var warnings = new List<string>();
        var blockers = new List<string>();
        ContainerInfo? container = null;

        if (string.Equals(request.Supplier, "XIZI", StringComparison.OrdinalIgnoreCase))
        {
            CalculateXizi(request, lines, warnings, blockers, out container);
        }
        else
        {
            CalculateSmec(request, lines, warnings, blockers, out container);
        }

        var totalCny = lines.Sum(line => line.AmountCny ?? 0m);
        var status = blockers.Count > 0
            ? "blocked"
            : warnings.Count > 0
                ? "warning"
                : "ready";

        if (!string.IsNullOrWhiteSpace(rate.Warning))
        {
            warnings.Add(rate.Warning);
            if (status == "ready")
            {
                status = "warning";
            }
        }

        return new PricingCalculationResult(
            status,
            request.Supplier,
            request.Series,
            "CNY",
            rate.TargetCurrency,
            rate.Rate,
            rate.Source,
            Math.Round(totalCny, 2),
            Math.Round(totalCny * rate.Rate, 2),
            lines,
            warnings,
            blockers,
            container,
            DateTimeOffset.UtcNow);
    }

    public byte[] BuildTkpDocx(PricingSpecification specification, UserProject? project)
    {
        var calculation = JsonSerializer.Deserialize<PricingCalculationResult>(
            specification.CalculationJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var request = JsonSerializer.Deserialize<PricingCalculationRequest>(
            specification.RequestJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        return TkpDocxBuilder.Build(specification, project, request, calculation);
    }

    private void CalculateXizi(
        PricingCalculationRequest request,
        List<PricingLine> lines,
        List<string> warnings,
        List<string> blockers,
        out ContainerInfo? container)
    {
        var catalog = Catalog.Xizi;
        var baseEntry = catalog.BasePrices.FirstOrDefault(item =>
            EqualsText(item.Series, request.Series)
            && item.Capacity == request.CapacityKg
            && SameNumber(item.Speed, request.Speed)
            && item.Stops == request.Stops);
        AddCatalogValue(lines, warnings, blockers, "base", "Базовая цена", baseEntry?.Price, true);

        AddXiziExtraRise(request, baseEntry, lines, warnings, blockers);
        AddXiziShaftSurcharge(request, lines);

        var doorCount = Math.Max(1, request.DoorCount);
        if (request.DoorWidthMm > 0)
        {
            var manufacturer = ResolveXiziDoorManufacturer(request);
            var carDoorFinish = MapXiziDoorFinish(GetSpecificationField(request, "Car Door Material"));
            var mainDoorFinish = MapXiziDoorFinish(GetSpecificationField(request, "Main Shaft Door"));
            var otherDoorFinish = MapXiziDoorFinish(GetSpecificationField(request, "Other Shaft Door"));
            var fireRating = GetSpecificationField(request, "Fire Rating") ?? "E30";

            AddXiziDoorLine(
                catalog,
                request,
                manufacturer,
                "Car door",
                "-",
                "None",
                carDoorFinish,
                "Дверь кабины",
                1,
                lines,
                warnings,
                blockers);

            var isThrough = IsXiziThroughCar(GetSpecificationField(request, "Car Type"));
            if (isThrough)
            {
                AddXiziDoorLine(
                    catalog,
                    request,
                    manufacturer,
                    "2nd door",
                    "-",
                    "None",
                    carDoorFinish,
                    "Вторая дверь проходной кабины",
                    1,
                    lines,
                    warnings,
                    blockers);
            }

            AddXiziDoorLine(
                catalog,
                request,
                manufacturer,
                "Shaft door",
                "First",
                fireRating,
                mainDoorFinish,
                "Дверь шахты, основной этаж",
                1,
                lines,
                warnings,
                blockers);
            if (doorCount > 1)
            {
                AddXiziDoorLine(
                    catalog,
                    request,
                    manufacturer,
                    "Shaft door",
                    "Other",
                    fireRating,
                    otherDoorFinish,
                    "Двери шахты, остальные этажи",
                    doorCount - 1,
                    lines,
                    warnings,
                    blockers);
            }

            AddXiziDoorHeightSurcharge(
                request,
                carDoorFinish,
                1 + (isThrough ? 1 : 0),
                "Высота дверей кабины",
                lines);
            AddXiziDoorHeightSurcharge(request, mainDoorFinish, 1, "Высота двери основного этажа", lines);
            AddXiziDoorHeightSurcharge(
                request,
                otherDoorFinish,
                Math.Max(0, doorCount - 1),
                "Высота дверей остальных этажей",
                lines);
        }

        AddXiziCabinFinish(catalog, request, lines, warnings, blockers);
        AddXiziPanels(catalog, request, doorCount, lines, warnings, blockers);
        AddXiziLocalRequirements(catalog, request, lines, warnings, blockers);
        AddXiziOption(catalog, request, "40HQ", lines, warnings, blockers);

        foreach (var option in request.Options ?? [])
        {
            if (!EqualsText(option, "40HQ"))
            {
                AddXiziOption(catalog, request, option, lines, warnings, blockers);
            }
        }

        var airConditioner = GetSpecificationField(request, "AC");
        if (HasText(airConditioner) && !ContainsAny(airConditioner!, "Нет", "None"))
        {
            AddXiziAirConditioner(catalog, request, airConditioner!, lines, warnings, blockers);
        }

        var rcc = GetSpecificationField(request, "RCC");
        if (HasText(rcc) && !ContainsAny(rcc!, "Нет", "None"))
        {
            warnings.Add($"RCC {rcc}: цена отсутствует в прайсе XIZI, требуется ручная проверка.");
            lines.Add(new PricingLine("rcc", $"Перенос станции управления RCC: {rcc}", 1, null, null, "warning"));
        }

        var containerEntry = catalog.Containers.FirstOrDefault(item =>
            item.Capacity == request.CapacityKg && item.Stops == request.Stops);
        container = containerEntry is null
            ? null
            : new ContainerInfo(containerEntry.Container, $"{containerEntry.Fraction} * {containerEntry.Container}");

        warnings.Add("Расчет XIZI является предварительным и требует ручной проверки перед выпуском ТКП.");
    }

    private void AddXiziDoorLine(
        XiziCatalog catalog,
        PricingCalculationRequest request,
        string manufacturer,
        string part,
        string floor,
        string fireRating,
        string finish,
        string label,
        int quantity,
        List<PricingLine> lines,
        List<string> warnings,
        List<string> blockers)
    {
        var capacity = request.CapacityKg == 1275 ? 1250 : request.CapacityKg;
        var entry = catalog.Doors.FirstOrDefault(item =>
            EqualsText(item.Manufacturer, manufacturer)
            && EqualsText(item.Part, part)
            && EqualsText(item.DoorType, request.DoorType)
            && EqualsText(item.FireRating, fireRating)
            && EqualsText(item.Finish, finish)
            && item.Capacity == capacity
            && item.Width == request.DoorWidthMm
            && EqualsText(item.Floor, floor));
        AddCatalogValue(lines, warnings, blockers, "door", label, entry?.Price, true, quantity);
    }

    private static void AddXiziExtraRise(
        PricingCalculationRequest request,
        XiziBasePrice? baseEntry,
        List<PricingLine> lines,
        List<string> warnings,
        List<string> blockers)
    {
        var travelHeight = GetSpecificationNumber(request, "Travel Height");
        var standardHeight = Math.Max(0, request.Stops - 1) * 3100m;
        if (travelHeight <= standardHeight)
        {
            return;
        }

        var extraMeters = (travelHeight - standardHeight) / 1000m;
        if (baseEntry is null || !TryReadDecimal(baseEntry.ExtraRisePerMeter, out var unitPrice))
        {
            warnings.Add("Превышение расчетной высоты: ставка не найдена в прайсе XIZI.");
            lines.Add(new PricingLine("extra-rise", "Превышение расчетной высоты", 1, null, null, "warning"));
            return;
        }

        if (unitPrice == -1m)
        {
            blockers.Add("Превышение расчетной высоты недоступно для выбранной конфигурации XIZI.");
            lines.Add(new PricingLine("extra-rise", "Превышение расчетной высоты", 1, unitPrice, null, "blocked"));
            return;
        }

        AddReadyLine(
            lines,
            "extra-rise",
            $"Превышение расчетной высоты, {extraMeters:0.###} м",
            unitPrice,
            unitPrice * extraMeters);
    }

    private static void AddXiziShaftSurcharge(PricingCalculationRequest request, List<PricingLine> lines)
    {
        var pit = GetSpecificationNumber(request, "Pit");
        var overhead = GetSpecificationNumber(request, "Overhead");
        var capacity = request.CapacityKg;
        var speed = request.Speed;
        decimal surcharge = 0;

        if (capacity > 1000 && SameNumber(speed, 1m) && pit < 1400 && overhead < 4150)
        {
            surcharge = 6000;
        }
        else if (capacity > 1000 && SameNumber(speed, 1.75m) && pit < 1450 && overhead < 4500)
        {
            surcharge = 6000;
        }
        else if (capacity <= 1000 && SameNumber(speed, 1m) && pit < 1400 && overhead < 4150)
        {
            surcharge = 5000;
        }
        else if (capacity <= 1000 && SameNumber(speed, 1.75m) && pit < 1450 && overhead < 4500)
        {
            surcharge = 5000;
        }
        else if (capacity <= 1000 && SameNumber(speed, 2m))
        {
            surcharge = 5000;
        }
        else if (capacity <= 1000 && SameNumber(speed, 2.5m))
        {
            surcharge = 6000;
        }
        else if (capacity is 1150 or 1250 or 1275 && (SameNumber(speed, 2m) || SameNumber(speed, 2.5m)))
        {
            surcharge = 8000;
        }
        else if (capacity is 1350 or 1600 && (SameNumber(speed, 2m) || SameNumber(speed, 2.5m)))
        {
            surcharge = 10000;
        }

        if (surcharge > 0)
        {
            AddReadyLine(lines, "shaft-surcharge", "Надбавка за параметры приямка и оголовка", surcharge, surcharge);
        }
    }

    private static void AddXiziDoorHeightSurcharge(
        PricingCalculationRequest request,
        string finish,
        int quantity,
        string label,
        List<PricingLine> lines)
    {
        var height = GetSpecificationNumber(request, "Door Height");
        var steps = (int)Math.Floor(Math.Max(0, height - 2100m) / 100m);
        if (steps <= 0 || quantity <= 0)
        {
            return;
        }

        var perStep = EqualsText(finish, "Painted steel") ? 100m : 120m;
        var unitPrice = steps * perStep;
        AddReadyLine(lines, "door-height", label, unitPrice, unitPrice * quantity, quantity);
    }

    private static void AddXiziCabinFinish(
        XiziCatalog catalog,
        PricingCalculationRequest request,
        List<PricingLine> lines,
        List<string> warnings,
        List<string> blockers)
    {
        var multiplier = request.CapacityKg >= 1600 ? 1.44m : request.CapacityKg > 1050 ? 1.2m : 1m;
        var carHeight = GetSpecificationNumber(request, "Car Height");
        var design = GetSpecificationField(request, "Cabin Design");

        if (EqualsText(design, "U-CR126"))
        {
            AddXiziDecorationLine(
                catalog,
                "Car walls",
                MapXiziWallFinish(GetSpecificationField(request, "Car Wall Material")),
                "Стены кабины",
                multiplier,
                carHeight,
                true,
                lines,
                warnings,
                blockers);
        }
        else
        {
            AddXiziDecorationLine(
                catalog,
                "Car design",
                design,
                "Дизайн кабины",
                multiplier,
                carHeight,
                true,
                lines,
                warnings,
                blockers);
        }

        AddXiziDecorationLine(
            catalog,
            "Ceiling",
            GetSpecificationField(request, "Ceiling"),
            "Потолок",
            multiplier,
            carHeight,
            false,
            lines,
            warnings,
            blockers);
        AddXiziDecorationLine(
            catalog,
            "Floor",
            GetSpecificationField(request, "Floor"),
            "Пол",
            multiplier,
            carHeight,
            false,
            lines,
            warnings,
            blockers);

        var mirrorWall = GetSpecificationField(request, "Mirror Wall");
        if (HasText(mirrorWall) && !ContainsAny(mirrorWall!, "Нет", "None"))
        {
            AddXiziDecorationLine(
                catalog,
                "Mirror",
                GetSpecificationField(request, "Mirror Height"),
                "Зеркало",
                multiplier,
                carHeight,
                false,
                lines,
                warnings,
                blockers);
        }

        var handrail = GetSpecificationField(request, "Handrail");
        var handrailPosition = GetSpecificationField(request, "Handrail Position");
        if (HasText(handrail)
            && HasText(handrailPosition)
            && !ContainsAny(handrailPosition!, "Нет", "None"))
        {
            var entry = FindXiziDecoration(catalog, "Handrail", handrail);
            var quantity = GetHandrailQuantity(handrailPosition);
            if (entry is null)
            {
                AddCatalogValue(lines, warnings, blockers, "handrail", $"Поручень {handrail}", null, true, quantity);
            }
            else if (TryReadDecimal(entry.Price, out var basePrice))
            {
                var largeCar = GetSpecificationNumber(request, "Car Width") > 1800
                    || GetSpecificationNumber(request, "Car Depth") > 1800;
                var overprice = largeCar && TryReadDecimal(entry.Overprice, out var largeSurcharge)
                    ? largeSurcharge
                    : 0m;
                AddReadyLine(
                    lines,
                    "handrail",
                    $"Поручень {handrail}",
                    basePrice + overprice,
                    (basePrice + overprice) * quantity,
                    quantity);
            }
            else
            {
                AddCatalogValue(lines, warnings, blockers, "handrail", $"Поручень {handrail}", entry.Price, true, quantity);
            }
        }
    }

    private static void AddXiziDecorationLine(
        XiziCatalog catalog,
        string category,
        string? code,
        string label,
        decimal multiplier,
        decimal carHeight,
        bool includeHeight,
        List<PricingLine> lines,
        List<string> warnings,
        List<string> blockers)
    {
        if (!HasText(code))
        {
            return;
        }

        var entry = FindXiziDecoration(catalog, category, code);
        if (entry is null || !TryReadDecimal(entry.Price, out var price))
        {
            AddCatalogValue(lines, warnings, blockers, NormalizeCode(category), $"{label} {code}", entry?.Price, true);
            return;
        }

        if (price == -1m)
        {
            AddCatalogValue(lines, warnings, blockers, NormalizeCode(category), $"{label} {code}", entry.Price, true);
            return;
        }

        var amount = price * multiplier;
        if (includeHeight
            && carHeight > 0
            && TryReadDecimal(entry.Height, out var standardHeight)
            && carHeight > standardHeight
            && TryReadDecimal(entry.Overprice, out var overprice))
        {
            amount += (carHeight - standardHeight) / 100m * overprice;
        }

        AddReadyLine(lines, NormalizeCode(category), $"{label} {code}", amount, amount);
    }

    private static void AddXiziPanels(
        XiziCatalog catalog,
        PricingCalculationRequest request,
        int doorCount,
        List<PricingLine> lines,
        List<string> warnings,
        List<string> blockers)
    {
        var cop = GetSpecificationField(request, "COP");
        if (HasText(cop))
        {
            var entry = FindXiziDecoration(catalog, "COP", cop);
            if (entry is not null && TryReadDecimal(entry.Price, out var price) && price != -1m)
            {
                var extra = request.Stops > 4 && TryReadDecimal(entry.Overprice, out var perFloor)
                    ? (request.Stops - 4) * perFloor
                    : 0m;
                AddReadyLine(lines, "cop", $"COP {cop}", price + extra, price + extra);
            }
            else
            {
                AddCatalogValue(lines, warnings, blockers, "cop", $"COP {cop}", entry?.Price, true);
            }
        }

        AddXiziPanelLine(
            catalog,
            "Button",
            GetSpecificationField(request, "COP Button"),
            "Кнопки COP",
            request.Stops + 2,
            lines,
            warnings,
            blockers);
        AddXiziPanelLine(catalog, "LOP", GetSpecificationField(request, "Main LOP"), "LOP, основной этаж", 1, lines, warnings, blockers);
        AddXiziPanelLine(catalog, "LOP", GetSpecificationField(request, "Other LOP"), "LOP, остальные этажи", Math.Max(0, doorCount - 1), lines, warnings, blockers);
        AddXiziPanelLine(catalog, "LIP", GetSpecificationField(request, "Main LIP"), "LIP, основной этаж", 1, lines, warnings, blockers);
        AddXiziPanelLine(catalog, "LIP", GetSpecificationField(request, "Other LIP"), "LIP, остальные этажи", Math.Max(0, doorCount - 1), lines, warnings, blockers);
    }

    private static void AddXiziPanelLine(
        XiziCatalog catalog,
        string category,
        string? code,
        string label,
        int quantity,
        List<PricingLine> lines,
        List<string> warnings,
        List<string> blockers)
    {
        if (!HasText(code) || quantity <= 0 || ContainsAny(code!, "Нет", "None"))
        {
            return;
        }

        var entry = FindXiziDecoration(catalog, category, code);
        AddCatalogValue(lines, warnings, blockers, NormalizeCode(category), $"{label}: {code}", entry?.Price, true, quantity);
    }

    private static void AddXiziLocalRequirements(
        XiziCatalog catalog,
        PricingCalculationRequest request,
        List<PricingLine> lines,
        List<string> warnings,
        List<string> blockers)
    {
        var travel = GetSpecificationNumber(request, "Travel Height") / 1000m;
        var overhead = GetSpecificationNumber(request, "Overhead") / 1000m;
        var pit = GetSpecificationNumber(request, "Pit") / 1000m;
        var buildingHeight = travel + overhead + pit;

        foreach (var entry in catalog.LocalRequirements)
        {
            if (ContainsAny(entry.Code, "Hydraulic buffer")
                && !(request.CapacityKg > 1600 && request.Speed > 1m))
            {
                continue;
            }

            if (ContainsAny(entry.Code, "Pit Inspection"))
            {
                if (TryReadDecimal(entry.Price, out var basePrice))
                {
                    var amount = basePrice + 24m * (travel + overhead + 16m);
                    AddReadyLine(lines, "lmr-pit-inspection", $"LMR: {entry.Code.Trim()}", amount, amount);
                }
                else
                {
                    AddCatalogValue(lines, warnings, blockers, "lmr-pit-inspection", $"LMR: {entry.Code.Trim()}", entry.Price, true);
                }
                continue;
            }

            if (ContainsAny(entry.Code, "Hoistway lighting"))
            {
                if (TryReadDecimal(entry.Price, out var unitPrice))
                {
                    var quantity = Math.Max(0m, buildingHeight / 4m - buildingHeight / 7m);
                    AddReadyLine(lines, "lmr-hoistway-lighting", $"LMR: {entry.Code}", unitPrice, unitPrice * quantity);
                }
                else
                {
                    AddCatalogValue(lines, warnings, blockers, "lmr-hoistway-lighting", $"LMR: {entry.Code}", entry.Price, true);
                }
                continue;
            }

            AddCatalogValue(lines, warnings, blockers, "lmr", $"LMR: {entry.Code.Trim()}", entry.Price, true);
        }
    }

    private static void AddXiziOption(
        XiziCatalog catalog,
        PricingCalculationRequest request,
        string option,
        List<PricingLine> lines,
        List<string> warnings,
        List<string> blockers)
    {
        var entry = catalog.Options.FirstOrDefault(item => EqualsText(item.Code, option));
        if (entry is null)
        {
            AddCatalogValue(lines, warnings, blockers, "option", $"Опция {option}", null, true);
            return;
        }

        if (!TryReadDecimal(entry.Price, out var basePrice))
        {
            AddCatalogValue(lines, warnings, blockers, "option", $"Опция {option}", entry.Price, true);
            return;
        }

        var travel = GetSpecificationNumber(request, "Travel Height") / 1000m;
        var overhead = GetSpecificationNumber(request, "Overhead") / 1000m;
        var pit = GetSpecificationNumber(request, "Pit") / 1000m;
        var normalized = NormalizeCode(option);
        var amount = basePrice;

        if (normalized == NormalizeCode("CCTV"))
        {
            amount = basePrice * (travel + overhead + 16m);
        }
        else if (normalized == NormalizeCode("TC"))
        {
            amount = basePrice * (travel + overhead + 16m);
        }
        else if (normalized == NormalizeCode("COP2"))
        {
            amount = basePrice + Math.Max(0, request.Stops - 10) * 45m;
        }
        else if (normalized == NormalizeCode("HAD-R"))
        {
            amount = basePrice + Math.Max(1, request.DoorCount) * 101m;
        }
        else if (normalized == NormalizeCode("EER"))
        {
            var buildingHeight = travel + overhead + pit;
            amount = basePrice + 300m * (buildingHeight / 1.5m - buildingHeight / 2.5m);
        }

        AddReadyLine(lines, $"option-{normalized}", $"Опция {option}", amount, amount);
    }

    private static void AddXiziAirConditioner(
        XiziCatalog catalog,
        PricingCalculationRequest request,
        string selection,
        List<PricingLine> lines,
        List<string> warnings,
        List<string> blockers)
    {
        var code = selection.Contains("нагрев", StringComparison.OrdinalIgnoreCase)
            ? "AC Охлаждение и нагрев"
            : "AC Охлаждение";
        var entry = catalog.Options.FirstOrDefault(item => EqualsText(item.Code, code));
        if (entry is null)
        {
            AddCatalogValue(lines, warnings, blockers, "air-conditioner", code, null, true);
            return;
        }

        var priceIndex = request.CapacityKg > 1350 ? 1 : 0;
        var rawPrice = entry.Prices is { Count: > 0 } && priceIndex < entry.Prices.Count
            ? entry.Prices[priceIndex]
            : entry.Price;
        if (!TryReadDecimal(rawPrice, out var basePrice))
        {
            AddCatalogValue(lines, warnings, blockers, "air-conditioner", code, rawPrice, true);
            return;
        }

        var travel = GetSpecificationNumber(request, "Travel Height") / 1000m;
        var overhead = GetSpecificationNumber(request, "Overhead") / 1000m;
        var amount = basePrice + 11m * (travel + overhead + 12m);
        AddReadyLine(lines, "air-conditioner", code, amount, amount);
    }

    private static PriceEntry? FindXiziDecoration(XiziCatalog catalog, string category, string? code)
    {
        return catalog.Decorations.FirstOrDefault(item =>
            EqualsText(item.Category, category) && EqualsText(item.Code, code));
    }

    private static string ResolveXiziDoorManufacturer(PricingCalculationRequest request)
    {
        var shaftDepth = GetSpecificationNumber(request, "Shaft Depth");
        var carDepth = GetSpecificationNumber(request, "Car Depth");
        if (shaftDepth <= 0 || carDepth <= 0)
        {
            return HasText(request.DoorManufacturer) ? request.DoorManufacturer! : "FERMATOR";
        }

        var through = IsXiziThroughCar(GetSpecificationField(request, "Car Type"));
        var sideOpening = EqualsText(request.DoorType, "2S");
        var threshold = (through, sideOpening) switch
        {
            (false, false) => 350m,
            (false, true) => 400m,
            (true, false) => 570m,
            _ => 670m
        };
        return shaftDepth - carDepth >= threshold ? "OPTIMAX" : "FERMATOR";
    }

    private static string MapXiziDoorFinish(string? value)
    {
        return ContainsAny(value ?? "", "AISI443", "Нерж", "stainless")
            ? "AISI443"
            : "Painted steel";
    }

    private static string MapXiziWallFinish(string? value)
    {
        if (ContainsAny(value ?? "", "GOLD"))
        {
            return "AISI304 GOLD HSS";
        }
        if (ContainsAny(value ?? "", "AISI304"))
        {
            return "AISI304";
        }
        if (ContainsAny(value ?? "", "AISI443", "Нерж"))
        {
            return "AISI443";
        }
        return "Painted Steel";
    }

    private static bool IsXiziThroughCar(string? value)
    {
        return EqualsText(value, "Проходная") || EqualsText(value, "Through");
    }

    private void CalculateSmec(
        PricingCalculationRequest request,
        List<PricingLine> lines,
        List<string> warnings,
        List<string> blockers,
        out ContainerInfo? container)
    {
        var catalog = Catalog.Smec;
        var speed = SameNumber(request.Speed, 1.6m) ? 1.75m : request.Speed;
        var baseEntry = catalog.BasePrices.FirstOrDefault(item =>
            SeriesMatches(item.Series, request.Series)
            && item.Capacity == request.CapacityKg
            && SameNumber(item.Speed, speed));

        var operation = GetSpecificationField(request, "Operation");
        if (HasText(operation) && !EqualsText(operation, "1C-2BC"))
        {
            var groupControl = catalog.GroupControl.FirstOrDefault(item => CodeMatches(item.Code, operation));
            AddCatalogValue(lines, warnings, blockers, "group-control", $"Групповое управление {operation}", groupControl?.Price, false);
        }
        AddCatalogValue(lines, warnings, blockers, "base", "Базовая цена", baseEntry?.BasicPrice, false);

        if (baseEntry is not null)
        {
            var stopDelta = request.Stops - baseEntry.BasicStops;
            if (stopDelta != 0)
            {
                AddCatalogValue(lines, warnings, blockers, "stops", $"Остановки: {stopDelta:+#;-#;0}", baseEntry.PricePerStop, false, Math.Abs(stopDelta), Math.Sign(stopDelta));
            }

            var travelHeight = GetSpecificationNumber(request, "TR");
            var overHeightMeters = travelHeight / 1000m - 3.6m * (request.Stops - 1);
            if (overHeightMeters > 0
                && baseEntry.OverHeightPer1000 is not null
                && TryReadDecimal(baseEntry.OverHeightPer1000, out var overHeightPrice))
            {
                AddReadyLine(
                    lines,
                    "travel-height",
                    $"Превышение расчетной высоты, {overHeightMeters:0.###} м",
                    overHeightPrice,
                    overHeightPrice * overHeightMeters);
            }

            var doorType = GetSpecificationField(request, "Door type");
            var throughPrice = FindSmecFunctionPrice(catalog, "Through type door opening");
            var throughAmount = EqualsText(doorType, "1D2G") || EqualsText(doorType, "2D2G")
                ? throughPrice ?? 0m
                : 0m;
            var extraDoors = EqualsText(doorType, "2D2G")
                ? Math.Max(0, request.DoorCount - request.Stops)
                : 0;
            if (extraDoors > 0
                && baseEntry.PricePerDoor2D2G is not null
                && TryReadDecimal(baseEntry.PricePerDoor2D2G, out var extraDoorPrice))
            {
                throughAmount += extraDoorPrice * extraDoors;
            }
            if (throughAmount > 0)
            {
                AddReadyLine(lines, "through-door", "Сквозной вход и дополнительные двери", throughAmount, throughAmount);
            }
        }

        AddSmecDecorationWeight(catalog, request, lines, warnings);
        AddSmecDoorMode(catalog, request, lines, warnings);
        var components = AddSmecEquipmentLines(catalog, request, lines, warnings, blockers);
        AddSmecOptionalFunctions(catalog, request, lines, warnings);
        var doorAddonPrice = AddSmecOtherRequirements(catalog, request, components.HandrailQuantity, lines, warnings);
        AddSmecSpecialConditions(
            catalog,
            request,
            baseEntry,
            components,
            doorAddonPrice,
            lines,
            warnings);

        var containerEntry = catalog.Containers.FirstOrDefault(item =>
            item.Capacity == request.CapacityKg
            && item.Stops >= request.Stops
            && EqualsText(item.Efs, request.Efs ? "Да" : "Нет")
            && EqualsText(item.E312, request.E312 ? "Да" : "Нет"));
        container = containerEntry is null
            ? null
            : new ContainerInfo(containerEntry.Single, $"{containerEntry.Single}; микс {containerEntry.Mix}{containerEntry.MixUnit}");
    }

    private static void AddSmecDecorationWeight(
        SmecCatalog catalog,
        PricingCalculationRequest request,
        List<PricingLine> lines,
        List<string> warnings)
    {
        var rawWeight = GetSpecificationField(request, "Decoration Weight");
        if (!HasText(rawWeight) || rawWeight!.Contains("standard", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var weight = ParseFirstDecimal(rawWeight);
        var unitPrice = FindSmecFunctionPrice(catalog, "Decoration Weight");
        if (weight is null || unitPrice is null)
        {
            warnings.Add("Вес отделки: не удалось применить формулу KIP.");
            return;
        }

        var units = Math.Ceiling(weight.Value / 100m);
        AddReadyLine(lines, "decoration-weight", $"Вес отделки, {units:0} x 100 кг", unitPrice.Value, unitPrice.Value * units);
    }

    private static decimal AddSmecDoorMode(
        SmecCatalog catalog,
        PricingCalculationRequest request,
        List<PricingLine> lines,
        List<string> warnings)
    {
        var doorMode = GetSpecificationField(request, "Door mode");
        var function = doorMode?.Contains("Side", StringComparison.OrdinalIgnoreCase) == true
            ? "2S door opening"
            : doorMode?.Contains("Central", StringComparison.OrdinalIgnoreCase) == true
                ? "Car door lock"
                : null;
        if (function is null)
        {
            if (HasText(doorMode))
            {
                warnings.Add($"Режим дверей «{doorMode}» требует ручной проверки SMEC.");
            }
            return 0;
        }

        var amount = FindSmecFunctionPrice(catalog, function);
        if (amount is null)
        {
            warnings.Add($"{function}: цена не найдена.");
            return 0;
        }

        AddReadyLine(lines, "door-mode", $"Режим дверей: {doorMode}", amount.Value, amount.Value);
        return amount.Value;
    }

    private static SmecCalculationComponents AddSmecEquipmentLines(
        SmecCatalog catalog,
        PricingCalculationRequest request,
        List<PricingLine> lines,
        List<string> warnings,
        List<string> blockers)
    {
        var design = GetSpecificationField(request, "Car Design");
        var wall = GetSpecificationField(request, "Wall");
        var carDoor = GetSpecificationField(request, "Car Door");
        SmecDecorationPrice? sideWall = null;
        SmecDecorationPrice? frontWall = null;
        SmecDecorationPrice? cabinDoor = null;
        if (HasText(design) || HasText(wall))
        {
            sideWall = FindSmecDecoration(catalog.Decorations, request, EqualsText(design, "Customized") ? "CarWall" : "CarDesign", EqualsText(design, "Customized") ? wall ?? "" : design ?? "", null);
            AddSmecResolvedDecoration(lines, warnings, blockers, "cabin-walls", EqualsText(design, "Customized") ? $"Стены кабины {wall}" : $"Дизайн кабины {design}", sideWall);
        }
        if (HasText(carDoor))
        {
            frontWall = FindSmecDecoration(catalog.Decorations, request, "FrontPanel", carDoor!, null);
            cabinDoor = FindSmecDecoration(catalog.Decorations, request, "CarDoor", carDoor!, null);
            AddSmecResolvedDecoration(lines, warnings, blockers, "cabin-front", $"Передняя панель кабины {carDoor}", frontWall);
            AddSmecResolvedDecoration(lines, warnings, blockers, "cabin-door", $"Дверь кабины {carDoor}", cabinDoor);
        }

        var ceilingCode = GetSpecificationField(request, "Ceiling");
        if (HasText(ceilingCode))
        {
            var ceiling = FindSmecDecoration(catalog.Decorations, request, "Ceiling", ceilingCode!, null);
            AddSmecResolvedDecoration(lines, warnings, blockers, "ceiling", "Потолок", ceiling);
        }
        var floorCode = GetSpecificationField(request, "Floor Type");
        if (HasText(floorCode))
        {
            var floor = FindSmecDecoration(catalog.Decorations, request, "Floor", floorCode!, null);
            AddSmecResolvedDecoration(lines, warnings, blockers, "floor", "Пол", floor);
        }

        var mirrorCode = GetSpecificationField(request, "Mirror");
        if (HasText(mirrorCode) && !EqualsText(mirrorCode, "None"))
        {
            AddSmecResolvedDecoration(
                lines,
                warnings,
                blockers,
                "mirror",
                $"Зеркало {mirrorCode}",
                FindSmecDecoration(catalog.Decorations, request, "Mirror", mirrorCode!, null));
        }

        var handrailQuantity = GetHandrailQuantity(GetSpecificationField(request, "Handrail Position"));
        var handrailCode = GetSpecificationField(request, "Handrail");
        if (HasText(handrailCode))
        {
            var handrail = FindSmecDecoration(catalog.Decorations, request, "Handrail", handrailCode!, null);
            AddSmecResolvedDecoration(lines, warnings, blockers, "handrail", $"Поручень, {handrailQuantity} шт.", handrail, handrailQuantity);
        }

        AddSmecControlLine(catalog, request, lines, warnings, "cop", "COP", "COP", 1);
        AddSmecControlLine(catalog, request, lines, warnings, "cop-2", "COP2", "COP 2", 1);
        AddSmecSpecialButtonLine(catalog, request, lines, warnings, "cop-button", "Кнопки COP", "COP Button", request.Stops);
        AddSmecControlLine(catalog, request, lines, warnings, "wheelchair-cop", "WheelchairCOP", "Wheelchair COP", 1);
        AddSmecControlLine(catalog, request, lines, warnings, "wheelchair-cop-2", "WheelchairCOP", "Wheelchair COP 2", 1);

        var doorCount = Math.Max(0, request.DoorCount);
        var otherDoorCount = Math.Max(0, doorCount - 1);
        SmecDecorationPrice? mainJamb = null;
        SmecDecorationPrice? otherJamb = null;
        if (doorCount > 0)
        {
            var mainJambCode = GetSpecificationField(request, "Main Jamb");
            var mainJambMaterial = GetSpecificationField(request, "Main Landing Material");
            if (HasText(mainJambCode) && HasText(mainJambMaterial))
            {
                mainJamb = FindSmecDecoration(catalog.Decorations, request, "Jamb", mainJambMaterial!, mainJambCode);
                AddSmecResolvedDecoration(lines, warnings, blockers, "jamb-main", "Портал основного этажа", mainJamb);
            }

            var otherJambCode = GetSpecificationField(request, "Other Jamb");
            var otherJambMaterial = GetSpecificationField(request, "Other Landing Material");
            if (otherDoorCount > 0 && HasText(otherJambCode) && HasText(otherJambMaterial))
            {
                otherJamb = FindSmecDecoration(catalog.Decorations, request, "Jamb", otherJambMaterial!, otherJambCode);
                AddSmecResolvedDecoration(lines, warnings, blockers, "jamb-other", "Порталы остальных этажей", otherJamb, otherDoorCount);
            }

            var sillUnitPrice = FindSmecFunctionPrice(catalog, "Sill Support") ?? 200m;
            AddSmecSillLine(request, lines, "sill-main", "Порог основного этажа", "Main Sill Bracket", 1, sillUnitPrice);
            AddSmecSillLine(request, lines, "sill-other", "Пороги остальных этажей", "Other Sill Bracket", otherDoorCount, sillUnitPrice);
        }

        SmecDecorationPrice? mainShaftDoor = null;
        SmecDecorationPrice? otherShaftDoor = null;
        if (doorCount > 0)
        {
            var mainShaftDoorCode = GetSpecificationField(request, "Main Landing Door");
            if (HasText(mainShaftDoorCode))
            {
                mainShaftDoor = FindSmecDecoration(catalog.Decorations, request, "LandingDoor", mainShaftDoorCode!, null);
                AddSmecResolvedDecoration(lines, warnings, blockers, "shaft-door-main", "Дверь шахты основного этажа", mainShaftDoor);
            }

            var otherShaftDoorCode = GetSpecificationField(request, "Other Landing Door");
            if (otherDoorCount > 0 && HasText(otherShaftDoorCode))
            {
                otherShaftDoor = FindSmecDecoration(catalog.Decorations, request, "LandingDoor", otherShaftDoorCode!, null);
                AddSmecResolvedDecoration(lines, warnings, blockers, "shaft-door-other", "Двери шахты остальных этажей", otherShaftDoor, otherDoorCount);
            }
        }

        AddSmecControlLine(catalog, request, lines, warnings, "lop-main", null, "Main LOP", 1);
        AddSmecSpecialButtonLine(catalog, request, lines, warnings, "lop-button-main", "Кнопка LOP основного этажа", "LOP Button", 1);
        AddSmecControlLine(catalog, request, lines, warnings, "lop-other", null, "Other LOP", otherDoorCount);
        AddSmecSpecialButtonLine(catalog, request, lines, warnings, "lop-button-other", "Кнопки LOP остальных этажей", "Other LOP Button", otherDoorCount);
        AddSmecControlLine(catalog, request, lines, warnings, "aux-lop-main", null, "Main Auxiliary LOP", 1);
        AddSmecControlLine(catalog, request, lines, warnings, "aux-lop-other", null, "Other Auxiliary LOP", otherDoorCount);
        AddSmecControlLine(catalog, request, lines, warnings, "hall-indicator", "HallIndicator", "Hall Indicator", Math.Max(1, doorCount));
        AddSmecControlLine(catalog, request, lines, warnings, "hall-lantern", "HallLantern", "Hall Lantern", Math.Max(1, doorCount));

        return new SmecCalculationComponents(
            sideWall?.Price ?? 0,
            frontWall?.Price ?? 0,
            cabinDoor?.Price ?? 0,
            mainJamb?.Price ?? 0,
            otherJamb?.Price ?? 0,
            mainShaftDoor?.Price ?? 0,
            otherShaftDoor?.Price ?? 0,
            handrailQuantity,
            false);
    }

    private static void AddSmecResolvedDecoration(
        List<PricingLine> lines,
        List<string> warnings,
        List<string> blockers,
        string code,
        string label,
        SmecDecorationPrice? entry,
        int quantity = 1)
    {
        AddCatalogValue(lines, warnings, blockers, code, label, entry?.Price, false, quantity);
    }

    private static void AddSmecSillLine(
        PricingCalculationRequest request,
        List<PricingLine> lines,
        string code,
        string label,
        string field,
        int quantity,
        decimal unitPrice)
    {
        if (quantity <= 0)
        {
            return;
        }

        var value = GetSpecificationField(request, field);
        if (value?.Contains("Steel", StringComparison.OrdinalIgnoreCase) == true)
        {
            AddReadyLine(lines, code, label, unitPrice, unitPrice * quantity, quantity);
        }
    }

    private static void AddSmecControlLine(
        SmecCatalog catalog,
        PricingCalculationRequest request,
        List<PricingLine> lines,
        List<string> warnings,
        string lineCode,
        string? category,
        string field,
        int quantity)
    {
        var code = GetSpecificationField(request, field);
        if (!HasText(code) || quantity <= 0)
        {
            return;
        }

        var entry = catalog.ControlPrices.FirstOrDefault(item =>
            (!HasText(category) || EqualsText(item.Category, category))
            && CodeMatches(item.Code, code));
        if (entry is null)
        {
            warnings.Add($"{field} {code}: цена не найдена в таблице Control & Display.");
            return;
        }

        var unitPrice = ReadCatalogPrice(entry.Price);
        if (unitPrice is null)
        {
            warnings.Add($"{field} {code}: цена требует ручной проверки.");
            return;
        }

        AddReadyLine(lines, lineCode, $"{field}: {code}", unitPrice.Value, unitPrice.Value * quantity, quantity);
    }

    private static void AddSmecSpecialButtonLine(
        SmecCatalog catalog,
        PricingCalculationRequest request,
        List<PricingLine> lines,
        List<string> warnings,
        string lineCode,
        string label,
        string field,
        int quantity)
    {
        var code = GetSpecificationField(request, field);
        if (!HasText(code) || quantity <= 0 || code is not ("A71" or "A23" or "A27"))
        {
            return;
        }

        var entry = catalog.ControlPrices.FirstOrDefault(item =>
            EqualsText(item.Category, "Button") && CodeMatches(item.Code, code));
        var unitPrice = entry is null ? null : ReadCatalogPrice(entry.Price);
        if (unitPrice is null)
        {
            warnings.Add($"{label}: цена кнопки {code} не найдена.");
            return;
        }

        AddReadyLine(lines, lineCode, $"{label}: {code}", unitPrice.Value, unitPrice.Value * quantity, quantity);
    }

    private static void AddSmecOptionalFunctions(
        SmecCatalog catalog,
        PricingCalculationRequest request,
        List<PricingLine> lines,
        List<string> warnings)
    {
        var operation = GetSpecificationField(request, "Operation");
        var travelMeters = GetSpecificationNumber(request, "TR") / 1000m;
        foreach (var option in (request.Options ?? []).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var lookupCode = option;
            decimal multiplier = 1;
            if (EqualsText(option, "CWT Safety Gear"))
            {
                var series = request.Series.Contains("ELE", StringComparison.OrdinalIgnoreCase)
                    ? "ELENESSA"
                    : "LEHY";
                var cwtPrice = catalog.CwtPrices.FirstOrDefault(item =>
                    EqualsText(item.Series, series)
                    && request.CapacityKg >= item.MinCapacity
                    && request.CapacityKg <= item.MaxCapacity)?.Price;
                if (cwtPrice is null)
                {
                    warnings.Add($"CWT Safety Gear: цена для {request.Series}, {request.CapacityKg} кг не найдена.");
                    continue;
                }

                AddReadyLine(
                    lines,
                    "function-cwt-safety-gear",
                    $"CWT Safety Gear, {request.CapacityKg} кг",
                    cwtPrice.Value,
                    cwtPrice.Value);
                continue;
            }
            if (EqualsText(option, "AECH") || EqualsText(option, "AHC"))
            {
                multiplier = request.DoorCount;
            }
            else if (EqualsText(option, "ITV"))
            {
                multiplier = travelMeters;
            }
            else if (EqualsText(option, "MELD"))
            {
                lookupCode = GetSpecificationField(request, "Ele Series")?.Contains("LEHY", StringComparison.OrdinalIgnoreCase) == true
                    ? "ELD(LEHY)"
                    : "MELD(ELENESSA)";
            }
            else if (EqualsText(option, "ABP") && !EqualsText(operation, "1C-2BC"))
            {
                continue;
            }

            var unitPrice = FindSmecFunctionPrice(catalog, lookupCode, allowPartialMatch: false);
            if (unitPrice is null)
            {
                warnings.Add($"Функция {option}: цена не найдена, функция пропущена как в KIP.");
                continue;
            }

            var amount = unitPrice.Value * multiplier;
            if (EqualsText(option, "FE"))
            {
                amount += FindSmecFunctionPrice(catalog, "Emergency exit at ceiling") ?? 0;
            }
            AddReadyLine(lines, $"function-{NormalizeCode(option)}", $"Функция {option}", unitPrice.Value, amount);
        }
    }

    private static decimal AddSmecOtherRequirements(
        SmecCatalog catalog,
        PricingCalculationRequest request,
        int handrailQuantity,
        List<PricingLine> lines,
        List<string> warnings)
    {
        decimal doorAddonPrice = 0;
        var requirements = (GetSpecificationField(request, "Other Requirements") ?? "")
            .Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(6);
        foreach (var requirement in requirements)
        {
            decimal? amount = null;
            decimal? unitPrice = null;
            var quantity = 1;
            if (ContainsAny(requirement, "EI60", "EI30"))
            {
                unitPrice = FindSmecDecoration(catalog.Decorations, request, "DoorAddon", "EI120", null)?.Price;
                quantity = request.DoorCount;
                doorAddonPrice = unitPrice ?? 0;
            }
            else if (ContainsAny(requirement, "E60", "E30"))
            {
                unitPrice = FindSmecDecoration(catalog.Decorations, request, "DoorAddon", "E120", null)?.Price;
                quantity = request.DoorCount;
                doorAddonPrice = unitPrice ?? 0;
            }
            else if (ContainsAny(requirement, "ZPKG-050", "ZPKG-150", "ZPKG-200"))
            {
                var code = new[] { "ZPKG-050", "ZPKG-150", "ZPKG-200" }.First(item => requirement.Contains(item, StringComparison.OrdinalIgnoreCase));
                unitPrice = FindSmecDecoration(catalog.Decorations, request, "DoorAddon", code, null)?.Price;
                var carDoorCount = EqualsText(GetSpecificationField(request, "Door type"), "1D1G") ? 1 : 2;
                amount = unitPrice * (request.DoorCount + carDoorCount);
                doorAddonPrice = unitPrice ?? 0;
            }
            else if (requirement.Contains("CWT", StringComparison.OrdinalIgnoreCase))
            {
                var series = GetSpecificationField(request, "Ele Series")?.Contains("LEHY", StringComparison.OrdinalIgnoreCase) == true
                    ? "LEHY"
                    : "ELENESSA";
                unitPrice = catalog.CwtPrices.FirstOrDefault(item =>
                    EqualsText(item.Series, series)
                    && request.CapacityKg >= item.MinCapacity
                    && request.CapacityKg <= item.MaxCapacity)?.Price;
            }
            else if (requirement.Contains("oller", StringComparison.OrdinalIgnoreCase))
            {
                unitPrice = FindSmecFunctionPrice(catalog, "Roller guide shoe");
            }
            else if (requirement.Contains("UV", StringComparison.OrdinalIgnoreCase))
            {
                unitPrice = 220;
                quantity = 8;
            }
            else if (requirement.Contains("ickplate", StringComparison.OrdinalIgnoreCase))
            {
                unitPrice = 1000;
            }
            else if (requirement.Contains("andrail", StringComparison.OrdinalIgnoreCase))
            {
                unitPrice = requirement.Contains("ZDT-500", StringComparison.OrdinalIgnoreCase)
                    ? 400
                    : requirement.Contains("ZDT-50", StringComparison.OrdinalIgnoreCase)
                        ? 700
                        : 300;
                quantity = handrailQuantity;
            }
            else if (requirement.Contains("utton", StringComparison.OrdinalIgnoreCase))
            {
                unitPrice = requirement.Contains("ZDT-50", StringComparison.OrdinalIgnoreCase) ? 300 : 200;
                quantity = request.Stops;
            }
            else if (ContainsAny(requirement, "OH/PD", "PD/OH"))
            {
                unitPrice = 16000;
            }
            else if (requirement.Contains("aceplate", StringComparison.OrdinalIgnoreCase))
            {
                var material = requirement.Contains("SUS-M", StringComparison.OrdinalIgnoreCase) ? "SUS-M" : "SUS-H";
                var titanium = Regex.Match(requirement, @"ZDT-\d{3}", RegexOptions.IgnoreCase).Value.ToUpperInvariant();
                if (titanium == "ZDT-007")
                {
                    titanium = "ZDT-001";
                }
                var code = HasText(titanium) ? $"{titanium} {material}" : material;
                var category = requirement.Contains("COP", StringComparison.OrdinalIgnoreCase) ? "CopFaceplate" : "LopFaceplate";
                unitPrice = FindSmecDecoration(catalog.Decorations, request, category, code, null)?.Price;
                quantity = EqualsText(category, "CopFaceplate")
                    ? HasText(GetSpecificationField(request, "COP 2")) ? 2 : 1
                    : request.DoorCount;
            }
            else if (requirement.Contains("EN81", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Требование «{requirement}»: цена EN81 отсутствует в исходном каталоге 2025 и требует проверки SMEC.");
                continue;
            }
            else
            {
                warnings.Add($"Требование «{requirement}» не распознано KIP-совместимым расчетом.");
                continue;
            }

            amount ??= unitPrice * quantity;
            if (unitPrice is null || amount is null)
            {
                warnings.Add($"Требование «{requirement}»: цена не найдена.");
                continue;
            }
            AddReadyLine(lines, $"requirement-{NormalizeCode(requirement)}", requirement, unitPrice.Value, amount.Value, quantity);
        }
        return doorAddonPrice;
    }

    private static void AddSmecSpecialConditions(
        SmecCatalog catalog,
        PricingCalculationRequest request,
        SmecBasePrice? baseEntry,
        SmecCalculationComponents components,
        decimal doorAddonPrice,
        List<PricingLine> lines,
        List<string> warnings)
    {
        if (request.Series.Contains("ELE", StringComparison.OrdinalIgnoreCase)
            && request.CapacityKg <= 1050
            && request.Speed < 2)
        {
            var pmf = FindSmecFunctionPrice(catalog, "Special type of traction machine");
            if (pmf is not null)
            {
                AddReadyLine(lines, "pmf", "Специальная тяговая машина PMF", pmf.Value, pmf.Value);
            }
        }

        var carHeight = GetSpecificationNumber(request, "HL");
        if (carHeight > 2400)
        {
            if (components.CarSideWall == 0 || components.CarFrontWall == 0 || components.CarDoor == 0)
            {
                warnings.Add("HL > 2400: надбавка не рассчитана полностью из-за отсутствующей цены отделки кабины.");
            }
            else
            {
                var amount = ((components.CarSideWall + components.CarFrontWall + components.CarDoor) * 0.05m + 3150m)
                    * (carHeight - 2400m) / 100m;
                AddReadyLine(lines, "car-height", $"Высота кабины HL {carHeight:0} мм", amount, amount);
            }
        }

        var doorHeight = GetSpecificationNumber(request, "HH");
        if (doorHeight > 2100)
        {
            var doorCount = Math.Max(0, request.DoorCount);
            var landingAmount = doorAddonPrice * doorCount
                + components.MainShaftDoor
                + components.OtherShaftDoor * Math.Max(0, doorCount - 1)
                + components.MainJamb
                + components.OtherJamb * Math.Max(0, doorCount - 1);
            var amount = (landingAmount * 0.05m + 320m) * (doorHeight - 2100m) / 100m;
            AddReadyLine(lines, "door-height", $"Высота дверей HH {doorHeight:0} мм", amount, amount);
        }

        if (components.CageIsPxx
            && baseEntry?.BasicPrice is not null
            && TryReadDecimal(baseEntry.BasicPrice, out var basePrice))
        {
            var amount = basePrice * 0.04m;
            AddReadyLine(lines, "pxx", "Нестандартная кабина PXX", amount, amount);
        }
    }

    private static decimal? FindSmecFunctionPrice(
        SmecCatalog catalog,
        string code,
        bool allowPartialMatch = true)
    {
        var entry = catalog.Functions.FirstOrDefault(item => CodeMatches(item.Code, code));
        if (entry is null && allowPartialMatch)
        {
            entry = catalog.Functions.FirstOrDefault(item =>
                item.Code.Contains(code, StringComparison.OrdinalIgnoreCase));
        }
        return entry is null ? null : ReadCatalogPrice(entry.Price);
    }

    private static decimal? ReadCatalogPrice(object? rawValue)
    {
        if (rawValue is null)
        {
            return null;
        }

        if (TryReadDecimal(rawValue, out var value))
        {
            return value;
        }

        return ParseFirstDecimal(Convert.ToString(rawValue, CultureInfo.InvariantCulture));
    }

    private static decimal? ParseFirstDecimal(string? value)
    {
        if (!HasText(value))
        {
            return null;
        }

        var match = Regex.Match(value!, @"-?\d+(?:[.,]\d+)?");
        return match.Success
            && decimal.TryParse(
                match.Value.Replace(",", ".", StringComparison.Ordinal),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var result)
            ? result
            : null;
    }

    private static void AddReadyLine(
        List<PricingLine> lines,
        string code,
        string label,
        decimal unitPrice,
        decimal amount,
        int quantity = 1)
    {
        lines.Add(new PricingLine(code, label, quantity, unitPrice, amount, "ready"));
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static bool CodeMatches(string? value, string? expected)
    {
        return NormalizeCode(value) == NormalizeCode(expected);
    }

    private static string NormalizeCode(string? value)
    {
        return Regex.Replace(
            (value ?? "")
                .Replace("■", "", StringComparison.Ordinal)
                .Replace("●", "", StringComparison.Ordinal)
                .ToUpperInvariant(),
            @"[^A-ZА-Я0-9]+",
            "");
    }

    private static SmecDecorationPrice? FindSmecDecoration(
        IReadOnlyList<SmecDecorationPrice> decorations,
        PricingCalculationRequest request,
        string category,
        string code,
        string? variant)
    {
        var capacity = category is "Jamb" or "CopFaceplate" or "LopFaceplate"
            ? 0
            : request.CapacityKg;
        var candidates = decorations
            .Where(item =>
                EqualsText(item.Category, category)
                && EqualsText(item.Code, code)
                && item.Capacity == capacity)
            .ToArray();
        if (candidates.Length == 0)
        {
            return null;
        }

        if (HasText(variant))
        {
            return candidates.FirstOrDefault(item => EqualsText(item.Variant, variant))
                ?? candidates.OrderByDescending(item => item.Price).First();
        }

        if (candidates.Length == 1)
        {
            return candidates[0];
        }

        var carWidth = GetSpecificationNumber(request, "AA");
        var carDepth = GetSpecificationNumber(request, "BB");
        var suffix = carDepth > carWidth
            ? "D"
            : carWidth > carDepth
                ? "W"
                : null;
        if (suffix is not null)
        {
            var oriented = candidates.FirstOrDefault(item => VariantContainsSuffix(item.Variant, suffix));
            if (oriented is not null)
            {
                return oriented;
            }
        }

        return candidates.OrderByDescending(item => item.Price).First();
    }

    private static bool VariantContainsSuffix(string? variant, string suffix)
    {
        return (variant ?? "")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(item => item.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    private static int GetHandrailQuantity(string? position)
    {
        if (position?.Contains("three", StringComparison.OrdinalIgnoreCase) == true
            || position?.TrimStart().StartsWith("3", StringComparison.Ordinal) == true)
        {
            return 3;
        }

        if (position?.Contains("two", StringComparison.OrdinalIgnoreCase) == true
            || position?.TrimStart().StartsWith("2", StringComparison.Ordinal) == true)
        {
            return 2;
        }

        return 1;
    }

    private static string? GetSpecificationField(PricingCalculationRequest request, string name)
    {
        if (request.SpecificationFields is null)
        {
            return null;
        }

        return request.SpecificationFields
            .FirstOrDefault(item => EqualsText(item.Key, name))
            .Value;
    }

    private static decimal GetSpecificationNumber(PricingCalculationRequest request, string name)
    {
        var value = GetSpecificationField(request, name);
        return decimal.TryParse(
            value?.Replace(",", ".", StringComparison.Ordinal),
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var result)
            ? result
            : 0;
    }

    private static void AddCatalogValue(
        List<PricingLine> lines,
        List<string> warnings,
        List<string> blockers,
        string code,
        string label,
        object? rawValue,
        bool blockNegativeOne,
        int quantity = 1,
        int sign = 1)
    {
        if (rawValue is null)
        {
            warnings.Add($"{label}: цена не найдена в прайсе, требуется ручная проверка.");
            lines.Add(new PricingLine(code, label, quantity, null, null, "warning"));
            return;
        }

        if (TryReadDecimal(rawValue, out var value))
        {
            if (blockNegativeOne && value == -1m)
            {
                blockers.Add($"{label}: комбинация недоступна в прайсе XIZI.");
                lines.Add(new PricingLine(code, label, quantity, value, null, "blocked"));
                return;
            }

            var amount = value * quantity * sign;
            lines.Add(new PricingLine(code, label, quantity, value, amount, "ready"));
            return;
        }

        var text = Convert.ToString(rawValue, CultureInfo.InvariantCulture) ?? "";
        var formulaPrice = TryEvaluateStopsFormula(text, quantity);
        if (formulaPrice is not null)
        {
            lines.Add(new PricingLine(code, label, quantity, formulaPrice.Value / Math.Max(1, quantity), formulaPrice.Value, "ready"));
            return;
        }

        warnings.Add($"{label}: значение прайса \"{text}\" требует ручной проверки.");
        lines.Add(new PricingLine(code, label, quantity, null, null, "warning"));
    }

    private static decimal? TryEvaluateStopsFormula(string text, int stops)
    {
        var normalized = text
            .Replace("¥", "", StringComparison.Ordinal)
            .Replace("×", "*", StringComparison.Ordinal)
            .Replace("x", "*", StringComparison.OrdinalIgnoreCase)
            .Trim();
        var parts = normalized.Split('*', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2
            && decimal.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            && parts[1].Contains("stop", StringComparison.OrdinalIgnoreCase))
        {
            return value * stops;
        }

        return null;
    }

    private static bool TryReadDecimal(object value, out decimal result)
    {
        switch (value)
        {
            case decimal decimalValue:
                result = decimalValue;
                return true;
            case double doubleValue:
                result = (decimal)doubleValue;
                return true;
            case int intValue:
                result = intValue;
                return true;
            case long longValue:
                result = longValue;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.Number:
                return element.TryGetDecimal(out result);
            case JsonElement element when element.ValueKind == JsonValueKind.String:
                return decimal.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out result);
            default:
                return decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out result);
        }
    }

    private static bool SeriesMatches(string value, string expected)
    {
        return EqualsText(value, expected)
            || value.Contains(expected, StringComparison.OrdinalIgnoreCase)
            || expected.Contains(value, StringComparison.OrdinalIgnoreCase)
            || (expected.StartsWith("ELE-", StringComparison.OrdinalIgnoreCase)
                && value.Contains("ELENESSA", StringComparison.OrdinalIgnoreCase));
    }

    private static bool SameNumber(decimal left, decimal right)
    {
        return Math.Abs(left - right) < 0.001m;
    }

    private static bool EqualsText(string? left, string? right)
    {
        return string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasText(string? value)
    {
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string FormatMoney(decimal value)
    {
        return value.ToString("N2", CultureInfo.GetCultureInfo("ru-RU"));
    }
}

public sealed record PricingCatalogSummary(
    string Currency,
    string GeneratedAt,
    IReadOnlyList<string> XiziSeries,
    IReadOnlyList<string> SmecSeries,
    IReadOnlyList<int> XiziCapacities,
    IReadOnlyList<decimal> XiziSpeeds,
    IReadOnlyList<int> DoorWidths,
    IReadOnlyList<string> DoorManufacturers,
    IReadOnlyList<string> DoorTypes,
    IReadOnlyList<PriceEntry> XiziDecorations,
    IReadOnlyList<PriceEntry> XiziOptions,
    IReadOnlyList<SmecVisualEntry> XiziVisualItems,
    IReadOnlyList<SpecificationChoiceGroup> XiziChoiceGroups,
    IReadOnlyList<int> SmecCapacities,
    IReadOnlyList<decimal> SmecSpeeds,
    IReadOnlyList<PriceEntry> SmecFunctions,
    IReadOnlyList<PriceEntry> SmecGroupControl,
    IReadOnlyList<SmecCarDesignEntry> SmecCarDesigns,
    IReadOnlyList<SmecVisualEntry> SmecVisualItems,
    IReadOnlyList<SmecPowerEntry> SmecPower,
    IReadOnlyList<SmecSpecField> SmecSpecFields,
    IReadOnlyList<SpecificationChoiceGroup> SmecChoiceGroups,
    IReadOnlyList<SmecFloorPatternGroup> SmecFloorPatterns);

public sealed record PricingCalculationRequest(
    string Supplier,
    string Series,
    int CapacityKg,
    decimal Speed,
    int Stops,
    int DoorWidthMm,
    string? DoorType,
    string? DoorManufacturer,
    int DoorCount,
    int ExtraHeightMm,
    string? DecorationCode,
    IReadOnlyList<string>? Options,
    bool Efs,
    bool E312,
    string? TargetCurrency,
    string? ProjectId,
    string? ProjectConfigurationId,
    IReadOnlyDictionary<string, string>? SpecificationFields,
    string? Name);

public sealed record PricingCalculationResult(
    string Status,
    string Supplier,
    string Series,
    string BaseCurrency,
    string TargetCurrency,
    decimal ExchangeRate,
    string ExchangeRateSource,
    decimal TotalCny,
    decimal TotalConverted,
    IReadOnlyList<PricingLine> Lines,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Blockers,
    ContainerInfo? Container,
    DateTimeOffset CalculatedAt);

public sealed record PricingLine(
    string Code,
    string Label,
    int Quantity,
    decimal? UnitPriceCny,
    decimal? AmountCny,
    string Status);

public sealed record ContainerInfo(string? Code, string Label);

public sealed record CurrencyRateResult(string TargetCurrency, decimal Rate, string Source, string? Warning);

public sealed class PricingCatalog
{
    public string GeneratedAt { get; set; } = "";
    public string Currency { get; set; } = "CNY";
    public XiziCatalog Xizi { get; set; } = new();
    public SmecCatalog Smec { get; set; } = new();
}

public sealed class XiziCatalog
{
    public List<string> Series { get; set; } = [];
    public List<XiziBasePrice> BasePrices { get; set; } = [];
    public List<XiziDoorPrice> Doors { get; set; } = [];
    public List<PriceEntry> Decorations { get; set; } = [];
    public List<PriceEntry> Options { get; set; } = [];
    public List<PriceEntry> LocalRequirements { get; set; } = [];
    public List<XiziContainerEntry> Containers { get; set; } = [];
    public List<SmecVisualEntry> VisualItems { get; set; } = [];
    public List<SpecificationChoiceGroup> ChoiceGroups { get; set; } = [];
}

public sealed class SmecCatalog
{
    public List<string> Series { get; set; } = [];
    public List<SmecBasePrice> BasePrices { get; set; } = [];
    public List<SmecDecorationPrice> Decorations { get; set; } = [];
    public List<PriceEntry> Functions { get; set; } = [];
    public List<PriceEntry> GroupControl { get; set; } = [];
    public List<PriceEntry> ControlPrices { get; set; } = [];
    public List<SmecCwtPrice> CwtPrices { get; set; } = [];
    public List<SmecContainerEntry> Containers { get; set; } = [];
    public List<SmecCarDesignEntry> CarDesigns { get; set; } = [];
    public List<SmecVisualEntry> VisualItems { get; set; } = [];
    public List<SmecPowerEntry> Power { get; set; } = [];
    public List<SmecSpecField> SpecFields { get; set; } = [];
    public List<SpecificationChoiceGroup> ChoiceGroups { get; set; } = [];
    public List<SmecFloorPatternGroup> FloorPatterns { get; set; } = [];
}

public sealed record XiziBasePrice(
    string Series,
    int Capacity,
    decimal Speed,
    int Stops,
    JsonElement? Price,
    JsonElement? ExtraRisePerMeter = null,
    string? StandardDoorOpening = null);

public sealed record XiziDoorPrice(
    string Manufacturer,
    string? Part,
    string? DoorType,
    string? FireRating,
    string? Finish,
    int Capacity,
    string Floor,
    int Width,
    JsonElement? Price);

public sealed record PriceEntry(
    string? Category,
    string Code,
    JsonElement? Price,
    JsonElement? Overprice = null,
    JsonElement? Height = null,
    string? Description = null,
    bool? IsStandard = null,
    string? ImageUrl = null,
    IReadOnlyList<JsonElement?>? Prices = null);

public sealed record SmecCarDesignEntry(
    string Code,
    string? WallDescription,
    string? DoorDescription,
    JsonElement? LastParagraph,
    string? ImageUrl);

public sealed record SmecVisualEntry(string Code, string ImageUrl, string? Description = null);

public sealed record SmecPowerEntry(string Series, int Capacity, decimal Speed, decimal Power);

public sealed record SmecSpecField(string Group, string Label, int Row);

public sealed record SmecDecorationPrice(
    string Category,
    string Code,
    int Capacity,
    string? Variant,
    decimal Price);

public sealed record SpecificationChoiceGroup(
    string Name,
    string SourceSheet,
    IReadOnlyList<string> Cells,
    IReadOnlyList<string> Options);

public sealed record SmecFloorPatternGroup(string FloorType, IReadOnlyList<string> Options);

public sealed record SmecCwtPrice(string Series, int MinCapacity, int MaxCapacity, decimal Price);

public sealed record XiziContainerEntry(int Capacity, int Stops, string Container, JsonElement? Fraction);

public sealed record SmecBasePrice(
    string Series,
    int Capacity,
    decimal Speed,
    int BasicStops,
    JsonElement? BasicPrice,
    JsonElement? PricePerStop,
    JsonElement? OverHeightPer1000,
    JsonElement? PricePerDoor2D2G);

public sealed record SmecContainerEntry(
    int Capacity,
    int Stops,
    string? Efs,
    string? E312,
    string? Single,
    JsonElement? Mix,
    string? MixUnit);

internal sealed record SmecCalculationComponents(
    decimal CarSideWall,
    decimal CarFrontWall,
    decimal CarDoor,
    decimal MainJamb,
    decimal OtherJamb,
    decimal MainShaftDoor,
    decimal OtherShaftDoor,
    int HandrailQuantity,
    bool CageIsPxx);

internal static class MinimalDocx
{
    public static byte[] Create(IEnumerable<string> paragraphs)
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
                </Types>
                """);
            WriteEntry(archive, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            WriteEntry(archive, "word/document.xml", BuildDocumentXml(paragraphs));
        }

        return output.ToArray();
    }

    private static string BuildDocumentXml(IEnumerable<string> paragraphs)
    {
        var body = new StringBuilder();
        foreach (var paragraph in paragraphs)
        {
            body.Append("<w:p><w:r><w:t xml:space=\"preserve\">")
                .Append(System.Security.SecurityElement.Escape(paragraph) ?? "")
                .Append("</w:t></w:r></w:p>");
        }

        return $$"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>{{body}}<w:sectPr/></w:body>
            </w:document>
            """;
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content.Trim());
    }
}
