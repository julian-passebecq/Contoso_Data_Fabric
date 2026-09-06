using System.Text.Json.Serialization;

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
    int RequestedSeed = 0,
    int? OrdersOverride = null,
    DateTime? StartDate = null)
{
    [JsonIgnore]
    public DateTime EffectiveStartDate => StartDate?.Date
        ?? new DateTime(2014, 1, 1, 0, 0, 0, DateTimeKind.Utc);
}
