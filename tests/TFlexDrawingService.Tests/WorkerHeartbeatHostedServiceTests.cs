using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Infrastructure.Automation;
using TFlexDrawingService.Infrastructure.Configuration;
using TFlexDrawingService.Infrastructure.Services;

namespace TFlexDrawingService.Tests;

public sealed class WorkerHeartbeatHostedServiceTests
{
    [Fact]
    public async Task ServicePublishesAndRemovesPerProcessHeartbeat()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        var service = new WorkerHeartbeatHostedService(
            Options.Create(new DrawingStorageOptions
            {
                RootPath = root,
                DatabasePath = Path.Combine(root, "drawings.db")
            }),
            Options.Create(new TFlexAutomationOptions { Mode = "Mock" }),
            new StubAutomationHealthProbe(
                TFlexAutomationHealthResult.Pass("ready")),
            new TFlexAutomationReadinessState(),
            NullLogger<WorkerHeartbeatHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        var heartbeatDirectory = Path.Combine(root, "worker-heartbeats");
        var heartbeatPath = await WaitForHeartbeatAsync(
            heartbeatDirectory,
            document => document.RootElement.GetProperty("ready").GetBoolean());

        using (var document = JsonDocument.Parse(await File.ReadAllTextAsync(heartbeatPath)))
        {
            Assert.True(document.RootElement.GetProperty("ready").GetBoolean());
            Assert.Equal(
                Environment.ProcessId,
                document.RootElement.GetProperty("processId").GetInt32());
            Assert.Equal(
                "Mock",
                document.RootElement.GetProperty("automationMode").GetString());
            Assert.Equal(
                JsonValueKind.String,
                document.RootElement.GetProperty("automationCheckedAt").ValueKind);
            Assert.Equal(
                JsonValueKind.String,
                document.RootElement.GetProperty("automationHealthValidUntil").ValueKind);
        }

        await service.StopAsync(CancellationToken.None);

        Assert.False(File.Exists(heartbeatPath));
    }

    [Fact]
    public async Task ServiceDoesNotReportUnsupportedAutomationModeAsReady()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        var commandPath = Path.Combine(root, "runner.exe");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(commandPath, "not a runner");
        var service = new WorkerHeartbeatHostedService(
            Options.Create(new DrawingStorageOptions
            {
                RootPath = root,
                DatabasePath = Path.Combine(root, "drawings.db")
            }),
            Options.Create(new TFlexAutomationOptions
            {
                Mode = "Unsupported",
                CommandPath = commandPath
            }),
            new StubAutomationHealthProbe(
                TFlexAutomationHealthResult.Fail("unsupported")),
            new TFlexAutomationReadinessState(),
            NullLogger<WorkerHeartbeatHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        var heartbeatPath = await WaitForHeartbeatAsync(
            Path.Combine(root, "worker-heartbeats"),
            document =>
                document.RootElement.GetProperty("automationCheckedAt").ValueKind
                == JsonValueKind.String);

        using (var document = JsonDocument.Parse(await File.ReadAllTextAsync(heartbeatPath)))
        {
            Assert.False(document.RootElement.GetProperty("ready").GetBoolean());
        }

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task AutomationHealthProbeAcceptsMockAndRejectsMissingExternalRunner()
    {
        var gate = new TFlexAutomationExecutionGate();
        var mockProbe = new TFlexAutomationHealthProbe(
            Options.Create(new TFlexAutomationOptions { Mode = "Mock" }),
            gate,
            new TFlexAutomationReadinessState(),
            NullLogger<TFlexAutomationHealthProbe>.Instance);
        var missingRunnerProbe = new TFlexAutomationHealthProbe(
            Options.Create(new TFlexAutomationOptions
            {
                Mode = "ExternalProcess",
                CommandPath = Path.Combine(
                    Path.GetTempPath(),
                    Guid.NewGuid().ToString("n"),
                    "missing-runner.exe")
            }),
            gate,
            new TFlexAutomationReadinessState(),
            NullLogger<TFlexAutomationHealthProbe>.Instance);

        Assert.True((await mockProbe.CheckAsync()).Ready);
        Assert.False((await missingRunnerProbe.CheckAsync()).Ready);
    }

    [Fact]
    public async Task StartupHealthGateBlocksFailureAndPublishesSuccess()
    {
        var options = Options.Create(new TFlexAutomationOptions
        {
            Mode = "Mock",
            HealthCheckEnabled = true
        });
        var environment = new TestHostEnvironment
        {
            EnvironmentName = Environments.Development
        };

        var failedState = new TFlexAutomationReadinessState();
        var failedService = new TFlexAutomationStartupHostedService(
            options,
            environment,
            new StubAutomationHealthProbe(
                TFlexAutomationHealthResult.Fail("failed")),
            failedState,
            NullLogger<TFlexAutomationStartupHostedService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => failedService.StartAsync(CancellationToken.None));
        Assert.False(failedState.GetSnapshot().Ready);

        var readyState = new TFlexAutomationReadinessState();
        var readyService = new TFlexAutomationStartupHostedService(
            options,
            environment,
            new StubAutomationHealthProbe(
                TFlexAutomationHealthResult.Pass("ready")),
            readyState,
            NullLogger<TFlexAutomationStartupHostedService>.Instance);

        await readyService.StartAsync(CancellationToken.None);
        Assert.True(readyState.GetSnapshot().Ready);
    }

    [Fact]
    public async Task DisabledStartupHealthCheckLeavesWorkerPausedWithoutCallingProbe()
    {
        var probe = new StubAutomationHealthProbe(
            TFlexAutomationHealthResult.Pass("must not run"));
        var state = new TFlexAutomationReadinessState();
        var service = new TFlexAutomationStartupHostedService(
            Options.Create(new TFlexAutomationOptions
            {
                Mode = "Mock",
                HealthCheckEnabled = false
            }),
            new TestHostEnvironment
            {
                EnvironmentName = Environments.Development
            },
            probe,
            state,
            NullLogger<TFlexAutomationStartupHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(0, probe.CallCount);
        Assert.False(state.GetSnapshot().Ready);
    }

    [Fact]
    public async Task ProductionWorkerRejectsMockAutomation()
    {
        var service = new TFlexAutomationStartupHostedService(
            Options.Create(new TFlexAutomationOptions { Mode = "Mock" }),
            new TestHostEnvironment
            {
                EnvironmentName = Environments.Production
            },
            new StubAutomationHealthProbe(
                TFlexAutomationHealthResult.Pass("ready")),
            new TFlexAutomationReadinessState(),
            NullLogger<TFlexAutomationStartupHostedService>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StartAsync(CancellationToken.None));

        Assert.Contains("Development", exception.Message);
    }

    private static async Task<string> WaitForHeartbeatAsync(
        string directory,
        Func<JsonDocument, bool> predicate)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var path = Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "worker-*.json").SingleOrDefault()
                : null;
            if (path is not null)
            {
                try
                {
                    using var document = JsonDocument.Parse(
                        await File.ReadAllTextAsync(path));
                    if (predicate(document))
                    {
                        return path;
                    }
                }
                catch (IOException)
                {
                    // The heartbeat is replaced atomically; retry.
                }
                catch (JsonException)
                {
                    // The heartbeat is replaced atomically; retry.
                }
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("Worker heartbeat was not published.");
    }

    private sealed class StubAutomationHealthProbe(
        TFlexAutomationHealthResult result) : ITFlexAutomationHealthProbe
    {
        public int CallCount { get; private set; }

        public Task<TFlexAutomationHealthResult> CheckAsync(
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "TFlexDrawingService.Tests";

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
