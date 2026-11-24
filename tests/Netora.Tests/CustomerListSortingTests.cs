using Netora.Application.Customers;

namespace Netora.Tests;

public class CustomerListSortingTests
{
    private static async Task<List<string>> Names(TestDb t, CustomerListQuery query)
    {
        await using var db = t.NewContext();
        var result = await t.Queries(db).ListAsync(query, includeManagerOnly: true, default);
        return result.Items.Select(i => i.PppoeUsername).ToList();
    }

    [Fact]
    public async Task Default_order_is_creation_order()
    {
        var t = new TestDb(); t.Seed();
        Assert.Equal(["maria_santos", "harry_hidden", "pia_pending", "tom_term"], await Names(t, new CustomerListQuery()));
    }

    [Fact]
    public async Task Sorts_by_name_ascending_and_descending()
    {
        var t = new TestDb(); t.Seed();
        // Last name, then first name: Hidden, Pending, Santos, Terminated.
        Assert.Equal(["harry_hidden", "pia_pending", "maria_santos", "tom_term"],
            await Names(t, new CustomerListQuery(SortBy: "name", SortDir: "asc")));
        Assert.Equal(["tom_term", "maria_santos", "pia_pending", "harry_hidden"],
            await Names(t, new CustomerListQuery(SortBy: "name", SortDir: "desc")));
    }

    [Fact]
    public async Task Sorts_by_created_date_descending()
    {
        var t = new TestDb(); t.Seed();
        Assert.Equal(["tom_term", "pia_pending", "harry_hidden", "maria_santos"],
            await Names(t, new CustomerListQuery(SortBy: "created", SortDir: "desc")));
    }

    [Fact]
    public async Task Equal_values_keep_a_stable_order_by_id()
    {
        var t = new TestDb(); t.Seed();
        // pia_pending and tom_term share "Fiber 30 Mbps"; the lower ID comes first in both directions.
        var asc = await Names(t, new CustomerListQuery(SortBy: "plan", SortDir: "asc"));
        var desc = await Names(t, new CustomerListQuery(SortBy: "plan", SortDir: "desc"));
        Assert.True(asc.IndexOf("pia_pending") < asc.IndexOf("tom_term"));
        Assert.True(desc.IndexOf("pia_pending") < desc.IndexOf("tom_term"));
    }

    [Theory]
    [InlineData("password")]
    [InlineData("name; DROP TABLE Customers")]
    [InlineData("")]
    public async Task Unknown_sort_column_falls_back_to_default_order(string column)
    {
        var t = new TestDb(); t.Seed();
        Assert.Equal(["maria_santos", "harry_hidden", "pia_pending", "tom_term"],
            await Names(t, new CustomerListQuery(SortBy: column, SortDir: "desc")));
    }
}
