FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5001

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/SentinelGate.Shared.Models/SentinelGate.Shared.Models.csproj src/SentinelGate.Shared.Models/
COPY src/SentinelGate.Shared.Infrastructure/SentinelGate.Shared.Infrastructure.csproj src/SentinelGate.Shared.Infrastructure/
COPY src/SentinelGate.RateLimiter.Service/SentinelGate.RateLimiter.Service.csproj src/SentinelGate.RateLimiter.Service/

RUN dotnet restore src/SentinelGate.RateLimiter.Service/SentinelGate.RateLimiter.Service.csproj

COPY src/SentinelGate.Shared.Models/ src/SentinelGate.Shared.Models/
COPY src/SentinelGate.Shared.Infrastructure/ src/SentinelGate.Shared.Infrastructure/
COPY src/SentinelGate.RateLimiter.Service/ src/SentinelGate.RateLimiter.Service/

RUN dotnet publish src/SentinelGate.RateLimiter.Service/SentinelGate.RateLimiter.Service.csproj -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SentinelGate.RateLimiter.Service.dll"]
