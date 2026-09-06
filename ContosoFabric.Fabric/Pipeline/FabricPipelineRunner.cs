using ContosoFabric.Core.Generation;
using ContosoFabric.Core.Models;
using ContosoFabric.Fabric.Api;
using ContosoFabric.Fabric.Notebooks;
using ContosoFabric.Fabric.OneLake;

namespace ContosoFabric.Fabric.Pipeline;

public enum PipelineExecutionState
{
    Pending,
    Running,
    Completed,
    Failed,
    Skipped
}

public sealed record PipelineProgress(
    PipelineStage Stage,
    PipelineExecutionState State,
    string Message);

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

    public async Task<FabricProvisioningResult> PrepareAsync(
        FabricProject project,
        IProgress<PipelineProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureLiveSupported(project);
        var workspace = await _api.ResolveWorkspaceAsync(project.Workspace, cancellationToken);
        var stringProgress = new Progress<string>(message =>
            progress?.Report(new PipelineProgress(PipelineStage.Bronze, PipelineExecutionState.Running, message)));

        FabricItemInfo? bronze = null;
        FabricItemInfo? silver = null;
        FabricItemInfo? gold = null;
        FabricItemInfo? bronzeNotebook = null;
        FabricItemInfo? silverNotebook = null;
        FabricItemInfo? goldNotebook = null;

        if (project.StopAfter >= PipelineStage.Bronze)
        {
            progress?.Report(new PipelineProgress(PipelineStage.Bronze, PipelineExecutionState.Running, "Preparing Bronze Fabric items"));
            bronze = await _api.EnsureLakehouseAsync(workspace.Id, project.BronzeLakehouse, stringProgress, cancellationToken);
            bronzeNotebook = await _api.EnsureNotebookAsync(
                workspace.Id,
                NotebookDefinitionFactory.NotebookName(project, PipelineStage.Bronze),
                NotebookDefinitionFactory.Bronze(project, workspace.Id, bronze.Id),
                stringProgress,
                cancellationToken);
        }

        if (project.StopAfter >= PipelineStage.Silver)
        {
            progress?.Report(new PipelineProgress(PipelineStage.Silver, PipelineExecutionState.Running, "Preparing Silver Fabric items"));
            silver = await _api.EnsureLakehouseAsync(workspace.Id, project.SilverLakehouse, stringProgress, cancellationToken);
            silverNotebook = await _api.EnsureNotebookAsync(
                workspace.Id,
                NotebookDefinitionFactory.NotebookName(project, PipelineStage.Silver),
                NotebookDefinitionFactory.Silver(workspace.Id, bronze!.Id, silver.Id),
                stringProgress,
                cancellationToken);
        }

        if (project.StopAfter >= PipelineStage.Gold)
        {
            progress?.Report(new PipelineProgress(PipelineStage.Gold, PipelineExecutionState.Running, "Preparing Gold Fabric items"));
            gold = await _api.EnsureLakehouseAsync(workspace.Id, project.GoldLakehouse, stringProgress, cancellationToken);
            goldNotebook = await _api.EnsureNotebookAsync(
                workspace.Id,
                NotebookDefinitionFactory.NotebookName(project, PipelineStage.Gold),
                NotebookDefinitionFactory.Gold(workspace.Id, silver!.Id, gold.Id),
                stringProgress,
                cancellationToken);
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
        var messageProgress = new Progress<string>(message =>
            progress?.Report(new PipelineProgress(PipelineStage.Bronze, PipelineExecutionState.Running, message)));
        var bronze = await _api.EnsureLakehouseAsync(workspace.Id, project.BronzeLakehouse, messageProgress, cancellationToken);
        return await _uploader.UploadAsync(project.RawFormat, generatedDataFolder, workspace.Id, bronze.Id, messageProgress, cancellationToken);
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

        progress?.Report(new PipelineProgress(PipelineStage.Generate, PipelineExecutionState.Running, $"Generating {project.Scale} {project.RawFormat} data"));
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
        var uploadMessages = new Progress<string>(message =>
            progress?.Report(new PipelineProgress(PipelineStage.Bronze, PipelineExecutionState.Running, message)));
        var uploaded = await _uploader.UploadAsync(
            project.RawFormat,
            dataFolder,
            prepared.Workspace.Id,
            prepared.BronzeLakehouse!.Id,
            uploadMessages,
            cancellationToken);

        var jobs = new List<FabricJobResult>();
        var apiMessagesBronze = new Progress<string>(message =>
            progress?.Report(new PipelineProgress(PipelineStage.Bronze, PipelineExecutionState.Running, message)));
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
            var apiMessages = new Progress<string>(message =>
                progress?.Report(new PipelineProgress(PipelineStage.Silver, PipelineExecutionState.Running, message)));
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
            var apiMessages = new Progress<string>(message =>
                progress?.Report(new PipelineProgress(PipelineStage.Gold, PipelineExecutionState.Running, message)));
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
}
