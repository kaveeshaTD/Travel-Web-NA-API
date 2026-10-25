using Microsoft.EntityFrameworkCore;
using TravelWebAPI.Application.Lists;
using TravelWebAPI.Domain.Common;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Infrastructure.Persistence.Lists;

namespace TravelWebAPI.Tests.Integration;

/// <summary>
/// The generic paging engine on a real PostgreSQL database, using the contact messages as sample rows: 45 messages, with
/// groups of 5 sharing the same creation time so the stable order matters.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ListSupportTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly string[] AllStatuses = [.. Enum.GetValues<MessageStatus>().Select(EnumWire.ToWire)];

    private static readonly ListSpec<ContactMessage> Spec = new ListSpec<ContactMessage>(
        SortMap.TieBreakBy<ContactMessage, Guid>(m => m.Id)
            .Add("createdAt", m => m.CreatedAt)
            .Add("name", m => m.Name)
            .Add("status", m => m.Status),
        [m => m.Name, m => m.Email, m => m.Message])
        .AddFacet(new Facet<ContactMessage, MessageStatus>("status", m => m.Status, EnumWire.ToWire, AllStatuses));

    private static readonly DateTimeOffset Start = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    private string connection = string.Empty;

    public async Task InitializeAsync()
    {
        connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);
        for (var index = 1; index <= 45; index++)
        {
            db.ContactMessages.Add(new ContactMessage
            {
                Id = Guid.CreateVersion7(),
                Name = index switch { 7 => "100% Sure", 8 => "Under_score", 9 => "Back\\slash", _ => $"Name {index:00}" },
                Email = $"person{index:00}@example.com",
                Message = index % 10 == 0 ? $"Please call about Jaffna trip {index}" : $"Hello number {index}",
                // 45 rows, 9 distinct timestamps: five messages share each one.
                CreatedAt = Start.AddHours((index - 1) / 5),
                Status = (index % 3) switch { 0 => MessageStatus.Replied, 1 => MessageStatus.New, _ => MessageStatus.Read },
            });
        }

        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static ListQuery Query(int page = 1, int pageSize = 20, string search = "", string sort = "-createdAt", params string[] facets)
        => new(page, pageSize, search, ListParser.ParseDefaultSort(sort), facets);

    private async Task<PagedResult<ContactMessage>> RunAsync(ListQuery query, Action<FilterSet<ContactMessage>>? filters = null)
    {
        await using var db = PostgresFixture.CreateContext(connection);
        var set = new FilterSet<ContactMessage>();
        filters?.Invoke(set);
        return await Spec.RunAsync(db.ContactMessages.AsNoTracking(), set, query, CancellationToken.None);
    }

    // ---------------------------------------------------------------- paging

    [Fact]
    public async Task Pages_give_the_right_slice_and_totals()
    {
        var first = await RunAsync(Query(page: 1, pageSize: 20));
        var third = await RunAsync(Query(page: 3, pageSize: 20));

        Assert.Equal(20, first.Items.Count);
        Assert.Equal(5, third.Items.Count);
        Assert.Equal((45, 3), (first.TotalCount, first.TotalPages));
        Assert.Equal((3, 20), (third.Page, third.PageSize));
    }

    [Fact]
    public async Task A_page_past_the_end_is_empty_with_the_real_totals()
    {
        var result = await RunAsync(Query(page: 9));

        Assert.Empty(result.Items);
        Assert.Equal((45, 3), (result.TotalCount, result.TotalPages));
    }

    [Fact]
    public async Task Rows_with_equal_sort_values_appear_on_exactly_one_page_in_a_fixed_order()
    {
        var seen = new List<Guid>();
        for (var page = 1; page <= 9; page++)
        {
            seen.AddRange((await RunAsync(Query(page: page, pageSize: 5))).Items.Select(m => m.Id));
        }

        Assert.Equal(45, seen.Count);
        Assert.Equal(45, seen.Distinct().Count());

        var again = new List<Guid>();
        for (var page = 1; page <= 9; page++)
        {
            again.AddRange((await RunAsync(Query(page: page, pageSize: 5))).Items.Select(m => m.Id));
        }

        Assert.Equal(seen, again);
    }

    [Fact]
    public async Task Different_page_sizes_walk_the_same_order()
    {
        var bySeven = new List<Guid>();
        for (var page = 1; page <= 7; page++)
        {
            bySeven.AddRange((await RunAsync(Query(page: page, pageSize: 7, sort: "status"))).Items.Select(m => m.Id));
        }

        var all = (await RunAsync(Query(pageSize: 100, sort: "status"))).Items.Select(m => m.Id).ToList();

        Assert.Equal(all, bySeven);
    }

    // ---------------------------------------------------------------- sorting

    [Fact]
    public async Task Sorting_by_a_field_and_then_another_orders_within_ties()
    {
        var result = await RunAsync(Query(pageSize: 100, sort: "-status,name"));

        var names = result.Items.Select(m => (m.Status, m.Name)).ToList();
        var expected = names.OrderByDescending(x => x.Status).ThenBy(x => x.Name, StringComparer.Ordinal).ToList();
        Assert.Equal(45, names.Count);
        Assert.Equal(names.Select(x => x.Status), expected.Select(x => x.Status));
        foreach (var group in names.GroupBy(x => x.Status))
        {
            Assert.Equal(group.Select(x => x.Name).OrderBy(n => n, StringComparer.Create(System.Globalization.CultureInfo.InvariantCulture, false)), group.Select(x => x.Name));
        }
    }

    [Fact]
    public async Task The_default_newest_first_and_its_opposite_are_mirror_images_of_the_time()
    {
        var newest = (await RunAsync(Query(pageSize: 100, sort: "-createdAt"))).Items.Select(m => m.CreatedAt).ToList();
        var oldest = (await RunAsync(Query(pageSize: 100, sort: "createdAt"))).Items.Select(m => m.CreatedAt).ToList();

        Assert.Equal(newest.OrderByDescending(t => t), newest);
        Assert.Equal(oldest.OrderBy(t => t), oldest);
        Assert.Equal(Start.AddHours(8), newest[0]);
        Assert.Equal(Start, oldest[0]);
    }

    // ---------------------------------------------------------------- search

    [Fact]
    public async Task Search_ignores_letter_case_and_looks_in_every_field()
    {
        var byName = await RunAsync(Query(search: "NAME 1"));
        var byEmail = await RunAsync(Query(search: "PERSON45@"));
        var byText = await RunAsync(Query(search: "jaffna"));

        Assert.Equal(10, byName.TotalCount); // Name 10 to Name 19
        Assert.Single(byEmail.Items);
        Assert.Equal(4, byText.TotalCount); // messages 10, 20, 30, 40
    }

    [Fact]
    public async Task Percent_underscore_and_backslash_are_matched_as_themselves()
    {
        Assert.Equal(["100% Sure"], (await RunAsync(Query(search: "100%"))).Items.Select(m => m.Name));
        Assert.Empty((await RunAsync(Query(search: "%%"))).Items);
        Assert.Equal(["Under_score"], (await RunAsync(Query(search: "r_s"))).Items.Select(m => m.Name));
        // A wildcard underscore would match "e 1" in "Name 10" to "Name 19".
        Assert.Empty((await RunAsync(Query(search: "e_1"))).Items);
        Assert.Equal(["Back\\slash"], (await RunAsync(Query(search: "k\\s"))).Items.Select(m => m.Name));
    }

    [Fact]
    public async Task Search_counts_only_the_matching_rows()
    {
        var result = await RunAsync(Query(search: "name 4", pageSize: 3));

        Assert.Equal(6, result.TotalCount); // Name 04 and Name 40 to Name 44 -- Name 4 matches "Name 4x" and "Name 04"? no: "name 4" is contained in "Name 40".."Name 45" (6 rows)
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(2, result.TotalPages);
    }

    // ---------------------------------------------------------------- filters

    [Fact]
    public async Task Filters_narrow_the_rows_and_the_total()
    {
        var result = await RunAsync(Query(pageSize: 100), filters => filters.Add("status", m => m.Status == MessageStatus.New));

        Assert.Equal(15, result.TotalCount);
        Assert.All(result.Items, m => Assert.Equal(MessageStatus.New, m.Status));
    }

    [Fact]
    public async Task Filters_and_search_combine_with_and()
    {
        var result = await RunAsync(Query(search: "jaffna"), filters => filters.Add("status", m => m.Status == MessageStatus.Replied));

        // messages 10, 20, 30, 40 mention Jaffna; of those only 30 has index % 3 == 0.
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task A_filter_that_was_not_asked_for_does_nothing()
    {
        var result = await RunAsync(Query(), filters => filters.Add("status", null));

        Assert.Equal(45, result.TotalCount);
    }

    // ---------------------------------------------------------------- facets

    [Fact]
    public async Task Facets_are_absent_unless_asked_for()
    {
        Assert.Null((await RunAsync(Query())).Facets);
    }

    [Fact]
    public async Task Facet_counts_ignore_their_own_filter_so_tabs_show_every_choice()
    {
        var result = await RunAsync(Query(facets: "status"), filters => filters.Add("status", m => m.Status == MessageStatus.New));

        Assert.Equal(15, result.TotalCount);
        var counts = result.Facets!["status"];
        Assert.Equal(15, counts["new"]);
        Assert.Equal(15, counts["read"]);
        Assert.Equal(15, counts["replied"]);
    }

    [Fact]
    public async Task Facet_counts_follow_the_search()
    {
        var result = await RunAsync(Query(search: "jaffna", facets: "status"));

        // Jaffna messages are 10 (new), 20 (read), 30 (replied) and 40 (new).
        var counts = result.Facets!["status"];
        Assert.Equal(2, counts["new"]);
        Assert.Equal(1, counts["read"]);
        Assert.Equal(1, counts["replied"]);
    }

    [Fact]
    public async Task Values_with_no_rows_are_listed_with_zero()
    {
        var result = await RunAsync(Query(search: "100%", facets: "status"));

        var counts = result.Facets!["status"];
        Assert.Equal(AllStatuses.Order(), counts.Keys.Order());
        Assert.Equal(1, counts.Values.Sum());
        Assert.Equal(2, counts.Values.Count(count => count == 0));
    }

    [Fact]
    public void The_spec_names_its_sort_fields_and_facets()
    {
        var (sort, facets) = Spec.Names;

        Assert.Equal(["createdAt", "name", "status"], sort.Order());
        Assert.Equal(["status"], facets);
    }
}
