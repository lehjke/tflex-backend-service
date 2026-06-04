using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace TFlexAutomationRunner
{
    internal sealed class AutomationRequest
    {
        public string JobId { get; private set; }

        public string TemplateId { get; private set; }

        public string TemplateCode { get; private set; }

        public string WorkingDirectory { get; private set; }

        public string TemplateCopyPath { get; private set; }

        public string ResultDirectory { get; private set; }

        public string ParameterFilePath { get; private set; }

        public string OutputFormat { get; private set; }

        public Dictionary<string, object> Parameters { get; private set; }

        public static AutomationRequest Load(string requestPath)
        {
            var serializer = new JavaScriptSerializer();
            var content = File.ReadAllText(requestPath, Encoding.UTF8);
            var root = serializer.DeserializeObject(content) as Dictionary<string, object>;

            if (root == null)
            {
                throw new InvalidOperationException("Automation request JSON must be an object.");
            }

            var request = new AutomationRequest
            {
                JobId = ReadRequiredString(root, "jobId"),
                TemplateId = ReadRequiredString(root, "templateId"),
                TemplateCode = ReadRequiredString(root, "templateCode"),
                WorkingDirectory = ReadRequiredString(root, "workingDirectory"),
                TemplateCopyPath = ReadRequiredString(root, "templateCopyPath"),
                ResultDirectory = ReadRequiredString(root, "resultDirectory"),
                ParameterFilePath = ReadOptionalString(root, "parameterFilePath"),
                OutputFormat = NormalizeFormat(ReadRequiredString(root, "outputFormat")),
                Parameters = ReadParameters(root)
            };

            request.ResolvePaths(requestPath);
            request.MergeParameterFileValues();
            request.Validate();

            return request;
        }

        private void MergeParameterFileValues()
        {
            if (string.IsNullOrWhiteSpace(ParameterFilePath))
            {
                return;
            }

            if (!File.Exists(ParameterFilePath))
            {
                return;
            }

            foreach (var pair in ParameterFileParser.Read(ParameterFilePath))
            {
                if (!Parameters.ContainsKey(pair.Key))
                {
                    Parameters.Add(pair.Key, pair.Value);
                }
            }
        }

        private void ResolvePaths(string requestPath)
        {
            var requestDirectory = Path.GetDirectoryName(Path.GetFullPath(requestPath));
            WorkingDirectory = ResolvePath(requestDirectory, WorkingDirectory);
            TemplateCopyPath = ResolvePath(requestDirectory, TemplateCopyPath);
            ResultDirectory = ResolvePath(requestDirectory, ResultDirectory);

            if (!string.IsNullOrWhiteSpace(ParameterFilePath))
            {
                ParameterFilePath = ResolvePath(requestDirectory, ParameterFilePath);
            }
        }

        private void Validate()
        {
            if (!File.Exists(TemplateCopyPath))
            {
                throw new FileNotFoundException("Template copy was not found.", TemplateCopyPath);
            }

            if (OutputFormat != "pdf" && OutputFormat != "dwg" && OutputFormat != "dxf")
            {
                throw new NotSupportedException("Unsupported output format '" + OutputFormat + "'.");
            }

            Directory.CreateDirectory(ResultDirectory);
        }

        private static Dictionary<string, object> ReadParameters(Dictionary<string, object> root)
        {
            var parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            object raw;
            if (!TryGetValue(root, "parameters", out raw) || raw == null)
            {
                return parameters;
            }

            var dictionary = raw as Dictionary<string, object>;
            if (dictionary == null)
            {
                throw new InvalidOperationException("Automation request 'parameters' must be an object.");
            }

            foreach (var pair in dictionary)
            {
                parameters[pair.Key] = pair.Value;
            }

            return parameters;
        }

        private static string ReadRequiredString(Dictionary<string, object> root, string name)
        {
            var value = ReadOptionalString(root, name);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("Automation request is missing required property '" + name + "'.");
            }

            return value;
        }

        private static string ReadOptionalString(Dictionary<string, object> root, string name)
        {
            object value;
            if (!TryGetValue(root, name, out value) || value == null)
            {
                return string.Empty;
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static bool TryGetValue(Dictionary<string, object> root, string name, out object value)
        {
            if (root.TryGetValue(name, out value))
            {
                return true;
            }

            foreach (var pair in root)
            {
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        private static string ResolvePath(string baseDirectory, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            return Path.GetFullPath(Path.IsPathRooted(path)
                ? path
                : Path.Combine(baseDirectory ?? Environment.CurrentDirectory, path));
        }

        private static string NormalizeFormat(string format)
        {
            return format.Trim().TrimStart('.').ToLowerInvariant();
        }
    }
}
