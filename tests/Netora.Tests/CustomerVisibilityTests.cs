using Netora.Application.Customers;

namespace Netora.Tests;

/// <summary>Manager-only customers must be invisible to Staff everywhere: rows, counts, search, options and details.</summary>
public class CustomerVisibilityTests
{
    [Fact]
    public async Task Staff_list_excludes_manager_only_rows_and_counts()
    {
        var t = new TestDb(); t.Seed();
        await using var db = t.NewContext();

        var staff = await t.Queries(db).ListAsync(new CustomerListQuery(), includeManagerOnly: false, default);
        var manager = await t.Queries(db).ListAsync(new CustomerListQuery(), includeManagerOnly: true, default);

        Assert.Equal(3, staff.TotalMatching);
        Assert.DoesNotContain(staff.Items, i => i.PppoeUsername == "harry_hidden");
        Assert.Equal(3, staff.Stats.Total);
        Assert.Equal(1, staff.Stats.Active);

        Assert.Equal(4, manager.TotalMatching);
        Assert.Equal(4, manager.Stats.Total);
        Assert.Equal(2, manager.Stats.Active);
    }

    [Fact]
    public async Task Staff_search_cannot_find_a_manager_only_customer()
    {
        var t = new TestDb(); t.Seed();
        await using var db = t.NewContext();

        foreach (var term in new[] { "Hidden", "harry_hidden", "Harry" })
        {
            var staff = await t.Queries(db).ListAsync(new CustomerListQuery(Search: term), false, default);
            Assert.Empty(staff.Items);
            Assert.Equal(0, staff.TotalMatching);

            var manager = await t.Queries(db).ListAsync(new CustomerListQuery(Search: term), true, default);
            Assert.Single(manager.Items);
        }
    }

    [Fact]
    public async Task Staff_filter_options_do_not_leak_manager_only_values()
    {
        var t = new TestDb(); t.Seed();
        await using var db = t.NewContext();

        var staff = await t.Queries(db).ListAsync(new CustomerListQuery(), false, default);
        var options = await t.Queries(db).GetFormOptionsAsync(false, default);

        Assert.DoesNotContain("NAP-02", staff.NapOptions);
        Assert.DoesNotContain("Fiber 50 Mbps", staff.PlanOptions);
        Assert.DoesNotContain("NAP-02", options.NapBoxes);
        Assert.DoesNotContain("Fiber 50 Mbps", options.Plans);
    }

    [Fact]
    public async Task Staff_gets_nothing_for_a_manager_only_customer_and_for_a_missing_one()
    {
        var t = new TestDb(); var ids = t.Seed();
        await using var db = t.NewContext();

        var hidden = await t.Queries(db).GetDetailAsync(ids.ManagerOnly, viewerIsManager: false, default);
        var missing = await t.Queries(db).GetDetailAsync(9999, viewerIsManager: false, default);
        var asManager = await t.Queries(db).GetDetailAsync(ids.ManagerOnly, viewerIsManager: true, default);

        Assert.Null(hidden);   // same answer as a customer that does not exist
        Assert.Null(missing);
        Assert.NotNull(asManager);
        Assert.Equal("NET-" + ids.ManagerOnly.ToString("D4"), asManager!.DisplayId);
    }

    [Fact]
    public async Task Staff_cannot_edit_a_manager_only_customer_and_gets_not_found()
    {
        var t = new TestDb(); var ids = t.Seed();
        await using var db = t.NewContext();
        var detail = (await t.Queries(db).GetDetailAsync(ids.ManagerOnly, true, default))!;

        var result = await t.Commands(db).UpdateAsync(ids.ManagerOnly,
            TestDb.EditOf(detail, r => r with { Notes = "tampered" }), TestDb.Staff, default);

        Assert.Equal(UpdateCustomerOutcome.NotFound, result.Outcome);
        await using var check = t.NewContext();
        Assert.Null((await t.Queries(check).GetDetailAsync(ids.ManagerOnly, true, default))!.Notes);
    }
}
