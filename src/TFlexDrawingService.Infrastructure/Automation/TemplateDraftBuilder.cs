using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TFlexDrawingService.Core.Models;
using TFlexDrawingService.Core.Services;

namespace TFlexDrawingService.Infrastructure.Automation;

public sealed partial class TemplateDraftBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public TemplateDraftBuildResult Build(
        TemplateAnalysisJob job,
        TFlexTemplateInspection inspection,
        IReadOnlyList<DrawingTemplate> referenceTemplates)
    {
        var warnings = new List<string>(inspection.RebuildWarnings);
        var reference = SelectReference(job.OriginalTemplateFileName, referenceTemplates);
        var referenceDefinitions = (reference?.Parameters ?? [])
            .Concat(reference?.CalculatedVariables ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var controls = inspection.Controls
            .Where(item => !string.IsNullOrWhiteSpace(item.Variable))
            .GroupBy(item => item.Variable!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var parameters = new List<DrawingParameterDefinition>();
        var calculated = new List<DrawingParameterDefinition>();
        foreach (var variable in inspection.Variables
                     .Where(item => !string.IsNullOrWhiteSpace(item.Name)))
        {
            if (HasVariableError(variable))
            {
                warnings.Add(
                    $"Variable '{variable.Name}' reports {variable.ErrorState}: {variable.ErrorString}".Trim());
            }

            if (variable.External)
            {
                if (variable.Hidden || variable.Service)
                {
                    warnings.Add(
                        $"External variable '{variable.Name}' is hidden or service-only and was not added to the user form.");
                    continue;
                }

                parameters.Add(BuildDefinition(variable, controls, referenceDefinitions, isCalculated: false));
                continue;
            }

            if (variable.IsUsed
                && !variable.Hidden
                && !variable.Service
                && !variable.IsConstant
                && !string.IsNullOrWhiteSpace(variable.Expression))
            {
                calculated.Add(BuildDefinition(variable, controls, referenceDefinitions, isCalculated: true));
            }
        }

        var knownNames = parameters.Concat(calculated)
            .Select(item => item.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var template = new DrawingTemplate
        {
            Id = BuildIdentifier(Path.GetFileNameWithoutExtension(job.OriginalTemplateFileName), job.Id),
            Code = BuildIdentifier(Path.GetFileNameWithoutExtension(job.OriginalTemplateFileName), job.Id),
            Name = Path.GetFileNameWithoutExtension(job.OriginalTemplateFileName),
            Description = reference is null
                ? "Automatically generated from T-FLEX external variables and controls."
                : $"Automatically generated from T-FLEX; field patterns matched to {reference.Name}.",
            OutputFormats = ["pdf", "dwg", "dxf"],
            Parameters = parameters,
            CalculatedVariables = calculated,
            ValidationRules = CloneCompatibleRules(reference?.ValidationRules ?? [], knownNames),
            LookupTables = CloneLookupTables(reference?.LookupTables)
        };
        RemoveUnsupportedRules(template, warnings);

        if (reference is null)
        {
            warnings.Add(
                "No LEHY/SMEC reference pattern matched the uploaded file; display names and limits use T-FLEX metadata only.");
        }
        else
        {
            warnings.Add($"Recognition pattern: {reference.Name}.");
        }

        if (parameters.Count == 0)
        {
            warnings.Add("T-FLEX did not expose any visible external variables for the user form.");
        }

        return new TemplateDraftBuildResult(
            template,
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static DrawingParameterDefinition BuildDefinition(
        TFlexVariableInspection variable,
        IReadOnlyDictionary<string, TFlexControlInspection> controls,
        IReadOnlyDictionary<string, DrawingParameterDefinition> references,
        bool isCalculated)
    {
        references.TryGetValue(variable.Name, out var reference);
        controls.TryGetValue(variable.Name, out var control);
        var allowedValues = variable.AllowedValues.Count > 0
            ? variable.AllowedValues
            : control?.AllowedValues ?? [];
        var type = InferType(variable, control, reference, allowedValues);
        var definition = new DrawingParameterDefinition
        {
            Name = variable.Name,
            DisplayName = FirstNonEmpty(
                reference?.DisplayName,
                variable.Comment,
                control?.ObjectName,
                variable.Name),
            Type = type,
            Unit = FirstNonEmpty(reference?.Unit, NormalizeUnit(variable.Unit)),
            IsRequired = !isCalculated && (reference?.IsRequired ?? true),
            IsReadOnly = isCalculated || (reference?.IsReadOnly ?? false),
            SubmitDefault = reference?.SubmitDefault ?? true,
            SubmitWhenDisabled = isCalculated || (reference?.SubmitWhenDisabled ?? false),
            LevelExpression = FirstNonEmpty(
                reference?.LevelExpression,
                NormalizeLevelExpression(control?.Level)),
            Expression = FirstNonEmpty(variable.Expression, reference?.Expression),
            MinValue = reference?.MinValue,
            MaxValue = reference?.MaxValue,
            DefaultValue = CreateDefaultValue(variable.Value, type, reference?.DefaultValue),
            AllowedValues = allowedValues.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            AllowedValueLabels = reference?.AllowedValueLabels is null
                ? []
                : new Dictionary<string, string>(reference.AllowedValueLabels, StringComparer.OrdinalIgnoreCase),
            LookupValues = CloneLookupValues(reference?.LookupValues),
            Description = FirstNonEmpty(
                reference?.Description,
                string.IsNullOrWhiteSpace(variable.GroupName) ? null : $"T-FLEX group: {variable.GroupName}"),
            Multiline = reference?.Multiline ?? false,
            Rows = reference?.Rows
        };

        if (definition.AllowedValues.Count > 0
            && !isCalculated
            && (variable.IsText
                || variable.Name.StartsWith('$')
                || definition.Type is "string" or "enum"))
        {
            definition.Type = definition.Type is "bool" or "boolean" ? "bool" : "enum";
        }

        return definition;
    }

    private static string InferType(
        TFlexVariableInspection variable,
        TFlexControlInspection? control,
        DrawingParameterDefinition? reference,
        IReadOnlyCollection<string> allowedValues)
    {
        if (!string.IsNullOrWhiteSpace(reference?.Type))
        {
            return reference.Type;
        }

        if (control?.ControlType?.Contains("Check", StringComparison.OrdinalIgnoreCase) == true
            || (allowedValues.Count == 2
                && allowedValues.All(value => value is "0" or "1")))
        {
            return "bool";
        }

        if (allowedValues.Count > 0)
        {
            return "enum";
        }

        if (variable.IsText || variable.Name.StartsWith('$'))
        {
            return "string";
        }

        return decimal.TryParse(variable.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
               && decimal.Truncate(value) == value
            ? "integer"
            : "number";
    }

    private static JsonElement? CreateDefaultValue(
        string? value,
        string type,
        JsonElement? fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback?.Clone();
        }

        if (type is "bool" or "boolean")
        {
            var boolean = value == "1"
                || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("да", StringComparison.OrdinalIgnoreCase);
            return JsonSerializer.SerializeToElement(boolean);
        }

        if (type is "number" or "integer"
            && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
        {
            return JsonSerializer.SerializeToElement(number);
        }

        return JsonSerializer.SerializeToElement(Unquote(value));
    }

    private static DrawingTemplate? SelectReference(
        string fileName,
        IReadOnlyList<DrawingTemplate> templates)
    {
        var sourceTokens = Tokenize(Path.GetFileNameWithoutExtension(fileName));
        return templates
            .Where(template =>
                template.Name.Contains("LEHY", StringComparison.OrdinalIgnoreCase)
                || template.Name.Contains("SMEC", StringComparison.OrdinalIgnoreCase))
            .Select(template => new
            {
                Template = template,
                Score = Tokenize(template.Name).Intersect(sourceTokens, StringComparer.OrdinalIgnoreCase).Count()
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Template.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Template)
            .FirstOrDefault();
    }

    private static string[] Tokenize(string value)
    {
        return NonAlphaNumericRegex().Split(value.ToUpperInvariant())
            .Where(token => token.Length > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildIdentifier(string value, string jobId)
    {
        var normalized = NonIdentifierRegex().Replace(value.ToLowerInvariant(), "_").Trim('_');
        if (string.IsNullOrWhiteSpace(normalized) || !char.IsAsciiLetterOrDigit(normalized[0]))
        {
            normalized = $"template_{jobId[..8]}";
        }

        return normalized[..Math.Min(normalized.Length, 80)];
    }

    private static string? NormalizeLevelExpression(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            return null;
        }

        return value.Trim();
    }

    private static string? NormalizeUnit(string? value)
    {
        var unit = value?.Trim();
        return string.IsNullOrWhiteSpace(unit) || unit.StartsWith("#ERROR", StringComparison.OrdinalIgnoreCase)
            ? null
            : unit;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"'
            ? trimmed[1..^1]
            : trimmed;
    }

    private static bool HasVariableError(TFlexVariableInspection variable)
    {
        return (!string.IsNullOrWhiteSpace(variable.ErrorString)
                && !variable.ErrorString.Equals("None", StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(variable.ErrorState)
                && !variable.ErrorState.Equals("None", StringComparison.OrdinalIgnoreCase)
                && !variable.ErrorState.Equals("NoError", StringComparison.OrdinalIgnoreCase));
    }

    private static List<DrawingValidationRule> CloneCompatibleRules(
        IReadOnlyList<DrawingValidationRule> rules,
        IReadOnlySet<string> knownNames)
    {
        return rules
            .Where(rule => rule.FieldNames.Count == 0 || rule.FieldNames.All(knownNames.Contains))
            .Select(rule => new DrawingValidationRule
            {
                Name = rule.Name,
                Expression = rule.Expression,
                Message = rule.Message,
                Severity = rule.Severity,
                FieldNames = [.. rule.FieldNames]
            })
            .ToList();
    }

    private static void RemoveUnsupportedRules(
        DrawingTemplate template,
        ICollection<string> warnings)
    {
        while (true)
        {
            var error = TemplateExpressionDefinitionValidator.Validate(template)
                .FirstOrDefault(item => item.Field.StartsWith(
                    "manifest.validationRules[",
                    StringComparison.Ordinal));
            if (error is null)
            {
                return;
            }

            var start = "manifest.validationRules[".Length;
            var end = error.Field.IndexOf(']', start);
            if (end <= start
                || !int.TryParse(error.Field[start..end], out var index)
                || index < 0
                || index >= template.ValidationRules.Count)
            {
                warnings.Add($"A reference validation rule could not be converted: {error.Message}");
                template.ValidationRules.Clear();
                return;
            }

            warnings.Add(
                $"Validation rule '{template.ValidationRules[index].Name}' needs manual review and was not added: {error.Message}");
            template.ValidationRules.RemoveAt(index);
        }
    }

    private static Dictionary<string, List<Dictionary<string, JsonElement>>> CloneLookupTables(
        Dictionary<string, List<Dictionary<string, JsonElement>>>? source)
    {
        if (source is null || source.Count == 0)
        {
            return [];
        }

        return JsonSerializer.Deserialize<Dictionary<string, List<Dictionary<string, JsonElement>>>>(
                   JsonSerializer.Serialize(source, JsonOptions),
                   JsonOptions)
               ?? [];
    }

    private static List<Dictionary<string, JsonElement>> CloneLookupValues(
        List<Dictionary<string, JsonElement>>? source)
    {
        if (source is null || source.Count == 0)
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(
                   JsonSerializer.Serialize(source, JsonOptions),
                   JsonOptions)
               ?? [];
    }

    [GeneratedRegex("[^A-Za-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonIdentifierRegex();

    [GeneratedRegex("[^A-Z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlphaNumericRegex();
}

public sealed record TemplateDraftBuildResult(
    DrawingTemplate Template,
    IReadOnlyList<string> Warnings);
