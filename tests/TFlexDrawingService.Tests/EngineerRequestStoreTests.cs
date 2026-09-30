using Microsoft.Extensions.Options;
using TFlexDrawingService.Api.Data;
using TFlexDrawingService.Infrastructure.Configuration;

namespace TFlexDrawingService.Tests;

public sealed class EngineerRequestStoreTests
{
    [Fact]
    public async Task EngineerMutationsRequireCurrentAssigneeUnlessAdmin()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var options = Options.Create(new DrawingStorageOptions { RootPath = root, DatabasePath = Path.Combine(root, "drawings.db") });
        var projects = new ProjectStore(options);
        await projects.InitializeAsync();
        var project = await projects.CreateProjectAsync("seller", "Lift", null, null);
        var store = new EngineerRequestStore(options);
        await store.InitializeAsync();
        var request = await store.CreateAsync(project.Id, "seller", project.Name, "Need drawing", "tpl", "pdf", "{}");
        Assert.True(await store.ClaimAsync(request.Id, "former-engineer"));
        Assert.True(await store.ReassignAsync(request.Id, "admin", "current-engineer"));

        Assert.False(await store.UpdateEngineerParametersAsync(request.Id, "former-engineer", "{\"width\":2}"));
        Assert.False(await store.ClarifyAsync(request.Id, "former-engineer", "stale"));
        Assert.False(await store.RejectAsync(request.Id, "former-engineer", "stale"));
        Assert.False(await store.ReserveJobAsync(request.Id, "former-engineer", "stale-job"));
        Assert.True(await store.UpdateEngineerParametersAsync(request.Id, "admin", "{\"width\":2}", isAdmin: true));
        Assert.True(await store.ClarifyAsync(request.Id, "admin", "clarify", isAdmin: true));
        Assert.True(await store.ResumeAsync(request.Id, "seller"));
        Assert.True(await store.ReserveJobAsync(request.Id, "admin", "job", isAdmin: true));
        Assert.False(await store.ClearLinkedJobAsync(request.Id, "former-engineer", "job"));
        Assert.True(await store.ClearLinkedJobAsync(request.Id, "admin", "job", isAdmin: true));
        Assert.True(await store.ReserveJobAsync(request.Id, "admin", "job", isAdmin: true));
        Assert.True(await store.ClarifyAsync(request.Id, "admin", "more details", isAdmin: true));
        Assert.True(await store.ClearLinkedJobAsync(request.Id, "admin", "job", isAdmin: true));
        Assert.True(await store.ResumeAsync(request.Id, "seller"));
        Assert.True(await store.ReserveJobAsync(request.Id, "admin", "job", isAdmin: true));
        Assert.False(await store.PublishResultAsync(request.Id, "former-engineer", "job", "{}"));
        Assert.True(await store.PublishResultAsync(request.Id, "admin", "job", "{}", isAdmin: true));
    }

    [Fact]
    public async Task MutationsAreConditionalAndHistoryAndReadMarksPersist()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var options = Options.Create(new DrawingStorageOptions { RootPath = root, DatabasePath = Path.Combine(root, "drawings.db") });
        var projects = new ProjectStore(options);
        await projects.InitializeAsync();
        var project = await projects.CreateProjectAsync("seller", "Lift", null, null);
        var store = new EngineerRequestStore(options);
        await store.InitializeAsync();
        var request = await store.CreateAsync(project.Id, "seller", project.Name, "Need drawing", "tpl", "pdf", "{\"width\":10}");

        Assert.True(await store.ClaimAsync(request.Id, "engineer"));
        Assert.False(await store.ClaimAsync(request.Id, "another"));
        Assert.True(await store.UpdateEngineerParametersAsync(request.Id, "engineer", "{\"width\":12}"));
        Assert.True(await store.ClarifyAsync(request.Id, "engineer", "Please use 12 mm"));
        Assert.True(await store.ResumeAsync(request.Id, "seller"));
        Assert.True(await store.ReserveJobAsync(request.Id, "engineer", "failed-job", "old-confirmation"));
        Assert.False(await store.ReserveJobAsync(request.Id, "engineer", "duplicate-job"));
        Assert.Equal("old-confirmation", (await store.GetAsync(request.Id))!.ConfirmationFingerprint);
        Assert.True(await store.ClearLinkedJobAsync(request.Id, "engineer", "failed-job"));
        Assert.Null((await store.GetAsync(request.Id))!.ConfirmationFingerprint);
        Assert.True(await store.UpdateEngineerParametersAsync(request.Id, "engineer", "{\"width\":13}"));
        Assert.True(await store.ReserveJobAsync(request.Id, "engineer", "job-1", "current-confirmation"));
        Assert.False(await store.PublishResultAsync(request.Id, "engineer", "failed-job", "{}"));
        Assert.True(await store.PublishResultAsync(request.Id, "engineer", "job-1", "{\"file\":\"result.pdf\"}"));
        Assert.False(await store.PublishResultAsync(request.Id, "engineer", "job-1", "{}"));
        Assert.False(await store.AddCommentAsync(request.Id, "seller", "Thanks"));
        await store.MarkReadAsync(request.Id, "seller");

        var stored = await store.GetByJobIdAsync("job-1");
        Assert.Equal("{\"width\":13}", stored!.EngineerParametersJson);
        Assert.Equal(EngineerRequestStatus.Ready, stored.Status);
        Assert.Equal("current-confirmation", stored.ConfirmationFingerprint);
        Assert.NotNull(stored.IssuedAt);
        Assert.True(await store.IsReadAsync(request.Id, "seller"));
        Assert.Equal(new[] { "Created", "Claimed", "ParametersUpdated", "StatusChanged", "Resumed", "JobReserved", "JobReservationCleared", "ParametersUpdated", "JobReserved", "ResultIssued" },
            (await store.GetEventsAsync(request.Id)).Select(e => e.Kind));
        Assert.Single(await store.GetCommentsAsync(request.Id));
    }
}
