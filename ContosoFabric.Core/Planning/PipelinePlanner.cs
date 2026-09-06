using ContosoFabric.Core.Models;

namespace ContosoFabric.Core.Planning;

public sealed record PipelineStep(
    PipelineStage Stage,
    string Title,
    string Description,
    bool Implemented);

public sealed record PipelinePlan(
    FabricProject Project,
    int OrdersCount,
    int EffectiveSeed,
    IReadOnlyList<PipelineStep> Steps,
    IReadOnlyList<string> Warnings);

public static class PipelinePlanner
{
    private static readonly PipelineStep[] AllSteps =
    [
        new(PipelineStage.Generate, "Generate data", "Run the existing deterministic Contoso C# generator.", true),
        new(PipelineStage.Bronze, "Bronze", "Create the Bronze Lakehouse, land raw files, deploy the Bronze notebook and execute it.", true),
        new(PipelineStage.Silver, "Silver", "Create Silver, deploy the cleaning/quality notebook and execute it.", true),
        new(PipelineStage.Gold, "Gold", "Create Gold, deploy the analytics notebook and build facts, dimensions and aggregates.", true),
        new(PipelineStage.SemanticModel, "Semantic model", "Create the Direct Lake semantic model.", false),
        new(PipelineStage.Report, "Power BI report", "Create and deploy the PBIR report definition.", false)
    ];

    public static PipelinePlan Build(FabricProject project)
    {
        Validate(project);

        var orders = project.OrdersOverride ?? OrdersForScale(project.Scale);
        var steps = AllSteps
            .Where(step => step.Stage <= project.StopAfter)
            .ToArray();

        var warnings = new List<string>();
        if (project.RequestedSeed != 0)
        {
            warnings.Add("The current Contoso generator is intentionally fixed to Random(0). The requested seed is stored but not applied yet.");
        }

        if (project.OrdersOverride is not null)
        {
            warnings.Add($"Custom order count {project.OrdersOverride:N0} overrides the {project.Scale} scale preset.");
        }

        if (project.Scenario != BusinessScenario.SalesBi)
        {
            warnings.Add($"{project.Scenario} is catalogued but its generator contract is not implemented yet. Live execution is currently SalesBi only.");
        }

        if (project.StopAfter > PipelineStage.Gold)
        {
            warnings.Add("Native end-to-end execution currently stops at Gold. Semantic model and PBIR are intentionally still marked as roadmap.");
        }

        return new PipelinePlan(project, orders, 0, steps, warnings);
    }

    public static int OrdersForScale(DataScale scale) => scale switch
    {
        DataScale.Tiny => 10_000,
        DataScale.Small => 100_000,
        DataScale.Medium => 500_000,
        DataScale.Large => 2_000_000,
        _ => throw new ArgumentOutOfRangeException(nameof(scale))
    };

    private static void Validate(FabricProject project)
    {
        if (string.IsNullOrWhiteSpace(project.Name))
            throw new ArgumentException("Project name is required.", nameof(project));
        if (project.Years is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(project), "Years must be between 1 and 20.");
        if (project.OrdersOverride is <= 0 or > 50_000_000)
            throw new ArgumentOutOfRangeException(nameof(project), "Custom order count must be between 1 and 50,000,000.");
        if (project.EffectiveStartDate.Year is < 1990 or > 2100)
            throw new ArgumentOutOfRangeException(nameof(project), "Start date year must be between 1990 and 2100.");
        if (string.IsNullOrWhiteSpace(project.BronzeLakehouse))
            throw new ArgumentException("Bronze Lakehouse name is required.", nameof(project));
        if (project.StopAfter >= PipelineStage.Silver && string.IsNullOrWhiteSpace(project.SilverLakehouse))
            throw new ArgumentException("Silver Lakehouse name is required for a Silver-or-later run.", nameof(project));
        if (project.StopAfter >= PipelineStage.Gold && string.IsNullOrWhiteSpace(project.GoldLakehouse))
            throw new ArgumentException("Gold Lakehouse name is required for a Gold-or-later run.", nameof(project));
    }
}
