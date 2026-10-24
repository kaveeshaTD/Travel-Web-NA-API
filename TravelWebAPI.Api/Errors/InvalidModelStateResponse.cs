using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace TravelWebAPI.Api.Errors;

/// <summary>Replaces the default <c>ProblemDetails</c> 400 of <c>[ApiController]</c> with the uniform error body.</summary>
public static class InvalidModelStateResponse
{
    public const string ValidationMessage = "Validation failed";
    public const string InvalidValueMessage = "Invalid value";

    public static IActionResult Create(ActionContext context)
    {
        var fields = new Dictionary<string, object>();
        var bodyInvalid = false;

        foreach (var (key, entry) in context.ModelState)
        {
            if (entry.ValidationState != ModelValidationState.Invalid || entry.Errors.Count == 0)
            {
                continue;
            }

            // A body that is not JSON at all is reported under "" or "$".
            if (key is "" or "$")
            {
                bodyInvalid = true;
                continue;
            }

            // "$.field" keys come from reading the JSON (wrong type, unknown enum value); their text
            // is framework wording, so the caller only learns that the value is invalid.
            var error = entry.Errors[0];
            var isParseError = key.StartsWith("$.", StringComparison.Ordinal) || error.Exception is not null;
            fields[FieldName(key)] = isParseError || string.IsNullOrWhiteSpace(error.ErrorMessage)
                ? InvalidValueMessage
                : error.ErrorMessage;
        }

        // When the body is not JSON at all, MVC also reports the parameter as "required"; that is noise.
        var error400 = bodyInvalid
            ? ErrorResponse.Create(StatusCodes.Status400BadRequest, GlobalExceptionHandler.InvalidBodyMessage)
            : ErrorResponse.Create(StatusCodes.Status400BadRequest, ValidationMessage, fields);

        return new ObjectResult(error400)
        {
            StatusCode = StatusCodes.Status400BadRequest,
        };
    }

    /// <summary>"$.address.zipCode" or "Address.ZipCode" becomes "address.zipCode" (camelCase like the JSON).</summary>
    internal static string FieldName(string key)
    {
        var path = key.StartsWith("$.", StringComparison.Ordinal) ? key[2..] : key;
        return string.Join('.', path.Split('.').Select(part => JsonNamingPolicy.CamelCase.ConvertName(part)));
    }
}
