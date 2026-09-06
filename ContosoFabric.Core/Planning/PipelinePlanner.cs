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
        new(PipelineStage.Bronze, "Bronze", "Land raw files in OneLake and materialize Bronze Delta tables.", true),
        new(PipelineStage.Silver, "Silver", "Clean, deduplicate and standardize the core entities.", false),
        new(PipelineStage.Gold, "Gold", "Build analytics-ready facts, dimensions and aggregates.", false),
        new(PipelineStage.SemanticModel, "Semantic model", "Create the Direct Lake semantic model.", false),
        new(PipelineStage.Report, "Power BI report", "Create and deploy the report definition.", false)
    ];

    public static PipelinePlan Build(FabricProject project)
    {
        Validate(project);

        var orders = project.Scale switch
        {
            DataScale.Tiny => 10_000,
            DataScale.Small => 100_000,
            DataScale.Medium => 500_000,
            DataScale.Large => 2_000_000,
            _ => throw new ArgumentOutOfRangeException(nameof(project.Scale))
        };

        var steps = AllSteps
            .Where(step => step.Stage <= project.StopAfter)
            .ToArray();

        var warnings = new List<string>();
        if (project.RequestedSeed != 0)
        {
            warnings.Add("The current Contoso generator is intentionally fixed to Random(0). The requested seed is stored but not applied yet.");
        }

        if (project.Scenario != BusinessScenario.SalesBi)
        {
            warnings.Add($"{project.Scenario} is part of the roadmap but the V1 generator contract is SalesBi.");
        }

        if (steps.Any(step => !step.Implemented))
        {
            warnings.Add("The planner can show later Fabric stages before their deployer is implemented; the UI marks those stages clearly.");
        }

        return new PipelinePlan(project, orders, 0, steps, warnings);
    }

    private static void Validate(FabricProject project)
    {
        if (string.IsNullOrWhiteSpace(project.Name))
            throw new ArgumentException("Project name is required.", nameof(project));
        if (project.Years is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(project), "Years must be between 1 and 20.");
    }
}
