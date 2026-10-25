using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TravelWebAPI.Application.Rates;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Tests.Integration.Support;

namespace TravelWebAPI.Tests.Integration;

/// <summary>Settings with exchange rates, and the refresher running inside the real API with a fake rate source.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ExchangeRateApiTests(PostgresFixture postgres) : ContentTestBase(postgres)
{
    private readonly FakeSource _source = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IExchangeRateProvider>();
        services.AddSingleton<IExchangeRateProvider>(_source);
    }

    private async Task<JsonElement> ReadSettingsAsync() => await Json(await Api.GetAsync("/api/settings"));

    private static readonly DateTimeOffset Fetched = new(2026, 10, 25, 6, 0, 0, TimeSpan.Zero);

    private Task AddRatesAsync(decimal usd, decimal eur, decimal gbp, DateTimeOffset at, string source = "stored-source") => Execute(
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"""
        INSERT INTO exchange_rates (id, currency, rate_lkr, source, fetched_at) VALUES
          (gen_random_uuid(), 'USD', {usd}, '{source}', '{at:u}'),
          (gen_random_uuid(), 'EUR', {eur}, '{source}', '{at:u}'),
          (gen_random_uuid(), 'GBP', {gbp}, '{source}', '{at:u}')
        """));

    // ---------------------------------------------------------------- the settings response

    [Fact]
    public async Task With_nothing_stored_the_settings_carry_the_fallback_rates()
    {
        var settings = await ReadSettingsAsync();

        var rates = settings.GetProperty("currencyRates");
        Assert.Equal(["LKR", "USD", "EUR", "GBP"], rates.EnumerateObject().Select(p => p.Name));
        Assert.Equal(1m, rates.GetProperty("LKR").GetDecimal());
        Assert.Equal(330m, rates.GetProperty("USD").GetDecimal());
        Assert.Equal(360m, rates.GetProperty("EUR").GetDecimal());
        Assert.Equal(420m, rates.GetProperty("GBP").GetDecimal());
        Assert.Equal(JsonValueKind.Null, settings.GetProperty("currencyRatesAsOf").ValueKind);
        Assert.Equal("fallback", settings.GetProperty("currencyRatesSource").GetString());
    }

    [Fact]
    public async Task Stored_rates_are_served_with_their_time_and_source_and_the_newest_wins()
    {
        await AddRatesAsync(300m, 330m, 380m, Fetched.AddDays(-3), "older");
        await AddRatesAsync(332.115576m, 372.439478m, 444.642063m, Fetched, "open.er-api.com");

        var settings = await ReadSettingsAsync();

        var rates = settings.GetProperty("currencyRates");
        Assert.Equal(1m, rates.GetProperty("LKR").GetDecimal());
        Assert.Equal(332.115576m, rates.GetProperty("USD").GetDecimal());
        Assert.Equal(372.439478m, rates.GetProperty("EUR").GetDecimal());
        Assert.Equal(444.642063m, rates.GetProperty("GBP").GetDecimal());
        Assert.Equal(Fetched, settings.GetProperty("currencyRatesAsOf").GetDateTimeOffset());
        Assert.Equal("open.er-api.com", settings.GetProperty("currencyRatesSource").GetString());
    }

    [Fact]
    public async Task Saving_settings_with_rates_changes_nothing_and_the_answer_still_has_the_stored_rates()
    {
        await AddRatesAsync(332m, 372m, 444m, Fetched);
        var body = new Dictionary<string, object?>
        {
            ["name"] = "Ceylon Roads",
            ["driverName"] = "Nuwan Dissanayake",
            ["phone"] = "+94 77 123 4567",
            ["whatsappNumber"] = "94771234567",
            ["email"] = "hello@ceylonroads.example",
            ["currencyRates"] = new { LKR = 1, USD = 1, EUR = 1, GBP = 1 },
            ["currencyRatesAsOf"] = "2000-01-01T00:00:00Z",
            ["currencyRatesSource"] = "hacked",
        };

        var response = await AsAdmin(HttpMethod.Put, "/api/settings", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await Json(response);
        Assert.Equal(332m, saved.GetProperty("currencyRates").GetProperty("USD").GetDecimal());
        Assert.Equal("stored-source", saved.GetProperty("currencyRatesSource").GetString());
        Assert.Equal(Fetched, saved.GetProperty("currencyRatesAsOf").GetDateTimeOffset());
        Assert.Equal(3L, await Scalar<long>("SELECT count(*) FROM exchange_rates"));
        Assert.True(SameJson(saved, await ReadSettingsAsync()));
    }

    // ---------------------------------------------------------------- the refresher inside the API

    [Fact]
    public async Task The_refresher_is_off_in_the_test_host_and_makes_no_request()
    {
        await ReadSettingsAsync();
        await Task.Delay(500);

        Assert.Equal(0, _source.Calls);
        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM exchange_rates"));
    }

    [Fact]
    public async Task A_running_refresher_stores_rates_shortly_after_start_and_the_settings_show_them()
    {
        await using var factory = ApiFactory.With(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = Connection,
                ["ExchangeRates:Enabled"] = "true",
                ["ExchangeRates:StartupDelaySeconds"] = "0",
            },
            configure: ConfigureServices);
        var api = new AuthApi(AuthApi.CreateClient(factory));
        await api.GetAsync("/api/health");

        await WaitForAsync(async () => await Scalar<long>("SELECT count(*) FROM exchange_rates") == 3);

        var settings = await Json(await api.GetAsync("/api/settings"));
        Assert.Equal(1, _source.Calls);
        Assert.Equal(332m, settings.GetProperty("currencyRates").GetProperty("USD").GetDecimal());
        Assert.Equal("fake-source", settings.GetProperty("currencyRatesSource").GetString());
        Assert.NotEqual(JsonValueKind.Null, settings.GetProperty("currencyRatesAsOf").ValueKind);
        Assert.Equal(1L, await Scalar<long>("SELECT count(DISTINCT fetched_at) FROM exchange_rates"));
        Assert.Contains(factory.Logs.Entries, entry => entry.Level == LogLevel.Information
            && entry.Message.Contains("Exchange rates updated from", StringComparison.Ordinal)
            && entry.Message.Contains("fake-source", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_restart_soon_after_a_fetch_makes_no_new_request()
    {
        await AddRatesAsync(332m, 372m, 444m, DateTimeOffset.UtcNow.AddHours(-2));
        await using var factory = ApiFactory.With(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = Connection,
                ["ExchangeRates:Enabled"] = "true",
                ["ExchangeRates:StartupDelaySeconds"] = "0",
            },
            configure: ConfigureServices);
        var api = new AuthApi(AuthApi.CreateClient(factory));

        await api.GetAsync("/api/health");
        await Task.Delay(1500);

        Assert.Equal(0, _source.Calls);
        Assert.Equal(3L, await Scalar<long>("SELECT count(*) FROM exchange_rates"));
    }

    [Fact]
    public async Task A_failing_source_is_logged_and_the_api_keeps_answering_with_the_old_rates()
    {
        _source.Quote = null;
        await AddRatesAsync(332m, 372m, 444m, DateTimeOffset.UtcNow.AddHours(-30));
        await using var factory = ApiFactory.With(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = Connection,
                ["ExchangeRates:Enabled"] = "true",
                ["ExchangeRates:StartupDelaySeconds"] = "0",
            },
            configure: ConfigureServices);
        var api = new AuthApi(AuthApi.CreateClient(factory));
        await api.GetAsync("/api/health");

        await WaitForAsync(() => Task.FromResult(_source.Calls >= 1 && factory.Logs.Entries.Any(e => e.Message.Contains("could not be refreshed", StringComparison.Ordinal))));

        var response = await api.GetAsync("/api/settings");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(332m, (await Json(response)).GetProperty("currencyRates").GetProperty("USD").GetDecimal());
        Assert.Equal(3L, await Scalar<long>("SELECT count(*) FROM exchange_rates"));
        Assert.Contains(factory.Logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("fake-source: no usable answer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_source_that_throws_does_not_take_the_api_down()
    {
        _source.Throw = new HttpRequestException("connection refused");
        await using var factory = ApiFactory.With(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = Connection,
                ["ExchangeRates:Enabled"] = "true",
                ["ExchangeRates:StartupDelaySeconds"] = "0",
            },
            configure: ConfigureServices);
        var api = new AuthApi(AuthApi.CreateClient(factory));
        await api.GetAsync("/api/health");

        await WaitForAsync(() => Task.FromResult(factory.Logs.Entries.Any(e => e.Message.Contains("connection refused", StringComparison.Ordinal))));

        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("/api/settings")).StatusCode);
        Assert.Equal(330m, (await Json(await api.GetAsync("/api/settings"))).GetProperty("currencyRates").GetProperty("USD").GetDecimal());
    }

    // ---------------------------------------------------------------- configuration

    [Fact]
    public async Task Production_refuses_to_start_with_a_fallback_rate_of_zero_and_names_the_setting()
    {
        var settings = ApiFactory.ProductionSettings(Connection);
        settings["ExchangeRates:Fallback:Usd"] = "0";
        await using var factory = ApiFactory.With(settings, "Production");

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ExchangeRates:Fallback", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_refuses_a_non_https_rate_source_when_refreshing_is_on()
    {
        var settings = ApiFactory.ProductionSettings(Connection);
        settings["ExchangeRates:Enabled"] = "true";
        settings["ExchangeRates:PrimaryUrl"] = "http://rates.example.com/lkr";
        await using var factory = ApiFactory.With(settings, "Production");

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("PrimaryUrl", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_starts_with_the_default_rate_settings()
    {
        await using var factory = ApiFactory.With(ApiFactory.ProductionSettings(Connection), "Production");

        using var client = factory.CreateClient();

        Assert.NotNull(client);
    }

    // ---------------------------------------------------------------- helpers

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.Fail("Timed out waiting for the exchange rate refresh");
    }

    private sealed class FakeSource : IExchangeRateProvider
    {
        private int _calls;

        public string Name => "fake-source";

        public int Calls => _calls;

        public RateQuote? Quote { get; set; } = new("fake-source", new Dictionary<CurrencyCode, decimal>
        {
            [CurrencyCode.Usd] = 332m,
            [CurrencyCode.Eur] = 372m,
            [CurrencyCode.Gbp] = 444m,
        });

        public Exception? Throw { get; set; }

        public Task<RateQuote?> FetchAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return Throw is null ? Task.FromResult(Quote) : Task.FromException<RateQuote?>(Throw);
        }
    }
}
