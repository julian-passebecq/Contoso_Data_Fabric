using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using ContosoFabric.Core.Models;

namespace ContosoFabric.Fabric.Api;

public sealed class FabricRestClient : IDisposable
{
    private static readonly Uri ApiRoot = new("https://api.fabric.microsoft.com/v1/");
    private static readonly string[] FabricScopes = ["https://api.fabric.microsoft.com/.default"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly TokenCredential _credential;
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    public FabricRestClient(TokenCredential? credential = null, HttpClient? httpClient = null)
    {
        _credential = credential ?? new AzureCliCredential();
        _http = httpClient ?? new HttpClient { BaseAddress = ApiRoot, Timeout = Timeout.InfiniteTimeSpan };
        _ownsHttpClient = httpClient is null;
    }

    public async Task<IReadOnlyList<FabricWorkspaceInfo>> ListWorkspacesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<FabricWorkspaceInfo>();
        string? next = "workspaces?roles=Admin,Member,Contributor";

        while (!string.IsNullOrWhiteSpace(next))
        {
            using var response = await SendAsync(HttpMethod.Get, next, null, cancellationToken);
            using var page = await ReadDocumentAsync(response, cancellationToken);
            if (page.RootElement.TryGetProperty("value", out var value))
            {
                foreach (var item in value.EnumerateArray())
                {
                    result.Add(new FabricWorkspaceInfo(
                        item.GetProperty("id").GetString()!,
                        item.GetProperty("displayName").GetString() ?? "(unnamed)",
                        item.TryGetProperty("type", out var type) ? type.GetString() ?? "Workspace" : "Workspace",
                        item.TryGetProperty("capacityId", out var capacity) ? capacity.GetString() : null));
                }
            }
            next = page.RootElement.TryGetProperty("continuationUri", out var uri) ? uri.GetString() : null;
        }

        return result.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<FabricWorkspaceInfo> ResolveWorkspaceAsync(FabricWorkspaceTarget target, CancellationToken cancellationToken = default)
    {
        var workspaces = await ListWorkspacesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(target.WorkspaceId))
        {
            var byId = workspaces.FirstOrDefault(x => x.Id.Equals(target.WorkspaceId, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
                return byId;
            throw new InvalidOperationException($"Workspace ID '{target.WorkspaceId}' is not accessible with the current Azure CLI identity.");
        }

        if (string.IsNullOrWhiteSpace(target.WorkspaceName))
            throw new InvalidOperationException("Choose a Fabric workspace before running a Fabric stage.");

        var matches = workspaces
            .Where(x => x.DisplayName.Equals(target.WorkspaceName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return matches.Length switch
        {
            0 => throw new InvalidOperationException($"Workspace '{target.WorkspaceName}' was not found or is not accessible."),
            1 => matches[0],
            _ => throw new InvalidOperationException($"More than one accessible workspace is named '{target.WorkspaceName}'. Select it from Refresh workspaces so the app can use its ID.")
        };
    }

    public async Task<IReadOnlyList<FabricItemInfo>> ListItemsAsync(string workspaceId, string? type = null, CancellationToken cancellationToken = default)
    {
        var result = new List<FabricItemInfo>();
        var typeQuery = string.IsNullOrWhiteSpace(type) ? string.Empty : $"?type={Uri.EscapeDataString(type)}";
        string? next = $"workspaces/{workspaceId}/items{typeQuery}";

        while (!string.IsNullOrWhiteSpace(next))
        {
            using var response = await SendAsync(HttpMethod.Get, next, null, cancellationToken);
            using var page = await ReadDocumentAsync(response, cancellationToken);
            if (page.RootElement.TryGetProperty("value", out var value))
            {
                foreach (var item in value.EnumerateArray())
                {
                    result.Add(new FabricItemInfo(
                        item.GetProperty("id").GetString()!,
                        item.GetProperty("displayName").GetString() ?? "(unnamed)",
                        item.TryGetProperty("type", out var itemType) ? itemType.GetString() ?? string.Empty : string.Empty,
                        item.TryGetProperty("workspaceId", out var ws) ? ws.GetString() ?? workspaceId : workspaceId));
                }
            }
            next = page.RootElement.TryGetProperty("continuationUri", out var uri) ? uri.GetString() : null;
        }

        return result;
    }

    public async Task<FabricItemInfo> EnsureLakehouseAsync(
        string workspaceId,
        string displayName,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var existing = (await ListItemsAsync(workspaceId, "Lakehouse", cancellationToken))
            .FirstOrDefault(x => x.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            progress?.Report($"Lakehouse exists: {displayName}");
            return existing;
        }

        progress?.Report($"Creating Lakehouse: {displayName}");
        using var response = await SendAsync(
            HttpMethod.Post,
            $"workspaces/{workspaceId}/lakehouses",
            new { displayName, description = "Created by Contoso Fabric Builder" },
            cancellationToken);

        return await ReadCreatedItemAsync(response, cancellationToken);
    }

    public async Task<FabricItemInfo> EnsureNotebookAsync(
        string workspaceId,
        string displayName,
        string notebookJson,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var definition = new
        {
            format = "ipynb",
            parts = new[]
            {
                new
                {
                    path = "notebook-content.ipynb",
                    payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(notebookJson)),
                    payloadType = "InlineBase64"
                }
            }
        };

        var existing = (await ListItemsAsync(workspaceId, "Notebook", cancellationToken))
            .FirstOrDefault(x => x.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            progress?.Report($"Creating notebook: {displayName}");
            using var createResponse = await SendAsync(
                HttpMethod.Post,
                $"workspaces/{workspaceId}/notebooks",
                new { displayName, description = "Generated by Contoso Fabric Builder", definition },
                cancellationToken);
            return await ReadCreatedItemAsync(createResponse, cancellationToken);
        }

        progress?.Report($"Updating notebook: {displayName}");
        using var updateResponse = await SendAsync(
            HttpMethod.Post,
            $"workspaces/{workspaceId}/notebooks/{existing.Id}/updateDefinition",
            new { definition },
            cancellationToken);
        await WaitForLongRunningOperationAsync(updateResponse, expectResult: false, cancellationToken);
        return existing;
    }

    public async Task<FabricJobResult> RunNotebookAndWaitAsync(
        string workspaceId,
        string notebookId,
        string displayName,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report($"Starting notebook: {displayName}");
        using var startResponse = await SendAsync(
            HttpMethod.Post,
            $"workspaces/{workspaceId}/notebooks/{notebookId}/jobs/execute/instances?beta=false",
            new { executionData = new { compute = "Spark" } },
            cancellationToken);

        if (startResponse.StatusCode != HttpStatusCode.Accepted)
            throw new FabricApiException((int)startResponse.StatusCode, $"Notebook '{displayName}' did not return 202 Accepted.");

        var location = startResponse.Headers.Location?.ToString()
            ?? throw new InvalidOperationException($"Fabric did not return a job-instance Location header for '{displayName}'.");
        var initialDelay = GetRetryAfter(startResponse, 10);
        if (initialDelay > TimeSpan.Zero)
            await Task.Delay(initialDelay, cancellationToken);

        var deadline = DateTimeOffset.UtcNow.AddMinutes(60);
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var response = await SendAsync(HttpMethod.Get, location, null, cancellationToken);
            using var document = await ReadDocumentAsync(response, cancellationToken);
            var root = document.RootElement;
            var status = root.TryGetProperty("status", out var statusElement) ? statusElement.GetString() ?? "Unknown" : "Unknown";
            var id = root.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty;
            var activity = root.TryGetProperty("rootActivityId", out var activityElement) ? activityElement.GetString() : null;
            var start = TryGetDateTimeOffset(root, "startTimeUtc");
            var end = TryGetDateTimeOffset(root, "endTimeUtc");

            progress?.Report($"{displayName}: {status}");

            if (status.Equals("Completed", StringComparison.OrdinalIgnoreCase))
                return new FabricJobResult(id, status, activity, start, end);

            if (status.Equals("Failed", StringComparison.OrdinalIgnoreCase)
                || status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)
                || status.Equals("Canceled", StringComparison.OrdinalIgnoreCase))
            {
                var failureReason = root.TryGetProperty("failureReason", out var reason) ? reason.ToString() : null;
                throw new InvalidOperationException($"Notebook '{displayName}' ended with status {status}. {failureReason}".Trim());
            }

            await Task.Delay(GetRetryAfter(response, 10), cancellationToken);
        }

        throw new TimeoutException($"Notebook '{displayName}' did not finish within 60 minutes.");
    }

    private async Task<FabricItemInfo> ReadCreatedItemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.OK)
            return await DeserializeAsync<FabricItemInfo>(response, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Accepted)
            throw new FabricApiException((int)response.StatusCode, "Fabric item creation did not return 201 or 202.");

        var resultResponse = await WaitForLongRunningOperationAsync(response, expectResult: true, cancellationToken);
        using (resultResponse)
            return await DeserializeAsync<FabricItemInfo>(resultResponse, cancellationToken);
    }

    private async Task<HttpResponseMessage> WaitForLongRunningOperationAsync(
        HttpResponseMessage initialResponse,
        bool expectResult,
        CancellationToken cancellationToken)
    {
        if (initialResponse.StatusCode != HttpStatusCode.Accepted)
            return new HttpResponseMessage(HttpStatusCode.OK);

        if (!initialResponse.Headers.TryGetValues("x-ms-operation-id", out var operationValues))
            throw new InvalidOperationException("Fabric returned 202 but no x-ms-operation-id header.");
        var operationId = operationValues.First();
        var deadline = DateTimeOffset.UtcNow.AddMinutes(30);
        var delay = GetRetryAfter(initialResponse, 5);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);

            using var stateResponse = await SendAsync(HttpMethod.Get, $"operations/{operationId}", null, cancellationToken);
            using var document = await ReadDocumentAsync(stateResponse, cancellationToken);
            var status = document.RootElement.TryGetProperty("status", out var state) ? state.GetString() ?? "Unknown" : "Unknown";

            if (status.Equals("Succeeded", StringComparison.OrdinalIgnoreCase))
            {
                if (!expectResult)
                    return new HttpResponseMessage(HttpStatusCode.OK);
                return await SendAsync(HttpMethod.Get, $"operations/{operationId}/result", null, cancellationToken);
            }

            if (status.Equals("Failed", StringComparison.OrdinalIgnoreCase))
            {
                var error = document.RootElement.TryGetProperty("error", out var errorElement) ? errorElement.ToString() : "Unknown Fabric operation error";
                throw new InvalidOperationException($"Fabric operation failed: {error}");
            }

            delay = GetRetryAfter(stateResponse, 5);
        }

        throw new TimeoutException($"Fabric operation {operationId} did not finish within 30 minutes.");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body, CancellationToken cancellationToken)
    {
        var requestUri = Uri.TryCreate(url, UriKind.Absolute, out var absolute) ? absolute : new Uri(ApiRoot, url);
        string? serializedBody = body is null ? null : JsonSerializer.Serialize(body, JsonOptions);

        for (var attempt = 1; attempt <= 6; attempt++)
        {
            var token = await _credential.GetTokenAsync(new TokenRequestContext(FabricScopes), cancellationToken);
            using var request = new HttpRequestMessage(method, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (serializedBody is not null)
                request.Content = new StringContent(serializedBody, System.Text.Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < 6)
            {
                var retryAfter = GetRetryAfter(response, Math.Min(30, attempt * 2));
                response.Dispose();
                await Task.Delay(retryAfter, cancellationToken);
                continue;
            }

            if (response.IsSuccessStatusCode)
                return response;

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new FabricApiException(status, $"Fabric API {method} {requestUri} failed with HTTP {status}. {ExtractErrorMessage(responseBody)}", responseBody);
        }

        throw new InvalidOperationException("Fabric API retry policy exhausted.");
    }

    private static string ExtractErrorMessage(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            if (root.TryGetProperty("message", out var directMessage))
                return directMessage.GetString() ?? responseBody;
            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var nestedMessage))
                    return nestedMessage.GetString() ?? error.ToString();
                return error.ToString();
            }
        }
        catch (JsonException)
        {
        }
        return responseBody;
    }

    private static TimeSpan GetRetryAfter(HttpResponseMessage response, int fallbackSeconds)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta)
            return delta;
        if (response.Headers.RetryAfter?.Date is DateTimeOffset retryDate)
            return retryDate > DateTimeOffset.UtcNow ? retryDate - DateTimeOffset.UtcNow : TimeSpan.Zero;
        return TimeSpan.FromSeconds(fallbackSeconds);
    }

    private static DateTimeOffset? TryGetDateTimeOffset(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value))
            return null;
        return value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), out var parsed) ? parsed : null;
    }

    private static async Task<JsonDocument> ReadDocumentAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException($"Fabric returned an empty {typeof(T).Name} payload.");
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _http.Dispose();
    }
}
