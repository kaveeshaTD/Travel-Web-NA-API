using TravelWebAPI.Domain.Exceptions;

namespace TravelWebAPI.Tests.Unit;

public class ExceptionTests
{
    [Fact]
    public void Validation_exception_carries_each_field()
    {
        var ex = new ValidationException(new Dictionary<string, string>
        {
            ["email"] = "Enter a valid email address",
            ["password"] = "Too short",
        });

        Assert.Equal("Validation failed", ex.Message);
        Assert.Equal(2, ex.Fields.Count);
        Assert.Equal("Too short", ex.Fields["password"]);
    }

    [Fact]
    public void Validation_exception_can_describe_one_field()
    {
        var ex = new ValidationException("date", "Date cannot be in the past");

        Assert.Equal("Date cannot be in the past", ex.Fields["date"]);
    }

    [Fact]
    public void Dependents_exception_is_a_conflict_listing_the_dependents()
    {
        var ex = new DependentsExistException("Destination", ["cultural-triangle", "hill-country"]);

        Assert.IsAssignableFrom<ConflictException>(ex);
        Assert.Equal(["cultural-triangle", "hill-country"], ex.Dependents);
        Assert.Equal("This Destination is still used by: cultural-triangle, hill-country", ex.Message);
    }

    [Fact]
    public void Not_found_names_the_record()
    {
        var ex = new NotFoundException("Package", "abc");

        Assert.Equal("Package 'abc' was not found", ex.Message);
    }

    [Fact]
    public void Defaults_have_friendly_messages_and_no_fields()
    {
        Assert.Equal("Sign in required", new UnauthorizedException().Message);
        Assert.Equal("You are not allowed to do this", new ForbiddenException().Message);
        Assert.Empty(new ForbiddenException().Fields);
    }
}
