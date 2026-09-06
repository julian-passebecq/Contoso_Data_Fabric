using ContosoFabric.Core.Generation;
using ContosoFabric.Core.Models;
using ContosoFabric.Core.Planning;
using ContosoFabric.Fabric.Api;
using ContosoFabric.Fabric.Notebooks;
using ContosoFabric.Fabric.OneLake;

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
    FabricItemInfo? GoldNotebook);

public sealed record FabricPipelineRunResult(
    FabricProvisioningResult Provisioning,
    IReadOnlyList<string> UploadedFiles,
    IReadOnlyList<FabricJobResult> Jobs,
    string GeneratedDataFolder);

public sealed class FabricPipelineRunner
{
    private static readonly string[] ExpectedRawTables =
    [
        "customer", "store", "product", "date", "currencyexchange", "sales", "orders", "orderrows"
    ];

    private readonly FabricRestClient _api;
    private readonly LegacyGeneratorAdapter _generator;
    private readonly OneLakeRawUploader _uploader;

    public FabricPipelineRunner(
        FabricRestClient? api = null,
        LegacyGeneratorAdapter? generator = null,
        OneLakeRawUploader? uploader = null)
    {
        _api = api ?? new FabricRestClient();
        _generator = generator ?? new LegacyGeneratorAdapter();
        _uploader = uploader ?? new OneLakeRawUploader();
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

        if (project.StopAfter > PipelineStage.Gold)
        {
            checks.Add(new PreflightCheck(
                "Selected endpoint",
                PreflightStatus.Fail,
                "Native execution is currently implemented through Gold. Semantic model and Report remain roadmap stages."));
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
                    "The selected workspace has no capacityId. Fabric Lakehouse/Notebook creation requires a supported Fabric capacity."));
            }
            else
            {
                checks.Add(new PreflightCheck(
                    "Fabric capacity",
                    PreflightStatus.Pass,
                    $"Workspace is assigned to capacity {workspace.CapacityId}."));
            }

            if (workspace.Type.Equals("Personal", StringComparison.OrdinalIgnoreCase))
            {
                checks.Add(new PreflightCheck(
                    "Workspace type",
                    PreflightStatus.Warning,
                    "A personal workspace was selected. A normal Fabric workspace is recommended for this pipeline."));
            }
            else
            {
                checks.Add(new PreflightCheck("Workspace type", PreflightStatus.Pass, $"Workspace type is {workspace.Type}."));
            }

            var visibleItems = await _api.ListItemsAsync(workspace.Id, cancellationToken: cancellationToken);
            checks.Add(new PreflightCheck(
                "Item API",
                PreflightStatus.Pass,
                $"Fabric item API is readable; {visibleItems.Count} current items are visible."));

            if (project.StartFrom == PipelineStage.Silver)
            {
                var bronze = FindExactItem(visibleItems, "Lakehouse", project.BronzeLakehouse);
                checks.Add(bronze is not null
                    ? new PreflightCheck("Upstream Bronze", PreflightStatus.Pass, $"Existing Bronze Lakehouse found: {project.BronzeLakehouse}.")
                    : new PreflightCheck("Upstream Bronze", PreflightStatus.Fail, $"Silver-start requires existing Bronze Lakehouse '{project.BronzeLakehouse}'."));
            }

            if (project.StartFrom == PipelineStage.Gold)
            {
                var silver = FindExactItem(visibleItems, "Lakehouse", project.SilverLakehouse);
                checks.Add(silver is not null
                    ? new PreflightCheck("Upstream Silver", PreflightStatus.Pass, $"Existing Silver Lakehouse found: {project.SilverLakehouse}.")
                    : new PreflightCheck("Upstream Silver", PreflightStatus.Fail, $"Gold-start requires existing Silver Lakehouse '{project.SilverLakehouse}'."));
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
            bronze = await RequireExistingLakehouseAsync(workspace.Id, project.BronzeLakehouse, cancellationToken);
        }

        if (project.IncludesSilver)
        {
            if (bronze is null)
                bronze = await RequireExistingLakehouseAsync(workspace.Id, project.BronzeLakehouse, cancellationToken);

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
            silver = await RequireExistingLakehouseAsync(workspace.Id, project.SilverLakehouse, cancellationToken);
        }

        if (project.IncludesGold)
        {
            if (silver is null)
                silver = await RequireExistingLakehouseAsync(workspace.Id, project.SilverLakehouse, cancellationToken);

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

        return new FabricProvisioningResult(workspace, bronze, silver, gold, bronzeNotebook, silverNotebook, goldNotebook);
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
    {
        PipelinePlanner.Build(project);
        EnsureLiveSupported(project);
        if (project.StopAfter > PipelineStage.Gold)
            throw new NotSupportedException("End-to-end native execution is implemented through Gold. Choose Gold or earlier for Run selected pipeline.");

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

        var prepared = await PrepareAsync(project, progress, cancellationToken);

        if (project.IncludesBronze)
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
            progress?.Report(new PipelineProgress(PipelineStage.Bronze, PipelineExecutionState.Completed, "Bronze materialization completed"));
        }

        if (project.IncludesSilver)
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
            progress?.Report(new PipelineProgress(PipelineStage.Silver, PipelineExecutionState.Completed, "Silver transformation completed"));
        }

        if (project.IncludesGold)
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
            progress?.Report(new PipelineProgress(PipelineStage.Gold, PipelineExecutionState.Completed, "Gold model completed"));
        }

        return new FabricPipelineRunResult(prepared, uploaded, jobs, dataFolder);
    }

    private async Task<FabricItemInfo> RequireExistingLakehouseAsync(
        string workspaceId,
        string displayName,
        CancellationToken cancellationToken)
    {
        var items = await _api.ListItemsAsync(workspaceId, "Lakehouse", cancellationToken);
        return FindExactItem(items, "Lakehouse", displayName)
            ?? throw new InvalidOperationException($"Required upstream Lakehouse '{displayName}' does not exist. Run its upstream stage first or change StartFrom.");
    }

    private static FabricItemInfo? FindExactItem(
        IEnumerable<FabricItemInfo> items,
        string type,
        string displayName)
        => items.FirstOrDefault(item =>
            item.Type.Equals(type, StringComparison.OrdinalIgnoreCase)
            && item.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase));

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
        => new Progress<string>(message =>
            progress?.Report(new PipelineProgress(stage, PipelineExecutionState.Running, message)));

    private static void EnsureLiveSupported(FabricProject project)
    {
        if (project.Scenario != BusinessScenario.SalesBi)
            throw new NotSupportedException("Live generation and Fabric execution are currently implemented for SalesBi only.");

        var needsFabric = project.StopAfter >= PipelineStage.Bronze;
        if (needsFabric
            && string.IsNullOrWhiteSpace(project.Workspace.WorkspaceId)
            && string.IsNullOrWhiteSpace(project.Workspace.WorkspaceName))
        {
            throw new InvalidOperationException("Choose or enter a Fabric workspace before running Fabric stages.");
        }
    }

    private static void EnsureWorkspaceCanHostFabric(FabricWorkspaceInfo workspace)
    {
        if (string.IsNullOrWhiteSpace(workspace.CapacityId))
            throw new InvalidOperationException($"Workspace '{workspace.DisplayName}' is not assigned to a Fabric capacity. Assign a supported capacity before creating Lakehouses or notebooks.");
    }
}
