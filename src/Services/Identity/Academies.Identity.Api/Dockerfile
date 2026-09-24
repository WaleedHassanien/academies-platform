# Build context is the repo root: docker build -f src/Services/Identity/Academies.Identity.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish src/Services/Identity/Academies.Identity.Api/Academies.Identity.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
# Signing key lives here (mount a volume or pass Jwt__SigningKeyPem); the app user must own it.
RUN mkdir -p /app/keys && chown $APP_UID /app/keys
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Academies.Identity.Api.dll"]
