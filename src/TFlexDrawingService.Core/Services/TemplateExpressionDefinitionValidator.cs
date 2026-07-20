using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TFlexDrawingService.Core.Models;

namespace TFlexDrawingService.Core.Services;

public static partial class TemplateExpressionDefinitionValidator
{
    public static IReadOnlyList<TemplateExpressionDefinitionError> Validate(
        DrawingTemplate template)
    {
        var errors = new List<TemplateExpressionDefinitionError>();
        var definitions = BuildDefinitionIndex(template);
        var requiredDefinitions = new Queue<DefinitionReference>();
        var visitedDefinitions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < template.ValidationRules.Count; index++)
        {
            var rule = template.ValidationRules[index];
            if (rule is null || string.IsNullOrWhiteSpace(rule.Expression))
            {
                continue;
            }

            var field = $"manifest.validationRules[{index}].expression";
            ValidateExpression(rule.Expression, field, template, errors);
            EnqueueDependencies(
                rule.Expression,
                definitions,
                requiredDefinitions);
        }

        for (var index = 0; index < template.Parameters.Count; index++)
        {
            var parameter = template.Parameters[index];
            if (parameter is null
                || !parameter.IsReadOnly
                || !parameter.SubmitWhenDisabled
                || HasDefaultValue(parameter))
            {
                continue;
            }

            requiredDefinitions.Enqueue(new DefinitionReference(
                parameter,
                $"manifest.parameters[{index}].expression",
                IsParameter: true));
        }

        while (requiredDefinitions.TryDequeue(out var reference))
        {
            var definition = reference.Definition;
            if (string.IsNullOrWhiteSpace(definition.Name)
                || !visitedDefinitions.Add(definition.Name))
            {
                continue;
            }

            if (reference.IsParameter && HasDefaultValue(definition))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(definition.Expression))
            {
                if (!HasDefaultValue(definition)
                    && definition.LookupValues.Count == 0)
                {
                    errors.Add(new TemplateExpressionDefinitionError(
                        reference.Field,
                        "A server-required value has no default, lookup or expression."));
                }

                continue;
            }

            ValidateExpression(
                definition.Expression,
                reference.Field,
                template,
                errors);
            EnqueueDependencies(
                definition.Expression,
                definitions,
                requiredDefinitions);
        }

        return errors;
    }

    private static Dictionary<string, DefinitionReference> BuildDefinitionIndex(
        DrawingTemplate template)
    {
        var definitions = new Dictionary<string, DefinitionReference>(
            StringComparer.OrdinalIgnoreCase);
        AddDefinitions(
            template.Parameters,
            "manifest.parameters",
            isParameter: true,
            definitions);
        AddDefinitions(
            template.CalculatedVariables,
            "manifest.calculatedVariables",
            isParameter: false,
            definitions);
        return definitions;
    }

    private static void AddDefinitions(
        IReadOnlyList<DrawingParameterDefinition> source,
        string field,
        bool isParameter,
        IDictionary<string, DefinitionReference> destination)
    {
        for (var index = 0; index < source.Count; index++)
        {
            var definition = source[index];
            if (definition is null || string.IsNullOrWhiteSpace(definition.Name))
            {
                continue;
            }

            destination.TryAdd(
                definition.Name.Trim(),
                new DefinitionReference(
                    definition,
                    $"{field}[{index}].expression",
                    isParameter));
        }
    }

    private static void EnqueueDependencies(
        string expression,
        IReadOnlyDictionary<string, DefinitionReference> definitions,
        Queue<DefinitionReference> queue)
    {
        var withoutStrings = MaskQuotedText(expression);
        foreach (Match match in IdentifierRegex().Matches(withoutStrings))
        {
            var name = match.Groups["name"].Value;
            if (TryGetDefinition(definitions, name, out var definition))
            {
                queue.Enqueue(definition);
            }
        }
    }

    private static bool TryGetDefinition(
        IReadOnlyDictionary<string, DefinitionReference> definitions,
        string name,
        out DefinitionReference definition)
    {
        if (definitions.TryGetValue(name, out var direct))
        {
            definition = direct;
            return true;
        }

        var alias = name.StartsWith('$') ? name[1..] : $"${name}";
        if (definitions.TryGetValue(alias, out var aliased))
        {
            definition = aliased;
            return true;
        }

        definition = null!;
        return false;
    }

    private static void ValidateExpression(
        string expression,
        string field,
        DrawingTemplate template,
        ICollection<TemplateExpressionDefinitionError> errors)
    {
        if (!TemplateExpressionContextBuilder.TryValidateSupportedExpression(
                expression,
                template))
        {
            errors.Add(new TemplateExpressionDefinitionError(
                field,
                "Expression contains unsupported or invalid server-side T-FLEX syntax."));
        }
    }

    private static bool HasDefaultValue(DrawingParameterDefinition definition)
    {
        return definition.DefaultValue is { } value
            && value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;
    }

    private static string MaskQuotedText(string expression)
    {
        var builder = new StringBuilder(expression.Length);
        var quote = '\0';
        var escaped = false;
        foreach (var character in expression)
        {
            if (quote == '\0')
            {
                if (character is '\'' or '"')
                {
                    quote = character;
                    builder.Append(' ');
                }
                else
                {
                    builder.Append(character);
                }

                continue;
            }

            if (escaped)
            {
                escaped = false;
            }
            else if (character == '\\')
            {
                escaped = true;
            }
            else if (character == quote)
            {
                quote = '\0';
            }

            builder.Append(' ');
        }

        return builder.ToString();
    }

    [GeneratedRegex(
        @"(?<![\p{L}\p{N}_$.])(?<name>\$?[\p{L}_][\p{L}\p{N}_]*)(?![\p{L}\p{N}_.])",
        RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();

    private sealed record DefinitionReference(
        DrawingParameterDefinition Definition,
        string Field,
        bool IsParameter);
}

public sealed record TemplateExpressionDefinitionError(string Field, string Message);
