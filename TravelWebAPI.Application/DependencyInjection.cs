using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using TravelWebAPI.Application.Auth;
using TravelWebAPI.Application.Auth.Validators;

namespace TravelWebAPI.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IValidator<RegisterRequest>, RegisterRequestValidator>();
        services.AddSingleton<IValidator<UpdateProfileRequest>, UpdateProfileRequestValidator>();
        services.AddSingleton<IValidator<ForgotPasswordRequest>, ForgotPasswordRequestValidator>();
        services.AddSingleton<IValidator<ResetPasswordRequest>, ResetPasswordRequestValidator>();

        services.AddScoped<AuthService>();

        return services;
    }
}
