using System.Text.Json;
using ContosoFabric.Fabric.Notebooks;

namespace ContosoFabric.Core.Tests;

public sealed class NotebookDefinitionTests
{
    [Fact]
    public void Gold_notebook_contains_reconciliation_contract_and_failure_gate()
    {
        var notebookJson = NotebookDefinitionFactory.Gold(
            "11111111-1111-1111-1111-111111111111",
            "22222222-2222-2222-2222-222222222222",
            "33333333-3333-3333-3333-333333333333");

        using var notebook = JsonDocument.Parse(notebookJson);
        var source = string.Concat(
            notebook.RootElement.GetProperty("cells")[1].GetProperty("source")
                .EnumerateArray()
                .Select(line => line.GetString()));

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
}
