# Design: Handcrafted Python SDK for CarbonFiles

**Date:** 2026-03-04
**Status:** Approved

## Overview

Replace the auto-generated Python client (`clients/python/`) with a handcrafted, idiomatic Python SDK. The C# client (`clients/csharp/`) is the reference for API surface and feature coverage.

## Goals

- Handcrafted, readable, zero-magic Python SDK
- Both sync and async interfaces via httpx
- Fluent resource-scoped API mirroring the C# client's pattern
- LLM-friendly — AI coding agents are a primary user
- Publish-ready to PyPI as `carbonfiles`

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Sync/async pattern | Shared transport protocol, paired resource classes | Less duplication than full copy, cleaner than codegen |
| Models | Pydantic v2 | Validation, IDE support, `.model_dump()` for serialization |
| HTTP library | httpx | Sync + async from one lib, good streaming support |
| SignalR events | **Deferred to v2** | No mature Python SignalR client, reduces scope |
| Package name | `carbonfiles` (PyPI), `carbonfiles` (import) | Clean, short |
| Upload tokens | Nested under bucket: `cf.buckets["id"].tokens` | Matches C# client |
| Dashboard | Top-level: `cf.dashboard` | Matches C# client |
| Auto-pagination | Generator / AsyncGenerator yielding pages | User controls iteration |
| Error type | `CarbonFilesError(status_code, error, hint)` | Matches API error shape |
| Progress callback | `Callable[[int, int \| None, float \| None], None]` | Simple, Pythonic |
| Python version | 3.10+ | Union syntax, modern typing |

## API Surface

### Sync Client

```python
from carbonfiles import CarbonFiles

cf = CarbonFiles("https://files.example.com", "cf4_your_api_key")

# Buckets
bucket = cf.buckets.create("my-project", description="Assets", expires_in="30d")
buckets = cf.buckets.list(limit=20, sort="created_at", order="desc")
detail = cf.buckets["bucket-id"].get()
cf.buckets["bucket-id"].update(name="renamed")
cf.buckets["bucket-id"].delete()
summary = cf.buckets["bucket-id"].summary()
zip_bytes = cf.buckets["bucket-id"].download_zip()  # returns bytes

# Files
files = cf.buckets["bucket-id"].files.list(limit=50)
dirs = cf.buckets["bucket-id"].files.list_directory("src/")
result = cf.buckets["bucket-id"].files.upload("./photo.jpg")
cf.buckets["bucket-id"].files.upload(b"hello", filename="hello.txt")
cf.buckets["bucket-id"].files.upload(open("big.zip", "rb"), filename="big.zip")
cf.buckets["bucket-id"].files.upload("./huge.iso", progress=lambda sent, total, pct: print(f"{pct}%"))
cf.buckets["bucket-id"].files.upload("./file.txt", upload_token="cfu_...")
data = cf.buckets["bucket-id"].files["readme.md"].download()
cf.buckets["bucket-id"].files["readme.md"].download_to("./readme.md")
meta = cf.buckets["bucket-id"].files["readme.md"].metadata()
cf.buckets["bucket-id"].files["readme.md"].delete()
cf.buckets["bucket-id"].files["log.txt"].append(b"new line\n")
cf.buckets["bucket-id"].files["data.bin"].patch(data, range_start=0, range_end=99, total_size=1000)

# Upload tokens (nested under bucket)
token = cf.buckets["bucket-id"].tokens.create(expires_in="7d", max_uploads=100)

# API Keys (admin)
keys = cf.keys.list()
key = cf.keys.create("ci-agent")
cf.keys["cf4_prefix"].revoke()
usage = cf.keys["cf4_prefix"].usage()

# Dashboard (admin)
dash_token = cf.dashboard.create_token()
info = cf.dashboard.me()

# Stats (admin)
stats = cf.stats.get()

# Short URLs
cf.short_urls["code"].delete()

# Health
health = cf.health()

# Pagination
for page in cf.buckets.list_all(limit=50):
    for bucket in page.items:
        print(bucket.name)

# Context manager
with CarbonFiles("https://files.example.com", "cf4_key") as cf:
    ...
```

### Async Client

```python
from carbonfiles import AsyncCarbonFiles

async with AsyncCarbonFiles("https://files.example.com", "cf4_key") as cf:
    bucket = await cf.buckets.create("my-stuff")
    await cf.buckets[bucket.id].files.upload("./data.csv")
    files = await cf.buckets[bucket.id].files.list()

    async for page in cf.buckets.list_all(limit=50):
        for bucket in page.items:
            print(bucket.name)
```

### Error Handling

```python
from carbonfiles import CarbonFilesError

try:
    cf.buckets["nonexistent"].get()
except CarbonFilesError as e:
    print(e.status_code)  # 404
    print(e.error)        # "Bucket not found"
    print(e.hint)         # Optional hint from API
```

## Architecture

### Project Structure

```
clients/python/
├── pyproject.toml
├── README.md
├── src/
│   └── carbonfiles/
│       ├── __init__.py          # Exports CarbonFiles, AsyncCarbonFiles, CarbonFilesError, models
│       ├── client.py            # CarbonFiles (sync client)
│       ├── async_client.py      # AsyncCarbonFiles (async client)
│       ├── _transport.py        # SyncTransport, AsyncTransport, CarbonFilesError
│       ├── _types.py            # ProgressCallback, type aliases
│       ├── models/
│       │   ├── __init__.py      # Re-exports all models
│       │   ├── buckets.py       # Bucket, BucketDetail, CreateBucketRequest, UpdateBucketRequest
│       │   ├── files.py         # BucketFile, UploadResponse, DirectoryListing
│       │   ├── keys.py          # ApiKey, ApiKeyListItem, ApiKeyUsage, CreateApiKeyRequest
│       │   ├── tokens.py        # UploadToken, DashboardToken, DashboardTokenInfo, create requests
│       │   ├── stats.py         # Stats, OwnerStats
│       │   └── common.py        # PaginatedResponse[T], HealthResponse, ErrorResponse
│       ├── resources/
│       │   ├── __init__.py
│       │   ├── buckets.py       # BucketsResource, BucketResource + Async variants
│       │   ├── files.py         # FilesResource, FileResource + Async variants
│       │   ├── keys.py          # KeysResource, KeyResource + Async variants
│       │   ├── tokens.py        # UploadTokensResource + Async variant
│       │   ├── dashboard.py     # DashboardResource + Async variant
│       │   ├── stats.py         # StatsResource + Async variant
│       │   └── short_urls.py    # ShortUrlsResource, ShortUrlResource + Async variants
│       └── py.typed             # PEP 561 marker
├── tests/
│   ├── conftest.py              # Shared fixtures, mock transport helpers
│   ├── test_transport.py        # Transport layer tests (auth, errors, serialization)
│   ├── test_buckets.py          # Bucket CRUD tests
│   ├── test_files.py            # File operations tests
│   ├── test_uploads.py          # Upload tests (path, bytes, BinaryIO, progress)
│   ├── test_keys.py             # API key admin tests
│   ├── test_tokens.py           # Upload token + dashboard token tests
│   ├── test_stats.py            # Stats tests
│   ├── test_short_urls.py       # Short URL tests
│   ├── test_pagination.py       # Pagination + list_all() tests
│   └── test_models.py           # Model serialization/validation tests
```

### Transport Layer

Two transport implementations sharing error handling logic:

```python
# _transport.py

class CarbonFilesError(Exception):
    def __init__(self, status_code: int, error: str, hint: str | None = None):
        self.status_code = status_code
        self.error = error
        self.hint = hint
        msg = f"{error} ({hint})" if hint else error
        super().__init__(msg)

class SyncTransport:
    """Wraps httpx.Client with auth, error handling, and URL building."""

    def __init__(self, base_url: str, api_key: str | None = None, timeout: float = 30.0):
        headers = {}
        if api_key:
            headers["Authorization"] = f"Bearer {api_key}"
        self._client = httpx.Client(base_url=base_url, timeout=timeout, headers=headers)

    def request(self, method: str, path: str, *,
                json: Any = None, params: dict | None = None,
                content: bytes | None = None, headers: dict | None = None) -> httpx.Response:
        resp = self._client.request(method, path, json=json, params=params,
                                     content=content, headers=headers or {})
        if resp.status_code >= 400:
            self._raise_error(resp)
        return resp

    def stream(self, method: str, path: str, **kw) -> Generator[httpx.Response]:
        # Context manager for streaming responses (downloads, ZIP)
        ...

    def close(self):
        self._client.close()

    @staticmethod
    def _raise_error(resp: httpx.Response) -> NoReturn:
        try:
            body = resp.json()
            raise CarbonFilesError(resp.status_code, body.get("error", "Unknown error"), body.get("hint"))
        except (json.JSONDecodeError, KeyError):
            raise CarbonFilesError(resp.status_code, resp.text or "Unknown error")

class AsyncTransport:
    """Wraps httpx.AsyncClient with auth, error handling, and URL building."""
    # Same interface as SyncTransport but async methods
    # Uses httpx.AsyncClient
```

### Resource Pattern

Each resource module contains sync and async classes. Resources hold a reference to the transport and construct URL paths.

```python
# resources/buckets.py

class BucketsResource:
    """Collection-level operations: create, list, list_all, indexer."""

    def __init__(self, transport: SyncTransport):
        self._transport = transport

    def create(self, name: str, *, description: str | None = None,
               expires_in: str | None = None) -> Bucket:
        body = {"name": name}
        if description is not None: body["description"] = description
        if expires_in is not None: body["expires_in"] = expires_in
        resp = self._transport.request("POST", "/api/buckets", json=body)
        return Bucket.model_validate(resp.json())

    def list(self, *, limit: int | None = None, offset: int | None = None,
             sort: str | None = None, order: str | None = None,
             include_expired: bool | None = None) -> PaginatedResponse[Bucket]:
        params = _pagination_params(limit, offset, sort, order)
        if include_expired is not None:
            params["include_expired"] = str(include_expired).lower()
        resp = self._transport.request("GET", "/api/buckets", params=params)
        return PaginatedResponse[Bucket].model_validate(resp.json())

    def list_all(self, *, limit: int = 50, sort: str | None = None,
                 order: str | None = None) -> Generator[PaginatedResponse[Bucket]]:
        """Auto-paginate through all pages. Yields one PaginatedResponse per page."""
        offset = 0
        while True:
            page = self.list(limit=limit, offset=offset, sort=sort, order=order)
            yield page
            if offset + limit >= page.total:
                break
            offset += limit

    def __getitem__(self, bucket_id: str) -> "BucketResource":
        return BucketResource(self._transport, bucket_id)


class BucketResource:
    """Single-bucket operations: get, update, delete, summary, download_zip."""

    def __init__(self, transport: SyncTransport, bucket_id: str):
        self._transport = transport
        self._id = bucket_id
        self._path = f"/api/buckets/{quote(bucket_id, safe='')}"
        self.files = FilesResource(transport, bucket_id)
        self.tokens = UploadTokensResource(transport, bucket_id)

    def get(self) -> BucketDetail: ...
    def update(self, *, name: str | None = None, ...) -> Bucket: ...
    def delete(self) -> None: ...
    def summary(self) -> str: ...
    def download_zip(self) -> bytes: ...
```

Async variants follow the same pattern with `async def` and `await`:

```python
class AsyncBucketsResource:
    def __init__(self, transport: AsyncTransport): ...
    async def create(self, name: str, ...) -> Bucket: ...
    async def list(self, ...) -> PaginatedResponse[Bucket]: ...
    async def list_all(self, ...) -> AsyncGenerator[PaginatedResponse[Bucket]]: ...
    def __getitem__(self, bucket_id: str) -> "AsyncBucketResource": ...
```

### Upload Design

```python
# resources/files.py

ProgressCallback = Callable[[int, int | None, float | None], None]

class FilesResource:
    def upload(self, source: str | bytes | BinaryIO | Path, *,
               filename: str | None = None,
               progress: ProgressCallback | None = None,
               upload_token: str | None = None) -> UploadResponse:
        """Upload a file. Source can be a file path, bytes, or file-like object."""

        if isinstance(source, (str, Path)):
            path = Path(source)
            filename = filename or path.name
            with open(path, "rb") as f:
                return self._upload_stream(f, filename, path.stat().st_size, progress, upload_token)
        elif isinstance(source, bytes):
            if not filename:
                raise ValueError("filename is required when uploading bytes")
            stream = io.BytesIO(source)
            return self._upload_stream(stream, filename, len(source), progress, upload_token)
        else:
            if not filename:
                raise ValueError("filename is required when uploading a file object")
            # Try to get size from seekable stream
            total = None
            if hasattr(source, 'seek') and hasattr(source, 'tell'):
                pos = source.tell()
                source.seek(0, 2)
                total = source.tell()
                source.seek(pos)
            return self._upload_stream(source, filename, total, progress, upload_token)

    def _upload_stream(self, stream, filename, total, progress, upload_token):
        params = {"filename": filename}
        if upload_token:
            params["token"] = upload_token

        if progress:
            content = ProgressReader(stream, total, progress)
        else:
            content = stream

        resp = self._transport.request("PUT", f"{self._path}/upload/stream",
                                        content=content, params=params)
        return UploadResponse.model_validate(resp.json())
```

### Models (Pydantic v2)

```python
# models/buckets.py
from pydantic import BaseModel, Field
from datetime import datetime

class Bucket(BaseModel):
    id: str
    name: str
    owner: str
    description: str | None = None
    created_at: datetime
    expires_at: datetime | None = None
    last_used_at: datetime | None = None
    file_count: int
    total_size: int

class BucketDetail(Bucket):
    files: list[BucketFile]
    has_more_files: bool

# models/common.py
from pydantic import BaseModel
from typing import Generic, TypeVar

T = TypeVar("T")

class PaginatedResponse(BaseModel, Generic[T]):
    items: list[T]
    total: int
    limit: int
    offset: int

class HealthResponse(BaseModel):
    status: str
    uptime_seconds: int
    db: str
```

### Client Classes

```python
# client.py
class CarbonFiles:
    def __init__(self, base_url: str, api_key: str | None = None, *, timeout: float = 30.0):
        self._transport = SyncTransport(base_url, api_key, timeout)
        self.buckets = BucketsResource(self._transport)
        self.keys = KeysResource(self._transport)
        self.stats = StatsResource(self._transport)
        self.short_urls = ShortUrlsResource(self._transport)
        self.dashboard = DashboardResource(self._transport)

    def health(self) -> HealthResponse:
        resp = self._transport.request("GET", "/healthz")
        return HealthResponse.model_validate(resp.json())

    def close(self):
        self._transport.close()

    def __enter__(self): return self
    def __exit__(self, *args): self.close()

# async_client.py
class AsyncCarbonFiles:
    def __init__(self, base_url: str, api_key: str | None = None, *, timeout: float = 30.0):
        self._transport = AsyncTransport(base_url, api_key, timeout)
        self.buckets = AsyncBucketsResource(self._transport)
        self.keys = AsyncKeysResource(self._transport)
        self.stats = AsyncStatsResource(self._transport)
        self.short_urls = AsyncShortUrlsResource(self._transport)
        self.dashboard = AsyncDashboardResource(self._transport)

    async def health(self) -> HealthResponse:
        resp = await self._transport.request("GET", "/healthz")
        return HealthResponse.model_validate(resp.json())

    async def aclose(self):
        await self._transport.aclose()

    async def __aenter__(self): return self
    async def __aexit__(self, *args): await self.aclose()
```

### Dependencies

```toml
[project]
name = "carbonfiles"
requires-python = ">=3.10"
dependencies = [
    "httpx>=0.24.0,<1.0",
    "pydantic>=2.0,<3.0",
]

[project.optional-dependencies]
dev = [
    "pytest>=7.0",
    "pytest-asyncio>=0.21",
    "ruff",
]
```

### Test Strategy

- **pytest** + **httpx MockTransport** for deterministic HTTP mocking
- **pytest-asyncio** for async test support
- One test file per resource area
- `conftest.py` provides `mock_client()` and `mock_async_client()` fixtures
- Target: 80+ tests matching C# client coverage areas
- Test naming: `test_<resource>_<action>_<scenario>`

### Mapping: C# → Python API Surface

| C# | Python |
|----|--------|
| `client.Buckets.CreateAsync(req)` | `cf.buckets.create("name", ...)` |
| `client.Buckets.ListAsync(opts)` | `cf.buckets.list(limit=20)` |
| `client.Buckets["id"].GetAsync()` | `cf.buckets["id"].get()` |
| `client.Buckets["id"].UpdateAsync(req)` | `cf.buckets["id"].update(name="new")` |
| `client.Buckets["id"].DeleteAsync()` | `cf.buckets["id"].delete()` |
| `client.Buckets["id"].GetSummaryAsync()` | `cf.buckets["id"].summary()` |
| `client.Buckets["id"].DownloadZipAsync()` | `cf.buckets["id"].download_zip()` |
| `client.Buckets["id"].Files.ListAsync(opts)` | `cf.buckets["id"].files.list()` |
| `client.Buckets["id"].Files.ListDirectoryAsync(path)` | `cf.buckets["id"].files.list_directory("src/")` |
| `client.Buckets["id"].Files.UploadAsync(stream, name)` | `cf.buckets["id"].files.upload(stream, filename="x")` |
| `client.Buckets["id"].Files.UploadAsync(bytes, name)` | `cf.buckets["id"].files.upload(b"data", filename="x")` |
| `client.Buckets["id"].Files.UploadFileAsync(path)` | `cf.buckets["id"].files.upload("./file.txt")` |
| `client.Buckets["id"].Files["path"].GetMetadataAsync()` | `cf.buckets["id"].files["path"].metadata()` |
| `client.Buckets["id"].Files["path"].DownloadAsync()` | `cf.buckets["id"].files["path"].download()` |
| `client.Buckets["id"].Files["path"].DeleteAsync()` | `cf.buckets["id"].files["path"].delete()` |
| `client.Buckets["id"].Files["path"].PatchAsync(...)` | `cf.buckets["id"].files["path"].patch(...)` |
| `client.Buckets["id"].Files["path"].AppendAsync(...)` | `cf.buckets["id"].files["path"].append(...)` |
| `client.Buckets["id"].Tokens.CreateAsync(req)` | `cf.buckets["id"].tokens.create(...)` |
| `client.Keys.CreateAsync(req)` | `cf.keys.create("name")` |
| `client.Keys.ListAsync(opts)` | `cf.keys.list()` |
| `client.Keys["prefix"].RevokeAsync()` | `cf.keys["prefix"].revoke()` |
| `client.Keys["prefix"].GetUsageAsync()` | `cf.keys["prefix"].usage()` |
| `client.Stats.GetAsync()` | `cf.stats.get()` |
| `client.ShortUrls["code"].DeleteAsync()` | `cf.short_urls["code"].delete()` |
| `client.Dashboard.CreateTokenAsync(req)` | `cf.dashboard.create_token()` |
| `client.Dashboard.GetCurrentUserAsync()` | `cf.dashboard.me()` |
| `client.Health.CheckAsync()` | `cf.health()` |
| `client.Events.*` | **Deferred to v2** |

### Not in C# but added for Python

| Feature | Rationale |
|---------|-----------|
| `list_all()` auto-pagination generator | Python generators are idiomatic; C# doesn't have this |
| `download_to(path)` on FileResource | Convenience for saving to disk |
| `upload()` accepting `str \| bytes \| BinaryIO \| Path` | Pythonic duck-typing; C# has separate overloads |
| Sync client | C# is async-only; Python users expect sync option |

### What's deferred to v2

- SignalR real-time events (no mature Python SignalR client)
- Multipart upload (stream upload covers all use cases)
- Short URL resolve (it's a 302 redirect, not a JSON endpoint)
