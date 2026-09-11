using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ContosoFabric.Core.Generation;
using ContosoFabric.Core.Models;
using ContosoFabric.Core.Planning;
using ContosoFabric.Core.Projects;
using ContosoFabric.Fabric.Api;
using ContosoFabric.Fabric.OneLake;
using ContosoFabric.Fabric.Pipeline;
using Microsoft.Win32;

namespace ContosoFabric.Desktop;

public partial class MainWindow : Window
{
    private readonly LegacyGeneratorAdapter _generator = new();
    private readonly FabricRestClient _fabricApi = new();
    private readonly OneLakeRawUploader _uploader = new();
    private readonly FabricPipelineRunner _runner;
    private readonly Dictionary<PipelineStage, PipelineExecutionState> _runtimeStates = new();
    private CancellationTokenSource? _operationCancellation;
    private string? _projectFilePath;
    private string? _loadedWorkspaceName;
    private string? _loadedWorkspaceId;
    private int _requestedSeed;
    private bool _applyingProject;

    public MainWindow()
    {
        InitializeComponent();
        _runner = new FabricPipelineRunner(_fabricApi, _generator, _uploader);

        ScenarioBox.ItemsSource = Enum.GetValues<BusinessScenario>();
        ScaleBox.ItemsSource = Enum.GetValues<DataScale>();
        YearsBox.ItemsSource = Enumerable.Range(1, 20).ToArray();
        FormatBox.ItemsSource = Enum.GetValues<RawFormat>();
        StartFromBox.ItemsSource = Enum.GetValues<PipelineStage>();
        StopAfterBox.ItemsSource = Enum.GetValues<PipelineStage>();

        ApplyProject(CreateDefaultProject());
        Closed += (_, _) => _fabricApi.Dispose();
        RenderPlan();
    }

    private static FabricProject CreateDefaultProject() => new(
        Name: "contoso-fabric-sales",
        Scenario: BusinessScenario.SalesBi,
        Scale: DataScale.Small,
        Years: 3,
        RawFormat: RawFormat.Parquet,
        StopAfter: PipelineStage.Bronze,
        Workspace: new FabricWorkspaceTarget(),
        BronzeLakehouse: "Contoso_Bronze",
        SilverLakehouse: "Contoso_Silver",
        GoldLakehouse: "Contoso_Gold",
        RequestedSeed: 0,
        OrdersOverride: null,
        StartDate: new DateTime(2014, 1, 1),
        StartFrom: PipelineStage.Generate,
        SemanticModelName: "Contoso_Sales_Model",
        ReportName: "Contoso_Sales_Report");

    private FabricProject ReadProject()
    {
        var selectedWorkspace = WorkspaceBox.SelectedItem as FabricWorkspaceInfo;
        var workspaceName = selectedWorkspace?.DisplayName ?? WorkspaceBox.Text.Trim();
        var workspaceId = selectedWorkspace?.Id;

        if (workspaceId is null
            && !string.IsNullOrWhiteSpace(_loadedWorkspaceId)
            && string.Equals(workspaceName, _loadedWorkspaceName, StringComparison.OrdinalIgnoreCase))
        {
            workspaceId = _loadedWorkspaceId;
        }

        int? ordersOverride = null;
        var customOrdersText = OrdersOverrideBox.Text
            .Trim()
            .Replace(" ", string.Empty)
            .Replace(",", string.Empty)
            .Replace("_", string.Empty);
        if (!string.IsNullOrWhiteSpace(customOrdersText))
        {
            if (!int.TryParse(customOrdersText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedOrders))
                throw new FormatException("Custom orders must be a whole number, for example 500000.");
            ordersOverride = parsedOrders;
        }

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
            GoldLakehouse: GoldLakehouseBox.Text.Trim(),
            RequestedSeed: _requestedSeed,
            OrdersOverride: ordersOverride,
            StartDate: StartDatePicker.SelectedDate?.Date,
            StartFrom: (PipelineStage)(StartFromBox.SelectedItem ?? PipelineStage.Generate),
            SemanticModelName: SemanticModelBox.Text.Trim(),
            ReportName: ReportBox.Text.Trim());
    }

    private void ApplyProject(FabricProject project)
    {
        _applyingProject = true;
        try
        {
            ProjectNameBox.Text = project.Name;
            ScenarioBox.SelectedItem = project.Scenario;
            ScaleBox.SelectedItem = project.Scale;
            OrdersOverrideBox.Text = project.OrdersOverride?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            StartDatePicker.SelectedDate = project.EffectiveStartDate;
            YearsBox.SelectedItem = project.Years;
            FormatBox.SelectedItem = project.RawFormat;
            StartFromBox.SelectedItem = project.StartFrom;
            StopAfterBox.SelectedItem = project.StopAfter;
            BronzeLakehouseBox.Text = project.BronzeLakehouse;
            SilverLakehouseBox.Text = project.SilverLakehouse;
            GoldLakehouseBox.Text = project.GoldLakehouse;
            SemanticModelBox.Text = project.SemanticModelName;
            ReportBox.Text = project.ReportName;

            _requestedSeed = project.RequestedSeed;
            _loadedWorkspaceName = project.Workspace.WorkspaceName;
            _loadedWorkspaceId = project.Workspace.WorkspaceId;

            var knownWorkspaces = (WorkspaceBox.ItemsSource as IEnumerable<FabricWorkspaceInfo>)?.ToArray()
                ?? Array.Empty<FabricWorkspaceInfo>();
            var match = !string.IsNullOrWhiteSpace(project.Workspace.WorkspaceId)
                ? knownWorkspaces.FirstOrDefault(x => x.Id.Equals(project.Workspace.WorkspaceId, StringComparison.OrdinalIgnoreCase))
                : knownWorkspaces.FirstOrDefault(x => x.DisplayName.Equals(project.Workspace.WorkspaceName, StringComparison.OrdinalIgnoreCase));

            WorkspaceBox.SelectedItem = match;
            if (match is null)
                WorkspaceBox.Text = project.Workspace.WorkspaceName ?? string.Empty;
        }
        finally
        {
            _applyingProject = false;
        }
    }

    private void NewProject_Click(object sender, RoutedEventArgs e)
    {
        _runtimeStates.Clear();
        _projectFilePath = null;
        ApplyProject(CreateDefaultProject());
        UpdateProjectFileLabel();
        WorkspaceStatusText.Text = "Not connected";
        ActivityLogBox.Clear();
        RenderPlan(false);
        StatusText.Text = "New unsaved project.";
    }

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open Contoso Fabric project",
            Filter = "Contoso Fabric project (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        await RunBusyAsync(async cancellationToken =>
        {
            var project = await ProjectFileService.LoadAsync(dialog.FileName, cancellationToken);
            _runtimeStates.Clear();
            _projectFilePath = dialog.FileName;
            ApplyProject(project);
            UpdateProjectFileLabel();
            RenderPlan(false);
            StatusText.Text = $"Opened project: {Path.GetFileName(dialog.FileName)}";
        });
    }

    private async void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        var target = _projectFilePath;
        if (string.IsNullOrWhiteSpace(target))
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save Contoso Fabric project",
                Filter = "Contoso Fabric project (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = ".json",
                AddExtension = true,
                FileName = $"{SanitizeFileName(ProjectNameBox.Text)}.fabric.json"
            };

            if (dialog.ShowDialog(this) != true)
                return;
            target = dialog.FileName;
        }

        await RunBusyAsync(async cancellationToken =>
        {
            var project = ReadProject();
            await ProjectFileService.SaveAsync(project, target!, cancellationToken);
            _projectFilePath = target;
            _loadedWorkspaceName = project.Workspace.WorkspaceName;
            _loadedWorkspaceId = project.Workspace.WorkspaceId;
            UpdateProjectFileLabel();
            StatusText.Text = $"Saved project: {Path.GetFileName(target)}";
        });
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "contoso-fabric" : cleaned;
    }

    private void UpdateProjectFileLabel()
    {
        ProjectFileText.Text = _projectFilePath is null ? "Unsaved project" : Path.GetFileName(_projectFilePath);
        ProjectFileText.ToolTip = _projectFilePath ?? "Project has not been saved yet.";
    }

    private void Plan_Click(object sender, RoutedEventArgs e)
    {
        _runtimeStates.Clear();
        RenderPlan();
    }

    private void StartFromBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded && !_applyingProject)
        {
            _runtimeStates.Clear();
            RenderPlan();
        }
    }

    private void StopAfterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded && !_applyingProject)
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

            PlanSummaryText.Text = $"{plan.Project.StartFrom} → {plan.Project.StopAfter} • {plan.OrdersCount:N0} configured orders • {plan.Project.EffectiveStartDate:yyyy-MM-dd} + {plan.Project.Years}y • {plan.Project.RawFormat}";
            WarningBorder.Visibility = plan.Warnings.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            WarningText.Text = string.Join(Environment.NewLine, plan.Warnings.Select(warning => $"• {warning}"));
            RunPipelineButton.IsEnabled = _operationCancellation is null;

            if (updateStatus)
                StatusText.Text = "Plan ready. Nothing has been sent to Fabric.";
        }
        catch (Exception ex)
        {
            RunPipelineButton.IsEnabled = false;
            StatusText.Text = ex.Message;
        }
    }

    private Border CreateStepCard(PipelineStep step)
    {
        var runtimeState = _runtimeStates.TryGetValue(step.Stage, out var state) ? state : (PipelineExecutionState?)null;
        var statusText = runtimeState?.ToString() ?? (step.Implemented ? "Ready" : "Roadmap");
        var statusColor = runtimeState switch
        {
            PipelineExecutionState.Prepared => Color.FromRgb(90, 75, 150),
            PipelineExecutionState.Running => Color.FromRgb(0, 95, 184),
            PipelineExecutionState.Completed => Color.FromRgb(20, 110, 55),
            PipelineExecutionState.Failed => Color.FromRgb(170, 30, 45),
            PipelineExecutionState.Skipped => Color.FromRgb(110, 110, 110),
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
            var requestedName = WorkspaceBox.Text.Trim();
            var requestedId = (WorkspaceBox.SelectedItem as FabricWorkspaceInfo)?.Id ?? _loadedWorkspaceId;

            StatusText.Text = "Reading accessible Fabric workspaces...";
            var workspaces = await _fabricApi.ListWorkspacesAsync(cancellationToken);
            WorkspaceBox.ItemsSource = workspaces;

            var match = !string.IsNullOrWhiteSpace(requestedId)
                ? workspaces.FirstOrDefault(x => x.Id.Equals(requestedId, StringComparison.OrdinalIgnoreCase))
                : workspaces.FirstOrDefault(x => x.DisplayName.Equals(requestedName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                WorkspaceBox.SelectedItem = match;
                _loadedWorkspaceName = match.DisplayName;
                _loadedWorkspaceId = match.Id;
            }
            else
            {
                WorkspaceBox.SelectedItem = null;
                WorkspaceBox.Text = requestedName;
            }

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
            var project = ReadProject() with { StartFrom = PipelineStage.Generate, StopAfter = PipelineStage.Generate };
            if (project.Scenario != BusinessScenario.SalesBi)
                throw new NotSupportedException("Only SalesBi generation is implemented in the native C# vertical slice.");

            var root = FindRepositoryRoot();
            var result = await _runner.RunToSelectedStageAsync(project, root,
                Path.Combine(root, "generated"), CreatePipelineProgress(), cancellationToken);
            AppendActivity($"Run receipt: {result.ReceiptPath}");
            if (result.ReceiptWarning is not null)
                AppendActivity(result.ReceiptWarning);
        });
    }

    private async void Prepare_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async cancellationToken =>
        {
            var project = ReadProject();
            if (project.StopAfter < PipelineStage.Bronze)
                throw new InvalidOperationException("The selected range contains no Fabric stage to prepare.");

            var progress = CreatePipelineProgress();
            var result = await _runner.PrepareAsync(project, progress, cancellationToken);
            WorkspaceStatusText.Text = $"{result.Workspace.DisplayName} • {result.Workspace.Id}";
            StatusText.Text = $"Selected Fabric definitions prepared for {project.StartFrom} → {project.StopAfter}.";
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
        ActivityLogBox.Clear();
        RenderPlan(false);
        await RunBusyAsync(async cancellationToken =>
        {
            var project = ReadProject();
            var root = FindRepositoryRoot();
            var generatedRoot = Path.Combine(root, "generated");
            var progress = CreatePipelineProgress();
            var result = await _runner.RunToSelectedStageAsync(project, root, generatedRoot, progress, cancellationToken);
            AppendActivity($"Run receipt: {result.ReceiptPath}");
            if (result.ReceiptWarning is not null)
                AppendActivity(result.ReceiptWarning);
            if (!string.IsNullOrWhiteSpace(result.Provisioning.Workspace.Id))
                WorkspaceStatusText.Text = $"{result.Provisioning.Workspace.DisplayName} • {result.Provisioning.Workspace.Id}";

            var biSummary = new List<string>();
            if (result.Provisioning.SemanticModel is not null)
                biSummary.Add($"semantic model {result.Provisioning.SemanticModel.DisplayName}");
            if (result.Provisioning.Report is not null)
                biSummary.Add($"report {result.Provisioning.Report.DisplayName}");
            var suffix = biSummary.Count == 0 ? string.Empty : $" Published {string.Join(" and ", biSummary)}.";

            StatusText.Text = $"Range completed: {project.StartFrom} → {project.StopAfter}. {result.UploadedFiles.Count} files uploaded; {result.Jobs.Count} notebook jobs completed.{suffix}";
        });
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Cancellation requested. If local generation is active, it will finish before stopping; no later stages will start.";
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
        AppendActivity($"{stage,-13} {state,-9} {message}");
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
        catch (OperationCanceledException ex)
        {
            StatusText.Text = "Operation cancelled.";
            AppendActivity("CANCEL Operation cancelled.");
            AppendReceiptDiagnostic(ex);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            var running = _runtimeStates.FirstOrDefault(pair => pair.Value == PipelineExecutionState.Running);
            if (!running.Equals(default(KeyValuePair<PipelineStage, PipelineExecutionState>)))
                _runtimeStates[running.Key] = PipelineExecutionState.Failed;
            AppendActivity($"FAIL  {ex.Message}");
            AppendReceiptDiagnostic(ex);
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

    private void AppendReceiptDiagnostic(Exception exception)
    {
        if (exception.Data["ReceiptPath"] is string path)
            AppendActivity($"Run receipt: {path}");
        if (exception.Data["ReceiptSaveError"] is string error)
            AppendActivity($"Could not finalize run receipt ({error}); its saved status may be incomplete.");
    }

    private void SetBusy(bool busy)
    {
        PlanButton.IsEnabled = !busy;
        PreflightButton.IsEnabled = !busy;
        GenerateButton.IsEnabled = !busy;
        PrepareButton.IsEnabled = !busy;
        UploadButton.IsEnabled = !busy;
        RefreshWorkspacesButton.IsEnabled = !busy;
        NewProjectButton.IsEnabled = !busy;
        OpenProjectButton.IsEnabled = !busy;
        SaveProjectButton.IsEnabled = !busy;
        StartFromBox.IsEnabled = !busy;
        StopAfterBox.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;

        if (!busy)
        {
            try
            {
                PipelinePlanner.Build(ReadProject());
                RunPipelineButton.IsEnabled = true;
            }
            catch
            {
                RunPipelineButton.IsEnabled = false;
            }
        }
        else
        {
            RunPipelineButton.IsEnabled = false;
        }
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
