using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Core.Abstractions;
using TFlexDrawingService.Core.Models;
using TFlexDrawingService.Infrastructure.Automation;
using TFlexDrawingService.Infrastructure.Configuration;
using TFlexDrawingService.Infrastructure.Persistence;

namespace TFlexDrawingService.Infrastructure.Services;

public sealed class TemplateAnalysisProcessor(
    TemplateAnalysisStore store,
    ITemplateCatalog templateCatalog,
    IOptions<TFlexAutomationOptions> options,
    TFlexAutomationExecutionGate executionGate,
    ILogger<TemplateAnalysisProcessor> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly TFlexAutomationOptions _options = options.Value;

    public async Task ProcessAsync(
        TemplateAnalysisJob job,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateInput(job);
            var inspection = await InspectAsync(job, cancellationToken);
            var references = await templateCatalog.ListAsync(cancellationToken);
            var draft = new TemplateDraftBuilder().Build(job, inspection, references);
            var components = InspectComponents(job.ComponentsDirectoryPath);
            var warnings = draft.Warnings.ToList();
            AddComponentWarnings(job, components, warnings);
            AddMissingDependencyWarnings(inspection.Dependencies, components, warnings);

            await store.CompleteAsync(
                job.Id,
                JsonSerializer.Serialize(draft.Template, JsonOptions),
                JsonSerializer.Serialize(inspection, JsonOptions),
                JsonSerializer.Serialize(warnings.Distinct(StringComparer.OrdinalIgnoreCase), JsonOptions),
                JsonSerializer.Serialize(components, JsonOptions),
                cancellationToken);

            logger.LogInformation(
                "T-FLEX template analysis {AnalysisId} completed with {ParameterCount} external parameter(s) and {WarningCount} warning(s).",
                job.Id,
                draft.Template.Parameters.Count,
                warnings.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "T-FLEX template analysis {AnalysisId} failed.", job.Id);
            await store.FailAsync(job.Id, SanitizeError(exception), CancellationToken.None);
        }
    }

    private async Task<TFlexTemplateInspection> InspectAsync(
        TemplateAnalysisJob job,
        CancellationToken cancellationToken)
    {
        if (!IsExternalMode(_options.Mode)
            || string.IsNullOrWhiteSpace(_options.CommandPath)
            || !File.Exists(_options.CommandPath))
        {
            throw new InvalidOperationException(
                "Automatic template parsing requires the configured Windows T-FLEX automation runner.");
        }

        var outputPath = Path.Combine(
            Path.GetDirectoryName(job.TemplateFilePath)!,
            "tflex-template-inspection.json");
        await executionGate.WaitAsync(cancellationToken);
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _options.CommandPath,
                WorkingDirectory = Path.GetDirectoryName(job.TemplateFilePath)!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--inspect-template");
            startInfo.ArgumentList.Add(job.TemplateFilePath);
            startInfo.ArgumentList.Add(outputPath);

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                throw new InvalidOperationException("The T-FLEX template inspection process could not be started.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var timeout = TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds));
            try
            {
                await process.WaitForExitAsync(cancellationToken).WaitAsync(timeout, cancellationToken);
            }
            catch
            {
                TryKill(process);
                throw;
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"T-FLEX template inspection exited with code {process.ExitCode}. {stderr}".Trim());
            }

            if (!string.IsNullOrWhiteSpace(stdout))
            {
                logger.LogInformation(
                    "T-FLEX template inspection output for {AnalysisId}: {Output}",
                    job.Id,
                    stdout);
            }
        }
        finally
        {
            executionGate.Release();
        }

        if (!File.Exists(outputPath))
        {
            throw new InvalidOperationException("T-FLEX did not produce a template inspection result.");
        }

        await using var stream = File.OpenRead(outputPath);
        return await JsonSerializer.DeserializeAsync<TFlexTemplateInspection>(
                   stream,
                   JsonOptions,
                   cancellationToken)
               ?? throw new InvalidDataException("T-FLEX returned an empty template inspection result.");
    }

    private static IReadOnlyList<TemplateComponentFile> InspectComponents(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return [];
        }

        var root = Path.GetFullPath(directoryPath);
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path =>
            {
                var info = new FileInfo(path);
                var extension = info.Extension.ToLowerInvariant();
                return new TemplateComponentFile(
                    Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'),
                    info.Length,
                    extension switch
                    {
                        ".grb" => "tflex-component",
                        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".svg" => "image",
                        ".mat" or ".mtr" => "material",
                        _ => "asset"
                    },
                    extension is ".grb" or ".3d" or ".tf3d");
            })
            .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddComponentWarnings(
        TemplateAnalysisJob job,
        IReadOnlyList<TemplateComponentFile> components,
        ICollection<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(job.ComponentsDirectoryPath))
        {
            warnings.Add("No components ZIP was uploaded; linked fragments and visual assets could not be verified.");
            return;
        }

        if (components.Count == 0)
        {
            warnings.Add("The components archive is empty.");
        }
        else if (!components.Any(item => item.IsPotentialTemplateDependency))
        {
            warnings.Add("The components archive contains no recognizable T-FLEX component files.");
        }
    }

    private static void AddMissingDependencyWarnings(
        IReadOnlyList<TFlexDocumentDependency> dependencies,
        IReadOnlyList<TemplateComponentFile> components,
        ICollection<string> warnings)
    {
        if (dependencies.Count == 0)
        {
            return;
        }

        var componentNames = components
            .Select(item => Path.GetFileName(item.RelativePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var dependency in dependencies)
        {
            var fileName = Path.GetFileName(dependency.Value?.Replace('\\', Path.DirectorySeparatorChar));
            if (!string.IsNullOrWhiteSpace(fileName) && !componentNames.Contains(fileName))
            {
                warnings.Add(
                    $"Linked T-FLEX asset '{fileName}' was referenced by the template but was not found in the components archive.");
            }
        }
    }

    private static void ValidateInput(TemplateAnalysisJob job)
    {
        if (!File.Exists(job.TemplateFilePath))
        {
            throw new FileNotFoundException("Uploaded T-FLEX template file was not found.", job.TemplateFilePath);
        }
    }

    private static bool IsExternalMode(string? mode)
    {
        return mode?.Trim().ToLowerInvariant() is "external" or "externalprocess" or "real";
    }

    private static string SanitizeError(Exception exception)
    {
        var text = exception.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= 2000 ? text : text[..2000];
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
            // Preserve the original timeout/cancellation error.
        }
    }
}
