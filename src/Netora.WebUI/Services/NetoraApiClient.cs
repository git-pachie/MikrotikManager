using System.Net.Http.Json;
using System.Text.Json;
using Netora.Application.Auth;
using Netora.Application.Common;
using Netora.Application.Customers;
using Netora.Application.Health;

namespace Netora.WebUI.Services;

/// <summary>The only way the WebUI reaches backend data: HTTP calls to the Web API.</summary>
public interface INetoraApiClient
{
    Task<ApiResponse<HealthStatusDto>> GetHealthAsync(CancellationToken cancellationToken);

    Task<ApiResponse<LoginResultDto>> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    Task LogoutAsync(CancellationToken cancellationToken);

    Task<ApiResponse<CustomerListDto>> GetCustomersAsync(CustomerListQuery query, CancellationToken cancellationToken);

    Task<ApiResponse<CustomerFormOptionsDto>> GetCustomerFormOptionsAsync(CancellationToken cancellationToken);

    Task<ApiResponse<CreatedCustomerDto>> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken);
}

public sealed class NetoraApiClient(HttpClient httpClient, ILogger<NetoraApiClient> logger) : INetoraApiClient
{
    public Task<ApiResponse<HealthStatusDto>> GetHealthAsync(CancellationToken cancellationToken)
        => SendAsync<HealthStatusDto>(HttpMethod.Get, "api/v1/health", null, cancellationToken);

    public Task<ApiResponse<LoginResultDto>> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
        => SendAsync<LoginResultDto>(HttpMethod.Post, "api/v1/auth/login", request, cancellationToken);

    public async Task LogoutAsync(CancellationToken cancellationToken)
        => await SendAsync<object>(HttpMethod.Post, "api/v1/auth/logout", null, cancellationToken);

    public Task<ApiResponse<CustomerFormOptionsDto>> GetCustomerFormOptionsAsync(CancellationToken cancellationToken)
        => SendAsync<CustomerFormOptionsDto>(HttpMethod.Get, "api/v1/customers/form-options", null, cancellationToken);

    public Task<ApiResponse<CreatedCustomerDto>> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken)
        => SendAsync<CreatedCustomerDto>(HttpMethod.Post, "api/v1/customers", request, cancellationToken);

    public Task<ApiResponse<CustomerListDto>> GetCustomersAsync(CustomerListQuery query, CancellationToken cancellationToken)
    {
        var parts = new List<string> { $"page={query.Page}", $"pageSize={query.PageSize}" };
        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) parts.Add($"{key}={Uri.EscapeDataString(value)}");
        }
        Add("search", query.Search);
        Add("status", query.Status);
        Add("nap", query.Nap);
        Add("plan", query.Plan);
        return SendAsync<CustomerListDto>(HttpMethod.Get, "api/v1/customers?" + string.Join("&", parts), null, cancellationToken);
    }

    private async Task<ApiResponse<T>> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<T>>(cancellationToken);
            return envelope ?? ApiResponse<T>.Fail("invalid_response", "The API returned an empty response.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            logger.LogWarning(ex, "Web API call {Method} {Path} failed", method, path);
            return ApiResponse<T>.Fail("api_unreachable", "The Web API could not be reached.");
        }
    }
}
