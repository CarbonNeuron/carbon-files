import { describe, it, expect } from "vitest";
import { CarbonFilesError } from "../src/errors.js";

describe("CarbonFilesError", () => {
  it("stores status, error, and hint", () => {
    const err = new CarbonFilesError(404, "Not found", "Check the bucket ID");
    expect(err.status).toBe(404);
    expect(err.error).toBe("Not found");
    expect(err.hint).toBe("Check the bucket ID");
    expect(err.message).toBe("Not found");
    expect(err).toBeInstanceOf(Error);
    expect(err.name).toBe("CarbonFilesError");
  });

  it("hint is undefined when not provided", () => {
    const err = new CarbonFilesError(500, "Internal error");
    expect(err.hint).toBeUndefined();
  });
});
