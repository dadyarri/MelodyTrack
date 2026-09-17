import { describe, expect, it } from "vitest";

import { canvasVideoBitrate, estimateVideoBytes } from "./recordCanvasVideo";

describe("estimateVideoBytes", () => {
  it("estimates the encoded size from duration and bitrate", () => {
    expect(estimateVideoBytes(60)).toBe(37_500_000);
  });

  it("accepts a configured bitrate", () => {
    expect(estimateVideoBytes(8, canvasVideoBitrate / 2)).toBe(2_500_000);
  });
});
