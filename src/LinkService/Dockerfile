# Build context: repository root.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/TinyLink.Contracts/TinyLink.Contracts.csproj src/TinyLink.Contracts/
COPY src/TinyLink.ServiceDefaults/TinyLink.ServiceDefaults.csproj src/TinyLink.ServiceDefaults/
COPY src/LinkService/LinkService.csproj src/LinkService/
RUN dotnet restore src/LinkService/LinkService.csproj
COPY src/TinyLink.Contracts/ src/TinyLink.Contracts/
COPY src/TinyLink.ServiceDefaults/ src/TinyLink.ServiceDefaults/
COPY src/LinkService/ src/LinkService/
RUN dotnet publish src/LinkService/LinkService.csproj -c Release -o /app --no-restore -p:UseAppHost=false

# chiseled-extra: distroless, non-root, but keeps ICU, which SqlClient needs.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "LinkService.dll"]
