# Build context: repository root.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/TinyLink.Contracts/TinyLink.Contracts.csproj src/TinyLink.Contracts/
COPY src/TinyLink.ServiceDefaults/TinyLink.ServiceDefaults.csproj src/TinyLink.ServiceDefaults/
COPY src/AnalyticsService/AnalyticsService.csproj src/AnalyticsService/
RUN dotnet restore src/AnalyticsService/AnalyticsService.csproj
COPY src/TinyLink.Contracts/ src/TinyLink.Contracts/
COPY src/TinyLink.ServiceDefaults/ src/TinyLink.ServiceDefaults/
COPY src/AnalyticsService/ src/AnalyticsService/
RUN dotnet publish src/AnalyticsService/AnalyticsService.csproj -c Release -o /app --no-restore -p:UseAppHost=false

# chiseled-extra: distroless, non-root, but keeps ICU, which SqlClient needs.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "AnalyticsService.dll"]
