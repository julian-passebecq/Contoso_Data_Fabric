using System.Text.Json;
using ContosoFabric.Core.Models;
using ContosoFabric.Fabric.Reports;
using ContosoFabric.Fabric.SemanticModel;

namespace ContosoFabric.Core.Tests;

public sealed class FabricDefinitionFactoryTests
{
    private const string WorkspaceId = "11111111-1111-1111-1111-111111111111";
    private const string GoldLakehouseId = "22222222-2222-2222-2222-222222222222";
    private const string SemanticModelId = "33333333-3333-3333-3333-333333333333";

    [Fact]
    public void Semantic_model_definition_contains_complete_direct_lake_tmdl_contract()
    {
        var definition = SemanticModelDefinitionFactory.Build(Project(), WorkspaceId, GoldLakehouseId);

        Assert.Equal("TMDL", definition.Format);
        Assert.Equal(definition.Parts.Count, definition.Parts.Select(part => part.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var paths = definition.Parts.Select(part => part.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("definition.pbism", paths);
        Assert.Contains("definition/database.tmdl", paths);
        Assert.Contains("definition/model.tmdl", paths);
        Assert.Contains("definition/expressions.tmdl", paths);
        Assert.Contains("definition/tables/Sales.tmdl", paths);
        Assert.Contains("definition/tables/Product.tmdl", paths);
        Assert.Contains("definition/tables/Store.tmdl", paths);
        Assert.Contains("definition/tables/Customer.tmdl", paths);
        Assert.Contains("definition/tables/Date.tmdl", paths);
        Assert.Contains("definition/relationships.tmdl", paths);

        using var pbism = JsonDocument.Parse(Part(definition, "definition.pbism"));
        Assert.Equal("4.2", pbism.RootElement.GetProperty("version").GetString());

        var model = Part(definition, "definition/model.tmdl");
        Assert.Contains("ref expression DirectLakeSource", model, StringComparison.Ordinal);
        Assert.Contains("ref table Sales", model, StringComparison.Ordinal);
        Assert.Contains("ref table Date", model, StringComparison.Ordinal);

        var expression = Part(definition, "definition/expressions.tmdl");
        Assert.Contains($"https://onelake.dfs.fabric.microsoft.com/{WorkspaceId}/{GoldLakehouseId}", expression, StringComparison.Ordinal);
        Assert.DoesNotContain("schemaName", expression, StringComparison.OrdinalIgnoreCase);

        var sales = Part(definition, "definition/tables/Sales.tmdl");
        Assert.Contains("partition Sales = entity", sales, StringComparison.Ordinal);
        Assert.Contains("mode: directLake", sales, StringComparison.Ordinal);
        Assert.Contains("entityName: fact_sales_enriched", sales, StringComparison.Ordinal);
        Assert.Contains("HASONEVALUE(Sales[CurrencyCode])", sales, StringComparison.Ordinal);
        Assert.Contains("measure 'Gross Margin %'", sales, StringComparison.Ordinal);
        Assert.Contains("formatString:", sales, StringComparison.Ordinal);

        var relationships = Part(definition, "definition/relationships.tmdl");
        Assert.Contains("fromColumn: Sales.ProductKey", relationships, StringComparison.Ordinal);
        Assert.Contains("toColumn: Product.ProductKey", relationships, StringComparison.Ordinal);
        Assert.Contains("fromColumn: Sales.OrderDay", relationships, StringComparison.Ordinal);
        Assert.Contains("toColumn: Date.Date", relationships, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_definition_is_deterministic_and_bound_by_connection_to_semantic_model()
    {
        var first = ReportDefinitionFactory.Build(Project(), SemanticModelId);
        var second = ReportDefinitionFactory.Build(Project(), SemanticModelId);

        Assert.Equal("PBIR", first.Format);
        Assert.Equal(
            first.Parts.Select(part => (part.Path, part.Content)),
            second.Parts.Select(part => (part.Path, part.Content)));

        foreach (var part in first.Parts)
            using (JsonDocument.Parse(part.Content)) { }

        using var binding = JsonDocument.Parse(Part(first, "definition.pbir"));
        var connection = binding.RootElement
            .GetProperty("datasetReference")
            .GetProperty("byConnection")
            .GetProperty("connectionString")
            .GetString();
        Assert.Equal($"semanticmodelid={SemanticModelId}", connection);
        Assert.False(binding.RootElement.GetProperty("datasetReference").TryGetProperty("byPath", out _));

        using var pages = JsonDocument.Parse(Part(first, "definition/pages/pages.json"));
        var pageId = pages.RootElement.GetProperty("activePageName").GetString();
        Assert.NotNull(pageId);
        Assert.Equal(20, pageId!.Length);

        var visualParts = first.Parts
            .Where(part => part.Path.Contains("/visuals/", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Equal(5, visualParts.Length);

        var visualTypes = visualParts
            .Select(part => JsonDocument.Parse(part.Content))
            .Select(document => document.RootElement.GetProperty("visual").GetProperty("visualType").GetString())
            .ToArray();
        Assert.Contains("slicer", visualTypes);
        Assert.Contains("cardVisual", visualTypes);
        Assert.Contains("lineChart", visualTypes);
        Assert.Contains("clusteredBarChart", visualTypes);
        Assert.Contains("clusteredColumnChart", visualTypes);
        Assert.DoesNotContain("card", visualTypes);
    }

    [Fact]
    public void Report_currency_slicer_and_local_currency_measure_are_both_present()
    {
        var semantic = SemanticModelDefinitionFactory.Build(Project(), WorkspaceId, GoldLakehouseId);
        var report = ReportDefinitionFactory.Build(Project(), SemanticModelId);

        Assert.Contains("HASONEVALUE(Sales[CurrencyCode])", Part(semantic, "definition/tables/Sales.tmdl"), StringComparison.Ordinal);

        var slicer = report.Parts
            .Where(part => part.Path.Contains("/visuals/", StringComparison.OrdinalIgnoreCase))
            .Select(part => JsonDocument.Parse(part.Content))
            .First(document => document.RootElement.GetProperty("visual").GetProperty("visualType").GetString() == "slicer");

        var queryRef = slicer.RootElement
            .GetProperty("visual")
            .GetProperty("query")
            .GetProperty("queryState")
            .GetProperty("Values")
            .GetProperty("projections")[0]
            .GetProperty("queryRef")
            .GetString();
        Assert.Equal("Sales.CurrencyCode", queryRef);
    }

    private static string Part(ContosoFabric.Fabric.Definitions.FabricItemDefinition definition, string path)
        => definition.Parts.Single(part => part.Path.Equals(path, StringComparison.OrdinalIgnoreCase)).Content;

    private static FabricProject Project() => new(
        Name: "definition-test",
        Scenario: BusinessScenario.SalesBi,
        Scale: DataScale.Small,
        Years: 3,
        RawFormat: RawFormat.Parquet,
        StopAfter: PipelineStage.Report,
        Workspace: new FabricWorkspaceTarget("Demo", WorkspaceId),
        BronzeLakehouse: "Bronze",
        SilverLakehouse: "Silver",
        GoldLakehouse: "Gold",
        StartFrom: PipelineStage.Generate,
        SemanticModelName: "Contoso_Model",
        ReportName: "Contoso_Report");
}
