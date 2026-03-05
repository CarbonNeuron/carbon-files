from __future__ import annotations

from datetime import datetime

from pydantic import BaseModel


class BucketFile(BaseModel):
    path: str
    name: str
    size: int
    mime_type: str
    short_code: str | None = None
    short_url: str | None = None
    sha256: str | None = None
    created_at: datetime
    updated_at: datetime


class UploadedFile(BaseModel):
    path: str
    name: str
    size: int
    mime_type: str
    short_code: str | None = None
    short_url: str | None = None
    sha256: str | None = None
    deduplicated: bool = False
    created_at: datetime
    updated_at: datetime


class UploadResponse(BaseModel):
    uploaded: list[UploadedFile]


class VerifyResponse(BaseModel):
    path: str
    stored_hash: str
    computed_hash: str
    valid: bool


class DirectoryEntry(BaseModel):
    path: str
    file_count: int
    total_size: int


class FileTreeResponse(BaseModel):
    prefix: str | None = None
    delimiter: str
    directories: list[DirectoryEntry]
    files: list[BucketFile]
    total_files: int
    total_directories: int
    cursor: str | None = None


class DirectoryListingResponse(BaseModel):
    files: list[BucketFile]
    folders: list[str]
    total_files: int
    total_folders: int
    limit: int
    offset: int
