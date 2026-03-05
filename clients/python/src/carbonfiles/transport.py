from __future__ import annotations

import urllib.parse
from collections.abc import Callable
from typing import BinaryIO, TypeVar

import httpx

from carbonfiles.exceptions import CarbonFilesError

T = TypeVar("T")


class SyncTransport:
    """Synchronous HTTP transport wrapping ``httpx.Client``."""

    def __init__(
        self,
        base_url: str,
        api_key: str | None = None,
        *,
        http_client: httpx.Client | None = None,
    ):
        if http_client is not None:
            self._client = http_client
        else:
            headers: dict[str, str] = {}
            if api_key:
                headers["authorization"] = f"Bearer {api_key}"
            self._client = httpx.Client(base_url=base_url, headers=headers)

    # -- public request helpers ------------------------------------------------

    def get(self, path: str, model_type: type[T], query: dict[str, str | None] | None = None) -> T:
        url = self.build_url(path, query)
        response = self._client.get(url)
        self._handle_error(response)
        return model_type.model_validate(response.json())  # type: ignore[attr-defined]

    def get_string(self, path: str) -> str:
        response = self._client.get(path)
        self._handle_error(response)
        return response.text

    def get_bytes(self, path: str) -> bytes:
        response = self._client.get(path)
        self._handle_error(response)
        return response.content

    def post(self, path: str, body: dict, model_type: type[T]) -> T:
        response = self._client.post(path, json=body)
        self._handle_error(response)
        return model_type.model_validate(response.json())  # type: ignore[attr-defined]

    def post_no_response(self, path: str, body: dict) -> None:
        response = self._client.post(path, json=body)
        self._handle_error(response)

    def patch(self, path: str, body: dict, model_type: type[T]) -> T:
        response = self._client.patch(path, json=body)
        self._handle_error(response)
        return model_type.model_validate(response.json())  # type: ignore[attr-defined]

    def delete(self, path: str) -> None:
        response = self._client.delete(path)
        self._handle_error(response)

    def put_stream(
        self,
        path: str,
        content: bytes | BinaryIO,
        model_type: type[T],
        *,
        headers: dict[str, str] | None = None,
        progress: Callable[[int, int | None, float | None], None] | None = None,
    ) -> T:
        """PUT with streaming content (for uploads). Optional *progress* callback."""
        request_headers: dict[str, str] = {"content-type": "application/octet-stream"}
        if headers:
            request_headers.update(headers)

        if progress and hasattr(content, "read"):
            total: int | None = None
            if hasattr(content, "seek") and hasattr(content, "tell"):
                pos = content.tell()  # type: ignore[union-attr]
                content.seek(0, 2)  # type: ignore[union-attr]
                total = content.tell()  # type: ignore[union-attr]
                content.seek(pos)  # type: ignore[union-attr]

            def generate():  # noqa: ANN202
                bytes_sent = 0
                while True:
                    chunk = content.read(81920)  # type: ignore[union-attr]
                    if not chunk:
                        break
                    bytes_sent += len(chunk)
                    pct = (bytes_sent / total * 100) if total else None
                    progress(bytes_sent, total, pct)  # type: ignore[misc]
                    yield chunk

            response = self._client.put(path, content=generate(), headers=request_headers)
        else:
            response = self._client.put(path, content=content, headers=request_headers)

        self._handle_error(response)
        return model_type.model_validate(response.json())  # type: ignore[attr-defined]

    def send_raw(
        self,
        method: str,
        path: str,
        *,
        content: bytes | BinaryIO | None = None,
        headers: dict[str, str] | None = None,
    ) -> httpx.Response:
        """Send a raw request and return the ``httpx.Response``. Caller handles errors."""
        request = self._client.build_request(method, path, content=content, headers=headers)
        return self._client.send(request)

    def close(self) -> None:
        self._client.close()

    # -- helpers ---------------------------------------------------------------

    @staticmethod
    def build_url(path: str, query: dict[str, str | None] | None) -> str:
        """Build URL with query params, skipping ``None`` values."""
        if not query:
            return path
        params = "&".join(
            f"{k}={urllib.parse.quote(str(v), safe='')}" for k, v in query.items() if v is not None
        )
        return f"{path}?{params}" if params else path

    def _handle_error(self, response: httpx.Response) -> None:
        if response.is_success:
            return
        try:
            data = response.json()
            error = data.get("error", response.text)
            hint = data.get("hint")
            raise CarbonFilesError(response.status_code, error, hint)
        except (ValueError, KeyError):
            raise CarbonFilesError(response.status_code, response.text)


class AsyncTransport:
    """Asynchronous HTTP transport wrapping ``httpx.AsyncClient``."""

    def __init__(
        self,
        base_url: str,
        api_key: str | None = None,
        *,
        http_client: httpx.AsyncClient | None = None,
    ):
        if http_client is not None:
            self._client = http_client
        else:
            headers: dict[str, str] = {}
            if api_key:
                headers["authorization"] = f"Bearer {api_key}"
            self._client = httpx.AsyncClient(base_url=base_url, headers=headers)

    # -- public request helpers ------------------------------------------------

    async def get(self, path: str, model_type: type[T], query: dict[str, str | None] | None = None) -> T:
        url = self.build_url(path, query)
        response = await self._client.get(url)
        self._handle_error(response)
        return model_type.model_validate(response.json())  # type: ignore[attr-defined]

    async def get_string(self, path: str) -> str:
        response = await self._client.get(path)
        self._handle_error(response)
        return response.text

    async def get_bytes(self, path: str) -> bytes:
        response = await self._client.get(path)
        self._handle_error(response)
        return response.content

    async def post(self, path: str, body: dict, model_type: type[T]) -> T:
        response = await self._client.post(path, json=body)
        self._handle_error(response)
        return model_type.model_validate(response.json())  # type: ignore[attr-defined]

    async def post_no_response(self, path: str, body: dict) -> None:
        response = await self._client.post(path, json=body)
        self._handle_error(response)

    async def patch(self, path: str, body: dict, model_type: type[T]) -> T:
        response = await self._client.patch(path, json=body)
        self._handle_error(response)
        return model_type.model_validate(response.json())  # type: ignore[attr-defined]

    async def delete(self, path: str) -> None:
        response = await self._client.delete(path)
        self._handle_error(response)

    async def put_stream(
        self,
        path: str,
        content: bytes | BinaryIO,
        model_type: type[T],
        *,
        headers: dict[str, str] | None = None,
        progress: Callable[[int, int | None, float | None], None] | None = None,
    ) -> T:
        """PUT with streaming content (for uploads). Optional *progress* callback."""
        request_headers: dict[str, str] = {"content-type": "application/octet-stream"}
        if headers:
            request_headers.update(headers)

        if progress and hasattr(content, "read"):
            total: int | None = None
            if hasattr(content, "seek") and hasattr(content, "tell"):
                pos = content.tell()  # type: ignore[union-attr]
                content.seek(0, 2)  # type: ignore[union-attr]
                total = content.tell()  # type: ignore[union-attr]
                content.seek(pos)  # type: ignore[union-attr]

            def generate():  # noqa: ANN202
                bytes_sent = 0
                while True:
                    chunk = content.read(81920)  # type: ignore[union-attr]
                    if not chunk:
                        break
                    bytes_sent += len(chunk)
                    pct = (bytes_sent / total * 100) if total else None
                    progress(bytes_sent, total, pct)  # type: ignore[misc]
                    yield chunk

            response = await self._client.put(path, content=generate(), headers=request_headers)
        else:
            response = await self._client.put(path, content=content, headers=request_headers)

        self._handle_error(response)
        return model_type.model_validate(response.json())  # type: ignore[attr-defined]

    async def send_raw(
        self,
        method: str,
        path: str,
        *,
        content: bytes | BinaryIO | None = None,
        headers: dict[str, str] | None = None,
    ) -> httpx.Response:
        """Send a raw request and return the ``httpx.Response``. Caller handles errors."""
        request = self._client.build_request(method, path, content=content, headers=headers)
        return await self._client.send(request)

    async def close(self) -> None:
        await self._client.aclose()

    # -- helpers ---------------------------------------------------------------

    build_url = staticmethod(SyncTransport.build_url)

    def _handle_error(self, response: httpx.Response) -> None:
        if response.is_success:
            return
        try:
            data = response.json()
            error = data.get("error", response.text)
            hint = data.get("hint")
            raise CarbonFilesError(response.status_code, error, hint)
        except (ValueError, KeyError):
            raise CarbonFilesError(response.status_code, response.text)
