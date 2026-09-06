namespace ContosoFabric.Fabric.Api;

public sealed record FabricWorkspaceInfo(
    string Id,
    string DisplayName,
    string Type,
    string? CapacityId = null)
{
    public override string ToString() => DisplayName;
}

public sealed record FabricItemInfo(
    string Id,
    string DisplayName,
    string Type,
    string WorkspaceId);

public sealed record FabricJobResult(
    string JobInstanceId,
    string Status,
    string? RootActivityId,
    DateTimeOffset? StartTimeUtc,
    DateTimeOffset? EndTimeUtc);

public sealed class FabricApiException : Exception
{
    public int StatusCode { get; }
    public string? ResponseBody { get; }

    public FabricApiException(int statusCode, string message, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
