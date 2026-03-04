import { describe, it, expect } from "vitest";
import type {
  Bucket,
  BucketFile,
  PaginatedResponse,
  BucketDetailResponse,
  DirectoryListingResponse,
  FileTreeResponse,
  UploadedFile,
  UploadResponse,
  CreateBucketRequest,
  UpdateBucketRequest,
  CreateApiKeyRequest,
  CreateUploadTokenRequest,
  ErrorResponse,
  HealthResponse,
  StatsResponse,
  ApiKeyResponse,
  ApiKeyListItem,
  ApiKeyUsageResponse,
  DashboardTokenResponse,
  DashboardTokenInfo,
  UploadTokenResponse,
  UploadProgress,
  VerifyResponse,
  BucketChanges,
  PaginationOptions,
} from "../src/types.js";

function calcPercentage(p: { bytesSent: number; totalBytes?: number }): number | undefined {
  if (p.totalBytes === undefined || p.totalBytes === 0) return undefined;
  return Math.round((p.bytesSent / p.totalBytes) * 100);
}

describe("Bucket", () => {
  it("deserializes from snake_case JSON", () => {
    const json = `{
      "id": "abc123",
      "name": "my-bucket",
      "owner": "owner1",
      "description": "A test bucket",
      "created_at": "2026-01-01T00:00:00Z",
      "expires_at": "2026-12-31T23:59:59Z",
      "last_used_at": "2026-03-01T12:00:00Z",
      "file_count": 42,
      "total_size": 1048576
    }`;

    const bucket: Bucket = JSON.parse(json);

    expect(bucket.id).toBe("abc123");
    expect(bucket.name).toBe("my-bucket");
    expect(bucket.owner).toBe("owner1");
    expect(bucket.description).toBe("A test bucket");
    expect(bucket.created_at).toBe("2026-01-01T00:00:00Z");
    expect(bucket.expires_at).toBe("2026-12-31T23:59:59Z");
    expect(bucket.last_used_at).toBe("2026-03-01T12:00:00Z");
    expect(bucket.file_count).toBe(42);
    expect(bucket.total_size).toBe(1048576);
  });

  it("serializes with nulls omitted (optional fields undefined)", () => {
    const bucket: Bucket = {
      id: "abc123",
      name: "my-bucket",
      owner: "owner1",
      created_at: "2026-01-01T00:00:00Z",
      file_count: 0,
      total_size: 0,
    };

    const json = JSON.stringify(bucket);
    const parsed = JSON.parse(json);

    expect(parsed.description).toBeUndefined();
    expect(parsed.expires_at).toBeUndefined();
    expect(parsed.last_used_at).toBeUndefined();
    expect("description" in parsed).toBe(false);
    expect("expires_at" in parsed).toBe(false);
    expect("last_used_at" in parsed).toBe(false);
  });
});

describe("BucketFile", () => {
  it("deserializes from snake_case JSON", () => {
    const json = `{
      "path": "docs/readme.md",
      "name": "readme.md",
      "size": 2048,
      "mime_type": "text/markdown",
      "short_code": "abc123",
      "short_url": "https://example.com/s/abc123",
      "sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
      "created_at": "2026-01-01T00:00:00Z",
      "updated_at": "2026-01-02T00:00:00Z"
    }`;

    const file: BucketFile = JSON.parse(json);

    expect(file.path).toBe("docs/readme.md");
    expect(file.name).toBe("readme.md");
    expect(file.size).toBe(2048);
    expect(file.mime_type).toBe("text/markdown");
    expect(file.short_code).toBe("abc123");
    expect(file.short_url).toBe("https://example.com/s/abc123");
    expect(file.sha256).toBe("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    expect(file.created_at).toBe("2026-01-01T00:00:00Z");
    expect(file.updated_at).toBe("2026-01-02T00:00:00Z");
  });

  it("handles null optional fields (short_code, short_url omitted)", () => {
    const json = `{
      "path": "photo.jpg",
      "name": "photo.jpg",
      "size": 4096,
      "mime_type": "image/jpeg",
      "created_at": "2026-01-01T00:00:00Z",
      "updated_at": "2026-01-02T00:00:00Z"
    }`;

    const file: BucketFile = JSON.parse(json);

    expect(file.short_code).toBeUndefined();
    expect(file.short_url).toBeUndefined();
    expect(file.sha256).toBeUndefined();
  });

  it("handles sha256 field present vs absent", () => {
    const withHash = `{
      "path": "file.bin",
      "name": "file.bin",
      "size": 100,
      "mime_type": "application/octet-stream",
      "sha256": "abcdef1234567890",
      "created_at": "2026-01-01T00:00:00Z",
      "updated_at": "2026-01-01T00:00:00Z"
    }`;

    const withoutHash = `{
      "path": "file.bin",
      "name": "file.bin",
      "size": 100,
      "mime_type": "application/octet-stream",
      "created_at": "2026-01-01T00:00:00Z",
      "updated_at": "2026-01-01T00:00:00Z"
    }`;

    const fileWith: BucketFile = JSON.parse(withHash);
    const fileWithout: BucketFile = JSON.parse(withoutHash);

    expect(fileWith.sha256).toBe("abcdef1234567890");
    expect(fileWithout.sha256).toBeUndefined();
  });
});

describe("PaginatedResponse", () => {
  it("deserializes with items array", () => {
    const json = `{
      "items": [
        { "id": "b1", "name": "bucket1", "owner": "o1", "created_at": "2026-01-01T00:00:00Z", "file_count": 5, "total_size": 1024 }
      ],
      "total": 1,
      "limit": 20,
      "offset": 0
    }`;

    const response: PaginatedResponse<Bucket> = JSON.parse(json);

    expect(response.items).toHaveLength(1);
    expect(response.items[0].id).toBe("b1");
    expect(response.total).toBe(1);
    expect(response.limit).toBe(20);
    expect(response.offset).toBe(0);
  });

  it("deserializes with empty items array", () => {
    const json = `{
      "items": [],
      "total": 0,
      "limit": 20,
      "offset": 0
    }`;

    const response: PaginatedResponse<Bucket> = JSON.parse(json);

    expect(response.items).toHaveLength(0);
    expect(response.total).toBe(0);
  });
});

describe("BucketDetailResponse", () => {
  it("deserializes full response with files array", () => {
    const json = `{
      "id": "bucket1",
      "name": "My Bucket",
      "owner": "owner1",
      "description": "A bucket",
      "created_at": "2026-01-01T00:00:00Z",
      "file_count": 2,
      "total_size": 4096,
      "unique_content_count": 1,
      "unique_content_size": 2048,
      "files": [
        {
          "path": "file1.txt",
          "name": "file1.txt",
          "size": 2048,
          "mime_type": "text/plain",
          "created_at": "2026-01-01T00:00:00Z",
          "updated_at": "2026-01-01T00:00:00Z"
        }
      ],
      "has_more_files": false
    }`;

    const detail: BucketDetailResponse = JSON.parse(json);

    expect(detail.id).toBe("bucket1");
    expect(detail.name).toBe("My Bucket");
    expect(detail.owner).toBe("owner1");
    expect(detail.description).toBe("A bucket");
    expect(detail.file_count).toBe(2);
    expect(detail.total_size).toBe(4096);
    expect(detail.files).toHaveLength(1);
    expect(detail.files[0].path).toBe("file1.txt");
    expect(detail.has_more_files).toBe(false);
  });

  it("deserializes with dedup stats", () => {
    const json = `{
      "id": "bucket1",
      "name": "Dedup Bucket",
      "owner": "owner1",
      "created_at": "2026-01-01T00:00:00Z",
      "file_count": 10,
      "total_size": 10240,
      "unique_content_count": 5,
      "unique_content_size": 5120,
      "files": [],
      "has_more_files": false
    }`;

    const detail: BucketDetailResponse = JSON.parse(json);

    expect(detail.unique_content_count).toBe(5);
    expect(detail.unique_content_size).toBe(5120);
  });
});

describe("DirectoryListingResponse", () => {
  it("deserializes with files and folders arrays", () => {
    const json = `{
      "files": [
        {
          "path": "docs/readme.md",
          "name": "readme.md",
          "size": 512,
          "mime_type": "text/markdown",
          "created_at": "2026-01-01T00:00:00Z",
          "updated_at": "2026-01-01T00:00:00Z"
        }
      ],
      "folders": ["images", "scripts"],
      "total_files": 1,
      "total_folders": 2,
      "limit": 100,
      "offset": 0
    }`;

    const listing: DirectoryListingResponse = JSON.parse(json);

    expect(listing.files).toHaveLength(1);
    expect(listing.files[0].name).toBe("readme.md");
    expect(listing.folders).toEqual(["images", "scripts"]);
    expect(listing.total_files).toBe(1);
    expect(listing.total_folders).toBe(2);
    expect(listing.limit).toBe(100);
    expect(listing.offset).toBe(0);
  });
});

describe("FileTreeResponse", () => {
  it("deserializes full tree with directories, files, cursor", () => {
    const json = `{
      "prefix": "docs/",
      "delimiter": "/",
      "directories": [
        { "path": "docs/images/", "file_count": 3, "total_size": 9216 }
      ],
      "files": [
        {
          "path": "docs/readme.md",
          "name": "readme.md",
          "size": 512,
          "mime_type": "text/markdown",
          "created_at": "2026-01-01T00:00:00Z",
          "updated_at": "2026-01-01T00:00:00Z"
        }
      ],
      "total_files": 1,
      "total_directories": 1,
      "cursor": "next-page-token"
    }`;

    const tree: FileTreeResponse = JSON.parse(json);

    expect(tree.prefix).toBe("docs/");
    expect(tree.delimiter).toBe("/");
    expect(tree.directories).toHaveLength(1);
    expect(tree.directories[0].path).toBe("docs/images/");
    expect(tree.directories[0].file_count).toBe(3);
    expect(tree.directories[0].total_size).toBe(9216);
    expect(tree.files).toHaveLength(1);
    expect(tree.total_files).toBe(1);
    expect(tree.total_directories).toBe(1);
    expect(tree.cursor).toBe("next-page-token");
  });

  it("handles null cursor", () => {
    const json = `{
      "delimiter": "/",
      "directories": [],
      "files": [],
      "total_files": 0,
      "total_directories": 0
    }`;

    const tree: FileTreeResponse = JSON.parse(json);

    expect(tree.cursor).toBeUndefined();
    expect(tree.prefix).toBeUndefined();
  });
});

describe("UploadedFile", () => {
  it("deserializes with sha256 and deduplicated=true", () => {
    const json = `{
      "path": "data/report.pdf",
      "name": "report.pdf",
      "size": 8192,
      "mime_type": "application/pdf",
      "short_code": "xyz789",
      "short_url": "https://example.com/s/xyz789",
      "sha256": "deadbeef",
      "deduplicated": true,
      "created_at": "2026-01-01T00:00:00Z",
      "updated_at": "2026-01-01T00:00:00Z"
    }`;

    const file: UploadedFile = JSON.parse(json);

    expect(file.path).toBe("data/report.pdf");
    expect(file.name).toBe("report.pdf");
    expect(file.size).toBe(8192);
    expect(file.mime_type).toBe("application/pdf");
    expect(file.short_code).toBe("xyz789");
    expect(file.short_url).toBe("https://example.com/s/xyz789");
    expect(file.sha256).toBe("deadbeef");
    expect(file.deduplicated).toBe(true);
    expect(file.created_at).toBe("2026-01-01T00:00:00Z");
    expect(file.updated_at).toBe("2026-01-01T00:00:00Z");
  });

  it("deserializes with deduplicated=false", () => {
    const json = `{
      "path": "new-file.txt",
      "name": "new-file.txt",
      "size": 256,
      "mime_type": "text/plain",
      "sha256": "abcd1234",
      "deduplicated": false,
      "created_at": "2026-01-01T00:00:00Z",
      "updated_at": "2026-01-01T00:00:00Z"
    }`;

    const file: UploadedFile = JSON.parse(json);

    expect(file.deduplicated).toBe(false);
  });
});

describe("UploadResponse", () => {
  it("deserializes uploaded array", () => {
    const json = `{
      "uploaded": [
        {
          "path": "file1.txt",
          "name": "file1.txt",
          "size": 100,
          "mime_type": "text/plain",
          "sha256": "hash1",
          "deduplicated": false,
          "created_at": "2026-01-01T00:00:00Z",
          "updated_at": "2026-01-01T00:00:00Z"
        }
      ]
    }`;

    const response: UploadResponse = JSON.parse(json);

    expect(response.uploaded).toHaveLength(1);
    expect(response.uploaded[0].path).toBe("file1.txt");
  });

  it("deserializes multiple uploaded files", () => {
    const json = `{
      "uploaded": [
        {
          "path": "file1.txt",
          "name": "file1.txt",
          "size": 100,
          "mime_type": "text/plain",
          "sha256": "hash1",
          "deduplicated": false,
          "created_at": "2026-01-01T00:00:00Z",
          "updated_at": "2026-01-01T00:00:00Z"
        },
        {
          "path": "file2.jpg",
          "name": "file2.jpg",
          "size": 2048,
          "mime_type": "image/jpeg",
          "sha256": "hash2",
          "deduplicated": true,
          "created_at": "2026-01-01T00:00:00Z",
          "updated_at": "2026-01-01T00:00:00Z"
        }
      ]
    }`;

    const response: UploadResponse = JSON.parse(json);

    expect(response.uploaded).toHaveLength(2);
    expect(response.uploaded[0].name).toBe("file1.txt");
    expect(response.uploaded[1].name).toBe("file2.jpg");
    expect(response.uploaded[1].deduplicated).toBe(true);
  });
});

describe("Request types", () => {
  it("CreateBucketRequest serializes with expires_in", () => {
    const request: CreateBucketRequest = {
      name: "test-bucket",
      description: "A test",
      expires_in: "7d",
    };

    const json = JSON.stringify(request);
    const parsed = JSON.parse(json);

    expect(parsed.name).toBe("test-bucket");
    expect(parsed.description).toBe("A test");
    expect(parsed.expires_in).toBe("7d");
  });

  it("UpdateBucketRequest serializes with nulls omitted", () => {
    const request: UpdateBucketRequest = {
      name: "new-name",
    };

    const json = JSON.stringify(request);
    const parsed = JSON.parse(json);

    expect(parsed.name).toBe("new-name");
    expect("description" in parsed).toBe(false);
    expect("expires_in" in parsed).toBe(false);
  });

  it("CreateApiKeyRequest serializes name", () => {
    const request: CreateApiKeyRequest = {
      name: "my-key",
    };

    const json = JSON.stringify(request);
    const parsed = JSON.parse(json);

    expect(parsed.name).toBe("my-key");
  });

  it("CreateUploadTokenRequest serializes expires_in and max_uploads", () => {
    const request: CreateUploadTokenRequest = {
      expires_in: "1h",
      max_uploads: 10,
    };

    const json = JSON.stringify(request);
    const parsed = JSON.parse(json);

    expect(parsed.expires_in).toBe("1h");
    expect(parsed.max_uploads).toBe(10);
  });
});

describe("ErrorResponse", () => {
  it("deserializes error and hint", () => {
    const json = `{
      "error": "Bucket not found",
      "hint": "Check the bucket ID and try again"
    }`;

    const response: ErrorResponse = JSON.parse(json);

    expect(response.error).toBe("Bucket not found");
    expect(response.hint).toBe("Check the bucket ID and try again");
  });

  it("deserializes without hint", () => {
    const json = `{
      "error": "Internal server error"
    }`;

    const response: ErrorResponse = JSON.parse(json);

    expect(response.error).toBe("Internal server error");
    expect(response.hint).toBeUndefined();
  });
});

describe("HealthResponse", () => {
  it("deserializes status, uptime_seconds, db", () => {
    const json = `{
      "status": "healthy",
      "uptime_seconds": 3600,
      "db": "ok"
    }`;

    const response: HealthResponse = JSON.parse(json);

    expect(response.status).toBe("healthy");
    expect(response.uptime_seconds).toBe(3600);
    expect(response.db).toBe("ok");
  });
});

describe("StatsResponse", () => {
  it("deserializes full stats with storage_by_owner array", () => {
    const json = `{
      "total_buckets": 10,
      "total_files": 100,
      "total_size": 1048576,
      "total_keys": 3,
      "total_downloads": 500,
      "storage_by_owner": [
        {
          "owner": "user1",
          "bucket_count": 5,
          "file_count": 50,
          "total_size": 524288
        },
        {
          "owner": "user2",
          "bucket_count": 5,
          "file_count": 50,
          "total_size": 524288
        }
      ]
    }`;

    const response: StatsResponse = JSON.parse(json);

    expect(response.total_buckets).toBe(10);
    expect(response.total_files).toBe(100);
    expect(response.total_size).toBe(1048576);
    expect(response.total_keys).toBe(3);
    expect(response.total_downloads).toBe(500);
    expect(response.storage_by_owner).toHaveLength(2);
    expect(response.storage_by_owner[0].owner).toBe("user1");
    expect(response.storage_by_owner[0].bucket_count).toBe(5);
    expect(response.storage_by_owner[1].owner).toBe("user2");
  });
});

describe("Auth responses", () => {
  it("ApiKeyResponse: key, prefix, name, created_at", () => {
    const json = `{
      "key": "cf4_abcdefghijklmnop",
      "prefix": "cf4_abc",
      "name": "my-api-key",
      "created_at": "2026-01-01T00:00:00Z"
    }`;

    const response: ApiKeyResponse = JSON.parse(json);

    expect(response.key).toBe("cf4_abcdefghijklmnop");
    expect(response.prefix).toBe("cf4_abc");
    expect(response.name).toBe("my-api-key");
    expect(response.created_at).toBe("2026-01-01T00:00:00Z");
  });

  it("ApiKeyListItem: prefix, name, created_at, last_used_at, bucket_count, file_count, total_size", () => {
    const json = `{
      "prefix": "cf4_abc",
      "name": "my-key",
      "created_at": "2026-01-01T00:00:00Z",
      "last_used_at": "2026-03-01T12:00:00Z",
      "bucket_count": 3,
      "file_count": 25,
      "total_size": 65536
    }`;

    const item: ApiKeyListItem = JSON.parse(json);

    expect(item.prefix).toBe("cf4_abc");
    expect(item.name).toBe("my-key");
    expect(item.created_at).toBe("2026-01-01T00:00:00Z");
    expect(item.last_used_at).toBe("2026-03-01T12:00:00Z");
    expect(item.bucket_count).toBe(3);
    expect(item.file_count).toBe(25);
    expect(item.total_size).toBe(65536);
  });

  it("ApiKeyUsageResponse: full with buckets array", () => {
    const json = `{
      "prefix": "cf4_abc",
      "name": "my-key",
      "created_at": "2026-01-01T00:00:00Z",
      "last_used_at": "2026-03-01T12:00:00Z",
      "bucket_count": 1,
      "file_count": 10,
      "total_size": 4096,
      "total_downloads": 50,
      "buckets": [
        {
          "id": "bucket1",
          "name": "My Bucket",
          "owner": "cf4_abc",
          "created_at": "2026-01-01T00:00:00Z",
          "file_count": 10,
          "total_size": 4096
        }
      ]
    }`;

    const response: ApiKeyUsageResponse = JSON.parse(json);

    expect(response.prefix).toBe("cf4_abc");
    expect(response.name).toBe("my-key");
    expect(response.bucket_count).toBe(1);
    expect(response.file_count).toBe(10);
    expect(response.total_size).toBe(4096);
    expect(response.total_downloads).toBe(50);
    expect(response.buckets).toHaveLength(1);
    expect(response.buckets[0].id).toBe("bucket1");
    expect(response.buckets[0].name).toBe("My Bucket");
  });

  it("DashboardTokenResponse: token, expires_at", () => {
    const json = `{
      "token": "eyJhbGciOiJIUzI1NiJ9.test.signature",
      "expires_at": "2026-01-02T00:00:00Z"
    }`;

    const response: DashboardTokenResponse = JSON.parse(json);

    expect(response.token).toBe("eyJhbGciOiJIUzI1NiJ9.test.signature");
    expect(response.expires_at).toBe("2026-01-02T00:00:00Z");
  });

  it("DashboardTokenInfo: scope, expires_at", () => {
    const json = `{
      "scope": "admin",
      "expires_at": "2026-01-02T00:00:00Z"
    }`;

    const info: DashboardTokenInfo = JSON.parse(json);

    expect(info.scope).toBe("admin");
    expect(info.expires_at).toBe("2026-01-02T00:00:00Z");
  });
});

describe("UploadTokenResponse", () => {
  it("deserializes token, bucket_id, expires_at, max_uploads, uploads_used", () => {
    const json = `{
      "token": "cfu_abcdefghijklmnop",
      "bucket_id": "bucket1",
      "expires_at": "2026-01-02T00:00:00Z",
      "max_uploads": 100,
      "uploads_used": 5
    }`;

    const response: UploadTokenResponse = JSON.parse(json);

    expect(response.token).toBe("cfu_abcdefghijklmnop");
    expect(response.bucket_id).toBe("bucket1");
    expect(response.expires_at).toBe("2026-01-02T00:00:00Z");
    expect(response.max_uploads).toBe(100);
    expect(response.uploads_used).toBe(5);
  });
});

describe("UploadProgress", () => {
  it("calculates percentage from bytesSent and totalBytes", () => {
    const progress: UploadProgress = {
      bytesSent: 512,
      totalBytes: 1024,
      percentage: calcPercentage({ bytesSent: 512, totalBytes: 1024 }),
    };

    expect(progress.percentage).toBe(50);
  });

  it("undefined totalBytes yields undefined percentage", () => {
    const progress: UploadProgress = {
      bytesSent: 512,
      percentage: calcPercentage({ bytesSent: 512 }),
    };

    expect(progress.totalBytes).toBeUndefined();
    expect(progress.percentage).toBeUndefined();
  });

  it("zero totalBytes yields undefined percentage (avoid division by zero)", () => {
    const progress: UploadProgress = {
      bytesSent: 0,
      totalBytes: 0,
      percentage: calcPercentage({ bytesSent: 0, totalBytes: 0 }),
    };

    expect(progress.percentage).toBeUndefined();
  });
});

describe("VerifyResponse", () => {
  it("deserializes valid response (stored_hash === computed_hash)", () => {
    const json = `{
      "path": "file.txt",
      "stored_hash": "abc123",
      "computed_hash": "abc123",
      "valid": true
    }`;

    const response: VerifyResponse = JSON.parse(json);

    expect(response.path).toBe("file.txt");
    expect(response.stored_hash).toBe("abc123");
    expect(response.computed_hash).toBe("abc123");
    expect(response.valid).toBe(true);
  });

  it("deserializes invalid response (hash mismatch)", () => {
    const json = `{
      "path": "corrupted.bin",
      "stored_hash": "abc123",
      "computed_hash": "def456",
      "valid": false
    }`;

    const response: VerifyResponse = JSON.parse(json);

    expect(response.stored_hash).toBe("abc123");
    expect(response.computed_hash).toBe("def456");
    expect(response.valid).toBe(false);
  });
});

describe("BucketChanges", () => {
  it("deserializes name, description, expires_at", () => {
    const json = `{
      "name": "new-name",
      "description": "new description",
      "expires_at": "2026-12-31T23:59:59Z"
    }`;

    const changes: BucketChanges = JSON.parse(json);

    expect(changes.name).toBe("new-name");
    expect(changes.description).toBe("new description");
    expect(changes.expires_at).toBe("2026-12-31T23:59:59Z");
  });
});

describe("PaginationOptions", () => {
  it("all fields default to undefined", () => {
    const options: PaginationOptions = {};

    expect(options.limit).toBeUndefined();
    expect(options.offset).toBeUndefined();
    expect(options.sort).toBeUndefined();
    expect(options.order).toBeUndefined();
  });
});
