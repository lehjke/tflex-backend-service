using System.Collections.Generic;
using System.IO;

namespace TFlexAutomationRunner
{
    internal static class ParameterFileParser
    {
        public static IDictionary<string, object> Read(string path)
        {
            var result = new Dictionary<string, object>(System.StringComparer.OrdinalIgnoreCase);

            foreach (var line in File.ReadLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("//", System.StringComparison.Ordinal))
                {
                    continue;
                }

                var separatorIndex = trimmed.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var name = trimmed.Substring(0, separatorIndex).Trim();
                var value = trimmed.Substring(separatorIndex + 1).Trim();

                if (value.EndsWith(";", System.StringComparison.Ordinal))
                {
                    value = value.Substring(0, value.Length - 1).Trim();
                }

                result[name] = Unquote(value);
            }

            return result;
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            {
                value = value.Substring(1, value.Length - 2);
                value = value.Replace("\\\"", "\"").Replace("\\\\", "\\");
            }

            return value;
        }
    }
}
