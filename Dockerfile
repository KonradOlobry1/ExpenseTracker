# Container image for ExpenseTracker.Api — the sync API and the Blazor web UI.
#
# Only this project is containerised. The MAUI app installs on phones and desktops; there is
# nothing meaningful to put in an image.
#
# Build from the REPOSITORY ROOT, not from ExpenseTracker.Api/, because the API references
# Domain, Application and UI:
#   docker build -t expensetracker-api .

# ── Build ─────────────────────────────────────────────────────────────────────
# The .NET 10 SDK, even though the API targets net9.0: ExpenseTracker.UI multi-targets
# net9.0 and net10.0, and restore evaluates every target framework — a .NET 9 SDK fails on
# the net10.0 one before it builds anything. A newer SDK happily builds older frameworks.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, with only the project files copied. This layer is cached and only
# invalidated when a .csproj changes — editing source does not re-download packages.
COPY ExpenseTracker.Domain/ExpenseTracker.Domain.csproj            ExpenseTracker.Domain/
COPY ExpenseTracker.Application/ExpenseTracker.Application.csproj  ExpenseTracker.Application/
COPY ExpenseTracker.UI/ExpenseTracker.UI.csproj                    ExpenseTracker.UI/
COPY ExpenseTracker.Api/ExpenseTracker.Api.csproj                  ExpenseTracker.Api/
RUN dotnet restore ExpenseTracker.Api/ExpenseTracker.Api.csproj

COPY ExpenseTracker.Domain/       ExpenseTracker.Domain/
COPY ExpenseTracker.Application/  ExpenseTracker.Application/
COPY ExpenseTracker.UI/           ExpenseTracker.UI/
COPY ExpenseTracker.Api/          ExpenseTracker.Api/

# Publish for net9.0 — the framework the runtime image below provides, and the one the
# Azure App Service stack is pinned to.
RUN dotnet publish ExpenseTracker.Api/ExpenseTracker.Api.csproj \
        -c Release -f net9.0 -o /app --no-restore

# ── Runtime ───────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Run as the non-root user the base image provides.
USER $APP_UID

COPY --from=build /app .

EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

# Jwt__Key and ConnectionStrings__DefaultConnection are supplied at run time — never baked
# into the image. Program.cs refuses to start without a valid signing key.
ENTRYPOINT ["dotnet", "ExpenseTracker.Api.dll"]
