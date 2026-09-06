using System.Text.Json;
using System.Text.Json.Serialization;
using ContosoFabric.Core.Models;
using ContosoFabric.Core.Planning;

namespace ContosoFabric.Core.Projects;

public static class ProjectFileService
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static async Task<FabricProject> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var project = await JsonSerializer.DeserializeAsync<FabricProject>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The project JSON is empty or invalid.");

        // Use the same validation contract as the UI and execution engine.
        PipelinePlanner.Build(project);
        return project;
    }

    public static async Task SaveAsync(
        FabricProject project,
        string path,
        CancellationToken cancellationToken = default)
    {
        PipelinePlanner.Build(project);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await using var stream = File.Create(fullPath);
        await JsonSerializer.SerializeAsync(stream, project, JsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static string Serialize(FabricProject project)
    {
        PipelinePlanner.Build(project);
        return JsonSerializer.Serialize(project, JsonOptions);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
