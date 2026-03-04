import type { HttpTransport, RequestOptions } from "../transport.js";
import type { StatsResponse } from "../types.js";

export class StatsOperations {
  constructor(private readonly transport: HttpTransport) {}

  async get(options?: RequestOptions): Promise<StatsResponse> {
    return this.transport.get<StatsResponse>("/api/stats", undefined, options);
  }
}
