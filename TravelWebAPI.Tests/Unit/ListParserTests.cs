using TravelWebAPI.Application.Lists;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Domain.Exceptions;

namespace TravelWebAPI.Tests.Unit;

public sealed class ListParserTests
{
    private static readonly System.Text.Json.JsonSerializerOptions Web = new(System.Text.Json.JsonSerializerDefaults.Web);

    private static readonly ListDefinition Definition = new(["createdAt", "name", "status"], "-createdAt", ["status"]);

    private static (ListQuery Query, FieldErrors Errors) Parse(ListQueryRequest request)
    {
        var errors = new FieldErrors();
        var query = ListParser.Parse(request, Definition, errors);
        return (query, errors);
    }

    // ---------------------------------------------------------------- paging

    [Fact]
    public void Nothing_sent_gives_page_1_of_20_and_the_default_sort()
    {
        var (query, errors) = Parse(new ListQueryRequest());

        Assert.False(errors.Any);
        Assert.Equal(1, query.Page);
        Assert.Equal(20, query.PageSize);
        Assert.Equal(0, query.Skip);
        Assert.Equal(string.Empty, query.Search);
        Assert.Equal([new SortTerm("createdAt", true)], query.Sort);
        Assert.Empty(query.Facets);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("7", 7)]
    [InlineData(" 3 ", 3)]
    [InlineData("2147483647", int.MaxValue)]
    public void A_good_page_is_used(string page, int expected)
        => Assert.Equal(expected, Parse(new ListQueryRequest { Page = page }).Query.Page);

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("+2")]
    [InlineData("99999999999")]
    public void A_wrong_page_gets_its_message(string page)
    {
        var (query, errors) = Parse(new ListQueryRequest { Page = page });

        Assert.Equal("Page must be a whole number of 1 or more", errors.Fields["page"]);
        Assert.Equal(1, query.Page);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("100", 100)]
    [InlineData("25", 25)]
    public void A_good_page_size_is_used(string size, int expected)
        => Assert.Equal(expected, Parse(new ListQueryRequest { PageSize = size }).Query.PageSize);

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("500")]
    [InlineData("many")]
    public void A_wrong_page_size_gets_its_message(string size)
    {
        var (query, errors) = Parse(new ListQueryRequest { PageSize = size });

        Assert.Equal("Page size must be a whole number from 1 to 100", errors.Fields["pageSize"]);
        Assert.Equal(20, query.PageSize);
    }

    [Fact]
    public void Both_wrong_values_are_reported_together()
    {
        var (_, errors) = Parse(new ListQueryRequest { Page = "0", PageSize = "500", Sort = "password", Facets = "nope" });

        Assert.Equal(["page", "pageSize", "sort", "facets"], errors.Fields.Keys);
    }

    [Fact]
    public void Throwing_reports_every_field_in_one_validation_error()
    {
        var (_, errors) = Parse(new ListQueryRequest { Page = "x", PageSize = "y" });

        var thrown = Assert.Throws<ValidationException>(errors.ThrowIfAny);
        Assert.Equal(2, thrown.Fields.Count);
    }

    [Fact]
    public void Nothing_is_thrown_when_everything_is_fine()
        => new FieldErrors().ThrowIfAny();

    // ---------------------------------------------------------------- search

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("  anna  ", "anna")]
    public void Search_is_trimmed(string? text, string expected)
        => Assert.Equal(expected, Parse(new ListQueryRequest { Search = text }).Query.Search);

    [Fact]
    public void Search_is_cut_to_100_characters()
        => Assert.Equal(100, Parse(new ListQueryRequest { Search = new string('x', 500) }).Query.Search.Length);

    [Theory]
    [InlineData("100%", "%100\\%%")]
    [InlineData("a_b", "%a\\_b%")]
    [InlineData("a\\b", "%a\\\\b%")]
    [InlineData("plain", "%plain%")]
    public void Wildcards_become_plain_characters(string text, string pattern)
        => Assert.Equal(pattern, LikePattern.Contains(text));

    // ---------------------------------------------------------------- sort

    [Fact]
    public void Sort_reads_the_descending_prefix_and_keeps_the_order_of_the_terms()
    {
        var (query, errors) = Parse(new ListQueryRequest { Sort = "-status, name" });

        Assert.False(errors.Any);
        Assert.Equal([new SortTerm("status", true), new SortTerm("name", false)], query.Sort);
    }

    [Fact]
    public void Sort_field_names_ignore_letter_case_and_use_the_canonical_name()
    {
        var (query, _) = Parse(new ListQueryRequest { Sort = "-CREATEDat" });

        Assert.Equal([new SortTerm("createdAt", true)], query.Sort);
    }

    [Fact]
    public void Sorting_by_the_same_field_twice_counts_once()
    {
        var (query, _) = Parse(new ListQueryRequest { Sort = "name,-name" });

        Assert.Equal([new SortTerm("name", false)], query.Sort);
    }

    [Fact]
    public void Three_terms_are_fine_and_four_are_not()
    {
        Assert.False(Parse(new ListQueryRequest { Sort = "name,status,createdAt" }).Errors.Any);

        var (query, errors) = Parse(new ListQueryRequest { Sort = "name,status,createdAt,-name" });
        Assert.Equal("Sort by at most 3 fields", errors.Fields["sort"]);
        Assert.Equal([new SortTerm("createdAt", true)], query.Sort);
    }

    [Fact]
    public void An_unknown_sort_field_names_the_allowed_ones()
    {
        var (_, errors) = Parse(new ListQueryRequest { Sort = "name,password" });

        Assert.Equal("Sort by one of: createdAt, name, status", errors.Fields["sort"]);
    }

    [Fact]
    public void Empty_pieces_in_the_sort_are_ignored_and_a_sort_of_only_commas_means_the_default()
    {
        Assert.Equal([new SortTerm("name", false)], Parse(new ListQueryRequest { Sort = ",name," }).Query.Sort);
        Assert.Equal([new SortTerm("createdAt", true)], Parse(new ListQueryRequest { Sort = ",," }).Query.Sort);
    }

    [Fact]
    public void A_default_sort_can_have_several_terms()
    {
        var terms = ListParser.ParseDefaultSort("-paidAt,-createdAt,name");

        Assert.Equal([new SortTerm("paidAt", true), new SortTerm("createdAt", true), new SortTerm("name", false)], terms);
    }

    // ---------------------------------------------------------------- facets

    [Fact]
    public void Facets_must_be_declared_by_the_list()
    {
        var (query, errors) = Parse(new ListQueryRequest { Facets = " status , STATUS " });

        Assert.False(errors.Any);
        Assert.Equal(["status"], query.Facets);

        Assert.Equal("Facets can be: status", Parse(new ListQueryRequest { Facets = "role" }).Errors.Fields["facets"]);
    }

    [Fact]
    public void A_list_without_facets_says_so()
    {
        var errors = new FieldErrors();
        ListParser.Parse(new ListQueryRequest { Facets = "status" }, new ListDefinition(["name"], "name"), errors);

        Assert.Equal("This list has no facets", errors.Fields["facets"]);
    }

    // ---------------------------------------------------------------- filters

    [Fact]
    public void Wire_filters_read_the_api_spelling_and_report_wrong_ones()
    {
        var errors = new FieldErrors();

        Assert.Equal(PaymentMethod.BankTransfer, ListFilters.Wire<PaymentMethod>(" bank_transfer ", "method", "m", errors));
        Assert.Null(ListFilters.Wire<PaymentMethod>(null, "method", "m", errors));
        Assert.Null(ListFilters.Wire<PaymentMethod>("", "method", "m", errors));
        Assert.False(errors.Any);

        Assert.Null(ListFilters.Wire<PaymentMethod>("Cash", "method", "Method must be one of: x", errors));
        Assert.Equal("Method must be one of: x", errors.Fields["method"]);
    }

    [Fact]
    public void The_one_of_message_lists_the_wire_values()
        => Assert.Equal("Status must be one of: pending, paid, refunded", ListFilters.OneOf<PaymentStatus>("Status"));

    [Theory]
    [InlineData("2026-11-03", true)]
    [InlineData(" 2026-11-03 ", true)]
    [InlineData("2026-02-30", false)]
    [InlineData("03/11/2026", false)]
    [InlineData("tomorrow", false)]
    public void Dates_are_real_calendar_days(string text, bool valid)
    {
        var errors = new FieldErrors();

        var date = ListFilters.Date(text, "from", errors);

        Assert.Equal(valid, date is not null);
        Assert.Equal(!valid, errors.Any);
        if (!valid)
        {
            Assert.Equal("Choose a valid date", errors.Fields["from"]);
        }
    }

    [Fact]
    public void Numbers_are_checked_against_their_range()
    {
        var errors = new FieldErrors();

        Assert.Equal(5, ListFilters.Number("5", "rating", 1, 5, "bad", errors));
        Assert.Null(ListFilters.Number(null, "rating", 1, 5, "bad", errors));
        Assert.False(errors.Any);
        Assert.Null(ListFilters.Number("9", "rating", 1, 5, "Rating must be a whole number from 1 to 5", errors));
        Assert.Equal("Rating must be a whole number from 1 to 5", errors.Fields["rating"]);
    }

    [Fact]
    public void Text_filters_are_trimmed_and_empty_ones_are_dropped()
    {
        Assert.Equal("hi", ListFilters.Text("  hi "));
        Assert.Null(ListFilters.Text("   "));
        Assert.Null(ListFilters.Text(null));
    }

    [Fact]
    public void The_first_message_for_a_field_wins()
    {
        var errors = new FieldErrors();
        errors.Add("status", "first");
        errors.Add("status", "second");

        Assert.Equal("first", errors.Fields["status"]);
    }

    // ---------------------------------------------------------------- the answer

    [Theory]
    [InlineData(0, 20, 0)]
    [InlineData(1, 20, 1)]
    [InlineData(20, 20, 1)]
    [InlineData(21, 20, 2)]
    [InlineData(45, 20, 3)]
    [InlineData(100, 10, 10)]
    public void Total_pages_round_up_and_are_zero_when_nothing_matches(int total, int size, int pages)
    {
        var query = new ListQuery(1, size, string.Empty, [], []);

        var result = Paged.Of<int>([], query, total);

        Assert.Equal(pages, result.TotalPages);
        Assert.Equal(total, result.TotalCount);
        Assert.Null(result.Facets);
    }

    [Fact]
    public void The_skip_follows_the_page()
        => Assert.Equal(40, new ListQuery(3, 20, string.Empty, [], []).Skip);

    [Fact]
    public void Mapping_keeps_the_totals_and_facets()
    {
        var facets = new Dictionary<string, IReadOnlyDictionary<string, int>> { ["status"] = new Dictionary<string, int> { ["new"] = 2 } };
        var result = Paged.Of<int>([1, 2], new ListQuery(2, 2, string.Empty, [], []), 7, facets);

        var mapped = result.Map(n => $"#{n}");

        Assert.Equal(["#1", "#2"], mapped.Items);
        Assert.Equal((2, 2, 7, 4), (mapped.Page, mapped.PageSize, mapped.TotalCount, mapped.TotalPages));
        Assert.Same(facets, mapped.Facets);
    }

    [Fact]
    public void The_answer_is_written_without_facets_unless_they_were_asked_for()
    {
        var query = new ListQuery(1, 20, string.Empty, [], []);
        var plain = System.Text.Json.JsonSerializer.Serialize(Paged.Of<int>([1], query, 1), Web);
        var withFacets = System.Text.Json.JsonSerializer.Serialize(
            Paged.Of<int>([1], query, 1, new Dictionary<string, IReadOnlyDictionary<string, int>> { ["status"] = new Dictionary<string, int> { ["new"] = 1 } }),
            Web);

        Assert.Equal("""{"items":[1],"page":1,"pageSize":20,"totalCount":1,"totalPages":1}""", plain);
        Assert.Contains("\"facets\":{\"status\":{\"new\":1}}", withFacets);
    }
}
