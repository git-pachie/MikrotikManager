using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Netora.Domain.Settings;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Router;

/// <summary>Loads the effective router mode once before services in an API request resolve the router client.</summary>
public sealed class RouterModeSelection(NetoraDbContext db, IOptions<RouterOptions> options)
{
    public string Mode { get; private set; } = Resolve(null, options.Value);

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var saved = await db.AppSettings.AsNoTracking()
            .Where(s => s.Key == AppSettingKeys.RouterMode)
            .Select(s => s.Value)
            .SingleOrDefaultAsync(cancellationToken);
        Mode = Resolve(saved, options.Value);
    }

    internal static string Resolve(string? saved, RouterOptions defaults) =>
        saved ?? (defaults.IsRouterOs ? RouterOptions.RouterOsMode : RouterOptions.MockMode);
}
