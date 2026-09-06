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
        new(PipelineStage.Bronze, "Bronze", "Create/reuse Bronze, land local raw files, deploy the Bronze notebook and execute it.", true),
        new(PipelineStage.Silver, "Silver", "Use existing Bronze tables, create/reuse Silver, deploy the cleaning/quality notebook and execute it.", true),
        new(PipelineStage.Gold, "Gold", "Use existing Silver tables, create/reuse Gold, deploy the analytics notebook and execute it.", true),
        new(PipelineStage.SemanticModel, "Semantic model", "Create/update a Direct Lake TMDL model over the Gold Lakehouse.", true),
        new(PipelineStage.Report, "Power BI report", "Create/update a PBIR Sales Overview report bound to the semantic model.", true)
    ];

    public static PipelinePlan Build(FabricProject project)
    {
        Validate(project);

        var orders = project.OrdersOverride ?? OrdersForScale(project.Scale);
        var steps = AllSteps
            .Where(step => step.Stage >= project.StartFrom && step.Stage <= project.StopAfter)
            .ToArray();

        var warnings = new List<string>();
        if (project.RequestedSeed != 0)
            warnings.Add("The current Contoso generator is intentionally fixed to Random(0). The requested seed is stored but not applied yet.");

        if (project.OrdersOverride is not null)
            warnings.Add($"Custom order count {project.OrdersOverride:N0} overrides the {project.Scale} scale preset.");

        if (project.StartFrom > PipelineStage.Generate)
            warnings.Add($"This run starts at {project.StartFrom}. Required upstream artifacts must already exist; upstream stages will not be recreated or rerun.");

        if (project.StartFrom == PipelineStage.Bronze)
            warnings.Add("Bronze-start runs use the existing local generated/data folder as the raw source; data generation is skipped.");

        if (project.StartFrom == PipelineStage.SemanticModel)
            warnings.Add($"SemanticModel-start requires existing Gold Lakehouse '{project.GoldLakehouse}'.");

        if (project.StartFrom == PipelineStage.Report)
            warnings.Add($"Report-start requires existing semantic model '{project.SemanticModelName}'.");

        if (project.Scenario != BusinessScenario.SalesBi)
            warnings.Add($"{project.Scenario} is catalogued but its generator contract is not implemented yet. Live execution is currently SalesBi only.");

        if (project.IncludesSemanticModel)
            warnings.Add("Local-currency revenue and margin measures intentionally return blank when more than one CurrencyCode is in filter context.");

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
        if (project.StartFrom > project.StopAfter)
            throw new ArgumentException($"StartFrom ({project.StartFrom}) cannot be later than StopAfter ({project.StopAfter}).", nameof(project));
        if (!Enum.IsDefined(project.StartFrom) || !Enum.IsDefined(project.StopAfter))
            throw new ArgumentException("StartFrom and StopAfter must be valid pipeline stages.", nameof(project));

        if ((project.IncludesBronze || project.StartFrom == PipelineStage.Silver) && string.IsNullOrWhiteSpace(project.BronzeLakehouse))
            throw new ArgumentException("Bronze Lakehouse name is required when Bronze is selected or used as a Silver dependency.", nameof(project));
        if ((project.IncludesSilver || project.StartFrom == PipelineStage.Gold) && string.IsNullOrWhiteSpace(project.SilverLakehouse))
            throw new ArgumentException("Silver Lakehouse name is required when Silver is selected or used as a Gold dependency.", nameof(project));
        if ((project.IncludesGold || project.StartFrom == PipelineStage.SemanticModel) && string.IsNullOrWhiteSpace(project.GoldLakehouse))
            throw new ArgumentException("Gold Lakehouse name is required when Gold is selected or used as a semantic-model dependency.", nameof(project));
        if ((project.IncludesSemanticModel || project.StartFrom == PipelineStage.Report) && string.IsNullOrWhiteSpace(project.SemanticModelName))
            throw new ArgumentException("Semantic model name is required when the semantic model is selected or used as a report dependency.", nameof(project));
        if (project.IncludesReport && string.IsNullOrWhiteSpace(project.ReportName))
            throw new ArgumentException("Report name is required when Report is selected.", nameof(project));
    }
}
