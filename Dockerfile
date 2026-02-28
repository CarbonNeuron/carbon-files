# Build stage — AOT compile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first for layer caching
COPY CarbonFiles.slnx .
COPY src/CarbonFiles.Core/CarbonFiles.Core.csproj src/CarbonFiles.Core/
COPY src/CarbonFiles.Infrastructure/CarbonFiles.Infrastructure.csproj src/CarbonFiles.Infrastructure/
COPY src/CarbonFiles.Api/CarbonFiles.Api.csproj src/CarbonFiles.Api/
RUN dotnet restore src/CarbonFiles.Api/CarbonFiles.Api.csproj

# Copy everything and publish
COPY . .
RUN dotnet publish src/CarbonFiles.Api -c Release -o /app/publish

# Runtime — no .NET runtime needed for AOT, just OS deps
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled
WORKDIR /app
COPY --from=build /app/publish .

# Create data directory
RUN mkdir -p /app/data

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["./CarbonFiles.Api"]
