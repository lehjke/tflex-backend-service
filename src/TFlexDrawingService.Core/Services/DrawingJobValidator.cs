using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TFlexDrawingService.Core.Abstractions;
using TFlexDrawingService.Core.Models;
using TFlexDrawingService.Core.Requests;

namespace TFlexDrawingService.Core.Services;

public sealed partial class DrawingJobValidator : IDrawingRequestValidator
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

            if (string.Equals(rule.Severity?.Trim(), "warning", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            errors.Add(string.IsNullOrWhiteSpace(rule.Message)
                ? $"Validation rule '{rule.Name}' failed."
                : InterpolateValidationMessage(rule.Message, expressionContext));
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

            if (!TryNormalizeTrustedReadOnlyInteger(definition, value, errors, out var normalizedValue))
            {
                continue;
            }

            normalizedParameters[definition.Name] = normalizedValue;
        }
    }

    private static bool TryNormalizeTrustedReadOnlyInteger(
        DrawingParameterDefinition definition,
        object value,
        ICollection<string> errors,
        out object normalizedValue)
    {
        normalizedValue = value;
        if (!string.Equals(definition.Type?.Trim(), "integer", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!TryConvertCalculatedDecimal(value, out var number)
            || decimal.Truncate(number) != number
            || number < long.MinValue
            || number > long.MaxValue)
        {
            errors.Add($"Read-only parameter '{definition.Name}' must calculate to an integer.");
            return false;
        }

        if (definition.MinValue.HasValue && number < definition.MinValue.Value)
        {
            errors.Add(
                $"Read-only parameter '{definition.Name}' must calculate to a value greater than or equal to {definition.MinValue.Value}.");
            return false;
        }

        if (definition.MaxValue.HasValue && number > definition.MaxValue.Value)
        {
            errors.Add(
                $"Read-only parameter '{definition.Name}' must calculate to a value less than or equal to {definition.MaxValue.Value}.");
            return false;
        }

        if (!IsAllowedNumericValue(definition, number))
        {
            errors.Add(
                $"Read-only parameter '{definition.Name}' calculated value '{number}' is not allowed.");
            return false;
        }

        // Keep calculated numeric values as decimals for compatibility with the
        // expression context and the existing T-FLEX parameter serializer.
        normalizedValue = number;
        return true;
    }

    private static string InterpolateValidationMessage(
        string message,
        TemplateExpressionContextBuilder.RuntimeExpressionContext expressionContext)
    {
        return ValidationMessagePlaceholderRegex().Replace(message, match =>
        {
            var expression = match.Groups[1].Value;
            return expressionContext.TryEvaluateExpression(expression, out var value) && value is not null
                ? FormatValidationValue(value)
                : match.Value;
        });
    }

    private static string FormatValidationValue(object value)
    {
        if (TryConvertValidationNumber(value, out var number))
        {
            try
            {
                // JavaScript's Math.round, used by the browser, rounds midpoint
                // values towards positive infinity.
                number = decimal.Floor((number * 1000m) + 0.5m) / 1000m;
            }
            catch (OverflowException)
            {
                // The value is still safe to display invariantly; only the
                // three-decimal presentation rounding could not be applied.
            }

            return number.ToString("0.###", CultureInfo.InvariantCulture);
        }

        return value switch
        {
            bool boolean => boolean ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static bool TryReadDecimal(JsonElement value, out decimal number)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out number))
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return TryReadDecimalString(value.GetString(), out number);
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
            return long.TryParse(
                value.GetString()?.Trim(),
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out number);
        }

        number = default;
        return false;
    }

    private static bool TryReadDecimalString(string? text, out decimal number)
    {
        number = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Trim();
        if (normalized.Contains(','))
        {
            // Request strings use either a comma or a dot as the decimal
            // separator. Group separators are intentionally unsupported, so
            // "20,5" cannot become 205 and "1,000" cannot become 1000.
            if (normalized.Contains('.'))
            {
                return false;
            }

            normalized = normalized.Replace(',', '.');
        }

        return decimal.TryParse(
            normalized,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out number);
    }

    private static bool TryConvertCalculatedDecimal(object value, out decimal number)
    {
        try
        {
            switch (value)
            {
                case decimal decimalValue:
                    number = decimalValue;
                    return true;
                case bool boolean:
                    number = boolean ? 1m : 0m;
                    return true;
                case byte or sbyte or short or ushort or int or uint or long or ulong:
                    number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                    return true;
                case float floatValue when float.IsFinite(floatValue):
                    number = Convert.ToDecimal(floatValue, CultureInfo.InvariantCulture);
                    return true;
                case double doubleValue when double.IsFinite(doubleValue):
                    number = Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture);
                    return true;
                case JsonElement { ValueKind: JsonValueKind.Number } json when json.TryGetDecimal(out number):
                    return true;
                case JsonElement { ValueKind: JsonValueKind.True }:
                    number = 1m;
                    return true;
                case JsonElement { ValueKind: JsonValueKind.False }:
                    number = 0m;
                    return true;
                case string text:
                    return decimal.TryParse(
                        text,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out number);
                default:
                    number = default;
                    return false;
            }
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            number = default;
            return false;
        }
    }

    private static bool TryConvertValidationNumber(object value, out decimal number)
    {
        // Message interpolation mirrors JavaScript's numeric formatting without
        // coercing strings or booleans into numbers.
        try
        {
            switch (value)
            {
                case decimal decimalValue:
                    number = decimalValue;
                    return true;
                case byte or sbyte or short or ushort or int or uint or long or ulong:
                    number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                    return true;
                case float floatValue when float.IsFinite(floatValue):
                    number = Convert.ToDecimal(floatValue, CultureInfo.InvariantCulture);
                    return true;
                case double doubleValue when double.IsFinite(doubleValue):
                    number = Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture);
                    return true;
                default:
                    number = default;
                    return false;
            }
        }
        catch (OverflowException)
        {
            number = default;
            return false;
        }
    }

    [GeneratedRegex(@"\{([^{}]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex ValidationMessagePlaceholderRegex();

    private static bool Fail(List<string> errors, string message)
    {
        errors.Add(message);
        return false;
    }
}
