using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ContosoFabric.Core.Generation;
using ContosoFabric.Core.Models;
using ContosoFabric.Core.Planning;
using ContosoFabric.Fabric.Api;
using ContosoFabric.Fabric.OneLake;
using ContosoFabric.Fabric.Pipeline;

namespace ContosoFabric.Desktop;

public partial class MainWindow : Window
{
    private readonly LegacyGeneratorAdapter _generator = new();
    private readonly FabricRestClient _fabricApi = new();
    private readonly OneLakeRawUploader _uploader = new();
    private readonly FabricPipelineRunner _runner;
    private readonly Dictionary<PipelineStage, PipelineExecutionState> _runtimeStates = new();
    private CancellationTokenSource? _operationCancellation;

    public MainWindow()
    {
        InitializeComponent();
        _runner = new FabricPipelineRunner(_fabricApi, _generator, _uploader);

        ScenarioBox.ItemsSource = Enum.GetValues<BusinessScenario>();
        ScaleBox.ItemsSource = Enum.GetValues<DataScale>();
        YearsBox.ItemsSource = Enumerable.Range(1, 10).ToArray();
        FormatBox.ItemsSource = Enum.GetValues<RawFormat>();
        StopAfterBox.ItemsSource = Enum.GetValues<PipelineStage>();

        ScenarioBox.SelectedItem = BusinessScenario.SalesBi;
        ScaleBox.SelectedItem = DataScale.Small;
        YearsBox.SelectedItem = 3;
        FormatBox.SelectedItem = RawFormat.Parquet;
        StopAfterBox.SelectedItem = PipelineStage.Bronze;

        Closed += (_, _) => _fabricApi.Dispose();
        RenderPlan();
    }

    private FabricProject ReadProject()
    {
        var selectedWorkspace = WorkspaceBox.SelectedItem as FabricWorkspaceInfo;
        var workspaceName = selectedWorkspace?.DisplayName ?? WorkspaceBox.Text.Trim();
        var workspaceId = selectedWorkspace?.Id;

        return new FabricProject(
            Name: ProjectNameBox.Text.Trim(),
            Scenario: (BusinessScenario)(ScenarioBox.SelectedItem ?? BusinessScenario.SalesBi),
            Scale: (DataScale)(ScaleBox.SelectedItem ?? DataScale.Small),
            Years: (int)(YearsBox.SelectedItem ?? 3),
            RawFormat: (RawFormat)(FormatBox.SelectedItem ?? RawFormat.Parquet),
            StopAfter: (PipelineStage)(StopAfterBox.SelectedItem ?? PipelineStage.Bronze),
            Workspace: new FabricWorkspaceTarget(workspaceName, workspaceId),
            BronzeLakehouse: BronzeLakehouseBox.Text.Trim(),
            SilverLakehouse: SilverLakehouseBox.Text.Trim(),
            GoldLakehouse: GoldLakehouseBox.Text.Trim());
    }

    private void Plan_Click(object sender, RoutedEventArgs e)
    {
        _runtimeStates.Clear();
        RenderPlan();
    }

    private void StopAfterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            _runtimeStates.Clear();
            RenderPlan();
        }
    }

    private void RenderPlan(bool updateStatus = true)
    {
        try
        {
            var plan = PipelinePlanner.Build(ReadProject());
            PipelinePanel.Children.Clear();

            foreach (var step in plan.Steps)
                PipelinePanel.Children.Add(CreateStepCard(step));

            PlanSummaryText.Text = $"{plan.OrdersCount:N0} orders • {plan.Project.RawFormat} • stop after {plan.Project.StopAfter}";
            WarningBorder.Visibility = plan.Warnings.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            WarningText.Text = string.Join(Environment.NewLine, plan.Warnings.Select(warning => $"• {warning}"));
            RunPipelineButton.IsEnabled = plan.Project.StopAfter <= PipelineStage.Gold && _operationCancellation is null;

            if (updateStatus)
                StatusText.Text = "Plan ready. Nothing has been sent to Fabric.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private Border CreateStepCard(PipelineStep step)
    {
        var runtimeState = _runtimeStates.TryGetValue(step.Stage, out var state) ? state : (PipelineExecutionState?)null;
        var statusText = runtimeState?.ToString() ?? (step.Implemented ? "Ready" : "Roadmap");
        var statusColor = runtimeState switch
        {
            PipelineExecutionState.Running => Color.FromRgb(0, 95, 184),
            PipelineExecutionState.Completed => Color.FromRgb(20, 110, 55),
            PipelineExecutionState.Failed => Color.FromRgb(170, 30, 45),
            _ when step.Implemented => Color.FromRgb(70, 70, 70),
            _ => Color.FromRgb(145, 95, 0)
        };

        var title = new TextBlock { Text = step.Title, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black };
        var description = new TextBlock { Text = step.Description, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(90, 90, 90)), Margin = new Thickness(0, 4, 0, 0) };
        var status = new TextBlock { Text = statusText, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(statusColor), VerticalAlignment = VerticalAlignment.Center };

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var number = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(Color.FromRgb(235, 242, 250)),
            Child = new TextBlock
            {
                Text = ((int)step.Stage + 1).ToString(),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold
            }
        };

        var text = new StackPanel { Margin = new Thickness(14, 0, 18, 0) };
        text.Children.Add(title);
        text.Children.Add(description);

        Grid.SetColumn(number, 0);
        Grid.SetColumn(text, 1);
        Grid.SetColumn(status, 2);
        content.Children.Add(number);
        content.Children.Add(text);
        content.Children.Add(status);

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(225, 225, 225)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 10),
            Child = content
        };
    }

    private async void RefreshWorkspaces_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async cancellationToken =>
        {
            StatusText.Text = "Reading accessible Fabric workspaces...";
            var workspaces = await _fabricApi.ListWorkspacesAsync(cancellationToken);
            WorkspaceBox.ItemsSource = workspaces;
            WorkspaceStatusText.Text = $"{workspaces.Count} workspaces";
            StatusText.Text = workspaces.Count == 0
                ? "No Admin/Member/Contributor workspaces were returned for the current identity."
                : "Workspace list refreshed.";
        });
    }

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async cancellationToken =>
        {
            var project = ReadProject();
            if (project.Scenario != BusinessScenario.SalesBi)
                throw new NotSupportedException("Only SalesBi generation is implemented in the native C# vertical slice.");

            var root = FindRepositoryRoot();
            var output = Path.Combine(root, "generated", "data");
            var cache = Path.Combine(root, "generated", "cache");

            UpdateStage(PipelineStage.Generate, PipelineExecutionState.Running, $"Generating {project.Scale} {project.RawFormat} data...");
            await _generator.GenerateAsync(project, root, output, cache, cancellationToken);
            UpdateStage(PipelineStage.Generate, PipelineExecutionState.Completed, $"Generation complete: {output}");
        });
    }

    private async void Prepare_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async cancellationToken =>
        {
            var project = ReadProject();
            if (project.StopAfter < PipelineStage.Bronze)
                throw new InvalidOperationException("Choose Bronze or later before preparing Fabric items.");

            var progress = CreatePipelineProgress();
            var result = await _runner.PrepareAsync(project, progress, cancellationToken);
            WorkspaceStatusText.Text = $"{result.Workspace.DisplayName} • {result.Workspace.Id}";
            StatusText.Text = "Fabric items prepared. No notebook jobs were run.";
        });
    }

    private async void Upload_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async cancellationToken =>
        {
            var project = ReadProject();
            var root = FindRepositoryRoot();
            var output = Path.Combine(root, "generated", "data");
            var progress = CreatePipelineProgress();
            var uploaded = await _runner.UploadBronzeAsync(project, output, progress, cancellationToken);
            StatusText.Text = $"Raw upload complete: {uploaded.Count} files.";
        });
    }

    private async void RunPipeline_Click(object sender, RoutedEventArgs e)
    {
        _runtimeStates.Clear();
        RenderPlan(false);
        await RunBusyAsync(async cancellationToken =>
        {
            var project = ReadProject();
            var root = FindRepositoryRoot();
            var generatedRoot = Path.Combine(root, "generated");
            var progress = CreatePipelineProgress();
            var result = await _runner.RunToSelectedStageAsync(project, root, generatedRoot, progress, cancellationToken);
            if (!string.IsNullOrWhiteSpace(result.Provisioning.Workspace.Id))
                WorkspaceStatusText.Text = $"{result.Provisioning.Workspace.DisplayName} • {result.Provisioning.Workspace.Id}";
            StatusText.Text = $"Pipeline completed through {project.StopAfter}. {result.UploadedFiles.Count} files uploaded; {result.Jobs.Count} Fabric notebook jobs completed.";
        });
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Cancellation requested...";
        _operationCancellation?.Cancel();
    }

    private IProgress<PipelineProgress> CreatePipelineProgress() => new Progress<PipelineProgress>(update =>
    {
        UpdateStage(update.Stage, update.State, update.Message);
    });

    private void UpdateStage(PipelineStage stage, PipelineExecutionState state, string message)
    {
        _runtimeStates[stage] = state;
        StatusText.Text = message;
        RenderPlan(false);
    }

    private async Task RunBusyAsync(Func<CancellationToken, Task> operation)
    {
        if (_operationCancellation is not null)
            return;

        _operationCancellation = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            await operation(_operationCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Operation cancelled.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            var running = _runtimeStates.FirstOrDefault(pair => pair.Value == PipelineExecutionState.Running);
            if (!running.Equals(default(KeyValuePair<PipelineStage, PipelineExecutionState>)))
                _runtimeStates[running.Key] = PipelineExecutionState.Failed;
            RenderPlan(false);
            MessageBox.Show(this, ex.Message, "Contoso Fabric Builder", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _operationCancellation.Dispose();
            _operationCancellation = null;
            SetBusy(false);
            RenderPlan(false);
        }
    }

    private void SetBusy(bool busy)
    {
        PlanButton.IsEnabled = !busy;
        GenerateButton.IsEnabled = !busy;
        PrepareButton.IsEnabled = !busy;
        UploadButton.IsEnabled = !busy;
        RefreshWorkspacesButton.IsEnabled = !busy;
        StopAfterBox.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;
        RunPipelineButton.IsEnabled = !busy && (StopAfterBox.SelectedItem as PipelineStage? ?? PipelineStage.Bronze) <= PipelineStage.Gold;
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "ContosoDGV2.sln")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate ContosoDGV2.sln. Run the desktop project from the repository checkout.");
    }
}
