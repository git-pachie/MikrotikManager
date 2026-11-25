using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Netora.Application.Health;

namespace Netora.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        services.AddScoped<IHealthService, HealthService>();
        services.AddScoped<Customers.ICustomerCommands, Customers.CustomerCommands>();
        services.AddScoped<Customers.Attachments.ICustomerAttachmentService, Customers.Attachments.CustomerAttachmentService>();
        services.AddScoped<Pppoe.IPppoeCommands, Pppoe.PppoeCommands>();
        services.AddScoped<Approvals.IApprovalCommands, Approvals.ApprovalCommands>();
        return services;
    }
}
