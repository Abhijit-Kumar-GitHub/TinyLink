# Build context: repository root.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/TinyLink.Contracts/TinyLink.Contracts.csproj src/TinyLink.Contracts/
COPY src/TinyLink.ServiceDefaults/TinyLink.ServiceDefaults.csproj src/TinyLink.ServiceDefaults/
COPY src/OperatorConsole/OperatorConsole.csproj src/OperatorConsole/
RUN dotnet restore src/OperatorConsole/OperatorConsole.csproj
COPY src/TinyLink.Contracts/ src/TinyLink.Contracts/
COPY src/TinyLink.ServiceDefaults/ src/TinyLink.ServiceDefaults/
COPY src/OperatorConsole/ src/OperatorConsole/
RUN dotnet publish src/OperatorConsole/OperatorConsole.csproj -c Release -o /app --no-restore -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "OperatorConsole.dll"]
