import { CarbonFilesError } from "./errors.js";
import type { ErrorResponse } from "./types.js";

export interface RequestOptions {
  signal?: AbortSignal;
}

export class HttpTransport {
  private readonly baseUrl: string;

  constructor(
    baseUrl: string,
    private readonly apiKey: string | undefined,
    private readonly fetchFn: typeof globalThis.fetch = globalThis.fetch.bind(globalThis),
  ) {
    this.baseUrl = baseUrl.replace(/\/+$/, "");
  }

  async get<T>(path: string, query?: Record<string, string | undefined>, options?: RequestOptions): Promise<T> {
    const response = await this.send("GET", path, undefined, query, options);
    return response.json() as Promise<T>;
  }

  async post<T>(path: string, body?: unknown, query?: Record<string, string | undefined>, options?: RequestOptions): Promise<T> {
    const response = await this.send("POST", path, body, query, options);
    return response.json() as Promise<T>;
  }

  async patch<T>(path: string, body?: unknown, query?: Record<string, string | undefined>, options?: RequestOptions): Promise<T> {
    const response = await this.send("PATCH", path, body, query, options);
    return response.json() as Promise<T>;
  }

  async put<T>(path: string, body: BodyInit, headers: Record<string, string>, query?: Record<string, string | undefined>, options?: RequestOptions): Promise<T> {
    const url = this.buildUrl(path, query);
    const request = new Request(url, {
      method: "PUT",
      body,
      headers: { ...this.authHeaders(), ...headers },
      signal: options?.signal,
    });
    const response = await this.fetchFn(request);
    await this.throwIfError(response);
    return response.json() as Promise<T>;
  }

  async delete(path: string, options?: RequestOptions): Promise<void> {
    const response = await this.send("DELETE", path, undefined, undefined, options);
    if (response.body) {
      await response.text();
    }
  }

  async getPlainText(path: string, options?: RequestOptions): Promise<string> {
    const response = await this.send("GET", path, undefined, undefined, options);
    return response.text();
  }

  async getResponse(path: string, query?: Record<string, string | undefined>, options?: RequestOptions): Promise<Response> {
    return this.send("GET", path, undefined, query, options);
  }

  async sendRaw(request: Request): Promise<Response> {
    const response = await this.fetchFn(request);
    await this.throwIfError(response);
    return response;
  }

  buildUrl(path: string, query?: Record<string, string | undefined>): string {
    const url = new URL(path, this.baseUrl);
    if (query) {
      for (const [key, value] of Object.entries(query)) {
        if (value !== undefined) {
          url.searchParams.set(key, value);
        }
      }
    }
    return url.toString();
  }

  authHeaders(): Record<string, string> {
    const headers: Record<string, string> = {};
    if (this.apiKey) {
      headers["Authorization"] = `Bearer ${this.apiKey}`;
    }
    return headers;
  }

  private async send(
    method: string,
    path: string,
    body?: unknown,
    query?: Record<string, string | undefined>,
    options?: RequestOptions,
  ): Promise<Response> {
    const url = this.buildUrl(path, query);
    const headers: Record<string, string> = { ...this.authHeaders() };
    let reqBody: BodyInit | undefined;
    if (body !== undefined) {
      headers["Content-Type"] = "application/json";
      reqBody = JSON.stringify(body);
    }
    const request = new Request(url, { method, headers, body: reqBody, signal: options?.signal });
    const response = await this.fetchFn(request);
    await this.throwIfError(response);
    return response;
  }

  private async throwIfError(response: Response): Promise<void> {
    if (response.ok) return;
    let error: string = response.statusText;
    let hint: string | undefined;
    try {
      const body = (await response.json()) as ErrorResponse;
      error = body.error;
      hint = body.hint;
    } catch {
      // Fall back to status text
    }
    throw new CarbonFilesError(response.status, error, hint);
  }
}
