using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Netora.Application.Abstractions;
using Netora.Application.Auth;
using Netora.Infrastructure.Identity;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "DefaultConnection";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string 'ConnectionStrings:{ConnectionStringName}' is not configured. " +
                "Set it with user-secrets in Development or an environment variable " +
                $"(ConnectionStrings__{ConnectionStringName}) elsewhere. Never commit it.");
        }

        services.AddDbContext<NetoraDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
                sql.MigrationsAssembly(typeof(NetoraDbContext).Assembly.FullName)));

        services.AddScoped<IDatabaseProbe, DatabaseProbe>();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Core Identity only: no cookie schemes and no SignInManager. Browser sign-in is a WebUI concern.
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 10;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.User.RequireUniqueEmail = false;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<NetoraDbContext>();

        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<ISecurityEventWriter, SecurityEventWriter>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<Netora.Application.Customers.ICustomerQueries, Customers.CustomerQueries>();
        services.AddScoped<Netora.Application.Customers.ICustomerRepository, Customers.CustomerRepository>();

        services.AddOptions<Netora.Application.Customers.Attachments.AttachmentOptions>()
            .Bind(configuration.GetSection(Netora.Application.Customers.Attachments.AttachmentOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<Netora.Application.Customers.Attachments.IAttachmentStore, Customers.FileSystemAttachmentStore>();
        services.AddScoped<Netora.Application.Customers.Attachments.ICustomerAttachmentRepository, Customers.CustomerAttachmentRepository>();

        services.AddScoped<Netora.Application.Pppoe.IPppoeQueries, Pppoe.PppoeQueries>();
        services.AddScoped<Netora.Application.Pppoe.IApprovalRepository, Pppoe.ApprovalRepository>();
        services.AddSingleton<Netora.Application.Pppoe.IPppoePasswordProtector, Pppoe.PppoePasswordProtector>();
        return services;
    }
}
