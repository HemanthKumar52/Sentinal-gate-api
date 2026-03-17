FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5002

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/SentinelGate.Shared.Models/SentinelGate.Shared.Models.csproj src/SentinelGate.Shared.Models/
COPY src/SentinelGate.Shared.Infrastructure/SentinelGate.Shared.Infrastructure.csproj src/SentinelGate.Shared.Infrastructure/
COPY src/SentinelGate.Analytics.Service/SentinelGate.Analytics.Service.csproj src/SentinelGate.Analytics.Service/

RUN dotnet restore src/SentinelGate.Analytics.Service/SentinelGate.Analytics.Service.csproj

COPY src/SentinelGate.Shared.Models/ src/SentinelGate.Shared.Models/
COPY src/SentinelGate.Shared.Infrastructure/ src/SentinelGate.Shared.Infrastructure/
COPY src/SentinelGate.Analytics.Service/ src/SentinelGate.Analytics.Service/

RUN dotnet publish src/SentinelGate.Analytics.Service/SentinelGate.Analytics.Service.csproj -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SentinelGate.Analytics.Service.dll"]
