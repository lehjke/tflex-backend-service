using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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

        var p14r = Assert.Single(template.LookupTables["TH"], row =>
            row["cap"].GetInt32() == 1050
            && row["car_type"].GetString() == "P14R");
        Assert.Equal(2100, p14r["AA"].GetInt32());
        Assert.Equal(1100, p14r["BB"].GetInt32());
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
