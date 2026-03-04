import type { HttpTransport, RequestOptions } from "../transport.js";
import type {
  BucketFile,
  DirectoryListingResponse,
  FileTreeResponse,
  PaginatedResponse,
  PaginationOptions,
  UploadProgress,
  UploadResponse,
  VerifyResponse,
} from "../types.js";

export class FileOperations {
  constructor(
    private readonly transport: HttpTransport,
    private readonly bucketId: string,
  ) {}

  async list(params?: PaginationOptions, options?: RequestOptions): Promise<PaginatedResponse<BucketFile>> {
    const query: Record<string, string | undefined> = {};
    if (params?.limit !== undefined) query["limit"] = String(params.limit);
    if (params?.offset !== undefined) query["offset"] = String(params.offset);
    if (params?.sort) query["sort"] = params.sort;
    if (params?.order) query["order"] = params.order;
    return this.transport.get<PaginatedResponse<BucketFile>>(
      `/api/buckets/${encodeURIComponent(this.bucketId)}/files`,
      query,
      options,
    );
  }

  async listDirectory(
    path?: string,
    params?: PaginationOptions,
    options?: RequestOptions,
  ): Promise<DirectoryListingResponse> {
    const query: Record<string, string | undefined> = {};
    if (path) query["path"] = path;
    if (params?.limit !== undefined) query["limit"] = String(params.limit);
    if (params?.offset !== undefined) query["offset"] = String(params.offset);
    if (params?.sort) query["sort"] = params.sort;
    if (params?.order) query["order"] = params.order;
    return this.transport.get<DirectoryListingResponse>(
      `/api/buckets/${encodeURIComponent(this.bucketId)}/ls`,
      query,
      options,
    );
  }

  async listTree(
    params?: { delimiter?: string; prefix?: string; limit?: number; cursor?: string },
    options?: RequestOptions,
  ): Promise<FileTreeResponse> {
    const query: Record<string, string | undefined> = {};
    if (params?.delimiter) query["delimiter"] = params.delimiter;
    if (params?.prefix) query["prefix"] = params.prefix;
    if (params?.limit !== undefined) query["limit"] = String(params.limit);
    if (params?.cursor) query["cursor"] = params.cursor;
    return this.transport.get<FileTreeResponse>(
      `/api/buckets/${encodeURIComponent(this.bucketId)}/tree`,
      query,
      options,
    );
  }

  async upload(
    body: ReadableStream<Uint8Array> | Uint8Array | Blob,
    filename: string,
    options?: {
      onProgress?: (progress: UploadProgress) => void;
      uploadToken?: string;
      signal?: AbortSignal;
    },
  ): Promise<UploadResponse> {
    const query: Record<string, string | undefined> = {
      filename,
      token: options?.uploadToken,
    };

    if (options?.onProgress && body instanceof Uint8Array) {
      const totalBytes = body.length;
      const chunkSize = 81920;
      let bytesSent = 0;
      const progress = options.onProgress;
      const source = body;
      const readable = new ReadableStream<Uint8Array>({
        pull(controller) {
          if (bytesSent >= totalBytes) {
            controller.close();
            return;
          }
          const end = Math.min(bytesSent + chunkSize, totalBytes);
          controller.enqueue(source.slice(bytesSent, end));
          bytesSent = end;
          progress({
            bytesSent,
            totalBytes,
            percentage: Math.round((bytesSent / totalBytes) * 100),
          });
        },
      });
      const url = this.transport.buildUrl(
        `/api/buckets/${encodeURIComponent(this.bucketId)}/upload/stream`,
        query,
      );
      const request = new Request(url, {
        method: "PUT",
        body: readable,
        headers: this.transport.authHeaders(),
        signal: options?.signal,
        // @ts-expect-error Node fetch needs duplex for streaming
        duplex: "half",
      });
      const response = await this.transport.sendRaw(request);
      return response.json() as Promise<UploadResponse>;
    }

    return this.transport.put<UploadResponse>(
      `/api/buckets/${encodeURIComponent(this.bucketId)}/upload/stream`,
      body,
      {},
      query,
      { signal: options?.signal },
    );
  }
}

export class FileResource {
  constructor(
    private readonly transport: HttpTransport,
    private readonly bucketId: string,
    private readonly filePath: string,
  ) {}

  private get basePath(): string {
    return `/api/buckets/${encodeURIComponent(this.bucketId)}/files/${encodeURIComponent(this.filePath)}`;
  }

  async getMetadata(options?: RequestOptions): Promise<BucketFile> {
    return this.transport.get<BucketFile>(this.basePath, undefined, options);
  }

  async download(options?: RequestOptions): Promise<Response> {
    return this.transport.getResponse(`${this.basePath}/content`, undefined, options);
  }

  async verify(options?: RequestOptions): Promise<VerifyResponse> {
    return this.transport.get<VerifyResponse>(`${this.basePath}/verify`, undefined, options);
  }

  async delete(options?: RequestOptions): Promise<void> {
    return this.transport.delete(this.basePath, options);
  }

  async patch(
    body: ReadableStream<Uint8Array> | Uint8Array | Blob,
    rangeStart: number,
    rangeEnd: number,
    totalSize: number,
    options?: RequestOptions,
  ): Promise<BucketFile> {
    const url = this.transport.buildUrl(this.basePath);
    const request = new Request(url, {
      method: "PATCH",
      body,
      headers: {
        ...this.transport.authHeaders(),
        "Content-Range": `bytes ${rangeStart}-${rangeEnd}/${totalSize}`,
      },
      signal: options?.signal,
    });
    const response = await this.transport.sendRaw(request);
    return response.json() as Promise<BucketFile>;
  }

  async append(
    body: ReadableStream<Uint8Array> | Uint8Array | Blob,
    options?: RequestOptions,
  ): Promise<BucketFile> {
    const url = this.transport.buildUrl(this.basePath);
    const request = new Request(url, {
      method: "PATCH",
      body,
      headers: {
        ...this.transport.authHeaders(),
        "X-Append": "true",
      },
      signal: options?.signal,
    });
    const response = await this.transport.sendRaw(request);
    return response.json() as Promise<BucketFile>;
  }
}

export type FileOperationsWithIndexer = FileOperations & Record<string, FileResource>;

export function createFileOperations(transport: HttpTransport, bucketId: string): FileOperationsWithIndexer {
  const ops = new FileOperations(transport, bucketId);
  return new Proxy(ops, {
    get(target, prop, receiver) {
      if (typeof prop === "string" && !(prop in target) && prop !== "then" && prop !== "toJSON") {
        return new FileResource(transport, bucketId, prop);
      }
      return Reflect.get(target, prop, receiver);
    },
  }) as FileOperationsWithIndexer;
}
