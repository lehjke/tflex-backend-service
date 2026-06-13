using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Core.Models;
using TFlexDrawingService.Infrastructure.Automation;
using TFlexDrawingService.Infrastructure.Configuration;

namespace TFlexDrawingService.Tests;

public sealed class ExternalProcessTFlexAutomationClientTests
{
    [Fact]
    public async Task GenerateAsync_InvokesExternalCommandAndCollectsGeneratedFile()
    {
        if (!File.Exists("/bin/sh"))
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);

        var scriptPath = Path.Combine(root, "fake-tflex.sh");
        await File.WriteAllTextAsync(
            scriptPath,
            """
            #!/bin/sh
            set -eu
            mkdir -p "$1"
            printf '%s' 'generated pdf' > "$1/result.pdf"
            """);

        var workingDirectory = Path.Combine(root, "work");
        var resultDirectory = Path.Combine(root, "result");
        Directory.CreateDirectory(workingDirectory);

        var templateCopyPath = Path.Combine(workingDirectory, "template.grb");
        await File.WriteAllTextAsync(templateCopyPath, "template");

        var client = new ExternalProcessTFlexAutomationClient(
            Options.Create(new TFlexAutomationOptions
            {
                CommandPath = "/bin/sh",
                Arguments = $"\"{scriptPath}\" \"{{resultDirectory}}\"",
                TimeoutSeconds = 10
            }),
            NullLogger<ExternalProcessTFlexAutomationClient>.Instance);

        var files = await client.GenerateAsync(
            new TFlexGenerationRequest(
                new DrawingJob { Id = "job-1" },
                new DrawingTemplate { Id = "template", Code = "template", Name = "Template" },
                workingDirectory,
                templateCopyPath,
                resultDirectory,
                new Dictionary<string, object?> { ["WIDTH"] = 1000 },
                "pdf"));

        Assert.Single(files);
        Assert.Equal("result.pdf", files[0].FileName);
        Assert.Equal("pdf", files[0].Format);
        Assert.True(File.Exists(Path.Combine(workingDirectory, "tflex-automation-request.json")));
        Assert.True(File.Exists(Path.Combine(workingDirectory, "parameters.par")));
    }

    [Fact]
    public async Task GenerateAsync_RejectsResponseFileOutsideResultDirectory()
    {
        if (!File.Exists("/bin/sh"))
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);

        var scriptPath = Path.Combine(root, "fake-tflex.sh");
        await File.WriteAllTextAsync(
            scriptPath,
            """
            #!/bin/sh
            set -eu
            mkdir -p "$1"
            printf '%s' 'outside pdf' > "$(dirname "$1")/outside.pdf"
            printf '%s' '{"files":[{"path":"../outside.pdf","fileName":"outside.pdf","format":"pdf"}]}' > "$2"
            """);

        var workingDirectory = Path.Combine(root, "work");
        var resultDirectory = Path.Combine(root, "result");
        Directory.CreateDirectory(workingDirectory);

        var templateCopyPath = Path.Combine(workingDirectory, "template.grb");
        await File.WriteAllTextAsync(templateCopyPath, "template");

        var client = new ExternalProcessTFlexAutomationClient(
            Options.Create(new TFlexAutomationOptions
            {
                CommandPath = "/bin/sh",
                Arguments = $"\"{scriptPath}\" \"{{resultDirectory}}\" \"{{responsePath}}\"",
                TimeoutSeconds = 10
            }),
            NullLogger<ExternalProcessTFlexAutomationClient>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GenerateAsync(
                new TFlexGenerationRequest(
                    new DrawingJob { Id = "job-1" },
                    new DrawingTemplate { Id = "template", Code = "template", Name = "Template" },
                    workingDirectory,
                    templateCopyPath,
                    resultDirectory,
                    new Dictionary<string, object?> { ["WIDTH"] = 1000 },
                    "pdf")));

        Assert.Contains("outside the result directory", exception.Message);
    }
}
