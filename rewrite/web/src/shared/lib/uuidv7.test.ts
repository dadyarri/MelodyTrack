import { describe, expect, it } from "vitest";
import { uuidv7 } from "./uuidv7";

describe("uuidv7", () => {
  it("sets UUID version and RFC variant bits", () => {
    const timestamp = 1_794_744_000_000;
    const value = uuidv7(timestamp);

    expect(value).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
    expect(BigInt(`0x${value.replaceAll("-", "").slice(0, 12)}`)).toBe(BigInt(timestamp));
  });
});
