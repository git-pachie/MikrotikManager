namespace Netora.WebUI.Navigation;

/// <param name="ManagerOnly">Hidden from Staff. The pages and the API refuse Staff as well.</param>
public sealed record NavItem(string Key, string Path, string Icon, string Label, bool ManagerOnly = false);

/// <summary>Sidebar entries, in the order and with the icons of the approved mockups.</summary>
public static class NavItems
{
    public static readonly IReadOnlyList<NavItem> All = new NavItem[]
    {
        new("dashboard", "/dashboard", "home", "Dashboard"),
        new("customers", "/customers", "users", "Customers"),
        new("approvals", "/approvals", "clipboard", "Pending Approvals"),
        new("pppoe", "/pppoe", "wifi", "PPPoE Management"),
        new("nap", "/nap-boxes", "layers", "NAP Boxes"),
        new("plans", "/plans", "tag", "Internet Plans"),
        new("onu", "/onu-history", "calendar", "ONU History"),
        new("diagnostics", "/diagnostics", "pulse", "Diagnostics"),
        new("audit", "/audit", "file", "Audit Trail"),
        new("users", "/users", "users", "User Management", ManagerOnly: true),
        new("settings", "/settings", "settings", "Settings", ManagerOnly: true),
        new("reports", "/reports", "file", "Reports"),
        new("backups", "/backups", "upload", "Backups"),
        new("notifications", "/notifications", "bell", "Notifications"),
    };
}
