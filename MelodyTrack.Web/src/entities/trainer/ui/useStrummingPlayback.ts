import { useCallback, useEffect, useRef, useState } from "react";

import { getSupportedWebmMimeType, maximumCanvasVideoSeconds, recordCanvasVideo } from "@/shared/lib";
import { useTrainerTiming } from "./TrainerTimingHost";
import { drawStrummingTrainer, type TrainerPosition } from "./drawStrummingTrainer";
import { getCountInBeats, getCountInDurationSeconds, isCompleteStrummingConfiguration, type StrummingConfiguration } from "../model/practiceExercise";

const startDelaySeconds = 0.06;
export function useStrummingPlayback(configuration: StrummingConfiguration, onPlaybackChange?: (running: boolean) => void, previewBar = 0) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const onTimingReleaseRef = useRef<() => void>(() => {});
  const presentationRef = useRef<{ configuration: StrummingConfiguration; position: TrainerPosition }>({ configuration, position: { bar: 0, slot: null, countIn: null, repetition: 1, bpm: configuration.bpm } });
  const drawFrame = useCallback(() => {
    const canvas = canvasRef.current;
    if (canvas) drawStrummingTrainer(canvas, presentationRef.current.configuration, presentationRef.current.position);
  }, []);
  const { getAudioStream, isRunning, position, start, stop } = useTrainerTiming(() => onTimingReleaseRef.current(), next => {
    presentationRef.current.position = next;
    drawFrame();
  });
  const [activeConfiguration, setActiveConfiguration] = useState(configuration);
  const [isStarting, setIsStarting] = useState(false);
  const [isRecording, setIsRecording] = useState(false);
  const [video, setVideo] = useState<Blob | null>(null);
  const [error, setError] = useState<string | null>(null);
  const generationRef = useRef(0);
  const recordingRef = useRef<AbortController | null>(null);
  const busyRef = useRef(false);
  const mountedRef = useRef(true);
  const isBusy = isStarting || isRunning || isRecording;
  const stopPlayback = useCallback(() => {
    generationRef.current += 1;
    busyRef.current = false;
    stop();
    setIsStarting(false);
    recordingRef.current?.abort("finish");
  }, [stop]);
  useEffect(() => { onTimingReleaseRef.current = stopPlayback; }, [stopPlayback]);
  useEffect(() => { onPlaybackChange?.(isBusy); }, [isBusy, onPlaybackChange]);
  useEffect(() => () => { onPlaybackChange?.(false); }, [onPlaybackChange]);
  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
      generationRef.current += 1;
      busyRef.current = false;
      recordingRef.current?.abort();
    };
  }, []);
  useEffect(() => {
    const onVisibilityChange = () => {
      if (document.hidden && busyRef.current) { stopPlayback(); setError("Проигрывание остановлено: вкладка скрыта. Вернитесь и нажмите «Старт»."); }
    };
    document.addEventListener("visibilitychange", onVisibilityChange);
    return () => document.removeEventListener("visibilitychange", onVisibilityChange);
  }, [stopPlayback]);
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    if (isRunning) return;
    drawStrummingTrainer(canvas, configuration, { bar: previewBar, slot: null, countIn: null, repetition: 1, bpm: configuration.bpm });
  }, [configuration, isRunning, previewBar]);
  useEffect(() => {
    const canvas = canvasRef.current;
    if (canvas && isRunning && position) drawStrummingTrainer(canvas, activeConfiguration, position);
  }, [activeConfiguration, isRunning, position]);
  const startPlayback = (recordingSeconds?: number, recordWholeExercise = false) => {
    if (busyRef.current || isRecording || !isCompleteStrummingConfiguration(configuration)) return;
    busyRef.current = true;
    const generation = ++generationRef.current;
    setError(null);
    setIsStarting(true);
    const sessionConfiguration = recordWholeExercise ? { ...configuration, loop: false } : configuration;
    setActiveConfiguration(sessionConfiguration);
    presentationRef.current = { configuration: sessionConfiguration, position: { bar: null, slot: null, countIn: 1, repetition: 1, bpm: sessionConfiguration.bpm } };
    drawFrame();
    let audioStream: MediaStream | null = null;
    let starting: Promise<boolean>;
    try {
      if (recordingSeconds !== undefined) audioStream = getAudioStream();
      starting = start({
        bpm: sessionConfiguration.bpm,
        beatsPerBar: getCountInBeats(sessionConfiguration.timeSignature),
        quarterNotesPerBeat: 4 / Number(sessionConfiguration.timeSignature.split("/")[1]),
        slotsPerBar: sessionConfiguration.bars.map(bar => bar.strokes.length),
        repetitions: sessionConfiguration.repetitions,
        loop: sessionConfiguration.loop,
        metronome: sessionConfiguration.metronome,
        tempoProgression: sessionConfiguration.tempoProgression,
      });
    } catch {
      busyRef.current = false;
      setIsStarting(false);
      setError("Не удалось включить звук. Проверьте настройки браузера и попробуйте снова.");
      return;
    }
    void starting.then((started) => {
      if (generation !== generationRef.current) return;
      setIsStarting(false);
      if (!started) { busyRef.current = false; setError("Не удалось включить звук. Нажмите «Старт», чтобы попробовать снова."); return; }
      if (recordingSeconds === undefined) return;
      const canvas = canvasRef.current;
      if (!canvas) { stopPlayback(); return; }
      const controller = new AbortController();
      recordingRef.current = controller;
      setIsRecording(true);
      setVideo(null);
      const durationSeconds = recordWholeExercise ? maximumCanvasVideoSeconds : Math.min(maximumCanvasVideoSeconds, startDelaySeconds + getCountInDurationSeconds(configuration.timeSignature, configuration.bpm) + recordingSeconds);
      void recordCanvasVideo({ canvas, audioStream, durationSeconds, signal: controller.signal, drawFrame })
        .then(({ blob }) => { if (mountedRef.current && (generation === generationRef.current || controller.signal.reason === "finish")) setVideo(blob); })
        .catch((reason: unknown) => {
          if (mountedRef.current && !controller.signal.aborted) setError(reason instanceof Error ? reason.message : "Не удалось записать видео.");
        })
        .finally(() => {
          if (!mountedRef.current || recordingRef.current !== controller) return;
          recordingRef.current = null;
          if (controller.signal.aborted && controller.signal.reason !== "finish") return;
          stop();
          busyRef.current = false;
          setIsRecording(false);
        });
    }).catch(() => {
      if (generation !== generationRef.current) return;
      stopPlayback();
      setError("Не удалось включить звук. Проверьте настройки браузера и попробуйте снова.");
    });
  };
  return { activeConfiguration, canvasRef, isBusy, isRecording, isStarting, isRunning, position, video, error, startPlayback, stopPlayback, canRecordVideo: getSupportedWebmMimeType() !== null };
}
