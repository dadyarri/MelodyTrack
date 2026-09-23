import { Alert, Button, Card, InputNumber, Popover, Segmented, Space, Switch, Typography } from "antd";
import { useEffect, useRef, useState } from "react";

import { downloadBlob, getSupportedWebmMimeType, recordCanvasVideo, useAudioClock } from "@/shared/lib";

import {
  createDefaultStrummingConfiguration,
  fixedCountInDurationSeconds,
  getBarLengthInQuarterNotes,
  getEmptyStrummingGridSpans,
  getStrummingCountLabels,
  getStrummingDurationSeconds,
  getStrummingEventDurationSeconds,
  type HandMotion,
  handMotions,
  isCompleteStrummingConfiguration,
  rhythmicValues,
  shouldIncreaseTempo,
  strumActions,
  type StrummingBar,
  type StrummingConfiguration,
  type StrummingEvent,
  strummingTimeSignatures,
} from "../model/practiceExercise";
import styles from "./StrummingTrainer.module.css";

type ScheduledTrainerStep = Readonly<{ time: number; activeIndex: number | null; countInBeat: number | null; bpm: number }>;

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
const handMotionLabels: Record<HandMotion, string> = {
  down: "Вниз",
  up: "Вверх",
  muted: "Глушь",
  rest: "Без движения",
  "double-down-up": "Двойной вниз-вверх",
};
const handMotionCodes: Record<HandMotion, string> = { down: "↓", up: "↑", muted: "×", rest: "—", "double-down-up": "⇊" };

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
  const events = configuration.bars.flatMap((bar) => bar.events);
  const labels = configuration.bars.flatMap((bar) => getStrummingCountLabels(bar.events));
  const columns = Math.max(1, Math.min(8, events.length));
  const width = (canvas.width - 180 - (columns - 1) * 20) / columns;
  events.forEach((event, index) => {
    const x = 90 + (index % columns) * (width + 20);
    const y = 260 + Math.floor(index / columns) * 290;
    context.fillStyle = index === activeIndex ? "#f4b860" : "#2d3c59";
    context.fillRect(x, y, width, 240);
    context.fillStyle = index === activeIndex ? "#172033" : "#e8edf7";
    context.font = "700 34px sans-serif";
    context.fillText(labels[index], x + 24, y + 48);
    context.font = "700 86px sans-serif";
    context.fillText(actionCodes[event.action], x + width / 2 - 38, y + 132);
    context.font = "600 28px sans-serif";
    context.fillText(`рука ${handMotionCodes[event.handMotion]}`, x + 24, y + 194);
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

function createRestEvent(): StrummingEvent {
  return { rhythmicValue: "sixteenth", handMotion: "rest", action: "rest", accent: false };
}

export type StrummingTrainerProps = Readonly<{
  configuration?: StrummingConfiguration;
  onConfigurationChange?: (configuration: StrummingConfiguration) => void;
  onPlaybackChange?: (isRunning: boolean) => void;
  showConfiguration?: boolean;
}>;

export function StrummingTrainer({
  configuration: suppliedConfiguration,
  onConfigurationChange,
  onPlaybackChange,
  showConfiguration = true,
}: StrummingTrainerProps) {
  const canRecordVideo = getSupportedWebmMimeType() !== null;
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const { getElapsedSeconds, isRunning, reset, scheduleMetronomeClick, start, stop } = useAudioClock();
  const [uncontrolledConfiguration, setUncontrolledConfiguration] = useState(createDefaultStrummingConfiguration);
  const configuration = suppliedConfiguration ?? uncontrolledConfiguration;
  const [activeConfiguration, setActiveConfiguration] = useState(() => suppliedConfiguration ?? createDefaultStrummingConfiguration());
  const [recordingSeconds, setRecordingSeconds] = useState(() =>
    Math.ceil(getStrummingDurationSeconds(createDefaultStrummingConfiguration())),
  );
  const [isRecording, setIsRecording] = useState(false);
  const [recordingError, setRecordingError] = useState<string | null>(null);
  const displayedConfiguration = isRunning ? activeConfiguration : configuration;
  const isComplete = isCompleteStrummingConfiguration(configuration);
  const canvasHeight = Math.max(720, 580 + Math.ceil(displayedConfiguration.bars.flatMap((bar) => bar.events).length / 8) * 290);
  const applyConfiguration = (nextConfiguration: StrummingConfiguration) => {
    if (isRunning) return;
    if (suppliedConfiguration === undefined) setUncontrolledConfiguration(nextConfiguration);
    onConfigurationChange?.(nextConfiguration);
    setActiveConfiguration(nextConfiguration);
  };
  const updateBar = (barIndex: number, update: (bar: StrummingBar) => StrummingBar) => {
    applyConfiguration({ ...configuration, bars: configuration.bars.map((bar, index) => (index === barIndex ? update(bar) : bar)) });
  };
  const updateEvent = (barIndex: number, eventIndex: number, changes: Partial<StrummingEvent>) => {
    updateBar(barIndex, (bar) => ({
      ...bar,
      events: bar.events.map((event, index) => (index === eventIndex ? { ...event, ...changes } : event)),
    }));
  };

  useEffect(() => onPlaybackChange?.(isRunning), [isRunning, onPlaybackChange]);
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas || !isRunning) {
      if (canvas) drawTrainer(canvas, displayedConfiguration, null);
      return;
    }
    const scheduledSteps: ScheduledTrainerStep[] = [];
    let nextStepTime = getElapsedSeconds() + 0.06,
      remainingCountInBeats = fixedCountInDurationSeconds,
      barIndex = 0,
      eventIndex = 0,
      completedIterations = 0,
      completedBars = 0,
      currentBpm = activeConfiguration.bpm,
      activeIndex = 0;
    let stopAt: number | null = null,
      schedulerId: ReturnType<typeof setTimeout> | null = null,
      animationFrame: number | null = null,
      lastStep: ScheduledTrainerStep | null = null,
      nextMetronomeTime: number | null = null,
      metronomeBeat = 0;
    const beatsPerBar = Number(activeConfiguration.timeSignature.split("/")[0]);
    let metronomeBeatDuration = (getBarLengthInQuarterNotes(activeConfiguration.timeSignature) * 60) / currentBpm / beatsPerBar;
    const schedule = () => {
      const scheduleUntil = getElapsedSeconds() + 0.12;
      while (stopAt === null && nextStepTime < scheduleUntil) {
        if (remainingCountInBeats > 0) {
          scheduledSteps.push({ time: nextStepTime, activeIndex: null, countInBeat: remainingCountInBeats, bpm: currentBpm });
          if (activeConfiguration.metronome.enabled)
            scheduleMetronomeClick({
              time: nextStepTime,
              accented: remainingCountInBeats === fixedCountInDurationSeconds,
              volume: activeConfiguration.metronome.volume,
            });
          remainingCountInBeats -= 1;
          nextStepTime += 1;
          continue;
        }
        nextMetronomeTime ??= nextStepTime;
        while (nextMetronomeTime < scheduleUntil) {
          if (activeConfiguration.metronome.enabled)
            scheduleMetronomeClick({
              time: nextMetronomeTime,
              accented: metronomeBeat === 0,
              volume: activeConfiguration.metronome.volume,
            });
          metronomeBeat = (metronomeBeat + 1) % beatsPerBar;
          nextMetronomeTime += metronomeBeatDuration;
        }
        const event = activeConfiguration.bars[barIndex].events[eventIndex];
        scheduledSteps.push({ time: nextStepTime, activeIndex, countInBeat: null, bpm: currentBpm });
        nextStepTime += getStrummingEventDurationSeconds(event, currentBpm);
        eventIndex += 1;
        activeIndex += 1;
        if (eventIndex !== activeConfiguration.bars[barIndex].events.length) continue;
        eventIndex = 0;
        barIndex += 1;
        completedBars += 1;
        if (
          activeConfiguration.tempoProgression.unit === "bars" &&
          shouldIncreaseTempo(activeConfiguration.tempoProgression, completedBars, completedIterations)
        ) {
          currentBpm = Math.min(currentBpm + activeConfiguration.tempoProgression.increment, 240);
          metronomeBeatDuration = (getBarLengthInQuarterNotes(activeConfiguration.timeSignature) * 60) / currentBpm / beatsPerBar;
        }
        if (barIndex !== activeConfiguration.bars.length) continue;
        barIndex = 0;
        activeIndex = 0;
        completedIterations += 1;
        if (
          activeConfiguration.tempoProgression.unit === "repetitions" &&
          shouldIncreaseTempo(activeConfiguration.tempoProgression, completedBars, completedIterations)
        ) {
          currentBpm = Math.min(currentBpm + activeConfiguration.tempoProgression.increment, 240);
          metronomeBeatDuration = (getBarLengthInQuarterNotes(activeConfiguration.timeSignature) * 60) / currentBpm / beatsPerBar;
        }
        if (!activeConfiguration.loop && completedIterations >= activeConfiguration.repetitions) stopAt = nextStepTime;
      }
      if (stopAt === null) schedulerId = setTimeout(schedule, 25);
    };
    const draw = () => {
      const elapsed = getElapsedSeconds();
      while (scheduledSteps.length > 0 && scheduledSteps[0].time <= elapsed) lastStep = scheduledSteps.shift() ?? null;
      if (lastStep) drawTrainer(canvas, { ...activeConfiguration, bpm: lastStep.bpm }, lastStep.activeIndex, lastStep.countInBeat);
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
  }, [activeConfiguration, displayedConfiguration, getElapsedSeconds, isRunning, scheduleMetronomeClick, stop]);
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
  const renderBar = (bar: StrummingBar, barIndex: number, kind: "stroke" | "motion") => {
    const counts = getStrummingCountLabels(bar.events);
    const emptySlots = Math.ceil(getEmptyStrummingGridSpans({ timeSignature: configuration.timeSignature, ...bar }) / 3);
    return (
      <div className={styles.barGroup} key={`bar-${String(barIndex)}-${kind}`}>
        <Typography.Text className={styles.barLabel}>Такт {barIndex + 1}</Typography.Text>
        <div className={styles.barCells}>
          {bar.events.map((event, eventIndex) =>
            kind === "stroke" ? (
              <div className={styles.rhythmColumn} key={`stroke-${String(eventIndex)}`}>
                <Typography.Text className={styles.countLabel}>{counts[eventIndex]}</Typography.Text>
                <Popover
                  trigger="click"
                  content={
                    <div className={styles.picker}>
                      {strumActions.map((action) => (
                        <Button
                          key={action}
                          disabled={isRunning}
                          type={action === event.action ? "primary" : "default"}
                          title={actionLabels[action]}
                          onClick={() => {
                            updateEvent(barIndex, eventIndex, { action });
                          }}
                        >
                          {actionCodes[action]}
                        </Button>
                      ))}
                    </div>
                  }
                >
                  <Button className={styles.rhythmCell} disabled={isRunning} title={actionLabels[event.action]}>
                    {actionCodes[event.action]}
                  </Button>
                </Popover>
                <Space.Compact className={styles.eventTools}>
                  <Popover
                    trigger="click"
                    content={
                      <div className={styles.picker}>
                        {rhythmicValues.map((rhythmicValue) => (
                          <Button
                            key={rhythmicValue}
                            disabled={isRunning}
                            type={rhythmicValue === event.rhythmicValue ? "primary" : "default"}
                            onClick={() => {
                              updateEvent(barIndex, eventIndex, { rhythmicValue });
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
                      updateEvent(barIndex, eventIndex, { accent: !event.accent });
                    }}
                  >
                    !
                  </Button>
                  <Button
                    size="small"
                    danger
                    disabled={isRunning || bar.events.length === 1}
                    onClick={() => {
                      updateBar(barIndex, (current) => ({ ...current, events: current.events.filter((_, index) => index !== eventIndex) }));
                    }}
                  >
                    ×
                  </Button>
                </Space.Compact>
              </div>
            ) : (
              <div className={styles.motionColumn} key={`motion-${String(eventIndex)}`}>
                <Popover
                  trigger="click"
                  content={
                    <div className={styles.picker}>
                      {handMotions.map((handMotion) => (
                        <Button
                          key={handMotion}
                          disabled={isRunning}
                          type={handMotion === event.handMotion ? "primary" : "default"}
                          title={handMotionLabels[handMotion]}
                          onClick={() => {
                            updateEvent(barIndex, eventIndex, { handMotion });
                          }}
                        >
                          {handMotionCodes[handMotion]}
                        </Button>
                      ))}
                    </div>
                  }
                >
                  <Button className={styles.motionCell} disabled={isRunning} title={handMotionLabels[event.handMotion]}>
                    {handMotionCodes[event.handMotion]}
                  </Button>
                </Popover>
              </div>
            ),
          )}
          {Array.from({ length: emptySlots }, (_, index) => (
            <Button
              className={styles.emptyCell}
              key={`empty-${String(index)}`}
              disabled={isRunning}
              title={kind === "stroke" ? "Добавить шестнадцатую ноту" : "Добавить движение руки без удара"}
              onClick={() => {
                updateBar(barIndex, (current) => ({
                  ...current,
                  events: [...current.events, { ...createRestEvent(), handMotion: kind === "motion" ? "down" : "rest" }],
                }));
              }}
            >
              +
            </Button>
          ))}
        </div>
      </div>
    );
  };
  const setTimeSignature = (timeSignature: StrummingConfiguration["timeSignature"]) => {
    const next = createDefaultStrummingConfiguration(timeSignature);
    applyConfiguration({
      ...next,
      bars: Array.from({ length: configuration.bars.length }, () => next.bars[0]),
      bpm: configuration.bpm,
      repetitions: configuration.repetitions,
      loop: configuration.loop,
      metronome: configuration.metronome,
      tempoProgression: configuration.tempoProgression,
    });
  };
  const setBarCount = (count: number) => {
    const defaultBar = createDefaultStrummingConfiguration(configuration.timeSignature).bars[0];
    applyConfiguration({ ...configuration, bars: Array.from({ length: count }, (_, index) => configuration.bars[index] ?? defaultBar) });
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
                  setTimeSignature(timeSignature);
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
                value={configuration.bars.length}
                disabled={isRunning}
                onChange={(count) => {
                  setBarCount(count ?? 1);
                }}
              />
            </div>
            <div className={styles.control}>
              Повторов
              <InputNumber
                min={1}
                value={configuration.repetitions}
                disabled={isRunning || configuration.loop}
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
            <div className={styles.switchControl}>
              Ускорение
              <Switch
                checked={configuration.tempoProgression.enabled}
                disabled={isRunning}
                onChange={(enabled) => {
                  applyConfiguration({ ...configuration, tempoProgression: { ...configuration.tempoProgression, enabled } });
                }}
              />
            </div>
            {configuration.tempoProgression.enabled ? (
              <div className={styles.progressionControls}>
                <div className={styles.control}>
                  + BPM
                  <InputNumber
                    min={1}
                    max={60}
                    value={configuration.tempoProgression.increment}
                    disabled={isRunning}
                    onChange={(increment) => {
                      applyConfiguration({
                        ...configuration,
                        tempoProgression: { ...configuration.tempoProgression, increment: increment ?? 5 },
                      });
                    }}
                  />
                </div>
                <div className={styles.control}>
                  Каждые
                  <InputNumber
                    min={1}
                    value={configuration.tempoProgression.every}
                    disabled={isRunning}
                    onChange={(every) => {
                      applyConfiguration({ ...configuration, tempoProgression: { ...configuration.tempoProgression, every: every ?? 1 } });
                    }}
                  />
                </div>
                <div className={styles.timeSignatureControl}>
                  Единица
                  <Segmented
                    value={configuration.tempoProgression.unit}
                    options={[
                      { label: "такта", value: "bars" },
                      { label: "повтора", value: "repetitions" },
                    ]}
                    disabled={isRunning}
                    onChange={(unit) => {
                      const tempoProgressionUnit = unit === "bars" ? "bars" : "repetitions";
                      applyConfiguration({
                        ...configuration,
                        tempoProgression: { ...configuration.tempoProgression, unit: tempoProgressionUnit },
                      });
                    }}
                  />
                </div>
              </div>
            ) : null}
          </div>
          <section className={styles.patternSection}>
            <Typography.Title level={2}>Ритмический рисунок</Typography.Title>
            <Typography.Text type="secondary">Удары</Typography.Text>
            <div className={styles.patternTrack}>{configuration.bars.map((bar, index) => renderBar(bar, index, "stroke"))}</div>
          </section>
          <section className={styles.patternSection}>
            <Typography.Text type="secondary">Движение руки — может не совпадать с ударом</Typography.Text>
            <div className={styles.patternTrack}>{configuration.bars.map((bar, index) => renderBar(bar, index, "motion"))}</div>
          </section>
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
        <Alert className={styles.alert} type="warning" title="Заполните каждый такт полностью, прежде чем начать упражнение." />
      ) : null}
      {recordingError ? <Alert className={styles.alert} type="error" title={recordingError} /> : null}
    </Card>
  );
}
