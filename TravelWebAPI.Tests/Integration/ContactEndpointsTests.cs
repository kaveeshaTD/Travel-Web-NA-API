using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Infrastructure.Seed;
using TravelWebAPI.Infrastructure.Services;
using TravelWebAPI.Tests.Integration.Support;

namespace TravelWebAPI.Tests.Integration;

/// <summary>The contact form, the admin inbox and the email log through the real API and database.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ContactEndpointsTests(PostgresFixture postgres) : ContentTestBase(postgres)
{
    private static object Message(string email = "visitor@example.com", string name = "Sam Visitor", string message = "Do you drive to Ella from the airport?")
        => new { name, email, message };

    private async Task<string> SendAsync(string email = "visitor@example.com", string message = "Do you drive to Ella from the airport?")
    {
        var response = await Api.PostAsync("/api/contact", Message(email, message: message));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Scalar<Guid>($"SELECT id FROM contact_messages WHERE email = '{email}' ORDER BY created_at DESC LIMIT 1")).ToString();
    }

    // ---------------------------------------------------------------- sending

    [Fact]
    public async Task A_message_is_201_with_a_reference_saved_as_new_with_an_acknowledgement_logged()
    {
        var response = await Api.PostAsync("/api/contact", Message());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Matches("^MSG-[0-9A-F]{8}$", (await Json(response)).GetProperty("reference").GetString());
        Assert.Equal("new", await Scalar<string>("SELECT status FROM contact_messages"));
        Assert.Equal("visitor@example.com", await Scalar<string>("SELECT email FROM contact_messages"));
        Assert.Equal(1L, await Scalar<long>("SELECT count(*) FROM email_logs"));
        Assert.Equal("contact-ack", await Scalar<string>("SELECT kind FROM email_logs"));
        Assert.Equal("visitor@example.com", await Scalar<string>("SELECT to_address FROM email_logs"));
        Assert.Equal(await Scalar<Guid>("SELECT id FROM contact_messages"), await Scalar<Guid>("SELECT contact_message_id FROM email_logs"));
        var body = await Scalar<string>("SELECT body FROM email_logs");
        Assert.Contains("Sam Visitor", body, StringComparison.Ordinal);
        Assert.Contains("Do you drive to Ella from the airport?", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_an_email_provider_the_acknowledgement_is_failed_with_the_reason_and_the_message_is_kept()
    {
        await Api.PostAsync("/api/contact", Message());

        Assert.Equal("failed", await Scalar<string>("SELECT delivery_status FROM email_logs"));
        Assert.Equal("No email provider configured", await Scalar<string>("SELECT error FROM email_logs"));
        Assert.Single(Factory.Emails.Messages);
        Assert.Equal(EmailKind.ContactAck, Factory.Emails.Messages[0].Kind);
        Assert.Equal(1L, await Scalar<long>("SELECT count(*) FROM contact_messages"));
    }

    [Fact]
    public async Task The_email_address_is_trimmed_and_lower_cased()
    {
        await Api.PostAsync("/api/contact", Message(email: "  Visitor@Example.COM "));

        Assert.Equal("visitor@example.com", await Scalar<string>("SELECT email FROM contact_messages"));
        Assert.Equal("visitor@example.com", await Scalar<string>("SELECT to_address FROM email_logs"));
    }

    [Fact]
    public async Task Invalid_messages_report_the_fields_and_store_nothing()
    {
        var response = await Api.PostAsync("/api/contact", new { name = "S", email = "nope", message = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Enter your name", await Field(response, "name"));
        Assert.Equal("Enter a valid email address", await Field(response, "email"));
        Assert.Equal("Please write at least 10 characters", await Field(response, "message"));
        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM contact_messages"));
        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM email_logs"));
    }

    [Fact]
    public async Task An_empty_object_and_an_empty_body_are_both_400()
    {
        var empty = await Api.PostAsync("/api/contact", new { });
        using var noBody = new HttpRequestMessage(HttpMethod.Post, "/api/contact") { Content = Raw(string.Empty) };
        var missing = await Api.SendAsync(noBody);

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [Fact]
    public async Task The_same_address_is_acknowledged_once_an_hour_and_each_address_gets_its_own()
    {
        await Api.PostAsync("/api/contact", Message());
        Factory.Clock.Advance(TimeSpan.FromMinutes(30));
        await Api.PostAsync("/api/contact", Message());
        await Api.PostAsync("/api/contact", Message(email: "other@example.com"));

        Assert.Equal(3L, await Scalar<long>("SELECT count(*) FROM contact_messages"));
        Assert.Equal(2L, await Scalar<long>("SELECT count(*) FROM email_logs"));

        Factory.Clock.Advance(TimeSpan.FromMinutes(31));
        await Api.PostAsync("/api/contact", Message());

        Assert.Equal(3L, await Scalar<long>("SELECT count(*) FROM email_logs"));
    }

    [Fact]
    public async Task Reviews_and_contact_messages_share_the_submission_limit_which_login_does_not()
    {
        await using var factory = ApiFactory.With(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = Connection,
            ["RateLimiting:SubmitPermitLimit"] = "5",
            ["RateLimiting:SubmitWindowSeconds"] = "600",
        });
        var api = new AuthApi(AuthApi.CreateClient(factory));
        var review = new { name = "Emma Wilson", country = "Germany", rating = 5, tripType = "Beach", comment = "Wonderful trip, our driver was excellent." };

        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.Equal(HttpStatusCode.Created, (await api.PostAsync("/api/reviews", review)).StatusCode);
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            Assert.Equal(HttpStatusCode.Created, (await api.PostAsync("/api/contact", Message($"v{attempt}@example.com"))).StatusCode);
        }

        var sixth = await api.PostAsync("/api/contact", Message("late@example.com"));

        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
        Assert.True(sixth.Headers.Contains("Retry-After"));
        AuthApi.AssertError(await Json(sixth), HttpStatusCode.TooManyRequests);
        Assert.Equal(2L, await Scalar<long>("SELECT count(*) FROM contact_messages"));
        Assert.Equal(2L, await Scalar<long>("SELECT count(*) FROM email_logs"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.LoginAsync("nobody@example.com", "Wrong12345")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("/api/reviews")).StatusCode);
    }

    // ---------------------------------------------------------------- inbox

    [Fact]
    public async Task The_inbox_is_newest_first_with_the_documented_fields()
    {
        await SendAsync("first@example.com", "The first message of the day.");
        Factory.Clock.Advance(TimeSpan.FromMinutes(5));
        await SendAsync("second@example.com", "The second message of the day.");

        var response = await AsAdmin(HttpMethod.Get, "/api/messages");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await Json(response);
        Assert.Equal(["second@example.com", "first@example.com"], list.EnumerateArray().Select(m => m.GetProperty("email").GetString()));
        var first = list[0];
        Assert.Equal(["id", "name", "email", "message", "status", "createdAt", "repliedAt"], first.EnumerateObject().Select(p => p.Name));
        Assert.Equal("new", first.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("repliedAt").ValueKind);
    }

    [Fact]
    public async Task Only_an_admin_can_use_the_inbox_routes()
    {
        var id = await SendAsync();
        var attempts = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Get, "/api/messages", null),
            (HttpMethod.Put, $"/api/messages/{id}", new { status = "read" }),
            (HttpMethod.Post, $"/api/messages/{id}/reply", new { body = "Hello there" }),
            (HttpMethod.Delete, $"/api/messages/{id}", null),
            (HttpMethod.Get, "/api/emails", null),
        };

        foreach (var (method, path, body) in attempts)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Api.SendAsync(method, path, body)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Api.SendAsync(method, path, body, bearer: CustomerToken)).StatusCode);
        }

        Assert.Equal("new", await Scalar<string>("SELECT status FROM contact_messages"));
        Assert.Equal(1L, await Scalar<long>("SELECT count(*) FROM email_logs"));
    }

    // ---------------------------------------------------------------- status

    [Theory]
    [InlineData("read")]
    [InlineData("replied")]
    [InlineData("new")]
    public async Task A_valid_status_is_stored(string status)
    {
        var id = await SendAsync();

        var response = await AsAdmin(HttpMethod.Put, $"/api/messages/{id}", new { status });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(status, (await Json(response)).GetProperty("status").GetString());
        Assert.Equal(status, await Scalar<string>("SELECT status FROM contact_messages"));
    }

    [Fact]
    public async Task An_invalid_status_unknown_id_and_malformed_id_are_rejected()
    {
        var id = await SendAsync();

        var invalid = await AsAdmin(HttpMethod.Put, $"/api/messages/{id}", new { status = "archived" });
        var unknown = await AsAdmin(HttpMethod.Put, $"/api/messages/{Guid.NewGuid()}", new { status = "read" });
        var malformed = await AsAdmin(HttpMethod.Put, "/api/messages/not-a-guid", new { status = "read" });

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("Status must be one of: new, read, replied", await Field(invalid, "status"));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
        Assert.Equal("new", await Scalar<string>("SELECT status FROM contact_messages"));
    }

    // ---------------------------------------------------------------- reply

    [Fact]
    public async Task A_reply_is_201_with_the_log_entry_and_the_message_becomes_replied()
    {
        var id = await SendAsync();
        Factory.Clock.Advance(TimeSpan.FromHours(2));

        var response = await AsAdmin(HttpMethod.Post, $"/api/messages/{id}/reply", new { body = "  Yes, we can pick you up from the airport.  " });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var entry = await Json(response);
        Assert.Equal("reply", entry.GetProperty("kind").GetString());
        Assert.Equal("visitor@example.com", entry.GetProperty("to").GetString());
        Assert.Equal("Re: your message to Ceylon Roads", entry.GetProperty("subject").GetString());
        Assert.Equal("Yes, we can pick you up from the airport.", entry.GetProperty("body").GetString());
        Assert.Equal(id, entry.GetProperty("messageId").GetString());
        Assert.Equal("failed", entry.GetProperty("status").GetString());
        Assert.Equal("No email provider configured", entry.GetProperty("error").GetString());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("sentAt").ValueKind);
        Assert.Equal("replied", await Scalar<string>("SELECT status FROM contact_messages"));
        Assert.NotEqual(DBNull.Value, await Scalar<object>("SELECT replied_at FROM contact_messages"));
        Assert.Equal(2L, await Scalar<long>("SELECT count(*) FROM email_logs"));
        Assert.Equal(EmailKind.Reply, Factory.Emails.Messages[^1].Kind);
        Assert.Equal("Yes, we can pick you up from the airport.", Factory.Emails.Messages[^1].Body);
    }

    [Fact]
    public async Task A_custom_subject_is_used_and_the_business_name_comes_from_the_settings()
    {
        await using (var db = PostgresFixture.CreateContext(Connection))
        {
            await new DatabaseSeeder(db, new PasswordHasher(iterations: 1_000), NullLogger<DatabaseSeeder>.Instance).SeedAsync(new SeedOptions());
        }

        var id = await SendAsync();
        var custom = await AsAdmin(HttpMethod.Post, $"/api/messages/{id}/reply", new { subject = "About Ella", body = "Yes, of course." });
        var plain = await AsAdmin(HttpMethod.Post, $"/api/messages/{id}/reply", new { body = "And one more thing." });

        Assert.Equal("About Ella", (await Json(custom)).GetProperty("subject").GetString());
        Assert.Equal("Re: your message to Ceylon Roads", (await Json(plain)).GetProperty("subject").GetString());
    }

    [Fact]
    public async Task A_short_reply_is_400_and_records_nothing()
    {
        var id = await SendAsync();

        var response = await AsAdmin(HttpMethod.Post, $"/api/messages/{id}/reply", new { body = "ok" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Write a reply", await Field(response, "body"));
        Assert.Equal("new", await Scalar<string>("SELECT status FROM contact_messages"));
        Assert.Equal(1L, await Scalar<long>("SELECT count(*) FROM email_logs"));
    }

    [Fact]
    public async Task Replying_to_an_unknown_message_is_404()
    {
        var response = await AsAdmin(HttpMethod.Post, $"/api/messages/{Guid.NewGuid()}/reply", new { body = "Hello there" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM email_logs"));
    }

    // ---------------------------------------------------------------- delete

    [Fact]
    public async Task Deleting_a_message_keeps_its_emails_with_the_link_cleared()
    {
        var id = await SendAsync();

        var first = await AsAdmin(HttpMethod.Delete, $"/api/messages/{id}");
        var second = await AsAdmin(HttpMethod.Delete, $"/api/messages/{id}");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM contact_messages"));
        Assert.Equal(1L, await Scalar<long>("SELECT count(*) FROM email_logs"));
        Assert.Equal(DBNull.Value, await Scalar<object>("SELECT contact_message_id FROM email_logs"));

        var emails = await Json(await AsAdmin(HttpMethod.Get, "/api/emails"));
        Assert.Equal(1, emails.GetArrayLength());
        Assert.Equal(JsonValueKind.Null, emails[0].GetProperty("messageId").ValueKind);
    }

    // ---------------------------------------------------------------- email log

    [Fact]
    public async Task The_email_log_is_newest_first_with_the_documented_fields()
    {
        await SendAsync("first@example.com");
        Factory.Clock.Advance(TimeSpan.FromMinutes(5));
        await SendAsync("second@example.com");

        var list = await Json(await AsAdmin(HttpMethod.Get, "/api/emails"));

        Assert.Equal(["second@example.com", "first@example.com"], list.EnumerateArray().Select(e => e.GetProperty("to").GetString()));
        var entry = list[0];
        Assert.Equal(
            ["id", "from", "to", "subject", "body", "kind", "bookingId", "messageId", "status", "error", "createdAt", "sentAt"],
            entry.EnumerateObject().Select(p => p.Name));
        Assert.Equal("contact-ack", entry.GetProperty("kind").GetString());
        Assert.Equal("failed", entry.GetProperty("status").GetString());
        Assert.Equal("No email provider configured", entry.GetProperty("error").GetString());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("sentAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("bookingId").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("from").GetString()));
    }

    [Fact]
    public async Task The_limit_selects_the_newest_and_must_be_in_range()
    {
        for (var number = 1; number <= 4; number++)
        {
            await SendAsync($"v{number}@example.com");
            Factory.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        var two = await Json(await AsAdmin(HttpMethod.Get, "/api/emails?limit=2"));
        var all = await Json(await AsAdmin(HttpMethod.Get, "/api/emails"));
        var zero = await AsAdmin(HttpMethod.Get, "/api/emails?limit=0");
        var huge = await AsAdmin(HttpMethod.Get, "/api/emails?limit=5000");
        var text = await AsAdmin(HttpMethod.Get, "/api/emails?limit=abc");

        Assert.Equal(["v4@example.com", "v3@example.com"], two.EnumerateArray().Select(e => e.GetProperty("to").GetString()));
        Assert.Equal(4, all.GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        Assert.NotNull(await Field(zero, "limit"));
        Assert.Equal(HttpStatusCode.BadRequest, huge.StatusCode);
        Assert.NotNull(await Field(huge, "limit"));
        Assert.Equal(HttpStatusCode.BadRequest, text.StatusCode);
    }

    [Fact]
    public async Task The_log_shows_a_password_reset_email_without_the_real_token()
    {
        await Api.RegisterAsync("emma@example.com");
        await Api.PostAsync("/api/auth/forgot", new { email = "emma@example.com" });
        var token = Factory.Emails.LatestResetToken();

        var list = await Json(await AsAdmin(HttpMethod.Get, "/api/emails"));

        var reset = list.EnumerateArray().First(e => e.GetProperty("kind").GetString() == "password-reset");
        Assert.Contains("token=[REDACTED]", reset.GetProperty("body").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(token, list.ToString(), StringComparison.Ordinal);
        Assert.Contains(list.EnumerateArray(), e => e.GetProperty("kind").GetString() == "welcome");
    }

    // ---------------------------------------------------------------- seed

    [Fact]
    public async Task Seeded_messages_and_emails_can_be_read()
    {
        await using (var db = PostgresFixture.CreateContext(Connection))
        {
            await new DatabaseSeeder(db, new PasswordHasher(iterations: 1_000), NullLogger<DatabaseSeeder>.Instance).SeedAsync(new SeedOptions());
        }

        var messages = await Json(await AsAdmin(HttpMethod.Get, "/api/messages"));
        var emails = await Json(await AsAdmin(HttpMethod.Get, "/api/emails"));

        Assert.NotEmpty(messages.EnumerateArray());
        Assert.NotEmpty(emails.EnumerateArray());
        Assert.All(messages.EnumerateArray(), m => Assert.Contains(m.GetProperty("status").GetString(), new[] { "new", "read", "replied" }));
        Assert.All(emails.EnumerateArray(), e => Assert.Contains(e.GetProperty("status").GetString(), new[] { "queued", "sent", "failed" }));
    }
}
