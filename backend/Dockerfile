# syntax=docker/dockerfile:1
# Build context: the REPOSITORY ROOT (so the root .editorconfig and analyzers apply exactly as locally).
#   docker build -f backend/Dockerfile -t stockpilot-api .

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# 1) Restore using only project files, so this layer is cached until a dependency changes.
COPY .editorconfig ./
COPY backend/global.json backend/Directory.Build.props backend/Directory.Packages.props backend/StockPilot.slnx backend/
COPY backend/src/StockPilot.Domain/StockPilot.Domain.csproj               backend/src/StockPilot.Domain/
COPY backend/src/StockPilot.Application/StockPilot.Application.csproj     backend/src/StockPilot.Application/
COPY backend/src/StockPilot.Infrastructure/StockPilot.Infrastructure.csproj backend/src/StockPilot.Infrastructure/
COPY backend/src/StockPilot.API/StockPilot.API.csproj                     backend/src/StockPilot.API/
RUN dotnet restore backend/src/StockPilot.API/StockPilot.API.csproj

# 2) Compile and publish.
COPY backend/src/ backend/src/
RUN dotnet publish backend/src/StockPilot.API/StockPilot.API.csproj \
    -c Release -o /app --no-restore /p:UseAppHost=false

# ---------- runtime ----------
# "Chiseled" Ubuntu image: no shell, no package manager, runs as non-root by default.
# Smaller attack surface and faster cold starts on Render's free tier.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
COPY --from=build /app .
# Chiseled images ship without tzdata. Reports and the dashboard group sales by the company's local day
# (e.g. Asia/Manila), which needs the IANA zone files: copy them from the SDK image (~4 MB).
COPY --from=build /usr/share/zoneinfo /usr/share/zoneinfo

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0 \
    DOTNET_TieredPGO=1
EXPOSE 8080

ENTRYPOINT ["dotnet", "StockPilot.API.dll"]
