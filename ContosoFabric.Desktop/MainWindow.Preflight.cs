using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ContosoFabric.Fabric.Pipeline;

namespace ContosoFabric.Desktop;

public partial class MainWindow
{
    private bool _statusLoggingAttached;
    private string? _lastLoggedStatus;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_statusLoggingAttached)
            return;

        var descriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
        descriptor?.AddValueChanged(StatusText, StatusText_ValueChanged);
        _statusLoggingAttached = true;
        AppendActivity(StatusText.Text);
    }

    private void StatusText_ValueChanged(object? sender, EventArgs e)
    {
        var message = StatusText.Text;
        if (string.IsNullOrWhiteSpace(message) || string.Equals(message, _lastLoggedStatus, StringComparison.Ordinal))
            return;
        AppendActivity(message);
    }

    private async void Preflight_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async cancellationToken =>
        {
            ActivityLogBox.Clear();
            _lastLoggedStatus = null;
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
        if (string.IsNullOrWhiteSpace(message))
            return;

        _lastLoggedStatus = message;
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        ActivityLogBox.AppendText(line + Environment.NewLine);
        ActivityLogBox.ScrollToEnd();
    }
}
