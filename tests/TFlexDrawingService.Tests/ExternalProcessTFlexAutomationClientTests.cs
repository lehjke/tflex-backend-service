using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Core.Models;
using TFlexDrawingService.Infrastructure.Automation;
using TFlexDrawingService.Infrastructure.Configuration;

namespace TFlexDrawingService.Tests;

public sealed class ExternalProcessTFlexAutomationClientTests
{
    [Fact]
    public async Task GenerateAsync_DoesNotStartWhileAutomationIsNotReady()
    {
        var client = new ExternalProcessTFlexAutomationClient(
            Options.Create(new TFlexAutomationOptions
            {
                Mode = "ExternalProcess",
                CommandPath = Path.Combine(
                    Path.GetTempPath(),
                    Guid.NewGuid().ToString("n"),
                    "runner.exe")
            }),
            new TFlexAutomationExecutionGate(),
            new TFlexAutomationReadinessState(),
            NullLogger<ExternalProcessTFlexAutomationClient>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GenerateAsync(
                new TFlexGenerationRequest(
                    new DrawingJob { Id = "not-ready" },
                    new DrawingTemplate
                    {
                        Id = "template",
                        Code = "template",
                        Name = "Template"
                    },
                    Path.GetTempPath(),
                    Path.Combine(Path.GetTempPath(), "template.grb"),
                    Path.GetTempPath(),
                    new Dictionary<string, object?>(),
                    "pdf")));

        Assert.Contains("not ready", exception.Message);
    }

    [Fact]
    public async Task GenerateAsync_InvokesExternalCommandAndCollectsGeneratedFile()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);

        var command = await CreateTestCommandAsync(
            root,
            "generate",
            """
            #!/bin/sh
            set -eu
            mkdir -p "$1"
            printf '%s' 'generated pdf' > "$1/result.pdf"
            """,
            """
            param([string] $ResultDirectory)
            $ErrorActionPreference = 'Stop'
            New-Item -ItemType Directory -Force -Path $ResultDirectory | Out-Null
            [IO.File]::WriteAllText((Join-Path $ResultDirectory 'result.pdf'), 'generated pdf')
            """,
            "{resultDirectory}");

        var workingDirectory = Path.Combine(root, "work");
        var resultDirectory = Path.Combine(root, "result");
        Directory.CreateDirectory(workingDirectory);

        var templateCopyPath = Path.Combine(workingDirectory, "template.grb");
        await File.WriteAllTextAsync(templateCopyPath, "template");

        var client = new ExternalProcessTFlexAutomationClient(
            Options.Create(new TFlexAutomationOptions
            {
                CommandPath = command.FileName,
                Arguments = command.Arguments,
                TimeoutSeconds = 10
            }),
            new TFlexAutomationExecutionGate(),
            CreateReadyAutomationState(),
            NullLogger<ExternalProcessTFlexAutomationClient>.Instance);

        const string address = "C:\\Temp\\lift \"quoted\"\r\nHIDDEN_VAR = 999;\r\n// корпус 1";
        var files = await client.GenerateAsync(
            new TFlexGenerationRequest(
                new DrawingJob { Id = "job-1" },
                new DrawingTemplate { Id = "template", Code = "template", Name = "Template" },
                workingDirectory,
                templateCopyPath,
                resultDirectory,
                new Dictionary<string, object?>
                {
                    ["WIDTH"] = 1000,
                    ["$Address"] = address
                },
                "pdf"));

        Assert.Single(files);
        Assert.Equal("result.pdf", files[0].FileName);
        Assert.Equal("pdf", files[0].Format);
        Assert.True(File.Exists(Path.Combine(workingDirectory, "tflex-automation-request.json")));
        var parameterFilePath = Path.Combine(workingDirectory, "parameters.par");
        Assert.True(File.Exists(parameterFilePath));

        var parameterLines = await File.ReadAllLinesAsync(parameterFilePath);
        Assert.Equal(3, parameterLines.Length);
        var parsedParameters = TFlexAutomationRunner.ParameterFileParser.Read(parameterFilePath);
        Assert.Equal(address, parsedParameters["$Address"]);
        Assert.False(parsedParameters.ContainsKey("HIDDEN_VAR"));

        using var requestJson = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(workingDirectory, "tflex-automation-request.json")));
        Assert.Equal(
            address,
            requestJson.RootElement.GetProperty("parameters").GetProperty("$Address").GetString());
    }

    [Fact]
    public async Task GenerateAsync_RejectsResponseFileOutsideResultDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);

        var command = await CreateTestCommandAsync(
            root,
            "outside-result",
            """
            #!/bin/sh
            set -eu
            mkdir -p "$1"
            printf '%s' 'outside pdf' > "$(dirname "$1")/outside.pdf"
            printf '%s' '{"files":[{"path":"../outside.pdf","fileName":"outside.pdf","format":"pdf"}]}' > "$2"
            """,
            """
            param([string] $ResultDirectory, [string] $ResponsePath)
            $ErrorActionPreference = 'Stop'
            New-Item -ItemType Directory -Force -Path $ResultDirectory | Out-Null
            $outsidePath = Join-Path (Split-Path -Parent $ResultDirectory) 'outside.pdf'
            [IO.File]::WriteAllText($outsidePath, 'outside pdf')
            [IO.File]::WriteAllText($ResponsePath, '{"files":[{"path":"../outside.pdf","fileName":"outside.pdf","format":"pdf"}]}')
            """,
            "{resultDirectory}",
            "{responsePath}");

        var workingDirectory = Path.Combine(root, "work");
        var resultDirectory = Path.Combine(root, "result");
        Directory.CreateDirectory(workingDirectory);

        var templateCopyPath = Path.Combine(workingDirectory, "template.grb");
        await File.WriteAllTextAsync(templateCopyPath, "template");

        var client = new ExternalProcessTFlexAutomationClient(
            Options.Create(new TFlexAutomationOptions
            {
                CommandPath = command.FileName,
                Arguments = command.Arguments,
                TimeoutSeconds = 10
            }),
            new TFlexAutomationExecutionGate(),
            CreateReadyAutomationState(),
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

    [Fact]
    public async Task GenerateAsync_TimeoutTerminatesChildProcessTree()
    {
        await AssertProcessTreeTerminatedAsync(cancelRequest: false);
    }

    [Fact]
    public async Task GenerateAsync_CancellationTerminatesChildProcessTree()
    {
        await AssertProcessTreeTerminatedAsync(cancelRequest: true);
    }

    private static TFlexAutomationReadinessState CreateReadyAutomationState()
    {
        var state = new TFlexAutomationReadinessState();
        state.Update(
            TFlexAutomationHealthResult.Pass("ready"),
            DateTimeOffset.UtcNow,
            TimeSpan.FromHours(1));
        return state;
    }

    private static async Task AssertProcessTreeTerminatedAsync(bool cancelRequest)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);

        try
        {
            var childPidPath = Path.Combine(root, "child.pid");
            var command = await CreateTestCommandAsync(
                root,
                "long-running-tflex",
                """
                #!/bin/sh
                set -eu
                sleep 60 &
                child_pid=$!
                printf '%s' "$child_pid" > "$1"
                wait "$child_pid"
                """,
                """
                param([string] $ChildPidPath)
                $ErrorActionPreference = 'Stop'
                $child = Start-Process `
                    -FilePath $env:ComSpec `
                    -ArgumentList '/d /c ping -n 61 127.0.0.1 ^> nul' `
                    -PassThru `
                    -WindowStyle Hidden
                [IO.File]::WriteAllText($ChildPidPath, $child.Id.ToString([Globalization.CultureInfo]::InvariantCulture))
                Wait-Process -Id $child.Id
                """,
                childPidPath);

            var workingDirectory = Path.Combine(root, "work");
            var resultDirectory = Path.Combine(root, "result");
            Directory.CreateDirectory(workingDirectory);

            var templateCopyPath = Path.Combine(workingDirectory, "template.grb");
            await File.WriteAllTextAsync(templateCopyPath, "template");

            var client = new ExternalProcessTFlexAutomationClient(
                Options.Create(new TFlexAutomationOptions
                {
                    CommandPath = command.FileName,
                    Arguments = command.Arguments,
                    TimeoutSeconds = cancelRequest ? 10 : (OperatingSystem.IsWindows() ? 3 : 1)
                }),
                new TFlexAutomationExecutionGate(),
                CreateReadyAutomationState(),
                NullLogger<ExternalProcessTFlexAutomationClient>.Instance);

            var request = new TFlexGenerationRequest(
                new DrawingJob { Id = cancelRequest ? "job-cancel" : "job-timeout" },
                new DrawingTemplate { Id = "template", Code = "template", Name = "Template" },
                workingDirectory,
                templateCopyPath,
                resultDirectory,
                new Dictionary<string, object?>(),
                "pdf");

            if (cancelRequest)
            {
                using var cancellation = new CancellationTokenSource();
                var generationTask = client.GenerateAsync(request, cancellation.Token);
                await WaitUntilAsync(() => File.Exists(childPidPath), TimeSpan.FromSeconds(5));
                Assert.True(File.Exists(childPidPath));
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generationTask);
            }
            else
            {
                await Assert.ThrowsAsync<TimeoutException>(() => client.GenerateAsync(request));
            }

            Assert.True(File.Exists(childPidPath));
            var childPid = int.Parse(await File.ReadAllTextAsync(childPidPath));
            await WaitUntilAsync(() => !IsProcessRunning(childPid), TimeSpan.FromSeconds(5));
            Assert.False(IsProcessRunning(childPid));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<TestCommand> CreateTestCommandAsync(
        string root,
        string name,
        string unixScript,
        string windowsScript,
        params string[] arguments)
    {
        string executablePath;
        string scriptPath;
        string commandArguments;

        if (OperatingSystem.IsWindows())
        {
            executablePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32",
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            Assert.True(File.Exists(executablePath), $"Windows PowerShell was not found at '{executablePath}'.");

            scriptPath = Path.Combine(root, name + ".ps1");
            await File.WriteAllTextAsync(scriptPath, windowsScript);
            commandArguments = string.Join(
                " ",
                new[]
                {
                    "-NoLogo",
                    "-NoProfile",
                    "-NonInteractive",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-File",
                    QuoteCommandArgument(scriptPath)
                }.Concat(arguments.Select(QuoteCommandArgument)));
        }
        else
        {
            executablePath = "/bin/sh";
            Assert.True(File.Exists(executablePath), "The test requires the standard /bin/sh executable.");

            scriptPath = Path.Combine(root, name + ".sh");
            await File.WriteAllTextAsync(scriptPath, unixScript);
            commandArguments = string.Join(
                " ",
                new[] { QuoteCommandArgument(scriptPath) }
                    .Concat(arguments.Select(QuoteCommandArgument)));
        }

        return new TestCommand(executablePath, commandArguments);
    }

    private static string QuoteCommandArgument(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(50);
        }
    }

    private sealed record TestCommand(string FileName, string Arguments);
}
