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
                $"Project is valid: {plan.OrdersCount:N0} orders, {project.RawFormat}, stop after {project.StopAfter}."));
        }
        catch (Exception ex)
        {
            checks.Add(new PreflightCheck("Project", PreflightStatus.Fail, ex.Message));
            return new FabricPreflightResult(null, checks);
        }

        var root = Path.GetFullPath(repositoryRoot);
        var configPath = Path.Combine(root, "_test_data", "IN", "config_test.json");
        var dataPath = Path.Combine(root, "_test_data", "IN", "data_test.xlsx");
        var missingInputs = new[] { configPath, dataPath }.Where(path => !File.Exists(path)).ToArray();
        checks.Add(missingInputs.Length == 0
            ? new PreflightCheck("Generator inputs", PreflightStatus.Pass, "Baseline Contoso config and workbook are present.")
            : new PreflightCheck("Generator inputs", PreflightStatus.Fail, $"Missing: {string.Join(", ", missingInputs.Select(Path.GetFileName))}"));

        if (project.Scenario != BusinessScenario.SalesBi)
        {
            checks.Add(new PreflightCheck(
                "Scenario implementation",
                PreflightStatus.Fail,
                "Live generation and Fabric execution are currently implemented for SalesBi only."));
        }
        else
        {
            checks.Add(new PreflightCheck("Scenario implementation", PreflightStatus.Pass, "SalesBi native generator contract is available."));
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
        EnsureLiveSupported(project);
        var workspace = await _api.ResolveWorkspaceAsync(project.Workspace, cancellationToken);
        EnsureWorkspaceCanHostFabric(workspace);

        FabricItemInfo? bronze = null;
        FabricItemInfo? silver = null;
        FabricItemInfo? gold = null;
        FabricItemInfo? bronzeNotebook = null;
        FabricItemInfo? silverNotebook = null;
        FabricItemInfo? goldNotebook = null;

        if (project.StopAfter >= PipelineStage.Bronze)
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

        if (project.StopAfter >= PipelineStage.Silver)
        {
            var messages = StageMessages(progress, PipelineStage.Silver);
            progress?.Report(new PipelineProgress(PipelineStage.Silver, PipelineExecutionState.Running, "Preparing Silver Fabric items"));
            silver = await _api.EnsureLakehouseAsync(workspace.Id, project.SilverLakehouse, messages, cancellationToken);
            silverNotebook = await _api.EnsureNotebookAsync(
                workspace.Id,
                NotebookDefinitionFactory.NotebookName(project, PipelineStage.Silver),
                NotebookDefinitionFactory.Silver(workspace.Id, bronze!.Id, silver.Id),
                messages,
                cancellationToken);
            progress?.Report(new PipelineProgress(PipelineStage.Silver, PipelineExecutionState.Prepared, "Silver Lakehouse and notebook prepared"));
        }

        if (project.StopAfter >= PipelineStage.Gold)
        {
            var messages = StageMessages(progress, PipelineStage.Gold);
            progress?.Report(new PipelineProgress(PipelineStage.Gold, PipelineExecutionState.Running, "Preparing Gold Fabric items"));
            gold = await _api.EnsureLakehouseAsync(workspace.Id, project.GoldLakehouse, messages, cancellationToken);
            goldNotebook = await _api.EnsureNotebookAsync(
                workspace.Id,
                NotebookDefinitionFactory.NotebookName(project, PipelineStage.Gold),
                NotebookDefinitionFactory.Gold(workspace.Id, silver!.Id, gold.Id),
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
        if (project.StopAfter < PipelineStage.Bronze)
            throw new InvalidOperationException("Choose Bronze or a later stage before uploading to Fabric.");
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
        EnsureLiveSupported(project);
        if (project.StopAfter > PipelineStage.Gold)
            throw new NotSupportedException("End-to-end native execution is implemented through Gold. Choose Gold or earlier for Run selected pipeline.");

        var dataFolder = Path.Combine(generatedRoot, "data");
        var cacheFolder = Path.Combine(generatedRoot, "cache");
        var plannedOrders = PipelinePlanner.Build(project).OrdersCount;

        progress?.Report(new PipelineProgress(PipelineStage.Generate, PipelineExecutionState.Running, $"Generating {plannedOrders:N0} {project.RawFormat} orders"));
        await _generator.GenerateAsync(project, repositoryRoot, dataFolder, cacheFolder, cancellationToken);
        progress?.Report(new PipelineProgress(PipelineStage.Generate, PipelineExecutionState.Completed, $"Generated data in {dataFolder}"));

        if (project.StopAfter == PipelineStage.Generate)
        {
            var localOnly = new FabricProvisioningResult(
                new FabricWorkspaceInfo(string.Empty, "Local only", "Local"),
                null, null, null, null, null, null);
            return new FabricPipelineRunResult(localOnly, Array.Empty<string>(), Array.Empty<FabricJobResult>(), dataFolder);
        }

        var prepared = await PrepareAsync(project, progress, cancellationToken);
        var uploadMessages = StageMessages(progress, PipelineStage.Bronze);
        var uploaded = await _uploader.UploadAsync(
            project.RawFormat,
            dataFolder,
            prepared.Workspace.Id,
            prepared.BronzeLakehouse!.Id,
            uploadMessages,
            cancellationToken);

        var jobs = new List<FabricJobResult>();
        var apiMessagesBronze = StageMessages(progress, PipelineStage.Bronze);
        progress?.Report(new PipelineProgress(PipelineStage.Bronze, PipelineExecutionState.Running, "Executing Bronze notebook"));
        var bronzeJob = await _api.RunNotebookAndWaitAsync(
            prepared.Workspace.Id,
            prepared.BronzeNotebook!.Id,
            prepared.BronzeNotebook.DisplayName,
            apiMessagesBronze,
            cancellationToken);
        jobs.Add(bronzeJob);
        progress?.Report(new PipelineProgress(PipelineStage.Bronze, PipelineExecutionState.Completed, "Bronze materialization completed"));

        if (project.StopAfter >= PipelineStage.Silver)
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

        if (project.StopAfter >= PipelineStage.Gold)
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

    private static IProgress<string> StageMessages(
        IProgress<PipelineProgress>? progress,
        PipelineStage stage)
        => new Progress<string>(message =>
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
            throw new InvalidOperationException($"Workspace '{workspace.DisplayName}' is not assigned to a Fabric capacity. Assign a supported capacity before creating Lakehouses or notebooks.");
    }
}
