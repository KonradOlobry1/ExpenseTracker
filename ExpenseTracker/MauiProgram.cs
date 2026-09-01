using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Application.Services;
using ExpenseTracker.Domain.Interfaces.Repositories;
using ExpenseTracker.Infrastructure.External;
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

            // ── Presentation — UI Services (singletons — shared app state) ────
            builder.Services.AddSingleton<ICurrencyService, CurrencyService>();
            builder.Services.AddSingleton<IThemeService, ThemeService>();
            builder.Services.AddSingleton<ILocalizationService, LocalizationService>();

            // ── Infrastructure — External Services ────────────────────────────
            builder.Services.AddScoped<HttpClient>();
            builder.Services.AddScoped<IAuthService, AuthService>();
            builder.Services.AddScoped<ISyncService, SyncService>();

            // ── Platform — Payment Capture ────────────────────────────────────
#if ANDROID
            builder.Services.AddScoped<IPaymentCaptureService, ExpenseTracker.Platforms.Android.PaymentCaptureService>();
#else
            builder.Services.AddScoped<IPaymentCaptureService, PaymentCaptureServiceStub>();
#endif

            var app = builder.Build();

            // Apply migrations and seed sample data on first launch
            Task.Run(async () =>
            {
                var factory = app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
                var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup.Migrations");
                await using var db = await factory.CreateDbContextAsync();

                try
                {
                    var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
                    var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
                    logger.LogInformation("Migrations applied: {Applied}", string.Join(", ", applied));
                    logger.LogInformation("Migrations pending: {Pending}",
                        pending.Count == 0 ? "(none)" : string.Join(", ", pending));

                    await db.Database.MigrateAsync();

                    var still = (await db.Database.GetPendingMigrationsAsync()).ToList();
                    if (still.Count > 0)
                        logger.LogError("Migrations still pending after Migrate: {Pending}", string.Join(", ", still));
                }
                catch (Exception ex)
                {
                    // A failed migration leaves the local database in an unknown shape; surface
                    // it rather than letting the app start against a half-migrated schema.
                    logger.LogError(ex, "Database migration failed.");
                    throw;
                }

                await DataSeeder.SeedAsync(db);
            }).GetAwaiter().GetResult();

            return app;
        }
    }
}
