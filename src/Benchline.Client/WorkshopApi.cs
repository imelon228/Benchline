using System.Net;
using System.Net.Http.Json;
using Benchline.Contracts;

namespace Benchline.Client;

public sealed class WorkshopApi(HttpClient http)
{
    public string? DemoAccess { get; private set; }

    public async Task<ApiReply<SessionDto?>> Session()
    {
        using var response = await http.GetAsync("api/session");
        DemoAccess = response.Headers.TryGetValues(DemoSignIn.Header, out var values) ? values.FirstOrDefault() : null;
        return response.StatusCode == HttpStatusCode.Unauthorized
            ? new ApiReply<SessionDto?>(null)
            : await Read<SessionDto?>(response);
    }

    public Task<ApiReply<BoardDto>> Board() => Get<BoardDto>("api/board");
    public Task<ApiReply<PlanDto>> Preview(Guid station) => Get<PlanDto>($"api/recovery/{station}");
    public Task<ApiReply<AuditDto[]>> Audit(long? before = null) => Get<AuditDto[]>(
        before is null ? "api/audit" : $"api/audit?before={before}");

    public async Task<ApiReply<T>> Send<T>(HttpMethod method, string path, object? body = null)
    {
        var token = await Get<TokenDto>("api/token");
        if (token.Error is { } failure)
            return ApiReply<T>.Failed(failure);

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Workshop-Token", token.Value.Token);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        using var response = await http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NoContent)
            return new ApiReply<T>(default!);
        return await Read<T>(response);
    }

    private async Task<ApiReply<T>> Get<T>(string path)
    {
        using var response = await http.GetAsync(path);
        return await Read<T>(response);
    }

    private static async Task<ApiReply<T>> Read<T>(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return new ApiReply<T>((await response.Content.ReadFromJsonAsync<T>())!);

        var message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Sign in to continue.",
            HttpStatusCode.TooManyRequests => "Too many attempts. Wait a minute and try again.",
            _ => "The request could not be completed."
        };
        if (response.Content.Headers.ContentType?.MediaType == "application/problem+json")
            message = (await response.Content.ReadFromJsonAsync<ApiProblem>())?.Detail ?? message;
        return ApiReply<T>.Failed(new ApiFailure(response.StatusCode, message));
    }

    private sealed record ApiProblem(string? Detail);
}

public sealed record ApiFailure(HttpStatusCode Status, string Message);

public sealed record ApiReply<T>(T Value, ApiFailure? Error = null)
{
    public static ApiReply<T> Failed(ApiFailure error) => new(default!, error);
}
