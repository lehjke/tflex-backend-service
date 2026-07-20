using System.Text.Json;
using TFlexDrawingService.Core.Models;
using TFlexDrawingService.Core.Services;

namespace TFlexDrawingService.Tests;

public sealed class ExpressionResourceLimitTests
{
    [Fact]
    public void TryEvaluateExpression_RejectsOversizedStringResult()
    {
        var variables = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["$TEXT"] = new string('x', 40 * 1024)
        };

        var evaluated = SafeTFlexExpressionEvaluator.TryEvaluateExpression(
            "$TEXT + $TEXT",
            variables,
            out _);

        Assert.False(evaluated);
    }

    [Fact]
    public void TryEvaluateExpression_RejectsExcessiveCumulativeStringWork()
    {
        var variables = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["$TEXT"] = "x"
        };
        var expression = string.Join(
            " + ",
            Enumerable.Repeat("$TEXT", 900));

        var evaluated = SafeTFlexExpressionEvaluator.TryEvaluateExpression(
            expression,
            variables,
            out _);

        Assert.False(evaluated);
    }

    [Fact]
    public void Build_StopsLookupWhenAggregateRowBudgetIsExhausted()
    {
        var template = new DrawingTemplate
        {
            Id = "lookup-budget",
            Code = "LOOKUP-BUDGET",
            Name = "Lookup budget",
            CalculatedVariables =
            [
                new DrawingParameterDefinition
                {
                    Name = "RESULT",
                    Type = "number",
                    Expression = "find(T.VALUE, T.KEY == AH)"
                }
            ],
            LookupTables = new Dictionary<string, List<Dictionary<string, JsonElement>>>
            {
                ["T"] =
                [
                    new Dictionary<string, JsonElement>
                    {
                        ["KEY"] = JsonSerializer.SerializeToElement(1),
                        ["VALUE"] = JsonSerializer.SerializeToElement(10)
                    },
                    new Dictionary<string, JsonElement>
                    {
                        ["KEY"] = JsonSerializer.SerializeToElement(2),
                        ["VALUE"] = JsonSerializer.SerializeToElement(20)
                    }
                ]
            }
        };
        var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["AH"] = 2m
        };

        var exhausted = TemplateExpressionContextBuilder.Build(
            template,
            parameters,
            maxLookupRowEvaluations: 1);
        var sufficient = TemplateExpressionContextBuilder.Build(
            template,
            parameters,
            maxLookupRowEvaluations: 2);

        Assert.DoesNotContain("RESULT", exhausted.Keys);
        Assert.Equal(20m, sufficient["RESULT"]);
    }
}
