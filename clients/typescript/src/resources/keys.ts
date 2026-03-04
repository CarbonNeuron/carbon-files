import type { HttpTransport, RequestOptions } from "../transport.js";
import type {
  ApiKeyListItem,
  ApiKeyResponse,
  ApiKeyUsageResponse,
  CreateApiKeyRequest,
  PaginatedResponse,
  PaginationOptions,
} from "../types.js";

export class ApiKeyOperations {
  constructor(private readonly transport: HttpTransport) {}

  async create(request: CreateApiKeyRequest, options?: RequestOptions): Promise<ApiKeyResponse> {
    return this.transport.post<ApiKeyResponse>("/api/keys", request, undefined, options);
  }

  async list(params?: PaginationOptions, options?: RequestOptions): Promise<PaginatedResponse<ApiKeyListItem>> {
    const query: Record<string, string | undefined> = {};
    if (params?.limit !== undefined) query["limit"] = String(params.limit);
    if (params?.offset !== undefined) query["offset"] = String(params.offset);
    if (params?.sort) query["sort"] = params.sort;
    if (params?.order) query["order"] = params.order;
    return this.transport.get<PaginatedResponse<ApiKeyListItem>>("/api/keys", query, options);
  }
}

export class ApiKeyResource {
  constructor(
    private readonly transport: HttpTransport,
    private readonly prefix: string,
  ) {}

  async revoke(options?: RequestOptions): Promise<void> {
    return this.transport.delete(`/api/keys/${encodeURIComponent(this.prefix)}`, options);
  }

  async getUsage(options?: RequestOptions): Promise<ApiKeyUsageResponse> {
    return this.transport.get<ApiKeyUsageResponse>(
      `/api/keys/${encodeURIComponent(this.prefix)}/usage`,
      undefined,
      options,
    );
  }
}

export type ApiKeyOperationsWithIndexer = ApiKeyOperations & Record<string, ApiKeyResource>;

export function createApiKeyOperations(transport: HttpTransport): ApiKeyOperationsWithIndexer {
  const ops = new ApiKeyOperations(transport);
  return new Proxy(ops, {
    get(target, prop, receiver) {
      if (typeof prop === "string" && !(prop in target) && prop !== "then" && prop !== "toJSON") {
        return new ApiKeyResource(transport, prop);
      }
      return Reflect.get(target, prop, receiver);
    },
  }) as ApiKeyOperationsWithIndexer;
}
