using Microsoft.Extensions.Options;
using TFlexDrawingService.Core.Models;
using TFlexDrawingService.Infrastructure.Configuration;
using TFlexDrawingService.Infrastructure.Persistence;

namespace TFlexDrawingService.Tests;

public sealed class TemplateAnalysisStoreTests
{
    [Fact]
    public async Task Store_ClaimsCompletesAndPersistsAnalysisJob()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tflex-analysis-{Guid.NewGuid():n}");
        Directory.CreateDirectory(root);
        try
        {
            var store = CreateStore(root);
            await store.InitializeAsync();
            var job = new TemplateAnalysisJob
            {
                OwnerUserName = "admin",
                OriginalTemplateFileName = "Lift.grb",
                TemplateFilePath = Path.Combine(root, "Lift.grb")
            };
            await store.CreateAsync(job);

            var claimed = await store.TryClaimNextAsync();
            Assert.NotNull(claimed);
            Assert.Equal(TemplateAnalysisStatus.Processing, claimed.Status);
            await store.CompleteAsync(job.Id, "{}", "{}", "[]", "[]");

            var completed = await store.GetAsync(job.Id);
            Assert.NotNull(completed);
            Assert.Equal(TemplateAnalysisStatus.Completed, completed.Status);
            Assert.NotNull(completed.FinishedAt);
            Assert.Equal("{}", completed.DraftJson);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Store_RecoversOnlyStaleProcessingJobs()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tflex-analysis-{Guid.NewGuid():n}");
        Directory.CreateDirectory(root);
        try
        {
            var store = CreateStore(root);
            await store.InitializeAsync();
            var job = new TemplateAnalysisJob
            {
                OriginalTemplateFileName = "Lift.grb",
                TemplateFilePath = Path.Combine(root, "Lift.grb")
            };
            await store.CreateAsync(job);
            Assert.NotNull(await store.TryClaimNextAsync());

            Assert.Equal(0, await store.RecoverInterruptedAsync(TimeSpan.FromHours(1)));
            Assert.Equal(1, await store.RecoverInterruptedAsync(TimeSpan.Zero));
            Assert.Equal(TemplateAnalysisStatus.Pending, (await store.GetAsync(job.Id))!.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Store_EnforcesActiveAnalysisLimitAtomically()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tflex-analysis-{Guid.NewGuid():n}");
        Directory.CreateDirectory(root);
        try
        {
            var store = CreateStore(root);
            await store.InitializeAsync();
            Assert.True(await store.TryCreateAsync(CreateJob(root, "First.grb"), 1));
            Assert.False(await store.TryCreateAsync(CreateJob(root, "Second.grb"), 1));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static TemplateAnalysisStore CreateStore(string root)
    {
        return new TemplateAnalysisStore(Options.Create(new DrawingStorageOptions
        {
            RootPath = root,
            DatabasePath = Path.Combine(root, "drawings.db")
        }));
    }

    private static TemplateAnalysisJob CreateJob(string root, string fileName)
    {
        return new TemplateAnalysisJob
        {
            OriginalTemplateFileName = fileName,
            TemplateFilePath = Path.Combine(root, fileName)
        };
    }
}
