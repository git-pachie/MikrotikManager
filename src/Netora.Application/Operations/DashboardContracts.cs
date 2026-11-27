namespace Netora.Application.Operations;

public sealed record DashboardActivity(int CustomerId, string CustomerName, string Action, string Outcome, DateTimeOffset OccurredAtUtc);

public sealed record DashboardRouter(string Mode, bool Reachable, DateTimeOffset CheckedAtUtc);

public sealed record DashboardDto(int Total, int Active, int Disabled, int Terminated, int PendingApproval,
    int PendingRequests, DashboardRouter Router, IReadOnlyList<DashboardActivity> RecentActivity);

public interface IDashboardQueries
{
    Task<DashboardDto> GetAsync(bool isManager, CancellationToken cancellationToken);
}
