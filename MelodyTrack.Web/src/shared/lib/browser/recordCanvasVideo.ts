const webmMimeTypes = ["video/webm;codecs=vp9", "video/webm"] as const;

export const canvasVideoFrameRate = 30;
export const canvasVideoBitrate = 5_000_000;

export type CanvasVideoRecording = Readonly<{
  blob: Blob;
  mimeType: string;
}>;

export function getSupportedWebmMimeType() {
  if (typeof MediaRecorder === "undefined") {
    return null;
  }

  return webmMimeTypes.find((mimeType) => MediaRecorder.isTypeSupported(mimeType)) ?? null;
}

export function estimateVideoBytes(durationSeconds: number, bitrate = canvasVideoBitrate) {
  return Math.ceil((durationSeconds * bitrate) / 8);
}

export function recordCanvasVideo({
  canvas,
  durationSeconds,
  onChunk,
}: {
  canvas: HTMLCanvasElement;
  durationSeconds: number;
  onChunk?: (totalBytes: number) => void;
}): Promise<CanvasVideoRecording> {
  if (!Number.isFinite(durationSeconds) || durationSeconds <= 0) {
    return Promise.reject(new Error("The recording duration must be a positive number."));
  }

  const mimeType = getSupportedWebmMimeType();
  if (!mimeType) {
    return Promise.reject(new Error("This browser cannot create a WebM recording."));
  }
  if (typeof canvas.captureStream !== "function") {
    return Promise.reject(new Error("This browser cannot capture the trainer presentation."));
  }

  const stream = canvas.captureStream(canvasVideoFrameRate);
  const recorder = new MediaRecorder(stream, {
    mimeType,
    videoBitsPerSecond: canvasVideoBitrate,
  });
  const chunks: Blob[] = [];
  let totalBytes = 0;

  return new Promise((resolve, reject) => {
    let stopTimer: number | undefined;
    let completed = false;

    const cleanUp = () => {
      if (stopTimer !== undefined) {
        window.clearTimeout(stopTimer);
      }
      stream.getTracks().forEach((track) => {
        track.stop();
      });
    };

    recorder.ondataavailable = (event) => {
      if (!event.data.size) {
        return;
      }

      chunks.push(event.data);
      totalBytes += event.data.size;
      onChunk?.(totalBytes);
    };

    recorder.onerror = () => {
      if (completed) {
        return;
      }

      completed = true;
      cleanUp();
      reject(new Error("The browser could not complete the video recording."));
    };

    recorder.onstop = () => {
      if (completed) {
        return;
      }

      completed = true;
      cleanUp();
      resolve({ blob: new Blob(chunks, { type: mimeType }), mimeType });
    };

    const scheduleStop = (remainingMilliseconds: number) => {
      const delay = Math.min(remainingMilliseconds, 2_147_483_647);
      stopTimer = window.setTimeout(() => {
        if (remainingMilliseconds > delay) {
          scheduleStop(remainingMilliseconds - delay);
          return;
        }

        if (recorder.state === "recording") {
          recorder.stop();
        }
      }, delay);
    };

    recorder.start(1_000);
    scheduleStop(durationSeconds * 1_000);
  });
}
