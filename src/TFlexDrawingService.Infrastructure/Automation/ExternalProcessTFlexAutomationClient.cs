using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Core.Abstractions;
using TFlexDrawingService.Core.Models;
using TFlexDrawingService.Infrastructure.Configuration;

namespace TFlexDrawingService.Infrastructure.Automation;

public sealed class ExternalProcessTFlexAutomationClient(
    IOptions<TFlexAutomationOptions> options,
    ILogger<ExternalProcessTFlexAutomationClient> logger) : ITFlexAutomationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly TFlexAutomationOptions _options = options.Value;

    public async Task<IReadOnlyList<GeneratedFile>> GenerateAsync(
        TFlexGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.CommandPath))
        {
            throw new InvalidOperationException(
                "Real T-FLEX automation is selected, but TFlexAutomation:CommandPath is not configured.");
        }

        Directory.CreateDirectory(request.WorkingDirectory);
        Directory.CreateDirectory(request.ResultDirectory);

        var parameterFilePath = Path.Combine(request.WorkingDirectory, "parameters.par");
        if (_options.WriteParameterFile)
        {
            await WriteParameterFileAsync(parameterFilePath, request.Parameters, cancellationToken);
        }

        var requestPath = Path.Combine(request.WorkingDirectory, "tflex-automation-request.json");
        var responsePath = Path.Combine(request.WorkingDirectory, "tflex-automation-response.json");
        var payload = new ExternalAutomationRequest(
            request.Job.Id,
            request.Template.Id,
            request.Template.Code,
            request.WorkingDirectory,
            request.TemplateCopyPath,
            request.ResultDirectory,
            parameterFilePath,
            request.OutputFormat,
            request.Parameters);

        await File.WriteAllTextAsync(
            requestPath,
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            cancellationToken);

        var startInfo = new ProcessStartInfo
        {
            FileName = ReplaceTokens(_options.CommandPath, request, requestPath, responsePath, parameterFilePath),
            Arguments = ReplaceTokens(_options.Arguments, request, requestPath, responsePath, parameterFilePath),
            WorkingDirectory = request.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        logger.LogInformation(
            "Starting external T-FLEX automation command for job {JobId}: {CommandPath} {Arguments}",
            request.Job.Id,
            startInfo.FileName,
            startInfo.Arguments);

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start external T-FLEX automation command.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var timeout = TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds));

        try
        {
            await process.WaitForExitAsync(cancellationToken).WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            TryKill(process);
            throw new TimeoutException($"External T-FLEX automation timed out after {timeout}.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"External T-FLEX automation failed with exit code {process.ExitCode}. {stderr}".Trim());
        }

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            logger.LogInformation("External T-FLEX automation output for job {JobId}: {Output}", request.Job.Id, stdout);
        }

        return await CollectGeneratedFilesAsync(request, responsePath, cancellationToken);
    }

    private static async Task WriteParameterFileAsync(
        string path,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// Generated for T-FLEX automation. The runner may import this .par file or use the JSON request.");

        foreach (var (name, value) in parameters.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(name)
                .Append(" = ")
                .Append(FormatParameterValue(value))
                .AppendLine(";");
        }

        await File.WriteAllTextAsync(path, builder.ToString(), Encoding.UTF8, cancellationToken);
    }

    private static string FormatParameterValue(object? value)
    {
        return value switch
        {
            null => "\"\"",
            JsonElement { ValueKind: JsonValueKind.Number } json when json.TryGetDecimal(out var number) =>
                number.ToString(CultureInfo.InvariantCulture),
            JsonElement { ValueKind: JsonValueKind.True } => "1",
            JsonElement { ValueKind: JsonValueKind.False } => "0",
            JsonElement { ValueKind: JsonValueKind.String } json => Quote(json.GetString() ?? string.Empty),
            JsonElement json => Quote(json.ToString()),
            bool boolean => boolean ? "1" : "0",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => Quote(value.ToString() ?? string.Empty)
        };
    }

    private static string Quote(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static string ReplaceTokens(
        string value,
        TFlexGenerationRequest request,
        string requestPath,
        string responsePath,
        string parameterFilePath)
    {
        return value
            .Replace("{jobId}", request.Job.Id, StringComparison.Ordinal)
            .Replace("{workingDirectory}", request.WorkingDirectory, StringComparison.Ordinal)
            .Replace("{templateCopyPath}", request.TemplateCopyPath, StringComparison.Ordinal)
            .Replace("{resultDirectory}", request.ResultDirectory, StringComparison.Ordinal)
            .Replace("{requestPath}", requestPath, StringComparison.Ordinal)
            .Replace("{responsePath}", responsePath, StringComparison.Ordinal)
            .Replace("{parameterFilePath}", parameterFilePath, StringComparison.Ordinal)
            .Replace("{outputFormat}", request.OutputFormat, StringComparison.Ordinal);
    }

    private static async Task<IReadOnlyList<GeneratedFile>> CollectGeneratedFilesAsync(
        TFlexGenerationRequest request,
        string responsePath,
        CancellationToken cancellationToken)
    {
        var files = new List<GeneratedFile>();
        if (File.Exists(responsePath))
        {
            await using var stream = File.OpenRead(responsePath);
            var response = await JsonSerializer.DeserializeAsync<ExternalAutomationResponse>(
                stream,
                JsonOptions,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(response?.ErrorMessage))
            {
                throw new InvalidOperationException(response.ErrorMessage);
            }

            foreach (var file in response?.Files ?? [])
            {
                var path = ResolveResultPath(request.ResultDirectory, file.Path);
                files.Add(ToGeneratedFile(request.Job.Id, path, file.FileName, file.Format));
            }
        }

        if (files.Count == 0)
        {
            var expectedExtension = "." + request.OutputFormat.Trim().TrimStart('.').ToLowerInvariant();
            var generatedPaths = Directory.EnumerateFiles(request.ResultDirectory)
                .Where(path => string.Equals(Path.GetExtension(path), expectedExtension, StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

            foreach (var path in generatedPaths)
            {
                files.Add(ToGeneratedFile(request.Job.Id, path, null, request.OutputFormat));
            }
        }

        if (files.Count == 0)
        {
            throw new InvalidOperationException(
                $"External T-FLEX automation completed, but no '{request.OutputFormat}' files were found in '{request.ResultDirectory}'.");
        }

        return files;
    }

    private static string ResolveResultPath(string resultDirectory, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("External T-FLEX automation response contains an empty file path.");
        }

        var resolvedPath = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(resultDirectory, path));
        if (!IsPathUnderDirectory(resolvedPath, resultDirectory))
        {
            throw new InvalidOperationException("External T-FLEX automation response contains a file path outside the result directory.");
        }

        return resolvedPath;
    }

    private static bool IsPathUnderDirectory(string path, string directory)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var normalizedDirectory = Path.GetFullPath(directory);
        if (!normalizedDirectory.EndsWith(Path.DirectorySeparatorChar))
        {
            normalizedDirectory += Path.DirectorySeparatorChar;
        }

        return Path.GetFullPath(path).StartsWith(normalizedDirectory, comparison);
    }

    private static GeneratedFile ToGeneratedFile(string jobId, string path, string? fileName, string? format)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("External T-FLEX automation returned a missing file.", path);
        }

        var fileInfo = new FileInfo(path);
        return new GeneratedFile
        {
            JobId = jobId,
            FileName = string.IsNullOrWhiteSpace(fileName) ? fileInfo.Name : fileName,
            Format = NormalizeFormat(format ?? fileInfo.Extension),
            Path = fileInfo.FullName,
            CreatedAt = DateTimeOffset.UtcNow,
            SizeBytes = fileInfo.Length
        };
    }

    private static string NormalizeFormat(string format)
    {
        return format.Trim().TrimStart('.').ToLowerInvariant();
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // The process may have exited between the timeout and kill attempt.
        }
    }

    private sealed record ExternalAutomationRequest(
        string JobId,
        string TemplateId,
        string TemplateCode,
        string WorkingDirectory,
        string TemplateCopyPath,
        string ResultDirectory,
        string ParameterFilePath,
        string OutputFormat,
        IReadOnlyDictionary<string, object?> Parameters);

    private sealed class ExternalAutomationResponse
    {
        public string? ErrorMessage { get; set; }

        public List<ExternalGeneratedFile> Files { get; set; } = [];
    }

    private sealed class ExternalGeneratedFile
    {
        public string? Path { get; set; }

        public string? FileName { get; set; }

        public string? Format { get; set; }
    }
}
