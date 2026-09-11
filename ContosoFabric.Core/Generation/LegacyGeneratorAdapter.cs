using System.Text.Json;
using ContosoFabric.Core.Models;
using ContosoFabric.Core.Planning;
using DatabaseGenerator;

namespace ContosoFabric.Core.Generation;

public sealed class LegacyGeneratorAdapter
{
    // The upstream engine uses a process-wide logger. Keep runs serialized,
    // including their manifest reads, even across different adapter instances.
    private static readonly SemaphoreSlim GenerationGate = new(1, 1);

    public async Task GenerateAsync(
        FabricProject project,
        string repositoryRoot,
        string outputFolder,
        string cacheFolder,
        CancellationToken cancellationToken = default)
    {
        await GenerationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Engine preparation includes synchronous I/O and CPU work. Never
            // execute it on the WPF synchronization context. Cancellation waits
            // for the active engine to finish; it must not release the gate early.
            await Task.Run(() => GenerateCoreAsync(project, repositoryRoot, outputFolder,
                cacheFolder, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            GenerationGate.Release();
        }
    }

    private static async Task GenerateCoreAsync(
        FabricProject project,
        string repositoryRoot,
        string outputFolder,
        string cacheFolder,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var root = Path.GetFullPath(repositoryRoot);
        var configPath = Path.Combine(root, "_test_data", "IN", "config_test.json");
        var dataPath = Path.Combine(root, "_test_data", "IN", "data_test.xlsx");

        if (!File.Exists(configPath))
            throw new FileNotFoundException("The baseline Contoso generator config could not be found.", configPath);
        if (!File.Exists(dataPath))
            throw new FileNotFoundException("The baseline Contoso generator workbook could not be found.", dataPath);

        var config = JsonSerializer.Deserialize<Config>(
            await File.ReadAllTextAsync(configPath, cancellationToken),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Unable to deserialize the legacy Contoso configuration.");

        var plan = PipelinePlanner.Build(project);
        config.OrdersCount = plan.OrdersCount;
        config.StartDT = DateTime.SpecifyKind(project.EffectiveStartDate.Date, DateTimeKind.Utc);
        config.YearsCount = project.Years;
        config.OutputFormat = project.RawFormat switch
        {
            RawFormat.Parquet => "PARQUET",
            RawFormat.Csv => "CSV",
            RawFormat.Delta => "DELTATABLE",
            _ => throw new ArgumentOutOfRangeException(nameof(project.RawFormat))
        };
        config.SalesOrders = "BOTH";
        config.SalesOrdersOut = new SOOutput(config.SalesOrders);

        Directory.CreateDirectory(outputFolder);
        Directory.CreateDirectory(cacheFolder);

        var engine = new Engine(dataPath, outputFolder, cacheFolder, config);
        await engine.Exec();

        cancellationToken.ThrowIfCancellationRequested();
        var manifest = await GenerationManifest.FromGeneratorLogAsync(project, outputFolder, cancellationToken);
        await manifest.SaveAsync(outputFolder, cancellationToken);
    }
}
