using System.Text.Json;
using System.Text.RegularExpressions;
using ContosoFabric.Core.Models;
using ContosoFabric.Core.Planning;

namespace ContosoFabric.Core.Generation;

public sealed record GenerationManifest(
    string SchemaVersion,
    string ProjectName,
    DateTimeOffset GeneratedAtUtc,
    int RequestedOrders,
    long ActualOrders,
    long ActualOrderRows,
    DateTime StartDate,
    int Years,
    RawFormat RawFormat,
    int EffectiveSeed,
    IReadOnlyList<string> ExpectedTables)
{
    public const string FileName = "truth_manifest.json";

    private static readonly string[] Tables =
    [
        "customer", "store", "product", "date", "currencyexchange", "sales", "orders", "orderrows"
    ];

    public static async Task<GenerationManifest> FromGeneratorLogAsync(
        FabricProject project,
        string outputFolder,
        CancellationToken cancellationToken = default)
    {
        var logPath = Path.Combine(outputFolder, "_log.log");
        if (!File.Exists(logPath))
            throw new FileNotFoundException("The Contoso generator completed without its expected _log.log output.", logPath);

        var log = await File.ReadAllTextAsync(logPath, cancellationToken);
        var actualOrders = ParseLastCounter(log, "Orders");
        var actualOrderRows = ParseLastCounter(log, "OrdersRows");
        var requestedOrders = PipelinePlanner.Build(project).OrdersCount;

        if (actualOrders <= 0)
            throw new InvalidOperationException("The generator truth manifest cannot be created because the final actual order count is zero or missing.");
        if (actualOrderRows <= 0)
            throw new InvalidOperationException("The generator truth manifest cannot be created because the final actual order-row count is zero or missing.");

        return new GenerationManifest(
            SchemaVersion: "1.0",
            ProjectName: project.Name,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            RequestedOrders: requestedOrders,
            ActualOrders: actualOrders,
            ActualOrderRows: actualOrderRows,
            StartDate: project.EffectiveStartDate,
            Years: project.Years,
            RawFormat: project.RawFormat,
            EffectiveSeed: 0,
            ExpectedTables: Tables);
    }

    public async Task<string> SaveAsync(string outputFolder, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(outputFolder, FileName);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(this, options), cancellationToken);
        return path;
    }

    internal static long ParseLastCounter(string log, string label)
    {
        // DatabaseGenerator.Logger formats every payload as:
        // yyyyMMdd HH:mm:ss | <elapsed> > <message>
        // Anchor after '>' so "Online orders:" can never be parsed as "Orders:".
        var pattern = $@"(?im)^.*>\s*{Regex.Escape(label)}\s*:\s*(?<value>[0-9][0-9,._ ]*)\s*$";
        var matches = Regex.Matches(log, pattern);
        if (matches.Count == 0)
            return 0;

        var raw = matches[matches.Count - 1].Groups["value"].Value;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return long.TryParse(digits, out var value) ? value : 0;
    }
}
