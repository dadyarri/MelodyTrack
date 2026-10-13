import { describe, expect, it } from "vitest";

import { getSupportedWebmMimeType, recordCanvasVideo } from "./recordCanvasVideo";

const supportsWebm = getSupportedWebmMimeType() !== null;

function nextVideoFrame(video: HTMLVideoElement) {
  return new Promise<void>((resolve) =>
    video.requestVideoFrameCallback(() => {
      resolve();
    }),
  );
}
describe("canvas video export", () => {
  it.runIf(supportsWebm)("records a painted first frame and changing images", async () => {
    const canvas = document.createElement("canvas");
    canvas.width = 64;
    canvas.height = 64;
    const context = canvas.getContext("2d");
    if (!context) throw new Error("Canvas context is unavailable");
    let frames = 0;
    const { blob } = await recordCanvasVideo({
      canvas,
      durationSeconds: 1,
      drawFrame: () => {
        context.fillStyle = frames++ < 15 ? "#ff0000" : "#00ff00";
        context.fillRect(0, 0, canvas.width, canvas.height);
      },
    });
    const video = document.createElement("video");
    video.muted = true;
    video.playsInline = true;
    const url = URL.createObjectURL(blob);
    video.src = url;
    document.body.appendChild(video);
    const sample = document.createElement("canvas");
    sample.width = 64;
    sample.height = 64;
    const sampleContext = sample.getContext("2d");
    if (!sampleContext) throw new Error("Sample context is unavailable");
    try {
      const firstFrame = nextVideoFrame(video);
      await video.play();
      await firstFrame;
      sampleContext.drawImage(video, 0, 0);
      const first = sampleContext.getImageData(32, 32, 1, 1).data;
      expect(first[0]).toBeGreaterThan(180);
      expect(first[1]).toBeLessThan(80);
      while (video.currentTime < 0.65 && !video.ended) await nextVideoFrame(video);
      sampleContext.drawImage(video, 0, 0);
      const later = sampleContext.getImageData(32, 32, 1, 1).data;
      expect(later[1]).toBeGreaterThan(180);
      expect(later[0]).toBeLessThan(80);
    } finally {
      video.pause();
      video.removeAttribute("src");
      video.load();
      video.remove();
      URL.revokeObjectURL(url);
    }
  });

  it.runIf(!supportsWebm)("explains when this browser cannot record WebM", async () => {
    const canvas = document.createElement("canvas");

    await expect(recordCanvasVideo({ canvas, durationSeconds: 1 })).rejects.toThrow("В этом браузере запись WebM недоступна");
  });
});
