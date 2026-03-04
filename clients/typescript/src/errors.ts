export class CarbonFilesError extends Error {
  constructor(
    public readonly status: number,
    public readonly error: string,
    public readonly hint?: string,
  ) {
    super(error);
    this.name = "CarbonFilesError";
  }
}
