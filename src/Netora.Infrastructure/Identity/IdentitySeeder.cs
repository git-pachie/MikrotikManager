using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Netora.Application.Abstractions;
using Netora.Application.Auth;
using Netora.Domain.Security;

namespace Netora.Infrastructure.Identity;

public static class IdentitySeeder
{
    /// <summary>
    /// Creates the Manager and Staff roles. If no Manager exists and Bootstrap:ManagerUserName / Bootstrap:ManagerPassword
    /// are configured, creates the first Manager. Remove those settings after the first start.
    /// </summary>
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Netora.IdentitySeeder");
        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var configuration = sp.GetRequiredService<IConfiguration>();

        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var result = await roleManager.CreateAsync(new IdentityRole(role));
                if (!result.Succeeded)
                    throw new InvalidOperationException($"Could not create role '{role}': {Describe(result)}");
            }
        }

        if ((await userManager.GetUsersInRoleAsync(Roles.Manager)).Count > 0) return;

        var userName = configuration["Bootstrap:ManagerUserName"];
        var password = configuration["Bootstrap:ManagerPassword"];
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "No Manager account exists. Set Bootstrap:ManagerUserName and Bootstrap:ManagerPassword " +
                "(dotnet user-secrets or appsettings.Secrets.json) and restart to create the first Manager.");
            return;
        }

        var manager = new ApplicationUser
        {
            UserName = userName.Trim(),
            DisplayName = userName.Trim(),
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        var create = await userManager.CreateAsync(manager, password);
        if (!create.Succeeded)
            throw new InvalidOperationException($"Could not create the bootstrap Manager: {Describe(create)}");

        var addRole = await userManager.AddToRoleAsync(manager, Roles.Manager);
        if (!addRole.Succeeded)
            throw new InvalidOperationException($"Could not assign the Manager role: {Describe(addRole)}");

        await sp.GetRequiredService<ISecurityEventWriter>().WriteAsync(
            SecurityEventTypes.ManagerBootstrapped, true, manager.Id, manager.UserName, null, null, cancellationToken);
        logger.LogWarning("First Manager '{UserName}' created. Remove the Bootstrap settings now.", manager.UserName);
    }

    private static string Describe(IdentityResult result) => string.Join("; ", result.Errors.Select(e => e.Description));
}
