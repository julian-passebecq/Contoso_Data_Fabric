using System.Text.Json;
using ContosoFabric.Core.Models;
using ContosoFabric.Fabric.Api;
using ContosoFabric.Fabric.Pipeline;

namespace ContosoFabric.Core.Tests;

public sealed class PipelineRunReceiptTests
{
    private static FabricProject Project => new("receipt-test", BusinessScenario.SalesBi,
        DataScale.Tiny, 1, RawFormat.Parquet, PipelineStage.Generate, new FabricWorkspaceTarget());

    [Fact]
    public async Task Success_receipt_records_ordered_states_and_job_identifiers()
    {
        var folder = Path.Combine(Path.GetTempPath(), "contoso-receipts", Guid.NewGuid().ToString("N"));
        try
        {
            var result = await PipelineRunReceipt.ExecuteAsync(Project, folder, null, (progress, receipt) =>
            {
                progress.Report(new(PipelineStage.Generate, PipelineExecutionState.Running, "start"));
                progress.Report(new(PipelineStage.Generate, PipelineExecutionState.Completed, "done"));
                return Task.FromResult(new FabricPipelineRunResult(
                    new(new("workspace-id", "Workspace", "Workspace"), null, null, null, null, null, null),
                    [], [new("job-id", "Completed", "activity-id", null, null)], folder));
            });
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(result.ReceiptPath!));
            var root = json.RootElement;
            Assert.Equal("completed", root.GetProperty("Status").GetString());
            Assert.Equal(2, root.GetProperty("Stages").GetArrayLength());
            Assert.Equal("activity-id", root.GetProperty("Jobs")[0].GetProperty("RootActivityId").GetString());
            Assert.Equal("workspace-id", root.GetProperty("Provisioning").GetProperty("Workspace").GetProperty("Id").GetString());
            Assert.NotEqual(JsonValueKind.Null, root.GetProperty("EndedAtUtc").ValueKind);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData(false, "failed")]
    [InlineData(true, "cancelled")]
    public async Task Failure_receipt_preserves_exception_and_omits_untrusted_error_text(bool cancelled, string status)
    {
        var folder = Path.Combine(Path.GetTempPath(), "contoso-receipts", Guid.NewGuid().ToString("N"));
        Exception failure = cancelled ? new OperationCanceledException("secret-token")
            : new FabricApiException(403, "secret-token", "secret-api-body");
        try
        {
            var thrown = await Assert.ThrowsAnyAsync<Exception>(() => PipelineRunReceipt.ExecuteAsync(Project, folder, null, (progress, receipt) =>
            {
                receipt.RecordJob(new("earlier-job", "Completed", "earlier-activity", null, null));
                progress.Report(new(PipelineStage.Bronze, PipelineExecutionState.Running, "secret-progress"));
                return Task.FromException<FabricPipelineRunResult>(failure);
            }));
            Assert.Same(failure, thrown);
            var text = await File.ReadAllTextAsync((string)thrown.Data["ReceiptPath"]!);
            Assert.DoesNotContain("secret-", text);
            using var json = JsonDocument.Parse(text);
            Assert.Equal(status, json.RootElement.GetProperty("Status").GetString());
            Assert.Equal("Bronze", json.RootElement.GetProperty("LastStage").GetString());
            Assert.Equal("earlier-job", json.RootElement.GetProperty("Jobs")[0].GetProperty("JobInstanceId").GetString());
            if (!cancelled)
                Assert.Equal(403, json.RootElement.GetProperty("Error").GetProperty("HttpStatusCode").GetInt32());
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Receipt_write_failure_does_not_change_pipeline_outcome(bool success)
    {
        var folder = Path.Combine(Path.GetTempPath(), "contoso-receipts", Guid.NewGuid().ToString("N"));
        var failure = new InvalidOperationException("original pipeline failure");
        try
        {
            var operation = PipelineRunReceipt.ExecuteAsync(Project, folder, null, (progress, receipt) =>
            {
                // The initial receipt exists; block only its final atomic write.
                Directory.CreateDirectory(Path.Combine(folder, "runs", receipt.RunId, "receipt.json.tmp"));
                return success
                    ? Task.FromResult(new FabricPipelineRunResult(
                        new(new("", "Local", "Local"), null, null, null, null, null, null), [], [], folder))
                    : Task.FromException<FabricPipelineRunResult>(failure);
            });
            if (success)
            {
                var result = await operation;
                Assert.NotNull(result.ReceiptWarning);
                Assert.True(File.Exists(result.ReceiptPath));
            }
            else
            {
                var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => operation);
                Assert.Same(failure, thrown);
                Assert.NotNull(thrown.Data["ReceiptSaveError"]);
            }
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
