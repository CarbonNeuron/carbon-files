import { describe, it, expect, vi } from "vitest";
import { HttpTransport } from "../src/transport.js";
import { CarbonFilesError } from "../src/errors.js";

function mockFetch(status: number, body: unknown, contentType = "application/json") {
  return vi.fn().mockResolvedValue(
    new Response(
      typeof body === "string" ? body : JSON.stringify(body),
      { status, headers: { "Content-Type": contentType } },
    ),
  );
}

describe("HttpTransport", () => {
  it("sends Authorization Bearer header", async () => {
    const fetch = mockFetch(200, { status: "ok" });
    const transport = new HttpTransport("https://example.com", "my-token", fetch);
    await transport.get<{ status: string }>("/test");
    expect(fetch).toHaveBeenCalledOnce();
    const req = fetch.mock.calls[0]![0] as Request;
    expect(req.headers.get("Authorization")).toBe("Bearer my-token");
  });

  it("GET deserializes JSON response", async () => {
    const fetch = mockFetch(200, { status: "healthy", uptime_seconds: 42 });
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const result = await transport.get<{ status: string; uptime_seconds: number }>("/healthz");
    expect(result).toEqual({ status: "healthy", uptime_seconds: 42 });
  });

  it("throws CarbonFilesError on 4xx", async () => {
    const fetch = vi.fn().mockImplementation(() =>
      Promise.resolve(
        new Response(
          JSON.stringify({ error: "Not found", hint: "Check ID" }),
          { status: 404, headers: { "Content-Type": "application/json" } },
        ),
      ),
    );
    const transport = new HttpTransport("https://example.com", "key", fetch);
    await expect(transport.get("/missing")).rejects.toThrow(CarbonFilesError);
    try {
      await transport.get("/missing");
    } catch (e) {
      const err = e as CarbonFilesError;
      expect(err.status).toBe(404);
      expect(err.error).toBe("Not found");
      expect(err.hint).toBe("Check ID");
    }
  });

  it("POST sends JSON body", async () => {
    const fetch = mockFetch(200, { id: "abc" });
    const transport = new HttpTransport("https://example.com", "key", fetch);
    await transport.post("/items", { name: "test" });
    const req = fetch.mock.calls[0]![0] as Request;
    expect(req.method).toBe("POST");
    expect(req.headers.get("Content-Type")).toBe("application/json");
    const body = await req.json();
    expect(body).toEqual({ name: "test" });
  });

  it("DELETE sends correct method and URL", async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(null, { status: 200 }));
    const transport = new HttpTransport("https://example.com", "key", fetch);
    await transport.delete("/items/1");
    const req = fetch.mock.calls[0]![0] as Request;
    expect(req.method).toBe("DELETE");
    expect(new URL(req.url).pathname).toBe("/items/1");
  });

  it("PATCH sends correct method and deserializes", async () => {
    const fetch = mockFetch(200, { updated: true });
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const result = await transport.patch<{ updated: boolean }>("/items/1", { name: "new" });
    const req = fetch.mock.calls[0]![0] as Request;
    expect(req.method).toBe("PATCH");
    expect(result).toEqual({ updated: true });
  });

  it("getPlainText returns string", async () => {
    const fetch = mockFetch(200, "plain text", "text/plain");
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const result = await transport.getPlainText("/summary");
    expect(result).toBe("plain text");
  });

  it("getResponse returns Response", async () => {
    const fetch = mockFetch(200, "binary", "application/octet-stream");
    const transport = new HttpTransport("https://example.com", "key", fetch);
    const response = await transport.getResponse("/file");
    expect(response).toBeInstanceOf(Response);
    expect(response.body).toBeDefined();
  });

  it("builds query string from params", async () => {
    const fetch = mockFetch(200, { items: [] });
    const transport = new HttpTransport("https://example.com", "key", fetch);
    await transport.get("/items", { limit: "10", offset: "5" });
    const req = fetch.mock.calls[0]![0] as Request;
    const url = new URL(req.url);
    expect(url.searchParams.get("limit")).toBe("10");
    expect(url.searchParams.get("offset")).toBe("5");
  });

  it("omits undefined query params", async () => {
    const fetch = mockFetch(200, { items: [] });
    const transport = new HttpTransport("https://example.com", "key", fetch);
    await transport.get("/items", { limit: "10", offset: undefined });
    const req = fetch.mock.calls[0]![0] as Request;
    const url = new URL(req.url);
    expect(url.searchParams.has("offset")).toBe(false);
    expect(url.searchParams.get("limit")).toBe("10");
  });

  it("works without auth token", async () => {
    const fetch = mockFetch(200, { ok: true });
    const transport = new HttpTransport("https://example.com", undefined, fetch);
    await transport.get("/test");
    const req = fetch.mock.calls[0]![0] as Request;
    expect(req.headers.has("Authorization")).toBe(false);
  });
});
