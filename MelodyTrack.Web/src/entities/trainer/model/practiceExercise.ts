export const rhythmicValues = ["whole", "half", "quarter", "eighth", "sixteenth", "eighth-triplet"] as const;

export type RhythmicValue = (typeof rhythmicValues)[number];

/**
 * The reusable, host-independent input to a trainer runtime.
 *
 * Standalone use keeps this value in browser memory. A later homework feature
 * may add durable identity around it without changing the trainer contract.
 */
export type PracticeExerciseDefinition<Type extends string = string, Configuration = unknown> = Readonly<{
  type: Type;
  configuration: Configuration;
}>;

export type PracticeExerciseTimeline = Readonly<{
  iterationDurationSeconds: number;
}>;

export type PracticeTrainerRuntime<Configuration = unknown> = Readonly<{
  configuration: Configuration;
  timeline: PracticeExerciseTimeline;
}>;

export const strumActions = [
  "down",
  "up",
  "down-click",
  "down-dead-notes",
  "up-dead-notes",
  "down-bass-strings",
  "up-bass-strings",
  "down-treble-strings",
  "up-treble-strings",
  "rest",
] as const;

export const handMotions = ["down", "up", "muted", "rest", "double-down-up"] as const;
export const strummingTimeSignatures = ["2/4", "3/4", "4/4", "6/8"] as const;

export type StrumAction = (typeof strumActions)[number];
export type HandMotion = (typeof handMotions)[number];
export type StrummingTimeSignature = (typeof strummingTimeSignatures)[number];

export type StrummingEvent = Readonly<{
  rhythmicValue: RhythmicValue;
  handMotion: HandMotion;
  action: StrumAction;
  accent: boolean;
}>;

/** A distinct bar in the exercise, rather than a repeat of another bar. */
export type StrummingBar = Readonly<{
  events: readonly StrummingEvent[];
}>;

export type MetronomeConfiguration = Readonly<{
  enabled: boolean;
  volume: number;
}>;

export const tempoProgressionUnits = ["bars", "repetitions"] as const;
export type TempoProgressionUnit = (typeof tempoProgressionUnits)[number];

export type TempoProgression = Readonly<{
  enabled: boolean;
  increment: number;
  every: number;
  unit: TempoProgressionUnit;
}>;

export function shouldIncreaseTempo(progression: TempoProgression, completedBars: number, completedRepetitions: number) {
  if (!progression.enabled) return false;
  const completedUnits = progression.unit === "bars" ? completedBars : completedRepetitions;
  return completedUnits > 0 && completedUnits % progression.every === 0;
}

export type StrummingConfiguration = Readonly<{
  bpm: number;
  timeSignature: StrummingTimeSignature;
  bars: readonly StrummingBar[];
  repetitions: number;
  loop: boolean;
  metronome: MetronomeConfiguration;
  tempoProgression: TempoProgression;
}>;

export const fixedCountInDurationSeconds = 5;

const beatLengths: Record<RhythmicValue, number> = {
  whole: 4,
  half: 2,
  quarter: 1,
  eighth: 0.5,
  sixteenth: 0.25,
  "eighth-triplet": 1 / 3,
};

export function getBarLengthInQuarterNotes(timeSignature: StrummingTimeSignature) {
  const [beats, denominator] = timeSignature.split("/").map(Number);
  return beats * (4 / denominator);
}

export function getStrummingDurationSeconds(configuration: StrummingConfiguration) {
  return (getBarLengthInQuarterNotes(configuration.timeSignature) * configuration.bars.length * 60) / configuration.bpm;
}

export function getStrummingEventDurationSeconds(event: StrummingEvent, bpm: number) {
  return (beatLengths[event.rhythmicValue] * 60) / bpm;
}

export function getStrummingCountLabels(events: readonly Pick<StrummingEvent, "rhythmicValue">[]) {
  let position = 0;

  return events.map((event) => {
    const beat = Math.floor(position) + 1;
    const subdivision = Math.round((position - Math.floor(position)) * 12);
    const label =
      subdivision === 0 ? String(beat) : subdivision === 6 ? "и" : subdivision === 4 ? "три" : subdivision === 8 ? "плет" : "та";
    position += beatLengths[event.rhythmicValue];
    return label;
  });
}

export function getEmptyStrummingGridSpans(configuration: Pick<StrummingConfiguration, "timeSignature"> & StrummingBar) {
  const used = configuration.events.reduce((total, event) => total + beatLengths[event.rhythmicValue] * 12, 0);
  const total = getBarLengthInQuarterNotes(configuration.timeSignature) * 12;
  return Math.max(0, total - used);
}

export function getActiveStrummingEventIndex(configuration: StrummingConfiguration, elapsedSeconds: number) {
  const events = configuration.bars.flatMap((bar) => bar.events);
  const elapsedQuarterNotes =
    ((elapsedSeconds * configuration.bpm) / 60) % (getBarLengthInQuarterNotes(configuration.timeSignature) * configuration.bars.length);
  let eventEnd = 0;

  for (const [index, event] of events.entries()) {
    eventEnd += beatLengths[event.rhythmicValue];
    if (elapsedQuarterNotes < eventEnd) return index;
  }

  return Math.max(events.length - 1, 0);
}

export function isCompleteStrummingBar(configuration: Pick<StrummingConfiguration, "timeSignature"> & StrummingBar) {
  const configuredLength = configuration.events.reduce((total, event) => total + beatLengths[event.rhythmicValue], 0);
  return Math.abs(configuredLength - getBarLengthInQuarterNotes(configuration.timeSignature)) < 0.000_001;
}

export function isCompleteStrummingConfiguration(configuration: StrummingConfiguration) {
  return (
    configuration.bars.length > 0 &&
    configuration.bars.every((bar) => isCompleteStrummingBar({ timeSignature: configuration.timeSignature, ...bar }))
  );
}

export function createDefaultStrummingConfiguration(timeSignature: StrummingTimeSignature = "4/4"): StrummingConfiguration {
  const eventCount = timeSignature === "6/8" ? 6 : Number(timeSignature[0]);
  const rhythmicValue: RhythmicValue = timeSignature === "6/8" ? "eighth" : "quarter";

  return {
    bpm: 80,
    timeSignature,
    bars: [
      {
        events: Array.from({ length: eventCount }, (_, index) => ({
          rhythmicValue,
          handMotion: index % 2 === 0 ? "down" : "up",
          action: index % 2 === 0 ? "down" : "up",
          accent: false,
        })),
      },
    ],
    repetitions: 4,
    loop: true,
    metronome: { enabled: true, volume: 0.6 },
    tempoProgression: { enabled: false, increment: 5, every: 4, unit: "repetitions" },
  };
}
