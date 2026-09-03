using System.Security.Claims;
using System.Text;
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Data.Repositories;
using ExpenseTracker.Api.Web;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Application.Services;
using ExpenseTracker.Domain.Interfaces.Repositories;
using ExpenseTracker.Presentation.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using MudBlazor.Services;
using ExpenseTracker.Api.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Database
// A factory for the Blazor repositories (concurrent renders need a context each) plus a
// scoped instance for the controllers, both from one options registration.
builder.Services.AddDbContextFactory<ApiDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        // The free serverless tier auto-pauses when idle. The first connection after that
        // fails with SQL error 40613 ("database is not currently available") while it
        // resumes, which without retries kills startup outright.
        sql => sql.EnableRetryOnFailure(
            maxRetryCount: 8,
            maxRetryDelay: TimeSpan.FromSeconds(20),
            errorNumbersToAdd: null)));
builder.Services.AddScoped<ApiDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<ApiDbContext>>().CreateDbContext());

// Identity
builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    // Length does more for strength than forcing symbol classes, which mostly drives users
    // toward predictable substitutions.
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 8;

    // Applies to both login paths. Identity only enforces the password policy when a
    // password is set or changed, so existing accounts keep working.
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;

    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<ApiDbContext>()
.AddDefaultTokenProviders();

// JWT Authentication
// Signing key never lives in appsettings.json. Local dev reads it from user-secrets
// (dotnet user-secrets set "Jwt:Key" "<random>"); deployments supply Jwt__Key as an
// environment variable.
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey))
    throw new InvalidOperationException(
        "Jwt:Key is not configured. Set it via user-secrets or the Jwt__Key environment variable.");
if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
    throw new InvalidOperationException(
        "Jwt:Key must be at least 32 bytes; HMAC-SHA256 rejects shorter keys.");

// The API is called by the MAUI app with a bearer token; the web UI is a browser with a
// cookie. A policy scheme picks per request rather than forcing one on both.
builder.Services.AddAuthentication(options =>
{
    // All three, explicitly: AddIdentity already set DefaultAuthenticateScheme to the
    // cookie, and leaving that in place makes every bearer-token API call a 401.
    options.DefaultScheme = "SmartAuth";
    options.DefaultAuthenticateScheme = "SmartAuth";
    options.DefaultChallengeScheme = "SmartAuth";
})
.AddPolicyScheme("SmartAuth", "JWT for /api, cookie for the UI", options =>
{
    options.ForwardDefaultSelector = context =>
        context.Request.Path.StartsWithSegments("/api")
            ? JwtBearerDefaults.AuthenticationScheme
            : IdentityConstants.ApplicationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
});

builder.Services.AddAuthorization();

// Throttles credential stuffing across many accounts, which per-account lockout cannot see.
// Partitioned by client IP: a single unpartitioned limiter would let one abusive caller
// exhaust the budget for every user. UseForwardedHeaders below resolves the real client IP
// behind App Service's proxy, without which every request would share one partition anyway.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    var permitPerMinute = builder.Configuration.GetValue("RateLimit:AuthPermitPerMinute", 10);

    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = permitPerMinute,
                QueueLimit = 0
            }));

    // Sync is the most expensive endpoint in the app — it reads and writes every table — and
    // was the only one with no limit at all. Partitioned by account rather than IP: a family
    // behind one router is several clients, and a phone roaming between networks is one.
    var syncPerMinute = builder.Configuration.GetValue("RateLimit:SyncPermitPerMinute", 30);

    options.AddPolicy("sync", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? context.Connection.RemoteIpAddress?.ToString()
                          ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = syncPerMinute,
                QueueLimit = 0
            }));
});

// App Service and the container proxy terminate TLS upstream, so the app sees plain HTTP.
// Without this, UseHttpsRedirection would redirect forever.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;
    // Cleared because the proxy is App Service's own front end, whose address is neither
    // fixed nor knowable here. KnownIPNetworks replaced the obsolete KnownNetworks in .NET 10.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/account/login";
    options.LogoutPath = "/account/logout";
    options.AccessDeniedPath = "/account/login";
});
builder.Services.AddControllers();

// Failures reach the clients as RFC 7807 JSON instead of an empty 500 body. The device sync
// service reads `detail` from it, so a failed sync can say what actually went wrong rather
// than only that the request did not succeed.
builder.Services.AddProblemDetails();

// Liveness and readiness are separate on purpose — see the endpoint registrations below.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApiDbContext>("database", tags: ["ready"]);

// Without this, keys are written to the container's filesystem and lost on every restart:
// each deploy or recycle would sign every browser out. Storing them in the database also
// means a second instance can read cookies issued by the first.
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<ApiDbContext>()
    .SetApplicationName("ExpenseTracker");

// ── Blazor Server UI ──────────────────────────────────────────────────────────
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();

// Repositories read the cloud database directly, scoped to the signed-in account.
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
builder.Services.AddScoped<IExpenseRepository, CloudExpenseRepository>();
builder.Services.AddScoped<ICategoryRepository, CloudCategoryRepository>();
builder.Services.AddScoped<IIncomeRepository, CloudIncomeRepository>();
builder.Services.AddScoped<ISubscriptionRepository, CloudSubscriptionRepository>();

// Same Application services the MAUI app uses — they only know the repository interfaces.
builder.Services.AddScoped<IExpenseService, ExpenseService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IIncomeService, IncomeService>();
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();

// UI services the shared components inject. Scoped = per Blazor circuit.
builder.Services.AddScoped<BrowserPreferences>();
builder.Services.AddScoped<AccountSettingsWriter>();
builder.Services.AddScoped<ICurrencyService, WebCurrencyService>();
builder.Services.AddScoped<IThemeService, WebThemeService>();
builder.Services.AddScoped<ILocalizationService, WebLocalizationService>();
builder.Services.AddScoped<IPaymentCaptureService, PaymentCaptureServiceStub>();
builder.Services.AddScoped<IAuthService, WebAuthService>();
builder.Services.AddScoped<ISyncService, NoOpSyncService>();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "ExpenseTracker API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter: Bearer {token}"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Applying migrations at startup re-runs on every cold start, which on the free App Service
// tier eats into the daily CPU budget — and tests supply their own schema. Opt in explicitly.
if (builder.Configuration.GetValue("RunMigrationsAtStartup", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
    await db.Database.MigrateAsync();
}

// First in the pipeline so it catches everything after it. Outside Development this turns an
// unhandled exception into a ProblemDetails response; Development keeps the developer page.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseExceptionHandler();
    app.UseStatusCodePages();
}

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.UseAuthentication();

// After authentication, not before: the sync policy partitions by account id, and running
// the limiter first left context.User anonymous, so every account fell into one partition
// keyed by address — which is what a test caught. Token validation is cheap; the expensive
// work (password hashing, database access) still happens after the limiter.
app.UseRateLimiter();

app.UseAuthorization();

// Two endpoints, not one. Liveness must not touch the database: it is what App Service polls,
// and on the free tier the SQL database auto-pauses — a probe that woke it every minute would
// defeat the auto-pause and burn the budget. Readiness is the one to call deliberately, and
// doubles as a way to warm the database before a first real request.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
   .AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") })
   .AllowAnonymous();

app.MapControllers();
// The shared pages live in ExpenseTracker.UI. Endpoint-based routing needs them declared
// here as well as on the Router — Router.AdditionalAssemblies alone leaves them 404.
app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode()
   .AddAdditionalAssemblies(typeof(ExpenseTracker.Components.Routes).Assembly)
   .RequireAuthorization();   // the sign-in page opts out with [AllowAnonymous]

// Sign-out has to be a real request for the same reason sign-in does: it clears a cookie.
app.MapPost("/account/logout", async (SignInManager<AppUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.Redirect("/account/login");
});

app.Run();

// WebApplicationFactory<T> needs a reachable entry-point type; top-level statements
// generate an internal one, so expose it for ExpenseTracker.Api.Tests.
public partial class Program { }
