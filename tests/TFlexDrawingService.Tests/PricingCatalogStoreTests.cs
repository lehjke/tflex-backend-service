using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using TFlexDrawingService.Api.Data;

namespace TFlexDrawingService.Tests;

public sealed class PricingCatalogStoreTests
{
    [Fact]
    public async Task XiziCalculation_UsesFormComponentsAndDerivedSurcharges()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        var dataDirectory = Path.Combine(root, "Data");
        Directory.CreateDirectory(dataDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(dataDirectory, "pricing-catalog.json"),
            """
            {
              "generatedAt": "2026-06-24T00:00:00Z",
              "currency": "CNY",
              "xizi": {
                "series": ["UN-Victor MRL"],
                "basePrices": [
                  {
                    "series": "UN-Victor MRL",
                    "capacity": 1000,
                    "speed": 1.0,
                    "stops": 5,
                    "price": 100000,
                    "extraRisePerMeter": 500
                  }
                ],
                "doors": [
                  { "manufacturer": "FERMATOR", "part": "Car door", "doorType": "CO", "fireRating": "E30", "finish": "AISI443", "capacity": 1000, "floor": "-", "width": 900, "price": 1000 },
                  { "manufacturer": "FERMATOR", "part": "2nd door", "doorType": "CO", "fireRating": "E30", "finish": "AISI443", "capacity": 1000, "floor": "-", "width": 900, "price": 2000 },
                  { "manufacturer": "FERMATOR", "part": "Shaft door", "doorType": "CO", "fireRating": "E30", "finish": "Painted steel", "capacity": 1000, "floor": "First", "width": 900, "price": 300 },
                  { "manufacturer": "FERMATOR", "part": "Shaft door", "doorType": "CO", "fireRating": "E30", "finish": "AISI443", "capacity": 1000, "floor": "Other", "width": 900, "price": 200 }
                ],
                "decorations": [
                  { "category": "Car walls", "code": "aisi-443", "height": 2400, "price": 500, "overprice": 200 },
                  { "category": "Ceiling", "code": "U-CL029", "price": 100, "overprice": 0 },
                  { "category": "Floor", "code": "U-FL033", "price": 200, "overprice": 0 },
                  { "category": "Mirror", "code": "Половина высоты", "price": 300, "overprice": 0 },
                  { "category": "Handrail", "code": "U-HR001", "price": 400, "overprice": 50 },
                  { "category": "COP", "code": "U-CY700", "price": 100, "overprice": 10 },
                  { "category": "Button", "code": "iBR34M(BL)", "price": 20, "overprice": 0 },
                  { "category": "LOP", "code": "LOP-M", "price": 30, "overprice": 0 },
                  { "category": "LOP", "code": "LOP-O", "price": 40, "overprice": 0 },
                  { "category": "LIP", "code": "LIP-M", "price": 50, "overprice": 0 },
                  { "category": "LIP", "code": "LIP-O", "price": 60, "overprice": 0 }
                ],
                "options": [
                  { "category": "Options", "code": "40HQ", "price": 8500, "prices": [8500] },
                  { "category": "Options", "code": "CCTV", "price": 10, "prices": [10] }
                ],
                "localRequirements": [
                  { "category": "LMR", "code": "Pit unlock device", "price": 77 },
                  { "category": "LMR", "code": "Pit Inspection box with European standard", "price": 1050 },
                  { "category": "LMR", "code": "Hoistway lighting by Factory", "price": 15 }
                ]
              },
              "smec": {}
            }
            """);

        try
        {
            var store = new PricingCatalogStore(
                new TestWebHostEnvironment(root),
                new TestHttpClientFactory());
            var result = await store.CalculateAsync(
                new PricingCalculationRequest(
                    "XIZI",
                    "UN-Victor MRL",
                    1000,
                    1m,
                    5,
                    900,
                    "CO",
                    "FERMATOR",
                    3,
                    0,
                    null,
                    ["CCTV"],
                    false,
                    false,
                    "CNY",
                    null,
                    null,
                    new Dictionary<string, string>
                    {
                        ["Travel Height"] = "13400",
                        ["Overhead"] = "4600",
                        ["Pit"] = "1500",
                        ["Shaft Depth"] = "2600",
                        ["Car Depth"] = "2100",
                        ["Car Width"] = "1900",
                        ["Car Height"] = "2400",
                        ["Car Type"] = "Проходная",
                        ["Door Height"] = "2200",
                        ["Fire Rating"] = "E30",
                        ["Car Door Material"] = "Нерж. сталь AISI443",
                        ["Main Shaft Door"] = "Окрашенная сталь RAL9006",
                        ["Other Shaft Door"] = "Нерж. сталь AISI443",
                        ["Cabin Design"] = "U-CR126-BASE",
                        ["Car Wall Material"] = "Нерж. сталь AISI443",
                        ["Ceiling"] = "U-CL029",
                        ["Floor"] = "U-FL033",
                        ["Mirror Wall"] = "Задняя стена",
                        ["Mirror Height"] = "Половина высоты",
                        ["Handrail Position"] = "2 х Боковые стены",
                        ["Handrail"] = "U-HR001",
                        ["COP"] = "U-CY700",
                        ["COP Button"] = "iBR34M(BL)",
                        ["Main LOP"] = "LOP-M",
                        ["Other LOP"] = "LOP-O",
                        ["Main LIP"] = "LIP-M",
                        ["Other LIP"] = "LIP-O",
                        ["AC"] = "Нет",
                        ["RCC"] = "Нет"
                    },
                    null));

            Assert.Equal("warning", result.Status);
            Assert.Equal(118124.34m, result.TotalCny);
            Assert.Contains(result.Lines, line => line.Label == "Вторая дверь проходной кабины" && line.AmountCny == 2000m);
            Assert.Contains(result.Lines, line => line.Label == "Превышение расчетной высоты, 1 м" && line.AmountCny == 500m);
            Assert.Contains(result.Lines, line => line.Label == "Кнопки COP: iBR34M(BL)" && line.AmountCny == 140m);
            Assert.Contains(result.Lines, line => line.Label == "Опция CCTV" && line.AmountCny == 340m);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task XiziNegativeOne_BlocksUnavailableConfiguration()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        var dataDirectory = Path.Combine(root, "Data");
        Directory.CreateDirectory(dataDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(dataDirectory, "pricing-catalog.json"),
            """
            {
              "generatedAt": "2026-06-24T00:00:00Z",
              "currency": "CNY",
              "xizi": {
                "basePrices": [
                  { "series": "G3", "capacity": 1000, "speed": 1.0, "stops": 5, "price": -1 }
                ],
                "options": [
                  { "category": "Options", "code": "40HQ", "price": 8500 }
                ]
              },
              "smec": {}
            }
            """);

        try
        {
            var store = new PricingCatalogStore(
                new TestWebHostEnvironment(root),
                new TestHttpClientFactory());
            var result = await store.CalculateAsync(
                new PricingCalculationRequest(
                    "XIZI",
                    "G3",
                    1000,
                    1m,
                    5,
                    0,
                    null,
                    null,
                    0,
                    0,
                    null,
                    [],
                    false,
                    false,
                    "CNY",
                    null,
                    null,
                    new Dictionary<string, string>(),
                    null));

            Assert.Equal("blocked", result.Status);
            Assert.Contains(result.Blockers, blocker => blocker.Contains("комбинация недоступна", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task MissingBasePrice_BlocksCalculation()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        var dataDirectory = Path.Combine(root, "Data");
        Directory.CreateDirectory(dataDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(dataDirectory, "pricing-catalog.json"),
            """
            {
              "generatedAt": "2026-06-24T00:00:00Z",
              "currency": "CNY",
              "xizi": { "basePrices": [] },
              "smec": {}
            }
            """);

        try
        {
            var store = new PricingCatalogStore(
                new TestWebHostEnvironment(root),
                new TestHttpClientFactory());
            var result = await store.CalculateAsync(
                new PricingCalculationRequest(
                    "XIZI",
                    "UNKNOWN",
                    1000,
                    1m,
                    5,
                    900,
                    "CO",
                    "FERMATOR",
                    5,
                    0,
                    null,
                    [],
                    false,
                    false,
                    "CNY",
                    null,
                    null,
                    new Dictionary<string, string>(),
                    null));

            Assert.Equal("blocked", result.Status);
            Assert.Contains(
                result.Blockers,
                blocker => blocker.Contains("обязательная цена", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Lines, line => line.Code == "base" && line.Status == "blocked");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("XIZI", true)]
    [InlineData("smec", true)]
    [InlineData("BOGUS", false)]
    [InlineData(null, false)]
    public void SupplierWhitelist_OnlyAllowsKnownCatalogs(string? supplier, bool expected)
    {
        Assert.Equal(expected, PricingCatalogStore.IsSupportedSupplier(supplier));
    }

    [Fact]
    public async Task SmecControlAndDisplays_UseKipQuantities()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        var dataDirectory = Path.Combine(root, "Data");
        Directory.CreateDirectory(dataDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(dataDirectory, "pricing-catalog.json"),
            """
            {
              "generatedAt": "2026-06-24T00:00:00Z",
              "currency": "CNY",
              "xizi": {},
              "smec": {
                "series": ["LEHY-L-Pro"],
                "basePrices": [
                  {
                    "series": "LEHY-L-Pro",
                    "capacity": 1050,
                    "speed": 1.0,
                    "basicStops": 5,
                    "basicPrice": 100000,
                    "pricePerStop": 0,
                    "overHeightPer1000": 0,
                    "pricePerDoor2D2G": 0
                  }
                ],
                "functions": [
                  { "code": "ABP", "price": 1360 }
                ],
                "groupControl": [
                  { "code": "2C-ITS-21", "price": 3890 }
                ],
                "controlPrices": [
                  { "category": "COP", "code": "ZCB-N612", "price": 1530 },
                  { "category": "Button", "code": "A71", "price": "¥240/floor" },
                  { "category": "HallIndicator", "code": "HID-A10", "price": 750 }
                ]
              }
            }
            """);

        try
        {
            var store = new PricingCatalogStore(
                new TestWebHostEnvironment(root),
                new TestHttpClientFactory());
            var result = await store.CalculateAsync(
                new PricingCalculationRequest(
                    "SMEC",
                    "LEHY-L-Pro",
                    1050,
                    1m,
                    5,
                    900,
                    null,
                    null,
                    5,
                    0,
                    null,
                    ["ABP"],
                    false,
                    false,
                    "CNY",
                    null,
                    null,
                    new Dictionary<string, string>
                    {
                        ["Operation"] = "2C-ITS-21",
                        ["COP"] = "ZCB■-N612",
                        ["COP Button"] = "A71",
                        ["Hall Indicator"] = "HID-A10"
                    },
                    null));

            Assert.Equal("ready", result.Status);
            Assert.Equal(110370m, result.TotalCny);
            Assert.Contains(result.Lines, line => line.Label == "Групповое управление 2C-ITS-21" && line.AmountCny == 3890m);
            Assert.Contains(result.Lines, line => line.Label == "COP: ZCB■-N612" && line.AmountCny == 1530m);
            Assert.Contains(result.Lines, line => line.Label == "Кнопки COP: A71" && line.AmountCny == 1200m);
            Assert.Contains(result.Lines, line => line.Label == "Hall Indicator: HID-A10" && line.AmountCny == 3750m);
            Assert.DoesNotContain(result.Lines, line => line.Label == "Функция ABP");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SmecFunctions_FollowKipMultipliers()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        var dataDirectory = Path.Combine(root, "Data");
        Directory.CreateDirectory(dataDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(dataDirectory, "pricing-catalog.json"),
            """
            {
              "generatedAt": "2026-06-24T00:00:00Z",
              "currency": "CNY",
              "xizi": {},
              "smec": {
                "series": ["LEHY-L-Pro"],
                "basePrices": [
                  {
                    "series": "LEHY-L-Pro",
                    "capacity": 1050,
                    "speed": 1.0,
                    "basicStops": 5,
                    "basicPrice": 100000,
                    "pricePerStop": 0,
                    "overHeightPer1000": 0,
                    "pricePerDoor2D2G": 0
                  }
                ],
                "functions": [
                  { "code": "AECH", "price": "¥610×stops" },
                  { "code": "AHC", "price": "¥110/stop" },
                  { "code": "ITV", "price": "¥50×TR(m)" },
                  { "code": "FE", "price": 3520 },
                  { "code": "Emergency exit at ceiling", "price": 730 },
                  { "code": "ELD(LEHY)", "price": 8330 },
                  { "code": "ABP", "price": 1360 },
                  { "code": "2S door opening", "price": 5940 }
                ]
              }
            }
            """);

        try
        {
            var store = new PricingCatalogStore(
                new TestWebHostEnvironment(root),
                new TestHttpClientFactory());
            var result = await store.CalculateAsync(
                new PricingCalculationRequest(
                    "SMEC",
                    "LEHY-L-Pro",
                    1050,
                    1m,
                    5,
                    900,
                    null,
                    null,
                    5,
                    0,
                    null,
                    ["AECH", "AHC", "ITV", "FE", "MELD", "ABP"],
                    false,
                    false,
                    "CNY",
                    null,
                    null,
                    new Dictionary<string, string>
                    {
                        ["TR"] = "30000",
                        ["Door mode"] = "Side opening",
                        ["Operation"] = "1C-2BC",
                        ["Ele Series"] = "LEHY Series"
                    },
                    null));

            Assert.Equal("ready", result.Status);
            Assert.Equal(124980m, result.TotalCny);
            Assert.Contains(result.Lines, line => line.Label == "Функция AECH" && line.AmountCny == 3050m);
            Assert.Contains(result.Lines, line => line.Label == "Функция AHC" && line.AmountCny == 550m);
            Assert.Contains(result.Lines, line => line.Label == "Функция ITV" && line.AmountCny == 1500m);
            Assert.Contains(result.Lines, line => line.Label == "Функция FE" && line.AmountCny == 4250m);
            Assert.Contains(result.Lines, line => line.Label == "Функция MELD" && line.AmountCny == 8330m);
            Assert.Contains(result.Lines, line => line.Label == "Режим дверей: Side opening" && line.AmountCny == 5940m);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SmecCwtSafetyGear_UsesSeriesAndCapacityPriceAutomatically()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        var dataDirectory = Path.Combine(root, "Data");
        Directory.CreateDirectory(dataDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(dataDirectory, "pricing-catalog.json"),
            """
            {
              "generatedAt": "2026-06-25T00:00:00Z",
              "currency": "CNY",
              "xizi": {},
              "smec": {
                "series": ["LEHY-L-PRO"],
                "basePrices": [
                  {
                    "series": "LEHY-L-PRO",
                    "capacity": 1050,
                    "speed": 1.0,
                    "basicStops": 5,
                    "basicPrice": 100000,
                    "pricePerStop": 0,
                    "overHeightPer1000": 0,
                    "pricePerDoor2D2G": 0
                  }
                ],
                "cwtPrices": [
                  { "series": "LEHY", "minCapacity": 1050, "maxCapacity": 1050, "price": 6100 }
                ]
              }
            }
            """);

        try
        {
            var store = new PricingCatalogStore(
                new TestWebHostEnvironment(root),
                new TestHttpClientFactory());
            var result = await store.CalculateAsync(
                new PricingCalculationRequest(
                    "SMEC",
                    "LEHY-L-PRO",
                    1050,
                    1m,
                    5,
                    900,
                    null,
                    null,
                    0,
                    0,
                    null,
                    ["CWT Safety Gear"],
                    false,
                    false,
                    "CNY",
                    null,
                    null,
                    new Dictionary<string, string>(),
                    null));

            Assert.Equal("ready", result.Status);
            Assert.Contains(
                result.Lines,
                line => line.Label == "CWT Safety Gear, 1050 кг" && line.AmountCny == 6100m);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SmecDecorations_AreIncludedAndDeepCabinUsesDVariant()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        var dataDirectory = Path.Combine(root, "Data");
        Directory.CreateDirectory(dataDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(dataDirectory, "pricing-catalog.json"),
            """
            {
              "generatedAt": "2026-06-24T00:00:00Z",
              "currency": "CNY",
              "xizi": {},
              "smec": {
                "series": ["LEHY-L-Pro"],
                "basePrices": [
                  {
                    "series": "LEHY-L-Pro",
                    "capacity": 1050,
                    "speed": 1.0,
                    "basicStops": 5,
                    "basicPrice": 100000,
                    "pricePerStop": 0,
                    "overHeightPer1000": 0,
                    "pricePerDoor2D2G": 0
                  }
                ],
                "decorations": [
                  { "category": "CarDesign", "code": "ZCD-020X", "capacity": 1050, "variant": "P13W", "price": 16830 },
                  { "category": "CarDesign", "code": "ZCD-020X", "capacity": 1050, "variant": "P14D", "price": 20200 },
                  { "category": "FrontPanel", "code": "SUS-H", "capacity": 1050, "variant": null, "price": 620 },
                  { "category": "CarDoor", "code": "SUS-H", "capacity": 1050, "variant": null, "price": 790 },
                  { "category": "Ceiling", "code": "ZCL-GS06", "capacity": 1050, "variant": null, "price": 7520 },
                  { "category": "Floor", "code": "Parquet PVC", "capacity": 1050, "variant": null, "price": 990 }
                ]
              }
            }
            """);

        try
        {
            var store = new PricingCatalogStore(
                new TestWebHostEnvironment(root),
                new TestHttpClientFactory());
            var result = await store.CalculateAsync(
                new PricingCalculationRequest(
                    "SMEC",
                    "LEHY-L-Pro",
                    1050,
                    1m,
                    5,
                    900,
                    null,
                    null,
                    0,
                    0,
                    null,
                    [],
                    false,
                    false,
                    "CNY",
                    null,
                    null,
                    new Dictionary<string, string>
                    {
                        ["AA"] = "1100",
                        ["BB"] = "2100",
                        ["Car Design"] = "ZCD-020X",
                        ["Wall"] = "SUS-H",
                        ["Car Door"] = "SUS-H",
                        ["Ceiling"] = "ZCL-GS06",
                        ["Floor Type"] = "Parquet PVC",
                        ["Mirror"] = "None"
                    },
                    null));

            Assert.Equal("ready", result.Status);
            Assert.Equal(130120m, result.TotalCny);
            Assert.Contains(result.Lines, line => line.Label == "Дизайн кабины ZCD-020X" && line.AmountCny == 20200m);
            Assert.Contains(result.Lines, line => line.Label == "Передняя панель кабины SUS-H" && line.AmountCny == 620m);
            Assert.Contains(result.Lines, line => line.Label == "Дверь кабины SUS-H" && line.AmountCny == 790m);
            Assert.Contains(result.Lines, line => line.Label == "Потолок" && line.AmountCny == 7520m);
            Assert.Contains(result.Lines, line => line.Label == "Пол" && line.AmountCny == 990m);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SmecPanoramicElenessaModel_UsesElenessaBasePrice()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        var dataDirectory = Path.Combine(root, "Data");
        Directory.CreateDirectory(dataDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(dataDirectory, "pricing-catalog.json"),
            """
            {
              "generatedAt": "2026-06-25T00:00:00Z",
              "currency": "CNY",
              "xizi": {},
              "smec": {
                "basePrices": [
                  {
                    "series": "ELENESSA",
                    "capacity": 1050,
                    "speed": 1.0,
                    "basicStops": 5,
                    "basicPrice": 100000,
                    "pricePerStop": 0,
                    "overHeightPer1000": 0,
                    "pricePerDoor2D2G": 0
                  }
                ]
              }
            }
            """);

        try
        {
            var store = new PricingCatalogStore(
                new TestWebHostEnvironment(root),
                new TestHttpClientFactory());
            var result = await store.CalculateAsync(
                new PricingCalculationRequest(
                    "SMEC",
                    "ELE-NZ11S(GQXV3)",
                    1050,
                    1m,
                    5,
                    900,
                    null,
                    null,
                    0,
                    0,
                    null,
                    [],
                    false,
                    false,
                    "CNY",
                    null,
                    null,
                    new Dictionary<string, string>(),
                    null));

            Assert.Contains(result.Lines, line => line.Code == "base" && line.AmountCny == 100000m);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void EmbeddedSmecCatalog_ContainsSupplierWorkbookPricesAndValidSpeeds()
    {
        var catalogPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/TFlexDrawingService.Api/Data/pricing-catalog.json"));
        using var document = JsonDocument.Parse(File.ReadAllText(catalogPath));
        var smec = document.RootElement.GetProperty("smec");
        var basePrices = smec.GetProperty("basePrices").EnumerateArray().ToArray();

        Assert.DoesNotContain(basePrices, item => item.GetProperty("speed").GetDecimal() == 0m);
        Assert.Contains(basePrices, item =>
            item.GetProperty("series").GetString() == "ELENESSA (GQXL3M3)"
            && item.GetProperty("capacity").GetInt32() == 1050
            && item.GetProperty("speed").GetDecimal() == 1.75m
            && item.GetProperty("basicPrice").GetDecimal() == 190790m);
        Assert.Contains(basePrices, item =>
            item.GetProperty("series").GetString() == "LEHY-Pro"
            && item.GetProperty("capacity").GetInt32() == 1050
            && item.GetProperty("speed").GetDecimal() == 1.75m
            && item.GetProperty("basicPrice").GetDecimal() == 170011.8m);

        var functions = smec.GetProperty("functions").EnumerateArray().ToArray();
        Assert.Contains(functions, item =>
            item.GetProperty("code").GetString() == "FCC-A"
            && item.GetProperty("price").GetDecimal() == 1720m);
        Assert.Contains(functions, item =>
            item.GetProperty("code").GetString() == "FERC"
            && item.GetProperty("price").GetDecimal() == 280m);
    }

    [Fact]
    public void BuildTkpDocx_CreatesWordPackageWithProposalContent()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);

        try
        {
            var templateDirectory = Path.Combine(root, "Data", "Templates");
            Directory.CreateDirectory(templateDirectory);
            File.Copy(
                Path.GetFullPath(Path.Combine(
                    AppContext.BaseDirectory,
                    "../../../../../src/TFlexDrawingService.Api/Data/Templates/TKP-SMEC.docx")),
                Path.Combine(templateDirectory, "TKP-SMEC.docx"));
            File.Copy(
                Path.GetFullPath(Path.Combine(
                    AppContext.BaseDirectory,
                    "../../../../../src/TFlexDrawingService.Api/Data/pricing-catalog.json")),
                Path.Combine(root, "Data", "pricing-catalog.json"));
            var assetDirectory = Path.Combine(root, "wwwroot", "assets", "smec");
            Directory.CreateDirectory(assetDirectory);
            File.Copy(
                Path.GetFullPath(Path.Combine(
                    AppContext.BaseDirectory,
                    "../../../../../src/TFlexDrawingService.Api/wwwroot/assets/smec/Pic_ZCD-020X.png")),
                Path.Combine(assetDirectory, "Pic_ZCD-020X.png"));
            var store = new PricingCatalogStore(
                new TestWebHostEnvironment(root),
                new TestHttpClientFactory());
            var request = new PricingCalculationRequest(
                "SMEC",
                "LEHY-L-Pro",
                1050,
                1m,
                5,
                900,
                null,
                null,
                5,
                0,
                null,
                ["FER"],
                false,
                false,
                "RUB",
                "project-1",
                null,
                new Dictionary<string, string>
                {
                    ["AH"] = "1700",
                    ["BH"] = "2500",
                    ["TR"] = "30000",
                    ["OH"] = "4300",
                    ["PD"] = "1600",
                    ["JJ"] = "900",
                    ["HH"] = "2200",
                    ["Car Design"] = "ZCD-020X",
                    ["Ceiling"] = "ZCL-GS06",
                    ["Floor Type"] = "concave-down",
                    ["Floor Pattern"] = "depth 25mm",
                    ["COP"] = "ZCB-ND10",
                    ["Main LOP"] = "ZPI-GD10",
                    ["COP Faceplate"] = "ZDT-001 SUS-H",
                    ["Main LOP Faceplate"] = "SUS-M",
                    ["Other LOP Faceplate"] = "ZDT-501 SUS-M",
                    ["Kickplate Finish"] = "ZDT-001",
                    ["Handrail Finish"] = "ZDT-501",
                    ["Button Finish"] = "ZDT-500",
                    ["Glass Door"] = "ZPKG-050",
                    ["Other Requirements"] = "Confirm factory colour"
                },
                "L1");
            var calculation = new PricingCalculationResult(
                "warning",
                "SMEC",
                "LEHY-L-Pro",
                "CNY",
                "RUB",
                12.5m,
                "manual",
                123456m,
                1543200m,
                [
                    new PricingLine("base", "Базовая цена LEHY-L-Pro", 1, 120000m, 120000m, "ready"),
                    new PricingLine("FER", "Режим пожарная опасность", 1, 3456m, 3456m, "ready")
                ],
                ["Предварительный расчет требует проверки"],
                [],
                new ContainerInfo("40HQ", "40HQ"),
                DateTimeOffset.UtcNow);
            var specification = new PricingSpecification(
                "specification-1",
                "project-1",
                null,
                "L1",
                "SMEC",
                "LEHY-L-Pro",
                "warning",
                calculation.TotalCny,
                calculation.TargetCurrency,
                calculation.TotalConverted,
                JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                JsonSerializer.Serialize(calculation, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                DateTimeOffset.Parse("2026-06-26T10:00:00Z"),
                DateTimeOffset.Parse("2026-06-26T10:00:00Z"));
            var project = new UserProject(
                "project-1",
                "admin",
                "ЖК Северный корпус 3",
                "г. Москва, ул. Мира 21",
                "P240174",
                "",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow);

            var docx = store.BuildTkpDocx(specification, project);

            using var archive = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
            Assert.NotNull(archive.GetEntry("[Content_Types].xml"));
            var documentEntry = archive.GetEntry("word/document.xml");
            Assert.NotNull(documentEntry);
            using var reader = new StreamReader(documentEntry!.Open());
            var documentXml = reader.ReadToEnd();
            _ = XDocument.Parse(documentXml);
            Assert.Contains("P240174", documentXml);
            Assert.Contains("ЖК Северный корпус 3", documentXml);
            Assert.Contains("LEHY-L-Pro", documentXml);
            Assert.Contains("Спецификация оборудования и материалов", documentXml);
            Assert.Contains("Размеры шахты (Ш x Г), мм", documentXml);
            Assert.Contains("1700 x 2500", documentXml);
            Assert.Contains("Функции и опции", documentXml);
            Assert.Contains("Отделка лицевой панели COP", documentXml);
            Assert.Contains("ZDT-001 SUS-H", documentXml);
            Assert.Contains("ZDT-501 SUS-M", documentXml);
            Assert.Contains("Отделка плинтуса", documentXml);
            Assert.Contains("Отделка поручня", documentXml);
            Assert.Contains("Отделка кнопок", documentXml);
            Assert.Contains("ZPKG-050", documentXml);
            Assert.Contains("Confirm factory colour", documentXml);
            Assert.Contains("Ниша под материал Заказчика", documentXml);
            Assert.DoesNotContain("concave-down", documentXml);
            Assert.DoesNotContain("■", documentXml);
            Assert.NotNull(archive.GetEntry("word/media/tkp-spec-1.png"));
            Assert.DoesNotContain("{kpNumber}", documentXml);
            Assert.DoesNotContain("{capacity}", documentXml);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void BuildTkpDocx_UsesXiziTemplateAndCatalogImage()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);

        try
        {
            var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/TFlexDrawingService.Api"));
            var templateDirectory = Path.Combine(root, "Data", "Templates");
            Directory.CreateDirectory(templateDirectory);
            File.Copy(Path.Combine(sourceRoot, "Data", "Templates", "TKP-XIZI.docx"), Path.Combine(templateDirectory, "TKP-XIZI.docx"));
            File.Copy(Path.Combine(sourceRoot, "Data", "pricing-catalog.json"), Path.Combine(root, "Data", "pricing-catalog.json"));
            var assetDirectory = Path.Combine(root, "wwwroot", "assets", "xizi-docx");
            Directory.CreateDirectory(assetDirectory);
            File.Copy(Path.Combine(sourceRoot, "wwwroot", "assets", "xizi-docx", "u-cr126.png"), Path.Combine(assetDirectory, "u-cr126.png"));

            var store = new PricingCatalogStore(new TestWebHostEnvironment(root), new TestHttpClientFactory());
            var request = new PricingCalculationRequest(
                "XIZI", "UN-Victor MRL", 1000, 1.75m, 10, 900, "CO", "OPTIMAX", 10,
                0, null, ["CCTV"], false, false, "RUB", "project-xizi", null,
                new Dictionary<string, string>
                {
                    ["Shaft Width"] = "2100",
                    ["Shaft Depth"] = "2400",
                    ["Travel Height"] = "27900",
                    ["Cabin Design"] = "U-CR126",
                    ["Car Width"] = "1600",
                    ["Car Depth"] = "1500",
                    ["Car Height"] = "2400",
                    ["Door Width"] = "900",
                    ["Door Height"] = "2100"
                },
                "X1");
            var calculation = new PricingCalculationResult(
                "ready", "XIZI", "UN-Victor MRL", "CNY", "RUB", 12m, "manual", 100000m, 1200000m,
                [new PricingLine("base", "Базовая цена", 1, 100000m, 100000m, "ready")],
                [], [], new ContainerInfo("40HQ", "40HQ"), DateTimeOffset.UtcNow);
            var specification = new PricingSpecification(
                "specification-xizi", "project-xizi", null, "X1", "XIZI", "UN-Victor MRL", "ready",
                calculation.TotalCny, calculation.TargetCurrency, calculation.TotalConverted,
                JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                JsonSerializer.Serialize(calculation, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            var project = new UserProject(
                "project-xizi", "admin", "Бизнес-центр Восток", "г. Москва", "P-XIZI", "",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

            var docx = store.BuildTkpDocx(specification, project);

            using var archive = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
            using var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open());
            var documentXml = reader.ReadToEnd();
            _ = XDocument.Parse(documentXml);
            Assert.Contains("UN-Victor MRL", documentXml);
            Assert.Contains("2100 x 2400", documentXml);
            Assert.Contains("U-CR126", documentXml);
            Assert.NotNull(archive.GetEntry("word/media/tkp-spec-1.png"));
            Assert.DoesNotContain("{kpNumber}", documentXml);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void BuildPricingRequestXlsx_PreservesTemplateAndReplacesPlaceholders()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        var templateDirectory = Path.Combine(root, "Data", "Templates");
        Directory.CreateDirectory(templateDirectory);

        try
        {
            File.Copy(
                Path.GetFullPath(Path.Combine(
                    AppContext.BaseDirectory,
                    "../../../../../src/TFlexDrawingService.Api/Data/Templates/shablon_zaprosa.xlsx")),
                Path.Combine(templateDirectory, "shablon_zaprosa.xlsx"));
            var request = new PricingCalculationRequest(
                "XIZI", "UN-Victor MRL", 1000, 1.75m, 10, 900, "CO", "OPTIMAX", 10,
                0, null, ["CONTAINER_40HQ", "EFS2"], false, false, "RUB", "project-1", null,
                new Dictionary<string, string>
                {
                    ["Model"] = "UN-Victior MRL",
                    ["Travel Height"] = "27900",
                    ["Shaft Width"] = "1800",
                    ["Shaft Depth"] = "2700",
                    ["Overhead"] = "5300",
                    ["Pit"] = "1900",
                    ["Car Width"] = "1100",
                    ["Car Depth"] = "2100",
                    ["Car Height"] = "2400"
                },
                "L1");
            var specification = new PricingSpecification(
                "specification-1", "project-1", null, "L1", "XIZI", "UN-Victor MRL", "ready",
                100000m, "RUB", 1250000m,
                JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                "{}", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            var project = new UserProject(
                "project-1", "admin", "ЖК Северный", "Москва", "P240174", "",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            var store = new PricingCatalogStore(new TestWebHostEnvironment(root), new TestHttpClientFactory());

            var xlsx = store.BuildPricingRequestXlsx(specification, project);

            using var archive = new ZipArchive(new MemoryStream(xlsx), ZipArchiveMode.Read);
            Assert.NotNull(archive.GetEntry("xl/styles.xml"));
            var sharedStrings = archive.GetEntry("xl/sharedStrings.xml");
            Assert.NotNull(sharedStrings);
            using var reader = new StreamReader(sharedStrings!.Open());
            var xml = reader.ReadToEnd();
            _ = XDocument.Parse(xml);
            Assert.Contains("ZhK Severnyy", xml);
            Assert.Contains("27.9", xml);
            Assert.DoesNotContain("{{projectName}}", xml);
            Assert.DoesNotContain("{{demand1_1}}", xml);

            var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
            Assert.NotNull(worksheetEntry);
            using var worksheetReader = new StreamReader(worksheetEntry!.Open());
            var worksheet = XDocument.Parse(worksheetReader.ReadToEnd());
            var unusedDemandCell = worksheet.Descendants().Single(element =>
                element.Name.LocalName == "c" && element.Attribute("r")?.Value == "D55");
            Assert.Null(unusedDemandCell.Elements().FirstOrDefault(element => element.Name.LocalName == "v"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void BuildPricingRequestXlsx_ForSmec_UsesFactoryConfigurationWorkbookAndClearsReferenceData()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        var templateDirectory = Path.Combine(root, "Data", "Templates");
        Directory.CreateDirectory(templateDirectory);

        try
        {
            File.Copy(
                Path.GetFullPath(Path.Combine(
                    AppContext.BaseDirectory,
                    "../../../../../src/TFlexDrawingService.Api/Data/Templates/smec_request.xlsx")),
                Path.Combine(templateDirectory, "smec_request.xlsx"));
            var request = new PricingCalculationRequest(
                "SMEC", "LEHY-III", 1600, 2.5m, 8, 1100, null, null, 8,
                0, null, ["CWT WITH SAFETY", "FER"], false, false, "CNY", "project-smec", null,
                new Dictionary<string, string>
                {
                    ["Ele Series"] = "Passenger Elevator",
                    ["Quantity"] = "2",
                    ["Floors"] = "25",
                    ["Main Floor"] = "1",
                    ["Other Floors"] = "2~25",
                    ["Power Supply"] = "380V, 50 Hz",
                    ["Lighting Supply"] = "220V, 50 Hz",
                    ["Operation"] = "Simplex",
                    ["AH"] = "2265",
                    ["BH"] = "1900",
                    ["TR"] = "27900",
                    ["OH"] = "4300",
                    ["PD"] = "1500",
                    ["AA"] = "1600",
                    ["BB"] = "1500",
                    ["HL"] = "2700",
                    ["JJ"] = "1100",
                    ["HH"] = "2400",
                    ["Door type"] = "1D1G",
                    ["Door mode"] = "Central opening",
                    ["COP Faceplate"] = "ZDT-501 SUS-M",
                    ["Other LOP Faceplate"] = "SUS-H",
                    ["Other Requirements"] = "Factory confirmation required"
                },
                "L1");
            var specification = new PricingSpecification(
                "specification-smec", "project-smec", null, "L1", "SMEC", "LEHY-III", "ready",
                195900m, "CNY", 195900m,
                JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                "{}", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            var project = new UserProject(
                "project-smec", "admin", "Мост Багратион", "Москва", "C100300001", "",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            var store = new PricingCatalogStore(new TestWebHostEnvironment(root), new TestHttpClientFactory());

            var xlsx = store.BuildPricingRequestXlsx(specification, project);

            using var archive = new ZipArchive(new MemoryStream(xlsx), ZipArchiveMode.Read);
            Assert.NotNull(archive.GetEntry("xl/styles.xml"));
            Assert.NotNull(archive.GetEntry("xl/drawings/drawing1.xml"));
            Assert.NotNull(archive.GetEntry("xl/media/image1.png"));
            using var workbookReader = new StreamReader(archive.GetEntry("xl/workbook.xml")!.Open());
            var workbookXml = workbookReader.ReadToEnd();
            Assert.Contains("电梯配置表", workbookXml);
            Assert.Contains("ZPML-G660(8ms)", workbookXml);

            using var worksheetReader = new StreamReader(
                archive.GetEntry("xl/worksheets/sheet5.xml")!.Open());
            var worksheet = XDocument.Parse(worksheetReader.ReadToEnd());
            Assert.Equal("项目储备：C100300001", ReadCell(worksheet, "B1"));
            Assert.Equal("L1（2台）", ReadCell(worksheet, "E2"));
            Assert.Equal("1600", ReadCell(worksheet, "E3"));
            Assert.Equal("2.5", ReadCell(worksheet, "E4"));
            Assert.Equal("27.9", ReadCell(worksheet, "E5"));
            Assert.Equal("25层/8站", ReadCell(worksheet, "E6"));
            Assert.Equal("AA 1600 x BB 1500 x HC 2700", ReadCell(worksheet, "E7"));
            Assert.Equal("JJ 1100 x HH 2400", ReadCell(worksheet, "E8"));
            Assert.Equal("1D1G / Central opening", ReadCell(worksheet, "E9"));
            Assert.Equal("1500", ReadCell(worksheet, "E34"));
            Assert.Contains("Shaft: AH 2265 x BH 1900", ReadCell(worksheet, "E59"));
            Assert.Contains("Options: CWT WITH SAFETY, FER", ReadCell(worksheet, "E59"));
            Assert.Equal("", ReadCell(worksheet, "G2"));
            Assert.Equal("", ReadCell(worksheet, "M59"));
            Assert.Contains("COP Faceplate: ZDT-501 SUS-M", ReadCell(worksheet, "E59"));
            Assert.Contains("Selected requirements", workbookXml);
            var detailSheet = XDocument.Parse(workbookXml).Descendants().Single(element => (string?)element.Attribute("name") == "Selected requirements");
            var detailId = detailSheet.Attributes().Single(attribute => attribute.Name.LocalName == "id").Value;
            using var relReader = new StreamReader(archive.GetEntry("xl/_rels/workbook.xml.rels")!.Open());
            var detailTarget = XDocument.Parse(relReader.ReadToEnd()).Descendants()
                .Single(element => (string?)element.Attribute("Id") == detailId).Attribute("Target")!.Value;
            using var detailReader = new StreamReader(archive.GetEntry($"xl/{detailTarget}")!.Open());
            var detail = XDocument.Parse(detailReader.ReadToEnd());
            var detailText = string.Join(" ", detail.Descendants().Where(element => element.Name.LocalName == "t").Select(element => element.Value));
            Assert.Contains("ZDT-501 SUS-M", detailText);
            Assert.Contains("Factory confirmation required", detailText);
            Assert.Contains("CWT WITH SAFETY", detailText);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task XiziSupplierCatalog_ResolvesDoorsAndMandatoryRequirements()
    {
        var result = await CreateSupplierCatalogStore().CalculateAsync(CreateXiziSupplierRequest() with
        {
            DoorWidthMm = 900,
            DoorType = "CO",
            DoorManufacturer = "FERMATOR"
        });

        Assert.Empty(result.Blockers);
        Assert.Single(result.Warnings); // Only the preliminary XIZI calculation notice.
        Assert.All(result.Lines, line => Assert.Equal("ready", line.Status));
        // Supplied HTML project prices. Dimensions: R=13.4, K=4.6, S=1.5 m.
        Assert.Contains(result.Lines, line => line.Code == "base" && line.AmountCny == 60862.47184m);
        Assert.Contains(result.Lines, line => line.Label == "Дверь кабины" && line.AmountCny == 2580m);
        Assert.Contains(result.Lines, line => line.Label == "Дверь шахты, основной этаж" && line.AmountCny == 1045m);
        Assert.Contains(result.Lines, line => line.Label == "Двери шахты, остальные этажи" && line.AmountCny == 4180m);
        Assert.Contains(result.Lines, line => line.Code == "lmr-pit-inspection" && line.AmountCny == 1866m);
        Assert.Contains(result.Lines, line => line.Code == "lmr-hoistway-lighting"
            && decimal.Round(line.AmountCny!.Value, 2) == 31.34m);
    }

    [Theory]
    [InlineData("E30")]
    [InlineData("EI60")]
    public async Task XiziSupplierCatalog_PrefersReferenceDoorOverGenericExcelPrice(string fireRating)
    {
        var request = CreateXiziSupplierRequest();
        var fields = new Dictionary<string, string>(request.SpecificationFields!)
        {
            ["Car Door Material"] = "AISI443", ["Fire Rating"] = fireRating
        };
        var result = await CreateSupplierCatalogStore().CalculateAsync(request with
        {
            DoorWidthMm = 900, DoorType = "CO", DoorManufacturer = "FERMATOR", SpecificationFields = fields
        });

        Assert.Equal(2708m, Assert.Single(result.Lines, line => line.Label == "Дверь кабины").AmountCny);
        Assert.Empty(result.Blockers);
    }

    [Fact]
    public async Task XiziSupplierCatalog_PreservesUnavailableExcelOnlyButton()
    {
        var request = CreateXiziSupplierRequest();
        var fields = new Dictionary<string, string>(request.SpecificationFields!) { ["COP Button"] = "iBR35B" };
        var result = await CreateSupplierCatalogStore().CalculateAsync(request with { SpecificationFields = fields });

        Assert.Equal("blocked", Assert.Single(result.Lines, line => line.Code == "BUTTON").Status);
    }

    [Theory]
    [InlineData(1600, 1.75, false)]
    [InlineData(2000, 1.0, false)]
    [InlineData(2000, 1.75, true)]
    public async Task XiziSupplierCatalog_ChargesHydraulicBufferOnlyWhenApplicable(int capacity, double speed, bool expected)
    {
        var result = await CreateSupplierCatalogStore().CalculateAsync(CreateXiziSupplierRequest() with
        {
            Series = "UN-Victor R", CapacityKg = capacity, Speed = (decimal)speed
        });

        var buffer = result.Lines.Where(line => line.Label.Contains("RUS_HYDRAULIC_BUFFER_CAPACITY")).ToArray();
        if (expected)
        {
            Assert.Equal(350m, Assert.Single(buffer).AmountCny);
        }
        else
        {
            Assert.Empty(buffer);
        }
    }

    [Theory]
    [InlineData("CCTV", 476)]
    [InlineData("TC", 714)]
    [InlineData("CWTSAFETY", 4578)]
    [InlineData("COP2", 1283)]
    [InlineData("HAD", 1105)]
    [InlineData("EARTHQUAKE_EMERGENCY_RETURN", 5060)]
    [InlineData("EFS2", 5650)]
    public async Task XiziSupplierCatalog_UsesSupplierPricesInOptions(string option, int expected)
    {
        var result = await CreateSupplierCatalogStore().CalculateAsync(CreateXiziSupplierRequest() with { Options = [option] });

        var line = Assert.Single(result.Lines, line => line.Label == $"Опция {option}");
        Assert.Equal("ready", line.Status);
        Assert.Equal((decimal)expected, line.AmountCny);
        Assert.Single(result.Warnings);
    }

    [Theory]
    [InlineData(1350, "Охлаждение", 3984)]
    [InlineData(1600, "Охлаждение", 4913)]
    [InlineData(1350, "Охлаждение и нагрев", 4313)]
    [InlineData(1600, "Охлаждение и нагрев", 5255)]
    public async Task XiziSupplierCatalog_ResolvesAirConditionerForLoad(int capacity, string selection, int expected)
    {
        var request = CreateXiziSupplierRequest();
        var fields = new Dictionary<string, string>(request.SpecificationFields!) { ["AC"] = selection };
        var result = await CreateSupplierCatalogStore().CalculateAsync(request with { CapacityKg = capacity, SpecificationFields = fields });

        Assert.Equal((decimal)expected, Assert.Single(result.Lines, line => line.Code == "air-conditioner").AmountCny);
        Assert.Single(result.Warnings);
    }

    [Theory]
    [InlineData("aisi-443", 352)]
    [InlineData("aisi-304", 1488)]
    [InlineData("painted-steel", 52)]
    [InlineData("1,5mm AISI304", 2816.25)]
    public async Task XiziSupplierCatalog_UsesWallPriceAndHeightSurcharge(string material, double expected)
    {
        var request = CreateXiziSupplierRequest();
        var fields = new Dictionary<string, string>(request.SpecificationFields!)
        {
            ["Cabin Design"] = "U-CR126-BASE", ["Car Wall Material"] = material, ["Car Height"] = "2500"
        };
        var result = await CreateSupplierCatalogStore().CalculateAsync(request with { SpecificationFields = fields });

        Assert.Equal((decimal)expected, Assert.Single(result.Lines, line => line.Code == "CARWALLS").AmountCny);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public async Task XiziSupplierCatalog_UsesReferenceSupplementForMissingModelAndOption()
    {
        var store = CreateSupplierCatalogStore();
        var result = await store.CalculateAsync(CreateXiziSupplierRequest() with
        {
            Series = "UN-Victor MRL(T)", Options = ["CWTSAFETY"]
        });

        Assert.Empty(result.Blockers);
        Assert.Single(result.Warnings);
        Assert.Equal(65704.325m, Assert.Single(result.Lines, line => line.Code == "base").AmountCny);
        Assert.Equal(450m, Assert.Single(result.Lines, line => line.Code == "extra-rise").AmountCny);
        Assert.Equal(4578m, Assert.Single(result.Lines, line => line.Label == "Опция CWTSAFETY").AmountCny);
        Assert.Equal(1612m, store.Catalog.Xizi.Decorations.Single(entry => entry.Category == "Mirror" && entry.Code == "FULL").Price!.Value.GetDecimal());
    }

    [Theory]
    [InlineData("EACH_ADDITIONAL_LANDING", 120)]
    [InlineData("24_MONTHS", 1000)]
    [InlineData("36_MONTHS", 5000)]
    [InlineData("48_MONTHS", 9000)]
    [InlineData("60_MONTHS", 14000)]
    [InlineData("CONTROL_CABINET", 1400)]
    [InlineData("Car door sound proof insulation", 450)]
    public async Task XiziSupplierCatalog_ResolvesSupplementedOptions(string option, int expected)
    {
        var result = await CreateSupplierCatalogStore().CalculateAsync(CreateXiziSupplierRequest() with { Options = [option] });

        Assert.Equal((decimal)expected, Assert.Single(result.Lines, line => line.Label == $"Опция {option}").AmountCny);
        Assert.Empty(result.Blockers);
        Assert.Single(result.Warnings);
    }

    [Theory]
    [InlineData("U-CR126", 0)]
    [InlineData("U-CR126（painted st st）", 500)]
    [InlineData("U-CR126\nTi - GOLD  HSS", 5292)]
    [InlineData("U-CR126(1.5mm HSS)", 439)]
    [InlineData("U-CR126(304 HSS)", 1136)]
    [InlineData("U-CR126(1.5mm304HSS)", 2035)]
    public async Task XiziSupplierCatalog_ResolvesSupplementedDesigns(string design, int expected)
    {
        var request = CreateXiziSupplierRequest();
        var fields = new Dictionary<string, string>(request.SpecificationFields!) { ["Cabin Design"] = design, ["Car Height"] = "2400" };
        var result = await CreateSupplierCatalogStore().CalculateAsync(request with { SpecificationFields = fields });

        Assert.Equal((decimal)expected, Assert.Single(result.Lines, line => line.Code == "CARDESIGN").AmountCny);
        Assert.Empty(result.Blockers);
        Assert.Single(result.Warnings);
    }

    [Theory]
    [InlineData("NICHE_10", 800)]
    [InlineData("MARBLE", 2000)]
    [InlineData("PVC", 600)]
    [InlineData("CARPET", 500)]
    public async Task XiziSupplierCatalog_ResolvesSupplementedFloors(string floor, int expected)
    {
        var request = CreateXiziSupplierRequest();
        var fields = new Dictionary<string, string>(request.SpecificationFields!)
        {
            ["Cabin Design"] = "U-CR126-BASE", ["Car Wall Material"] = "aisi-443", ["Floor"] = floor
        };
        var result = await CreateSupplierCatalogStore().CalculateAsync(request with { SpecificationFields = fields });

        Assert.Equal((decimal)expected, Assert.Single(result.Lines, line => line.Code == "FLOOR").AmountCny);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public async Task XiziSupplierCatalog_ResolvesReferencePanelsAndPreviouslyUnavailableButton()
    {
        var request = CreateXiziSupplierRequest();
        var fields = new Dictionary<string, string>(request.SpecificationFields!)
        {
            ["Main LOP"] = "U-ZW1600-F", ["Other LOP"] = "U-ZW1600(XHB12-Bi)",
            ["Main LIP"] = "U-HW100(7_TFT)", ["Other LIP"] = "U-HW200(7_LED)", ["COP Button"] = "iBR35C"
        };
        var result = await CreateSupplierCatalogStore().CalculateAsync(request with { SpecificationFields = fields, Options = ["Ladder"] });

        Assert.Equal(0m, Assert.Single(result.Lines, line => line.Label == "LOP, основной этаж: U-ZW1600-F").AmountCny);
        Assert.Equal(400m, Assert.Single(result.Lines, line => line.Label == "LOP, остальные этажи: U-ZW1600(XHB12-Bi)").AmountCny);
        Assert.Equal(1000m, Assert.Single(result.Lines, line => line.Label == "LIP, основной этаж: U-HW100(7_TFT)").AmountCny);
        Assert.Equal(2640m, Assert.Single(result.Lines, line => line.Label == "LIP, остальные этажи: U-HW200(7_LED)").AmountCny);
        Assert.Equal(200m, Assert.Single(result.Lines, line => line.Label == "LMR: Ladder").AmountCny);
        Assert.Empty(result.Blockers);
        Assert.Equal("ready", Assert.Single(result.Lines, line => line.Code == "BUTTON").Status);
        Assert.Equal(700m, Assert.Single(result.Lines, line => line.Code == "BUTTON").AmountCny);
    }

    [Fact]
    public async Task XiziSupplierCatalog_ZeroLightingAndIncludedVoiceAreNotMissingOrDuplicated()
    {
        var result = await CreateSupplierCatalogStore().CalculateAsync(CreateXiziSupplierRequest() with
        {
            SpecificationFields = new Dictionary<string, string>(), Options = ["VOICE_ANNOUNCEMENT_IN"]
        });

        Assert.Equal(0m, Assert.Single(result.Lines, line => line.Code == "lmr-hoistway-lighting").AmountCny);
        Assert.Equal(240m, Assert.Single(result.Lines, line => line.Label.Contains("VOICE_ANNOUNCEMENT_IN")).AmountCny);
        Assert.Single(result.Warnings);
    }

    [Theory]
    [InlineData("UN-Victor MRL")]
    [InlineData("UN-Victor MRL(T)")]
    [InlineData("G3")]
    public async Task XiziReview_BlocksSideCounterweightForMachineRoomlessModels(string series)
    {
        var result = await CreateSupplierCatalogStore().CalculateAsync(CreateXiziSupplierRequest() with { Series = series, Options = ["CWT_SIDE"] });
        Assert.Contains(result.Blockers, value => value.Contains("CWT at side"));
    }

    [Fact]
    public async Task XiziReview_AutomaticallyChoosesOneArdAndOneContainer()
    {
        var result = await CreateSupplierCatalogStore().CalculateAsync(CreateXiziSupplierRequest() with { Options = ["ARD_37", "ARD_22", "CONTAINER_20GP", "CONTAINER_40HQ"] });
        Assert.Single(result.Lines, line => line.Code.StartsWith("option-ARD"));
        Assert.Contains(result.Lines, line => line.Code == "option-ARD15" && line.AmountCny == 2028m);
        Assert.DoesNotContain(result.Lines, line => line.Code.Contains("20GP"));
        Assert.Single(result.Lines, line => line.Code.Contains("40HQ"));
    }

    [Theory]
    [InlineData(1100, 2100, 1200, 5300, "Ширина шахты")]
    [InlineData(2100, 1600, 2500, 5300, "отсутствует")]
    [InlineData(1100, 2100, 1800, 35400, "6000")]
    public async Task XiziReview_RejectsInvalidGeometryUsingActualTemplate(int width, int depth, int shaftWidth, int overhead, string message)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/TFlexDrawingService.Api"));
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "../../templates/templates.json")));
        var templates = JsonSerializer.Deserialize<TFlexDrawingService.Core.Models.DrawingTemplate[]>(document.RootElement.GetProperty("templates"), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var store = new PricingCatalogStore(new TestWebHostEnvironment(root), new TestHttpClientFactory(), new Support.InMemoryTemplateCatalog(templates));
        var request = CreateXiziSupplierRequest();
        var fields = new Dictionary<string, string>(request.SpecificationFields!)
        {
            ["Car Width"] = width.ToString(), ["Car Depth"] = depth.ToString(), ["Shaft Width"] = shaftWidth.ToString(),
            ["Shaft Depth"] = "2700", ["Overhead"] = overhead.ToString(), ["Car Height"] = "2400", ["Door Height"] = "2100"
        };
        var result = await store.CalculateAsync(request with { DoorWidthMm = 900, DoorType = "CO", SpecificationFields = fields });
        Assert.Contains(result.Blockers, value => value.Contains(message));
    }

    [Fact]
    public async Task XiziReview_CustomCabinRequiresExplicitSelectionAndKeepsPhysicalChecks()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/TFlexDrawingService.Api"));
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "../../templates/templates.json")));
        var templates = JsonSerializer.Deserialize<TFlexDrawingService.Core.Models.DrawingTemplate[]>(document.RootElement.GetProperty("templates"), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var store = new PricingCatalogStore(new TestWebHostEnvironment(root), new TestHttpClientFactory(), new Support.InMemoryTemplateCatalog(templates));
        var fields = new Dictionary<string, string>
        {
            ["Car Width"] = "1600", ["Car Depth"] = "2100", ["Car Height"] = "2400", ["Door Height"] = "2100",
            ["Shaft Width"] = "2400", ["Shaft Depth"] = "2800", ["Travel Height"] = "13400", ["Overhead"] = "4600", ["Pit"] = "1500"
        };
        var request = CreateXiziSupplierRequest() with { CapacityKg = 1600, DoorWidthMm = 900, SpecificationFields = fields };
        var standard = await store.CalculateAsync(request);
        Assert.Contains(standard.Blockers, message => message.Contains("отсутствует"));
        fields["Custom Configuration"] = "Yes";
        var custom = await store.CalculateAsync(request);
        Assert.Empty(custom.Blockers);
        Assert.Contains(custom.Warnings, message => message.Contains("Нестандартная конфигурация"));
        fields["Shaft Width"] = "1200";
        var narrow = await store.CalculateAsync(request);
        Assert.Contains(narrow.Blockers, message => message.Contains("Ширина шахты"));
        fields["Car Depth"] = "-2100";
        var negative = await store.CalculateAsync(request);
        Assert.Contains(negative.Blockers, message => message.Contains("Car Depth"));
    }

    [Fact]
    public async Task XiziReview_ProjectExportContainsTwoWorkbooksAndAllLifts()
    {
        var store = CreateSupplierCatalogStore();
        var request = CreateXiziSupplierRequest() with { Stops = 10, Speed = 2.5m, Options = ["EFS2", "ILED_7"], SpecificationFields = new Dictionary<string, string>
        {
            ["Travel Height"] = "27900", ["Car Width"] = "1100", ["Car Depth"] = "2100", ["Car Height"] = "2400",
            ["Handrail Position"] = "Нет", ["Handrail"] = "U-HR001", ["COP"] = "U-CY100", ["Main LIP"] = "Нет", ["Main LOP"] = "U-ZW1600", ["Quantity"] = "3",
            ["Mirror Wall"] = "Left wall", ["Mirror Height"] = "None"
        } };
        var calculation = await store.CalculateAsync(request);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var specification = new PricingSpecification("s1", "p1", null, "L1", "XIZI", request.Series, "warning", calculation.TotalCny, "CNY", calculation.TotalCny,
            JsonSerializer.Serialize(request, json), JsonSerializer.Serialize(calculation, json), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var project = new UserProject("p1", "admin", "Office", "Москва", "REF1", "", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var bytes = store.BuildXiziProjectExport([specification, specification with { Id = "s2", Name = "L2" }], project);
        using var bundle = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.Equal(2, bundle.Entries.Count);
        using var requestStream = new MemoryStream();
        bundle.GetEntry("XIZI-request.xlsx")!.Open().CopyTo(requestStream);
        using var workbook = new ZipArchive(requestStream, ZipArchiveMode.Read);
        using var sheetReader = new StreamReader(workbook.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var sheet = XDocument.Parse(sheetReader.ReadToEnd());
        Assert.Null(workbook.GetEntry("xl/worksheets/sheet2.xml"));
        Assert.Equal("L1", ReadCell(sheet, "D5"));
        Assert.Equal("L2", ReadCell(sheet, "F5"));
        Assert.Equal("27.9", ReadCell(sheet, "F13"));
        Assert.Equal("Russia, Moscow", ReadCell(sheet, "C4"));
        Assert.Equal("27.9", ReadCell(sheet, "D13"));
        Assert.Equal("By XIZI", ReadCell(sheet, "D17"));
        Assert.Equal("1100 x 2100", ReadCell(sheet, "D19"));
        Assert.Equal("0", ReadCell(sheet, "D29"));
        Assert.Equal("IRC", ReadCell(sheet, "D31"));
        Assert.Equal("None", ReadCell(sheet, "D39"));
        Assert.Equal("TFT 7", ReadCell(sheet, "D43"));
        Assert.Equal("U-ZW1600", ReadCell(sheet, "D46"));
        Assert.Contains("GOST 33984.1-2016", ReadCell(sheet, "D83"));
        Assert.Equal("提升高度（米）", ReadCell(sheet, "B13"));
        Assert.Contains("Fire man function", ReadCell(sheet, "D52"));
        Assert.Contains("Mirror: Left wall", sheet.ToString());
        Assert.DoesNotContain("Left wall, None", sheet.ToString());
        using var priceStream = new MemoryStream();
        bundle.GetEntry("XIZI-prices.xlsx")!.Open().CopyTo(priceStream);
        using var prices = new ZipArchive(priceStream, ZipArchiveMode.Read);
        using var priceReader = new StreamReader(prices.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var priceSheet = XDocument.Parse(priceReader.ReadToEnd());
        Assert.Equal("3", ReadCell(priceSheet, "F2"));
        Assert.Equal("0.5", ReadCell(priceSheet, "G2"));
        Assert.Equal("1.5", ReadCell(priceSheet, "H2"));
        Assert.Equal("6", ReadCell(priceSheet, "F4"));
        Assert.Equal("3.0", ReadCell(priceSheet, "H4"));
        Assert.Equal((calculation.TotalCny * 6).ToString(System.Globalization.CultureInfo.InvariantCulture), ReadCell(priceSheet, "J4"));
    }

    [Fact]
    public async Task SmecStructuredRequirements_PriceEverySelectionAndSplitLopQuantities()
    {
        var request = new PricingCalculationRequest(
            "SMEC", "LEHY-L-Pro", 1050, 1m, 5, 900, null, null, 5, 0, null,
            ["UV", "Reduced OH/PD", "CWT Safety Gear", "Roller guide shoe"], false, false, "CNY", null, null,
            new Dictionary<string, string>
            {
                ["Ele Series"] = "LEHY Series", ["Door type"] = "1D1G", ["HH"] = "2100",
                ["COP"] = "ZCB-ND10", ["COP 2"] = "None", ["Handrail"] = "ZYH-RH06", ["Handrail Position"] = "rear wall",
                ["Fire Rating"] = "EI60", ["Glass Door"] = "ZPKG-050",
                ["Kickplate Finish"] = "ZDT-001", ["Handrail Finish"] = "ZDT-501", ["Button Finish"] = "ZDT-501",
                ["COP Faceplate"] = "ZDT-001 SUS-H", ["Main LOP Faceplate"] = "SUS-M", ["Other LOP Faceplate"] = "ZDT-501 SUS-M"
            }, "SMEC test");
        var store = CreateSupplierCatalogStore();
        var result = await store.CalculateAsync(request);
        PricingLine Line(string label) => Assert.Single(result.Lines, line => line.Label.StartsWith(label, StringComparison.Ordinal));
        Assert.Equal(1760m, Line("UV disinfection").AmountCny);
        Assert.Equal(16000m, Line("Reduced overhead").AmountCny);
        Assert.Equal(1000m, Line("Kickplate finish").AmountCny);
        Assert.Equal(700m, Line("Handrail finish").AmountCny);
        Assert.Equal(1500m, Line("Button finish").AmountCny);
        Assert.Equal(490m, Line("COP Faceplate").AmountCny);
        Assert.Equal(60m, Line("Main LOP Faceplate").AmountCny);
        Assert.Equal(1920m, Line("Other LOP Faceplate").AmountCny);
        Assert.Equal(5, Line("Fire-rated landing doors").Quantity);
        Assert.True(Line("Fire-rated landing doors").AmountCny > 0);
        Assert.Equal(6, Line("Glass doors").Quantity);
        Assert.True(Line("Glass doors").AmountCny > 0);
        Assert.Equal(9280m, Line("CWT Safety Gear").AmountCny);
        Assert.Equal(6000m, Line("Функция Roller guide shoe").AmountCny);
        Assert.Contains(result.Warnings, warning => warning.Contains("EI60") && warning.Contains("EI120"));

        var legacy = request with
        {
            Options = ["CWT Safety Gear", "Roller guide shoe"],
            SpecificationFields = new Dictionary<string, string>
            {
                ["Ele Series"] = "LEHY Series", ["Door type"] = "1D1G", ["HH"] = "2100",
                ["COP"] = "ZCB-ND10", ["COP 2"] = "None", ["Handrail"] = "ZYH-RH06", ["Handrail Position"] = "rear wall",
                ["Other Requirements"] = "EI60;ZPKG-050;CWT;Roller;UV;Kickplate ZDT-001;Handrail ZDT-501;Button ZDT-501;OH/PD;COP Faceplate ZDT-001 SUS-H;LOP Faceplate ZDT-501 SUS-M"
            }
        };
        var legacyResult = await store.CalculateAsync(legacy);
        // The old common LOP finish applies to the main floor too (480 instead of 60).
        Assert.Equal(result.TotalCny + 420, legacyResult.TotalCny);
        Assert.Single(legacyResult.Lines, line => line.Code == "function-cwt-safety-gear");
        Assert.Single(legacyResult.Lines, line => line.Label.StartsWith("Функция Roller guide shoe", StringComparison.Ordinal));
    }

    [Fact]
    public void SmecLegacyRequirements_PreserveUnknownNotesAndExplicitOverrides()
    {
        var request = CreateXiziSupplierRequest() with
        {
            Supplier = "SMEC", Options = ["UV"],
            SpecificationFields = new Dictionary<string, string>
            {
                ["Fire Rating"] = "", ["COP Faceplate"] = "SUS-M",
                ["Other Requirements"] = "1 EI60\n2 COP Faceplate ZDT-007 SUS-H\n3 LOP Faceplate ZDT-007 SUS-M\n4 UV\n5 EN81\nConfirm factory colour"
            }
        };
        var migrated = SmecRequirements.Normalize(request);
        Assert.Equal("", migrated.SpecificationFields!["Fire Rating"]);
        Assert.Equal("SUS-M", migrated.SpecificationFields["COP Faceplate"]);
        Assert.Equal("ZDT-001 SUS-M", migrated.SpecificationFields["Main LOP Faceplate"]);
        Assert.Equal("ZDT-001 SUS-M", migrated.SpecificationFields["Other LOP Faceplate"]);
        Assert.Equal("Confirm factory colour", migrated.SpecificationFields["Other Requirements"]);
        Assert.Equal(new[] { "UV", "EN81" }, migrated.Options);
        var repeated = SmecRequirements.Normalize(migrated);
        Assert.Equal(migrated.SpecificationFields, repeated.SpecificationFields);
        Assert.Equal(migrated.Options, repeated.Options);
        var xizi = request with { Supplier = "XIZI" };
        Assert.Same(xizi, SmecRequirements.Normalize(xizi));
    }

    private static PricingCatalogStore CreateSupplierCatalogStore()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/TFlexDrawingService.Api"));
        return new PricingCatalogStore(new TestWebHostEnvironment(root), new TestHttpClientFactory());
    }

    private static PricingCalculationRequest CreateXiziSupplierRequest() => new(
        "XIZI", "UN-Victor MRL", 1000, 1m, 5, 0, null, null, 5, 0, null, [], false, false, "CNY", null, null,
        new Dictionary<string, string> { ["Travel Height"] = "13400", ["Overhead"] = "4600", ["Pit"] = "1500" }, null);

    private static string ReadCell(XDocument worksheet, string reference)
    {
        var cell = worksheet.Descendants().Single(element =>
            element.Name.LocalName == "c" && element.Attribute("r")?.Value == reference);
        if (cell.Attribute("t")?.Value == "inlineStr")
        {
            return string.Concat(cell.Descendants().Where(element => element.Name.LocalName == "t")
                .Select(element => element.Value));
        }

        return cell.Elements().FirstOrDefault(element => element.Name.LocalName == "v")?.Value ?? "";
    }

    private sealed class TestWebHostEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "TFlexDrawingService.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(root);
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient();
        }
    }
}
