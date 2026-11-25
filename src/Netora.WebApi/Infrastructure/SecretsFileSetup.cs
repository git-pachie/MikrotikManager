using Microsoft.Extensions.Configuration.Json;

namespace Netora.WebApi.Infrastructure;

/// <summary>
/// Loads secrets from <c>appsettings.Secrets.json</c> in the API folder, so a server needs no environment variables
/// and no administrator rights: only permission to place one file next to the app.
/// The file is git-ignored and never copied by build or publish; create it by hand on each machine.
/// </summary>
public static class SecretsFileSetup
{
    public const string FileName = "appsettings.Secrets.json";

    /// <summary>
    /// Order of precedence (later wins): appsettings.json, appsettings.{Environment}.json, appsettings.Secrets.json,
    /// user-secrets (Development only), command-line arguments.
    /// </summary>
    public static WebApplicationBuilder AddSecretsFile(this WebApplicationBuilder builder)
    {
        var sources = builder.Configuration.Sources;
        builder.Configuration.AddJsonFile(FileName, optional: true, reloadOnChange: true);
        var secrets = sources[^1];

        // Place it right after appsettings.{Environment}.json, so user-secrets and command-line arguments still win.
        var environmentFile = $"appsettings.{builder.Environment.EnvironmentName}.json";
        var after = -1;
        for (var i = 0; i < sources.Count - 1; i++)
        {
            if (sources[i] is JsonConfigurationSource json && string.Equals(json.Path, environmentFile, StringComparison.OrdinalIgnoreCase))
                after = i;
        }
        if (after >= 0)
        {
            sources.RemoveAt(sources.Count - 1);
            sources.Insert(after + 1, secrets);
        }
        return builder;
    }
}
