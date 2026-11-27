using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Netora.Application.Common;
using Microsoft.OpenApi.Models;
using Netora.Application;
using Netora.Infrastructure;
using Netora.Infrastructure.Identity;
using Netora.Logging;
using Netora.WebApi.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Secrets come from user-secrets (Development) or appsettings.Secrets.json next to the app (any environment).
builder.AddSecretsFile();

// Structured (JSON) console logs outside Development.
if (!builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole();
}

// Daily log files netora-api-yyyy-MM-dd.txt (module 18). Folder: Logging:File:Path; levels: Logging:File:LogLevel.
builder.AddNetoraFileLogging("netora-api", "logs/api");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Encryption keys for stored PPPoE passwords. Back up this folder with the database: without the keys the stored
// passwords cannot be decrypted. Outside Development the folder must be configured explicitly.
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (string.IsNullOrWhiteSpace(keysPath))
    throw new InvalidOperationException("DataProtection:KeysPath is not configured. Set a folder outside the web root for the encryption keys.");
builder.Services.AddDataProtection()
    .SetApplicationName("Netora")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.IsPathRooted(keysPath) ? keysPath : Path.Combine(builder.Environment.ContentRootPath, keysPath)));
builder.Services.AddNetoraAuthentication();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
            context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retry.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        await context.HttpContext.Response.WriteAsJsonAsync(ApiResponse<object>.Fail("login_rate_limited",
            "Too many sign-in attempts. Wait a minute and try again.", context.HttpContext.TraceIdentifier), cancellationToken);
    };
});

// FluentValidation in Application owns field rules, so MVC must not add implicit [Required] on non-nullable strings.
builder.Services.AddControllers(o =>
    {
        o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
        o.Filters.Add<ValidationFailureLogFilter>();
    })
    .UseApiResponseEnvelopeForValidation();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
// Swagger is controlled only by the "Swagger:Enabled" setting, in any environment.
var swaggerEnabled = builder.Configuration.GetValue<bool>("Swagger:Enabled");
if (swaggerEnabled)
{
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Call POST /api/v1/auth/login, then paste the accessToken here.",
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        },
    });
});

}
var app = builder.Build();

await IdentitySeeder.SeedAsync(app.Services);

// Fictional sample customers, Development only and only into an empty table.
if (app.Environment.IsDevelopment())
{
    await Netora.Infrastructure.Customers.SampleCustomerSeeder.SeedAsync(app.Services);
}

app.UseCorrelationId();
app.UseExceptionHandler();
app.UseUnhandledExceptionLogging();

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// In Development both http and https are served side by side, so no forced redirect.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<PasswordChangeRequiredMiddleware>();
app.UseAuthorization();
app.MapControllers();

app.Run();
