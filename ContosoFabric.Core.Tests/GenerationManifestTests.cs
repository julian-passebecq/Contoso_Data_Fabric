using System.Text.Json;
using ContosoFabric.Core.Generation;
using ContosoFabric.Core.Models;

namespace ContosoFabric.Core.Tests;

public sealed class GenerationManifestTests
{
    [Fact]
    public async Task Generator_log_is_converted_to_structured_truth_manifest_without_confusing_online_orders()
    {
        var folder = Path.Combine(Path.GetTempPath(), "contoso-manifest-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(folder, "_log.log"),
                "20260907 00:00:00 | 0.00 > Orders:            12,345\n" +
                "20260907 00:00:00 | 0.00 > Online orders:     4,321\n" +
                "20260907 00:00:00 | 0.00 > OrdersRows:        34,567\n" +
                "20260907 00:00:00 | 0.00 > THE END\n");

            var project = new FabricProject(
                Name: "manifest-test",
                Scenario: BusinessScenario.SalesBi,
                Scale: DataScale.Tiny,
                Years: 2,
                RawFormat: RawFormat.Parquet,
                StopAfter: PipelineStage.Bronze,
                Workspace: new FabricWorkspaceTarget(),
                OrdersOverride: 12_345,
                StartDate: new DateTime(2020, 1, 1));

            var manifest = await GenerationManifest.FromGeneratorLogAsync(project, folder);
            var path = await manifest.SaveAsync(folder);

            Assert.Equal(12_345, manifest.RequestedOrders);
            Assert.Equal(12_345, manifest.ActualOrders);
            Assert.NotEqual(4_321, manifest.ActualOrders);
            Assert.Equal(34_567, manifest.ActualOrderRows);
            Assert.Equal(0, manifest.EffectiveSeed);
            Assert.Equal(RawFormat.Parquet, manifest.RawFormat);
            Assert.Contains("orders", manifest.ExpectedTables);
            Assert.Contains("orderrows", manifest.ExpectedTables);

            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert.Equal("1.0", json.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal("parquet", json.RootElement.GetProperty("rawFormat").GetString());
            Assert.Equal(12_345, json.RootElement.GetProperty("actualOrders").GetInt64());
            Assert.Equal(34_567, json.RootElement.GetProperty("actualOrderRows").GetInt64());
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Manifest_creation_fails_when_generator_final_counters_are_missing()
    {
        var folder = Path.Combine(Path.GetTempPath(), "contoso-manifest-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "_log.log"), "20260907 00:00:00 | 0.00 > THE END\n");
            var project = new FabricProject(
                Name: "manifest-test",
                Scenario: BusinessScenario.SalesBi,
                Scale: DataScale.Tiny,
                Years: 1,
                RawFormat: RawFormat.Csv,
                StopAfter: PipelineStage.Generate,
                Workspace: new FabricWorkspaceTarget());

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                GenerationManifest.FromGeneratorLogAsync(project, folder));
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }
}
