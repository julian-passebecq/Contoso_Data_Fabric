using System.Text.Json;
using System.Text.Json.Serialization;
using ContosoFabric.Core.Models;
using ContosoFabric.Fabric.Api;

namespace ContosoFabric.Fabric.Pipeline;

public sealed record RunStageEvent(DateTimeOffset AtUtc, PipelineStage Stage, PipelineExecutionState State);
public sealed record RunFailure(string ExceptionType, int? HttpStatusCode);

/// <summary>Allowlisted diagnostics: never serialize arbitrary exception messages or API bodies.</summary>
public sealed class PipelineRunReceipt
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string RunId { get; init; } = Guid.NewGuid().ToString("N");
    public required string ProjectName { get; init; }
    public required FabricWorkspaceTarget Workspace { get; init; }
    public PipelineStage StartFrom { get; init; }
    public PipelineStage StopAfter { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAtUtc { get; private set; }
    public string Status { get; private set; } = "running";
    public PipelineStage? LastStage { get; private set; }
    public List<RunStageEvent> Stages { get; } = new();
    public FabricProvisioningResult? Provisioning { get; private set; }
    public IReadOnlyList<FabricJobResult> Jobs { get; private set; } = Array.Empty<FabricJobResult>();
    public RunFailure? Error { get; private set; }

    public void RecordProvisioning(FabricProvisioningResult provisioning) => Provisioning = provisioning;

    public void RecordJob(FabricJobResult job) => Jobs = Jobs.Append(job).ToArray();

    public static async Task<FabricPipelineRunResult> ExecuteAsync(
        FabricProject project,
        string generatedRoot,
        IProgress<PipelineProgress>? progress,
        Func<IProgress<PipelineProgress>, PipelineRunReceipt, Task<FabricPipelineRunResult>> execute)
    {
        var receipt = new PipelineRunReceipt
        {
            ProjectName = project.Name, Workspace = project.Workspace,
            StartFrom = project.StartFrom, StopAfter = project.StopAfter
        };
        var directory = Path.Combine(generatedRoot, "runs", receipt.RunId);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "receipt.json");
        await receipt.SaveAsync(path);
        var tracked = new InlineProgress<PipelineProgress>(update =>
        {
            receipt.LastStage = update.Stage;
            // Repeated polling messages don't add useful state transitions.
            var last = receipt.Stages.LastOrDefault();
            if (last is null || last.Stage != update.Stage || last.State != update.State)
                receipt.Stages.Add(new RunStageEvent(DateTimeOffset.UtcNow, update.Stage, update.State));
            progress?.Report(update);
        });
        FabricPipelineRunResult result;
        try
        {
            result = await execute(tracked, receipt);
        }
        catch (Exception ex)
        {
            receipt.Status = ex is OperationCanceledException ? "cancelled" : "failed";
            receipt.EndedAtUtc = DateTimeOffset.UtcNow;
            receipt.Error = new RunFailure(ex.GetType().Name, (ex as FabricApiException)?.StatusCode);
            ex.Data["ReceiptPath"] = path;
            try { await receipt.SaveAsync(path); }
            catch (Exception saveError)
            {
                // Preserve the pipeline's original failure if diagnostics cannot be written.
                ex.Data["ReceiptSaveError"] = saveError.GetType().Name;
            }
            throw;
        }
        receipt.Status = "completed";
        receipt.EndedAtUtc = DateTimeOffset.UtcNow;
        receipt.Provisioning = result.Provisioning;
        receipt.Jobs = result.Jobs;
        try { await receipt.SaveAsync(path); }
        catch (Exception ex)
        {
            // A completed deployment must not be reported as failed and accidentally rerun.
            return result with { ReceiptPath = path, ReceiptWarning = $"Could not finalize run receipt ({ex.GetType().Name})." };
        }
        return result with { ReceiptPath = path };
    }

    private async Task SaveAsync(string path)
    {
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }
}

internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
