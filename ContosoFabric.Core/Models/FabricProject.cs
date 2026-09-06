namespace ContosoFabric.Core.Models;

public enum BusinessScenario
{
    SalesBi,
    CustomerExperience,
    DataQuality,
    MlDissatisfaction
}

public enum DataScale
{
    Tiny,
    Small,
    Medium,
    Large
}

public enum RawFormat
{
    Parquet,
    Csv,
    Delta
}

public enum PipelineStage
{
    Generate = 0,
    Bronze = 1,
    Silver = 2,
    Gold = 3,
    SemanticModel = 4,
    Report = 5
}

public sealed record FabricWorkspaceTarget(
    string? WorkspaceName = null,
    string? WorkspaceId = null);

public sealed record FabricProject(
    string Name,
    BusinessScenario Scenario,
    DataScale Scale,
    int Years,
    RawFormat RawFormat,
    PipelineStage StopAfter,
    FabricWorkspaceTarget Workspace,
    string BronzeLakehouse = "Contoso_Bronze",
    string SilverLakehouse = "Contoso_Silver",
    string GoldLakehouse = "Contoso_Gold",
    int RequestedSeed = 0);
