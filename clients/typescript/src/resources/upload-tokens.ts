import type { HttpTransport, RequestOptions } from "../transport.js";
import type { CreateUploadTokenRequest, UploadTokenResponse } from "../types.js";

export class UploadTokenOperations {
  constructor(
    private readonly transport: HttpTransport,
    private readonly bucketId: string,
  ) {}

  async create(request: CreateUploadTokenRequest, options?: RequestOptions): Promise<UploadTokenResponse> {
    return this.transport.post<UploadTokenResponse>(
      `/api/buckets/${encodeURIComponent(this.bucketId)}/tokens`,
      request,
      undefined,
      options,
    );
  }
}
