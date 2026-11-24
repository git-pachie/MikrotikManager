namespace Netora.Application.Auth;

public interface IAuthService
{
    Task<LoginResult> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken);

    Task<CurrentUserDto?> GetCurrentUserAsync(string userId, CancellationToken cancellationToken);

    Task RecordLogoutAsync(string userId, string? ipAddress, CancellationToken cancellationToken);
}
