# CarbonFiles

A fast, lightweight file-sharing API with bucket-based organization, API key authentication, and real-time events. Built with ASP.NET Minimal API on .NET 10, compiled with Native AOT for sub-millisecond response times.

## Quick Start

```bash
docker compose up -d
```

The API is available at `http://localhost:8080`. The default admin key is `change-me-in-production` — change it via the `CarbonFiles__AdminKey` environment variable.

## API Overview

### Authentication

All auth uses `Authorization: Bearer <token>`. Three token types:

- **Admin key** — full access to everything
- **API keys** — scoped to their own buckets (`cf4_<prefix>_<secret>`)
- **Dashboard tokens** — short-lived JWTs for admin UI access

### Endpoints

```bash
# Health check
curl http://localhost:8080/healthz

# Create an API key (admin)
curl -X POST http://localhost:8080/api/keys \
  -H "Authorization: Bearer $ADMIN_KEY" \
  -H "Content-Type: application/json" \
  -d '{"name": "my-agent"}'

# Create a bucket
curl -X POST http://localhost:8080/api/buckets \
  -H "Authorization: Bearer $API_KEY" \
  -H "Content-Type: application/json" \
  -d '{"name": "my-project", "expires_in": "1w"}'

# Upload files
curl -X POST http://localhost:8080/api/buckets/$BUCKET_ID/upload \
  -H "Authorization: Bearer $API_KEY" \
  -F "files=@screenshot.png" \
  -F "files=@README.md"

# Upload with custom paths
curl -X POST http://localhost:8080/api/buckets/$BUCKET_ID/upload \
  -H "Authorization: Bearer $API_KEY" \
  -F "src/main.rs=@main.rs"

# Stream upload (large files)
curl -X PUT "http://localhost:8080/api/buckets/$BUCKET_ID/upload/stream?filename=video.mp4" \
  -H "Authorization: Bearer $API_KEY" \
  -H "Content-Type: application/octet-stream" \
  --data-binary @video.mp4

# Download file
curl http://localhost:8080/api/buckets/$BUCKET_ID/files/screenshot.png/content

# Download via short URL
curl -L http://localhost:8080/s/xK9mQ2

# Download bucket as ZIP
curl http://localhost:8080/api/buckets/$BUCKET_ID/zip -o project.zip

# Get bucket summary (plaintext, LLM-friendly)
curl http://localhost:8080/api/buckets/$BUCKET_ID/summary

# System stats (admin)
curl http://localhost:8080/api/stats \
  -H "Authorization: Bearer $ADMIN_KEY"

# Create upload token (share upload access without API key)
curl -X POST http://localhost:8080/api/buckets/$BUCKET_ID/tokens \
  -H "Authorization: Bearer $API_KEY" \
  -H "Content-Type: application/json" \
  -d '{"expires_in": "1d", "max_uploads": 10}'

# Upload with token (no auth header needed)
curl -X POST "http://localhost:8080/api/buckets/$BUCKET_ID/upload?token=$UPLOAD_TOKEN" \
  -F "files=@photo.jpg"
```

### Real-Time Events (SignalR)

Connect to `/hub/files` for real-time file and bucket notifications:

```javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hub/files", { accessTokenFactory: () => token })
    .withAutomaticReconnect()
    .build();

connection.on("FileCreated", (bucketId, file) => { /* ... */ });
connection.on("BucketUpdated", (bucketId, changes) => { /* ... */ });

await connection.start();
await connection.invoke("SubscribeToBucket", bucketId);
```

## Configuration

| Setting | Env Var | Default | Description |
|---------|---------|---------|-------------|
| `AdminKey` | `CarbonFiles__AdminKey` | Required | Admin API key |
| `JwtSecret` | `CarbonFiles__JwtSecret` | Derived from AdminKey | JWT signing secret |
| `DataDir` | `CarbonFiles__DataDir` | `./data` | File storage directory |
| `DbPath` | `CarbonFiles__DbPath` | `./data/carbonfiles.db` | SQLite database path |
| `MaxUploadSize` | `CarbonFiles__MaxUploadSize` | `0` (unlimited) | Max upload size in bytes |
| `CleanupIntervalMinutes` | `CarbonFiles__CleanupIntervalMinutes` | `60` | Expired bucket cleanup interval |
| `CorsOrigins` | `CarbonFiles__CorsOrigins` | `*` | Allowed CORS origins |

## Architecture

```
Clients (curl, frontend, LLM agents)
         |
         | HTTP / WebSocket
         v
+----------------------------------+
|        CarbonFiles.Api           |
|  Endpoints | Auth | SignalR Hub  |
+---------|------------------------+
          |
+---------|------------------------+
|   CarbonFiles.Infrastructure     |
|  Services | EF Core | FileStore  |
+---------|------------|----------+
          |            |
    +-----v----+  +----v------+
    |  SQLite  |  | Filesystem |
    +----------+  +-----------+
```

## Development Setup

```bash
# Prerequisites: .NET 10 SDK

# Clone and build
git clone <repo-url>
cd carbon-files
dotnet build

# Run tests
dotnet test

# Run locally
dotnet run --project src/CarbonFiles.Api
```

### Migration Workflow

After changing EF Core entities:

```bash
# 1. Create migration
dotnet ef migrations add <Name> \
  --project src/CarbonFiles.Infrastructure \
  --startup-project src/CarbonFiles.Api

# 2. Apply migration
dotnet ef database update \
  --project src/CarbonFiles.Infrastructure \
  --startup-project src/CarbonFiles.Api

# 3. Regenerate compiled models (REQUIRED for AOT)
dotnet ef dbcontext optimize \
  --project src/CarbonFiles.Infrastructure \
  --startup-project src/CarbonFiles.Api
```

Migrations auto-apply in development mode. In production, run migrations explicitly before deploying.

## API Documentation

Interactive API docs available at `/scalar` when running locally (development mode).

## License

MIT
