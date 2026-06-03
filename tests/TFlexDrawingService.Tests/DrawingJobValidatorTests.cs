using System.Text.Json;
using TFlexDrawingService.Core.Models;
using TFlexDrawingService.Core.Requests;
using TFlexDrawingService.Core.Services;
using TFlexDrawingService.Tests.Support;

namespace TFlexDrawingService.Tests;

public sealed class DrawingJobValidatorTests
{
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

    private static Dictionary<string, JsonElement> JsonParameters(string json)
    {
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
    }
}
