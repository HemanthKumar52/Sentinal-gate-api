FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy shared projects first (dependency layer caching)
COPY src/SentinelGate.Shared.Models/SentinelGate.Shared.Models.csproj src/SentinelGate.Shared.Models/
COPY src/SentinelGate.Shared.Infrastructure/SentinelGate.Shared.Infrastructure.csproj src/SentinelGate.Shared.Infrastructure/
COPY src/SentinelGate.Gateway.API/SentinelGate.Gateway.API.csproj src/SentinelGate.Gateway.API/

RUN dotnet restore src/SentinelGate.Gateway.API/SentinelGate.Gateway.API.csproj

# Copy source code
COPY src/SentinelGate.Shared.Models/ src/SentinelGate.Shared.Models/
COPY src/SentinelGate.Shared.Infrastructure/ src/SentinelGate.Shared.Infrastructure/
COPY src/SentinelGate.Gateway.API/ src/SentinelGate.Gateway.API/

RUN dotnet publish src/SentinelGate.Gateway.API/SentinelGate.Gateway.API.csproj -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SentinelGate.Gateway.API.dll"]
