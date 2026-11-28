using System.Net.Http.Json;
using System.Text.Json;
using Netora.Application.Auth;
using Netora.Application.Catalog;
using Netora.Application.Common;
using Netora.Application.Customers;
using Netora.Application.Customers.Attachments;
using Netora.Application.Health;
using Netora.Application.Approvals;
using Netora.Application.Operations;
using Netora.Application.Pppoe;
using Netora.Application.Router;
using Netora.Application.Settings;
using Netora.Application.Users;

namespace Netora.WebUI.Services;

/// <summary>The only way the WebUI reaches backend data: HTTP calls to the Web API.</summary>
public interface INetoraApiClient
{
    Task<ApiResponse<DashboardDto>> GetDashboardAsync(CancellationToken cancellationToken);
    Task<ApiResponse<OperationsReportDto>> GetOperationsReportAsync(ReportType type, ReportRange range, CancellationToken ct);
    Task<ApiResponse<IReadOnlyList<NotificationItemDto>>> GetNotificationsAsync(CancellationToken ct);
    Task<ApiResponse<BackupHistoryDto>> GetBackupHistoryAsync(CancellationToken ct);
    Task<ApiResponse<BackupSettingsDto>> GetBackupSettingsAsync(CancellationToken ct);
    Task<ApiResponse<object>> SaveBackupSettingsAsync(UpdateBackupSettingsRequest request, CancellationToken ct);
    Task<ApiResponse<object>> QueueBackupAsync(CancellationToken ct);
    Task<ApiResponse<OnuHistoryDto>> GetOnuHistoryAsync(int? customerId, string? search, int page, CancellationToken cancellationToken);
    Task<ApiResponse<object>> ReplaceOnuAsync(int customerId, ReplaceOnuRequest request, CancellationToken cancellationToken);
    Task<ApiResponse<PppoeDiagnosticsDto>> GetDiagnosticsAsync(int customerId, bool includeLogs, CancellationToken cancellationToken);
    Task<ApiResponse<HealthStatusDto>> GetHealthAsync(CancellationToken cancellationToken);

    Task<ApiResponse<LoginResultDto>> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    Task LogoutAsync(CancellationToken cancellationToken);

    Task<ApiResponse<CustomerListDto>> GetCustomersAsync(CustomerListQuery query, CancellationToken cancellationToken);

    Task<ApiResponse<CustomerFormOptionsDto>> GetCustomerFormOptionsAsync(CancellationToken cancellationToken);

    Task<ApiResponse<CreatedCustomerDto>> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken);
    Task<ApiResponse<CustomerDetailDto>> GetCustomerAsync(int id, CancellationToken cancellationToken);
    Task<ApiResponse<object>> DeleteCustomerAsync(int id, DeleteCustomerRequest request, CancellationToken cancellationToken);
    Task<ApiResponse<UpdatedCustomerDto>> UpdateCustomerAsync(int id, UpdateCustomerRequest request, CancellationToken cancellationToken);
    Task<ApiResponse<AttachmentDto>> UploadAttachmentAsync(int customerId, Stream content, string fileName, CancellationToken cancellationToken);
    Task<ApiResponse<object>> DeleteAttachmentAsync(int customerId, long attachmentId, CancellationToken cancellationToken);

    /// <summary>
    /// Starts streaming a file from the API. On success the caller owns <see cref="ApiFile.Response"/> and must dispose
    /// it after the body has been sent.
    /// </summary>
    Task<ApiFile> DownloadAttachmentAsync(int customerId, long attachmentId, CancellationToken cancellationToken);
    Task<ApiResponse<PppoeListDto>> GetPppoeAccountsAsync(PppoeListQuery query, CancellationToken cancellationToken);
    Task<ApiResponse<PppoeDetailDto>> GetPppoeAccountAsync(int customerId, CancellationToken cancellationToken);
    Task<ApiResponse<DisconnectPppoeResult>> DisconnectPppoeSessionAsync(int customerId, DisconnectPppoeRequest request, CancellationToken ct);
    Task<ApiResponse<PppoePasswordDto>> RevealPppoePasswordAsync(int customerId, CancellationToken cancellationToken);
    Task<ApiResponse<ApprovalRequestDto>> DeletePppoeAccountAsync(int customerId, DeletePppoeRequest request, CancellationToken cancellationToken);
    Task<ApiResponse<PppoeChangedDto>> UpdatePppoeAccountAsync(int customerId, UpdatePppoeRequest request, CancellationToken cancellationToken);
    Task<ApiResponse<ApprovalRequestDto>> SubmitPppoeRequestAsync(int customerId, SubmitPppoeRequest request, CancellationToken cancellationToken);
    Task<ApiResponse<ApprovalRequestDto>> DecidePppoeRequestAsync(int customerId, long requestId, DecidePppoeRequest decision, CancellationToken cancellationToken);
    Task<ApiResponse<ApprovalListDto>> GetApprovalsAsync(ApprovalListQuery query, CancellationToken cancellationToken);
    Task<ApiResponse<ApprovalDetailDto>> GetApprovalAsync(long requestId, CancellationToken cancellationToken);
    Task<ApiResponse<ApprovalRequestDto>> DecideApprovalAsync(long requestId, DecidePppoeRequest decision, CancellationToken cancellationToken);
    Task<ApiResponse<UserListDto>> GetUsersAsync(CancellationToken cancellationToken);
    Task<ApiResponse<UserDetailDto>> GetUserAsync(string id, CancellationToken cancellationToken);
    Task<ApiResponse<UserChangedDto>> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken);
    Task<ApiResponse<UserChangedDto>> UpdateUserAsync(string id, UpdateUserRequest request, CancellationToken cancellationToken);
    Task<ApiResponse<UserChangedDto>> DeleteUserAsync(string id, DeleteUserRequest request, CancellationToken cancellationToken);
    Task<ApiResponse<UserChangedDto>> ResetUserPasswordAsync(string id, ResetPasswordRequest request, CancellationToken cancellationToken);
    Task<ApiResponse<ProfileDto>> GetProfileAsync(CancellationToken cancellationToken);
    Task<ApiResponse<ChangedDto>> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken cancellationToken);
    Task<ApiResponse<ChangedDto>> SetThemeAsync(ThemeRequest request, CancellationToken cancellationToken);
    Task<ApiResponse<LoginResultDto>> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken);
    Task<ApiResponse<SettingsDto>> GetSettingsAsync(CancellationToken cancellationToken);
    Task<ApiResponse<SavedDto>> UpdateRouterSettingsAsync(UpdateRouterSettingsRequest request, CancellationToken cancellationToken);
    Task<ApiResponse<SavedDto>> UpdateEmailSettingsAsync(UpdateEmailSettingsRequest request, CancellationToken cancellationToken);
    Task<ApiResponse<TestEmailResultDto>> SendTestEmailAsync(CancellationToken cancellationToken);
    Task<ApiResponse<RouterTestResultDto>> TestRouterAsync(CancellationToken cancellationToken);
    Task<ApiResponse<ProvisioningResult>> RetryApprovalAsync(long requestId, CancellationToken cancellationToken);
    Task<ApiResponse<RouterStatusDto>> GetRouterStatusAsync(int customerId, CancellationToken cancellationToken);
    Task<ApiResponse<ImportScanDto>> ScanImportAsync(CancellationToken cancellationToken);
    Task<ApiResponse<ImportResultDto>> ImportPppoeAsync(ImportRequest request, CancellationToken cancellationToken);
    Task<ApiResponse<PlanListDto>> GetPlansAsync(CancellationToken cancellationToken, string? sortBy = null, bool descending = false);
    Task<ApiResponse<PlanDetailDto>> GetPlanAsync(int id, CancellationToken cancellationToken);
    Task<ApiResponse<CatalogSavedDto>> CreatePlanAsync(SavePlanRequest request, CancellationToken cancellationToken);
    Task<ApiResponse<CatalogSavedDto>> UpdatePlanAsync(int id, SavePlanRequest request, CancellationToken cancellationToken);
    Task<ApiResponse<PlanProfileCheckDto>> CheckPlanProfilesAsync(CancellationToken cancellationToken);
    Task<ApiResponse<PlanSentDto>> SendPlanToRouterAsync(int id, CancellationToken cancellationToken);
    Task<ApiResponse<NapListDto>> GetNapBoxesAsync(CancellationToken cancellationToken);
    Task<ApiResponse<NapDetailDto>> GetNapBoxAsync(int id, CancellationToken cancellationToken);
    Task<ApiResponse<CatalogSavedDto>> CreateNapBoxAsync(SaveNapRequest request, CancellationToken cancellationToken);
    Task<ApiResponse<CatalogSavedDto>> UpdateNapBoxAsync(int id, SaveNapRequest request, CancellationToken cancellationToken);
    Task<ApiResponse<UpdatedCustomerDto>> MoveCustomerNapAsync(int customerId, MoveNapRequest request, CancellationToken cancellationToken);
    Task<ApiResponse<Netora.Application.Audit.AuditListDto>> GetAuditAsync(Netora.Application.Audit.AuditQuery query, CancellationToken cancellationToken);
}

public sealed record PppoeChangedDto(bool Changed);

public sealed record UserChangedDto(string Id, bool Changed);

public sealed record ChangedDto(bool Changed);

public sealed record SavedDto(bool Saved);

/// <summary>A streamed file from the API, or the error code when there is none.</summary>
public sealed record ApiFile(HttpResponseMessage? Response, string? ErrorCode);

public sealed class NetoraApiClient(HttpClient httpClient, ILogger<NetoraApiClient> logger, IHttpClientFactory clientFactory) : INetoraApiClient
{
    internal const string EmailTestClient = "NetoraEmailTest";

    public Task<ApiResponse<DashboardDto>> GetDashboardAsync(CancellationToken cancellationToken)
        => SendAsync<DashboardDto>(HttpMethod.Get, "api/v1/dashboard", null, cancellationToken);

    public Task<ApiResponse<OperationsReportDto>> GetOperationsReportAsync(ReportType type, ReportRange range, CancellationToken ct)
        => SendAsync<OperationsReportDto>(HttpMethod.Get, $"api/v1/reports?type={type}&range={range}", null, ct);

    public Task<ApiResponse<IReadOnlyList<NotificationItemDto>>> GetNotificationsAsync(CancellationToken ct)
        => SendAsync<IReadOnlyList<NotificationItemDto>>(HttpMethod.Get, "api/v1/notifications", null, ct);

    public Task<ApiResponse<BackupHistoryDto>> GetBackupHistoryAsync(CancellationToken ct)
        => SendAsync<BackupHistoryDto>(HttpMethod.Get, "api/v1/backups", null, ct);

    public Task<ApiResponse<BackupSettingsDto>> GetBackupSettingsAsync(CancellationToken ct)
        => SendAsync<BackupSettingsDto>(HttpMethod.Get, "api/v1/backups/settings", null, ct);

    public Task<ApiResponse<object>> SaveBackupSettingsAsync(UpdateBackupSettingsRequest request, CancellationToken ct)
        => SendAsync<object>(HttpMethod.Put, "api/v1/backups/settings", request, ct);

    public Task<ApiResponse<object>> QueueBackupAsync(CancellationToken ct)
        => SendAsync<object>(HttpMethod.Post, "api/v1/backups/run", null, ct);

    public Task<ApiResponse<OnuHistoryDto>> GetOnuHistoryAsync(int? customerId, string? search, int page, CancellationToken cancellationToken)
        => SendAsync<OnuHistoryDto>(HttpMethod.Get,
            $"api/v1/onu-history?customerId={customerId}&page={page}&search={Uri.EscapeDataString(search ?? string.Empty)}",
            null, cancellationToken);

    public Task<ApiResponse<object>> ReplaceOnuAsync(int customerId, ReplaceOnuRequest request, CancellationToken cancellationToken)
        => SendAsync<object>(HttpMethod.Post, $"api/v1/onu-history/{customerId}/replacements", request, cancellationToken);

    public Task<ApiResponse<PppoeDiagnosticsDto>> GetDiagnosticsAsync(int customerId, bool includeLogs, CancellationToken cancellationToken)
        => SendAsync<PppoeDiagnosticsDto>(HttpMethod.Get,
            $"api/v1/customers/{customerId}/diagnostics?includeLogs={(includeLogs ? "true" : "false")}", null, cancellationToken);
    public Task<ApiResponse<HealthStatusDto>> GetHealthAsync(CancellationToken cancellationToken)
        => SendAsync<HealthStatusDto>(HttpMethod.Get, "api/v1/health", null, cancellationToken);

    public Task<ApiResponse<object>> DeleteCustomerAsync(int id, DeleteCustomerRequest request, CancellationToken cancellationToken)
        => SendAsync<object>(HttpMethod.Delete, $"api/v1/customers/{id}", request, cancellationToken);

    public Task<ApiResponse<ApprovalRequestDto>> DeletePppoeAccountAsync(int customerId, DeletePppoeRequest request, CancellationToken cancellationToken)
        => SendAsync<ApprovalRequestDto>(HttpMethod.Delete, $"api/v1/pppoe/{customerId}", request, cancellationToken);

    public Task<ApiResponse<LoginResultDto>> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
        => SendAsync<LoginResultDto>(HttpMethod.Post, "api/v1/auth/login", request, cancellationToken);

    public async Task LogoutAsync(CancellationToken cancellationToken)
        => await SendAsync<object>(HttpMethod.Post, "api/v1/auth/logout", null, cancellationToken);

    public Task<ApiResponse<CustomerFormOptionsDto>> GetCustomerFormOptionsAsync(CancellationToken cancellationToken)
        => SendAsync<CustomerFormOptionsDto>(HttpMethod.Get, "api/v1/customers/form-options", null, cancellationToken);

    public Task<ApiResponse<CreatedCustomerDto>> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken)
        => SendAsync<CreatedCustomerDto>(HttpMethod.Post, "api/v1/customers", request, cancellationToken);

    public Task<ApiResponse<CustomerDetailDto>> GetCustomerAsync(int id, CancellationToken cancellationToken)
        => SendAsync<CustomerDetailDto>(HttpMethod.Get, $"api/v1/customers/{id}", null, cancellationToken);

    public Task<ApiResponse<UpdatedCustomerDto>> UpdateCustomerAsync(int id, UpdateCustomerRequest request, CancellationToken cancellationToken)
        => SendAsync<UpdatedCustomerDto>(HttpMethod.Put, $"api/v1/customers/{id}", request, cancellationToken);

    public Task<ApiResponse<AttachmentDto>> UploadAttachmentAsync(int customerId, Stream content, string fileName,
        CancellationToken cancellationToken)
    {
        // The API detects the real type from the file content; the content type sent here is not trusted.
        var form = new MultipartFormDataContent();
        var file = new StreamContent(content);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        return SendAsync<AttachmentDto>(HttpMethod.Post, $"api/v1/customers/{customerId}/attachments", form, cancellationToken);
    }

    public Task<ApiResponse<object>> DeleteAttachmentAsync(int customerId, long attachmentId, CancellationToken cancellationToken)
        => SendAsync<object>(HttpMethod.Delete, $"api/v1/customers/{customerId}/attachments/{attachmentId}", null, cancellationToken);

    public async Task<ApiFile> DownloadAttachmentAsync(int customerId, long attachmentId, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/customers/{customerId}/attachments/{attachmentId}");
            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode) return new ApiFile(response, null);

            var code = response.StatusCode == System.Net.HttpStatusCode.NotFound ? "not_found" : "download_failed";
            response.Dispose();
            return new ApiFile(null, code);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Web API attachment download failed");
            return new ApiFile(null, "api_unreachable");
        }
    }

    public Task<ApiResponse<PppoeListDto>> GetPppoeAccountsAsync(PppoeListQuery query, CancellationToken cancellationToken)
    {
        var parts = new List<string> { $"page={query.Page}", $"pageSize={query.PageSize}" };
        if (!string.IsNullOrWhiteSpace(query.Search)) parts.Add("search=" + Uri.EscapeDataString(query.Search));
        if (query.State is not null) parts.Add("state=" + Uri.EscapeDataString(query.State));
        if (query.Session is { } session) parts.Add("session=" + Uri.EscapeDataString(session));
        if (query.SortBy is not null) { parts.Add("sortBy=" + query.SortBy); parts.Add("sortDir=" + (query.Descending ? "desc" : "asc")); }
        return SendAsync<PppoeListDto>(HttpMethod.Get, "api/v1/pppoe?" + string.Join("&", parts), null, cancellationToken);
    }

    public Task<ApiResponse<PppoeDetailDto>> GetPppoeAccountAsync(int customerId, CancellationToken cancellationToken)
        => SendAsync<PppoeDetailDto>(HttpMethod.Get, $"api/v1/pppoe/{customerId}", null, cancellationToken);

    public Task<ApiResponse<DisconnectPppoeResult>> DisconnectPppoeSessionAsync(int customerId, DisconnectPppoeRequest request, CancellationToken ct)
        => SendAsync<DisconnectPppoeResult>(HttpMethod.Post, $"api/v1/pppoe/{customerId}/disconnect", request, ct);

    public Task<ApiResponse<PppoePasswordDto>> RevealPppoePasswordAsync(int customerId, CancellationToken cancellationToken)
        => SendAsync<PppoePasswordDto>(HttpMethod.Post, $"api/v1/pppoe/{customerId}/password", null, cancellationToken);

    public Task<ApiResponse<PppoeChangedDto>> UpdatePppoeAccountAsync(int customerId, UpdatePppoeRequest request, CancellationToken cancellationToken)
        => SendAsync<PppoeChangedDto>(HttpMethod.Put, $"api/v1/pppoe/{customerId}", request, cancellationToken);

    public Task<ApiResponse<ApprovalRequestDto>> SubmitPppoeRequestAsync(int customerId, SubmitPppoeRequest request, CancellationToken cancellationToken)
        => SendAsync<ApprovalRequestDto>(HttpMethod.Post, $"api/v1/pppoe/{customerId}/requests", request, cancellationToken);

    public Task<ApiResponse<ApprovalRequestDto>> DecidePppoeRequestAsync(int customerId, long requestId, DecidePppoeRequest decision, CancellationToken cancellationToken)
        => SendAsync<ApprovalRequestDto>(HttpMethod.Post, $"api/v1/pppoe/{customerId}/requests/{requestId}/decision", decision, cancellationToken);

    public Task<ApiResponse<ApprovalListDto>> GetApprovalsAsync(ApprovalListQuery query, CancellationToken cancellationToken)
    {
        var parts = new List<string> { $"page={query.Page}", $"pageSize={query.PageSize}" };
        if (query.Status is not null) parts.Add("status=" + Uri.EscapeDataString(query.Status));
        if (query.Operation is not null) parts.Add("operation=" + Uri.EscapeDataString(query.Operation));
        if (query.Search is not null) parts.Add("search=" + Uri.EscapeDataString(query.Search));
        return SendAsync<ApprovalListDto>(HttpMethod.Get, "api/v1/approvals?" + string.Join("&", parts), null, cancellationToken);
    }

    public Task<ApiResponse<ApprovalDetailDto>> GetApprovalAsync(long requestId, CancellationToken cancellationToken)
        => SendAsync<ApprovalDetailDto>(HttpMethod.Get, $"api/v1/approvals/{requestId}", null, cancellationToken);

    public Task<ApiResponse<ApprovalRequestDto>> DecideApprovalAsync(long requestId, DecidePppoeRequest decision, CancellationToken cancellationToken)
        => SendAsync<ApprovalRequestDto>(HttpMethod.Post, $"api/v1/approvals/{requestId}/decision", decision, cancellationToken);

    public Task<ApiResponse<UserListDto>> GetUsersAsync(CancellationToken cancellationToken)
        => SendAsync<UserListDto>(HttpMethod.Get, "api/v1/users", null, cancellationToken);

    public Task<ApiResponse<UserDetailDto>> GetUserAsync(string id, CancellationToken cancellationToken)
        => SendAsync<UserDetailDto>(HttpMethod.Get, $"api/v1/users/{Uri.EscapeDataString(id)}", null, cancellationToken);

    public Task<ApiResponse<UserChangedDto>> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken)
        => SendAsync<UserChangedDto>(HttpMethod.Post, "api/v1/users", request, cancellationToken);

    public Task<ApiResponse<UserChangedDto>> UpdateUserAsync(string id, UpdateUserRequest request, CancellationToken cancellationToken)
        => SendAsync<UserChangedDto>(HttpMethod.Put, $"api/v1/users/{Uri.EscapeDataString(id)}", request, cancellationToken);

    public Task<ApiResponse<UserChangedDto>> DeleteUserAsync(string id, DeleteUserRequest request, CancellationToken cancellationToken)
        => SendAsync<UserChangedDto>(HttpMethod.Delete, $"api/v1/users/{Uri.EscapeDataString(id)}", request, cancellationToken);

    public Task<ApiResponse<UserChangedDto>> ResetUserPasswordAsync(string id, ResetPasswordRequest request, CancellationToken cancellationToken)
        => SendAsync<UserChangedDto>(HttpMethod.Post, $"api/v1/users/{Uri.EscapeDataString(id)}/reset-password", request, cancellationToken);

    public Task<ApiResponse<ProfileDto>> GetProfileAsync(CancellationToken cancellationToken)
        => SendAsync<ProfileDto>(HttpMethod.Get, "api/v1/profile", null, cancellationToken);

    public Task<ApiResponse<ChangedDto>> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken cancellationToken)
        => SendAsync<ChangedDto>(HttpMethod.Put, "api/v1/profile", request, cancellationToken);

    public Task<ApiResponse<ChangedDto>> SetThemeAsync(ThemeRequest request, CancellationToken cancellationToken)
        => SendAsync<ChangedDto>(HttpMethod.Put, "api/v1/profile/theme", request, cancellationToken);

    public Task<ApiResponse<LoginResultDto>> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken)
        => SendAsync<LoginResultDto>(HttpMethod.Post, "api/v1/profile/password", request, cancellationToken);

    public Task<ApiResponse<SettingsDto>> GetSettingsAsync(CancellationToken cancellationToken)
        => SendAsync<SettingsDto>(HttpMethod.Get, "api/v1/settings", null, cancellationToken);

    public Task<ApiResponse<SavedDto>> UpdateRouterSettingsAsync(UpdateRouterSettingsRequest request, CancellationToken cancellationToken)
        => SendAsync<SavedDto>(HttpMethod.Put, "api/v1/settings/router", request, cancellationToken);

    public Task<ApiResponse<SavedDto>> UpdateEmailSettingsAsync(UpdateEmailSettingsRequest request, CancellationToken cancellationToken)
        => SendAsync<SavedDto>(HttpMethod.Put, "api/v1/settings/email", request, cancellationToken);

    public async Task<ApiResponse<TestEmailResultDto>> SendTestEmailAsync(CancellationToken cancellationToken)
    {
        using var client = clientFactory.CreateClient(EmailTestClient);
        return await SendAsync<TestEmailResultDto>(HttpMethod.Post, "api/v1/settings/email/test", null, cancellationToken, client);
    }

    public Task<ApiResponse<RouterTestResultDto>> TestRouterAsync(CancellationToken cancellationToken)
        => SendAsync<RouterTestResultDto>(HttpMethod.Post, "api/v1/settings/router/test", null, cancellationToken);

    public Task<ApiResponse<ProvisioningResult>> RetryApprovalAsync(long requestId, CancellationToken cancellationToken)
        => SendAsync<ProvisioningResult>(HttpMethod.Post, $"api/v1/approvals/{requestId}/retry", null, cancellationToken);

    public Task<ApiResponse<RouterStatusDto>> GetRouterStatusAsync(int customerId, CancellationToken cancellationToken)
        => SendAsync<RouterStatusDto>(HttpMethod.Get, $"api/v1/customers/{customerId}/router-status", null, cancellationToken);

    public Task<ApiResponse<ImportScanDto>> ScanImportAsync(CancellationToken cancellationToken)
        => SendAsync<ImportScanDto>(HttpMethod.Get, "api/v1/pppoe/import/scan", null, cancellationToken);

    public Task<ApiResponse<ImportResultDto>> ImportPppoeAsync(ImportRequest request, CancellationToken cancellationToken)
        => SendAsync<ImportResultDto>(HttpMethod.Post, "api/v1/pppoe/import", request, cancellationToken);
    public Task<ApiResponse<PlanListDto>> GetPlansAsync(CancellationToken cancellationToken, string? sortBy = null, bool descending = false)
        => SendAsync<PlanListDto>(HttpMethod.Get,
            sortBy is null ? "api/v1/plans" : $"api/v1/plans?sortBy={Uri.EscapeDataString(sortBy)}&sortDir={(descending ? "desc" : "asc")}",
            null, cancellationToken);

    public Task<ApiResponse<PlanDetailDto>> GetPlanAsync(int id, CancellationToken cancellationToken)
        => SendAsync<PlanDetailDto>(HttpMethod.Get, $"api/v1/plans/{id}", null, cancellationToken);

    public Task<ApiResponse<CatalogSavedDto>> CreatePlanAsync(SavePlanRequest request, CancellationToken cancellationToken)
        => SendAsync<CatalogSavedDto>(HttpMethod.Post, "api/v1/plans", request, cancellationToken);

    public Task<ApiResponse<CatalogSavedDto>> UpdatePlanAsync(int id, SavePlanRequest request, CancellationToken cancellationToken)
        => SendAsync<CatalogSavedDto>(HttpMethod.Put, $"api/v1/plans/{id}", request, cancellationToken);

    public Task<ApiResponse<PlanProfileCheckDto>> CheckPlanProfilesAsync(CancellationToken cancellationToken)
        => SendAsync<PlanProfileCheckDto>(HttpMethod.Get, "api/v1/plans/router-check", null, cancellationToken);

    public Task<ApiResponse<PlanSentDto>> SendPlanToRouterAsync(int id, CancellationToken cancellationToken)
        => SendAsync<PlanSentDto>(HttpMethod.Post, $"api/v1/plans/{id}/send-to-router", null, cancellationToken);

    public Task<ApiResponse<NapListDto>> GetNapBoxesAsync(CancellationToken cancellationToken)
        => SendAsync<NapListDto>(HttpMethod.Get, "api/v1/nap-boxes", null, cancellationToken);

    public Task<ApiResponse<NapDetailDto>> GetNapBoxAsync(int id, CancellationToken cancellationToken)
        => SendAsync<NapDetailDto>(HttpMethod.Get, $"api/v1/nap-boxes/{id}", null, cancellationToken);

    public Task<ApiResponse<CatalogSavedDto>> CreateNapBoxAsync(SaveNapRequest request, CancellationToken cancellationToken)
        => SendAsync<CatalogSavedDto>(HttpMethod.Post, "api/v1/nap-boxes", request, cancellationToken);

    public Task<ApiResponse<CatalogSavedDto>> UpdateNapBoxAsync(int id, SaveNapRequest request, CancellationToken cancellationToken)
        => SendAsync<CatalogSavedDto>(HttpMethod.Put, $"api/v1/nap-boxes/{id}", request, cancellationToken);

    public Task<ApiResponse<UpdatedCustomerDto>> MoveCustomerNapAsync(int customerId, MoveNapRequest request, CancellationToken cancellationToken)
        => SendAsync<UpdatedCustomerDto>(HttpMethod.Put, $"api/v1/customers/{customerId}/nap", request, cancellationToken);

    public Task<ApiResponse<Netora.Application.Audit.AuditListDto>> GetAuditAsync(Netora.Application.Audit.AuditQuery query, CancellationToken cancellationToken)
    {
        var parts = new List<string> { $"page={query.Page}", $"pageSize={query.PageSize}" };
        if (query.From is { } f) parts.Add("from=" + f.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        if (query.To is { } t) parts.Add("to=" + t.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(query.Category)) parts.Add("category=" + Uri.EscapeDataString(query.Category));
        if (!string.IsNullOrWhiteSpace(query.Actor)) parts.Add("actor=" + Uri.EscapeDataString(query.Actor));
        if (!string.IsNullOrWhiteSpace(query.Search)) parts.Add("search=" + Uri.EscapeDataString(query.Search));
        return SendAsync<Netora.Application.Audit.AuditListDto>(HttpMethod.Get, "api/v1/audit?" + string.Join("&", parts), null, cancellationToken);
    }

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
        Add("sortBy", query.SortBy);
        Add("sortDir", query.SortBy is null ? null : query.SortDir);
        return SendAsync<CustomerListDto>(HttpMethod.Get, "api/v1/customers?" + string.Join("&", parts), null, cancellationToken);
    }

    private async Task<ApiResponse<T>> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken,
        HttpClient? client = null)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            request.Content = body as HttpContent ?? (body is null ? null : JsonContent.Create(body));

            using var response = await (client ?? httpClient).SendAsync(request, cancellationToken);

            // Some failures come from the server itself (for example 413 from Kestrel) and have no JSON envelope.
            if (response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
            {
                return response.StatusCode == System.Net.HttpStatusCode.RequestEntityTooLarge
                    ? ApiResponse<T>.Fail("file_too_large", "The file is larger than the upload limit.")
                    : ApiResponse<T>.Fail("invalid_response", $"The API returned HTTP {(int)response.StatusCode}.");
            }
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
