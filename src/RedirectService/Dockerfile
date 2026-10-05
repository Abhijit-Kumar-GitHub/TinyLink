# Build context: repository root.
# Native AOT must compile on Linux for a Linux target, so the build happens in this SDK image (clang included).
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble-aot AS build
WORKDIR /src
COPY src/TinyLink.Contracts/TinyLink.Contracts.csproj src/TinyLink.Contracts/
COPY src/TinyLink.ServiceDefaults/TinyLink.ServiceDefaults.csproj src/TinyLink.ServiceDefaults/
COPY src/RedirectService/RedirectService.csproj src/RedirectService/
RUN dotnet restore src/RedirectService/RedirectService.csproj -r linux-x64
COPY src/TinyLink.Contracts/ src/TinyLink.Contracts/
COPY src/TinyLink.ServiceDefaults/ src/TinyLink.ServiceDefaults/
COPY src/RedirectService/ src/RedirectService/
RUN dotnet publish src/RedirectService/RedirectService.csproj -c Release -r linux-x64 -o /app --no-restore

# runtime-deps: no .NET runtime at all, the AOT binary is self-contained. Distroless and non-root.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled
WORKDIR /app
COPY --from=build /app/RedirectService /app/appsettings.json ./
EXPOSE 8080
ENTRYPOINT ["./RedirectService"]
