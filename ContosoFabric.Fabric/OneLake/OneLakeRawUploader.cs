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
        var createdDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        async Task EnsureDirectoryAsync(string remoteDirectory)
        {
            if (!createdDirectories.Add(remoteDirectory))
                return;
            await workspace.GetDirectoryClient(remoteDirectory).CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        }

        await EnsureDirectoryAsync(rawRoot);

        var uploaded = new List<string>(candidates.Length);
        foreach (var localFile in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(localRoot, localFile).Replace('\\', '/');
            var remoteRelative = relative;
            var relativeDirectory = Path.GetDirectoryName(relative)?.Replace('\\', '/');
            var remoteDirectory = string.IsNullOrWhiteSpace(relativeDirectory) ? rawRoot : $"{rawRoot}/{relativeDirectory}";
            await EnsureDirectoryAsync(remoteDirectory);

            progress?.Report($"Uploading raw/{remoteRelative}");
            var remote = workspace.GetFileClient($"{rawRoot}/{remoteRelative}");
            await using var stream = File.OpenRead(localFile);
            await remote.UploadAsync(stream, overwrite: true, cancellationToken: cancellationToken);
            uploaded.Add(remoteRelative);
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
