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
