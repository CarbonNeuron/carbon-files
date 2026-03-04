import type { HttpTransport, RequestOptions } from "../transport.js";

export class ShortUrlResource {
  constructor(
    private readonly transport: HttpTransport,
    private readonly code: string,
  ) {}

  async delete(options?: RequestOptions): Promise<void> {
    return this.transport.delete(`/api/short/${encodeURIComponent(this.code)}`, options);
  }
}

export class ShortUrlOperations {
  constructor(private readonly transport: HttpTransport) {}
}

export type ShortUrlOperationsWithIndexer = ShortUrlOperations & Record<string, ShortUrlResource>;

export function createShortUrlOperations(transport: HttpTransport): ShortUrlOperationsWithIndexer {
  const ops = new ShortUrlOperations(transport);
  return new Proxy(ops, {
    get(target, prop, receiver) {
      if (typeof prop === "string" && !(prop in target) && prop !== "then" && prop !== "toJSON") {
        return new ShortUrlResource(transport, prop);
      }
      return Reflect.get(target, prop, receiver);
    },
  }) as ShortUrlOperationsWithIndexer;
}
