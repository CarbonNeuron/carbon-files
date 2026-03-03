# carbonfiles-client

Python client for the [CarbonFiles](https://github.com/CarbonNeuron/carbon-files) file-sharing API. Generated from the OpenAPI spec using [openapi-python-client](https://github.com/openapi-generators/openapi-python-client).

## Installation

```bash
pip install carbonfiles-client
```

Requires Python 3.10+.

## Quick Start

```python
from carbonfiles_client import AuthenticatedClient
from carbonfiles_client.api.buckets import post_api_buckets
from carbonfiles_client.models import CreateBucketRequest

client = AuthenticatedClient(
    base_url="https://files.example.com",
    token="cf4_your_api_key",
)

with client as c:
    bucket = post_api_buckets.sync(
        client=c,
        body=CreateBucketRequest(name="my-bucket"),
    )
    print(f"Created bucket: {bucket.id}")
```

## Common Operations

### Create a bucket

```python
from carbonfiles_client.api.buckets import post_api_buckets
from carbonfiles_client.models import CreateBucketRequest

with client as c:
    bucket = post_api_buckets.sync(
        client=c,
        body=CreateBucketRequest(
            name="my-bucket",
            description="Project assets",
            expires_in="30d",
        ),
    )
```

### Upload a file

```python
from carbonfiles_client.api.uploads import post_api_buckets_id_upload

with client as c:
    result = post_api_buckets_id_upload.sync(
        client=c,
        id="bucket-id",
    )
```

### List files in a bucket

```python
from carbonfiles_client.api.files import get_api_buckets_id_files

with client as c:
    files = get_api_buckets_id_files.sync(
        client=c,
        id="bucket-id",
        limit=50,
        offset=0,
    )
    for f in files.items:
        print(f"{f.name} ({f.size} bytes)")
```

### Get bucket details

```python
from carbonfiles_client.api.buckets import get_api_buckets_id

with client as c:
    detail = get_api_buckets_id.sync(client=c, id="bucket-id")
```

### Delete a bucket

```python
from carbonfiles_client.api.buckets import delete_api_buckets_id

with client as c:
    delete_api_buckets_id.sync(client=c, id="bucket-id")
```

## Async Usage

Every endpoint module has async variants (`asyncio` and `asyncio_detailed`):

```python
from carbonfiles_client.api.buckets import post_api_buckets
from carbonfiles_client.models import CreateBucketRequest

async with client as c:
    bucket = await post_api_buckets.asyncio(
        client=c,
        body=CreateBucketRequest(name="my-bucket"),
    )
```

## Detailed Responses

Use `sync_detailed` or `asyncio_detailed` to get the full response including status code and headers:

```python
from carbonfiles_client.api.buckets import get_api_buckets
from carbonfiles_client.types import Response

with client as c:
    response: Response = get_api_buckets.sync_detailed(client=c)
    print(response.status_code)
    print(response.headers)
    print(response.parsed)  # deserialized data
```

## Authentication

CarbonFiles supports four token types, all passed as Bearer tokens:

| Type | Format | Scope |
|------|--------|-------|
| Admin key | Any string | Full access |
| API key | `cf4_` prefix | Own buckets |
| Dashboard JWT | JWT token | Admin-level, 24h max |
| Upload token | `cfu_` prefix | Single bucket |

```python
from carbonfiles_client import AuthenticatedClient

# API key
client = AuthenticatedClient(
    base_url="https://files.example.com",
    token="cf4_your_api_key",
)

# Admin key
client = AuthenticatedClient(
    base_url="https://files.example.com",
    token="your_admin_key",
)
```

To use a custom auth header or prefix:

```python
client = AuthenticatedClient(
    base_url="https://files.example.com",
    token="your_token",
    prefix="Bearer",            # default
    auth_header_name="Authorization",  # default
)
```

## API Modules

Endpoints are organized by tag:

| Module | Endpoints |
|--------|-----------|
| `api.buckets` | `post_api_buckets`, `get_api_buckets`, `get_api_buckets_id`, `patch_api_buckets_id`, `delete_api_buckets_id`, `get_api_buckets_id_summary` |
| `api.files` | `get_api_buckets_id_files`, `get_api_buckets_id_files_file_path`, `delete_api_buckets_id_files_file_path`, `patch_api_buckets_id_files_file_path` |
| `api.uploads` | `post_api_buckets_id_upload`, `put_api_buckets_id_upload_stream` |
| `api.upload_tokens` | `post_api_buckets_id_tokens` |
| `api.api_keys` | `get_api_keys`, `post_api_keys`, `delete_api_keys_prefix`, `get_api_keys_prefix_usage` |
| `api.dashboard_tokens` | `post_api_tokens_dashboard`, `get_api_tokens_dashboard_me` |
| `api.short_ur_ls` | `get_s_code`, `delete_api_short_code` |
| `api.stats` | `get_api_stats` |
| `api.health` | `get_healthz` |

Each module provides four functions: `sync`, `sync_detailed`, `asyncio`, `asyncio_detailed`.

## Links

- [CarbonFiles repository](https://github.com/CarbonNeuron/carbon-files)
- [PyPI package](https://pypi.org/project/carbonfiles-client/)
