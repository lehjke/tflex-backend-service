using System.Text.Json;
using TFlexDrawingService.Core.Models;
using TFlexDrawingService.Core.Requests;
using TFlexDrawingService.Core.Services;
using TFlexDrawingService.Tests.Support;

namespace TFlexDrawingService.Tests;

public sealed class DrawingJobValidatorTests
{
    [Fact]
    public async Task ValidateAsync_RejectsNullParameters()
    {
        var validator = new DrawingJobValidator(new InMemoryTemplateCatalog(CreateTemplate()));

        var result = await validator.ValidateAsync(new CreateDrawingJobRequest
        {
            TemplateId = "frame",
            OutputFormat = "pdf",
            Parameters = null!
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Parameters", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateAsync_RejectsUnknownParameter()
    {
        var validator = new DrawingJobValidator(new InMemoryTemplateCatalog(CreateTemplate()));

        var result = await validator.ValidateAsync(new CreateDrawingJobRequest
        {
            TemplateId = "frame",
            OutputFormat = "pdf",
            Parameters = JsonParameters("""
                {
                  "WIDTH": 1000,
                  "HEIGHT": 800,
                  "MATERIAL": "Сталь",
                  "EXTRA": 1
                }
                """)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Unknown parameter 'EXTRA'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateAsync_RejectsNumberOutsideRange()
    {
        var validator = new DrawingJobValidator(new InMemoryTemplateCatalog(CreateTemplate()));

        var result = await validator.ValidateAsync(new CreateDrawingJobRequest
        {
            TemplateId = "frame",
            OutputFormat = "pdf",
            Parameters = JsonParameters("""
                {
                  "WIDTH": 10,
                  "HEIGHT": 800,
                  "MATERIAL": "Сталь"
                }
                """)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("WIDTH", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateAsync_NormalizesValidParameters()
    {
        var validator = new DrawingJobValidator(new InMemoryTemplateCatalog(CreateTemplate()));

        var result = await validator.ValidateAsync(new CreateDrawingJobRequest
        {
            TemplateId = "frame",
            OutputFormat = ".PDF",
            Parameters = JsonParameters("""
                {
                  "WIDTH": 1200,
                  "HEIGHT": 900,
                  "MATERIAL": "Алюминий"
                }
                """)
        });

        Assert.True(result.IsValid);
        Assert.Equal("pdf", result.OutputFormat);
        Assert.Equal(1200m, result.NormalizedParameters["WIDTH"]);
        Assert.Equal("Алюминий", result.NormalizedParameters["MATERIAL"]);
    }

    [Theory]
    [InlineData("20,5", "20.5")]
    [InlineData("1,000", "1")]
    [InlineData("-0,125", "-0.125")]
    [InlineData("20.5", "20.5")]
    public async Task ValidateAsync_ParsesStringDecimalSeparatorsWithoutThousands(
        string input,
        string expected)
    {
        var template = CreateSingleParameterTemplate(new DrawingParameterDefinition
        {
            Name = "VALUE",
            Type = "number",
            IsRequired = true
        });
        var validator = new DrawingJobValidator(new InMemoryTemplateCatalog(template));

        var result = await validator.ValidateAsync(new CreateDrawingJobRequest
        {
            TemplateId = template.Id,
            OutputFormat = "pdf",
            Parameters = new Dictionary<string, JsonElement>
            {
                ["VALUE"] = JsonSerializer.SerializeToElement(input)
            }
        });

        Assert.True(result.IsValid);
        Assert.Equal(
            decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            result.NormalizedParameters["VALUE"]);
    }

    [Theory]
    [InlineData("1,000.5")]
    [InlineData("1.000,5")]
    [InlineData("1 000,5")]
    public async Task ValidateAsync_RejectsAmbiguousOrGroupedStringNumbers(string input)
    {
        var template = CreateSingleParameterTemplate(new DrawingParameterDefinition
        {
            Name = "VALUE",
            Type = "number",
            IsRequired = true
        });
        var validator = new DrawingJobValidator(new InMemoryTemplateCatalog(template));

        var result = await validator.ValidateAsync(new CreateDrawingJobRequest
        {
            TemplateId = template.Id,
            OutputFormat = "pdf",
            Parameters = new Dictionary<string, JsonElement>
            {
                ["VALUE"] = JsonSerializer.SerializeToElement(input)
            }
        });

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Contains("must be a number", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateAsync_RecalculatesSubmittedReadOnlyParameters()
    {
        var template = new DrawingTemplate
        {
            Id = "calculated",
            Code = "calculated",
            Name = "Calculated",
            OutputFormats = ["pdf"],
            Parameters =
            [
                new DrawingParameterDefinition
                {
                    Name = "INPUT",
                    Type = "integer",
                    IsRequired = true
                },
                new DrawingParameterDefinition
                {
                    Name = "DERIVED",
                    Type = "integer",
                    IsReadOnly = true,
                    SubmitWhenDisabled = true,
                    Expression = "INPUT + 10"
                },
                new DrawingParameterDefinition
                {
                    Name = "DISPLAY_ONLY",
                    Type = "integer",
                    IsReadOnly = true,
                    Expression = "INPUT + 20"
                }
            ],
            ValidationRules =
            [
                new DrawingValidationRule
                {
                    Name = "derived",
                    Expression = "DERIVED == 15",
                    Message = "Derived value is invalid."
                }
            ]
        };
        var validator = new DrawingJobValidator(new InMemoryTemplateCatalog(template));

        var result = await validator.ValidateAsync(new CreateDrawingJobRequest
        {
            TemplateId = template.Id,
            OutputFormat = "pdf",
            Parameters = JsonParameters("""
                {
                  "INPUT": 5,
                  "DERIVED": 999,
                  "DISPLAY_ONLY": 777
                }
                """)
        });

        Assert.True(result.IsValid);
        Assert.Equal(15m, result.NormalizedParameters["DERIVED"]);
        Assert.DoesNotContain("DISPLAY_ONLY", result.NormalizedParameters.Keys);
    }

    [Fact]
    public async Task ValidateAsync_RejectsFractionalCalculatedReadOnlyInteger()
    {
        var template = new DrawingTemplate
        {
            Id = "fractional-read-only",
            Code = "fractional-read-only",
            Name = "Fractional read-only",
            OutputFormats = ["pdf"],
            Parameters =
            [
                new DrawingParameterDefinition
                {
                    Name = "INPUT",
                    Type = "number",
                    IsRequired = true
                },
                new DrawingParameterDefinition
                {
                    Name = "DERIVED",
                    Type = "integer",
                    IsReadOnly = true,
                    SubmitWhenDisabled = true,
                    Expression = "INPUT * 1000"
                }
            ]
        };
        var validator = new DrawingJobValidator(new InMemoryTemplateCatalog(template));

        var result = await validator.ValidateAsync(new CreateDrawingJobRequest
        {
            TemplateId = template.Id,
            OutputFormat = "pdf",
            Parameters = JsonParameters("""{ "INPUT": 20.0005 }""")
        });

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Contains(
                "Read-only parameter 'DERIVED' must calculate to an integer",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateAsync_RejectsCalculatedReadOnlyIntegerOutsideDeclaredRange()
    {
        var template = new DrawingTemplate
        {
            Id = "bounded-read-only",
            Code = "bounded-read-only",
            Name = "Bounded read-only",
            OutputFormats = ["pdf"],
            Parameters =
            [
                new DrawingParameterDefinition
                {
                    Name = "INPUT",
                    Type = "integer",
                    IsRequired = true
                },
                new DrawingParameterDefinition
                {
                    Name = "DERIVED",
                    Type = "integer",
                    IsReadOnly = true,
                    SubmitWhenDisabled = true,
                    Expression = "INPUT + 10",
                    MaxValue = 15
                }
            ]
        };
        var validator = new DrawingJobValidator(new InMemoryTemplateCatalog(template));

        var result = await validator.ValidateAsync(new CreateDrawingJobRequest
        {
            TemplateId = template.Id,
            OutputFormat = "pdf",
            Parameters = JsonParameters("""{ "INPUT": 6 }""")
        });

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Contains("less than or equal to 15", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("integer")]
    [InlineData("number")]
    public async Task ValidateAsync_RejectsNumericValueOutsideAllowedValues(string parameterType)
    {
        var template = CreateSingleParameterTemplate(new DrawingParameterDefinition
        {
            Name = "NE",
            DisplayName = "Entrances",
            Type = parameterType,
            IsRequired = true,
            AllowedValues = ["1", "2"]
        });
        var validator = new DrawingJobValidator(new InMemoryTemplateCatalog(template));

        var result = await validator.ValidateAsync(new CreateDrawingJobRequest
        {
            TemplateId = template.Id,
            OutputFormat = "pdf",
            Parameters = JsonParameters("""{ "NE": 3 }""")
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("not allowed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateAsync_AcceptsIntegerInsideAllowedValues()
    {
        var template = CreateSingleParameterTemplate(new DrawingParameterDefinition
        {
            Name = "NE",
            DisplayName = "Entrances",
            Type = "integer",
            IsRequired = true,
            AllowedValues = ["1", "2"]
        });
        var validator = new DrawingJobValidator(new InMemoryTemplateCatalog(template));

        var result = await validator.ValidateAsync(new CreateDrawingJobRequest
        {
            TemplateId = template.Id,
            OutputFormat = "pdf",
            Parameters = JsonParameters("""{ "NE": 2 }""")
        });

        Assert.True(result.IsValid);
        Assert.Equal(2L, result.NormalizedParameters["NE"]);
    }

    [Fact]
    public async Task ValidateAsync_RejectsOversizedStringParameter()
    {
        var template = CreateSingleParameterTemplate(new DrawingParameterDefinition
        {
            Name = "$DESCRIPTION",
            DisplayName = "Description",
            Type = "string",
            IsRequired = true
        });
        var validator = new DrawingJobValidator(new InMemoryTemplateCatalog(template));
        var parameters = new Dictionary<string, JsonElement>
        {
            ["$DESCRIPTION"] = JsonSerializer.SerializeToElement(new string('x', 16 * 1024 + 1))
        };

        var result = await validator.ValidateAsync(new CreateDrawingJobRequest
        {
            TemplateId = template.Id,
            OutputFormat = "pdf",
            Parameters = parameters
        });

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Contains("must not exceed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateAsync_RejectsFailedSafeTemplateRule()
    {
        var template = CreateRuleTemplate("(!(AA-JJ>125)) ? 0 : 1");
        var validator = new DrawingJobValidator(new InMemoryTemplateCatalog(template));

        var result = await validator.ValidateAsync(new CreateDrawingJobRequest
        {
            TemplateId = template.Id,
            OutputFormat = "pdf",
            Parameters = JsonParameters("""{ "AA": 1100, "JJ": 1000 }""")
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("больше 125", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateAsync_InterpolatesSafeExpressionsInRuleMessage()
    {
        var template = CreateRuleTemplate("0");
        template.ValidationRules[0].Message =
            "AA-JJ = {AA-JJ}; half = {(AA-JJ)/2}; third = {(AA-JJ)/3}.";
        var validator = new DrawingJobValidator(new InMemoryTemplateCatalog(template));

        var result = await validator.ValidateAsync(new CreateDrawingJobRequest
        {
            TemplateId = template.Id,
            OutputFormat = "pdf",
            Parameters = JsonParameters("""{ "AA": 1100, "JJ": 1000 }""")
        });

        Assert.False(result.IsValid);
        Assert.Contains("AA-JJ = 100; half = 50; third = 33.333.", result.Errors);
        Assert.DoesNotContain(result.Errors, error => error.Contains('{', StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("warning", true)]
    [InlineData("WARNING", true)]
    [InlineData("error", false)]
    [InlineData("unsupported", false)]
    public async Task ValidateAsync_OnlyWarningSeverityIsNonBlocking(
        string severity,
        bool expectedValid)
    {
        var template = CreateRuleTemplate("0");
        template.ValidationRules[0].Severity = severity;
        var validator = new DrawingJobValidator(new InMemoryTemplateCatalog(template));

        var result = await validator.ValidateAsync(new CreateDrawingJobRequest
        {
            TemplateId = template.Id,
            OutputFormat = "pdf",
            Parameters = JsonParameters("""{ "AA": 1100, "JJ": 1000 }""")
        });

        Assert.Equal(expectedValid, result.IsValid);
        if (!expectedValid)
        {
            Assert.Contains(
                result.Errors,
                error => error.Contains("больше 125", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task ValidateAsync_AcceptsPassedSafeTemplateRule()
    {
        var template = CreateRuleTemplate("(!(AA-JJ>125)) ? 0 : 1");
        var validator = new DrawingJobValidator(new InMemoryTemplateCatalog(template));

        var result = await validator.ValidateAsync(new CreateDrawingJobRequest
        {
            TemplateId = template.Id,
            OutputFormat = "pdf",
            Parameters = JsonParameters("""{ "AA": 1100, "JJ": 900 }""")
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_RejectsUnsupportedOrIndeterminateRule()
    {
        var template = CreateRuleTemplate("find(TH.AA, missing==1) ? 0 : 1");
        var validator = new DrawingJobValidator(new InMemoryTemplateCatalog(template));

        var result = await validator.ValidateAsync(new CreateDrawingJobRequest
        {
            TemplateId = template.Id,
            OutputFormat = "pdf",
            Parameters = JsonParameters("""{ "AA": 1100, "JJ": 900 }""")
        });

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Contains("could not be evaluated safely", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(18, true)]
    [InlineData(19, false)]
    [InlineData(48, true)]
    [InlineData(49, false)]
    public async Task ValidateAsync_ComputesProductionStyleStopsLimit(
        int stops,
        bool expectedValid)
    {
        var template = CreateStopsRuleTemplate();
        var validator = new DrawingJobValidator(new InMemoryTemplateCatalog(template));

        var result = await validator.ValidateAsync(new CreateDrawingJobRequest
        {
            TemplateId = template.Id,
            OutputFormat = "pdf",
            Parameters = JsonParameters($$"""
                {
                  "cap": {{(stops <= 19 ? 1600 : 1800)}},
                  "speed": {{(stops <= 19 ? 1 : 2)}},
                  "stops": {{stops}}
                }
                """)
        });

        Assert.Equal(expectedValid, result.IsValid);
        if (!expectedValid)
        {
            Assert.Contains(result.Errors, error => error.Contains("остановок", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task ValidateAsync_ResolvesCalculatedVariableFromLookupTable()
    {
        var template = CreateLookupRuleTemplate();
        var validator = new DrawingJobValidator(new InMemoryTemplateCatalog(template));

        var result = await validator.ValidateAsync(new CreateDrawingJobRequest
        {
            TemplateId = template.Id,
            OutputFormat = "pdf",
            Parameters = JsonParameters("""
                {
                  "cap": 630,
                  "AH": 1200
                }
                """)
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("шахты", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_EvaluatesDirectLookupRuleWhenMatchPasses()
    {
        var template = CreateDirectLookupRuleTemplate(
            "find(T.VALUE, T.KEY == KEY) == 10",
            "Lookup value is invalid.");
        var validator = new DrawingJobValidator(new InMemoryTemplateCatalog(template));

        var result = await validator.ValidateAsync(new CreateDrawingJobRequest
        {
            TemplateId = template.Id,
            OutputFormat = "pdf",
            Parameters = JsonParameters("""{ "KEY": 1 }""")
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsRuleFailureWhenDirectLookupValueDoesNotPass()
    {
        var template = CreateDirectLookupRuleTemplate(
            "find(T.VALUE, T.KEY == KEY) == 20",
            "Lookup value is invalid.");
        var validator = new DrawingJobValidator(new InMemoryTemplateCatalog(template));

        var result = await validator.ValidateAsync(new CreateDrawingJobRequest
        {
            TemplateId = template.Id,
            OutputFormat = "pdf",
            Parameters = JsonParameters("""{ "KEY": 1 }""")
        });

        Assert.False(result.IsValid);
        Assert.Contains("Lookup value is invalid.", result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_TreatsMissingDirectLookupRowAsZero()
    {
        var template = CreateDirectLookupRuleTemplate(
            "find(T.VALUE, T.KEY == KEY) != 0",
            "Lookup row was not found.");
        var validator = new DrawingJobValidator(new InMemoryTemplateCatalog(template));

        var result = await validator.ValidateAsync(new CreateDrawingJobRequest
        {
            TemplateId = template.Id,
            OutputFormat = "pdf",
            Parameters = JsonParameters("""{ "KEY": 999 }""")
        });

        Assert.False(result.IsValid);
        Assert.Contains("Lookup row was not found.", result.Errors);
        Assert.DoesNotContain(
            result.Errors,
            error => error.Contains("could not be evaluated safely", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateAsync_SharesAggregateLookupBudgetAcrossContextAndAllRules()
    {
        var template = CreateDirectLookupRuleTemplate(
            "find(T.VALUE, T.KEY == KEY) == 10",
            "First lookup is invalid.");
        template.CalculatedVariables =
        [
            new DrawingParameterDefinition
            {
                Name = "CONTEXT_VALUE",
                Type = "integer",
                Expression = "find(T.VALUE, T.KEY == KEY)"
            }
        ];
        template.ValidationRules.Add(new DrawingValidationRule
        {
            Name = "second_lookup",
            Expression = "find(T.VALUE, T.KEY == KEY) == 10",
            Message = "Second lookup is invalid."
        });
        var catalog = new InMemoryTemplateCatalog(template);

        var exhausted = await new DrawingJobValidator(
            catalog,
            maxLookupRowEvaluations: 3).ValidateAsync(
            new CreateDrawingJobRequest
            {
                TemplateId = template.Id,
                OutputFormat = "pdf",
                Parameters = JsonParameters("""{ "KEY": 1 }""")
            });
        var sufficient = await new DrawingJobValidator(
            catalog,
            maxLookupRowEvaluations: 4).ValidateAsync(
            new CreateDrawingJobRequest
            {
                TemplateId = template.Id,
                OutputFormat = "pdf",
                Parameters = JsonParameters("""{ "KEY": 1 }""")
            });

        Assert.False(exhausted.IsValid);
        Assert.Contains(
            exhausted.Errors,
            error => error.Contains(
                "Validation rule 'second_lookup' could not be evaluated safely",
                StringComparison.Ordinal));
        Assert.DoesNotContain("First lookup is invalid.", exhausted.Errors);
        Assert.True(sufficient.IsValid);
    }

    private static DrawingTemplate CreateTemplate()
    {
        return new DrawingTemplate
        {
            Id = "frame",
            Code = "frame",
            Name = "Frame",
            OutputFormats = ["pdf", "dwg"],
            Parameters =
            [
                new DrawingParameterDefinition
                {
                    Name = "WIDTH",
                    DisplayName = "Width",
                    Type = "number",
                    IsRequired = true,
                    MinValue = 100,
                    MaxValue = 5000
                },
                new DrawingParameterDefinition
                {
                    Name = "HEIGHT",
                    DisplayName = "Height",
                    Type = "number",
                    IsRequired = true,
                    MinValue = 100,
                    MaxValue = 5000
                },
                new DrawingParameterDefinition
                {
                    Name = "MATERIAL",
                    DisplayName = "Material",
                    Type = "string",
                    IsRequired = true,
                    AllowedValues = ["Сталь", "Алюминий"]
                }
            ]
        };
    }

    private static DrawingTemplate CreateSingleParameterTemplate(DrawingParameterDefinition parameter)
    {
        return new DrawingTemplate
        {
            Id = "numeric-allowed-values",
            Code = "numeric-allowed-values",
            Name = "Numeric allowed values",
            OutputFormats = ["pdf"],
            Parameters = [parameter]
        };
    }

    private static DrawingTemplate CreateRuleTemplate(string expression)
    {
        return new DrawingTemplate
        {
            Id = "razvertki_lehy",
            Code = "razvertki_lehy",
            Name = "Razvertki Lehy",
            OutputFormats = ["pdf"],
            Parameters =
            [
                new DrawingParameterDefinition
                {
                    Name = "AA",
                    DisplayName = "Cabin width",
                    Type = "integer",
                    IsRequired = true
                },
                new DrawingParameterDefinition
                {
                    Name = "JJ",
                    DisplayName = "Door width",
                    Type = "integer",
                    IsRequired = true
                }
            ],
            ValidationRules =
            [
                new DrawingValidationRule
                {
                    Name = "e1",
                    Expression = expression,
                    Message = "· Разница ширины кабины и дверей должна быть больше 125мм."
                }
            ]
        };
    }

    private static DrawingTemplate CreateStopsRuleTemplate()
    {
        return new DrawingTemplate
        {
            Id = "lehy_pro_side_cwt",
            Code = "lehy_pro_side_cwt",
            Name = "LEHY PRO",
            OutputFormats = ["pdf"],
            Parameters =
            [
                new DrawingParameterDefinition
                {
                    Name = "cap",
                    Type = "integer",
                    IsRequired = true
                },
                new DrawingParameterDefinition
                {
                    Name = "speed",
                    Type = "number",
                    IsRequired = true
                },
                new DrawingParameterDefinition
                {
                    Name = "stops",
                    Type = "integer",
                    IsRequired = true
                }
            ],
            CalculatedVariables =
            [
                new DrawingParameterDefinition
                {
                    Name = "S_stops",
                    Type = "integer",
                    Expression = """
                        select(
                        (cap<=1600)&&(speed==1), 18,
                        (cap<=1600)&&(speed==1.6), 32,
                        (cap<=1600)&&(speed==1.75), 32,
                        (cap<=1600)&&(speed==2), 36,
                        (cap<=1600)&&(speed==2.5), 48,
                        (cap<=1600)&&(speed==3), 48,
                        (cap>1600)&&(speed==1), 18,
                        (cap>1600)&&(speed>1), 48
                        )
                        """
                },
                new DrawingParameterDefinition
                {
                    Name = "r_stops",
                    Type = "integer",
                    Expression = "stops<=S_stops ? 1 : error($r_stops_text)"
                },
                new DrawingParameterDefinition
                {
                    Name = "$r_stops_text",
                    Type = "string",
                    Expression = "\"Количество остановок превышает допустимый предел.\""
                }
            ],
            ValidationRules =
            [
                new DrawingValidationRule
                {
                    Name = "r_stops",
                    Expression = "stops<=S_stops ? 1 : error($r_stops_text)",
                    Message = "Количество остановок превышает допустимый предел."
                }
            ]
        };
    }

    private static DrawingTemplate CreateLookupRuleTemplate()
    {
        return new DrawingTemplate
        {
            Id = "lookup-template",
            Code = "lookup-template",
            Name = "Lookup template",
            OutputFormats = ["pdf"],
            Parameters =
            [
                new DrawingParameterDefinition
                {
                    Name = "cap",
                    Type = "integer",
                    IsRequired = true
                },
                new DrawingParameterDefinition
                {
                    Name = "AH",
                    Type = "integer",
                    IsRequired = true
                }
            ],
            CalculatedVariables =
            [
                new DrawingParameterDefinition
                {
                    Name = "min_AH",
                    Type = "integer",
                    Expression = "find(TH.AA, (TH.cap == cap)) + 100"
                }
            ],
            LookupTables = new Dictionary<string, List<Dictionary<string, JsonElement>>>
            {
                ["TH"] =
                [
                    JsonParameters("""{ "cap": 630, "AA": 1150 }"""),
                    JsonParameters("""{ "cap": 1050, "AA": 1600 }""")
                ]
            },
            ValidationRules =
            [
                new DrawingValidationRule
                {
                    Name = "r_AH",
                    Expression = "AH >= min_AH ? 1 : error(\"invalid\")",
                    Message = "Ширина шахты меньше допустимой."
                }
            ]
        };
    }

    private static DrawingTemplate CreateDirectLookupRuleTemplate(
        string expression,
        string message)
    {
        return new DrawingTemplate
        {
            Id = "direct-lookup-template",
            Code = "direct-lookup-template",
            Name = "Direct lookup template",
            OutputFormats = ["pdf"],
            Parameters =
            [
                new DrawingParameterDefinition
                {
                    Name = "KEY",
                    Type = "integer",
                    IsRequired = true
                }
            ],
            ValidationRules =
            [
                new DrawingValidationRule
                {
                    Name = "direct_lookup",
                    Expression = expression,
                    Message = message
                }
            ],
            LookupTables = new Dictionary<string, List<Dictionary<string, JsonElement>>>
            {
                ["T"] =
                [
                    JsonParameters("""{ "KEY": 1, "VALUE": 10 }"""),
                    JsonParameters("""{ "KEY": 2, "VALUE": 20 }""")
                ]
            }
        };
    }

    private static Dictionary<string, JsonElement> JsonParameters(string json)
    {
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
    }
}
