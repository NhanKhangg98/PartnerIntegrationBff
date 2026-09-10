# ── Stage 1: Build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files first to maximise Docker layer cache.
# dotnet restore only re-runs when a .csproj changes, not on every source edit.
COPY ["PartnerIntegration.Api/PartnerIntegration.Api.csproj",              "PartnerIntegration.Api/"]
COPY ["PartnerIntegration.Core/PartnerIntegration.Core.csproj",            "PartnerIntegration.Core/"]
COPY ["PartnerIntegration.Infrastructure/PartnerIntegration.Infrastructure.csproj", "PartnerIntegration.Infrastructure/"]

RUN dotnet restore "PartnerIntegration.Api/PartnerIntegration.Api.csproj"

# Copy the rest of the source and publish
COPY . .

RUN dotnet publish "PartnerIntegration.Api/PartnerIntegration.Api.csproj" \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    --no-self-contained

# ── Stage 2: Runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Run as a non-root user for security hardening
RUN adduser --disabled-password --no-create-home appuser
USER appuser

EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "PartnerIntegration.Api.dll"]
