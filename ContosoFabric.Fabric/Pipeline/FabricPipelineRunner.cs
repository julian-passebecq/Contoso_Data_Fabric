using ContosoFabric.Core.Generation;
using ContosoFabric.Core.Models;
using ContosoFabric.Core.Planning;
using ContosoFabric.Fabric.Api;
using ContosoFabric.Fabric.Notebooks;
using ContosoFabric.Fabric.OneLake;
using ContosoFabric.Fabric.Reports;
using ContosoFabric.Fabric.SemanticModel;

namespace ContosoFabric.Fabric.Pipeline;

public enum PipelineExecutionState
{
    Pending,
    Prepared,
    Running,
    Completed,
    Failed,
    Skipped
}

public enum PreflightStatus
{
    Pass,
    Warning,
    Fail
}

public sealed record PipelineProgress(
    PipelineStage Stage,
    PipelineExecutionState State,
    string Message);

public sealed record PreflightCheck(
    string Name,
    PreflightStatus Status,
    string Message);

public sealed record FabricPreflightResult(
    FabricWorkspaceInfo? Workspace,
    IReadOnlyList<PreflightCheck> Checks)
{
    public bool Ready => Checks.All(check => check.Status != PreflightStatus.Fail);
}

public sealed record FabricProvisioningResult(
    FabricWorkspaceInfo Workspace,
    FabricItemInfo? BronzeLakehouse,
    FabricItemInfo? SilverLakehouse,
    FabricItemInfo? GoldLakehouse,
    FabricItemInfo? BronzeNotebook,
    FabricItemInfo? SilverNotebook,
    FabricItemInfo? GoldNotebook,
    FabricItemInfo? SemanticModel = null,
    FabricItemInfo? Report = null);

public sealed record FabricPipelineRunResult(
    FabricProvisioningResult Provisioning,
    IReadOnlyList<string> UploadedFiles,
    IReadOnlyList<FabricJobResult> Jobs,
    string GeneratedDataFolder,
    string? ReceiptPath = null,
    string? ReceiptWarning = null);

public sealed class FabricPipelineRunner
{
    private static readonly string[] ExpectedRawTables =
    [
        "customer", "store", "product", "date", "currencyexchange", "sales", "orders", "orderrows"
    ];

    private readonly FabricRestClient _api;
    private readonly LegacyGeneratorAdapter _generator;
    private readonly OneLakeRawUploader _uploader;
    private readonly FabricDefinitionDeployer _definitions;

    public FabricPipelineRunner(
        FabricRestClient? api = null,
        LegacyGeneratorAdapter? generator = null,
        OneLakeRawUploader? uploader = null,
        FabricDefinitionDeployer? definitions = null)
    {
        _api = api ?? new FabricRestClient();
        _generator = generator ?? new LegacyGeneratorAdapter();
        _uploader = uploader ?? new OneLakeRawUploader();
        _definitions = definitions ?? new FabricDefinitionDeployer(_api);
    }

    public async Task<FabricPreflightResult> PreflightAsync(
        FabricProject project,
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        var checks = new List<PreflightCheck>();
        FabricWorkspaceInfo? workspace = null;

        try
        {
            var plan = PipelinePlanner.Build(project);
            checks.Add(new PreflightCheck(
                "Project",
                PreflightStatus.Pass,
                $"Project is valid: {project.StartFrom} → {project.StopAfter}, {plan.OrdersCount:N0} configured orders, {project.RawFormat}."));
        }
        catch (Exception ex)
        {
            checks.Add(new PreflightCheck("Project", PreflightStatus.Fail, ex.Message));
            return new FabricPreflightResult(null, checks);
        }

        var root = Path.GetFullPath(repositoryRoot);
        if (project.IncludesGeneration)
        {
            var configPath = Path.Combine(root, "_test_data", "IN", "config_test.json");
            var dataPath = Path.Combine(root, "_test_data", "IN", "data_test.xlsx");
            var missingInputs = new[] { configPath, dataPath }.Where(path => !File.Exists(path)).ToArray();
            checks.Add(missingInputs.Length == 0
                ? new PreflightCheck("Generator inputs", PreflightStatus.Pass, "Baseline Contoso config and workbook are present.")
                : new PreflightCheck("Generator inputs", PreflightStatus.Fail, $"Missing: {string.Join(", ", missingInputs.Select(Path.GetFileName))}"));
        }
        else
        {
            checks.Add(new PreflightCheck("Generator", PreflightStatus.Pass, "Generation is outside the selected stage range and will be skipped."));
        }

        if (project.StartFrom == PipelineStage.Bronze)
        {
            var localData = Path.Combine(root, "generated", "data");
            var missingRaw = MissingRawTables(localData, project.RawFormat);
            checks.Add(missingRaw.Count == 0
                ? new PreflightCheck("Local raw data", PreflightStatus.Pass, $"Existing {project.RawFormat} data is ready in generated/data.")
                : new PreflightCheck("Local raw data", PreflightStatus.Fail, $"Bronze-start requires existing local raw data. Missing tables: {string.Join(", ", missingRaw)}"));
        }

        if (project.Scenario != BusinessScenario.SalesBi)
        {
            checks.Add(new PreflightCheck(
                "Scenario implementation",
                PreflightStatus.Fail,
                "Live generation and Fabric execution are currently implemented for SalesBi only."));
        }
        else
        {
            checks.Add(new PreflightCheck("Scenario implementation", PreflightStatus.Pass, "SalesBi native contract is available."));
        }

        if (project.StopAfter == PipelineStage.Generate)
        {
            checks.Add(new PreflightCheck("Fabric", PreflightStatus.Pass, "Local-only run selected; Fabric authentication is not required."));
            return new FabricPreflightResult(null, checks);
        }

        try
        {
            EnsureLiveSupported(project);
            workspace = await _api.ResolveWorkspaceAsync(project.Workspace, cancellationToken);
            checks.Add(new PreflightCheck(
                "Workspace access",
                PreflightStatus.Pass,
                $"Resolved {workspace.DisplayName} ({workspace.Id})."));

            if (string.IsNullOrWhiteSpace(workspace.CapacityId))
            {
                checks.Add(new PreflightCheck(
                    "Fabric capacity",
                    PreflightStatus.Fail,
                    "The selected workspace has no capacityId. Fabric items require a supported Fabric capacity."));
            }
            else
            {
                checks.Add(new PreflightCheck(
                    "Fabric capacity",
                    PreflightStatus.Pass,
                    $"Workspace is assigned to capacity {workspace.CapacityId}."));
            }

            checks.Add(workspace.Type.Equals("Personal", StringComparison.OrdinalIgnoreCase)
                ? new PreflightCheck("Workspace type", PreflightStatus.Warning, "A personal workspace was selected. A normal Fabric workspace is recommended for this pipeline.")
                : new PreflightCheck("Workspace type", PreflightStatus.Pass, $"Workspace type is {workspace.Type}."));

            var visibleItems = await _api.ListItemsAsync(workspace.Id, cancellationToken: cancellationToken);
            checks.Add(new PreflightCheck(
                "Item API",
                PreflightStatus.Pass,
                $"Fabric item API is readable; {visibleItems.Count} current items are visible."));

            if (project.StartFrom == PipelineStage.Silver)
                AddDependencyCheck(checks, visibleItems, "Lakehouse", project.BronzeLakehouse, "Upstream Bronze", "Silver-start");

            if (project.StartFrom == PipelineStage.Gold)
                AddDependencyCheck(checks, visibleItems, "Lakehouse", project.SilverLakehouse, "Upstream Silver", "Gold-start");

            if (project.IncludesSemanticModel && !project.IncludesGold)
                AddDependencyCheck(checks, visibleItems, "Lakehouse", project.GoldLakehouse, "Upstream Gold", "Semantic-model stage");

            if (project.IncludesReport && !project.IncludesSemanticModel)
                AddDependencyCheck(checks, visibleItems, "SemanticModel", project.SemanticModelName, "Upstream semantic model", "Report stage");

            if (project.IncludesSemanticModel)
            {
                checks.Add(new PreflightCheck(
                    "Currency semantics",
                    PreflightStatus.Pass,
                    "Direct Lake measures guard local-currency revenue/margin with HASONEVALUE(CurrencyCode); mixed-currency totals are not silently summed."));
            }
        }
        catch (Exception ex)
        {
            checks.Add(new PreflightCheck(
                "Fabric connection",
                PreflightStatus.Fail,
                $"Fabric preflight failed: {ex.Message}"));
        }

        return new FabricPreflightResult(workspace, checks);
    }

    public async Task<FabricProvisioningResult> PrepareAsync(
        FabricProject project,
        IProgress<PipelineProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        PipelinePlanner.Build(project);
        EnsureLiveSupported(project);
        var workspace = await _api.ResolveWorkspaceAsync(project.Workspace, cancellationToken);
        EnsureWorkspaceCanHostFabric(workspace);

        FabricItemInfo? bronze = null;
        FabricItemInfo? silver = null;
        FabricItemInfo? gold = null;
        FabricItemInfo? bronzeNotebook = null;
        FabricItemInfo? silverNotebook = null;
        FabricItemInfo? goldNotebook = null;
        FabricItemInfo? semanticModel = null;
        FabricItemInfo? report = null;

        if (project.IncludesBronze)
        {
            var messages = StageMessages(progress, PipelineStage.Bronze);
            progress?.Report(new PipelineProgress(PipelineStage.Bronze, PipelineExecutionState.Running, "Preparing Bronze Fabric items"));
            bronze = await _api.EnsureLakehouseAsync(workspace.Id, project.BronzeLakehouse, messages, cancellationToken);
            bronzeNotebook = await _api.EnsureNotebookAsync(
                workspace.Id,
                NotebookDefinitionFactory.NotebookName(project, PipelineStage.Bronze),
                NotebookDefinitionFactory.Bronze(project, workspace.Id, bronze.Id),
                messages,
                cancellationToken);
            progress?.Report(new PipelineProgress(PipelineStage.Bronze, PipelineExecutionState.Prepared, "Bronze Lakehouse and notebook prepared"));
        }
        else if (project.StartFrom == PipelineStage.Silver)
        {
            bronze = await RequireExistingItemAsync(workspace.Id, "Lakehouse", project.BronzeLakehouse, "upstream Bronze Lakehouse", cancellationToken);
        }

        if (project.IncludesSilver)
        {
            bronze ??= await RequireExistingItemAsync(workspace.Id, "Lakehouse", project.BronzeLakehouse, "upstream Bronze Lakehouse", cancellationToken);
            var messages = StageMessages(progress, PipelineStage.Silver);
            progress?.Report(new PipelineProgress(PipelineStage.Silver, PipelineExecutionState.Running, "Preparing Silver Fabric items"));
            silver = await _api.EnsureLakehouseAsync(workspace.Id, project.SilverLakehouse, messages, cancellationToken);
            silverNotebook = await _api.EnsureNotebookAsync(
                workspace.Id,
                NotebookDefinitionFactory.NotebookName(project, PipelineStage.Silver),
                NotebookDefinitionFactory.Silver(workspace.Id, bronze.Id, silver.Id),
                messages,
                cancellationToken);
            progress?.Report(new PipelineProgress(PipelineStage.Silver, PipelineExecutionState.Prepared, "Silver Lakehouse and notebook prepared"));
        }
        else if (project.StartFrom == PipelineStage.Gold)
        {
            silver = await RequireExistingItemAsync(workspace.Id, "Lakehouse", project.SilverLakehouse, "upstream Silver Lakehouse", cancellationToken);
        }

        if (project.IncludesGold)
        {
            silver ??= await RequireExistingItemAsync(workspace.Id, "Lakehouse", project.SilverLakehouse, "upstream Silver Lakehouse", cancellationToken);
            var messages = StageMessages(progress, PipelineStage.Gold);
            progress?.Report(new PipelineProgress(PipelineStage.Gold, PipelineExecutionState.Running, "Preparing Gold Fabric items"));
            gold = await _api.EnsureLakehouseAsync(workspace.Id, project.GoldLakehouse, messages, cancellationToken);
            goldNotebook = await _api.EnsureNotebookAsync(
                workspace.Id,
                NotebookDefinitionFactory.NotebookName(project, PipelineStage.Gold),
                NotebookDefinitionFactory.Gold(workspace.Id, silver.Id, gold.Id),
                messages,
                cancellationToken);
            progress?.Report(new PipelineProgress(PipelineStage.Gold, PipelineExecutionState.Prepared, "Gold Lakehouse and notebook prepared"));
        }

        var downstreamDataIsUnexecuted = project.IncludesSemanticModel && project.IncludesGold;
        if (downstreamDataIsUnexecuted)
        {
            progress?.Report(new PipelineProgress(PipelineStage.SemanticModel, PipelineExecutionState.Skipped, "Semantic model preparation deferred until the selected Gold stage has executed."));
            if (project.IncludesReport)
                progress?.Report(new PipelineProgress(PipelineStage.Report, PipelineExecutionState.Skipped, "Report preparation deferred until the semantic model is published."));
        }
        else
        {
            if (project.IncludesSemanticModel)
            {
                gold ??= await RequireExistingItemAsync(workspace.Id, "Lakehouse", project.GoldLakehouse, "Gold Lakehouse", cancellationToken);
                var messages = StageMessages(progress, PipelineStage.SemanticModel);
                progress?.Report(new PipelineProgress(PipelineStage.SemanticModel, PipelineExecutionState.Running, "Publishing Direct Lake TMDL semantic model"));
                semanticModel = await _definitions.EnsureSemanticModelAsync(
                    workspace.Id,
                    project.SemanticModelName,
                    SemanticModelDefinitionFactory.Build(project, workspace.Id, gold.Id),
                    messages,
                    cancellationToken);
                progress?.Report(new PipelineProgress(PipelineStage.SemanticModel, PipelineExecutionState.Prepared, $"Semantic model prepared: {semanticModel.DisplayName}"));
            }

            if (project.IncludesReport)
            {
                semanticModel ??= await RequireExistingItemAsync(workspace.Id, "SemanticModel", project.SemanticModelName, "semantic model", cancellationToken);
                var messages = StageMessages(progress, PipelineStage.Report);
                progress?.Report(new PipelineProgress(PipelineStage.Report, PipelineExecutionState.Running, "Publishing PBIR Sales Overview report"));
                report = await _definitions.EnsureReportAsync(
                    workspace.Id,
                    project.ReportName,
                    ReportDefinitionFactory.Build(project, semanticModel.Id),
                    messages,
                    cancellationToken);
                progress?.Report(new PipelineProgress(PipelineStage.Report, PipelineExecutionState.Prepared, $"Report prepared: {report.DisplayName}"));
            }
        }

        return new FabricProvisioningResult(
            workspace,
            bronze,
            silver,
            gold,
            bronzeNotebook,
            silverNotebook,
            goldNotebook,
            semanticModel,
            report);
    }

    public async Task<IReadOnlyList<string>> UploadBronzeAsync(
        FabricProject project,
        string generatedDataFolder,
        IProgress<PipelineProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        PipelinePlanner.Build(project);
        if (!project.IncludesBronze)
            throw new InvalidOperationException("Bronze is outside the selected StartFrom/StopAfter range. Include Bronze before uploading raw data with this project.");
        EnsureLiveSupported(project);

        var workspace = await _api.ResolveWorkspaceAsync(project.Workspace, cancellationToken);
        EnsureWorkspaceCanHostFabric(workspace);
        var messages = StageMessages(progress, PipelineStage.Bronze);
        var bronze = await _api.EnsureLakehouseAsync(workspace.Id, project.BronzeLakehouse, messages, cancellationToken);
        var uploaded = await _uploader.UploadAsync(project.RawFormat, generatedDataFolder, workspace.Id, bronze.Id, messages, cancellationToken);
        progress?.Report(new PipelineProgress(PipelineStage.Bronze, PipelineExecutionState.Prepared, $"{uploaded.Count} raw files uploaded; Bronze transform has not been run"));
        return uploaded;
    }

    public async Task<FabricPipelineRunResult> RunToSelectedStageAsync(
        FabricProject project,
        string repositoryRoot,
        string generatedRoot,
        IProgress<PipelineProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => await PipelineRunReceipt.ExecuteAsync(project, generatedRoot, progress,
            (tracked, receipt) => RunCoreAsync(project, repositoryRoot, generatedRoot, receipt, tracked, cancellationToken));

    private async Task<FabricPipelineRunResult> RunCoreAsync(
        FabricProject project,
        string repositoryRoot,
        string generatedRoot,
        PipelineRunReceipt receipt,
        IProgress<PipelineProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        PipelinePlanner.Build(project);
        EnsureLiveSupported(project);

        var dataFolder = Path.Combine(generatedRoot, "data");
        var cacheFolder = Path.Combine(generatedRoot, "cache");
        var uploaded = new List<string>();
        var jobs = new List<FabricJobResult>();

        if (project.IncludesGeneration)
        {
            var plannedOrders = PipelinePlanner.Build(project).OrdersCount;
            progress?.Report(new PipelineProgress(PipelineStage.Generate, PipelineExecutionState.Running, $"Generating {plannedOrders:N0} {project.RawFormat} orders"));
            await _generator.GenerateAsync(project, repositoryRoot, dataFolder, cacheFolder, cancellationToken);
            progress?.Report(new PipelineProgress(PipelineStage.Generate, PipelineExecutionState.Completed, $"Generated data in {dataFolder}"));
        }

        if (project.StopAfter == PipelineStage.Generate)
        {
            var localOnly = new FabricProvisioningResult(
                new FabricWorkspaceInfo(string.Empty, "Local only", "Local"),
                null, null, null, null, null, null);
            return new FabricPipelineRunResult(localOnly, uploaded, jobs, dataFolder);
        }

        FabricProvisioningResult? prepared = null;
        if (project.StartFrom <= PipelineStage.Gold)
        {
            var dataStop = project.StopAfter > PipelineStage.Gold ? PipelineStage.Gold : project.StopAfter;
            var dataProject = project with { StopAfter = dataStop };
            prepared = await PrepareAsync(dataProject, progress, cancellationToken);
            receipt.RecordProvisioning(prepared);

            if (dataProject.IncludesBronze)
            {
                var uploadMessages = StageMessages(progress, PipelineStage.Bronze);
                var uploadedNow = await _uploader.UploadAsync(
                    project.RawFormat,
                    dataFolder,
                    prepared.Workspace.Id,
                    prepared.BronzeLakehouse!.Id,
                    uploadMessages,
                    cancellationToken);
                uploaded.AddRange(uploadedNow);

                var apiMessages = StageMessages(progress, PipelineStage.Bronze);
                progress?.Report(new PipelineProgress(PipelineStage.Bronze, PipelineExecutionState.Running, "Executing Bronze notebook"));
                var bronzeJob = await _api.RunNotebookAndWaitAsync(
                    prepared.Workspace.Id,
                    prepared.BronzeNotebook!.Id,
                    prepared.BronzeNotebook.DisplayName,
                    apiMessages,
                    cancellationToken);
                jobs.Add(bronzeJob);
                receipt.RecordJob(bronzeJob);
                progress?.Report(new PipelineProgress(PipelineStage.Bronze, PipelineExecutionState.Completed, "Bronze materialization completed"));
            }

            if (dataProject.IncludesSilver)
            {
                var apiMessages = StageMessages(progress, PipelineStage.Silver);
                progress?.Report(new PipelineProgress(PipelineStage.Silver, PipelineExecutionState.Running, "Executing Silver notebook"));
                var silverJob = await _api.RunNotebookAndWaitAsync(
                    prepared.Workspace.Id,
                    prepared.SilverNotebook!.Id,
                    prepared.SilverNotebook.DisplayName,
                    apiMessages,
                    cancellationToken);
                jobs.Add(silverJob);
                receipt.RecordJob(silverJob);
                progress?.Report(new PipelineProgress(PipelineStage.Silver, PipelineExecutionState.Completed, "Silver transformation completed"));
            }

            if (dataProject.IncludesGold)
            {
                var apiMessages = StageMessages(progress, PipelineStage.Gold);
                progress?.Report(new PipelineProgress(PipelineStage.Gold, PipelineExecutionState.Running, "Executing Gold notebook"));
                var goldJob = await _api.RunNotebookAndWaitAsync(
                    prepared.Workspace.Id,
                    prepared.GoldNotebook!.Id,
                    prepared.GoldNotebook.DisplayName,
                    apiMessages,
                    cancellationToken);
                jobs.Add(goldJob);
                receipt.RecordJob(goldJob);
                progress?.Report(new PipelineProgress(PipelineStage.Gold, PipelineExecutionState.Completed, "Gold model completed"));
            }
        }

        if (project.StopAfter >= PipelineStage.SemanticModel)
        {
            var biStart = project.StartFrom > PipelineStage.SemanticModel ? project.StartFrom : PipelineStage.SemanticModel;
            var biProject = project with { StartFrom = biStart };
            var biPrepared = await PrepareAsync(biProject, progress, cancellationToken);
            prepared = prepared is null ? biPrepared : Merge(prepared, biPrepared);
            receipt.RecordProvisioning(prepared);

            if (biProject.IncludesSemanticModel)
                progress?.Report(new PipelineProgress(PipelineStage.SemanticModel, PipelineExecutionState.Completed, $"Direct Lake semantic model published: {biPrepared.SemanticModel!.DisplayName}"));
            if (biProject.IncludesReport)
                progress?.Report(new PipelineProgress(PipelineStage.Report, PipelineExecutionState.Completed, $"PBIR report published: {biPrepared.Report!.DisplayName}"));
        }

        if (prepared is null)
            throw new InvalidOperationException("No executable stage was selected.");

        return new FabricPipelineRunResult(prepared, uploaded, jobs, dataFolder);
    }

    private async Task<FabricItemInfo> RequireExistingItemAsync(
        string workspaceId,
        string type,
        string displayName,
        string purpose,
        CancellationToken cancellationToken)
    {
        var items = await _api.ListItemsAsync(workspaceId, type, cancellationToken);
        var matches = items
            .Where(item => item.Type.Equals(type, StringComparison.OrdinalIgnoreCase)
                && item.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"Required {purpose} '{displayName}' does not exist. Run its upstream stage first or change StartFrom."),
            _ => throw new InvalidOperationException($"More than one {type} is named '{displayName}'. Rename duplicates before using it as an upstream dependency.")
        };
    }

    private static FabricProvisioningResult Merge(FabricProvisioningResult left, FabricProvisioningResult right)
        => new(
            right.Workspace.Id.Length > 0 ? right.Workspace : left.Workspace,
            left.BronzeLakehouse ?? right.BronzeLakehouse,
            left.SilverLakehouse ?? right.SilverLakehouse,
            left.GoldLakehouse ?? right.GoldLakehouse,
            left.BronzeNotebook ?? right.BronzeNotebook,
            left.SilverNotebook ?? right.SilverNotebook,
            left.GoldNotebook ?? right.GoldNotebook,
            left.SemanticModel ?? right.SemanticModel,
            left.Report ?? right.Report);

    private static void AddDependencyCheck(
        ICollection<PreflightCheck> checks,
        IEnumerable<FabricItemInfo> items,
        string type,
        string displayName,
        string checkName,
        string reason)
    {
        var matches = items.Where(item =>
            item.Type.Equals(type, StringComparison.OrdinalIgnoreCase)
            && item.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase)).ToArray();

        checks.Add(matches.Length switch
        {
            1 => new PreflightCheck(checkName, PreflightStatus.Pass, $"Existing {type} found: {displayName}."),
            0 => new PreflightCheck(checkName, PreflightStatus.Fail, $"{reason} requires existing {type} '{displayName}'."),
            _ => new PreflightCheck(checkName, PreflightStatus.Fail, $"{reason} found duplicate {type} items named '{displayName}'.")
        });
    }

    private static IReadOnlyList<string> MissingRawTables(string dataFolder, RawFormat format)
    {
        if (!Directory.Exists(dataFolder))
            return ExpectedRawTables;

        var missing = new List<string>();
        foreach (var table in ExpectedRawTables)
        {
            var exists = format switch
            {
                RawFormat.Csv => File.Exists(Path.Combine(dataFolder, $"{table}.csv")),
                RawFormat.Parquet => File.Exists(Path.Combine(dataFolder, $"{table}.parquet")),
                RawFormat.Delta => Directory.Exists(Path.Combine(dataFolder, table))
                    && Directory.Exists(Path.Combine(dataFolder, table, "_delta_log")),
                _ => false
            };
            if (!exists)
                missing.Add(table);
        }
        return missing;
    }

    private static IProgress<string> StageMessages(
        IProgress<PipelineProgress>? progress,
        PipelineStage stage)
        => new InlineProgress<string>(message =>
            progress?.Report(new PipelineProgress(stage, PipelineExecutionState.Running, message)));

    private static void EnsureLiveSupported(FabricProject project)
    {
        if (project.Scenario != BusinessScenario.SalesBi)
            throw new NotSupportedException("Live generation and Fabric execution are currently implemented for SalesBi only.");

        if (project.StopAfter >= PipelineStage.Bronze
            && string.IsNullOrWhiteSpace(project.Workspace.WorkspaceId)
            && string.IsNullOrWhiteSpace(project.Workspace.WorkspaceName))
        {
            throw new InvalidOperationException("Choose or enter a Fabric workspace before running Fabric stages.");
        }
    }

    private static void EnsureWorkspaceCanHostFabric(FabricWorkspaceInfo workspace)
    {
        if (string.IsNullOrWhiteSpace(workspace.CapacityId))
            throw new InvalidOperationException($"Workspace '{workspace.DisplayName}' is not assigned to a Fabric capacity. Assign a supported capacity before creating Fabric items.");
    }
}
