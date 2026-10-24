using Microsoft.EntityFrameworkCore;
using Npgsql;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Infrastructure.Persistence;

namespace TravelWebAPI.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class SchemaTests(PostgresFixture postgres)
{
    private const string SqlStateCheckViolation = "23514";
    private const string SqlStateForeignKeyViolation = "23503";

    // The 25 tables of BE-ER-DIAGRAM.dbml. The table-count test fails when the model and this list drift apart.
    private static readonly string[] ExpectedTables =
    [
        "bookings", "contact_messages", "destination_images", "destinations", "email_logs", "exchange_rates",
        "faqs", "gallery_images", "itinerary_days", "media_files", "package_destinations", "package_images",
        "package_interests", "package_items", "packages", "password_reset_tokens", "payments", "refresh_tokens",
        "reviews", "service_details", "services", "site_settings", "travel_tips", "users", "vehicles",
    ];

    [Fact]
    public async Task Migration_creates_exactly_the_25_designed_tables()
    {
        var connection = await postgres.CreateDatabaseAsync();

        await using var db = PostgresFixture.CreateContext(connection);
        var tables = new List<string>();
        await using (var npgsql = new NpgsqlConnection(connection))
        {
            await npgsql.OpenAsync();
            await using var command = new NpgsqlCommand(
                "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_name <> '__EFMigrationsHistory' ORDER BY table_name",
                npgsql);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        Assert.Equal(25, ExpectedTables.Length);
        Assert.Equal(ExpectedTables, tables);
    }

    [Fact]
    public async Task Applying_migrations_twice_changes_nothing()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);

        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Single(await db.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task Column_names_are_snake_case()
    {
        var connection = await postgres.CreateDatabaseAsync();

        var count = await PostgresFixture.ScalarAsync<long>(
            connection,
            "SELECT count(*) FROM information_schema.columns WHERE table_name = 'bookings' AND column_name IN ('travel_date', 'travel_time', 'price_lkr', 'trip_plan', 'user_id')");

        Assert.Equal(5, count);
    }

    [Fact]
    public async Task Money_columns_are_lkr_decimals()
    {
        var connection = await postgres.CreateDatabaseAsync();

        var columns = await PostgresFixture.ScalarAsync<long>(
            connection,
            "SELECT count(*) FROM information_schema.columns WHERE column_name IN ('price_lkr', 'price_per_km_lkr', 'price_per_day_lkr', 'amount_lkr') AND data_type = 'numeric'");

        Assert.Equal(5, columns); // packages, vehicles x2, bookings, payments
    }

    [Fact]
    public async Task Database_rejects_a_value_outside_an_enum()
    {
        var connection = await postgres.CreateDatabaseAsync();

        var ex = await Assert.ThrowsAsync<PostgresException>(() => PostgresFixture.ExecuteAsync(
            connection,
            "INSERT INTO contact_messages (name, email, message, status) VALUES ('a', 'a@b.c', 'long enough message', 'bogus')"));

        Assert.Equal(SqlStateCheckViolation, ex.SqlState);
    }

    [Fact]
    public async Task Email_kinds_are_stored_with_the_hyphenated_contract_values()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);
        db.EmailLogs.Add(new EmailLog
        {
            Kind = EmailKind.PasswordReset,
            FromAddress = "from@example.com",
            ToAddress = "to@example.com",
            Subject = "Reset",
            Body = "Body",
        });
        await db.SaveChangesAsync();

        var stored = await PostgresFixture.ScalarAsync<string>(connection, "SELECT kind FROM email_logs");

        Assert.Equal("password-reset", stored);
    }

    [Fact]
    public async Task Rating_outside_1_to_5_is_rejected()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);
        db.Reviews.Add(new Review
        {
            Name = "A",
            Country = "UK",
            CountryCode = "GB",
            Rating = 6,
            TripType = "Couple",
            Comment = "Wonderful trip overall",
            ReviewDate = new DateOnly(2026, 1, 1),
        });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        Assert.Equal(SqlStateCheckViolation, ((PostgresException)ex.InnerException!).SqlState);
    }

    [Fact]
    public async Task Restricted_relations_refuse_the_delete()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);
        var user = TestData.User();
        var service = TestData.Service();
        db.AddRange(user, service);
        await db.SaveChangesAsync();
        db.Bookings.Add(TestData.Booking(user.Id, service.Id));
        await db.SaveChangesAsync();

        var serviceDelete = await Assert.ThrowsAsync<PostgresException>(
            () => PostgresFixture.ExecuteAsync(connection, "DELETE FROM services WHERE id = 'airport'"));
        var userDelete = await Assert.ThrowsAsync<PostgresException>(
            () => PostgresFixture.ExecuteAsync(connection, "DELETE FROM users"));

        Assert.Equal(SqlStateForeignKeyViolation, serviceDelete.SqlState);
        Assert.Equal(SqlStateForeignKeyViolation, userDelete.SqlState);
    }

    [Fact]
    public async Task A_destination_used_by_a_package_cannot_be_deleted()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);
        var package = TestData.Package();
        package.Destinations.Add(new PackageDestination { DestinationId = "sigiriya", Position = 1 });
        db.AddRange(TestData.Destination(), package);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<PostgresException>(
            () => PostgresFixture.ExecuteAsync(connection, "DELETE FROM destinations WHERE id = 'sigiriya'"));

        Assert.Equal(SqlStateForeignKeyViolation, ex.SqlState);
    }

    [Fact]
    public async Task Deleting_a_package_removes_its_children_but_keeps_destinations()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);
        var package = TestData.Package();
        package.Destinations.Add(new PackageDestination { DestinationId = "sigiriya", Position = 1 });
        package.ItineraryDays.Add(new ItineraryDay { Day = 1, Title = "Day 1", Description = "Drive" });
        package.Interests.Add(new PackageInterest { Interest = InterestType.Culture });
        package.Images.Add(new PackageImage { Src = "img:a" });
        package.Items.Add(new PackageItem { Kind = PackageItemKind.Included, Content = "Fuel" });
        db.AddRange(TestData.Destination(), package);
        await db.SaveChangesAsync();

        await PostgresFixture.ExecuteAsync(connection, "DELETE FROM packages WHERE id = 'cultural-triangle'");

        foreach (var table in new[] { "package_destinations", "itinerary_days", "package_interests", "package_images", "package_items" })
        {
            Assert.Equal(0, await PostgresFixture.ScalarAsync<long>(connection, $"SELECT count(*) FROM {table}"));
        }

        Assert.Equal(1, await PostgresFixture.ScalarAsync<long>(connection, "SELECT count(*) FROM destinations"));
    }

    [Fact]
    public async Task Deleting_a_booking_removes_payments_and_keeps_email_logs_unlinked()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);
        var user = TestData.User();
        var service = TestData.Service();
        db.AddRange(user, service);
        await db.SaveChangesAsync();
        var booking = TestData.Booking(user.Id, service.Id);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        db.Payments.Add(TestData.Payment(booking.Id));
        db.EmailLogs.Add(new EmailLog
        {
            BookingId = booking.Id,
            Kind = EmailKind.BookingReceived,
            FromAddress = "a@b.c",
            ToAddress = "d@e.f",
            Subject = "S",
            Body = "B",
        });
        await db.SaveChangesAsync();

        await PostgresFixture.ExecuteAsync(connection, "DELETE FROM bookings");

        Assert.Equal(0, await PostgresFixture.ScalarAsync<long>(connection, "SELECT count(*) FROM payments"));
        Assert.Equal(1, await PostgresFixture.ScalarAsync<long>(connection, "SELECT count(*) FROM email_logs"));
        Assert.Equal(1, await PostgresFixture.ScalarAsync<long>(connection, "SELECT count(*) FROM email_logs WHERE booking_id IS NULL"));
    }

    [Fact]
    public async Task Deleting_a_user_keeps_their_reviews_with_the_link_cleared()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);
        var user = TestData.User();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.Reviews.Add(new Review
        {
            UserId = user.Id,
            Name = "A",
            Country = "UK",
            CountryCode = "GB",
            Rating = 5,
            TripType = "Couple",
            Comment = "Wonderful trip overall",
            ReviewDate = new DateOnly(2026, 1, 1),
        });
        await db.SaveChangesAsync();

        await PostgresFixture.ExecuteAsync(connection, "DELETE FROM users");

        Assert.Equal(1, await PostgresFixture.ScalarAsync<long>(connection, "SELECT count(*) FROM reviews WHERE user_id IS NULL"));
    }

    [Theory]
    [InlineData(PaymentStatus.Pending, "pending")]
    [InlineData(PaymentStatus.Paid, "paid")]
    [InlineData(PaymentStatus.Refunded, "refunded")]
    public async Task A_payment_keeps_exactly_the_status_it_was_saved_with(PaymentStatus status, string stored)
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);
        var user = TestData.User();
        var service = TestData.Service();
        db.AddRange(user, service);
        await db.SaveChangesAsync();
        var booking = TestData.Booking(user.Id, service.Id);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        var payment = TestData.Payment(booking.Id);
        payment.Status = status;
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        Assert.Equal(stored, await PostgresFixture.ScalarAsync<string>(connection, "SELECT status FROM payments"));
    }

    [Fact]
    public async Task Email_is_unique()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);
        db.Users.AddRange(TestData.User("same@example.com"), TestData.User("same@example.com"));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        Assert.Equal("23505", ((PostgresException)ex.InnerException!).SqlState);
    }

    [Fact]
    public async Task Singleton_tables_only_accept_id_1()
    {
        var connection = await postgres.CreateDatabaseAsync();

        var ex = await Assert.ThrowsAsync<PostgresException>(() => PostgresFixture.ExecuteAsync(
            connection,
            "INSERT INTO travel_tips (id, regions, info) VALUES (2, '[]', '[]')"));

        Assert.Equal(SqlStateCheckViolation, ex.SqlState);
    }

    [Fact]
    public async Task Uuid_keys_are_version_7_and_timestamps_are_stamped_in_utc()
    {
        var connection = await postgres.CreateDatabaseAsync();
        var fixedNow = new DateTimeOffset(2030, 5, 1, 10, 0, 0, TimeSpan.Zero);
        await using var db = PostgresFixture.CreateContext(connection, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(fixedNow));
        var user = TestData.User();

        db.Users.Add(user);
        await db.SaveChangesAsync();

        Assert.Equal(7, (user.Id.ToByteArray(bigEndian: true)[6] >> 4));
        Assert.Equal(fixedNow, user.CreatedAt);
        Assert.Equal(fixedNow, user.UpdatedAt);

        user.Name = "Changed";
        await db.SaveChangesAsync();
        Assert.Equal(fixedNow, user.CreatedAt);
    }

    [Fact]
    public async Task A_failed_multi_write_operation_stores_nothing()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);
        IUnitOfWork unitOfWork = new UnitOfWork(db);
        IRepository<User> users = new EfRepository<User>(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await users.AddAsync(TestData.User("first@example.com"), ct);
            await unitOfWork.SaveChangesAsync(ct);
            throw new InvalidOperationException("second step failed");
        }));

        Assert.Equal(0, await PostgresFixture.ScalarAsync<long>(connection, "SELECT count(*) FROM users"));
    }

    [Fact]
    public async Task A_successful_transaction_is_committed()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);
        IUnitOfWork unitOfWork = new UnitOfWork(db);
        IRepository<User> users = new EfRepository<User>(db);

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await users.AddAsync(TestData.User("kept@example.com"), ct);
            await unitOfWork.SaveChangesAsync(ct);
        });

        var found = await users.GetByIdAsync((await db.Users.SingleAsync()).Id);
        Assert.NotNull(found);
        Assert.Equal(1, await PostgresFixture.ScalarAsync<long>(connection, "SELECT count(*) FROM users"));
    }
}
