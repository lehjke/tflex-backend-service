using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using TFlex;
using TFlex.Model;
using TFlex.Model.Model2D;

namespace TFlexAutomationRunner
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            var responsePath = string.Empty;

            try
            {
                if (args.Length == 0)
                {
                    throw new ArgumentException(
                        "Usage: TFlexAutomationRunner.exe --health-check | <requestPath> <responsePath>");
                }

                if (string.Equals(args[0], "--health-check", StringComparison.OrdinalIgnoreCase))
                {
                    TFlexApiBootstrap.Initialize();
                    CheckHealth();
                    Console.WriteLine("T-FLEX CAD Open API session is ready.");
                    return 0;
                }

                if (string.Equals(args[0], "--inspect-controls", StringComparison.OrdinalIgnoreCase))
                {
                    if (args.Length < 3)
                    {
                        throw new ArgumentException("Usage: TFlexAutomationRunner.exe --inspect-controls <templatePath> <outputPath>");
                    }

                    var templatePath = Path.GetFullPath(args[1]);
                    var outputPath = Path.GetFullPath(args[2]);

                    TFlexApiBootstrap.Initialize();
                    InspectControls(templatePath, outputPath);
                    return 0;
                }

                if (string.Equals(args[0], "--inspect-variables", StringComparison.OrdinalIgnoreCase))
                {
                    if (args.Length < 3)
                    {
                        throw new ArgumentException(
                            "Usage: TFlexAutomationRunner.exe --inspect-variables <templatePath> <outputPath> [parametersPath]");
                    }

                    var templatePath = Path.GetFullPath(args[1]);
                    var outputPath = Path.GetFullPath(args[2]);
                    var parametersPath = args.Length > 3 ? Path.GetFullPath(args[3]) : null;

                    TFlexApiBootstrap.Initialize();
                    InspectVariables(templatePath, outputPath, parametersPath);
                    return 0;
                }

                if (args.Length < 2)
                {
                    throw new ArgumentException("Usage: TFlexAutomationRunner.exe <requestPath> <responsePath>");
                }

                var requestPath = Path.GetFullPath(args[0]);
                responsePath = Path.GetFullPath(args[1]);

                TFlexApiBootstrap.Initialize();
                var request = AutomationRequest.Load(requestPath);
                var resultFilePath = RunAutomation(request);
                WriteSuccessResponse(responsePath, resultFilePath, request.OutputFormat);

                Console.WriteLine("Generated " + resultFilePath);
                return 0;
            }
            catch (Exception exception)
            {
                TryWriteErrorResponse(responsePath, exception);
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static void CheckHealth()
        {
            var setup = new ApplicationSessionSetup
            {
                ReadOnly = true,
                Enable3D = true,
                EnableDOCs = false,
                EnableMacros = false,
                PromptToSaveModifiedDocuments = false
            };

            if (!Application.InitSession(setup))
            {
                throw new InvalidOperationException(
                    "T-FLEX CAD Open API session initialization failed.");
            }

            Application.ExitSession();
        }

        private static void InspectVariables(string templatePath, string outputPath, string parametersPath)
        {
            var parameters = string.IsNullOrWhiteSpace(parametersPath)
                ? null
                : LoadInspectionParameters(parametersPath);
            var setup = new ApplicationSessionSetup
            {
                ReadOnly = parameters == null,
                Enable3D = true,
                EnableDOCs = false,
                EnableMacros = false,
                PromptToSaveModifiedDocuments = false
            };

            if (!Application.InitSession(setup))
            {
                throw new InvalidOperationException("T-FLEX CAD Open API session initialization failed.");
            }

            Document document = null;
            try
            {
                Application.DisableSubstituteFontDialog = true;
                document = Application.OpenDocument(templatePath, false, parameters == null);
                if (document == null)
                {
                    throw new InvalidOperationException("T-FLEX CAD failed to open document '" + templatePath + "'.");
                }

                if (parameters != null)
                {
                    ApplyParameters(document, parameters);
                    Regenerate(document);
                }

                var variables = new List<Dictionary<string, object>>();
                foreach (Variable variable in document.GetVariables())
                {
                    var item = new Dictionary<string, object>
                    {
                        ["name"] = GetSafeValue(delegate { return variable.Name; }),
                        ["expression"] = GetSafeValue(delegate { return variable.Expression; }),
                        ["value"] = FormatVariableValue(variable),
                        ["isText"] = GetSafeValue(delegate { return variable.IsText; }),
                        ["isReal"] = GetSafeValue(delegate { return variable.IsReal; }),
                        ["isUsed"] = GetSafeValue(delegate { return variable.IsUsed; }),
                        ["isConstant"] = GetSafeValue(delegate { return variable.IsConstant; }),
                        ["hidden"] = GetSafeValue(delegate { return variable.Hidden; }),
                        ["service"] = GetSafeValue(delegate { return variable.Service; }),
                        ["external"] = GetSafeValue(delegate { return variable.External; }),
                        ["comment"] = GetSafeValue(delegate { return variable.Comment; }),
                        ["groupName"] = GetSafeValue(delegate { return variable.GroupName; }),
                        ["unit"] = GetSafeString(delegate { return variable.Unit; }),
                        ["autoUnit"] = GetSafeValue(delegate { return variable.AutoUnit; }),
                        ["listType"] = GetSafeString(delegate { return variable.ListType; }),
                        ["groupType"] = GetSafeString(delegate { return variable.GroupType; }),
                        ["errorState"] = GetSafeString(delegate { return variable.ErrorState; }),
                        ["errorString"] = GetSafeValue(delegate { return variable.ErrorString; }),
                        ["allowedValues"] = GetVariableValueList(variable)
                    };

                    variables.Add(item);
                }

                var serializer = new JavaScriptSerializer();
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                File.WriteAllText(outputPath, serializer.Serialize(variables), Encoding.UTF8);
            }
            finally
            {
                if (document != null && !document.IsDisposed)
                {
                    document.Close();
                }

                Application.ExitSession();
            }
        }

        private static IDictionary<string, object> LoadInspectionParameters(string parametersPath)
        {
            if (!File.Exists(parametersPath))
            {
                throw new FileNotFoundException("Inspection parameters file was not found.", parametersPath);
            }

            var serializer = new JavaScriptSerializer();
            return serializer.Deserialize<Dictionary<string, object>>(
                File.ReadAllText(parametersPath, Encoding.UTF8));
        }

        private static string RunAutomation(AutomationRequest request)
        {
            var setup = new ApplicationSessionSetup
            {
                ReadOnly = false,
                Enable3D = true,
                EnableDOCs = false,
                EnableMacros = false,
                PromptToSaveModifiedDocuments = false
            };

            if (!Application.InitSession(setup))
            {
                throw new InvalidOperationException("T-FLEX CAD Open API session initialization failed.");
            }

            Document document = null;
            try
            {
                Application.DisableSubstituteFontDialog = true;
                document = Application.OpenDocument(request.TemplateCopyPath, false, false);
                if (document == null)
                {
                    throw new InvalidOperationException("T-FLEX CAD failed to open document '" + request.TemplateCopyPath + "'.");
                }

                ApplyParameters(document, request.Parameters);
                Regenerate(document);

                return Export(document, request);
            }
            finally
            {
                if (document != null && !document.IsDisposed)
                {
                    document.Close();
                }

                Application.ExitSession();
            }
        }

        private static void InspectControls(string templatePath, string outputPath)
        {
            var setup = new ApplicationSessionSetup
            {
                ReadOnly = false,
                Enable3D = true,
                EnableDOCs = false,
                EnableMacros = false,
                PromptToSaveModifiedDocuments = false
            };

            if (!Application.InitSession(setup))
            {
                throw new InvalidOperationException("T-FLEX CAD Open API session initialization failed.");
            }

            Document document = null;
            try
            {
                Application.DisableSubstituteFontDialog = true;
                document = Application.OpenDocument(templatePath, false, false);
                if (document == null)
                {
                    throw new InvalidOperationException("T-FLEX CAD failed to open document '" + templatePath + "'.");
                }

                var controls = new List<Dictionary<string, object>>();
                foreach (ModelObject modelObject in document.GetObjects())
                {
                    if (!modelObject.IsKindOf(ObjectType.Control))
                    {
                        continue;
                    }

                    var control = modelObject as TFlex.Model.Model2D.Control;
                    var variableControl = modelObject as VariableControl;
                    var staticTextControl = modelObject as StaticTextControl;
                    var checkBoxControl = modelObject as CheckBoxControl;
                    var variable = variableControl == null ? null : variableControl.Variable;

                    var item = new Dictionary<string, object>
                    {
                        ["objectName"] = modelObject.Name,
                        ["objectType"] = modelObject.GetType().FullName,
                        ["controlType"] = control == null ? null : control.ControlType.ToString(),
                        ["level"] = control == null ? null : FormatParameter(control.Level),
                        ["levelVariable"] = control == null ? null : GetParameterVariableName(control.Level),
                        ["levelValue"] = control == null ? null : GetParameterValue(control.Level),
                        ["variable"] = variable == null ? null : variable.Name,
                        ["variableExpression"] = variable == null ? null : variable.Expression,
                        ["variableValue"] = FormatVariableValue(variable),
                        ["allowedValues"] = GetVariableValueList(variable),
                        ["caption"] = staticTextControl == null ? null : staticTextControl.Caption,
                        ["valueOn"] = checkBoxControl == null ? null : checkBoxControl.ValueOn,
                        ["valueOff"] = checkBoxControl == null ? null : checkBoxControl.ValueOff,
                        ["x1"] = control == null ? null : FormatParameter(control.X1),
                        ["y1"] = control == null ? null : FormatParameter(control.Y1),
                        ["x2"] = control == null ? null : FormatParameter(control.X2),
                        ["y2"] = control == null ? null : FormatParameter(control.Y2)
                    };

                    controls.Add(item);
                }

                var serializer = new JavaScriptSerializer();
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                File.WriteAllText(outputPath, serializer.Serialize(controls), Encoding.UTF8);
            }
            finally
            {
                if (document != null && !document.IsDisposed)
                {
                    document.Close();
                }

                Application.ExitSession();
            }
        }

        private static string FormatParameter(Parameter parameter)
        {
            if (parameter == null)
            {
                return null;
            }

            return parameter.Variable == null
                ? parameter.Value.ToString(CultureInfo.InvariantCulture)
                : parameter.Variable.Expression;
        }

        private static string GetParameterVariableName(Parameter parameter)
        {
            return parameter == null || parameter.Variable == null ? null : parameter.Variable.Name;
        }

        private static string GetParameterValue(Parameter parameter)
        {
            return parameter == null ? null : parameter.Value.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatVariableValue(Variable variable)
        {
            if (variable == null)
            {
                return null;
            }

            if (variable.IsText)
            {
                return variable.TextValue;
            }

            return variable.IsReal ? variable.RealValue.ToString(CultureInfo.InvariantCulture) : variable.Expression;
        }

        private static List<string> GetVariableValueList(Variable variable)
        {
            var values = new List<string>();
            if (variable == null)
            {
                return values;
            }

            for (var index = 0; index < variable.ValueListCount; index++)
            {
                values.Add(variable.GetValueListString(index));
            }

            return values;
        }

        private static object GetSafeValue(Func<object> getter)
        {
            try
            {
                return getter();
            }
            catch (Exception exception)
            {
                return "#ERROR: " + exception.GetType().Name + ": " + exception.Message;
            }
        }

        private static string GetSafeString(Func<object> getter)
        {
            var value = GetSafeValue(getter);
            return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static void ApplyParameters(Document document, IDictionary<string, object> parameters)
        {
            if (parameters == null || parameters.Count == 0)
            {
                return;
            }

            var variables = new Dictionary<string, Variable>(StringComparer.OrdinalIgnoreCase);
            foreach (var variable in document.GetVariables())
            {
                if (!variables.ContainsKey(variable.Name))
                {
                    variables.Add(variable.Name, variable);
                }
            }

            var missing = new List<string>();
            foreach (var name in parameters.Keys)
            {
                if (document.FindVariable(name) == null && !variables.ContainsKey(name))
                {
                    missing.Add(name);
                }
            }

            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    "T-FLEX document does not contain required variable(s): " + string.Join(", ", missing.ToArray()));
            }

            var changesStarted = false;
            document.BeginChanges("Set automation parameters");
            changesStarted = true;

            try
            {
                foreach (var pair in parameters)
                {
                    var variable = document.FindVariable(pair.Key);
                    if (variable == null)
                    {
                        variable = variables[pair.Key];
                    }

                    SetVariableValue(variable, pair.Value);
                }

                document.EndChanges(true, true);
                changesStarted = false;
            }
            finally
            {
                if (changesStarted)
                {
                    document.CancelChanges();
                }
            }
        }

        private static void SetVariableValue(Variable variable, object value)
        {
            if (variable.IsText)
            {
                variable.Expression = Quote(ConvertToText(value));
                return;
            }

            if (variable.IsReal)
            {
                variable.Expression = RealVariableValueFormatter.Format(value);
                return;
            }

            variable.Expression = ConvertToExpression(value);
        }

        private static void Regenerate(Document document)
        {
            var options = new RegenerateOptions
            {
                Full = true,
                NotifyPlugins = true,
                Projections = true,
                UpdateAllLinks = true,
                UpdateBillOfMaterials = true,
                UpdateDrawingViews = true,
                UpdateProductStructures = true,
                UpdateSymbols = true
            };

            document.Regenerate(options);
        }

        private static string Export(Document document, AutomationRequest request)
        {
            var resultPath = Path.Combine(
                request.ResultDirectory,
                SanitizeFileName(request.TemplateCode + "_" + request.JobId) + "." + request.OutputFormat);

            if (File.Exists(resultPath))
            {
                File.Delete(resultPath);
            }

            if (request.OutputFormat == "pdf")
            {
                var exporter = new ExportToPDF(document)
                {
                    OpenExportFile = false,
                    Export3DModel = false,
                    IsSelectPagesDialogEnabled = false
                };

                var pages = GetPages(document);
                if (pages.Count > 0)
                {
                    exporter.ExportPages = pages;
                }

                if (!exporter.Export(resultPath))
                {
                    throw new InvalidOperationException("T-FLEX PDF export failed.");
                }

                return resultPath;
            }

            if (request.OutputFormat == "dwg")
            {
                var exporter = new ExportToDWG(document)
                {
                    ExportAllPages = true
                };

                var pages = GetPages(document);
                if (pages.Count > 0)
                {
                    exporter.ExportPages = pages;
                }

                if (!exporter.Export(resultPath))
                {
                    throw new InvalidOperationException("T-FLEX DWG export failed.");
                }

                return resultPath;
            }

            if (request.OutputFormat == "dxf")
            {
                var exporter = new ExportToDXF(document)
                {
                    ExportAllPages = true
                };

                var pages = GetPages(document);
                if (pages.Count > 0)
                {
                    exporter.ExportPages = pages;
                }

                if (!exporter.Export(resultPath))
                {
                    throw new InvalidOperationException("T-FLEX DXF export failed.");
                }

                return resultPath;
            }

            throw new NotSupportedException("Unsupported output format '" + request.OutputFormat + "'.");
        }

        private static List<Page> GetPages(Document document)
        {
            var pages = new List<Page>();
            foreach (var page in document.GetPages())
            {
                pages.Add(page);
            }

            return pages;
        }

        private static string ConvertToExpression(object value)
        {
            if (value == null)
            {
                return "\"\"";
            }

            if (value is string)
            {
                return Quote((string)value);
            }

            if (value is bool)
            {
                return (bool)value ? "1" : "0";
            }

            var formattable = value as IFormattable;
            if (formattable != null)
            {
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            }

            return Quote(Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        private static string ConvertToText(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string SanitizeFileName(string value)
        {
            var invalid = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
            var sanitized = Regex.Replace(value, "[" + invalid + "]+", "_").Trim('_', ' ');
            return string.IsNullOrWhiteSpace(sanitized) ? "result" : sanitized;
        }

        private static void WriteSuccessResponse(string responsePath, string filePath, string format)
        {
            EnsureParentDirectory(responsePath);

            var serializer = new JavaScriptSerializer();
            var fileName = Path.GetFileName(filePath);
            var response = new
            {
                files = new[]
                {
                    new
                    {
                        path = fileName,
                        fileName = fileName,
                        format = format
                    }
                }
            };

            File.WriteAllText(responsePath, serializer.Serialize(response), Encoding.UTF8);
        }

        private static void TryWriteErrorResponse(string responsePath, Exception exception)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(responsePath))
                {
                    return;
                }

                EnsureParentDirectory(responsePath);
                var serializer = new JavaScriptSerializer();
                File.WriteAllText(
                    responsePath,
                    serializer.Serialize(new { errorMessage = exception.Message }),
                    Encoding.UTF8);
            }
            catch
            {
                // Standard error still carries the exception if the response cannot be written.
            }
        }

        private static void EnsureParentDirectory(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
    }
}
