using ContosoFabric.Core.Generation;
using ContosoFabric.Core.Models;
using DatabaseGenerator;

namespace ContosoFabric.Core.Tests;

public sealed class GeneratorLifetimeTests
{
    [Fact]
    public async Task Closed_generator_log_can_be_read_for_manifest_and_reused()
    {
        var folder = Path.Combine(Path.GetTempPath(), "contoso-logger-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var project = new FabricProject("logger-test", BusinessScenario.SalesBi, DataScale.Tiny,
                1, RawFormat.Parquet, PipelineStage.Generate, new FabricWorkspaceTarget());
            for (var run = 1; run <= 2; run++)
            {
                Logger.Init(Path.Combine(folder, "_log.log"));
                Logger.Info($"Orders: {run * 100}");
                Logger.Info($"OrdersRows: {run * 300}");
                Logger.Close();
                var manifest = await GenerationManifest.FromGeneratorLogAsync(project, folder);
                Assert.Equal(run * 100, manifest.ActualOrders);
                Assert.Equal(run * 300, manifest.ActualOrderRows);
                await manifest.SaveAsync(folder);
            }
        }
        finally
        {
            Logger.Close();
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task Engine_failure_releases_log_for_next_run()
    {
        var folder = Path.Combine(Path.GetTempPath(), "contoso-logger-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var workbook = Path.Combine(folder, "invalid.xlsx");
        await File.WriteAllTextAsync(workbook, "invalid workbook");
        try
        {
            // A locked previous output fails in preparation, before downloads.
            using var lockedOutput = new FileStream(Path.Combine(folder, "orders.csv"),
                FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            var engine = new Engine(workbook, folder, folder, new Config
            {
                StartDT = new DateTime(2014, 1, 1), YearsCount = 1, OrdersCount = 10_000
            });
            await Assert.ThrowsAnyAsync<Exception>(() => engine.Exec());
            var log = await File.ReadAllTextAsync(Path.Combine(folder, "_log.log"));
            Assert.Contains("EXCEPTION:", log);
            using var exclusive = new FileStream(Path.Combine(folder, "_log.log"),
                FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            Logger.Close();
            Directory.Delete(folder, true);
        }
    }
}
