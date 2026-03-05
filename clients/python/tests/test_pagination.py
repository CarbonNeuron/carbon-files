import httpx

from carbonfiles.resources.buckets import BucketsResource
from carbonfiles.resources.files import FilesResource
from carbonfiles.transport import SyncTransport


def make_transport(handler, api_key="test-key"):
    mock = httpx.MockTransport(handler)
    client = httpx.Client(transport=mock, base_url="https://example.com")
    return SyncTransport("https://example.com", api_key, http_client=client)


class TestBucketsPagination:
    def test_list_all_single_page(self):
        # total=2, limit=50 -> yields 1 page with 2 items
        call_count = 0

        def handler(request):
            nonlocal call_count
            call_count += 1
            items = [
                {
                    "id": f"b{i}",
                    "name": f"bucket-{i}",
                    "owner": "o",
                    "created_at": "2026-01-01T00:00:00Z",
                    "file_count": 0,
                    "total_size": 0,
                }
                for i in range(2)
            ]
            return httpx.Response(
                200, json={"items": items, "total": 2, "limit": 50, "offset": 0}
            )

        transport = make_transport(handler)
        pages = list(BucketsResource(transport).list_all())
        assert len(pages) == 1
        assert len(pages[0].items) == 2
        assert call_count == 1

    def test_list_all_multiple_pages(self):
        # total=5, limit=2 -> yields 3 pages (2+2+1)
        call_count = 0

        def handler(request):
            nonlocal call_count
            call_count += 1
            params = dict(request.url.params)
            offset = int(params.get("offset", "0"))
            limit = int(params.get("limit", "50"))
            remaining = 5 - offset
            count = min(limit, remaining)
            items = [
                {
                    "id": f"b{offset + i}",
                    "name": "b",
                    "owner": "o",
                    "created_at": "2026-01-01T00:00:00Z",
                    "file_count": 0,
                    "total_size": 0,
                }
                for i in range(count)
            ]
            return httpx.Response(
                200,
                json={
                    "items": items,
                    "total": 5,
                    "limit": limit,
                    "offset": offset,
                },
            )

        transport = make_transport(handler)
        pages = list(BucketsResource(transport).list_all(limit=2))
        assert len(pages) == 3
        assert len(pages[0].items) == 2
        assert len(pages[1].items) == 2
        assert len(pages[2].items) == 1

    def test_list_all_empty(self):
        # total=0 -> yields 1 empty page
        def handler(request):
            return httpx.Response(
                200, json={"items": [], "total": 0, "limit": 50, "offset": 0}
            )

        transport = make_transport(handler)
        pages = list(BucketsResource(transport).list_all())
        assert len(pages) == 1
        assert len(pages[0].items) == 0


class TestFilesPagination:
    def test_list_all_single_page(self):
        def handler(request):
            items = [
                {
                    "path": "f.txt",
                    "name": "f.txt",
                    "size": 0,
                    "mime_type": "text/plain",
                    "created_at": "2026-01-01T00:00:00Z",
                    "updated_at": "2026-01-01T00:00:00Z",
                }
            ]
            return httpx.Response(
                200, json={"items": items, "total": 1, "limit": 50, "offset": 0}
            )

        transport = make_transport(handler)
        pages = list(FilesResource(transport, "b1").list_all())
        assert len(pages) == 1

    def test_list_all_multiple_pages(self):
        def handler(request):
            params = dict(request.url.params)
            offset = int(params.get("offset", "0"))
            limit = int(params.get("limit", "50"))
            remaining = 3 - offset
            count = min(limit, remaining)
            items = [
                {
                    "path": f"f{offset + i}.txt",
                    "name": f"f{offset + i}.txt",
                    "size": 0,
                    "mime_type": "text/plain",
                    "created_at": "2026-01-01T00:00:00Z",
                    "updated_at": "2026-01-01T00:00:00Z",
                }
                for i in range(count)
            ]
            return httpx.Response(
                200,
                json={
                    "items": items,
                    "total": 3,
                    "limit": limit,
                    "offset": offset,
                },
            )

        transport = make_transport(handler)
        pages = list(FilesResource(transport, "b1").list_all(limit=2))
        assert len(pages) == 2  # 2+1
        assert len(pages[0].items) == 2
        assert len(pages[1].items) == 1
