using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Netora.Application.Abstractions;
using Netora.Application.Auth;
using Netora.Application.Logging;
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
                "Set it with dotnet user-secrets, or in appsettings.Secrets.json next to the Web API " +
                "(see appsettings.Secrets.example.json). Never commit it.");
        }

        // File logging (module 18). The host replaces this with the real log (Netora.Logging); tools get nothing.
        services.TryAddSingleton<IActivityLog>(NullActivityLog.Instance);
        services.AddScoped<ActivityLogInterceptor>();

        services.AddDbContext<NetoraDbContext>((sp, options) =>
            options.UseSqlServer(connectionString, sql =>
                    sql.MigrationsAssembly(typeof(NetoraDbContext).Assembly.FullName))
                .AddInterceptors(sp.GetRequiredService<ActivityLogInterceptor>()));

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

        services.AddScoped<Netora.Application.Approvals.IApprovalQueries, Approvals.ApprovalQueries>();
        services.Configure<Approvals.EmailOptions>(configuration.GetSection(Approvals.EmailOptions.SectionName));
        services.AddSingleton<Approvals.ApprovalEmailNotifier>();
        services.AddSingleton<Netora.Application.Approvals.IApprovalNotifier>(sp => sp.GetRequiredService<Approvals.ApprovalEmailNotifier>());
        services.AddHostedService<Approvals.ApprovalEmailDispatcher>();
        services.AddScoped<Approvals.IEmailSettingsProvider, Approvals.EmailSettingsProvider>();

        services.AddScoped<Netora.Application.Users.IUserAdminService, UserAdminService>();
        services.AddScoped<Netora.Application.Users.IProfileService, ProfileService>();
        services.AddScoped<Netora.Application.Settings.ISettingsService, Settings.SettingsService>();
        services.AddScoped<Netora.Application.Catalog.IPlanService, Catalog.PlanService>();
        services.AddScoped<Netora.Application.Catalog.INapService, Catalog.NapService>();
        services.AddScoped<Netora.Application.Audit.IAuditQueries, Audit.AuditQueries>();

        // Router (module 09). Router:Mode must be set explicitly, so mock results can never pass for a real router.
        var routerMode = configuration[$"{Router.RouterOptions.SectionName}:Mode"];
        if (!string.Equals(routerMode, Router.RouterOptions.MockMode, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(routerMode, Router.RouterOptions.RouterOsMode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Router:Mode is not set. Set it to \"RouterOS\" to use the real MikroTik router, or \"Mock\" for the pretend router (appsettings.json).");
        }
        services.Configure<Router.RouterOptions>(configuration.GetSection(Router.RouterOptions.SectionName));
        services.AddScoped<Router.IRouterConnectionProvider, Router.RouterConnectionProvider>();
        services.AddScoped<Router.RouterOsRestClient>();
        services.AddSingleton<Router.MockRouterClient>();
        services.AddScoped<Netora.Application.Router.IRouterClient>(sp =>
            string.Equals(routerMode, Router.RouterOptions.RouterOsMode, StringComparison.OrdinalIgnoreCase)
                ? sp.GetRequiredService<Router.RouterOsRestClient>()
                : sp.GetRequiredService<Router.MockRouterClient>());
        services.AddScoped<Netora.Application.Router.IRouterProvisioner, Router.RouterProvisioner>();
        services.AddScoped<Netora.Application.Router.IRouterStatusService, Router.RouterStatusService>();
        services.AddScoped<Netora.Application.Router.IPppoeImportService, Router.PppoeImportService>();
        services.AddScoped<Netora.Application.Router.IRouterConnectionTester, Router.RouterConnectionTester>();        return services;
    }
}
