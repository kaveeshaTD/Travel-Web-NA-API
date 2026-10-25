using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TravelWebAPI.Application.Rates;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Infrastructure.Services;

namespace TravelWebAPI.Tests.Unit;

/// <summary>The two web sources, read from stub answers shaped like the real ones (checked on 2026-10-25).</summary>
public sealed class ExchangeRateProviderTests
{
    private const string PrimaryBody = """
        {"result":"success","provider":"https://www.exchangerate-api.com","base_code":"LKR","time_last_update_unix":1792886551,
         "rates":{"LKR":1,"USD":0.003011,"EUR":0.002685,"GBP":0.002249,"JPY":0.4701}}
        """;

    private const string FallbackBody = """
        {"date":"2026-10-25","lkr":{"usd":0.0030137899,"eur":0.0026899039,"gbp":0.002252666,"jpy":0.47}}
        """;

    private readonly ExchangeRateOptions _options = new();

    private PrimaryExchangeRateProvider Primary(StubHandler handler)
        => new(new HttpClient(handler), Options.Create(_options), NullLogger<PrimaryExchangeRateProvider>.Instance);

    private FallbackExchangeRateProvider Fallback(StubHandler handler)
        => new(new HttpClient(handler), Options.Create(_options), NullLogger<FallbackExchangeRateProvider>.Instance);

    private static decimal Inverse(decimal perLkr) => decimal.Round(1m / perLkr, 6);

    // ---------------------------------------------------------------- success

    [Fact]
    public async Task The_primary_source_is_read_and_each_rate_is_inverted_to_lkr_per_unit()
    {
        var handler = StubHandler.Json(PrimaryBody);

        var quote = await Primary(handler).FetchAsync();

        Assert.NotNull(quote);
        Assert.Equal("open.er-api.com", quote.Source);
        Assert.Equal(Inverse(0.003011m), quote.RatesLkr[CurrencyCode.Usd]);
        Assert.Equal(Inverse(0.002685m), quote.RatesLkr[CurrencyCode.Eur]);
        Assert.Equal(Inverse(0.002249m), quote.RatesLkr[CurrencyCode.Gbp]);
        Assert.Equal(3, quote.RatesLkr.Count);
        Assert.InRange(quote.RatesLkr[CurrencyCode.Usd], 332.1m, 332.2m);
        Assert.Equal(_options.PrimaryUrl, handler.Requests.Single().RequestUri!.ToString());
    }

    [Fact]
    public async Task The_fallback_source_is_read_with_lower_case_codes()
    {
        var handler = StubHandler.Json(FallbackBody);

        var quote = await Fallback(handler).FetchAsync();

        Assert.NotNull(quote);
        Assert.Equal("currency-api", quote.Source);
        Assert.Equal(Inverse(0.0030137899m), quote.RatesLkr[CurrencyCode.Usd]);
        Assert.Equal(Inverse(0.0026899039m), quote.RatesLkr[CurrencyCode.Eur]);
        Assert.Equal(Inverse(0.002252666m), quote.RatesLkr[CurrencyCode.Gbp]);
        Assert.Equal(_options.FallbackUrl, handler.Requests.Single().RequestUri!.ToString());
    }

    [Fact]
    public async Task The_urls_come_from_the_options()
    {
        _options.PrimaryUrl = "https://rates.example.com/lkr";
        var handler = StubHandler.Json(PrimaryBody);

        await Primary(handler).FetchAsync();

        Assert.Equal("https://rates.example.com/lkr", handler.Requests.Single().RequestUri!.ToString());
    }

    [Theory]
    [InlineData(0.003011, 332.115576)]
    [InlineData(1, 1)]
    [InlineData(0.5, 2)]
    public void Invert_gives_lkr_per_unit_rounded_to_six_decimals(double perLkr, double expected)
        => Assert.Equal((decimal)expected, HttpExchangeRateProvider.Invert((decimal)perLkr));

    [Theory]
    [InlineData(0)]
    [InlineData(-0.003)]
    [InlineData(0.00000000001)]
    public void Invert_refuses_values_that_cannot_be_used(double perLkr)
        => Assert.Null(HttpExchangeRateProvider.Invert((decimal)perLkr));

    // ---------------------------------------------------------------- bad answers

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_status_other_than_success_gives_null(HttpStatusCode status)
    {
        Assert.Null(await Primary(StubHandler.Status(status)).FetchAsync());
        Assert.Null(await Fallback(StubHandler.Status(status)).FetchAsync());
    }

    [Fact]
    public async Task A_network_error_gives_null()
    {
        Assert.Null(await Primary(StubHandler.Throws(new HttpRequestException("no route to host"))).FetchAsync());
        Assert.Null(await Fallback(StubHandler.Throws(new HttpRequestException("no route to host"))).FetchAsync());
    }

    [Fact]
    public async Task A_timeout_gives_null()
    {
        var timeout = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException());

        Assert.Null(await Primary(StubHandler.Throws(timeout)).FetchAsync());
    }

    [Fact]
    public async Task Shutdown_during_a_fetch_is_not_swallowed()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Primary(StubHandler.Json(PrimaryBody)).FetchAsync(cancelled.Token));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("[1,2,3]")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"result\":\"success\"}")]
    public async Task Answers_in_the_wrong_format_give_null(string body)
    {
        Assert.Null(await Primary(StubHandler.Json(body)).FetchAsync());
        Assert.Null(await Fallback(StubHandler.Json(body)).FetchAsync());
    }

    [Fact]
    public async Task The_primary_answer_must_say_success_and_be_for_lkr()
    {
        var failed = PrimaryBody.Replace("\"success\"", "\"error\"", StringComparison.Ordinal);
        var otherBase = PrimaryBody.Replace("\"base_code\":\"LKR\"", "\"base_code\":\"USD\"", StringComparison.Ordinal);

        Assert.Null(await Primary(StubHandler.Json(failed)).FetchAsync());
        Assert.Null(await Primary(StubHandler.Json(otherBase)).FetchAsync());
    }

    [Fact]
    public async Task A_missing_currency_is_left_out_so_the_checks_can_name_it()
    {
        var withoutGbp = PrimaryBody.Replace("\"GBP\":0.002249,", string.Empty, StringComparison.Ordinal);

        var quote = await Primary(StubHandler.Json(withoutGbp)).FetchAsync();

        Assert.NotNull(quote);
        Assert.False(quote.RatesLkr.ContainsKey(CurrencyCode.Gbp));
        Assert.Contains("GBP", RateCheck.Validate(quote.RatesLkr, new Dictionary<CurrencyCode, decimal>(), 25), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"USD\":0.003011", "\"USD\":0")]
    [InlineData("\"USD\":0.003011", "\"USD\":-0.003")]
    [InlineData("\"USD\":0.003011", "\"USD\":\"0.003011\"")]
    [InlineData("\"USD\":0.003011", "\"USD\":null")]
    public async Task Unusable_numbers_are_left_out(string original, string replacement)
    {
        var quote = await Primary(StubHandler.Json(PrimaryBody.Replace(original, replacement, StringComparison.Ordinal))).FetchAsync();

        Assert.NotNull(quote);
        Assert.False(quote.RatesLkr.ContainsKey(CurrencyCode.Usd));
        Assert.True(quote.RatesLkr.ContainsKey(CurrencyCode.Eur));
    }

    [Fact]
    public async Task An_answer_with_none_of_the_currencies_gives_null()
    {
        const string onlyOthers = "{\"result\":\"success\",\"base_code\":\"LKR\",\"rates\":{\"JPY\":0.47}}";

        Assert.Null(await Primary(StubHandler.Json(onlyOthers)).FetchAsync());
    }

    // ---------------------------------------------------------------- size

    [Fact]
    public async Task An_answer_that_declares_a_huge_size_is_not_read()
    {
        var handler = StubHandler.Json(PrimaryBody, declaredLength: HttpExchangeRateProvider.MaxBodyBytes + 1);

        Assert.Null(await Primary(handler).FetchAsync());
    }

    [Fact]
    public async Task An_answer_that_turns_out_to_be_huge_is_cut_off()
    {
        var padding = new string(' ', HttpExchangeRateProvider.MaxBodyBytes + 10);
        var handler = StubHandler.Respond(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(PrimaryBody + padding))),
        });

        Assert.Null(await Primary(handler).FetchAsync());
    }

    // ---------------------------------------------------------------- the stub

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public static StubHandler Respond(Func<HttpRequestMessage, HttpResponseMessage> respond) => new(respond);

        public static StubHandler Json(string body, long? declaredLength = null) => new(_ =>
        {
            var content = new StringContent(body, Encoding.UTF8, "application/json");
            if (declaredLength is not null)
            {
                content.Headers.ContentLength = declaredLength;
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

        public static StubHandler Status(HttpStatusCode status) => new(_ => new HttpResponseMessage(status));

        public static StubHandler Throws(Exception exception) => new(_ => throw exception);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }
}
