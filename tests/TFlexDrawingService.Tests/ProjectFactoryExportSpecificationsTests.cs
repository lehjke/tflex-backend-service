using System.Text.Json;
using System.IO.Compression;
using System.Xml.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using TFlexDrawingService.Api.Data;
using TFlexDrawingService.Core.Models;
using TFlexDrawingService.Core.Services;
using TFlexDrawingService.Tests.Support;

namespace TFlexDrawingService.Tests;

public sealed class ProjectFactoryExportSpecificationsTests
{
    [Fact]
    public async Task BuildAsync_UsesNewestLinkedPriceAndMapsDrawingOnlySupplierFields()
    {
        var smec = Template("lehy_l_pro_320_1050", "cap", "stops", "JJ", "NE", "s_top_rear_1", "s01_front_1", "s01_rear_1", "s02_front_1", "s02_rear_1");
        smec.Parameters.Add(new DrawingParameterDefinition { Name = "speed", Type = "number", IsReadOnly = true, SubmitWhenDisabled = true, Expression = "1.75" });
        var xizi = Template("un_victor_mrl", "$CARTYPE_MENU", "$V", "$N", "$R", "NBENT_MENU", "OP", "OPH", "CH", "HW", "WTW", "K", "S", "$DOOR_MENU", "$CEILTYPE", "$EFS", "$CWT");
        var catalog = new InMemoryTemplateCatalog(smec, xizi);
        var now = DateTimeOffset.UtcNow;
        var smecConfig = Configuration("drawing-smec", "L1", smec.Id, new { cap = 1000, stops = 3, JJ = 900, NE = 2, s_top_rear_1 = 1, s01_front_1 = 1, s01_rear_1 = 0, s02_front_1 = 1, s02_rear_1 = 1 }, now);
        var xiziConfig = Configuration("drawing-xizi", "L2", xizi.Id, new Dictionary<string, object> { ["$CARTYPE_MENU"] = "13D / 1000 / 1100×2100", ["$V"] = "1.75", ["$N"] = "10", ["$R"] = "27.9", ["NBENT_MENU"] = 1, ["OP"] = 900, ["OPH"] = 2000, ["CH"] = 2400, ["HW"] = 1800, ["WTW"] = 2700, ["K"] = 5300, ["S"] = 1900, ["$DOOR_MENU"] = "TLD", ["$CEILTYPE"] = "Structured", ["$EFS"] = "EFS2", ["$CWT"] = "WSAFE" }, now);
        var old = Saved("old", "L1", "drawing-smec", now.AddMinutes(-1));
        var newest = Saved("new", "L1", "drawing-smec", now);
        var standalone = Saved("standalone", "Standalone", null, now);

        var result = await ProjectFactoryExportSpecifications.BuildAsync([smecConfig, xiziConfig], [old, newest, standalone], catalog, new DrawingJobValidator(catalog), "XIZI");

        Assert.Equal(["drawing-drawing-xizi"], result.Select(item => item.Id));
        var drawing = result.Single(item => item.Id == "drawing-drawing-xizi");
        var request = JsonSerializer.Deserialize<PricingCalculationRequest>(drawing.RequestJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("XIZI", request.Supplier); Assert.Equal("UN-Victor MRL", request.Series);
        Assert.Equal(1000, request.CapacityKg); Assert.Equal(1.75m, request.Speed); Assert.Equal(10, request.Stops);
        Assert.Equal(10, request.DoorCount); Assert.Equal("2S", request.DoorType); Assert.Equal("27900", request.SpecificationFields!["Travel Height"]);
        Assert.Equal("1100", request.SpecificationFields["Car Width"]); Assert.Equal("2100", request.SpecificationFields["Car Depth"]);
        Assert.True(request.Efs); Assert.Contains("CWTSAFETY", request.Options!); Assert.Contains("Ceiling from drawing: Structured", request.Options!);
        Assert.Equal("null", drawing.CalculationJson); Assert.Contains(request.Options!, option => option.Contains("confirmation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildAsync_UsesDrawingDoorTogglesAndRejectsUnknownTemplate()
    {
        var template = Template("lehy_l_pro_320_1050", "cap", "stops", "JJ", "NE", "s_top_rear_1", "s01_front_1", "s01_rear_1", "s02_front_1", "s02_rear_1");
        template.Parameters.Add(new DrawingParameterDefinition { Name = "speed", Type = "number", IsReadOnly = true, SubmitWhenDisabled = true, Expression = "1.75" });
        var catalog = new InMemoryTemplateCatalog(template);
        var now = DateTimeOffset.UtcNow;
        var config = Configuration("drawing", "L1", template.Id, new { cap = 1000, stops = 3, JJ = 900, NE = 2, s_top_rear_1 = 1, s01_front_1 = 1, s01_rear_1 = 0, s02_front_1 = 1, s02_rear_1 = 1 }, now);

        var result = await ProjectFactoryExportSpecifications.BuildAsync([config], [], catalog, new DrawingJobValidator(catalog), "SMEC");
        var request = JsonSerializer.Deserialize<PricingCalculationRequest>(result.Single().RequestJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("SMEC", request.Supplier); Assert.Equal("LEHY-L-Pro", request.Series); Assert.Equal(5, request.DoorCount);
        var unsupported = Configuration("unsupported", "L2", "other", new { cap = 1 }, now);
        Assert.Empty(await ProjectFactoryExportSpecifications.BuildAsync([unsupported], [], new InMemoryTemplateCatalog(Template("other", "cap")), new DrawingJobValidator(new InMemoryTemplateCatalog(Template("other", "cap"))), "SMEC"));
    }

    [Fact]
    public async Task BuildAsync_AddsFeOnlyWhenDrawingPppIsTruthy()
    {
        var template = Template("lehy_l_pro_320_1050", "cap", "stops", "JJ", "$PPP");
        template.Parameters.Add(new DrawingParameterDefinition { Name = "speed", Type = "number", IsReadOnly = true, SubmitWhenDisabled = true, Expression = "1" });
        var catalog = new InMemoryTemplateCatalog(template);
        var now = DateTimeOffset.UtcNow;
        var enabled = Configuration("enabled", "L1", template.Id, new Dictionary<string, object> { ["cap"] = 1000, ["stops"] = 3, ["JJ"] = 900, ["$PPP"] = "Да" }, now);
        var disabled = Configuration("disabled", "L2", template.Id, new { cap = 1000, stops = 3, JJ = 900 }, now);

        var specs = await ProjectFactoryExportSpecifications.BuildAsync([enabled, disabled], [], catalog, new DrawingJobValidator(catalog), "SMEC");
        var requests = specs.Select(spec => JsonSerializer.Deserialize<PricingCalculationRequest>(spec.RequestJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!).ToArray();

        Assert.Contains("FE", requests[0].Options!);
        Assert.DoesNotContain("FE", requests[1].Options!);
    }

    [Fact]
    public async Task BuildAsync_UsesOmittedSmecTemplateDefaultsForExportFields()
    {
        var template = Template("lehy_l_pro_320_1050", "cap", "stops", "JJ", "AH_1", "BH_1", "OH_1", "PD_1", "AA_1", "BB_1", "main_floor");
        void SetDefault(string name, string value) => template.Parameters.Single(parameter => parameter.Name == name).DefaultValue = JsonDocument.Parse(value).RootElement.Clone();
        SetDefault("AH_1", "1800"); SetDefault("BH_1", "2700"); SetDefault("OH_1", "5300"); SetDefault("PD_1", "1900");
        SetDefault("AA_1", "1100"); SetDefault("BB_1", "2100"); SetDefault("main_floor", "2");
        template.Parameters.Add(new DrawingParameterDefinition { Name = "speed", Type = "number", IsReadOnly = true, SubmitWhenDisabled = true, Expression = "1" });
        var catalog = new InMemoryTemplateCatalog(template);
        var configuration = Configuration("drawing", "L1", template.Id, new { cap = 1000, stops = 3, JJ = 900 }, DateTimeOffset.UtcNow);

        var specification = (await ProjectFactoryExportSpecifications.BuildAsync([configuration], [], catalog, new DrawingJobValidator(catalog), "SMEC")).Single();
        var request = JsonSerializer.Deserialize<PricingCalculationRequest>(specification.RequestJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal("1800", request.SpecificationFields!["AH"]);
        Assert.Equal("2700", request.SpecificationFields["BH"]);
        Assert.Equal("5300", request.SpecificationFields["OH"]);
        Assert.Equal("1900", request.SpecificationFields["PD"]);
        Assert.Equal("1100", request.SpecificationFields["AA"]);
        Assert.Equal("2100", request.SpecificationFields["BB"]);
        Assert.Equal("2", request.SpecificationFields["Main Floor"]);

        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/TFlexDrawingService.Api"));
        var store = new PricingCatalogStore(new TestEnvironment(root), new TestHttpClientFactory());
        using var workbook = new ZipArchive(new MemoryStream(store.BuildSmecProjectExport([specification], null)), ZipArchiveMode.Read);
        var sheet = XDocument.Load(workbook.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        static string Cell(XDocument xml, string reference) => xml.Descendants().Single(cell =>
            cell.Name.LocalName == "c" && cell.Attribute("r")?.Value == reference).Descendants()
            .Single(value => value.Name.LocalName is "v" or "t").Value;
        Assert.Equal("2", Cell(sheet, "D10"));
        Assert.Equal("1800", Cell(sheet, "D12"));
        Assert.Equal("2700", Cell(sheet, "F12"));
        Assert.Equal("5300", Cell(sheet, "F13"));
        Assert.Equal("1900", Cell(sheet, "H13"));
        Assert.Equal("1100", Cell(sheet, "D15"));
        Assert.Equal("2100", Cell(sheet, "F15"));
    }

    [Fact]
    public async Task BuildAsync_DeduplicatesLinkedAndLegacySpecificationsWithoutPunctuationCollisions()
    {
        var template = Template("lehy_l_pro_320_1050", "cap", "stops", "JJ");
        template.Parameters.Add(new DrawingParameterDefinition { Name = "speed", Type = "number", IsReadOnly = true, SubmitWhenDisabled = true, Expression = "1" });
        var catalog = new InMemoryTemplateCatalog(template);
        var now = DateTimeOffset.UtcNow;
        var first = Configuration("first", "L1.1", template.Id, new { cap = 1000, stops = 2, JJ = 900 }, now);
        var second = Configuration("second", "L11", template.Id, new { cap = 1000, stops = 2, JJ = 900 }, now);
        var old = Saved("old", "Linked", "second", now.AddMinutes(-1));
        var newest = Saved("new", "Linked", "second", now);
        var legacy = Saved("legacy", "L1.1", null, now);
        var standalone = Saved("standalone", "Other", null, now);

        var result = await ProjectFactoryExportSpecifications.BuildAsync([first, second], [old, newest, legacy, standalone], catalog, new DrawingJobValidator(catalog), "SMEC");

        Assert.Equal(["new", "legacy", "standalone"], result.Select(item => item.Id));
    }

    [Fact]
    public async Task BuildAsync_ExportsEscalatorWithoutInventingElevatorPricingFields()
    {
        var template = Template("k_ii_type", "N", "W", "HE", "V_v");
        template.Parameters[0].DisplayName = "Quantity";
        template.Parameters[1].DisplayName = "Step width";
        template.Parameters[2].DisplayName = "Rise";
        template.Parameters[3].DisplayName = "Speed";
        template.Parameters[3].Type = "number";
        var catalog = new InMemoryTemplateCatalog(template);
        var configuration = Configuration("escalator", "E5", template.Id, new { N = 2, W = 1000, HE = 3900, V_v = 0.5 }, DateTimeOffset.UtcNow);

        var specification = (await ProjectFactoryExportSpecifications.BuildAsync([configuration], [], catalog, new DrawingJobValidator(catalog), "SMEC")).Single();
        var request = JsonSerializer.Deserialize<PricingCalculationRequest>(specification.RequestJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal("K-II", request.Series); Assert.Equal(0, request.CapacityKg); Assert.Equal(0, request.Stops); Assert.Equal(0, request.DoorWidthMm);
        Assert.Equal("2", request.SpecificationFields!["Quantity"]); Assert.Contains("Step width: 1000", request.SpecificationFields["Other Requirements"]);
    }

    [Fact]
    public void XiziExport_BlanksPricesForDrawingOnlySpecification()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/TFlexDrawingService.Api"));
        var store = new PricingCatalogStore(new TestEnvironment(root), new TestHttpClientFactory());
        var request = new PricingCalculationRequest("XIZI", "UN-Victor MRL", 1000, 1m, 5, 900, "CO", null, 5, 0, null, [], false, false, "CNY", "project", "drawing", new Dictionary<string, string> { ["Quantity"] = "1" }, "L1");
        var specification = new PricingSpecification("drawing", "project", "drawing", "L1", "XIZI", "UN-Victor MRL", "drawing-only", 0, "CNY", 0, JsonSerializer.Serialize(request), "null", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var export = store.BuildXiziProjectExport([specification], null);

        using var archive = new ZipArchive(new MemoryStream(export), ZipArchiveMode.Read);
        using var prices = new ZipArchive(archive.GetEntry("XIZI-prices.xlsx")!.Open(), ZipArchiveMode.Read);
        var sheet = XDocument.Load(prices.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        Assert.DoesNotContain(sheet.Descendants(), cell => (string?)cell.Attribute("r") is "I2" or "J2" && cell.Descendants().Any(element => element.Name.LocalName == "v"));
        Assert.Contains("Incomplete: drawing-only units require pricing confirmation.", sheet.ToString());
    }

    [Fact]
    public void SmecExport_UsesDrawingDefaultsFireRatingAndPreservesEscalatorNotes()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/TFlexDrawingService.Api"));
        var store = new PricingCatalogStore(new TestEnvironment(root), new TestHttpClientFactory());
        static PricingSpecification Spec(string id, string series, Dictionary<string, string> fields, string[] options) => new(
            id, "project", null, id, "SMEC", series, "ready", 1, "CNY", 1,
            JsonSerializer.Serialize(new PricingCalculationRequest("SMEC", series, 1000, 1, 3, 900, "CO", null, 3, 0, null, options, false, false, "CNY", "project", id, fields, id)),
            "null", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var options = new[] { "ABP", "OLHL", "BA", "ITV", "MELD", "MBS", "AAN-S", "AECC", "ACB", "AHC", "FER", "FERC" };
        var specs = new[]
        {
            Spec("L1", "LEHY-L-Pro", new Dictionary<string, string>(), options.Append("CWTSAFETY").ToArray()),
            Spec("L2", "LEHY-Pro", new Dictionary<string, string> { ["Fire Rating"] = "EI30" }, options),
            Spec("E1", "K-II", new Dictionary<string, string> { ["Other Requirements"] = "Step width: 1000\nFactory note: retain" }, [])
        };

        using var archive = new ZipArchive(new MemoryStream(store.BuildSmecProjectExport(specs, null)), ZipArchiveMode.Read);
        static string Cell(ZipArchive archive, int sheetNumber, string address)
        {
            var sheet = XDocument.Load(archive.GetEntry($"xl/worksheets/sheet{sheetNumber}.xml")!.Open());
            return sheet.Descendants().Single(cell => cell.Name.LocalName == "c" && cell.Attribute("r")?.Value == address)
                .Descendants().FirstOrDefault(value => value.Name.LocalName is "v" or "t")?.Value ?? "";
        }
        Assert.Equal(string.Join(", ", options), Cell(archive, 1, "B30"));
        Assert.Equal("EI60", Cell(archive, 1, "B31"));
        Assert.Equal("CWT Safety Gear", Cell(archive, 1, "B32"));
        Assert.Equal("EI30", Cell(archive, 2, "B31"));
        Assert.Equal("Step width: 1000", Cell(archive, 3, "A9"));
        Assert.Equal("Factory note: retain", Cell(archive, 3, "A10"));
    }

    private static DrawingTemplate Template(string id, params string[] names) => new()
    {
        Id = id, Code = id, OutputFormats = ["pdf"], Parameters = names.Select(name => new DrawingParameterDefinition { Name = name, Type = name.StartsWith('$') ? "string" : "integer" }).ToList()
    };
    private static ProjectConfiguration Configuration(string id, string name, string templateId, object parameters, DateTimeOffset now) => new(id, "project", "owner", name, templateId, "pdf", JsonSerializer.Serialize(parameters), now, now);
    private static PricingSpecification Saved(string id, string name, string? configurationId, DateTimeOffset now) => new(id, "project", configurationId, name, "SMEC", "LEHY-L-Pro", "ready", 1, "CNY", 1, "{}", "{}", now, now);

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(root);
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory { public HttpClient CreateClient(string name) => new(); }
}
