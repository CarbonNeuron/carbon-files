from __future__ import annotations

import json

import httpx
import pytest

from carbonfiles.exceptions import CarbonFilesError
from carbonfiles.models import Bucket
from carbonfiles.transport import AsyncTransport, SyncTransport

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

BUCKET_JSON = {
    "id": "abc123",
    "name": "test-bucket",
    "owner": "owner1",
    "description": None,
    "created_at": "2026-01-01T00:00:00Z",
    "expires_at": None,
    "last_used_at": None,
    "file_count": 0,
    "total_size": 0,
}


def make_sync_transport(handler, api_key: str | None = "test-key") -> SyncTransport:
    """Create a SyncTransport backed by a mock handler."""
    mock = httpx.MockTransport(handler)
    client = httpx.Client(transport=mock, base_url="https://example.com")
    if api_key:
        client.headers["authorization"] = f"Bearer {api_key}"
    return SyncTransport("https://example.com", api_key, http_client=client)


def make_async_transport(handler, api_key: str | None = "test-key") -> AsyncTransport:
    """Create an AsyncTransport backed by a mock handler."""
    mock = httpx.MockTransport(handler)
    client = httpx.AsyncClient(transport=mock, base_url="https://example.com")
    if api_key:
        client.headers["authorization"] = f"Bearer {api_key}"
    return AsyncTransport("https://example.com", api_key, http_client=client)


def json_response(data: dict, status_code: int = 200) -> httpx.Response:
    return httpx.Response(status_code, json=data)


# ===========================================================================
# TestBuildUrl
# ===========================================================================


class TestBuildUrl:
    def test_no_query(self) -> None:
        assert SyncTransport.build_url("/api/buckets", None) == "/api/buckets"

    def test_with_params(self) -> None:
        result = SyncTransport.build_url("/api/buckets", {"limit": "10", "offset": "0"})
        assert result == "/api/buckets?limit=10&offset=0"

    def test_skips_none(self) -> None:
        result = SyncTransport.build_url("/api/buckets", {"limit": "10", "offset": None})
        assert result == "/api/buckets?limit=10"

    def test_encodes_values(self) -> None:
        result = SyncTransport.build_url("/api/files", {"path": "hello world/foo&bar"})
        assert result == "/api/files?path=hello%20world%2Ffoo%26bar"

    def test_all_none_returns_bare_path(self) -> None:
        result = SyncTransport.build_url("/api/buckets", {"limit": None, "offset": None})
        assert result == "/api/buckets"


# ===========================================================================
# TestSyncTransport
# ===========================================================================


class TestSyncTransport:
    def test_get_deserializes_json(self) -> None:
        def handler(request: httpx.Request) -> httpx.Response:
            return json_response(BUCKET_JSON)

        transport = make_sync_transport(handler)
        bucket = transport.get("/api/buckets/abc123", Bucket)
        assert isinstance(bucket, Bucket)
        assert bucket.id == "abc123"
        assert bucket.name == "test-bucket"

    def test_get_with_query_builds_url(self) -> None:
        captured_url: str | None = None

        def handler(request: httpx.Request) -> httpx.Response:
            nonlocal captured_url
            captured_url = str(request.url)
            return json_response(BUCKET_JSON)

        transport = make_sync_transport(handler)
        transport.get("/api/buckets", Bucket, query={"limit": "10", "offset": "0"})
        assert captured_url is not None
        assert "limit=10" in captured_url
        assert "offset=0" in captured_url

    def test_auth_header_injected(self) -> None:
        captured_headers: httpx.Headers | None = None

        def handler(request: httpx.Request) -> httpx.Response:
            nonlocal captured_headers
            captured_headers = request.headers
            return json_response(BUCKET_JSON)

        transport = make_sync_transport(handler, api_key="my-secret")
        transport.get("/api/buckets/abc123", Bucket)
        assert captured_headers is not None
        assert captured_headers["authorization"] == "Bearer my-secret"

    def test_auth_header_omitted_when_no_key(self) -> None:
        captured_headers: httpx.Headers | None = None

        def handler(request: httpx.Request) -> httpx.Response:
            nonlocal captured_headers
            captured_headers = request.headers
            return json_response(BUCKET_JSON)

        transport = make_sync_transport(handler, api_key=None)
        transport.get("/api/buckets/abc123", Bucket)
        assert captured_headers is not None
        assert "authorization" not in captured_headers

    def test_error_response_raises(self) -> None:
        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(404, json={"error": "Not found", "hint": "Check ID"})

        transport = make_sync_transport(handler)
        with pytest.raises(CarbonFilesError) as exc_info:
            transport.get("/api/buckets/nope", Bucket)
        assert exc_info.value.status_code == 404
        assert exc_info.value.error == "Not found"
        assert exc_info.value.hint == "Check ID"

    def test_error_without_json_body(self) -> None:
        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(500, text="Internal Server Error")

        transport = make_sync_transport(handler)
        with pytest.raises(CarbonFilesError) as exc_info:
            transport.get("/api/buckets/abc123", Bucket)
        assert exc_info.value.status_code == 500
        assert "Internal Server Error" in exc_info.value.error

    def test_get_string_returns_text(self) -> None:
        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(200, text="hello world")

        transport = make_sync_transport(handler)
        result = transport.get_string("/api/text")
        assert result == "hello world"

    def test_get_bytes_returns_content(self) -> None:
        payload = b"\x00\x01\x02\x03"

        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(200, content=payload)

        transport = make_sync_transport(handler)
        result = transport.get_bytes("/api/binary")
        assert result == payload

    def test_post_sends_json_body(self) -> None:
        captured_body: dict | None = None

        def handler(request: httpx.Request) -> httpx.Response:
            nonlocal captured_body
            captured_body = json.loads(request.content)
            return json_response(BUCKET_JSON)

        transport = make_sync_transport(handler)
        transport.post("/api/buckets", {"name": "test-bucket"}, Bucket)
        assert captured_body == {"name": "test-bucket"}

    def test_post_deserializes_response(self) -> None:
        def handler(request: httpx.Request) -> httpx.Response:
            return json_response(BUCKET_JSON)

        transport = make_sync_transport(handler)
        bucket = transport.post("/api/buckets", {"name": "test-bucket"}, Bucket)
        assert isinstance(bucket, Bucket)
        assert bucket.name == "test-bucket"

    def test_delete_sends_delete(self) -> None:
        captured_method: str | None = None

        def handler(request: httpx.Request) -> httpx.Response:
            nonlocal captured_method
            captured_method = request.method
            return httpx.Response(204)

        transport = make_sync_transport(handler)
        transport.delete("/api/buckets/abc123")
        assert captured_method == "DELETE"

    def test_patch_sends_json(self) -> None:
        captured_method: str | None = None
        captured_body: dict | None = None

        def handler(request: httpx.Request) -> httpx.Response:
            nonlocal captured_method, captured_body
            captured_method = request.method
            captured_body = json.loads(request.content)
            return json_response(BUCKET_JSON)

        transport = make_sync_transport(handler)
        transport.patch("/api/buckets/abc123", {"name": "updated"}, Bucket)
        assert captured_method == "PATCH"
        assert captured_body == {"name": "updated"}

    def test_put_stream_sends_content(self) -> None:
        captured_content: bytes | None = None
        captured_content_type: str | None = None

        def handler(request: httpx.Request) -> httpx.Response:
            nonlocal captured_content, captured_content_type
            captured_content = request.content
            captured_content_type = request.headers.get("content-type")
            return json_response(BUCKET_JSON)

        transport = make_sync_transport(handler)
        transport.put_stream("/api/upload", b"file-bytes", Bucket)
        assert captured_content == b"file-bytes"
        assert captured_content_type == "application/octet-stream"

    def test_send_raw_returns_response(self) -> None:
        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(200, text="raw-response")

        transport = make_sync_transport(handler)
        response = transport.send_raw("GET", "/api/raw")
        assert isinstance(response, httpx.Response)
        assert response.text == "raw-response"

    def test_post_no_response(self) -> None:
        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(204)

        transport = make_sync_transport(handler)
        result = transport.post_no_response("/api/action", {"key": "value"})
        assert result is None


# ===========================================================================
# TestAsyncTransport
# ===========================================================================


class TestAsyncTransport:
    async def test_get_deserializes_json(self) -> None:
        def handler(request: httpx.Request) -> httpx.Response:
            return json_response(BUCKET_JSON)

        transport = make_async_transport(handler)
        bucket = await transport.get("/api/buckets/abc123", Bucket)
        assert isinstance(bucket, Bucket)
        assert bucket.id == "abc123"

    async def test_error_response_raises(self) -> None:
        def handler(request: httpx.Request) -> httpx.Response:
            return httpx.Response(404, json={"error": "Not found", "hint": "Check ID"})

        transport = make_async_transport(handler)
        with pytest.raises(CarbonFilesError) as exc_info:
            await transport.get("/api/buckets/nope", Bucket)
        assert exc_info.value.status_code == 404
        assert exc_info.value.error == "Not found"
        assert exc_info.value.hint == "Check ID"

    async def test_auth_header_injected(self) -> None:
        captured_headers: httpx.Headers | None = None

        def handler(request: httpx.Request) -> httpx.Response:
            nonlocal captured_headers
            captured_headers = request.headers
            return json_response(BUCKET_JSON)

        transport = make_async_transport(handler, api_key="my-secret")
        await transport.get("/api/buckets/abc123", Bucket)
        assert captured_headers is not None
        assert captured_headers["authorization"] == "Bearer my-secret"

    async def test_delete_sends_delete(self) -> None:
        captured_method: str | None = None

        def handler(request: httpx.Request) -> httpx.Response:
            nonlocal captured_method
            captured_method = request.method
            return httpx.Response(204)

        transport = make_async_transport(handler)
        await transport.delete("/api/buckets/abc123")
        assert captured_method == "DELETE"

    async def test_post_sends_json_body(self) -> None:
        captured_body: dict | None = None

        def handler(request: httpx.Request) -> httpx.Response:
            nonlocal captured_body
            captured_body = json.loads(request.content)
            return json_response(BUCKET_JSON)

        transport = make_async_transport(handler)
        await transport.post("/api/buckets", {"name": "test-bucket"}, Bucket)
        assert captured_body == {"name": "test-bucket"}
