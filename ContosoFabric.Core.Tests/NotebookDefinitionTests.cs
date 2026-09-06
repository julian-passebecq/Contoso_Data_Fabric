using System.Text.Json;
using ContosoFabric.Core.Models;
using ContosoFabric.Fabric.Notebooks;

namespace ContosoFabric.Core.Tests;

public sealed class NotebookDefinitionTests
{
    [Fact]
    public void Bronze_notebook_validates_generator_truth_manifest_when_present()
    {
        var project = new FabricProject(
            Name: "bronze-test",
            Scenario: BusinessScenario.SalesBi,
            Scale: DataScale.Small,
            Years: 3,
            RawFormat: RawFormat.Parquet,
            StopAfter: PipelineStage.Bronze,
            Workspace: new FabricWorkspaceTarget("Demo"));

        var notebookJson = NotebookDefinitionFactory.Bronze(
            project,
            "11111111-1111-1111-1111-111111111111",
            "22222222-2222-2222-2222-222222222222");

        var source = CodeSource(notebookJson);
        Assert.Contains("truth_manifest.json", source, StringComparison.Ordinal);
        Assert.Contains("actualOrders", source, StringComparison.Ordinal);
        Assert.Contains("actualOrderRows", source, StringComparison.Ordinal);
        Assert.Contains("bronze_validation_summary", source, StringComparison.Ordinal);
        Assert.Contains("orders_vs_generator_manifest", source, StringComparison.Ordinal);
        Assert.Contains("orderrows_vs_generator_manifest", source, StringComparison.Ordinal);
        Assert.Contains("raise RuntimeError", source, StringComparison.Ordinal);
        Assert.Contains("Downstream execution is blocked", source, StringComparison.Ordinal);
        Assert.Contains("manifest_fs.exists", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Gold_notebook_contains_reconciliation_contract_and_failure_gate()
    {
        var notebookJson = NotebookDefinitionFactory.Gold(
            "11111111-1111-1111-1111-111111111111",
            "22222222-2222-2222-2222-222222222222",
            "33333333-3333-3333-3333-333333333333");

        var source = CodeSource(notebookJson);
        Assert.Contains("pipeline_validation_summary", source, StringComparison.Ordinal);
        Assert.Contains("orphan_product_keys", source, StringComparison.Ordinal);
        Assert.Contains("orphan_store_keys", source, StringComparison.Ordinal);
        Assert.Contains("orphan_customer_keys", source, StringComparison.Ordinal);
        Assert.Contains("daily_aggregate_currency_mismatches", source, StringComparison.Ordinal);
        Assert.Contains("RevenueDelta", source, StringComparison.Ordinal);
        Assert.Contains("MarginDelta", source, StringComparison.Ordinal);
        Assert.Contains("raise RuntimeError", source, StringComparison.Ordinal);
        Assert.Contains("BI publication is blocked", source, StringComparison.Ordinal);
    }

    private static string CodeSource(string notebookJson)
    {
        using var notebook = JsonDocument.Parse(notebookJson);
        return string.Concat(
            notebook.RootElement.GetProperty("cells")[1].GetProperty("source")
                .EnumerateArray()
                .Select(line => line.GetString()));
    }
}
