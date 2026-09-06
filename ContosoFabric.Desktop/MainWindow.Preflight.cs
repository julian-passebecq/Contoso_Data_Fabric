using System.Windows;
using ContosoFabric.Fabric.Pipeline;

namespace ContosoFabric.Desktop;

public partial class MainWindow
{
    private async void Preflight_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async cancellationToken =>
        {
            ActivityLogBox.Clear();
            StatusText.Text = "Running read-only preflight...";

            var project = ReadProject();
            var root = FindRepositoryRoot();
            var result = await _runner.PreflightAsync(project, root, cancellationToken);

            foreach (var check in result.Checks)
            {
                var marker = check.Status switch
                {
                    PreflightStatus.Pass => "PASS",
                    PreflightStatus.Warning => "WARN",
                    _ => "FAIL"
                };
                AppendActivity($"{marker,-4}  {check.Name}: {check.Message}");
            }

            if (result.Workspace is not null)
                WorkspaceStatusText.Text = $"{result.Workspace.DisplayName} • {result.Workspace.Id}";

            StatusText.Text = result.Ready
                ? "Preflight passed. No Fabric resources were changed."
                : "Preflight found blocking issues. Review the activity log before running.";
        });
    }

    private void AppendActivity(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        ActivityLogBox.AppendText(line + Environment.NewLine);
        ActivityLogBox.ScrollToEnd();
    }
}
