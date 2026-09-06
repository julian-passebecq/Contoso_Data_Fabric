using Azure.Identity;
using Azure.Storage.Files.DataLake;
using ContosoFabric.Core.Models;

namespace ContosoFabric.Fabric.OneLake;

public sealed class OneLakeRawUploader
{
    private static readonly Uri OneLakeEndpoint = new("https://onelake.dfs.fabric.microsoft.com");

    public async Task<IReadOnlyList<string>> UploadAsync(
        FabricProject project,
        string generatedDataFolder,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var workspaceName = project.Workspace.WorkspaceName;
        if (string.IsNullOrWhiteSpace(workspaceName))
            throw new InvalidOperationException("A Fabric workspace name is required for the first OneLake uploader implementation.");

        var localRoot = Path.GetFullPath(generatedDataFolder);
        if (!Directory.Exists(localRoot))
            throw new DirectoryNotFoundException($"Generated data folder does not exist: {localRoot}");

        var credential = new AzureCliCredential();
        var service = new DataLakeServiceClient(OneLakeEndpoint, credential);
        var workspace = service.GetFileSystemClient(workspaceName);
        var rawDirectory = workspace.GetDirectoryClient($"{project.BronzeLakehouse}.Lakehouse/Files/raw");
        await rawDirectory.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var candidates = Directory
            .EnumerateFiles(localRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(IsSupportedRawFile)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidates.Length == 0)
            throw new InvalidOperationException("No CSV or Parquet files were found to upload.");

        var uploaded = new List<string>(candidates.Length);
        foreach (var localFile in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(localFile);
            progress?.Report($"Uploading {fileName}");

            var remote = rawDirectory.GetFileClient(fileName);
            await using var stream = File.OpenRead(localFile);
            await remote.UploadAsync(stream, overwrite: true, cancellationToken: cancellationToken);
            uploaded.Add(fileName);
        }

        progress?.Report($"Uploaded {uploaded.Count} raw files to {project.BronzeLakehouse}.Lakehouse/Files/raw");
        return uploaded;
    }

    private static bool IsSupportedRawFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".parquet", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".csv", StringComparison.OrdinalIgnoreCase);
    }
}
