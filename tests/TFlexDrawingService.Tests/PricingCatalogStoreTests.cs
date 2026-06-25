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
                  { "manufacturer": "FERMATOR", "part": "Car door", "doorType": "CO", "fireRating": "None", "finish": "AISI443", "capacity": 1000, "floor": "-", "width": 900, "price": 1000 },
                  { "manufacturer": "FERMATOR", "part": "2nd door", "doorType": "CO", "fireRating": "None", "finish": "AISI443", "capacity": 1000, "floor": "-", "width": 900, "price": 2000 },
                  { "manufacturer": "FERMATOR", "part": "Shaft door", "doorType": "CO", "fireRating": "E30", "finish": "Painted steel", "capacity": 1000, "floor": "First", "width": 900, "price": 300 },
                  { "manufacturer": "FERMATOR", "part": "Shaft door", "doorType": "CO", "fireRating": "E30", "finish": "AISI443", "capacity": 1000, "floor": "Other", "width": 900, "price": 200 }
                ],
                "decorations": [
                  { "category": "Car walls", "code": "AISI443", "height": 2400, "price": 500, "overprice": 200 },
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
                        ["Cabin Design"] = "U-CR126",
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
