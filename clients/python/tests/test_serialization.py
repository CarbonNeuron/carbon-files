"""Tests for Pydantic model serialization and deserialization."""

from __future__ import annotations

from datetime import datetime, timezone

from carbonfiles.models import (
    Bucket,
    BucketDetail,
    BucketFile,
    DirectoryEntry,
    DirectoryListingResponse,
    ErrorResponse,
    FileTreeResponse,
    PaginatedResponse,
    UploadedFile,
    UploadResponse,
    VerifyResponse,
)

NOW = datetime(2026, 3, 4, 12, 0, 0, tzinfo=timezone.utc)
NOW_STR = "2026-03-04T12:00:00Z"


# ---------- common.py ----------


class TestErrorResponse:
    def test_with_hint(self) -> None:
        data = {"error": "not found", "hint": "check the bucket id"}
        model = ErrorResponse.model_validate(data)
        assert model.error == "not found"
        assert model.hint == "check the bucket id"

    def test_without_hint(self) -> None:
        data = {"error": "unauthorized"}
        model = ErrorResponse.model_validate(data)
        assert model.error == "unauthorized"
        assert model.hint is None

    def test_roundtrip(self) -> None:
        data = {"error": "bad request", "hint": "missing name"}
        model = ErrorResponse.model_validate(data)
        dumped = model.model_dump(exclude_none=True)
        assert dumped == data


class TestPaginatedResponse:
    def test_paginated_buckets(self) -> None:
        data = {
            "items": [
                {
                    "id": "abc123",
                    "name": "test",
                    "owner": "owner1",
                    "created_at": NOW_STR,
                    "file_count": 5,
                    "total_size": 1024,
                }
            ],
            "total": 1,
            "limit": 20,
            "offset": 0,
        }
        model = PaginatedResponse[Bucket].model_validate(data)
        assert model.total == 1
        assert model.limit == 20
        assert model.offset == 0
        assert len(model.items) == 1
        assert model.items[0].id == "abc123"
        assert model.items[0].name == "test"


# ---------- buckets.py ----------


def _make_bucket_data(**overrides: object) -> dict:
    base = {
        "id": "bkt_abc123",
        "name": "my-bucket",
        "owner": "owner1",
        "description": "A test bucket",
        "created_at": NOW_STR,
        "expires_at": "2026-04-04T12:00:00Z",
        "last_used_at": NOW_STR,
        "file_count": 10,
        "total_size": 2048,
    }
    base.update(overrides)
    return base


class TestBucket:
    def test_full(self) -> None:
        model = Bucket.model_validate(_make_bucket_data())
        assert model.id == "bkt_abc123"
        assert model.name == "my-bucket"
        assert model.owner == "owner1"
        assert model.description == "A test bucket"
        assert model.file_count == 10
        assert model.total_size == 2048
        assert model.expires_at is not None
        assert model.last_used_at is not None

    def test_minimal(self) -> None:
        data = _make_bucket_data(description=None, expires_at=None, last_used_at=None)
        model = Bucket.model_validate(data)
        assert model.description is None
        assert model.expires_at is None
        assert model.last_used_at is None

    def test_roundtrip(self) -> None:
        data = _make_bucket_data()
        model = Bucket.model_validate(data)
        dumped = model.model_dump(mode="json")
        model2 = Bucket.model_validate(dumped)
        assert model2 == model


class TestBucketDetail:
    def test_with_files(self) -> None:
        data = {
            **_make_bucket_data(),
            "unique_content_count": 8,
            "unique_content_size": 1500,
            "has_more_files": True,
            "files": [
                {
                    "path": "/docs/readme.txt",
                    "name": "readme.txt",
                    "size": 256,
                    "mime_type": "text/plain",
                    "short_code": "abc123",
                    "created_at": NOW_STR,
                    "updated_at": NOW_STR,
                }
            ],
        }
        model = BucketDetail.model_validate(data)
        assert model.unique_content_count == 8
        assert model.unique_content_size == 1500
        assert model.has_more_files is True
        assert model.files is not None
        assert len(model.files) == 1
        assert model.files[0].path == "/docs/readme.txt"

    def test_without_files(self) -> None:
        data = {**_make_bucket_data()}
        model = BucketDetail.model_validate(data)
        assert model.unique_content_count == 0
        assert model.unique_content_size == 0
        assert model.files is None
        assert model.has_more_files is False


# ---------- files.py ----------


def _make_file_data(**overrides: object) -> dict:
    base = {
        "path": "/images/photo.jpg",
        "name": "photo.jpg",
        "size": 50000,
        "mime_type": "image/jpeg",
        "short_code": "xYz789",
        "short_url": "https://example.com/s/xYz789",
        "sha256": "abcdef1234567890",
        "created_at": NOW_STR,
        "updated_at": NOW_STR,
    }
    base.update(overrides)
    return base


class TestBucketFile:
    def test_full(self) -> None:
        model = BucketFile.model_validate(_make_file_data())
        assert model.path == "/images/photo.jpg"
        assert model.name == "photo.jpg"
        assert model.size == 50000
        assert model.mime_type == "image/jpeg"
        assert model.short_code == "xYz789"
        assert model.short_url == "https://example.com/s/xYz789"
        assert model.sha256 == "abcdef1234567890"

    def test_minimal(self) -> None:
        data = _make_file_data(short_code=None, short_url=None, sha256=None)
        model = BucketFile.model_validate(data)
        assert model.short_code is None
        assert model.short_url is None
        assert model.sha256 is None


class TestUploadResponse:
    def test_with_dedup(self) -> None:
        data = {
            "uploaded": [
                {
                    "path": "/docs/file.txt",
                    "name": "file.txt",
                    "size": 100,
                    "mime_type": "text/plain",
                    "short_code": "abc",
                    "sha256": "deadbeef",
                    "deduplicated": True,
                    "created_at": NOW_STR,
                    "updated_at": NOW_STR,
                },
                {
                    "path": "/docs/file2.txt",
                    "name": "file2.txt",
                    "size": 200,
                    "mime_type": "text/plain",
                    "deduplicated": False,
                    "created_at": NOW_STR,
                    "updated_at": NOW_STR,
                },
            ]
        }
        model = UploadResponse.model_validate(data)
        assert len(model.uploaded) == 2
        assert model.uploaded[0].deduplicated is True
        assert model.uploaded[1].deduplicated is False


class TestVerifyResponse:
    def test_valid(self) -> None:
        data = {
            "path": "/docs/file.txt",
            "stored_hash": "aaa",
            "computed_hash": "aaa",
            "valid": True,
        }
        model = VerifyResponse.model_validate(data)
        assert model.valid is True
        assert model.stored_hash == model.computed_hash

    def test_invalid(self) -> None:
        data = {
            "path": "/docs/file.txt",
            "stored_hash": "aaa",
            "computed_hash": "bbb",
            "valid": False,
        }
        model = VerifyResponse.model_validate(data)
        assert model.valid is False


class TestFileTreeResponse:
    def test_with_directories_and_cursor(self) -> None:
        data = {
            "prefix": "docs/",
            "delimiter": "/",
            "directories": [
                {"path": "docs/sub/", "file_count": 3, "total_size": 900},
            ],
            "files": [_make_file_data()],
            "total_files": 1,
            "total_directories": 1,
            "cursor": "next_page_token",
        }
        model = FileTreeResponse.model_validate(data)
        assert model.prefix == "docs/"
        assert model.delimiter == "/"
        assert len(model.directories) == 1
        assert model.directories[0].path == "docs/sub/"
        assert model.directories[0].file_count == 3
        assert len(model.files) == 1
        assert model.cursor == "next_page_token"

    def test_no_prefix_no_cursor(self) -> None:
        data = {
            "delimiter": "/",
            "directories": [],
            "files": [],
            "total_files": 0,
            "total_directories": 0,
        }
        model = FileTreeResponse.model_validate(data)
        assert model.prefix is None
        assert model.cursor is None


class TestDirectoryListingResponse:
    def test_basic(self) -> None:
        data = {
            "files": [_make_file_data()],
            "folders": ["images/", "docs/"],
            "total_files": 1,
            "total_folders": 2,
            "limit": 50,
            "offset": 0,
        }
        model = DirectoryListingResponse.model_validate(data)
        assert len(model.files) == 1
        assert model.folders == ["images/", "docs/"]
        assert model.total_files == 1
        assert model.total_folders == 2
        assert model.limit == 50
        assert model.offset == 0
