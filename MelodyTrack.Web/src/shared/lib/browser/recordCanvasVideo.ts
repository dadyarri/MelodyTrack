const webmMimeTypes = ["video/webm;codecs=vp9", "video/webm"] as const;
export const canvasVideoFrameRate = 30;
export const canvasVideoBitrate = 5_000_000;
export const maximumCanvasVideoSeconds = 180;
const maximumCanvasVideoBytes = 128 * 1024 * 1024;
export type CanvasVideoRecording = Readonly<{ blob: Blob; mimeType: string }>;
export function getSupportedWebmMimeType() {
  if (typeof MediaRecorder === "undefined" || typeof HTMLCanvasElement === "undefined" || typeof HTMLCanvasElement.prototype.captureStream !== "function") return null;
  return webmMimeTypes.find((mimeType) => MediaRecorder.isTypeSupported(mimeType)) ?? null;
}
export function estimateVideoBytes(durationSeconds: number, bitrate = canvasVideoBitrate) { return Math.ceil(durationSeconds * bitrate / 8); }
/** Abort with reason "finish" to keep the partial video; any other abort discards it. */
export function recordCanvasVideo({ canvas, durationSeconds, audioStream, signal, onChunk, drawFrame }: {
  canvas: HTMLCanvasElement;
  durationSeconds: number;
  audioStream?: MediaStream | null;
  signal?: AbortSignal;
  onChunk?: (totalBytes: number) => void;
  drawFrame?: () => void;
}): Promise<CanvasVideoRecording> {
  if (!Number.isFinite(durationSeconds) || durationSeconds <= 0 || durationSeconds > maximumCanvasVideoSeconds) return Promise.reject(new Error("Длительность записи должна быть от 1 до 180 секунд."));
  const mimeType = getSupportedWebmMimeType();
  if (!mimeType) return Promise.reject(new Error("В этом браузере запись WebM недоступна. Попробуйте другой браузер."));
  if (signal?.aborted) return Promise.reject(new DOMException("Запись отменена.", "AbortError"));
  return new Promise((resolve, reject) => {
    let canvasStream: MediaStream | null = null;
    let recorder: MediaRecorder | null = null;
    let stopTimer: number | undefined;
    let frame: number | undefined;
    let completed = false;
    let failure: Error | null = null;
    const chunks: Blob[] = [];
    let totalBytes = 0;
    const cleanUp = () => {
      if (stopTimer !== undefined) window.clearTimeout(stopTimer);
      if (frame !== undefined) cancelAnimationFrame(frame);
      signal?.removeEventListener("abort", onAbort);
      canvasStream?.getTracks().forEach((track) => track.stop());
    };
    const fail = (error: Error) => {
      if (completed) return;
      failure = error;
      if (recorder && recorder.state !== "inactive") recorder.stop();
      else { completed = true; cleanUp(); reject(error); }
    };
    const onAbort = () => {
      if (signal?.reason === "finish") { if (recorder?.state === "recording") recorder.stop(); }
      else fail(new DOMException("Запись отменена.", "AbortError"));
    };
    try {
      drawFrame?.();
      canvasStream = canvas.captureStream(canvasVideoFrameRate);
      const stream = new MediaStream([...canvasStream.getVideoTracks(), ...(audioStream?.getAudioTracks() ?? [])]);
      recorder = new MediaRecorder(stream, { mimeType, videoBitsPerSecond: canvasVideoBitrate });
      recorder.ondataavailable = (event) => {
        if (!event.data.size || failure) return;
        totalBytes += event.data.size;
        if (totalBytes > maximumCanvasVideoBytes) { fail(new Error("Видео получилось слишком большим. Уменьшите длительность записи.")); return; }
        chunks.push(event.data);
        onChunk?.(totalBytes);
      };
      recorder.onerror = () => fail(new Error("Браузер не смог завершить запись. Попробуйте записать более короткое видео."));
      recorder.onstop = () => {
        if (completed) return;
        completed = true;
        cleanUp();
        if (failure) reject(failure);
        else resolve({ blob: new Blob(chunks, { type: mimeType }), mimeType });
      };
      signal?.addEventListener("abort", onAbort, { once: true });
      recorder.start(1_000);
      const videoTrack = canvasStream.getVideoTracks()[0] as CanvasCaptureMediaStreamTrack | undefined;
      const captureFrame = () => {
        if (completed || recorder?.state !== "recording") return;
        drawFrame?.();
        if (typeof videoTrack?.requestFrame === "function") videoTrack.requestFrame();
        frame = requestAnimationFrame(captureFrame);
      };
      captureFrame();
      stopTimer = window.setTimeout(() => { if (recorder?.state === "recording") recorder.stop(); }, durationSeconds * 1_000);
    } catch {
      completed = true;
      cleanUp();
      reject(new Error("Не удалось начать запись видео. Попробуйте другой браузер."));
    }
  });
}
