using Azure.Identity;
using Azure.Storage.Files.DataLake;
using ContosoFabric.Core.Models;

namespace ContosoFabric.Fabric.OneLake;

public sealed class OneLakeRawUploader
{
    private static readonly Uri OneLakeEndpoint = new("https://onelake.dfs.fabric.microsoft.com");

    public Task<IReadOnlyList<string>> UploadAsync(
        FabricProject project,
        string generatedDataFolder,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var workspaceRef = !string.IsNullOrWhiteSpace(project.Workspace.WorkspaceId)
            ? project.Workspace.WorkspaceId!
            : project.Workspace.WorkspaceName;
        if (string.IsNullOrWhiteSpace(workspaceRef))
            throw new InvalidOperationException("A Fabric workspace name or ID is required.");

        return UploadAsync(project.RawFormat, generatedDataFolder, workspaceRef, project.BronzeLakehouse, false, progress, cancellationToken);
    }

    public Task<IReadOnlyList<string>> UploadAsync(
        RawFormat rawFormat,
        string generatedDataFolder,
        string workspaceId,
        string bronzeLakehouseId,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
        => UploadAsync(rawFormat, generatedDataFolder, workspaceId, bronzeLakehouseId, true, progress, cancellationToken);

    private async Task<IReadOnlyList<string>> UploadAsync(
        RawFormat rawFormat,
        string generatedDataFolder,
        string workspaceRef,
        string lakehouseRef,
        bool guidPath,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var localRoot = Path.GetFullPath(generatedDataFolder);
        if (!Directory.Exists(localRoot))
            throw new DirectoryNotFoundException($"Generated data folder does not exist: {localRoot}");

        var candidates = Directory
            .EnumerateFiles(localRoot, "*", SearchOption.AllDirectories)
            .Where(path => IsSupportedRawFile(path, rawFormat))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidates.Length == 0)
            throw new InvalidOperationException($"No generated {rawFormat} data files were found under {localRoot}.");

        var credential = new AzureCliCredential();
        var service = new DataLakeServiceClient(OneLakeEndpoint, credential);
        var workspace = service.GetFileSystemClient(workspaceRef);
        var itemRoot = guidPath ? lakehouseRef : $"{lakehouseRef}.Lakehouse";
        var rawRoot = $"{itemRoot}/Files/raw";

        // The Lakehouse item and its Files container are Fabric-managed. Create the raw
        // directory directly, then create only descendants beneath raw as required for
        // Delta folders such as sales/_delta_log.
        await workspace.GetDirectoryClient(rawRoot)
            .CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var createdRelativeDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        async Task EnsureRelativeDirectoryTreeAsync(string? relativeDirectory)
        {
            if (string.IsNullOrWhiteSpace(relativeDirectory))
                return;

            var segments = relativeDirectory
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries);

            var relativeCurrent = string.Empty;
            foreach (var segment in segments)
            {
                relativeCurrent = string.IsNullOrEmpty(relativeCurrent)
                    ? segment
                    : $"{relativeCurrent}/{segment}";

                if (!createdRelativeDirectories.Add(relativeCurrent))
                    continue;

                await workspace.GetDirectoryClient($"{rawRoot}/{relativeCurrent}")
                    .CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            }
        }

        var uploaded = new List<string>(candidates.Length);
        foreach (var localFile in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(localRoot, localFile).Replace('\\', '/');
            var relativeDirectory = Path.GetDirectoryName(relative)?.Replace('\\', '/');
            await EnsureRelativeDirectoryTreeAsync(relativeDirectory);

            progress?.Report($"Uploading raw/{relative}");
            var remote = workspace.GetFileClient($"{rawRoot}/{relative}");
            await using var stream = File.OpenRead(localFile);
            await remote.UploadAsync(stream, overwrite: true, cancellationToken: cancellationToken);
            uploaded.Add(relative);
        }

        progress?.Report($"Uploaded {uploaded.Count} raw files to OneLake.");
        return uploaded;
    }

    private static bool IsSupportedRawFile(string path, RawFormat rawFormat)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.Equals("_log.log", StringComparison.OrdinalIgnoreCase))
            return false;

        var extension = Path.GetExtension(path);
        return rawFormat switch
        {
            RawFormat.Csv => extension.Equals(".csv", StringComparison.OrdinalIgnoreCase),
            RawFormat.Parquet => extension.Equals(".parquet", StringComparison.OrdinalIgnoreCase),
            RawFormat.Delta => extension.Equals(".parquet", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".json", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}
