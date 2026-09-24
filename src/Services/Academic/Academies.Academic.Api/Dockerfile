# Build context is the repo root: docker build -f src/Services/Academic/Academies.Academic.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish src/Services/Academic/Academies.Academic.Api/Academies.Academic.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
# Arabic-capable fonts for certificate PDFs (US-041).
RUN apt-get update && apt-get install -y --no-install-recommends fonts-noto-core && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Academies.Academic.Api.dll"]
