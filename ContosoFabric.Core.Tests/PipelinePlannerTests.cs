using ContosoFabric.Core.Models;
using ContosoFabric.Core.Planning;

namespace ContosoFabric.Core.Tests;

public sealed class PipelinePlannerTests
{
    [Fact]
    public void Bronze_stop_contains_only_generate_and_bronze()
    {
        var plan = PipelinePlanner.Build(Project(stopAfter: PipelineStage.Bronze));

        Assert.Equal(
            new[] { PipelineStage.Generate, PipelineStage.Bronze },
            plan.Steps.Select(step => step.Stage).ToArray());
    }

    [Fact]
    public void Gold_stop_contains_all_native_execution_stages()
    {
        var plan = PipelinePlanner.Build(Project(stopAfter: PipelineStage.Gold));

        Assert.Equal(
            new[] { PipelineStage.Generate, PipelineStage.Bronze, PipelineStage.Silver, PipelineStage.Gold },
            plan.Steps.Select(step => step.Stage).ToArray());
        Assert.All(plan.Steps, step => Assert.True(step.Implemented));
    }

    [Fact]
    public void Custom_orders_override_scale_preset()
    {
        var plan = PipelinePlanner.Build(Project(scale: DataScale.Tiny, ordersOverride: 345_678));

        Assert.Equal(345_678, plan.OrdersCount);
        Assert.Contains(plan.Warnings, warning => warning.Contains("overrides", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(DataScale.Tiny, 10_000)]
    [InlineData(DataScale.Small, 100_000)]
    [InlineData(DataScale.Medium, 500_000)]
    [InlineData(DataScale.Large, 2_000_000)]
    public void Scale_presets_map_to_expected_order_counts(DataScale scale, int expected)
    {
        Assert.Equal(expected, PipelinePlanner.Build(Project(scale: scale)).OrdersCount);
    }

    [Fact]
    public void Invalid_custom_order_count_is_rejected()
    {
        var project = Project(ordersOverride: 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => PipelinePlanner.Build(project));
    }

    [Fact]
    public void Nonzero_seed_is_explicitly_warned_and_not_silently_applied()
    {
        var plan = PipelinePlanner.Build(Project(requestedSeed: 42));

        Assert.Equal(0, plan.EffectiveSeed);
        Assert.Contains(plan.Warnings, warning => warning.Contains("Random(0)", StringComparison.Ordinal));
    }

    private static FabricProject Project(
        PipelineStage stopAfter = PipelineStage.Bronze,
        DataScale scale = DataScale.Small,
        int? ordersOverride = null,
        int requestedSeed = 0)
        => new(
            Name: "planner-test",
            Scenario: BusinessScenario.SalesBi,
            Scale: scale,
            Years: 3,
            RawFormat: RawFormat.Parquet,
            StopAfter: stopAfter,
            Workspace: new FabricWorkspaceTarget("Demo"),
            RequestedSeed: requestedSeed,
            OrdersOverride: ordersOverride,
            StartDate: new DateTime(2014, 1, 1));
}
