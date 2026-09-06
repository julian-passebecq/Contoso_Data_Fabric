using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ContosoFabric.Core.Generation;
using ContosoFabric.Core.Models;
using ContosoFabric.Core.Planning;
using ContosoFabric.Fabric.OneLake;

namespace ContosoFabric.Desktop;

public partial class MainWindow : Window
{
    private readonly LegacyGeneratorAdapter _generator = new();
    private readonly OneLakeRawUploader _uploader = new();

    public MainWindow()
    {
        InitializeComponent();

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

        RenderPlan();
    }

    private FabricProject ReadProject()
    {
        return new FabricProject(
            Name: ProjectNameBox.Text.Trim(),
            Scenario: (BusinessScenario)(ScenarioBox.SelectedItem ?? BusinessScenario.SalesBi),
            Scale: (DataScale)(ScaleBox.SelectedItem ?? DataScale.Small),
            Years: (int)(YearsBox.SelectedItem ?? 3),
            RawFormat: (RawFormat)(FormatBox.SelectedItem ?? RawFormat.Parquet),
            StopAfter: (PipelineStage)(StopAfterBox.SelectedItem ?? PipelineStage.Bronze),
            Workspace: new FabricWorkspaceTarget(WorkspaceBox.Text.Trim()),
            BronzeLakehouse: BronzeLakehouseBox.Text.Trim());
    }

    private void Plan_Click(object sender, RoutedEventArgs e) => RenderPlan();

    private void StopAfterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
            RenderPlan();
    }

    private void RenderPlan()
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
            StatusText.Text = "Plan ready. Nothing has been sent to Fabric.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private static Border CreateStepCard(PipelineStep step)
    {
        var title = new TextBlock
        {
            Text = step.Title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Black
        };
        var description = new TextBlock
        {
            Text = step.Description,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(90, 90, 90)),
            Margin = new Thickness(0, 4, 0, 0)
        };
        var status = new TextBlock
        {
            Text = step.Implemented ? "Implemented" : "Roadmap",
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(step.Implemented ? Color.FromRgb(20, 110, 55) : Color.FromRgb(145, 95, 0)),
            VerticalAlignment = VerticalAlignment.Center
        };

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

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            var project = ReadProject();
            if (project.Scenario != BusinessScenario.SalesBi)
                throw new NotSupportedException("Only SalesBi generation is implemented in the first native C# slice.");

            var root = FindRepositoryRoot();
            var output = Path.Combine(root, "generated", "data");
            var cache = Path.Combine(root, "generated", "cache");

            StatusText.Text = $"Generating {project.Scale} {project.RawFormat} data...";
            await _generator.GenerateAsync(project, root, output, cache);
            StatusText.Text = $"Generation complete: {output}";
        });
    }

    private async void Upload_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            var project = ReadProject();
            if (string.IsNullOrWhiteSpace(project.Workspace.WorkspaceName))
                throw new InvalidOperationException("Enter an existing Fabric workspace name before uploading.");

            var root = FindRepositoryRoot();
            var output = Path.Combine(root, "generated", "data");
            var progress = new Progress<string>(message => StatusText.Text = message);
            var uploaded = await _uploader.UploadAsync(project, output, progress);
            StatusText.Text = $"Bronze upload complete: {uploaded.Count} files.";
        });
    }

    private async Task RunBusyAsync(Func<Task> operation)
    {
        SetBusy(true);
        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Contoso Fabric Builder", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        PlanButton.IsEnabled = !busy;
        GenerateButton.IsEnabled = !busy;
        UploadButton.IsEnabled = !busy;
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
