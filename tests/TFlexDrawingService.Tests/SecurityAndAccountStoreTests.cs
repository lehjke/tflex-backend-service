using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TFlexDrawingService.Api.Data;
using TFlexDrawingService.Api.Security;
using TFlexDrawingService.Infrastructure.Configuration;

namespace TFlexDrawingService.Tests;

public sealed class SecurityAndAccountStoreTests
{
    [Fact]
    public async Task PendingUser_CannotValidateCredentialsUntilApproved()
    {
        var storageOptions = CreateStorageOptions();
        var store = new ConfiguredUserStore(
            storageOptions,
            new StaticOptionsMonitor<SecurityOptions>(new SecurityOptions()),
            NullLogger<ConfiguredUserStore>.Instance);

        await store.InitializeAsync();
        var result = await store.RegisterPendingUserAsync("operator", "Operator", "password");

        Assert.True(result.Created);
        Assert.Null(await store.ValidateCredentialsAsync("operator", "password"));

        var pendingUser = await store.FindUserAsync("operator");
        Assert.NotNull(pendingUser);
        Assert.False(pendingUser.Enabled);
        Assert.Equal("Pending", pendingUser.ApprovalStatus);

        Assert.True(await store.ApproveUserAsync("operator", "admin", ["Operator", "Viewer"]));

        var approvedUser = await store.ValidateCredentialsAsync("operator", "password");
        Assert.NotNull(approvedUser);
        Assert.True(approvedUser.Enabled);
        Assert.Equal("Approved", approvedUser.ApprovalStatus);
        Assert.Contains("Operator", approvedUser.Roles);
    }

    [Fact]
    public async Task ProjectStore_PersistsProjectsAndConfigurations()
    {
        var storageOptions = CreateStorageOptions();
        var store = new ProjectStore(storageOptions);
        await store.InitializeAsync();

        var project = await store.CreateProjectAsync("operator", "Lift A", null);
        var parameters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """{"WIDTH":1200,"MATERIAL":"Сталь"}""")!;

        var configuration = await store.SaveConfigurationAsync(
            "operator",
            project.Id,
            "PDF configuration",
            "template-a",
            "pdf",
            parameters);

        Assert.NotNull(configuration);

        var freshStore = new ProjectStore(storageOptions);
        await freshStore.InitializeAsync();

        var projects = await freshStore.ListProjectsAsync("operator");
        var configurations = await freshStore.ListConfigurationsAsync(project.Id, "operator");

        Assert.Single(projects);
        Assert.Equal("Lift A", projects[0].Name);
        Assert.Single(configurations);
        Assert.Equal("PDF configuration", configurations[0].Name);
        Assert.Contains("\"WIDTH\":1200", configurations[0].ParametersJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TemplateAccessStore_DefaultsEnabledAndPersistsDisabledState()
    {
        var storageOptions = CreateStorageOptions();
        var store = new TemplateAccessStore(storageOptions);
        await store.InitializeAsync();

        Assert.True(await store.IsEnabledAsync("template-a"));

        await store.SetEnabledAsync("template-a", false, "admin");

        var freshStore = new TemplateAccessStore(storageOptions);
        await freshStore.InitializeAsync();

        Assert.False(await freshStore.IsEnabledAsync("template-a"));
    }

    private static IOptions<DrawingStorageOptions> CreateStorageOptions()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        return Options.Create(new DrawingStorageOptions
        {
            RootPath = Path.Combine(root, "storage"),
            DatabasePath = Path.Combine(root, "storage", "drawings.db")
        });
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name)
        {
            return value;
        }

        public IDisposable? OnChange(Action<T, string?> listener)
        {
            return null;
        }
    }
}
