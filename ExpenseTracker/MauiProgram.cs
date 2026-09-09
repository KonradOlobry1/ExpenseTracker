using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Application.Services;
using ExpenseTracker.Domain.Interfaces.Repositories;
using ExpenseTracker.Infrastructure.External;
using ExpenseTracker.Infrastructure.Platform;
using ExpenseTracker.Infrastructure.Persistence;
using ExpenseTracker.Infrastructure.Persistence.Repositories;
using ExpenseTracker.Presentation.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;

namespace ExpenseTracker
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();
            builder.Services.AddMudServices();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            // ── Database ──────────────────────────────────────────────────────
            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "expensetracker.db");
            builder.Services.AddDbContextFactory<AppDbContext>(options =>
                options.UseSqlite($"Data Source={dbPath}"));

            // ── Infrastructure — Repositories ─────────────────────────────────
            builder.Services.AddScoped<IExpenseRepository, ExpenseRepository>();
            builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
            builder.Services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
            builder.Services.AddScoped<IIncomeRepository, IncomeRepository>();

            // ── Application — Services ────────────────────────────────────────
            builder.Services.AddScoped<IExpenseService, ExpenseService>();
            builder.Services.AddScoped<ICategoryService, CategoryService>();
            builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();
            builder.Services.AddScoped<IIncomeService, IncomeService>();

            // ── Platform storage ──────────────────────────────────────────────
            // The only two MAUI APIs the sync and auth services need. Behind these interfaces
            // everything they do is ordinary code that runs — and is tested — off-device.
            builder.Services.AddSingleton<IPreferenceStore, MauiPreferenceStore>();
            builder.Services.AddSingleton<ISecureStore, MauiSecureStore>();

            // ── Presentation — UI Services (singletons — shared app state) ────
            builder.Services.AddSingleton<LocalSettings>();
            builder.Services.AddSingleton<ICurrencyService, CurrencyService>();
            builder.Services.AddSingleton<IThemeService, ThemeService>();
            builder.Services.AddSingleton<ILocalizationService, LocalizationService>();

            // ── Infrastructure — External Services ────────────────────────────
            // AddHttpClient injects a pooled, resilience-wrapped HttpClient into the existing
            // `HttpClient http` constructor parameter on each — same idea as EnableRetryOnFailure
            // on the EF side, for the calls that don't go through EF: sync push/pull and login
            // hit the same Azure SQL cold-start window the database retry already covers.
            // This also moves both services from Scoped to Transient, which AddHttpClient does
            // by default and which is correct here — neither holds state beyond its already-
            // Singleton dependencies.
            //
            // ResiliencePolicy widens the standard handler's timeouts. Its defaults — 10s per
            // attempt, 30s total — are shorter than an Azure SQL serverless resume, so the
            // first call after an idle period could not succeed under them. See that class.
            builder.Services.AddHttpClient<IAuthService, AuthService>()
                .AddStandardResilienceHandler(ResiliencePolicy.Configure);
            builder.Services.AddHttpClient<ISyncService, SyncService>()
                .AddStandardResilienceHandler(ResiliencePolicy.Configure);

            // ── Platform — Payment Capture ────────────────────────────────────
#if ANDROID
            builder.Services.AddScoped<IPaymentCaptureService, ExpenseTracker.Platforms.Android.PaymentCaptureService>();
#else
            builder.Services.AddScoped<IPaymentCaptureService, PaymentCaptureServiceStub>();
#endif

            // Migrations run through DatabaseInitializer, which MainLayout awaits before it
            // reads anything — behind the spinner it already shows during its session check.
            //
            // They used to run right here, with Task.Run(...).GetAwaiter().GetResult() blocking
            // the launch thread until every pending migration had replayed. On a cold first
            // install that is the whole migration history, on whatever storage the device has,
            // with Android's ANR watchdog running.
            //
            // Singleton so the gate inside it is shared: the work happens once however many
            // callers ask.
            builder.Services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();

            // Sample data is deliberately NOT seeded. DataSeeder writes thirty expenses and ten
            // subscriptions whenever the local database has none, which was fine as a demo but
            // is wrong once accounts are real: a fresh install seeded them before the user
            // signed in, and the first sync pushed all forty rows into their cloud account.
            // Call DataSeeder.SeedAsync by hand if you want a populated demo.

            return builder.Build();
        }
    }
}
