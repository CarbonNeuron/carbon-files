from __future__ import annotations

from collections.abc import Callable


class CarbonFilesEvents:
    """SignalR real-time event handler for CarbonFiles.

    Requires the 'events' extra: pip install carbonfiles[events]
    """

    def __init__(self, base_url: str, api_key: str | None = None):
        self._base_url = base_url.rstrip("/")
        self._api_key = api_key
        self._handlers: dict[str, list[Callable]] = {
            "FileCreated": [],
            "FileUpdated": [],
            "FileDeleted": [],
            "BucketCreated": [],
            "BucketUpdated": [],
            "BucketDeleted": [],
        }
        self._connection = None

    def _register(self, event: str, handler: Callable) -> Callable:
        """Register a handler. Returns an unsubscribe callable."""
        self._handlers[event].append(handler)

        def unsubscribe():
            self._handlers[event].remove(handler)

        return unsubscribe

    def on_file_created(self, handler: Callable) -> Callable:
        """Register handler for FileCreated events. Can be used as decorator.
        Returns an unsubscribe callable."""
        return self._register("FileCreated", handler)

    def on_file_updated(self, handler: Callable) -> Callable:
        return self._register("FileUpdated", handler)

    def on_file_deleted(self, handler: Callable) -> Callable:
        return self._register("FileDeleted", handler)

    def on_bucket_created(self, handler: Callable) -> Callable:
        return self._register("BucketCreated", handler)

    def on_bucket_updated(self, handler: Callable) -> Callable:
        return self._register("BucketUpdated", handler)

    def on_bucket_deleted(self, handler: Callable) -> Callable:
        return self._register("BucketDeleted", handler)

    def connect(self) -> None:
        """Connect to the SignalR hub. Requires signalr-async package."""
        try:
            import signalr_async  # noqa: F401
        except ImportError:
            raise ImportError(
                "signalr-async is required for real-time events. Install it with: pip install carbonfiles[events]"
            )
        # TODO: Implement actual SignalR connection
        raise NotImplementedError("SignalR connection not yet implemented")

    def disconnect(self) -> None:
        """Disconnect from the SignalR hub."""
        if self._connection is not None:
            # TODO: Implement disconnect
            self._connection = None

    def subscribe_bucket(self, bucket_id: str) -> None:
        """Subscribe to events for a specific bucket."""
        if self._connection is None:
            raise RuntimeError("Not connected. Call connect() first.")

    def unsubscribe_bucket(self, bucket_id: str) -> None:
        if self._connection is None:
            raise RuntimeError("Not connected. Call connect() first.")

    def subscribe_file(self, bucket_id: str, path: str) -> None:
        if self._connection is None:
            raise RuntimeError("Not connected. Call connect() first.")

    def unsubscribe_file(self, bucket_id: str, path: str) -> None:
        if self._connection is None:
            raise RuntimeError("Not connected. Call connect() first.")

    def subscribe_all(self) -> None:
        if self._connection is None:
            raise RuntimeError("Not connected. Call connect() first.")

    def unsubscribe_all(self) -> None:
        if self._connection is None:
            raise RuntimeError("Not connected. Call connect() first.")
