using System.Globalization;
using System.Text.Json;
using TFlexDrawingService.Core.Abstractions;
using TFlexDrawingService.Core.Models;
using TFlexDrawingService.Core.Requests;

namespace TFlexDrawingService.Api.Data;

/// <summary>Builds supplier export specifications from saved prices and drawing-only project configurations.</summary>
public static class ProjectFactoryExportSpecifications
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<IReadOnlyList<PricingSpecification>> BuildAsync(
        IReadOnlyList<ProjectConfiguration> configurations,
        IReadOnlyList<PricingSpecification> savedSpecifications,
        ITemplateCatalog templateCatalog,
        IDrawingRequestValidator validator,
        string supplier,
        CancellationToken cancellationToken = default)
    {
        if (!PricingCatalogStore.IsSupportedSupplier(supplier)) throw new ArgumentException("Supplier must be XIZI or SMEC.", nameof(supplier));
        var configurationsById = configurations.ToDictionary(configuration => configuration.Id, StringComparer.Ordinal);
        var supplierSpecifications = savedSpecifications.Where(specification => string.Equals(specification.Supplier, supplier, StringComparison.OrdinalIgnoreCase)).ToArray();
        var newestLinked = supplierSpecifications
            .Where(specification => !string.IsNullOrWhiteSpace(specification.ProjectConfigurationId)
                && configurationsById.ContainsKey(specification.ProjectConfigurationId))
            .GroupBy(specification => specification.ProjectConfigurationId!, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(specification => specification.UpdatedAt).First())
            .ToArray();
        var legacyNames = supplierSpecifications
            .Where(specification => string.IsNullOrWhiteSpace(specification.ProjectConfigurationId))
            .GroupBy(specification => NormalizeName(specification.Name), StringComparer.Ordinal)
            .Where(group => group.Key.Length > 0 && configurations.Count(configuration => NormalizeName(configuration.Name) == group.Key) == 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        var saved = newestLinked.Concat(supplierSpecifications.Where(specification => string.IsNullOrWhiteSpace(specification.ProjectConfigurationId)
            || !configurationsById.ContainsKey(specification.ProjectConfigurationId))).ToArray();
        var linkedIds = newestLinked.Select(specification => specification.ProjectConfigurationId!).ToHashSet(StringComparer.Ordinal);
        var result = new List<PricingSpecification>(saved);

        foreach (var configuration in configurations)
        {
            if (linkedIds.Contains(configuration.Id) || legacyNames.Contains(NormalizeName(configuration.Name))) continue;
            var template = await templateCatalog.GetByIdOrCodeAsync(configuration.TemplateId, cancellationToken)
                ?? throw new InvalidDataException($"Drawing configuration '{configuration.Name}' uses missing template '{configuration.TemplateId}'.");
            var supplierModel = SupplierModel(template.Id, template.Code);
            if (supplierModel is null) continue;
            if (!string.Equals(supplierModel.Value.Supplier, supplier, StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(await FromDrawingAsync(configuration, templateCatalog, validator, cancellationToken));
        }

        return result;
    }

    private static async Task<PricingSpecification> FromDrawingAsync(
        ProjectConfiguration configuration,
        ITemplateCatalog templateCatalog,
        IDrawingRequestValidator validator,
        CancellationToken cancellationToken)
    {
        var template = await templateCatalog.GetByIdOrCodeAsync(configuration.TemplateId, cancellationToken)
            ?? throw new InvalidDataException($"Drawing configuration '{configuration.Name}' uses missing template '{configuration.TemplateId}'.");
        var supplierModel = SupplierModel(template.Id, template.Code)
            ?? throw new InvalidDataException($"Drawing configuration '{configuration.Name}' has no XIZI or SMEC factory export mapping.");
        var parameters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(configuration.ParametersJson, Json)
            ?? throw new InvalidDataException($"Drawing configuration '{configuration.Name}' has unreadable parameters.");
        var validation = await validator.ValidateAsync(new CreateDrawingJobRequest
        {
            TemplateId = configuration.TemplateId,
            OutputFormat = configuration.OutputFormat,
            Parameters = parameters
        }, cancellationToken);
        if (!validation.IsValid || validation.Template is null)
            throw new InvalidDataException($"Drawing configuration '{configuration.Name}' cannot be exported: {string.Join(" ", validation.Errors)}");

        var values = validation.NormalizedParameters;
        var capacity = Integer(Get(values, "cap", "DLOAD", "Q", "CAP", "Груз."));
        if (capacity <= 0) capacity = CapacityFromCarType(Text(Get(values, "$CARTYPE_MENU")));
        var speed = Decimal(Get(values, $"$speed_{capacity:0000}", "SPEED", "speed", "V", "$V", "Скорость"));
        var stops = Integer(Get(values, "stops", "NBLD", "Stops", "$N", "Floors_num", "Остановки"));
        var doorWidth = Integer(Get(values, "JJ", "OP"));
        if (supplierModel.Supplier == "XIZI" && (capacity <= 0 || speed <= 0 || stops <= 0 || doorWidth <= 0))
            throw new InvalidDataException($"Drawing configuration '{configuration.Name}' is missing capacity, speed, stops, or door width required for factory export.");

        var rawDoorType = Text(Get(values, "$door_type", "door_type", "$DOOR_MENU", "Door type"));
        var doors = ResolveDoorCount(parameters, values, validation.Template);
        if (supplierModel.Supplier == "XIZI" && doors <= 0) throw new InvalidDataException($"Drawing configuration '{configuration.Name}' has no landing doors for factory export.");
        var travel = TravelHeightMillimeters(Get(values, "TR", "$R"));
        var fields = Fields(configuration.Name, values, validation.Template, supplierModel.Supplier, travel, doors, rawDoorType);
        var options = DrawingOptions(values);
        var request = new PricingCalculationRequest(
            supplierModel.Supplier, supplierModel.Series, capacity, speed, stops, doorWidth,
            DoorType(rawDoorType), null, doors, 0, null, options, HasEfs(values), false, "CNY",
            configuration.ProjectId, configuration.Id, fields, configuration.Name);
        return new PricingSpecification(
            $"drawing-{configuration.Id}", configuration.ProjectId, configuration.Id, configuration.Name,
            supplierModel.Supplier, supplierModel.Series, "drawing-only", 0, "CNY", 0,
            JsonSerializer.Serialize(request, Json), "null", configuration.CreatedAt, configuration.UpdatedAt);
    }

    private static IReadOnlyDictionary<string, string> Fields(string name, IReadOnlyDictionary<string, object?> values, DrawingTemplate template, string supplier, decimal? travel, int doors, string rawDoorType)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Lift No"] = name,
            ["Quantity"] = Text(Get(values, "Quantity", "N"), "1"), ["Floors"] = Text(Get(values, "stops", "NBLD", "Stops", "$N", "Floors_num", "Остановки")),
            ["AH"] = Text(Get(values, "AH", "AH_1", "HW")), ["BH"] = Text(Get(values, "BH", "BH_1", "WTW")),
            ["TR"] = travel?.ToString("0.###", CultureInfo.InvariantCulture) ?? "", ["OH"] = Text(Get(values, "OH", "OH_1", "K")),
            ["PD"] = Text(Get(values, "PD", "PD_1", "S")), ["JJ"] = Text(Get(values, "JJ", "OP")),
            ["HH"] = Text(Get(values, "HH", "OPH")), ["AA"] = Text(Get(values, "AA", "CW")),
            ["BB"] = Text(Get(values, "BB", "CD")), ["HL"] = Text(Get(values, "HL", "CH")),
            ["Door mode"] = DoorMode(rawDoorType),
            ["Other Requirements"] = KnownRequirements(template, values)
        };
        var car = CarDimensions(Text(Get(values, "$CARTYPE_MENU")));
        if (string.IsNullOrWhiteSpace(fields["AA"])) fields["AA"] = car.Width;
        if (string.IsNullOrWhiteSpace(fields["BB"])) fields["BB"] = car.Depth;
        var entrances = Math.Max(1, Integer(Get(values, "NE", "NBENT_MENU", "Entrances", "Входы")));
        fields["Car Type"] = entrances > 1 ? "Проходная" : "Непроходная";
        fields["Door type"] = DoorArrangement(Text(Get(values, "$DoorsType")), entrances);
        fields["Fire Rating"] = Text(Get(values, "$fire_rating", "$fire_rating_1", "fire_rating", "Fire rating"));
        fields["CWT Safety Gear"] = HasCwtSafetyGear(values) ? "Yes" : "";
        var knownFinish = Text(Get(values, "$CEILTYPE", "$CeilingType", "Ceiling"));
        if (!string.IsNullOrWhiteSpace(knownFinish)) fields["Ceiling"] = knownFinish;
        fields["Floor Type"] = Text(Get(values, "$FloorType", "Floor Type"));
        fields["COP"] = Text(Get(values, "$COP", "COP"));
        fields["COP Button"] = Text(Get(values, "$Button", "COP Button"));
        fields["Decoration Weight"] = Text(Get(values, "DecorWeight", "Decoration Weight"));
        fields["Wall"] = Text(Get(values, "$DecorCode", "Wall"));
        if (supplier == "XIZI")
        {
            fields["Shaft Width"] = fields["AH"]; fields["Shaft Depth"] = fields["BH"]; fields["Travel Height"] = fields["TR"];
            fields["Overhead"] = fields["OH"]; fields["Pit"] = fields["PD"]; fields["Door Width"] = fields["JJ"];
            fields["Door Height"] = fields["HH"]; fields["Car Width"] = fields["AA"]; fields["Car Depth"] = fields["BB"]; fields["Car Height"] = fields["HL"];
            var stops = Integer(Get(values, "stops", "NBLD", "Stops", "$N", "Floors_num", "Остановки"));
            fields["Front Doors"] = stops.ToString(CultureInfo.InvariantCulture);
            fields["Rear Doors"] = entrances > 1 ? stops.ToString(CultureInfo.InvariantCulture) : "0";
            fields["Door Opening"] = DoorMode(rawDoorType);
        }
        return fields;
    }

    private static (string Supplier, string Series)? SupplierModel(params string[] ids)
    {
        var key = string.Join(" ", ids).ToLowerInvariant().Replace('-', '_');
        if (key.Contains("un_victor_mrl_t", StringComparison.Ordinal)) return ("XIZI", "UN-Victor MRL(T)");
        if (key.Contains("un_victor_mrl", StringComparison.Ordinal)) return ("XIZI", "UN-Victor MRL");
        if (key.Contains("lehy_l_pro", StringComparison.Ordinal)) return ("SMEC", "LEHY-L-Pro");
        if (key.Contains("lehy_pro", StringComparison.Ordinal)) return ("SMEC", "LEHY-Pro");
        if (key.Contains("k_ii", StringComparison.Ordinal)) return ("SMEC", "K-II");
        return null;
    }

    private static int ResolveDoorCount(IReadOnlyDictionary<string, JsonElement> stored, IReadOnlyDictionary<string, object?> values, DrawingTemplate template)
    {
        var explicitCount = Integer(Get(stored, "Doors", "Двери", "doorCount"));
        if (explicitCount > 0) return explicitCount;
        var rawStops = Integer(Get(values, "stops", "NBLD", "Stops", "$N", "Floors_num", "Остановки"));
        if (rawStops <= 0) return 0;
        var stops = Math.Clamp(rawStops, 1, 48);
        var entrances = Integer(Get(values, "NE", "NBENT_MENU", "Entrances", "Входы"));
        if (entrances <= 0) entrances = Text(Get(values, "$DoorsType")).Contains("2G", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
        if (entrances == 1) return stops;
        var definitions = template.Parameters.ToDictionary(definition => definition.Name, StringComparer.OrdinalIgnoreCase);
        if (entrances != 2 || !definitions.Keys.Any(name => System.Text.RegularExpressions.Regex.IsMatch(name, "^s(?:\\d{2}|_top)_(?:front|rear)_1$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))) return stops * entrances;
        var count = 1 + Flag(Get(stored, "s_top_rear_1"), definitions.GetValueOrDefault("s_top_rear_1")?.DefaultValue);
        for (var index = 1; index < stops; index++)
        {
            var prefix = $"s{index:00}";
            count += Flag(Get(stored, $"{prefix}_front_1"), definitions.GetValueOrDefault($"{prefix}_front_1")?.DefaultValue);
            count += Flag(Get(stored, $"{prefix}_rear_1"), definitions.GetValueOrDefault($"{prefix}_rear_1")?.DefaultValue);
        }
        return count;
    }

    private static int Flag(JsonElement? stored, JsonElement? fallback) => stored is { } storedValue && HasValue(storedValue) ? Flag(storedValue) : fallback is { } fallbackValue ? Flag(fallbackValue) : 0;
    private static int Flag(JsonElement value) => value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) && number != 0 || value.ValueKind == JsonValueKind.String && new[] { "1", "true", "да", "yes" }.Contains(value.GetString()?.Trim().ToLowerInvariant()) ? 1 : 0;
    private static bool HasValue(JsonElement value) => value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined && (value.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(value.GetString()));
    private static object? Get(IReadOnlyDictionary<string, object?> values, params string[] names)
    {
        foreach (var name in names) if (values.TryGetValue(name, out var exact) && exact is not null && !string.IsNullOrWhiteSpace(exact.ToString())) return exact;
        return names.Select(name => values.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).Value).FirstOrDefault(value => value is not null && !string.IsNullOrWhiteSpace(value.ToString()));
    }
    private static JsonElement? Get(IReadOnlyDictionary<string, JsonElement> values, params string[] names)
    {
        foreach (var name in names) if (values.TryGetValue(name, out var exact) && HasValue(exact)) return exact;
        return names.Select(name => values.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).Value).FirstOrDefault(HasValue);
    }
    private static int Integer(object? value) => decimal.ToInt32(decimal.Round(Decimal(value)));
    private static decimal Decimal(object? value) => decimal.TryParse(Text(value).Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : 0;
    private static decimal? TravelHeightMillimeters(object? value) { var number = Decimal(value); return number > 0 ? number < 1000 ? number * 1000 : number : null; }
    private static string DoorType(string value) => value.StartsWith("то", StringComparison.OrdinalIgnoreCase) || value.StartsWith("2s", StringComparison.OrdinalIgnoreCase) || value.StartsWith("tld", StringComparison.OrdinalIgnoreCase) || value.StartsWith("telescopic", StringComparison.OrdinalIgnoreCase) || value.StartsWith("side", StringComparison.OrdinalIgnoreCase) ? "2S" : "CO";
    private static string DoorMode(string value) => DoorType(value) == "2S" ? "Side opening" : "Central opening";
    private static string Text(object? value, string fallback = "") => value is null ? fallback : Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() is { Length: > 0 } text ? text : fallback;
    private static int CapacityFromCarType(string value)
    {
        var parts = value.Split('/');
        return parts.Length > 1 ? Integer(parts[1]) : 0;
    }
    private static (string Width, string Depth) CarDimensions(string value)
    {
        var part = value.Split('/').LastOrDefault() ?? "";
        var dimensions = part.Split('×');
        return dimensions.Length == 2 ? (dimensions[0].Trim(), dimensions[1].Trim()) : ("", "");
    }
    private static string DoorArrangement(string value, int entrances) => value.Replace("-", "", StringComparison.Ordinal) switch
    {
        "1D1G" or "1D2G" or "2D2G" => value.Replace("-", "", StringComparison.Ordinal),
        _ => entrances > 1 ? "1D2G" : "1D1G"
    };
    private static string KnownRequirements(DrawingTemplate template, IReadOnlyDictionary<string, object?> values)
    {
        var known = template.Parameters.Where(parameter => !parameter.IsReadOnly)
            .Select(parameter => (Label: string.IsNullOrWhiteSpace(parameter.DisplayName) ? parameter.Name : parameter.DisplayName,
                Value: parameter.AllowedValueLabels.GetValueOrDefault(Text(Get(values, parameter.Name)), Text(Get(values, parameter.Name)))))
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => $"{item.Label}: {item.Value}");
        return string.Join("\n", known.Append("Any finishes and functions not present in the drawing require factory confirmation."));
    }
    private static IReadOnlyList<string> DrawingOptions(IReadOnlyDictionary<string, object?> values)
    {
        var options = new List<string> { "Finishes and optional functions not specified in the drawing require confirmation." };
        var ceiling = Text(Get(values, "$CEILTYPE", "Ceiling"));
        if (!string.IsNullOrWhiteSpace(ceiling)) options.Add($"Ceiling from drawing: {ceiling}");
        if (HasEfs(values)) options.Add("EFS2");
        if (HasCwtSafetyGear(values)) options.Add("CWTSAFETY");
        return options;
    }
    private static bool HasEfs(IReadOnlyDictionary<string, object?> values) => Text(Get(values, "$EFS", "EFS")).Equals("EFS2", StringComparison.OrdinalIgnoreCase);
    private static bool HasCwtSafetyGear(IReadOnlyDictionary<string, object?> values) => Text(Get(values, "$CWT", "$cwt_sg", "cwt_sg")).Equals("WSAFE", StringComparison.OrdinalIgnoreCase) || FlagText(Text(Get(values, "$cwt_sg", "cwt_sg")));
    private static bool FlagText(string value) => new[] { "1", "true", "yes", "да" }.Contains(value.Trim().ToLowerInvariant());
    private static string NormalizeName(string value) => System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"\s+", " ").ToUpperInvariant();
}
