import pytest

from carbonfiles.events import CarbonFilesEvents


def _noop(*args, **kwargs):
    pass


class TestCarbonFilesEvents:
    def test_on_file_created_registers_handler(self):
        events = CarbonFilesEvents("https://example.com")
        events.on_file_created(_noop)
        assert _noop in events._handlers["FileCreated"]

    def test_on_file_created_as_decorator(self):
        events = CarbonFilesEvents("https://example.com")

        def handle(bucket_id, file):
            pass

        events.on_file_created(handle)
        assert handle in events._handlers["FileCreated"]

    def test_handler_returns_unsubscribe(self):
        events = CarbonFilesEvents("https://example.com")

        def handler():
            pass

        unsub = events.on_file_created(handler)
        assert handler in events._handlers["FileCreated"]
        unsub()
        assert handler not in events._handlers["FileCreated"]

    def test_all_six_event_types_registrable(self):
        events = CarbonFilesEvents("https://example.com")

        for method in [
            events.on_file_created,
            events.on_file_updated,
            events.on_file_deleted,
            events.on_bucket_created,
            events.on_bucket_updated,
            events.on_bucket_deleted,
        ]:

            def handler():
                pass

            method(handler)

        assert len(events._handlers["FileCreated"]) == 1
        assert len(events._handlers["FileUpdated"]) == 1
        assert len(events._handlers["FileDeleted"]) == 1
        assert len(events._handlers["BucketCreated"]) == 1
        assert len(events._handlers["BucketUpdated"]) == 1
        assert len(events._handlers["BucketDeleted"]) == 1

    def test_connect_without_signalr_raises_import_error(self):
        events = CarbonFilesEvents("https://example.com")
        with pytest.raises(ImportError, match="signalr-async"):
            events.connect()

    def test_subscribe_without_connect_raises(self):
        events = CarbonFilesEvents("https://example.com")
        with pytest.raises(RuntimeError, match="Not connected"):
            events.subscribe_bucket("bucket-id")

    def test_multiple_handlers_per_event(self):
        events = CarbonFilesEvents("https://example.com")

        def h1():
            pass

        def h2():
            pass

        events.on_file_created(h1)
        events.on_file_created(h2)
        assert len(events._handlers["FileCreated"]) == 2
