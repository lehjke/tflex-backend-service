using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TFlexDrawingService.Core.Models;

namespace TFlexDrawingService.Core.Services;

internal static partial class TemplateExpressionContextBuilder
{
    private const int MaxEvaluationPasses = 32;
    private const int MaxFindReplacements = 64;
    internal const int MaxLookupRowEvaluations = 100_000;

    public static IReadOnlyDictionary<string, object?> Build(
        DrawingTemplate template,
        IReadOnlyDictionary<string, object?> normalizedParameters)
    {
        return BuildRuntimeContext(
            template,
            normalizedParameters,
            MaxLookupRowEvaluations).Values;
    }

    internal static IReadOnlyDictionary<string, object?> Build(
        DrawingTemplate template,
        IReadOnlyDictionary<string, object?> normalizedParameters,
        int maxLookupRowEvaluations)
    {
        return BuildRuntimeContext(
            template,
            normalizedParameters,
            maxLookupRowEvaluations).Values;
    }

    internal static RuntimeExpressionContext BuildRuntimeContext(
        DrawingTemplate template,
        IReadOnlyDictionary<string, object?> normalizedParameters,
        int maxLookupRowEvaluations = MaxLookupRowEvaluations)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxLookupRowEvaluations);
        var lookupBudget = new LookupWorkBudget(maxLookupRowEvaluations);
        var context = BuildCore(template, normalizedParameters, lookupBudget);
        return new RuntimeExpressionContext(template, context, lookupBudget);
    }

    private static IReadOnlyDictionary<string, object?> BuildCore(
        DrawingTemplate template,
        IReadOnlyDictionary<string, object?> normalizedParameters,
        LookupWorkBudget lookupBudget)
    {
        // T-FLEX identifiers are case-sensitive when a template deliberately
        // defines case-distinct symbols (for example, S/s or $Mode/$MODE).
        // The evaluator still provides a compatibility fallback for a uniquely
        // cased identifier, so the context itself must preserve every exact name.
        var context = new Dictionary<string, object?>(StringComparer.Ordinal);

        // Hidden parameter variants are not all submitted by the browser, but their
        // defaults participate in T-FLEX expressions. They are calculation inputs,
        // not additional values persisted in the normalized request.
        foreach (var parameter in template.Parameters)
        {
            if (TryGetDefaultValue(parameter, out var defaultValue))
            {
                context[parameter.Name] = CoerceValue(parameter, defaultValue);
            }
        }

        foreach (var (name, value) in normalizedParameters)
        {
            context[name] = value;
        }

        SeedKnownDerivedInputs(context);
        SeedImplicitTemplateConstants(template, context);

        foreach (var variable in template.CalculatedVariables)
        {
            if (string.IsNullOrWhiteSpace(variable.Expression)
                && TryGetDefaultValue(variable, out var defaultValue))
            {
                context[variable.Name] = CoerceValue(variable, defaultValue);
            }
        }

        var definitions = template.Parameters
            .Where(parameter => parameter.IsReadOnly)
            .Concat(template.CalculatedVariables)
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Name))
            .ToArray();

        for (var pass = 0; pass < MaxEvaluationPasses; pass++)
        {
            var changed = false;

            foreach (var parameter in template.Parameters.Where(parameter => parameter.LookupValues.Count > 0))
            {
                if (TryGetInlineLookupValue(parameter, context, lookupBudget, out var lookupValue))
                {
                    changed |= PutIfChanged(context, parameter.Name, CoerceValue(parameter, lookupValue));
                }
            }

            SeedKnownDerivedInputs(context);

            foreach (var definition in definitions)
            {
                if (definition.LookupValues.Count > 0
                    && TryGetInlineLookupValue(
                        definition,
                        context,
                        lookupBudget,
                        out var lookupValue))
                {
                    changed |= PutIfChanged(context, definition.Name, CoerceValue(definition, lookupValue));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(definition.Expression)
                    || !TryExpandFindExpressions(
                        definition.Expression,
                        template,
                        context,
                        lookupBudget,
                        out var expression)
                    || !SafeTFlexExpressionEvaluator.TryEvaluateExpression(expression, context, out var value))
                {
                    continue;
                }

                changed |= PutIfChanged(context, definition.Name, CoerceValue(definition, value));
            }

            SeedKnownDerivedInputs(context);

            if (!changed)
            {
                break;
            }
        }

        return context;
    }

    internal sealed class RuntimeExpressionContext(
        DrawingTemplate template,
        IReadOnlyDictionary<string, object?> values,
        LookupWorkBudget lookupBudget)
    {
        public IReadOnlyDictionary<string, object?> Values { get; } = values;

        public bool TryEvaluateRule(string source, out bool passed)
        {
            passed = false;
            return TryExpandFindExpressions(
                    source,
                    template,
                    Values,
                    lookupBudget,
                    out var expression)
                && SafeTFlexExpressionEvaluator.TryEvaluateRule(
                    expression,
                    Values,
                    out passed);
        }
    }

    internal static bool TryValidateSupportedExpression(
        string source,
        DrawingTemplate template)
    {
        return TryReplaceFindCallsForValidation(source, template, out var expression)
            && SafeTFlexExpressionEvaluator.TryValidateSupportedSyntax(expression);
    }

    private static bool TryReplaceFindCallsForValidation(
        string source,
        DrawingTemplate template,
        out string expression)
    {
        expression = source;
        for (var replacement = 0; replacement < MaxFindReplacements; replacement++)
        {
            var findIndex = expression.IndexOf("find(", StringComparison.OrdinalIgnoreCase);
            if (findIndex < 0)
            {
                return true;
            }

            if (!TryReadFunctionCall(expression, findIndex, out var endIndex, out var argumentText)
                || !TrySplitTopLevel(argumentText, out var target, out var predicate))
            {
                return false;
            }

            var targetMatch = LookupTargetRegex().Match(target.Trim());
            if (!targetMatch.Success)
            {
                return false;
            }

            var tableName = targetMatch.Groups["table"].Value;
            var fieldName = targetMatch.Groups["field"].Value;
            var table = template.LookupTables.FirstOrDefault(pair =>
                string.Equals(pair.Key, tableName, StringComparison.OrdinalIgnoreCase)).Value;
            if (table is null
                || !table.Any(row => row is not null && row.Keys.Any(name =>
                    string.Equals(name, fieldName, StringComparison.OrdinalIgnoreCase))))
            {
                return false;
            }

            var rowReferenceRegex = new Regex(
                $@"\b{Regex.Escape(tableName)}\.(?<field>[\p{{L}}_][\p{{L}}\p{{N}}_]*)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var referencedFields = rowReferenceRegex.Matches(predicate)
                .Select(match => match.Groups["field"].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (referencedFields.Any(referencedField =>
                    !table.Any(row => row is not null && row.Keys.Any(name =>
                        string.Equals(name, referencedField, StringComparison.OrdinalIgnoreCase)))))
            {
                return false;
            }

            var normalizedPredicate = rowReferenceRegex.Replace(
                predicate,
                match => LookupAlias(match.Groups["field"].Value));
            if (!TryReplaceFindCallsForValidation(
                    normalizedPredicate,
                    template,
                    out normalizedPredicate)
                || !SafeTFlexExpressionEvaluator.TryValidateSupportedSyntax(normalizedPredicate))
            {
                return false;
            }

            expression = string.Concat(
                expression.AsSpan(0, findIndex),
                "0",
                expression.AsSpan(endIndex + 1));
        }

        return false;
    }

    private static bool TryExpandFindExpressions(
        string source,
        DrawingTemplate template,
        IReadOnlyDictionary<string, object?> context,
        LookupWorkBudget lookupBudget,
        out string expression)
    {
        expression = source;
        for (var replacement = 0; replacement < MaxFindReplacements; replacement++)
        {
            var findIndex = expression.IndexOf("find(", StringComparison.OrdinalIgnoreCase);
            if (findIndex < 0)
            {
                return true;
            }

            if (!TryReadFunctionCall(expression, findIndex, out var endIndex, out var argumentText)
                || !TrySplitTopLevel(argumentText, out var target, out var predicate)
                || !TryResolveFind(
                    target,
                    predicate,
                    template,
                    context,
                    lookupBudget,
                    out var value))
            {
                return false;
            }

            expression = string.Concat(
                expression.AsSpan(0, findIndex),
                ToExpressionLiteral(value),
                expression.AsSpan(endIndex + 1));
        }

        return false;
    }

    private static bool TryResolveFind(
        string target,
        string predicate,
        DrawingTemplate template,
        IReadOnlyDictionary<string, object?> context,
        LookupWorkBudget lookupBudget,
        out object? value)
    {
        value = null;
        var targetMatch = LookupTargetRegex().Match(target.Trim());
        if (!targetMatch.Success)
        {
            return false;
        }

        var tableName = targetMatch.Groups["table"].Value;
        var fieldName = targetMatch.Groups["field"].Value;
        var table = template.LookupTables.FirstOrDefault(pair =>
            string.Equals(pair.Key, tableName, StringComparison.OrdinalIgnoreCase)).Value;
        if (table is null)
        {
            return false;
        }

        var rowReferenceRegex = new Regex(
            $@"\b{Regex.Escape(tableName)}\.(?<field>[\p{{L}}_][\p{{L}}\p{{N}}_]*)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var rowPredicate = rowReferenceRegex.Replace(
            predicate,
            match => LookupAlias(match.Groups["field"].Value));

        foreach (var row in table)
        {
            if (!lookupBudget.TryConsumeRow())
            {
                return false;
            }

            var rowContext = new Dictionary<string, object?>(
                row.Count,
                StringComparer.Ordinal);
            foreach (var (name, rowValue) in row)
            {
                rowContext[LookupAlias(name)] = ConvertJsonValue(rowValue);
            }

            if (!SafeTFlexExpressionEvaluator.TryEvaluateRule(
                    rowPredicate,
                    context,
                    rowContext,
                    out var matched)
                || !matched)
            {
                continue;
            }

            var field = row.FirstOrDefault(pair =>
                string.Equals(pair.Key, fieldName, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(field.Key))
            {
                return false;
            }

            value = ConvertJsonValue(field.Value);
            return true;
        }

        // T-FLEX table lookup expressions use zero as their safe "not found"
        // value. Returning a known zero lets the dependent validation rule fail
        // normally instead of turning a bad parameter combination into an
        // indeterminate catalogue error.
        value = 0m;
        return true;
    }

    private static bool TryReadFunctionCall(
        string expression,
        int functionIndex,
        out int endIndex,
        out string arguments)
    {
        endIndex = -1;
        arguments = string.Empty;
        var openIndex = expression.IndexOf('(', functionIndex);
        if (openIndex < 0)
        {
            return false;
        }

        var depth = 1;
        var quote = '\0';
        for (var index = openIndex + 1; index < expression.Length; index++)
        {
            var current = expression[index];
            var previous = index == 0 ? '\0' : expression[index - 1];
            if (quote != '\0')
            {
                if (current == quote && previous != '\\')
                {
                    quote = '\0';
                }

                continue;
            }

            if (current is '\'' or '"')
            {
                quote = current;
            }
            else if (current == '(')
            {
                depth++;
            }
            else if (current == ')' && --depth == 0)
            {
                endIndex = index;
                arguments = expression[(openIndex + 1)..index];
                return true;
            }
        }

        return false;
    }

    private static bool TrySplitTopLevel(string value, out string left, out string right)
    {
        left = string.Empty;
        right = string.Empty;
        var depth = 0;
        var quote = '\0';

        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            var previous = index == 0 ? '\0' : value[index - 1];
            if (quote != '\0')
            {
                if (current == quote && previous != '\\')
                {
                    quote = '\0';
                }

                continue;
            }

            if (current is '\'' or '"')
            {
                quote = current;
            }
            else if (current == '(')
            {
                depth++;
            }
            else if (current == ')')
            {
                depth--;
            }
            else if (current == ',' && depth == 0)
            {
                left = value[..index].Trim();
                right = value[(index + 1)..].Trim();
                return left.Length > 0 && right.Length > 0;
            }
        }

        return false;
    }

    private static bool TryGetInlineLookupValue(
        DrawingParameterDefinition parameter,
        IReadOnlyDictionary<string, object?> context,
        LookupWorkBudget lookupBudget,
        out object? value)
    {
        value = null;
        foreach (var row in parameter.LookupValues)
        {
            if (!lookupBudget.TryConsumeRow())
            {
                return false;
            }

            var matched = true;
            foreach (var (name, expected) in row)
            {
                if (string.Equals(name, "value", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryGetExactOrUniqueCaseInsensitive(context, name, out var actual)
                    || !AreLookupValuesEqual(ConvertJsonValue(expected), actual))
                {
                    matched = false;
                    break;
                }
            }

            if (!matched)
            {
                continue;
            }

            var result = row.FirstOrDefault(pair =>
                string.Equals(pair.Key, "value", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(result.Key))
            {
                return false;
            }

            value = ConvertJsonValue(result.Value);
            return true;
        }

        return false;
    }

    private static bool TryGetExactOrUniqueCaseInsensitive(
        IReadOnlyDictionary<string, object?> source,
        string name,
        out object? value)
    {
        var fallbackCount = 0;
        value = null;

        foreach (var pair in source)
        {
            if (string.Equals(pair.Key, name, StringComparison.Ordinal))
            {
                value = pair.Value;
                return true;
            }

            if (!string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            fallbackCount++;
            value = pair.Value;
        }

        if (fallbackCount == 1)
        {
            return true;
        }

        value = null;
        return false;
    }

    internal sealed class LookupWorkBudget(int remainingRows)
    {
        private int _remainingRows = remainingRows;

        public bool TryConsumeRow()
        {
            if (_remainingRows <= 0)
            {
                return false;
            }

            _remainingRows--;
            return true;
        }
    }

    private static bool AreLookupValuesEqual(object? expected, object? actual)
    {
        if (TryConvertDecimal(expected, out var expectedNumber)
            && TryConvertDecimal(actual, out var actualNumber))
        {
            return expectedNumber == actualNumber;
        }

        return string.Equals(
            Convert.ToString(expected, CultureInfo.InvariantCulture),
            Convert.ToString(actual, CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    private static bool TryGetDefaultValue(DrawingParameterDefinition definition, out object? value)
    {
        value = null;
        if (!definition.DefaultValue.HasValue
            || definition.DefaultValue.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return false;
        }

        value = ConvertJsonValue(definition.DefaultValue.Value);
        return true;
    }

    private static object? CoerceValue(DrawingParameterDefinition definition, object? value)
    {
        var type = (definition.Type ?? "string").Trim().ToLowerInvariant();
        if (type is "number" or "integer")
        {
            return TryConvertDecimal(value, out var number) ? number : value;
        }

        if (type is "bool" or "boolean")
        {
            if (value is bool boolean)
            {
                return boolean;
            }

            return TryConvertDecimal(value, out var number) ? number != 0 : value;
        }

        return value is null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static object? ConvertJsonValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => value.Clone()
        };
    }

    private static bool TryConvertDecimal(object? value, out decimal number)
    {
        try
        {
            switch (value)
            {
                case null:
                    number = default;
                    return false;
                case decimal decimalValue:
                    number = decimalValue;
                    return true;
                case bool boolean:
                    number = boolean ? 1m : 0m;
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
                    return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
                case IConvertible convertible:
                    number = convertible.ToDecimal(CultureInfo.InvariantCulture);
                    return true;
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

    private static bool PutIfChanged(IDictionary<string, object?> context, string name, object? value)
    {
        if (context.TryGetValue(name, out var existing) && AreLookupValuesEqual(existing, value))
        {
            return false;
        }

        context[name] = value;
        return true;
    }

    private static void SeedKnownDerivedInputs(IDictionary<string, object?> context)
    {
        if (!context.ContainsKey("$car_type")
            && context.TryGetValue("cap", out var capacityValue)
            && TryConvertDecimal(capacityValue, out var capacity))
        {
            var suffix = decimal.ToInt32(decimal.Truncate(capacity)).ToString("D4", CultureInfo.InvariantCulture);
            var variantName = $"$car_type_{suffix}";
            if (context.TryGetValue(variantName, out var carType))
            {
                context["$car_type"] = carType;
            }
        }

        if (!context.ContainsKey("speed")
            && context.TryGetValue("cap", out capacityValue)
            && TryConvertDecimal(capacityValue, out capacity))
        {
            var suffix = decimal.ToInt32(decimal.Truncate(capacity)).ToString("D4", CultureInfo.InvariantCulture);
            var variantName = $"$speed_{suffix}";
            if (context.TryGetValue(variantName, out var speed)
                && TryConvertDecimal(speed, out var speedNumber))
            {
                context["speed"] = speedNumber;
            }
        }
    }

    private static void SeedImplicitTemplateConstants(
        DrawingTemplate template,
        IDictionary<string, object?> context)
    {
        if (context.ContainsKey("S_HF"))
        {
            return;
        }

        var heightMessage = template.CalculatedVariables.FirstOrDefault(variable =>
            string.Equals(variable.Name, "$r_HF_text", StringComparison.OrdinalIgnoreCase));
        if (heightMessage?.DefaultValue is not { ValueKind: JsonValueKind.String } defaultValue)
        {
            return;
        }

        var match = MinimumFloorHeightRegex().Match(defaultValue.GetString() ?? string.Empty);
        if (match.Success
            && decimal.TryParse(
                match.Groups["height"].Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var minimumHeight))
        {
            context["S_HF"] = minimumHeight;
        }
    }

    private static string ToExpressionLiteral(object? value)
    {
        return value switch
        {
            null => "0",
            bool boolean => boolean ? "1" : "0",
            string text => JsonSerializer.Serialize(text),
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => JsonSerializer.Serialize(value)
        };
    }

    private static string LookupAlias(string fieldName)
    {
        var builder = new StringBuilder("__lookup_");
        foreach (var character in fieldName)
        {
            builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        }

        return builder.ToString();
    }

    [GeneratedRegex(
        @"^(?<table>[\p{L}_][\p{L}\p{N}_]*)\.(?<field>[\p{L}_][\p{L}\p{N}_]*)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex LookupTargetRegex();

    [GeneratedRegex(
        @"(?:≥|>=)\s*(?<height>\d{3,5})",
        RegexOptions.CultureInvariant)]
    private static partial Regex MinimumFloorHeightRegex();
}
