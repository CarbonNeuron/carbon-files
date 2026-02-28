# Build stage — AOT compile the API
FROM mcr.microsoft.com/dotnet/sdk:10.0-aot AS build-api
WORKDIR /src

# Copy project files first for layer caching
COPY CarbonFiles.slnx .
COPY src/CarbonFiles.Core/CarbonFiles.Core.csproj src/CarbonFiles.Core/
COPY src/CarbonFiles.Infrastructure/CarbonFiles.Infrastructure.csproj src/CarbonFiles.Infrastructure/
COPY src/CarbonFiles.Api/CarbonFiles.Api.csproj src/CarbonFiles.Api/
RUN dotnet restore src/CarbonFiles.Api/CarbonFiles.Api.csproj

# Copy everything and publish
COPY . .
RUN dotnet publish src/CarbonFiles.Api -c Release -o /app/api

# Build stage — standard compile the migrator (no AOT)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-migrator
WORKDIR /src

# Copy project files first for layer caching
COPY CarbonFiles.slnx .
COPY src/CarbonFiles.Core/CarbonFiles.Core.csproj src/CarbonFiles.Core/
COPY src/CarbonFiles.Infrastructure/CarbonFiles.Infrastructure.csproj src/CarbonFiles.Infrastructure/
COPY src/CarbonFiles.Migrator/CarbonFiles.Migrator.csproj src/CarbonFiles.Migrator/
RUN dotnet restore src/CarbonFiles.Migrator/CarbonFiles.Migrator.csproj

# Copy everything and publish
COPY . .
RUN dotnet publish src/CarbonFiles.Migrator -c Release -o /app/migrator

# Runtime — needs .NET runtime for migrator + native deps for AOT API
FROM mcr.microsoft.com/dotnet/runtime:10.0-noble
WORKDIR /app

COPY --from=build-api /app/api ./
COPY --from=build-migrator /app/migrator ./migrator/

# Create data directory and ensure writable
RUN mkdir -p /app/data && chmod 777 /app/data

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# Run migrator then start API
ENTRYPOINT ["sh", "-c", "dotnet ./migrator/CarbonFiles.Migrator.dll && exec ./CarbonFiles.Api"]
