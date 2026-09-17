import { Alert, Button, Card, InputNumber, Popover, Segmented, Space, Switch, Typography } from "antd";
import { useEffect, useRef, useState } from "react";

import { downloadBlob, getSupportedWebmMimeType, recordCanvasVideo, useAudioClock } from "@/shared/lib";

import {
  createDefaultStrummingConfiguration,
  fixedCountInDurationSeconds,
  getStrummingDurationSeconds,
  getStrummingEventDurationSeconds,
  isCompleteStrummingBar,
  rhythmicValues,
  strumActions,
  type StrummingConfiguration,
  strummingTimeSignatures,
} from "../model/practiceExercise";
import styles from "./StrummingTrainer.module.css";

type ScheduledTrainerStep = Readonly<{
  time: number;
  activeIndex: number | null;
  countInBeat: number | null;
}>;

const actionLabels = {
  down: "Вниз",
  up: "Вверх",
  "down-click": "Вниз с щелчком",
  "down-dead-notes": "Вниз по заглушенным",
  "up-dead-notes": "Вверх по заглушенным",
  "down-bass-strings": "Вниз по басовым",
  "up-bass-strings": "Вверх по басовым",
  "down-treble-strings": "Вниз по тонким",
  "up-treble-strings": "Вверх по тонким",
  rest: "Пауза",
} as const;

const rhythmicValueLabels = {
  whole: "1",
  half: "1/2",
  quarter: "1/4",
  eighth: "1/8",
  sixteenth: "1/16",
  "eighth-triplet": "1/8T",
} as const;

const actionCodes = {
  down: "↓",
  up: "↑",
  "down-click": "↓•",
  "down-dead-notes": "↓×",
  "up-dead-notes": "↑×",
  "down-bass-strings": "↓B",
  "up-bass-strings": "↑B",
  "down-treble-strings": "↓T",
  "up-treble-strings": "↑T",
  rest: "—",
} as const;

function drawTrainer(
  canvas: HTMLCanvasElement,
  configuration: StrummingConfiguration,
  activeIndex: number | null,
  countInBeat: number | null = null,
) {
  const context = canvas.getContext("2d");
  if (!context) return;

  context.fillStyle = "#172033";
  context.fillRect(0, 0, canvas.width, canvas.height);
  context.fillStyle = "#e8edf7";
  context.font = "700 64px sans-serif";
  context.fillText("Гитарный бой", 90, 120);
  context.font = "40px sans-serif";
  context.fillStyle = "#b9c7dd";
  context.fillText(`${configuration.timeSignature} · ${String(configuration.bpm)} BPM`, 90, 184);

  const gap = 24;
  const itemsPerRow = 4;
  const width = (canvas.width - 180 - gap * (itemsPerRow - 1)) / itemsPerRow;
  configuration.events.forEach((event, index) => {
    const column = index % itemsPerRow;
    const row = Math.floor(index / itemsPerRow);
    const x = 90 + column * (width + gap);
    const y = 290 + row * 380;
    context.fillStyle = index === activeIndex ? "#f4b860" : "#2d3c59";
    context.fillRect(x, y, width, 330);
    context.fillStyle = index === activeIndex ? "#172033" : "#e8edf7";
    context.font = "700 92px sans-serif";
    context.fillText(event.action === "rest" ? "—" : event.action.startsWith("down") ? "↓" : "↑", x + width / 2 - 28, y + 140);
    context.font = "600 34px sans-serif";
    context.fillText(event.handMotion === "down" ? "рука ↓" : "рука ↑", x + 30, y + 235);
    context.font = "28px sans-serif";
    context.fillText(rhythmicValueLabels[event.rhythmicValue], x + 30, y + 285);
  });

  if (countInBeat !== null) {
    context.fillStyle = "rgba(0, 0, 0, 0.48)";
    context.fillRect(0, 0, canvas.width, canvas.height);
    context.fillStyle = "#ffffff";
    context.font = "700 280px sans-serif";
    context.textAlign = "center";
    context.fillText(String(countInBeat), canvas.width / 2, canvas.height / 2 + 100);
    context.textAlign = "start";
  }
}

export type StrummingTrainerProps = Readonly<{
  configuration?: StrummingConfiguration;
  onConfigurationChange?: (configuration: StrummingConfiguration) => void;
  showConfiguration?: boolean;
}>;

export function StrummingTrainer({
  configuration: suppliedConfiguration,
  onConfigurationChange,
  showConfiguration = true,
}: StrummingTrainerProps) {
  const canRecordVideo = getSupportedWebmMimeType() !== null;
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const { getElapsedSeconds, isRunning, reset, start, stop } = useAudioClock();
  const [uncontrolledConfiguration, setUncontrolledConfiguration] = useState(createDefaultStrummingConfiguration);
  const configuration = suppliedConfiguration ?? uncontrolledConfiguration;
  const [activeConfiguration, setActiveConfiguration] = useState(() => suppliedConfiguration ?? createDefaultStrummingConfiguration());
  const [recordingSeconds, setRecordingSeconds] = useState(() =>
    Math.ceil(getStrummingDurationSeconds(createDefaultStrummingConfiguration())),
  );
  const [isRecording, setIsRecording] = useState(false);
  const [recordingError, setRecordingError] = useState<string | null>(null);
  const displayedConfiguration = isRunning ? activeConfiguration : configuration;
  const canvasHeight = Math.max(1080, 290 + Math.ceil(displayedConfiguration.events.length / 4) * 380 + 90);
  const isComplete = isCompleteStrummingBar(configuration);

  const applyConfiguration = (nextConfiguration: StrummingConfiguration) => {
    if (isRunning) return;

    if (suppliedConfiguration === undefined) setUncontrolledConfiguration(nextConfiguration);
    onConfigurationChange?.(nextConfiguration);
    setActiveConfiguration(nextConfiguration);
  };

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas || !isRunning) {
      if (canvas) drawTrainer(canvas, displayedConfiguration, null);
      return;
    }

    const scheduledSteps: ScheduledTrainerStep[] = [];
    // Give the scheduler a small lead, as Web Audio schedulers do, so the
    // first count-in beat is never shortened by React's effect timing.
    let nextStepTime = getElapsedSeconds() + 0.06;
    let remainingCountInBeats = fixedCountInDurationSeconds;
    let eventIndex = 0;
    let barIndex = 0;
    let completedIterations = 0;
    let stopAt: number | null = null;
    let schedulerId: ReturnType<typeof setTimeout> | null = null;
    let animationFrame: number | null = null;
    let lastStep: ScheduledTrainerStep | null = null;

    const schedule = () => {
      const scheduleUntil = getElapsedSeconds() + 0.12;

      while (stopAt === null && nextStepTime < scheduleUntil) {
        if (remainingCountInBeats > 0) {
          scheduledSteps.push({ time: nextStepTime, activeIndex: null, countInBeat: remainingCountInBeats });
          remainingCountInBeats -= 1;
          nextStepTime += 1;
          continue;
        }

        scheduledSteps.push({ time: nextStepTime, activeIndex: eventIndex, countInBeat: null });
        nextStepTime += getStrummingEventDurationSeconds(activeConfiguration.events[eventIndex], activeConfiguration.bpm);
        eventIndex += 1;

        if (eventIndex !== activeConfiguration.events.length) continue;

        eventIndex = 0;
        barIndex += 1;
        if (barIndex !== activeConfiguration.bars) continue;

        barIndex = 0;
        completedIterations += 1;
        if (!activeConfiguration.loop && completedIterations >= activeConfiguration.repetitions) stopAt = nextStepTime;
      }

      if (stopAt === null) schedulerId = setTimeout(schedule, 25);
    };

    const draw = () => {
      const elapsed = getElapsedSeconds();
      while (scheduledSteps.length > 0 && scheduledSteps[0].time <= elapsed) lastStep = scheduledSteps.shift() ?? null;

      if (lastStep !== null) drawTrainer(canvas, activeConfiguration, lastStep.activeIndex, lastStep.countInBeat);
      if (stopAt !== null && elapsed >= stopAt) {
        stop();
        return;
      }

      animationFrame = requestAnimationFrame(draw);
    };

    schedule();
    animationFrame = requestAnimationFrame(draw);

    return () => {
      if (schedulerId !== null) clearTimeout(schedulerId);
      if (animationFrame !== null) cancelAnimationFrame(animationFrame);
    };
  }, [activeConfiguration, displayedConfiguration, getElapsedSeconds, isRunning, stop]);

  const updateEvent = (index: number, changes: Partial<StrummingConfiguration["events"][number]>) => {
    applyConfiguration({
      ...configuration,
      events: configuration.events.map((event, eventIndex) => (eventIndex === index ? { ...event, ...changes } : event)),
    });
  };

  const startTrainer = () => {
    if (!isComplete) return;
    reset();
    setActiveConfiguration(configuration);
    start();
  };

  const recordTrainer = () => {
    const canvas = canvasRef.current;
    if (!canvas || !isComplete || isRecording) return;

    setRecordingError(null);
    setIsRecording(true);
    startTrainer();
    void recordCanvasVideo({ canvas, durationSeconds: fixedCountInDurationSeconds + recordingSeconds })
      .then(({ blob }) => {
        downloadBlob(blob, "melodytrack-guitar-strumming.webm");
      })
      .catch((error: unknown) => {
        setRecordingError(error instanceof Error ? error.message : "Не удалось записать видео.");
      })
      .finally(() => {
        stop();
        setIsRecording(false);
      });
  };

  return (
    <Card className={styles.trainerCard}>
      <canvas ref={canvasRef} width="1920" height={canvasHeight} className={styles.trainerCanvas} aria-label="Тренажёр гитарного боя" />
      {showConfiguration ? (
        <>
          <div className={styles.controls}>
            <div className={styles.timeSignatureControl}>
              Размер
              <Segmented
                value={configuration.timeSignature}
                options={[...strummingTimeSignatures]}
                disabled={isRunning}
                onChange={(timeSignature) => {
                  applyConfiguration({
                    ...configuration,
                    timeSignature: timeSignature,
                    events: createDefaultStrummingConfiguration(timeSignature).events,
                  });
                }}
              />
            </div>
            <div className={styles.control}>
              BPM
              <InputNumber
                min={30}
                max={240}
                value={configuration.bpm}
                disabled={isRunning}
                onChange={(bpm) => {
                  applyConfiguration({ ...configuration, bpm: bpm ?? 80 });
                }}
              />
            </div>
            <div className={styles.control}>
              Тактов
              <InputNumber
                min={1}
                value={configuration.bars}
                disabled={isRunning}
                onChange={(bars) => {
                  applyConfiguration({ ...configuration, bars: bars ?? 1 });
                }}
              />
            </div>
            <div className={styles.control}>
              Повторов
              <InputNumber
                min={1}
                value={configuration.repetitions}
                disabled={isRunning}
                onChange={(repetitions) => {
                  applyConfiguration({ ...configuration, repetitions: repetitions ?? 1 });
                }}
              />
            </div>
            <div className={styles.switchControl}>
              Цикл
              <Switch
                checked={configuration.loop}
                disabled={isRunning}
                onChange={(loop) => {
                  applyConfiguration({ ...configuration, loop });
                }}
              />
            </div>
          </div>
          <Typography.Title level={2}>Рисунок боя</Typography.Title>
          <div className={styles.eventGrid}>
            {configuration.events.map((event, index) => (
              <div className={styles.eventEditor} key={`${event.action}-${String(index)}`}>
                <span className={styles.eventNumber}>{index + 1}</span>
                <Popover
                  trigger="click"
                  content={
                    <div className={styles.actionPicker}>
                      {strumActions.map((action) => (
                        <Button
                          key={action}
                          disabled={isRunning}
                          type={action === event.action ? "primary" : "default"}
                          title={actionLabels[action]}
                          onClick={() => {
                            updateEvent(index, { action });
                          }}
                        >
                          {actionCodes[action]}
                        </Button>
                      ))}
                    </div>
                  }
                >
                  <Button className={styles.strokeButton} disabled={isRunning} title="Выбрать действие">
                    {actionCodes[event.action]}
                  </Button>
                </Popover>
                <div className={styles.eventActions}>
                  <Button
                    size="small"
                    disabled={isRunning}
                    onClick={() => {
                      updateEvent(index, { handMotion: event.handMotion === "down" ? "up" : "down" });
                    }}
                  >
                    Рука {event.handMotion === "down" ? "↓" : "↑"}
                  </Button>
                  <Popover
                    trigger="click"
                    content={
                      <div className={styles.valuePicker}>
                        {rhythmicValues.map((rhythmicValue) => (
                          <Button
                            key={rhythmicValue}
                            disabled={isRunning}
                            type={rhythmicValue === event.rhythmicValue ? "primary" : "default"}
                            onClick={() => {
                              updateEvent(index, { rhythmicValue });
                            }}
                          >
                            {rhythmicValueLabels[rhythmicValue]}
                          </Button>
                        ))}
                      </div>
                    }
                  >
                    <Button size="small" disabled={isRunning}>
                      {rhythmicValueLabels[event.rhythmicValue]}
                    </Button>
                  </Popover>
                  <Button
                    size="small"
                    type={event.accent ? "primary" : "default"}
                    disabled={isRunning || event.action === "rest"}
                    onClick={() => {
                      updateEvent(index, { accent: !event.accent });
                    }}
                  >
                    Акцент
                  </Button>
                </div>
                <Button
                  type="text"
                  danger
                  size="small"
                  disabled={isRunning || configuration.events.length === 1}
                  onClick={() => {
                    applyConfiguration({ ...configuration, events: configuration.events.filter((_, eventIndex) => eventIndex !== index) });
                  }}
                >
                  Убрать
                </Button>
              </div>
            ))}
          </div>
          <Button
            disabled={isRunning}
            onClick={() => {
              applyConfiguration({
                ...configuration,
                events: [...configuration.events, { rhythmicValue: "quarter", handMotion: "down", action: "down", accent: false }],
              });
            }}
          >
            Добавить позицию
          </Button>
        </>
      ) : null}
      <Space wrap>
        <Button type="primary" disabled={!isComplete || isRunning} onClick={startTrainer}>
          Старт
        </Button>
        <Button onClick={stop} disabled={!isRunning}>
          Стоп
        </Button>
        <Space.Compact>
          <InputNumber
            min={1}
            value={recordingSeconds}
            onChange={(seconds) => {
              setRecordingSeconds(seconds ?? Math.ceil(getStrummingDurationSeconds(configuration)));
            }}
          />
          <Typography.Text>сек.</Typography.Text>
        </Space.Compact>
        <Button disabled={!canRecordVideo || !isComplete || isRecording || isRunning} onClick={recordTrainer}>
          {isRecording ? "Запись…" : "Записать WebM"}
        </Button>
      </Space>
      {!isComplete ? (
        <Alert className={styles.alert} type="warning" title="Заполните такт полностью, прежде чем начать упражнение." />
      ) : null}
      {recordingError ? <Alert className={styles.alert} type="error" title={recordingError} /> : null}
    </Card>
  );
}
