import type { HttpTransport, RequestOptions } from "../transport.js";
import type { HealthResponse } from "../types.js";

export class HealthOperations {
  constructor(private readonly transport: HttpTransport) {}

  async check(options?: RequestOptions): Promise<HealthResponse> {
    return this.transport.get<HealthResponse>("/healthz", undefined, options);
  }
}
