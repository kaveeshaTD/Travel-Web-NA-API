using TravelWebAPI.Application.Content;
using TravelWebAPI.Domain.Exceptions;

namespace TravelWebAPI.Tests.Unit;

public sealed class SlugGeneratorTests
{
    [Theory]
    [InlineData("Cultural Triangle", "cultural-triangle")]
    [InlineData("  Hill   Country -- Escape!  ", "hill-country-escape")]
    [InlineData("Café Colombo", "cafe-colombo")]
    [InlineData("Do you provide child seats?", "do-you-provide-child-seats")]
    [InlineData("Day 1/2: Kandy", "day-1-2-kandy")]
    public void Slugify_makes_readable_lower_case_ids(string text, string expected)
        => Assert.Equal(expected, SlugGenerator.Slugify(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!! ???")]
    [InlineData("සිගිරිය")]
    public void Slugify_falls_back_when_nothing_usable_is_left(string? text)
        => Assert.Equal(SlugGenerator.Fallback, SlugGenerator.Slugify(text));

    [Fact]
    public void Slugify_cuts_long_text_without_a_trailing_dash()
    {
        var slug = SlugGenerator.Slugify(new string('a', 99) + " bbbbbb");

        Assert.True(slug.Length <= SlugGenerator.MaxLength);
        Assert.False(slug.EndsWith('-'));
    }

    [Fact]
    public void Unique_returns_the_plain_slug_when_it_is_free()
        => Assert.Equal("galle", SlugGenerator.Unique("Galle", new HashSet<string>()));

    [Fact]
    public void Unique_adds_the_first_free_number()
    {
        var taken = new HashSet<string> { "galle", "galle-2", "galle-4" };

        Assert.Equal("galle-3", SlugGenerator.Unique("Galle", taken));
    }

    [Fact]
    public void The_reserved_word_is_never_an_id()
        => Assert.Equal("reorder-2", SlugGenerator.Unique("Reorder", new HashSet<string>()));

    [Fact]
    public void A_suffix_never_pushes_the_id_over_the_limit()
    {
        var longName = new string('x', 120);
        var first = SlugGenerator.Unique(longName, new HashSet<string>());
        var second = SlugGenerator.Unique(longName, new HashSet<string> { first });

        Assert.Equal(SlugGenerator.MaxLength, first.Length);
        Assert.True(second.Length <= SlugGenerator.MaxLength);
        Assert.NotEqual(first, second);
        Assert.EndsWith("-2", second);
    }

    [Fact]
    public void The_lookup_prefix_is_shared_by_every_candidate()
    {
        var text = new string('y', 150);
        var prefix = SlugGenerator.LookupPrefix(text);
        var taken = new HashSet<string> { SlugGenerator.Unique(text, new HashSet<string>()) };

        Assert.StartsWith(prefix, SlugGenerator.Unique(text, taken));
        Assert.StartsWith(prefix, SlugGenerator.Unique(text, new HashSet<string>()));
    }
}

public sealed class ImageReferenceTests
{
    [Theory]
    [InlineData("https://images.example.com/a.jpg")]
    [InlineData("http://localhost:5115/uploads/a.png")]
    [InlineData("/uploads/3f2a9c.webp")]
    [InlineData("/uploads/a_b-c.1.jpg")]
    [InlineData("img:sigiriya")]
    [InlineData("img:nuwara-eliya")]
    public void Accepts_links_uploads_and_website_photos(string value)
        => Assert.True(ImageReference.IsValid(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" https://a.example/a.jpg")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("ftp://example.com/a.jpg")]
    [InlineData("//example.com/a.jpg")]
    [InlineData("https://")]
    [InlineData("https://exa mple.com/a.jpg")]
    [InlineData("/uploads/../appsettings.json")]
    [InlineData("/uploads/a/b.jpg")]
    [InlineData("/uploads/")]
    [InlineData("/other/a.jpg")]
    [InlineData("img:")]
    [InlineData("img:a b")]
    [InlineData("img:../x")]
    public void Rejects_everything_else(string? value)
        => Assert.False(ImageReference.IsValid(value));

    [Fact]
    public void Rejects_values_longer_than_the_column()
        => Assert.False(ImageReference.IsValid("https://example.com/" + new string('a', ImageReference.MaxLength)));
}

public sealed class OrderingTests
{
    private static readonly string[] Current = ["a", "b", "c", "d"];

    private static ReorderRequest Request(params string[] ids) => new(ids);

    [Fact]
    public void Listed_ids_come_first_in_the_given_order()
        => Assert.Equal(["c", "a", "b", "d"], Ordering.Reorder(Current, Request("c", "a"), x => x));

    [Fact]
    public void A_full_list_replaces_the_order()
        => Assert.Equal(["d", "c", "b", "a"], Ordering.Reorder(Current, Request("d", "c", "b", "a"), x => x));

    [Fact]
    public void An_empty_list_keeps_the_order()
        => Assert.Equal(Current, Ordering.Reorder(Current, Request(), x => x));

    [Fact]
    public void A_missing_list_is_a_validation_error()
    {
        var error = Assert.Throws<ValidationException>(() => Ordering.Reorder(Current, new ReorderRequest(), x => x));

        Assert.Equal(Ordering.IdsMessage, error.Fields["ids"]);
    }

    [Fact]
    public void An_unknown_id_is_a_validation_error()
    {
        var error = Assert.Throws<ValidationException>(() => Ordering.Reorder(Current, Request("a", "zzz"), x => x));

        Assert.Contains("zzz", error.Fields["ids"]);
    }

    [Fact]
    public void A_repeated_id_is_a_validation_error()
    {
        var error = Assert.Throws<ValidationException>(() => Ordering.Reorder(Current, Request("a", "a"), x => x));

        Assert.Contains("a", error.Fields["ids"]);
    }

    [Fact]
    public void The_input_list_is_not_changed()
    {
        var copy = Current.ToArray();
        Ordering.Reorder(Current, Request("d"), x => x);

        Assert.Equal(copy, Current);
    }
}
