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
    public void ProductionSecurityOptions_FailClosed()
    {
        var authenticationDisabled = new SecurityOptions
        {
            RequireAuthentication = false
        };
        var csrfDisabled = new SecurityOptions
        {
            RequireCsrfHeader = false
        };
        var sensitiveDetailsExposed = new SecurityOptions
        {
            RedactJobErrors = false,
            ExposeWorkingDirectory = true
        };

        Assert.Throws<InvalidOperationException>(
            () => SecurityOptionsValidation.Validate(authenticationDisabled, isDevelopment: false));
        Assert.Throws<InvalidOperationException>(
            () => SecurityOptionsValidation.Validate(csrfDisabled, isDevelopment: false));
        Assert.Throws<InvalidOperationException>(
            () => SecurityOptionsValidation.Validate(sensitiveDetailsExposed, isDevelopment: false));

        SecurityOptionsValidation.Validate(authenticationDisabled, isDevelopment: true);
        SecurityOptionsValidation.Validate(csrfDisabled, isDevelopment: true);
    }

    [Fact]
    public void SecurityOptions_RejectUnsafeResourceAndSessionRanges()
    {
        var oversizedBody = new SecurityOptions
        {
            MaxRequestBodyBytes = SecurityOptionsValidation.MaximumRequestBodyBytes + 1
        };
        var unboundedLoginRate = new SecurityOptions
        {
            LoginRateLimitPermitLimit = SecurityOptionsValidation.MaximumRateLimitPermits + 1
        };
        var shortLoginWindow = new SecurityOptions
        {
            LoginRateLimitWindowSeconds = 1
        };
        var excessiveSession = new SecurityOptions
        {
            SessionMinutes = SecurityOptionsValidation.MaximumSessionMinutes + 1
        };

        Assert.Throws<InvalidOperationException>(
            () => SecurityOptionsValidation.Validate(oversizedBody, isDevelopment: false));
        Assert.Throws<InvalidOperationException>(
            () => SecurityOptionsValidation.Validate(unboundedLoginRate, isDevelopment: false));
        Assert.Throws<InvalidOperationException>(
            () => SecurityOptionsValidation.Validate(shortLoginWindow, isDevelopment: false));
        Assert.Throws<InvalidOperationException>(
            () => SecurityOptionsValidation.Validate(excessiveSession, isDevelopment: false));
    }

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
    public async Task DeletedUser_RemainsReservedAndCannotAccessPreviousAccount()
    {
        var storageOptions = CreateStorageOptions();
        var store = new ConfiguredUserStore(
            storageOptions,
            new StaticOptionsMonitor<SecurityOptions>(new SecurityOptions()),
            NullLogger<ConfiguredUserStore>.Instance);

        await store.InitializeAsync();
        Assert.True((await store.RegisterPendingUserAsync("operator", "Original operator", "old-password")).Created);
        Assert.True(await store.ApproveUserAsync("operator", "admin", ["Operator", "Viewer"]));
        Assert.NotNull(await store.ValidateCredentialsAsync("operator", "old-password"));

        Assert.True(await store.DeleteUserAsync("operator"));
        Assert.Null(await store.FindUserAsync("operator"));
        Assert.Null(await store.ValidateCredentialsAsync("operator", "old-password"));
        Assert.Empty(await store.ListUsersAsync());

        var repeatedRegistration = await store.RegisterPendingUserAsync(
            "OPERATOR",
            "Another person",
            "new-password");

        Assert.False(repeatedRegistration.Created);
        Assert.Equal("Deleted", repeatedRegistration.Status);
        Assert.Null(await store.ValidateCredentialsAsync("operator", "new-password"));
        Assert.Empty(await store.ListUsersAsync());
    }

    [Fact]
    public async Task UserUpsert_CannotRemoveTheLastEnabledAdmin()
    {
        var storageOptions = CreateStorageOptions();
        var store = new ConfiguredUserStore(
            storageOptions,
            new StaticOptionsMonitor<SecurityOptions>(new SecurityOptions()),
            NullLogger<ConfiguredUserStore>.Instance);
        await store.InitializeAsync();

        var admin = CreateAdmin("admin");
        await store.UpsertUserAsync(admin);

        var result = await store.TryUpsertUserPreservingLastAdminAsync(Demote(admin));

        Assert.Equal(ConfiguredUserUpsertResult.LastEnabledAdminWouldBeRemoved, result);
        var persistedAdmin = await store.FindUserAsync(admin.UserName);
        Assert.NotNull(persistedAdmin);
        Assert.True(persistedAdmin.Enabled);
        Assert.Contains("Admin", persistedAdmin.Roles);
    }

    [Fact]
    public async Task ConcurrentUserUpserts_AlwaysPreserveOneEnabledAdmin()
    {
        var storageOptions = CreateStorageOptions();
        var store = new ConfiguredUserStore(
            storageOptions,
            new StaticOptionsMonitor<SecurityOptions>(new SecurityOptions()),
            NullLogger<ConfiguredUserStore>.Instance);
        await store.InitializeAsync();

        var firstAdmin = CreateAdmin("admin-a");
        var secondAdmin = CreateAdmin("admin-b");
        await store.UpsertUserAsync(firstAdmin);
        await store.UpsertUserAsync(secondAdmin);

        var results = await Task.WhenAll(
            store.TryUpsertUserPreservingLastAdminAsync(Demote(firstAdmin)),
            store.TryUpsertUserPreservingLastAdminAsync(Demote(secondAdmin)));

        Assert.Single(results, result => result == ConfiguredUserUpsertResult.Succeeded);
        Assert.Single(
            results,
            result => result == ConfiguredUserUpsertResult.LastEnabledAdminWouldBeRemoved);

        var enabledAdmins = (await store.ListUsersAsync()).Where(user =>
            user.Enabled
            && string.Equals(user.ApprovalStatus, "Approved", StringComparison.OrdinalIgnoreCase)
            && user.Roles.Contains("Admin", StringComparer.OrdinalIgnoreCase));
        Assert.Single(enabledAdmins);
    }

    [Fact]
    public async Task ActiveAdmin_CannotBeRewrittenThroughApproveOrRejectActions()
    {
        var storageOptions = CreateStorageOptions();
        var store = new ConfiguredUserStore(
            storageOptions,
            new StaticOptionsMonitor<SecurityOptions>(new SecurityOptions()),
            NullLogger<ConfiguredUserStore>.Instance);
        await store.InitializeAsync();

        var admin = CreateAdmin("admin");
        await store.UpsertUserAsync(admin);

        Assert.False(await store.ApproveUserAsync(admin.UserName, "other-admin", ["Viewer"]));
        Assert.False(await store.RejectUserAsync(admin.UserName, "other-admin"));

        var persistedAdmin = await store.FindUserAsync(admin.UserName);
        Assert.NotNull(persistedAdmin);
        Assert.True(persistedAdmin.Enabled);
        Assert.Equal("Approved", persistedAdmin.ApprovalStatus);
        Assert.Contains("Admin", persistedAdmin.Roles);
    }

    [Fact]
    public async Task ActiveAdmin_CannotBeDeletedThroughStoreSink()
    {
        var storageOptions = CreateStorageOptions();
        var store = new ConfiguredUserStore(
            storageOptions,
            new StaticOptionsMonitor<SecurityOptions>(new SecurityOptions()),
            NullLogger<ConfiguredUserStore>.Instance);
        await store.InitializeAsync();

        var admin = CreateAdmin("admin");
        await store.UpsertUserAsync(admin);

        Assert.False(await store.DeleteUserAsync(admin.UserName));

        var persistedAdmin = await store.FindUserAsync(admin.UserName);
        Assert.NotNull(persistedAdmin);
        Assert.True(persistedAdmin.Enabled);
        Assert.Equal("Approved", persistedAdmin.ApprovalStatus);
        Assert.Contains("Admin", persistedAdmin.Roles);
    }

    [Fact]
    public async Task ProjectStore_PersistsProjectsAndConfigurations()
    {
        var storageOptions = CreateStorageOptions();
        var store = new ProjectStore(storageOptions);
        await store.InitializeAsync();

        var project = await store.CreateProjectAsync("operator", "Lift A", "г. Москва, ул. Мира 21", "REQ-001");
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
        Assert.Equal("г. Москва, ул. Мира 21", projects[0].Address);
        Assert.Equal("REQ-001", projects[0].FactoryRequestNumber);
        Assert.Single(configurations);
        Assert.Equal("PDF configuration", configurations[0].Name);
        Assert.Contains("\"WIDTH\":1200", configurations[0].ParametersJson, StringComparison.Ordinal);

        var updatedParameters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """{"WIDTH":1400,"MATERIAL":"Сталь"}""")!;
        var updatedConfiguration = await freshStore.UpdateConfigurationAsync(
            "operator",
            configuration.Id,
            "L1",
            "template-a",
            "pdf",
            updatedParameters);

        Assert.NotNull(updatedConfiguration);

        var updatedConfigurations = await freshStore.ListConfigurationsAsync(project.Id, "operator");
        Assert.Single(updatedConfigurations);
        Assert.Equal(configuration.Id, updatedConfigurations[0].Id);
        Assert.Equal("L1", updatedConfigurations[0].Name);
        Assert.Contains("\"WIDTH\":1400", updatedConfigurations[0].ParametersJson, StringComparison.Ordinal);

        var updatedProject = await freshStore.UpdateProjectAsync(
            project.Id,
            "operator",
            "Lift A updated",
            "г. Санкт-Петербург, Невский 10",
            "REQ-002");

        Assert.NotNull(updatedProject);
        Assert.Equal("Lift A updated", updatedProject.Name);
        Assert.Equal("г. Санкт-Петербург, Невский 10", updatedProject.Address);
        Assert.Equal("REQ-002", updatedProject.FactoryRequestNumber);
    }

    [Fact]
    public async Task ProjectStore_AdminScopeCanManageProjectsAndConfigurationsAcrossOwners()
    {
        var storageOptions = CreateStorageOptions();
        var store = new ProjectStore(storageOptions);
        await store.InitializeAsync();

        var operatorProject = await store.CreateProjectAsync("operator", "Operator project", "Operator address", "OP-001");
        var engineerProject = await store.CreateProjectAsync("engineer", "Engineer project", "Engineer address", "EN-001");
        var parameters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """{"WIDTH":1200}""")!;

        var operatorConfiguration = await store.SaveConfigurationAsync(
            "operator",
            operatorProject.Id,
            "Operator configuration",
            "template-a",
            "pdf",
            parameters);
        var engineerConfiguration = await store.SaveConfigurationAsync(
            "engineer",
            engineerProject.Id,
            "Engineer configuration",
            "template-b",
            "pdf",
            parameters);

        Assert.NotNull(operatorConfiguration);
        Assert.NotNull(engineerConfiguration);
        Assert.Null(await store.GetConfigurationAsync(operatorConfiguration.Id, "engineer"));

        var allProjects = await store.ListProjectsAsync(null);
        Assert.Equal(2, allProjects.Count);
        Assert.Contains(allProjects, project => project.OwnerUserName == "operator");
        Assert.Contains(allProjects, project => project.OwnerUserName == "engineer");

        var adminConfiguration = await store.GetConfigurationAsync(operatorConfiguration.Id, null);
        Assert.NotNull(adminConfiguration);
        Assert.Equal("operator", adminConfiguration.OwnerUserName);

        var updatedParameters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """{"WIDTH":1500}""")!;
        var updatedConfiguration = await store.UpdateConfigurationAsync(
            null,
            operatorConfiguration.Id,
            "Updated by admin",
            "template-a",
            "pdf",
            updatedParameters);

        Assert.NotNull(updatedConfiguration);
        Assert.Equal("operator", updatedConfiguration.OwnerUserName);

        var operatorConfigurations = await store.ListConfigurationsAsync(operatorProject.Id, "operator");
        Assert.Single(operatorConfigurations);
        Assert.Equal("Updated by admin", operatorConfigurations[0].Name);
        Assert.Contains("\"WIDTH\":1500", operatorConfigurations[0].ParametersJson, StringComparison.Ordinal);

        Assert.True(await store.DeleteConfigurationAsync(engineerConfiguration.Id, null));
        Assert.Empty(await store.ListConfigurationsAsync(engineerProject.Id, "engineer"));

        Assert.True(await store.DeleteProjectAsync(engineerProject.Id, null));
        Assert.DoesNotContain(await store.ListProjectsAsync(null), project => project.Id == engineerProject.Id);
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

    private static ConfiguredUser CreateAdmin(string userName)
    {
        return new ConfiguredUser
        {
            UserName = userName,
            DisplayName = userName,
            PasswordHash = PasswordHashing.HashPassword("password"),
            Enabled = true,
            ApprovalStatus = "Approved",
            Roles = ["Admin", "Operator", "Viewer"]
        };
    }

    private static ConfiguredUser Demote(ConfiguredUser user)
    {
        return new ConfiguredUser
        {
            UserName = user.UserName,
            DisplayName = user.DisplayName,
            PasswordHash = user.PasswordHash,
            Enabled = true,
            ApprovalStatus = "Approved",
            Roles = ["Operator", "Viewer"]
        };
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
