namespace Netora.Application.Auth;

public static class Roles
{
    public const string Manager = "Manager";
    public const string Staff = "Staff";

    public static readonly IReadOnlyList<string> All = [Manager, Staff];
}

public static class Policies
{
    /// <summary>Endpoints only a Manager may call (approvals and other sensitive operations).</summary>
    public const string ManagerOnly = "ManagerOnly";
}
