using System.Text.Json;
using TFlexDrawingService.Core.Models;
using TFlexDrawingService.Infrastructure.Automation;

namespace TFlexDrawingService.Tests;

public sealed class TemplateDraftBuilderTests
{
    [Fact]
    public void Build_ExposesOnlyVisibleExternalVariables_AndKeepsCalculatedVariablesAutomatic()
    {
        var inspection = new TFlexTemplateInspection
        {
            Variables =
            [
                new TFlexVariableInspection
                {
                    Name = "AA",
                    External = true,
                    IsReal = true,
                    IsUsed = true,
                    Value = "1600",
                    Comment = "Ширина кабины",
                    Unit = "mm"
                },
                new TFlexVariableInspection
                {
                    Name = "AH",
                    IsReal = true,
                    IsUsed = true,
                    Expression = "AA+625",
                    Value = "2225"
                },
                new TFlexVariableInspection
                {
                    Name = "internal_unused",
                    IsReal = true,
                    Expression = "1",
                    Value = "1"
                },
                new TFlexVariableInspection
                {
                    Name = "secret_external",
                    External = true,
                    Hidden = true,
                    IsReal = true,
                    Value = "10"
                }
            ]
        };

        var result = new TemplateDraftBuilder().Build(
            CreateJob("Generic.grb"),
            inspection,
            []);

        var parameter = Assert.Single(result.Template.Parameters);
        Assert.Equal("AA", parameter.Name);
        Assert.Equal(1600m, parameter.DefaultValue?.GetDecimal());
        var calculated = Assert.Single(result.Template.CalculatedVariables);
        Assert.Equal("AH", calculated.Name);
        Assert.True(calculated.IsReadOnly);
        Assert.True(calculated.SubmitWhenDisabled);
        Assert.Equal("AA+625", calculated.Expression);
        Assert.DoesNotContain(
            result.Template.Parameters,
            item => item.Name == "secret_external");
        Assert.Contains(result.Warnings, item => item.Contains("secret_external", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_UsesLehyReferenceForUnitsRangesVisibilityAndValidationRules()
    {
        var reference = new DrawingTemplate
        {
            Id = "lehy_l_pro_320_1050",
            Code = "lehy_l_pro_320_1050",
            Name = "LEHY-L-PRO [320-1050]",
            Parameters =
            [
                new DrawingParameterDefinition
                {
                    Name = "speed",
                    DisplayName = "Вертикальный разрез / Скорость",
                    Type = "number",
                    Unit = "м/с",
                    MinValue = 1,
                    MaxValue = 2.5m,
                    IsRequired = true,
                    LevelExpression = "cap>=1050 ? 1 : -1"
                }
            ],
            ValidationRules =
            [
                new DrawingValidationRule
                {
                    Name = "speed_range",
                    Expression = "speed<=2.5",
                    Message = "Скорость вне диапазона",
                    FieldNames = ["speed"]
                }
            ]
        };
        var inspection = new TFlexTemplateInspection
        {
            Variables =
            [
                new TFlexVariableInspection
                {
                    Name = "speed",
                    External = true,
                    IsReal = true,
                    IsUsed = true,
                    Value = "2.5"
                }
            ]
        };

        var result = new TemplateDraftBuilder().Build(
            CreateJob("LEHY-L-PRO [320-1050] new.grb"),
            inspection,
            [reference]);

        var definition = Assert.Single(result.Template.Parameters);
        Assert.Equal(reference.Parameters[0].DisplayName, definition.DisplayName);
        Assert.Equal(1, definition.MinValue);
        Assert.Equal(2.5m, definition.MaxValue);
        Assert.Equal("м/с", definition.Unit);
        Assert.Equal("cap>=1050 ? 1 : -1", definition.LevelExpression);
        Assert.Single(result.Template.ValidationRules);
        Assert.Contains(result.Warnings, item => item.Contains(reference.Name, StringComparison.Ordinal));
    }

    [Fact]
    public void Build_UsesTflexAllowedValuesAndControlVisibility()
    {
        var inspection = new TFlexTemplateInspection
        {
            Variables =
            [
                new TFlexVariableInspection
                {
                    Name = "$door_type",
                    External = true,
                    IsText = true,
                    IsUsed = true,
                    Value = "\"ТО\"",
                    AllowedValues = ["ТО", "ТС"]
                }
            ],
            Controls =
            [
                new TFlexControlInspection
                {
                    Variable = "$door_type",
                    Level = "cap>=630 ? 1 : -1"
                }
            ]
        };

        var result = new TemplateDraftBuilder().Build(CreateJob("Door.grb"), inspection, []);

        var definition = Assert.Single(result.Template.Parameters);
        Assert.Equal("enum", definition.Type);
        Assert.Equal(["ТО", "ТС"], definition.AllowedValues);
        Assert.Equal("ТО", definition.DefaultValue?.GetString());
        Assert.Equal("cap>=630 ? 1 : -1", definition.LevelExpression);
    }

    private static TemplateAnalysisJob CreateJob(string fileName)
    {
        return new TemplateAnalysisJob
        {
            Id = "1234567890abcdef1234567890abcdef",
            OriginalTemplateFileName = fileName
        };
    }
}
