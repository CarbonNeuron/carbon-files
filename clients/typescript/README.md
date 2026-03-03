# @carbonfiles/client

TypeScript client for the [CarbonFiles](https://github.com/CarbonNeuron/carbon-files) file-sharing API. Generated from the OpenAPI spec using [Hey API](https://heyapi.dev/).

## Installation

```bash
npm install @carbonfiles/client
```

## Quick Start

```typescript
import { createClient, createConfig, postApiBuckets } from '@carbonfiles/client';

const client = createClient(
  createConfig({
    baseUrl: 'https://files.example.com',
    headers: {
      Authorization: 'Bearer cf4_your_api_key',
    },
  })
);

const { data, error } = await postApiBuckets({
  client,
  body: { name: 'my-bucket' },
});

if (data) {
  console.log('Created bucket:', data.id);
}
```

## Common Operations

### Create a bucket

```typescript
import { postApiBuckets } from '@carbonfiles/client';

const { data } = await postApiBuckets({
  client,
  body: {
    name: 'my-bucket',
    description: 'Project assets',
    expires_in: '30d',
  },
});
```

### Upload a file

```typescript
import { postApiBucketsByIdUpload } from '@carbonfiles/client';

const formData = new FormData();
formData.append('file', file);

const { data } = await postApiBucketsByIdUpload({
  client,
  path: { id: 'bucket-id' },
  body: formData,
});
```

### List files in a bucket

```typescript
import { getApiBucketsByIdFiles } from '@carbonfiles/client';

const { data } = await getApiBucketsByIdFiles({
  client,
  path: { id: 'bucket-id' },
  query: { limit: 50, offset: 0 },
});
```

### Download a file

```typescript
import { getApiBucketsByIdFilesByFilePath } from '@carbonfiles/client';

const { data } = await getApiBucketsByIdFilesByFilePath({
  client,
  path: { id: 'bucket-id', filePath: 'photo.jpg' },
});
```

### Delete a bucket

```typescript
import { deleteApiBucketsById } from '@carbonfiles/client';

await deleteApiBucketsById({
  client,
  path: { id: 'bucket-id' },
});
```

## Authentication

CarbonFiles supports four token types, all passed as Bearer tokens:

| Type | Format | Scope |
|------|--------|-------|
| Admin key | Any string | Full access |
| API key | `cf4_` prefix | Own buckets |
| Dashboard JWT | JWT token | Admin-level, 24h max |
| Upload token | `cfu_` prefix | Single bucket |

Set authentication globally on the client:

```typescript
const client = createClient(
  createConfig({
    baseUrl: 'https://files.example.com',
    headers: {
      Authorization: 'Bearer cf4_your_api_key',
    },
  })
);
```

Or per-request:

```typescript
const { data } = await getApiBuckets({
  client,
  headers: {
    Authorization: 'Bearer cf4_your_api_key',
  },
});
```

Upload tokens can also be passed as a query parameter:

```typescript
const { data } = await postApiBucketsByIdUpload({
  client,
  path: { id: 'bucket-id' },
  query: { token: 'cfu_upload_token' },
  body: formData,
});
```

## Error Handling

All functions return `{ data, error }` by default:

```typescript
const { data, error } = await postApiBuckets({
  client,
  body: { name: 'my-bucket' },
});

if (error) {
  // ErrorResponse: { error: string, hint?: string }
  console.error(error.error, error.hint);
}
```

Enable throwing on errors instead:

```typescript
const client = createClient(
  createConfig({
    baseUrl: 'https://files.example.com',
    throwOnError: true,
  })
);
```

## Available Functions

| Function | Description |
|----------|-------------|
| `getHealthz` | Health check |
| `postApiBuckets` | Create bucket |
| `getApiBuckets` | List buckets |
| `getApiBucketsById` | Get bucket details |
| `patchApiBucketsById` | Update bucket |
| `deleteApiBucketsById` | Delete bucket |
| `getApiBucketsByIdSummary` | Bucket summary (plaintext) |
| `getApiBucketsByIdZip` | Download bucket as ZIP |
| `getApiBucketsByIdFiles` | List files in bucket |
| `getApiBucketsByIdFilesByFilePath` | Get file |
| `deleteApiBucketsByIdFilesByFilePath` | Delete file |
| `patchApiBucketsByIdFilesByFilePath` | Patch file (ranges/append) |
| `postApiBucketsByIdUpload` | Upload files (multipart) |
| `putApiBucketsByIdUploadStream` | Stream upload |
| `getSByCode` | Resolve short URL |
| `deleteApiShortByCode` | Delete short URL |
| `getApiKeys` | List API keys |
| `postApiKeys` | Create API key |
| `deleteApiKeysByPrefix` | Revoke API key |
| `getApiKeysByPrefixUsage` | API key usage stats |
| `postApiBucketsByIdTokens` | Create upload token |
| `postApiTokensDashboard` | Create dashboard token |
| `getApiTokensDashboardMe` | Validate dashboard token |
| `getApiStats` | System statistics |

## Links

- [CarbonFiles repository](https://github.com/CarbonNeuron/carbon-files)
- [npm package](https://www.npmjs.com/package/@carbonfiles/client)
