using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Core.Abstractions;
using TFlexDrawingService.Core.Models;
using TFlexDrawingService.Infrastructure.Configuration;

namespace TFlexDrawingService.Infrastructure.Storage;

public sealed class LocalFileStorage(IOptions<DrawingStorageOptions> options) : IFileStorage
{
    public Task<string> CreateWorkingDirectoryAsync(DrawingJob job, CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(
            options.Value.RootPath,
            "jobs",
            job.Id,
            GetAttemptDirectoryName(job));
        ResetDirectory(directory, cancellationToken);
        return Task.FromResult(directory);
    }

    public Task<string> CopyTemplateToWorkingDirectoryAsync(
        DrawingTemplate template,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(template.TemplateFilePath))
        {
            throw new FileNotFoundException("T-FLEX template file was not found.", template.TemplateFilePath);
        }

        Directory.CreateDirectory(workingDirectory);
        var destination = Path.Combine(workingDirectory, Path.GetFileName(template.TemplateFilePath));
        File.Copy(template.TemplateFilePath, destination, overwrite: true);

        var templateFragmentsDirectory = Path.Combine(
            Path.GetDirectoryName(template.TemplateFilePath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(template.TemplateFilePath));

        if (Directory.Exists(templateFragmentsDirectory))
        {
            var fragmentsDestination = Path.Combine(workingDirectory, Path.GetFileName(templateFragmentsDirectory));
            CopyDirectory(templateFragmentsDirectory, fragmentsDestination, cancellationToken);
        }

        return Task.FromResult(destination);
    }

    public string CreateGeneratedDirectory(DrawingJob job)
    {
        var directory = Path.Combine(
            options.Value.RootPath,
            "generated",
            job.Id,
            GetAttemptDirectoryName(job));
        ResetDirectory(directory, CancellationToken.None);
        return directory;
    }

    private static string GetAttemptDirectoryName(DrawingJob job)
    {
        if (string.IsNullOrWhiteSpace(job.LeaseToken))
        {
            throw new InvalidOperationException(
                $"Drawing job '{job.Id}' does not have an attempt lease token.");
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(job.LeaseToken));
        return $"attempt-{Convert.ToHexStringLower(hash.AsSpan(0, 16))}";
    }

    private static void ResetDirectory(string directory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(directory);
    }

    private static void CopyDirectory(
        string sourceDirectory,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destinationDirectory);

        foreach (var filePath in Directory.EnumerateFiles(sourceDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(filePath));
            File.Copy(filePath, destinationPath, overwrite: true);
        }

        foreach (var directoryPath in Directory.EnumerateDirectories(sourceDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nestedDestination = Path.Combine(destinationDirectory, Path.GetFileName(directoryPath));
            CopyDirectory(directoryPath, nestedDestination, cancellationToken);
        }
    }
}
