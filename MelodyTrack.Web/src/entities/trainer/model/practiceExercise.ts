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

export const handMotions = ["down", "up"] as const;
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

export type StrummingConfiguration = Readonly<{
  bpm: number;
  timeSignature: StrummingTimeSignature;
  bars: number;
  repetitions: number;
  loop: boolean;
  events: readonly StrummingEvent[];
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
  return (getBarLengthInQuarterNotes(configuration.timeSignature) * configuration.bars * 60) / configuration.bpm;
}

export function getStrummingEventDurationSeconds(event: StrummingEvent, bpm: number) {
  return (beatLengths[event.rhythmicValue] * 60) / bpm;
}

export function getActiveStrummingEventIndex(configuration: StrummingConfiguration, elapsedSeconds: number) {
  const elapsedQuarterNotes = ((elapsedSeconds * configuration.bpm) / 60) % getBarLengthInQuarterNotes(configuration.timeSignature);
  let eventEnd = 0;

  for (const [index, event] of configuration.events.entries()) {
    eventEnd += beatLengths[event.rhythmicValue];
    if (elapsedQuarterNotes < eventEnd) return index;
  }

  return Math.max(configuration.events.length - 1, 0);
}

export function isCompleteStrummingBar(configuration: Pick<StrummingConfiguration, "timeSignature" | "events">) {
  const configuredLength = configuration.events.reduce((total, event) => total + beatLengths[event.rhythmicValue], 0);
  return Math.abs(configuredLength - getBarLengthInQuarterNotes(configuration.timeSignature)) < 0.000_001;
}

export function createDefaultStrummingConfiguration(timeSignature: StrummingTimeSignature = "4/4"): StrummingConfiguration {
  const eventCount = timeSignature === "6/8" ? 6 : Number(timeSignature[0]);
  const rhythmicValue: RhythmicValue = timeSignature === "6/8" ? "eighth" : "quarter";

  return {
    bpm: 80,
    timeSignature,
    bars: 1,
    repetitions: 4,
    loop: true,
    events: Array.from({ length: eventCount }, (_, index) => ({
      rhythmicValue,
      handMotion: index % 2 === 0 ? "down" : "up",
      action: index % 2 === 0 ? "down" : "up",
      accent: false,
    })),
  };
}
