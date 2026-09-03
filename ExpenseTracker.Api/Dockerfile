# Container image for ExpenseTracker.Api — the sync API and the Blazor web UI.
#
# Only this project is containerised. The MAUI app installs on phones and desktops; there is
# nothing meaningful to put in an image.
#
# This file sits next to the .csproj (where Visual Studio's container publish looks for it),
# but the build CONTEXT is the repository root, because the API references Domain,
# Application and UI. The csproj sets <DockerfileContext>..</DockerfileContext> so VS agrees.
#
# From the command line, build from the repository root:
#   docker build -f ExpenseTracker.Api/Dockerfile -t expensetracker-api .

# ── Build ─────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, with only the project files copied. This layer is cached and only
# invalidated when a .csproj changes — editing source does not re-download packages.
#
# Every project referenced by the API must be listed, and so must the two props files:
# package versions live centrally in Directory.Packages.props, and without it restore fails
# with NU1015 ("PackageReference items do not have a version specified"). A local build never
# notices, because it sees the whole repository.
COPY Directory.Build.props Directory.Packages.props                ./
COPY ExpenseTracker.Domain/ExpenseTracker.Domain.csproj            ExpenseTracker.Domain/
COPY ExpenseTracker.Contracts/ExpenseTracker.Contracts.csproj      ExpenseTracker.Contracts/
COPY ExpenseTracker.Application/ExpenseTracker.Application.csproj  ExpenseTracker.Application/
COPY ExpenseTracker.UI/ExpenseTracker.UI.csproj                    ExpenseTracker.UI/
COPY ExpenseTracker.Api/ExpenseTracker.Api.csproj                  ExpenseTracker.Api/
RUN dotnet restore ExpenseTracker.Api/ExpenseTracker.Api.csproj

COPY ExpenseTracker.Domain/       ExpenseTracker.Domain/
COPY ExpenseTracker.Contracts/    ExpenseTracker.Contracts/
COPY ExpenseTracker.Application/  ExpenseTracker.Application/
COPY ExpenseTracker.UI/           ExpenseTracker.UI/
COPY ExpenseTracker.Api/          ExpenseTracker.Api/

RUN dotnet publish ExpenseTracker.Api/ExpenseTracker.Api.csproj \
        -c Release -o /app --no-restore

# ── Runtime ───────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Run as the non-root user the base image provides.
USER $APP_UID

COPY --from=build /app .

EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

# Jwt__Key and ConnectionStrings__DefaultConnection are supplied at run time — never baked
# into the image. Program.cs refuses to start without a valid signing key.
ENTRYPOINT ["dotnet", "ExpenseTracker.Api.dll"]
