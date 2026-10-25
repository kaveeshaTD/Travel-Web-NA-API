using System.Net;
using System.Text.Json;
using TravelWebAPI.Application.Contact;
using TravelWebAPI.Application.Users;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Infrastructure.Persistence;
using TravelWebAPI.Tests.Integration.Support;

namespace TravelWebAPI.Tests.Integration;

/// <summary>The user list and the inbox on the common list contract, each with 45 rows (groups of three share a creation time).</summary>
[Collection(PostgresCollection.Name)]
public sealed class UserMessageListQueryTests(PostgresFixture postgres) : ContentTestBase(postgres)
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await CreateAsync("services", Service("Private tour"));

        await using var db = PostgresFixture.CreateContext(Connection);
        for (var index = 1; index <= 45; index++)
        {
            var user = TestData.User($"person{index:00}@example.com");
            user.Name = index switch { 7 => "100% Sure", 8 => "Under_score", _ => $"Person {index:00}" };
            user.Country = index % 2 == 0 ? "Sri Lanka" : "United Kingdom";
            user.Role = index % 9 == 0 ? UserRole.Admin : UserRole.Customer;
            user.Status = index % 7 == 0 ? UserStatus.Inactive : UserStatus.Active;
            user.CreatedAt = Start.AddHours((index - 1) / 3);
            user.UpdatedAt = user.CreatedAt;
            db.Users.Add(user);
            await db.SaveChangesAsync();

            // person NN has NN % 4 bookings: 0, 1, 2 or 3.
            for (var number = 1; number <= index % 4; number++)
            {
                db.Bookings.Add(TestData.Booking(user.Id, "private-tour", $"BK-{index:00}{number:00}"));
            }
        }

        for (var index = 1; index <= 45; index++)
        {
            db.ContactMessages.Add(new ContactMessage
            {
                Id = Guid.CreateVersion7(),
                Name = index switch { 7 => "100% Sure", 8 => "Under_score", _ => $"Visitor {index:00}" },
                Email = $"visitor{index:00}@example.com",
                Message = index % 10 == 0 ? $"Please call about Jaffna trip {index}" : $"Hello number {index}",
                CreatedAt = Start.AddHours((index - 1) / 3),
                Status = (index % 3) switch { 0 => MessageStatus.Replied, 1 => MessageStatus.New, _ => MessageStatus.Read },
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task<JsonElement> GetAsync(string path)
    {
        var response = await AsAdmin(HttpMethod.Get, path);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await Json(response);
    }

    private static List<string> Values(JsonElement page, string property) => page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty(property).GetString()!).ToList();

    // ---------------------------------------------------------------- access

    [Theory]
    [InlineData("/api/users")]
    [InlineData("/api/messages")]
    public async Task Both_lists_refuse_guests_and_customers(string path)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Api.GetAsync(path, CustomerToken)).StatusCode);
    }

    // ---------------------------------------------------------------- users

    [Fact]
    public async Task The_first_page_of_users_is_newest_first_with_booking_counts_and_no_password_data()
    {
        var page = await GetAsync("/api/users");

        Assert.Equal((20, 45, 3), (page.GetProperty("items").GetArrayLength(), page.GetProperty("totalCount").GetInt32(), page.GetProperty("totalPages").GetInt32()));
        var times = page.GetProperty("items").EnumerateArray().Select(u => u.GetProperty("createdAt").GetDateTimeOffset()).ToList();
        Assert.Equal(times.OrderByDescending(t => t), times);
        Assert.All(page.GetProperty("items").EnumerateArray(), user =>
        {
            Assert.False(user.TryGetProperty("passwordHash", out _));
            Assert.False(user.TryGetProperty("password", out _));
            var email = user.GetProperty("email").GetString()!;
            Assert.Equal(int.Parse(email[6..8], System.Globalization.CultureInfo.InvariantCulture) % 4, user.GetProperty("bookingCount").GetInt32());
        });
    }

    [Fact]
    public async Task Walking_every_user_page_shows_each_user_once_and_a_page_past_the_end_is_empty()
    {
        var seen = new List<string>();
        for (var number = 1; number <= 9; number++)
        {
            seen.AddRange(Values(await GetAsync($"/api/users?page={number}&pageSize=5"), "id"));
        }

        Assert.Equal(45, seen.Distinct().Count());
        var beyond = await GetAsync("/api/users?page=10&pageSize=5");
        Assert.Equal(0, beyond.GetProperty("items").GetArrayLength());
        Assert.Equal(45, beyond.GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("createdAt")]
    [InlineData("-createdAt")]
    [InlineData("name")]
    [InlineData("-name")]
    [InlineData("email")]
    [InlineData("-email")]
    [InlineData("role")]
    [InlineData("-role")]
    [InlineData("status")]
    [InlineData("-status")]
    [InlineData("bookingCount")]
    [InlineData("-bookingCount")]
    public async Task Every_user_sort_field_works_in_both_directions_with_stable_pages(string sort)
    {
        var seen = new List<string>();
        for (var number = 1; number <= 3; number++)
        {
            seen.AddRange(Values(await GetAsync($"/api/users?sort={sort}&page={number}&pageSize=20"), "id"));
        }

        Assert.Equal(45, seen.Distinct().Count());
        Assert.Equal(seen, Values(await GetAsync($"/api/users?sort={sort}&pageSize=100"), "id"));
    }

    [Fact]
    public async Task Users_sort_by_their_booking_count_so_the_busiest_come_first()
    {
        var counts = (await GetAsync("/api/users?sort=-bookingCount&pageSize=100")).GetProperty("items").EnumerateArray().Select(u => u.GetProperty("bookingCount").GetInt32()).ToList();

        Assert.Equal(counts.OrderByDescending(c => c), counts);
        Assert.Equal(3, counts[0]);
    }

    [Fact]
    public async Task User_search_looks_in_the_name_email_and_country_and_treats_wildcards_as_plain()
    {
        Assert.Equal(1, (await GetAsync("/api/users?search=person45%40")).GetProperty("totalCount").GetInt32());
        Assert.Equal(22, (await GetAsync("/api/users?search=sri%20lanka")).GetProperty("totalCount").GetInt32());
        Assert.Equal(["100% Sure"], Values(await GetAsync("/api/users?search=" + Uri.EscapeDataString("100%")), "name"));
        Assert.Equal(0, (await GetAsync("/api/users?search=" + Uri.EscapeDataString("%%"))).GetProperty("items").GetArrayLength());
        Assert.Equal(["Under_score"], Values(await GetAsync("/api/users?search=" + Uri.EscapeDataString("r_s")), "name"));
        Assert.Equal(0, (await GetAsync("/api/users?search=" + Uri.EscapeDataString("n_0"))).GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task The_role_and_status_filters_and_their_tab_counts()
    {
        var admins = await GetAsync("/api/users?role=admin&facets=role,status&pageSize=100");
        var inactive = await GetAsync("/api/users?status=inactive&pageSize=100");

        Assert.Equal(5, admins.GetProperty("totalCount").GetInt32()); // 9, 18, 27, 36, 45
        Assert.All(admins.GetProperty("items").EnumerateArray(), u => Assert.Equal("admin", u.GetProperty("role").GetString()));
        var role = admins.GetProperty("facets").GetProperty("role");
        Assert.Equal(5, role.GetProperty("admin").GetInt32());
        Assert.Equal(40, role.GetProperty("customer").GetInt32());
        // the status counts follow the role filter (admins only): 9..45 step 9, of which 63 is out of range -> none are multiples of 7 except none
        var status = admins.GetProperty("facets").GetProperty("status");
        Assert.Equal(5, status.GetProperty("active").GetInt32() + status.GetProperty("inactive").GetInt32());
        Assert.Equal(6, inactive.GetProperty("totalCount").GetInt32()); // 7, 14, 21, 28, 35, 42
    }

    [Fact]
    public async Task Wrong_user_filters_and_fields_get_their_messages()
    {
        var response = await AsAdmin(HttpMethod.Get, "/api/users?role=owner&status=banned&sort=password&facets=country&page=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Role must be admin or customer", await Field(response, "role"));
        Assert.Equal("Status must be active or inactive", await Field(response, "status"));
        Assert.Equal("Sort by one of: createdAt, name, email, role, status, bookingCount", await Field(response, "sort"));
        Assert.Equal("Facets can be: role, status", await Field(response, "facets"));
        Assert.Equal("Page must be a whole number of 1 or more", await Field(response, "page"));
    }

    [Fact]
    public void The_user_list_implements_exactly_what_its_definition_declares()
    {
        var (sort, facets) = UserAdminRepository.ListNames;

        Assert.Equal(UserLists.Definition.SortFields.Order(), sort.Order());
        Assert.Equal(UserLists.Definition.Facets.Order(), facets.Order());
    }

    // ---------------------------------------------------------------- messages

    [Fact]
    public async Task The_first_page_of_the_inbox_is_newest_first_with_the_totals()
    {
        var page = await GetAsync("/api/messages");

        Assert.Equal((20, 45, 3), (page.GetProperty("items").GetArrayLength(), page.GetProperty("totalCount").GetInt32(), page.GetProperty("totalPages").GetInt32()));
        var times = page.GetProperty("items").EnumerateArray().Select(m => m.GetProperty("createdAt").GetDateTimeOffset()).ToList();
        Assert.Equal(times.OrderByDescending(t => t), times);
        Assert.Equal(["id", "name", "email", "message", "status", "createdAt", "repliedAt"], page.GetProperty("items")[0].EnumerateObject().Select(p => p.Name));
    }

    [Theory]
    [InlineData("createdAt")]
    [InlineData("-createdAt")]
    [InlineData("name")]
    [InlineData("-name")]
    [InlineData("status")]
    [InlineData("-status")]
    public async Task Every_inbox_sort_field_works_in_both_directions_with_stable_pages(string sort)
    {
        var seen = new List<string>();
        for (var number = 1; number <= 9; number++)
        {
            seen.AddRange(Values(await GetAsync($"/api/messages?sort={sort}&page={number}&pageSize=5"), "id"));
        }

        Assert.Equal(45, seen.Distinct().Count());
        Assert.Equal(seen, Values(await GetAsync($"/api/messages?sort={sort}&pageSize=100"), "id"));
    }

    [Fact]
    public async Task The_inbox_search_looks_in_the_name_email_and_text_and_treats_wildcards_as_plain()
    {
        Assert.Equal(4, (await GetAsync("/api/messages?search=JAFFNA")).GetProperty("totalCount").GetInt32());
        Assert.Equal(1, (await GetAsync("/api/messages?search=visitor45%40")).GetProperty("totalCount").GetInt32());
        Assert.Equal(["100% Sure"], Values(await GetAsync("/api/messages?search=" + Uri.EscapeDataString("100%")), "name"));
        Assert.Equal(0, (await GetAsync("/api/messages?search=" + Uri.EscapeDataString("%%"))).GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task The_unread_count_comes_from_the_status_facet_without_loading_the_inbox()
    {
        var page = await GetAsync("/api/messages?pageSize=1&facets=status");

        Assert.Equal(1, page.GetProperty("items").GetArrayLength());
        var counts = page.GetProperty("facets").GetProperty("status");
        Assert.Equal(15, counts.GetProperty("new").GetInt32());
        Assert.Equal(15, counts.GetProperty("read").GetInt32());
        Assert.Equal(15, counts.GetProperty("replied").GetInt32());
    }

    [Fact]
    public async Task The_status_filter_narrows_the_inbox_and_the_facet_ignores_it()
    {
        var page = await GetAsync("/api/messages?status=new&facets=status&pageSize=100");

        Assert.Equal(15, page.GetProperty("totalCount").GetInt32());
        Assert.All(page.GetProperty("items").EnumerateArray(), m => Assert.Equal("new", m.GetProperty("status").GetString()));
        Assert.Equal(15, page.GetProperty("facets").GetProperty("status").GetProperty("replied").GetInt32());
    }

    [Fact]
    public async Task A_wrong_inbox_status_gets_its_message()
    {
        var response = await AsAdmin(HttpMethod.Get, "/api/messages?status=done");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Status must be one of: new, read, replied", await Field(response, "status"));
    }

    [Fact]
    public void The_inbox_implements_exactly_what_its_definition_declares()
    {
        var (sort, facets) = ContactMessageRepository.ListNames;

        Assert.Equal(MessageLists.Definition.SortFields.Order(), sort.Order());
        Assert.Equal(MessageLists.Definition.Facets.Order(), facets.Order());
    }
}
