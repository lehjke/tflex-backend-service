using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Core.Requests;
using TFlexDrawingService.Core.Services;
using TFlexDrawingService.Infrastructure.Configuration;
using TFlexDrawingService.Infrastructure.Storage;

namespace TFlexDrawingService.Tests;

public sealed class JsonTemplateCatalogTests
{
    [Fact]
    public async Task ProductionCatalogContainsCompleteLehyProRearCwtTemplate()
    {
        var repositoryRoot = FindRepositoryRoot();
        var catalog = new JsonTemplateCatalog(
            Options.Create(new TemplateCatalogOptions
            {
                ProjectRootPath = repositoryRoot,
                ConfigPath = Path.Combine(repositoryRoot, "templates", "templates.json")
            }),
            NullLogger<JsonTemplateCatalog>.Instance);

        var template = await catalog.GetByIdOrCodeAsync("lehy_pro_rear_cwt");

        Assert.NotNull(template);
        Assert.Equal("LEHY-PRO [REAR CWT]", template.Name);
        Assert.True(File.Exists(template.TemplateFilePath));
        Assert.Equal(318, template.Parameters.Count);
        Assert.Equal(756, template.CalculatedVariables.Count);
        Assert.Equal(30, template.ValidationRules.Count);
        Assert.Equal(12, template.LookupTables["TH"].Count);
        Assert.Equal(49, template.LookupTables["OH"].Count);
        Assert.Contains(template.ValidationRules, rule => rule.Name == "r_CJ_1");
        Assert.Contains(template.ValidationRules, rule => rule.Name == "r_CJ_2");

        var entrances = Assert.Single(template.Parameters, parameter => parameter.Name == "NE");
        Assert.Equal(["1", "2"], entrances.AllowedValues);
        Assert.Equal("Кабина / Количество входов", entrances.DisplayName);

        var doorDirection = Assert.Single(template.Parameters, parameter => parameter.Name == "$s_1");
        Assert.Equal("Двери / Открывание дверей", doorDirection.DisplayName);
        Assert.Equal(["Налево", "Направо"], doorDirection.AllowedValues);

        var manualShaftOffset = Assert.Single(template.Parameters, parameter => parameter.Name == "CB_1");
        Assert.Equal(
            "Шахта / От оси кабины до правой стены шахты",
            manualShaftOffset.DisplayName);

        var p14r = Assert.Single(template.LookupTables["TH"], row =>
            row["cap"].GetInt32() == 1050
            && row["car_type"].GetString() == "P14R");
        Assert.Equal(2100, p14r["AA"].GetInt32());
        Assert.Equal(1100, p14r["BB"].GetInt32());
    }

    [Fact]
    public async Task ProductionCatalog_DefaultContextsHaveEvaluableValidationRules()
    {
        var repositoryRoot = FindRepositoryRoot();
        var catalog = new JsonTemplateCatalog(
            Options.Create(new TemplateCatalogOptions
            {
                ProjectRootPath = repositoryRoot,
                ConfigPath = Path.Combine(repositoryRoot, "templates", "templates.json")
            }),
            NullLogger<JsonTemplateCatalog>.Instance);
        var failures = new List<string>();

        foreach (var template in await catalog.ListAsync())
        {
            var parameters = template.Parameters
                .Where(parameter => !parameter.IsReadOnly
                    && parameter.DefaultValue is { } value
                    && value.ValueKind is not System.Text.Json.JsonValueKind.Null
                        and not System.Text.Json.JsonValueKind.Undefined)
                .ToDictionary(
                    parameter => parameter.Name,
                    parameter => ToObject(parameter.DefaultValue!.Value),
                    StringComparer.OrdinalIgnoreCase);
            var context = TemplateExpressionContextBuilder.Build(template, parameters);
            foreach (var rule in template.ValidationRules)
            {
                if (!SafeTFlexExpressionEvaluator.TryEvaluateRule(rule.Expression, context, out _))
                {
                    var missing = System.Text.RegularExpressions.Regex.Matches(
                            rule.Expression,
                            @"\$?[\p{L}_][\p{L}\p{N}_]*")
                        .Select(match => match.Value)
                        .Where(name => !context.ContainsKey(name)
                            && !context.ContainsKey(name.StartsWith('$') ? name[1..] : $"${name}"))
                        .Distinct(StringComparer.OrdinalIgnoreCase);
                    failures.Add(
                        $"{template.Id}/{rule.Name}: missing [{string.Join(", ", missing)}]; {rule.Expression}");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Theory]
    [InlineData("lehy_l_pro_320_1050")]
    [InlineData("lehy_l_pro_1050_2500")]
    public async Task ProductionCatalog_PxxAreaRuleUsesActualCarDimensions(
        string templateId)
    {
        var repositoryRoot = FindRepositoryRoot();
        var catalog = new JsonTemplateCatalog(
            Options.Create(new TemplateCatalogOptions
            {
                ProjectRootPath = repositoryRoot,
                ConfigPath = Path.Combine(repositoryRoot, "templates", "templates.json")
            }),
            NullLogger<JsonTemplateCatalog>.Instance);
        var template = await catalog.GetByIdOrCodeAsync(templateId);
        Assert.NotNull(template);

        var parameters = template.Parameters
            .Where(parameter => !parameter.IsReadOnly
                && parameter.DefaultValue is { } value
                && value.ValueKind is not System.Text.Json.JsonValueKind.Null
                    and not System.Text.Json.JsonValueKind.Undefined)
            .ToDictionary(
                parameter => parameter.Name,
                parameter => ToObject(parameter.DefaultValue!.Value),
                StringComparer.OrdinalIgnoreCase);
        parameters["cap"] = 1050m;
        parameters["$car_type_1050"] = "PXX";
        parameters["AA_1"] = 2000m;
        parameters["BB_1"] = 2000m;

        var context = TemplateExpressionContextBuilder.Build(template, parameters);
        Assert.Equal("PXX", Assert.IsType<string>(context["$car_type"]));
        Assert.Equal(2000m, Assert.IsType<decimal>(context["AA_1"]));
        Assert.Equal(2000m, Assert.IsType<decimal>(context["BB_1"]));
        Assert.Equal(2000m, Assert.IsType<decimal>(context["AA"]));
        Assert.Equal(2000m, Assert.IsType<decimal>(context["BB"]));

        var areaRule = Assert.Single(
            template.ValidationRules,
            rule => rule.Name == "r_AREA");
        Assert.True(
            SafeTFlexExpressionEvaluator.TryEvaluateRule(
                areaRule.Expression,
                context,
                out var passed));
        Assert.False(passed);

        var requestParameters = template.Parameters
            .Where(parameter => !parameter.IsReadOnly
                && parameter.DefaultValue is { } value
                && value.ValueKind is not System.Text.Json.JsonValueKind.Null
                    and not System.Text.Json.JsonValueKind.Undefined)
            .ToDictionary(
                parameter => parameter.Name,
                parameter => parameter.DefaultValue!.Value.Clone(),
                StringComparer.OrdinalIgnoreCase);
        requestParameters["cap"] = System.Text.Json.JsonSerializer.SerializeToElement(1050);
        requestParameters["$car_type_1050"] = System.Text.Json.JsonSerializer.SerializeToElement("PXX");
        requestParameters["AA_1"] = System.Text.Json.JsonSerializer.SerializeToElement(2000);
        requestParameters["BB_1"] = System.Text.Json.JsonSerializer.SerializeToElement(2000);

        var validation = await new DrawingJobValidator(catalog).ValidateAsync(
            new CreateDrawingJobRequest
            {
                TemplateId = template.Id,
                OutputFormat = "pdf",
                Parameters = requestParameters
            });
        Assert.False(validation.IsValid);
        Assert.Contains(
            validation.Errors,
            error => error.StartsWith("AA*BB =", StringComparison.Ordinal));

        parameters["$car_type_1050"] = "P14G";
        var standardContext = TemplateExpressionContextBuilder.Build(template, parameters);
        Assert.Equal(1600m, Assert.IsType<decimal>(standardContext["AA"]));
        Assert.Equal(1500m, Assert.IsType<decimal>(standardContext["BB"]));
    }

    [Theory]
    [InlineData("lehy_l_pro_320_1050")]
    [InlineData("lehy_l_pro_1050_2500")]
    [InlineData("lehy_pro_side_cwt")]
    [InlineData("lehy_pro_rear_cwt")]
    public async Task ProductionCatalog_LehyStopSelectorsCoverFullSupportedRange(
        string templateId)
    {
        var template = await GetProductionTemplateAsync(templateId);

        var stops = Assert.Single(template.Parameters, parameter => parameter.Name == "stops");
        Assert.Equal("integer", stops.Type);
        Assert.Equal(2m, stops.MinValue);
        Assert.Equal(48m, stops.MaxValue);

        var mainFloor = Assert.Single(
            template.Parameters,
            parameter => parameter.Name == "main_floor");
        Assert.Equal("integer", mainFloor.Type);
        Assert.Equal(1m, mainFloor.MinValue);
        Assert.Equal(48m, mainFloor.MaxValue);
        Assert.Equal(
            Enumerable.Range(1, 48).Select(value => value.ToString()).ToArray(),
            mainFloor.AllowedValues);
        Assert.Equal(1, mainFloor.DefaultValue?.GetInt32());
    }

    [Theory]
    [InlineData("lehy_l_pro_320_1050", "AH<=max_A1+A2+max_A3", "{max_A1+A2+max_A3}")]
    [InlineData("lehy_l_pro_1050_2500", "AH<=max_AH", "{max_AH}")]
    [InlineData("lehy_pro_side_cwt", "AH<=max_BW+CA+max_CB", "{max_BW+CA+max_CB}")]
    [InlineData("lehy_pro_rear_cwt", "AH<=2*max_CB", "{2*max_CB}")]
    public async Task ProductionCatalog_LehyAhRulesEnforceDocumentedUpperBound(
        string templateId,
        string upperBoundExpression,
        string upperBoundMessage)
    {
        var template = await GetProductionTemplateAsync(templateId);
        var rule = Assert.Single(template.ValidationRules, rule => rule.Name == "r_AH");

        Assert.Contains("AH>=min_AH", rule.Expression, StringComparison.Ordinal);
        Assert.Contains(upperBoundExpression, rule.Expression, StringComparison.Ordinal);
        Assert.Contains("{min_AH} ≤ AH ≤", rule.Message, StringComparison.Ordinal);
        Assert.Contains(upperBoundMessage, rule.Message, StringComparison.Ordinal);

        var calculatedRule = template.CalculatedVariables.SingleOrDefault(
            variable => variable.Name == "r_AH");
        if (calculatedRule is not null)
        {
            Assert.Contains(
                upperBoundExpression,
                calculatedRule.Expression,
                StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("lehy_l_pro_320_1050")]
    [InlineData("lehy_l_pro_1050_2500")]
    public async Task ProductionCatalog_LProA4RuleIsSymmetricAndIncludesBoundaries(
        string templateId)
    {
        var template = await GetProductionTemplateAsync(templateId);
        var rule = Assert.Single(template.ValidationRules, rule => rule.Name == "r_A4_1");
        var context = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["$door_type"] = "ЦО",
            ["AA"] = 1400m,
            ["JJ"] = 900m,
            ["A4"] = -200m,
            ["$r_A4_1_text"] = string.Empty
        };

        Assert.Contains("abs(A4)", rule.Expression, StringComparison.Ordinal);
        Assert.Contains("≤ A4 ≤", rule.Message, StringComparison.Ordinal);
        Assert.True(
            SafeTFlexExpressionEvaluator.TryEvaluateRule(rule.Expression, context, out var boundaryPassed));
        Assert.True(boundaryPassed);

        context["A4"] = -201m;
        Assert.True(
            SafeTFlexExpressionEvaluator.TryEvaluateRule(rule.Expression, context, out var outsidePassed));
        Assert.False(outsidePassed);
    }

    [Theory]
    [InlineData("lehy_l_pro_320_1050")]
    [InlineData("lehy_l_pro_1050_2500")]
    [InlineData("lehy_pro_side_cwt")]
    [InlineData("lehy_pro_rear_cwt")]
    public async Task ProductionCatalog_LehyMessagesMatchInclusiveRules(
        string templateId)
    {
        var template = await GetProductionTemplateAsync(templateId);

        var doorWidthRule = Assert.Single(
            template.ValidationRules,
            rule => rule.Name == "r_JJ_AA");
        Assert.Contains("AA-JJ ≥", doorWidthRule.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("JJ-AA", doorWidthRule.Message, StringComparison.Ordinal);

        var travelRule = Assert.Single(template.ValidationRules, rule => rule.Name == "r_TR");
        Assert.Contains("TR ≤", travelRule.Message, StringComparison.Ordinal);

        var maximumFloorHeightRule = Assert.Single(
            template.ValidationRules,
            rule => rule.Name == "r_maxHF");
        Assert.Contains("≤ 11000", maximumFloorHeightRule.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("lehy_l_pro_320_1050")]
    [InlineData("lehy_l_pro_1050_2500")]
    [InlineData("lehy_pro_side_cwt")]
    [InlineData("lehy_pro_rear_cwt")]
    public async Task ProductionCatalog_LehyMaximumFloorHeightChecksEveryActiveGap(
        string templateId)
    {
        var template = await GetProductionTemplateAsync(templateId);
        var rule = Assert.Single(template.ValidationRules, rule => rule.Name == "r_maxHF");
        var context = Enumerable.Range(1, 48).ToDictionary(
            index => $"maxHF{index:00}",
            _ => (object?)1m,
            StringComparer.Ordinal);
        context["maxHF03"] = 0m;
        context["$r_maxHF_text"] = "Maximum floor height exceeded.";

        Assert.DoesNotContain("**", rule.Expression, StringComparison.Ordinal);
        Assert.True(
            SafeTFlexExpressionEvaluator.TryEvaluateRule(
                rule.Expression,
                context,
                out var passed));
        Assert.False(passed);
    }

    [Fact]
    public async Task ProductionCatalog_SideCwtMessagesUseLivePlaceholders()
    {
        var template = await GetProductionTemplateAsync("lehy_pro_side_cwt");

        Assert.Equal(
            "BW = {BW}. Должно быть {min_BW} ≤ BW ≤ {max_BW}.",
            Assert.Single(template.ValidationRules, rule => rule.Name == "r_BW_1").Message);
        Assert.Equal(
            "AH = {AH}. Должно быть {min_AH} ≤ AH ≤ {max_BW+CA+max_CB}.",
            Assert.Single(template.ValidationRules, rule => rule.Name == "r_AH").Message);
        Assert.Equal(
            "Остановки = {stops}. Должно быть остановки ≤ {S_stops}.",
            Assert.Single(template.ValidationRules, rule => rule.Name == "r_stops").Message);
        Assert.Equal(
            "Минимальное межэтажное расстояние должно быть ≥ {S_HF}.",
            Assert.Single(template.ValidationRules, rule => rule.Name == "r_THF").Message);
    }

    [Fact]
    public async Task ProductionCatalog_KIiMessagesMatchCalculatedLimitsAndUnits()
    {
        var template = await GetProductionTemplateAsync("k_ii_type");

        Assert.Contains(
            "1100",
            Assert.Single(template.CalculatedVariables, variable => variable.Name == "TJmax").Expression,
            StringComparison.Ordinal);
        Assert.Contains(
            "1100",
            Assert.Single(template.CalculatedVariables, variable => variable.Name == "TKmax").Expression,
            StringComparison.Ordinal);
        Assert.Contains(
            "{TJmax}",
            Assert.Single(template.ValidationRules, rule => rule.Name == "err1").Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "{TKmax}",
            Assert.Single(template.ValidationRules, rule => rule.Name == "err2").Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "{TJmax}",
            Assert.Single(template.CalculatedVariables, variable => variable.Name == "err1").Expression,
            StringComparison.Ordinal);
        Assert.Contains(
            "{TKmax}",
            Assert.Single(template.CalculatedVariables, variable => variable.Name == "err2").Expression,
            StringComparison.Ordinal);
        Assert.Contains(
            "≥ 9500 мм",
            Assert.Single(template.ValidationRules, rule => rule.Name == "err3").Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "≥ 9500 мм",
            Assert.Single(template.CalculatedVariables, variable => variable.Name == "err3").Expression,
            StringComparison.Ordinal);
        Assert.EndsWith(
            "{HEmax} мм",
            Assert.Single(template.ValidationRules, rule => rule.Name == "err6").Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "{HEmax} мм",
            Assert.Single(template.CalculatedVariables, variable => variable.Name == "err6").Expression,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProductionCatalog_RazvertkiMessagesIncludeAllowedBoundaries()
    {
        var template = await GetProductionTemplateAsync("razvertki_lehy");

        Assert.Contains(
            "не более 54",
            Assert.Single(template.ValidationRules, rule => rule.Name == "e2").Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "не более 2800",
            Assert.Single(template.ValidationRules, rule => rule.Name == "e3").Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "не более 2500",
            Assert.Single(template.ValidationRules, rule => rule.Name == "e4").Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "не более 1300",
            Assert.Single(template.ValidationRules, rule => rule.Name == "e5").Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "не менее 700",
            Assert.Single(template.ValidationRules, rule => rule.Name == "e6").Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "не более 54",
            Assert.Single(template.CalculatedVariables, variable => variable.Name == "$e2").Expression,
            StringComparison.Ordinal);
        Assert.Contains(
            "не менее 700",
            Assert.Single(template.CalculatedVariables, variable => variable.Name == "$e6").Expression,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProductionCatalog_VictorWarningsDeclareWarningSeverity()
    {
        var template = await GetProductionTemplateAsync("un_victor_mrl");

        Assert.Equal(
            "warning",
            Assert.Single(template.ValidationRules, rule => rule.Name == "warn01").Severity);
        Assert.Equal(
            "warning",
            Assert.Single(template.ValidationRules, rule => rule.Name == "warn02").Severity);
    }

    [Fact]
    public async Task ProductionCatalog_KIiTypePreservesAndResolvesExactCaseSymbols()
    {
        var template = await GetProductionTemplateAsync("k_ii_type");
        var context = TemplateExpressionContextBuilder.Build(
            template,
            BuildDefaultParameterValues(template));

        AssertExactSymbolsResolve(
            context,
            "$Mode",
            "$mode",
            "$MODE",
            "tg",
            "TG",
            "la",
            "LA");

        Assert.Equal("Нормальный", Assert.IsType<string>(context["$Mode"]));
        Assert.Equal("нормального", Assert.IsType<string>(context["$mode"]));
        Assert.Equal("Н", Assert.IsType<string>(context["$MODE"]));
        Assert.NotEqual(context["tg"], context["TG"]);
        Assert.NotEqual(context["la"], context["LA"]);

        Assert.False(
            SafeTFlexExpressionEvaluator.TryEvaluateExpression(
                "$MoDe",
                context,
                out _));
        Assert.False(
            SafeTFlexExpressionEvaluator.TryEvaluateExpression(
                "tG",
                context,
                out _));
        Assert.False(
            SafeTFlexExpressionEvaluator.TryEvaluateExpression(
                "lA",
                context,
                out _));
    }

    [Theory]
    [InlineData("un_victor_mrl")]
    [InlineData("un_victor_mrl_t")]
    public async Task ProductionCatalog_VictorTemplatesPreserveAndResolveExactCaseSymbols(
        string templateId)
    {
        var template = await GetProductionTemplateAsync(templateId);
        var builtContext = TemplateExpressionContextBuilder.Build(
            template,
            BuildDefaultParameterValues(template));
        var context = new Dictionary<string, object?>(builtContext, StringComparer.Ordinal);

        // GET-backed values are resolved later by T-FLEX. Seed the real catalogue
        // fallback here so both intentional gap_rear/GAP_REAR symbols participate
        // in the server resolver regression.
        var gapRear = Assert.Single(
            template.CalculatedVariables,
            variable => variable.Name == "gap_rear");
        context["gap_rear"] = ToObject(gapRear.DefaultValue!.Value);

        AssertExactSymbolsResolve(
            context,
            "S",
            "s",
            "HW1_MIN",
            "hw1_min",
            "HW1_MAX",
            "hw1_max",
            "gap_rear",
            "GAP_REAR");

        Assert.NotEqual(context["S"], context["s"]);
        Assert.NotEqual(context["HW1_MAX"], context["hw1_max"]);
        Assert.NotEqual(context["gap_rear"], context["GAP_REAR"]);

        Assert.False(
            SafeTFlexExpressionEvaluator.TryEvaluateExpression(
                "hW1_mIn",
                context,
                out _));
        Assert.False(
            SafeTFlexExpressionEvaluator.TryEvaluateExpression(
                "hW1_mAx",
                context,
                out _));
        Assert.False(
            SafeTFlexExpressionEvaluator.TryEvaluateExpression(
                "Gap_Rear",
                context,
                out _));
    }

    [Fact]
    public void ExpressionEvaluator_UsesUniqueCaseInsensitiveFallbackForCompatibility()
    {
        var context = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["UniqueName"] = 42m
        };

        Assert.True(
            SafeTFlexExpressionEvaluator.TryEvaluateExpression(
                "uniquename",
                context,
                out var value));
        Assert.Equal(42m, Assert.IsType<decimal>(value));
    }

    private static async Task<TFlexDrawingService.Core.Models.DrawingTemplate>
        GetProductionTemplateAsync(string templateId)
    {
        var repositoryRoot = FindRepositoryRoot();
        var catalog = new JsonTemplateCatalog(
            Options.Create(new TemplateCatalogOptions
            {
                ProjectRootPath = repositoryRoot,
                ConfigPath = Path.Combine(repositoryRoot, "templates", "templates.json")
            }),
            NullLogger<JsonTemplateCatalog>.Instance);

        return Assert.IsType<TFlexDrawingService.Core.Models.DrawingTemplate>(
            await catalog.GetByIdOrCodeAsync(templateId));
    }

    private static Dictionary<string, object?> BuildDefaultParameterValues(
        TFlexDrawingService.Core.Models.DrawingTemplate template)
    {
        return template.Parameters
            .Where(parameter => !parameter.IsReadOnly
                && parameter.DefaultValue is { } value
                && value.ValueKind is not System.Text.Json.JsonValueKind.Null
                    and not System.Text.Json.JsonValueKind.Undefined)
            .ToDictionary(
                parameter => parameter.Name,
                parameter => ToObject(parameter.DefaultValue!.Value),
                StringComparer.Ordinal);
    }

    private static void AssertExactSymbolsResolve(
        IReadOnlyDictionary<string, object?> context,
        params string[] names)
    {
        foreach (var name in names)
        {
            Assert.True(context.ContainsKey(name), $"Context is missing exact symbol '{name}'.");
            Assert.True(
                SafeTFlexExpressionEvaluator.TryEvaluateExpression(
                    name,
                    context,
                    out var value),
                $"Exact symbol '{name}' did not evaluate.");
            Assert.Equal(context[name], value);
        }
    }

    private static object? ToObject(System.Text.Json.JsonElement value)
    {
        return value.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => value.GetString(),
            System.Text.Json.JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            _ => null
        };
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TFlexDrawingService.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
