using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Netora.Infrastructure.Identity;

public static class NetoraClaimTypes
{
    public const string Subject = "sub";
    public const string Name = "name";
    public const string Role = "role";
    public const string SecurityStamp = "sstamp";
}

public interface IJwtTokenService
{
    (string Token, DateTimeOffset ExpiresAtUtc) Create(ApplicationUser user, string role);
}

internal sealed class JwtTokenService(IOptions<JwtOptions> options) : IJwtTokenService
{
    public (string Token, DateTimeOffset ExpiresAtUtc) Create(ApplicationUser user, string role)
    {
        var jwt = options.Value;
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(jwt.LifetimeMinutes);

        var claims = new List<System.Security.Claims.Claim>
        {
            new(NetoraClaimTypes.Subject, user.Id),
            new(NetoraClaimTypes.Name, user.UserName ?? string.Empty),
            new(NetoraClaimTypes.Role, role),
            new(NetoraClaimTypes.SecurityStamp, user.SecurityStamp ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey));
        var token = new JwtSecurityToken(
            issuer: jwt.Issuer,
            audience: jwt.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
