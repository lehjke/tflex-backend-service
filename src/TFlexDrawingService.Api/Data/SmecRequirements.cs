using System.Text.RegularExpressions;

namespace TFlexDrawingService.Api.Data;

/// <summary>Migrates the former free-text SMEC requirements without losing unrecognized notes.</summary>
public static class SmecRequirements
{
    public static readonly string[] Fields =
    [
        "Fire Rating", "Glass Door", "Kickplate Finish", "Handrail Finish", "Button Finish",
        "COP Faceplate", "Main LOP Faceplate", "Other LOP Faceplate"
    ];

    public static readonly string[] AdditionalOptions = ["UV", "Reduced OH/PD", "EN81"];

    public static PricingCalculationRequest Normalize(PricingCalculationRequest request)
    {
        if (!request.Supplier.Equals("SMEC", StringComparison.OrdinalIgnoreCase)) return request;
        var fields = new Dictionary<string, string>(request.SpecificationFields ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        var options = (request.Options ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var remaining = new List<string>();
        var originalKeys = fields.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        void Set(string key, string value)
        {
            // Explicit structured selections (including an empty selection) supersede old text.
            if (!originalKeys.Contains(key)) fields[key] = value;
        }
        void Option(string code)
        {
            if (!options.Contains(code, StringComparer.OrdinalIgnoreCase)) options.Add(code);
        }

        foreach (var line in fields.GetValueOrDefault("Other Requirements", "")
                     .Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var value = Regex.Replace(line, @"^\s*\d+[.)]?\s+", "");
            var fire = Regex.Match(value, @"\b(EI?(?:30|60|120))\b", RegexOptions.IgnoreCase);
            var glass = Regex.Match(value, @"\bZPKG-(050|150|200)A?\b", RegexOptions.IgnoreCase);
            var finish = Regex.Match(value, @"ZDT-\d{3}", RegexOptions.IgnoreCase).Value.ToUpperInvariant();
            var material = value.Contains("SUS-M", StringComparison.OrdinalIgnoreCase) ? "SUS-M" : "SUS-H";
            bool Has(string text) => value.Contains(text, StringComparison.OrdinalIgnoreCase);
            if (fire.Success) Set("Fire Rating", fire.Value.ToUpperInvariant());
            else if (glass.Success) Set("Glass Door", $"ZPKG-{glass.Groups[1].Value}");
            else if (Has("CWT")) Option("CWT Safety Gear");
            else if (Has("oller")) Option("Roller guide shoe");
            else if (Has("UV")) Option("UV");
            else if (Has("ickplate")) Set("Kickplate Finish", finish.Length > 0 ? finish : material);
            else if (Has("andrail")) Set("Handrail Finish", finish.Length > 0 ? finish : material);
            else if (Has("utton")) Set("Button Finish", finish.Length > 0 ? finish : material);
            else if (Has("OH/PD") || Has("PD/OH")) Option("Reduced OH/PD");
            else if (Has("aceplate"))
            {
                if (finish == "ZDT-007") finish = "ZDT-001";
                var code = finish.Length > 0 ? $"{finish} {material}" : material;
                if (Has("COP")) Set("COP Faceplate", code);
                else
                {
                    Set("Main LOP Faceplate", code);
                    Set("Other LOP Faceplate", code);
                }
            }
            else if (Has("EN81")) Option("EN81");
            else remaining.Add(line);
        }
        fields["Other Requirements"] = string.Join('\n', remaining);
        return request with { SpecificationFields = fields, Options = options };
    }
}
