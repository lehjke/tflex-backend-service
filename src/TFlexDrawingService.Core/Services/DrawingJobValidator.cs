using System.Globalization;
using System.Text.Json;
using TFlexDrawingService.Core.Abstractions;
using TFlexDrawingService.Core.Models;
using TFlexDrawingService.Core.Requests;

namespace TFlexDrawingService.Core.Services;

public sealed class DrawingJobValidator : IDrawingRequestValidator
{
    private const int MaxStringParameterLength = 16 * 1024;
    private readonly ITemplateCatalog _templateCatalog;
    private readonly int _maxLookupRowEvaluations;

    public DrawingJobValidator(ITemplateCatalog templateCatalog)
        : this(
            templateCatalog,
            TemplateExpressionContextBuilder.MaxLookupRowEvaluations)
    {
    }

    internal DrawingJobValidator(
        ITemplateCatalog templateCatalog,
        int maxLookupRowEvaluations)
    {
        ArgumentNullException.ThrowIfNull(templateCatalog);
        ArgumentOutOfRangeException.ThrowIfNegative(maxLookupRowEvaluations);
        _templateCatalog = templateCatalog;
        _maxLookupRowEvaluations = maxLookupRowEvaluations;
    }

    public async Task<DrawingJobValidationResult> ValidateAsync(
        CreateDrawingJobRequest request,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();

        if (request.Parameters is null)
        {
            return DrawingJobValidationResult.Failure(["Parameters are required."]);
        }

        if (string.IsNullOrWhiteSpace(request.TemplateId))
        {
            return DrawingJobValidationResult.Failure(["TemplateId is required."]);
        }

        var template = await _templateCatalog.GetByIdOrCodeAsync(request.TemplateId, cancellationToken);
        if (template is null)
        {
            return DrawingJobValidationResult.Failure([$"Template '{request.TemplateId}' was not found."]);
        }

        var outputFormat = NormalizeFormat(request.OutputFormat);
        if (string.IsNullOrWhiteSpace(outputFormat))
        {
            errors.Add("OutputFormat is required.");
        }
        else if (!template.OutputFormats.Any(format => string.Equals(format, outputFormat, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add($"Output format '{request.OutputFormat}' is not allowed for template '{template.Code}'.");
        }

        var definitions = template.Parameters.ToDictionary(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var suppliedName in request.Parameters.Keys)
        {
            if (!definitions.ContainsKey(suppliedName))
            {
                errors.Add($"Unknown parameter '{suppliedName}'.");
            }
        }

        var normalized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in template.Parameters)
        {
            if (definition.IsReadOnly)
            {
                // Browser-calculated values are display hints, not trusted inputs.
                // Required disabled values are derived below from the catalogue.
                continue;
            }

            if (!request.Parameters.TryGetValue(definition.Name, out var value) || IsMissing(value))
            {
                if (definition.IsRequired)
                {
                    errors.Add($"Parameter '{definition.Name}' is required.");
                    continue;
                }

                if (definition.SubmitDefault && HasDefaultValue(definition))
                {
                    if (TryNormalizeParameter(definition, definition.DefaultValue!.Value, errors, out var defaultValue))
                    {
                        normalized[definition.Name] = defaultValue;
                    }
                }

                continue;
            }

            if (TryNormalizeParameter(definition, value, errors, out var normalizedValue))
            {
                normalized[definition.Name] = normalizedValue;
            }
        }

        if (errors.Count == 0)
        {
            var expressionContext = TemplateExpressionContextBuilder.BuildRuntimeContext(
                template,
                normalized,
                _maxLookupRowEvaluations);
            ValidateTemplateRules(template, expressionContext, errors);
            AddTrustedReadOnlyParameters(template, expressionContext.Values, normalized, errors);
        }

        return errors.Count == 0
            ? DrawingJobValidationResult.Success(template, outputFormat, normalized)
            : DrawingJobValidationResult.Failure(errors);
    }

    private static string NormalizeFormat(string? outputFormat)
    {
        return (outputFormat ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();
    }

    private static bool HasDefaultValue(DrawingParameterDefinition definition)
    {
        return definition.DefaultValue.HasValue
            && definition.DefaultValue.Value.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null;
    }

    private static bool IsMissing(JsonElement value)
    {
        return value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            || (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()));
    }

    private static bool TryNormalizeParameter(
        DrawingParameterDefinition definition,
        JsonElement value,
        List<string> errors,
        out object? normalizedValue)
    {
        normalizedValue = null;
        var parameterType = (definition.Type ?? "string").Trim().ToLowerInvariant();

        return parameterType switch
        {
            "number" => TryNormalizeNumber(definition, value, errors, out normalizedValue),
            "integer" => TryNormalizeInteger(definition, value, errors, out normalizedValue),
            "bool" or "boolean" => TryNormalizeBoolean(definition, value, errors, out normalizedValue),
            "string" or "enum" => TryNormalizeString(definition, value, errors, out normalizedValue),
            _ => Fail(errors, $"Parameter '{definition.Name}' has unsupported type '{definition.Type}'.")
        };
    }

    private static bool TryNormalizeNumber(
        DrawingParameterDefinition definition,
        JsonElement value,
        List<string> errors,
        out object? normalizedValue)
    {
        normalizedValue = null;
        if (!TryReadDecimal(value, out var number))
        {
            return Fail(errors, $"Parameter '{definition.Name}' must be a number.");
        }

        if (definition.MinValue.HasValue && number < definition.MinValue.Value)
        {
            return Fail(errors, $"Parameter '{definition.Name}' must be greater than or equal to {definition.MinValue.Value}.");
        }

        if (definition.MaxValue.HasValue && number > definition.MaxValue.Value)
        {
            return Fail(errors, $"Parameter '{definition.Name}' must be less than or equal to {definition.MaxValue.Value}.");
        }

        if (!IsAllowedNumericValue(definition, number))
        {
            return Fail(errors, $"Parameter '{definition.Name}' has value '{number}', which is not allowed.");
        }

        normalizedValue = number;
        return true;
    }

    private static bool TryNormalizeInteger(
        DrawingParameterDefinition definition,
        JsonElement value,
        List<string> errors,
        out object? normalizedValue)
    {
        normalizedValue = null;
        if (!TryReadInt64(value, out var number))
        {
            return Fail(errors, $"Parameter '{definition.Name}' must be an integer.");
        }

        if (definition.MinValue.HasValue && number < definition.MinValue.Value)
        {
            return Fail(errors, $"Parameter '{definition.Name}' must be greater than or equal to {definition.MinValue.Value}.");
        }

        if (definition.MaxValue.HasValue && number > definition.MaxValue.Value)
        {
            return Fail(errors, $"Parameter '{definition.Name}' must be less than or equal to {definition.MaxValue.Value}.");
        }

        if (!IsAllowedNumericValue(definition, number))
        {
            return Fail(errors, $"Parameter '{definition.Name}' has value '{number}', which is not allowed.");
        }

        normalizedValue = number;
        return true;
    }

    private static bool TryNormalizeBoolean(
        DrawingParameterDefinition definition,
        JsonElement value,
        List<string> errors,
        out object? normalizedValue)
    {
        normalizedValue = null;
        if (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
        {
            normalizedValue = value.GetBoolean();
            return true;
        }

        if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed))
        {
            normalizedValue = parsed;
            return true;
        }

        return Fail(errors, $"Parameter '{definition.Name}' must be a boolean.");
    }

    private static bool TryNormalizeString(
        DrawingParameterDefinition definition,
        JsonElement value,
        List<string> errors,
        out object? normalizedValue)
    {
        normalizedValue = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        var text = normalizedValue?.ToString() ?? string.Empty;

        if (definition.IsRequired && string.IsNullOrWhiteSpace(text))
        {
            return Fail(errors, $"Parameter '{definition.Name}' is required.");
        }

        if (text.Length > MaxStringParameterLength)
        {
            return Fail(
                errors,
                $"Parameter '{definition.Name}' must not exceed {MaxStringParameterLength} characters.");
        }

        if (definition.AllowedValues.Count > 0
            && !definition.AllowedValues.Any(allowed => string.Equals(allowed, text, StringComparison.OrdinalIgnoreCase)))
        {
            return Fail(errors, $"Parameter '{definition.Name}' has value '{text}', which is not allowed.");
        }

        normalizedValue = text;
        return true;
    }

    private static bool IsAllowedNumericValue(DrawingParameterDefinition definition, decimal value)
    {
        if (definition.AllowedValues.Count == 0)
        {
            return true;
        }

        return definition.AllowedValues.Any(allowed =>
            decimal.TryParse(allowed, NumberStyles.Number, CultureInfo.InvariantCulture, out var allowedValue)
            && allowedValue == value);
    }

    private static void ValidateTemplateRules(
        DrawingTemplate template,
        TemplateExpressionContextBuilder.RuntimeExpressionContext expressionContext,
        List<string> errors)
    {
        foreach (var rule in template.ValidationRules)
        {
            if (string.IsNullOrWhiteSpace(rule.Expression)
                || !expressionContext.TryEvaluateRule(
                    rule.Expression,
                    out var passed))
            {
                errors.Add(
                    $"Validation rule '{rule.Name}' could not be evaluated safely. "
                    + "The template catalogue must be corrected before this request can be generated.");
                continue;
            }

            if (passed)
            {
                continue;
            }

            errors.Add(string.IsNullOrWhiteSpace(rule.Message)
                ? $"Validation rule '{rule.Name}' failed."
                : rule.Message);
        }
    }

    private static void AddTrustedReadOnlyParameters(
        DrawingTemplate template,
        IReadOnlyDictionary<string, object?> expressionContext,
        IDictionary<string, object?> normalizedParameters,
        ICollection<string> errors)
    {
        foreach (var definition in template.Parameters.Where(parameter =>
                     parameter.IsReadOnly && parameter.SubmitWhenDisabled))
        {
            if (!expressionContext.TryGetValue(definition.Name, out var value) || value is null)
            {
                errors.Add(
                    $"Read-only parameter '{definition.Name}' could not be calculated safely. "
                    + "The template catalogue must be corrected before this request can be generated.");
                continue;
            }

            normalizedParameters[definition.Name] = value;
        }
    }

    private static bool TryReadDecimal(JsonElement value, out decimal number)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out number))
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out number)
                || decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out number);
        }

        number = default;
        return false;
    }

    private static bool TryReadInt64(JsonElement value, out long number)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out number))
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
                || long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.CurrentCulture, out number);
        }

        number = default;
        return false;
    }

    private static bool Fail(List<string> errors, string message)
    {
        errors.Add(message);
        return false;
    }
}
