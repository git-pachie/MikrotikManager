using Microsoft.OpenApi.Models;
using Netora.Application;
using Netora.Infrastructure;
using Netora.Infrastructure.Identity;
using Netora.WebApi.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Structured (JSON) console logs outside Development. File logging is module 18.
if (!builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole();
}

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddNetoraAuthentication();

// FluentValidation in Application owns field rules, so MVC must not add implicit [Required] on non-nullable strings.
builder.Services.AddControllers(o => o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true)
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

app.UseExceptionHandler();

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

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
