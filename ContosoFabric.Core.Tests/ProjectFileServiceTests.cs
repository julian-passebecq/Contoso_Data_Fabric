using ContosoFabric.Core.Models;
using ContosoFabric.Core.Projects;

namespace ContosoFabric.Core.Tests;

public sealed class ProjectFileServiceTests
{
    [Fact]
    public async Task Project_round_trips_with_string_enums_and_exact_generation_parameters()
    {
        var project = new FabricProject(
            Name: "round-trip",
            Scenario: BusinessScenario.SalesBi,
            Scale: DataScale.Medium,
            Years: 5,
            RawFormat: RawFormat.Delta,
            StopAfter: PipelineStage.Gold,
            Workspace: new FabricWorkspaceTarget("Fabric Demo", "11111111-1111-1111-1111-111111111111"),
            BronzeLakehouse: "Bronze_Test",
            SilverLakehouse: "Silver_Test",
            GoldLakehouse: "Gold_Test",
            RequestedSeed: 0,
            OrdersOverride: 765_432,
            StartDate: new DateTime(2018, 4, 3),
            StartFrom: PipelineStage.Silver);

        var folder = Path.Combine(Path.GetTempPath(), "contoso-fabric-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "project.fabric.json");

        try
        {
            await ProjectFileService.SaveAsync(project, path);
            var json = await File.ReadAllTextAsync(path);
            var loaded = await ProjectFileService.LoadAsync(path);

            Assert.Contains("\"scenario\": \"salesBi\"", json, StringComparison.Ordinal);
            Assert.Contains("\"startFrom\": \"silver\"", json, StringComparison.Ordinal);
            Assert.Contains("\"stopAfter\": \"gold\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("effectiveStartDate", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("includesBronze", json, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(project, loaded);
            Assert.Equal(new DateTime(2018, 4, 3), loaded.EffectiveStartDate);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Invalid_project_is_rejected_before_save()
    {
        var project = new FabricProject(
            Name: "invalid",
            Scenario: BusinessScenario.SalesBi,
            Scale: DataScale.Small,
            Years: 0,
            RawFormat: RawFormat.Parquet,
            StopAfter: PipelineStage.Generate,
            Workspace: new FabricWorkspaceTarget());

        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.fabric.json");
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ProjectFileService.SaveAsync(project, path));
    }
}
