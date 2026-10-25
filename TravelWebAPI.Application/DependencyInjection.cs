using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using TravelWebAPI.Application.Auth;
using TravelWebAPI.Application.Auth.Validators;
using TravelWebAPI.Application.Bookings;
using TravelWebAPI.Application.Contact;
using TravelWebAPI.Application.Content;
using TravelWebAPI.Application.Rates;
using TravelWebAPI.Application.Reviews;

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

        services.AddSingleton<IValidator<FaqDraft>, FaqDraftValidator>();
        services.AddSingleton<IValidator<GalleryImageDraft>, GalleryImageDraftValidator>();
        services.AddSingleton<IValidator<VehicleDraft>, VehicleDraftValidator>();
        services.AddSingleton<IValidator<ServiceDraft>, ServiceDraftValidator>();
        services.AddSingleton<IValidator<DestinationDraft>, DestinationDraftValidator>();
        services.AddSingleton<IValidator<PackageDraft>, PackageDraftValidator>();
        services.AddSingleton<IValidator<SettingsDto>, SettingsDtoValidator>();
        services.AddSingleton<IValidator<TipsDto>, TipsDtoValidator>();
        services.AddSingleton<IValidator<ReviewSubmissionDraft>, ReviewSubmissionDraftValidator>();
        services.AddSingleton<IValidator<ReviewDraft>, ReviewDraftValidator>();
        services.AddSingleton<IValidator<ContactDraft>, ContactDraftValidator>();
        services.AddSingleton<IValidator<ReplyDraft>, ReplyDraftValidator>();
        services.AddSingleton<IValidator<BookingDraft>, BookingDraftValidator>();
        services.AddSingleton<IValidator<BookingUpdateDraft>, BookingUpdateDraftValidator>();
        services.AddSingleton<IValidator<BookingEmailDraft>, BookingEmailDraftValidator>();

        services.AddScoped<FaqService>();
        services.AddScoped<GalleryService>();
        services.AddScoped<VehicleService>();
        services.AddScoped<ServiceOfferingService>();
        services.AddScoped<DestinationService>();
        services.AddScoped<PackageService>();
        services.AddScoped<UploadService>();
        services.AddScoped<SettingsService>();
        services.AddScoped<TipsService>();

        services.AddScoped<ReviewService>();
        services.AddScoped<ContactService>();
        services.AddScoped<EmailLogService>();
        services.AddScoped<BookingService>();

        services.AddScoped<ExchangeRateService>();
        services.AddScoped<IExchangeRates>(sp => sp.GetRequiredService<ExchangeRateService>());

        return services;
    }
}
