import type { HttpTransport, RequestOptions } from "../transport.js";
import type { CreateDashboardTokenRequest, DashboardTokenInfo, DashboardTokenResponse } from "../types.js";

export class DashboardOperations {
  constructor(private readonly transport: HttpTransport) {}

  async createToken(request?: CreateDashboardTokenRequest, options?: RequestOptions): Promise<DashboardTokenResponse> {
    return this.transport.post<DashboardTokenResponse>("/api/tokens/dashboard", request ?? {}, undefined, options);
  }

  async getCurrentUser(options?: RequestOptions): Promise<DashboardTokenInfo> {
    return this.transport.get<DashboardTokenInfo>("/api/tokens/dashboard/me", undefined, options);
  }
}
