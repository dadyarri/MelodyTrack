import { getExerciseTimingDurationSeconds } from "./exerciseTiming";
export { shouldIncreaseTempo } from "./tempoProgression";

export const rhythmicValues = ["quarter", "eighth", "sixteenth"] as const;
export type RhythmicValue = (typeof rhythmicValues)[number];
export type PracticeExerciseDefinition<Type extends string = string, Configuration = unknown> = Readonly<{
  type: Type;
  configuration: Configuration;
}>;
export type PracticeExerciseTimeline = Readonly<{ iterationDurationSeconds: number }>;
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
export const handMotions = ["down", "up", "muted", "rest", "down-up", "up-down"] as const;
export const strummingTimeSignatures = ["2/4", "3/4", "4/4", "6/8"] as const;
export const tempoProgressionUnits = ["bars", "repetitions"] as const;
export const maximumStrummingBars = 16;
export const maximumStrummingRepetitions = 100;
export type StrumAction = (typeof strumActions)[number];
export type HandMotion = (typeof handMotions)[number];
export type StrummingTimeSignature = (typeof strummingTimeSignatures)[number];
export type TempoProgressionUnit = (typeof tempoProgressionUnits)[number];
export type StrummingStroke = Readonly<{ action: StrumAction; accent: boolean }>;
/** Both rows use the same fixed time slots. An omitted action is represented by rest. */
export type StrummingBar = Readonly<{
  rhythmicValue: RhythmicValue;
  strokes: readonly StrummingStroke[];
  handMotions: readonly HandMotion[];
}>;
export type MetronomeConfiguration = Readonly<{ enabled: boolean; volume: number }>;
export type TempoProgression = Readonly<{ enabled: boolean; increment: number; every: number; unit: TempoProgressionUnit }>;
export type StrummingConfiguration = Readonly<{
  schemaVersion: 1;
  bpm: number;
  timeSignature: StrummingTimeSignature;
  bars: readonly StrummingBar[];
  repetitions: number;
  loop: boolean;
  metronome: MetronomeConfiguration;
  tempoProgression: TempoProgression;
}>;
const beatLengths: Record<RhythmicValue, number> = { quarter: 1, eighth: 0.5, sixteenth: 0.25 };
export function getBarLengthInQuarterNotes(timeSignature: StrummingTimeSignature) {
  const [beats, denominator] = timeSignature.split("/").map(Number);
  return beats * (4 / denominator);
}
export function getBarDurationSeconds(timeSignature: StrummingTimeSignature, bpm: number) {
  return (getBarLengthInQuarterNotes(timeSignature) * 60) / bpm;
}
export function getCountInBeats(timeSignature: StrummingTimeSignature) {
  return Number(timeSignature.split("/")[0]);
}
/** BPM always counts quarter notes; in 6/8 the six clicks are eighth notes. */
export function getMetronomeBeatDurationSeconds(timeSignature: StrummingTimeSignature, bpm: number) {
  return ((4 / Number(timeSignature.split("/")[1])) * 60) / bpm;
}
export function getCountInDurationSeconds(timeSignature: StrummingTimeSignature, bpm: number) {
  return getCountInBeats(timeSignature) * getMetronomeBeatDurationSeconds(timeSignature, bpm);
}
export function getStrummingDurationSeconds(configuration: StrummingConfiguration) {
  return configuration.bars.length * getBarDurationSeconds(configuration.timeSignature, configuration.bpm);
}
export function getBarSlotCount(timeSignature: StrummingTimeSignature, rhythmicValue: RhythmicValue) {
  return getBarLengthInQuarterNotes(timeSignature) / beatLengths[rhythmicValue];
}
export function getStrummingCountLabels(timeSignature: StrummingTimeSignature, count: number) {
  const beats = getCountInBeats(timeSignature);
  const subdivisions = count / beats;
  return Array.from({ length: count }, (_, index) => {
    const subdivision = index % subdivisions;
    if (subdivision === 0) return String(Math.floor(index / subdivisions) + 1);
    if (subdivisions === 2) return "и";
    return ["", "та", "и", "та"][subdivision] ?? "";
  });
}
export function isCompleteStrummingBar(configuration: Pick<StrummingConfiguration, "timeSignature"> & StrummingBar) {
  const slots = getBarSlotCount(configuration.timeSignature, configuration.rhythmicValue);
  return (
    Number.isInteger(slots) &&
    !(configuration.timeSignature === "6/8" && configuration.rhythmicValue === "quarter") &&
    configuration.strokes.length === slots &&
    configuration.handMotions.length === slots
  );
}
function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
function inRange(value: unknown, minimum: number, maximum: number, integer = false): value is number {
  return (
    typeof value === "number" && Number.isFinite(value) && value >= minimum && value <= maximum && (!integer || Number.isInteger(value))
  );
}
export function parseStrummingConfiguration(value: unknown): StrummingConfiguration | null {
  if (
    !isRecord(value) ||
    value.schemaVersion !== 1 ||
    !inRange(value.bpm, 30, 240) ||
    !inRange(value.repetitions, 1, maximumStrummingRepetitions, true) ||
    typeof value.loop !== "boolean"
  )
    return null;
  if (
    !strummingTimeSignatures.includes(value.timeSignature as StrummingTimeSignature) ||
    !Array.isArray(value.bars) ||
    value.bars.length < 1 ||
    value.bars.length > maximumStrummingBars
  )
    return null;
  const timeSignature = value.timeSignature as StrummingTimeSignature;
  if (
    !value.bars.every((bar: unknown) => {
      if (!isRecord(bar) || !rhythmicValues.includes(bar.rhythmicValue as RhythmicValue)) return false;
      const slots = getBarSlotCount(timeSignature, bar.rhythmicValue as RhythmicValue);
      return (
        Number.isInteger(slots) &&
        !(timeSignature === "6/8" && bar.rhythmicValue === "quarter") &&
        Array.isArray(bar.strokes) &&
        bar.strokes.length === slots &&
        bar.strokes.every(
          (stroke: unknown) =>
            isRecord(stroke) &&
            strumActions.includes(stroke.action as StrumAction) &&
            typeof stroke.accent === "boolean" &&
            (stroke.action !== "rest" || !stroke.accent),
        ) &&
        Array.isArray(bar.handMotions) &&
        bar.handMotions.length === slots &&
        bar.handMotions.every((motion: unknown) => handMotions.includes(motion as HandMotion))
      );
    })
  )
    return null;
  const metronome = value.metronome;
  const progression = value.tempoProgression;
  if (!isRecord(metronome) || typeof metronome.enabled !== "boolean" || !inRange(metronome.volume, 0, 1)) return null;
  if (
    !isRecord(progression) ||
    typeof progression.enabled !== "boolean" ||
    !inRange(progression.increment, 1, 60, true) ||
    !inRange(progression.every, 1, 100, true) ||
    !tempoProgressionUnits.includes(progression.unit as TempoProgressionUnit)
  )
    return null;
  return value as unknown as StrummingConfiguration;
}
export function isCompleteStrummingConfiguration(configuration: StrummingConfiguration) {
  return parseStrummingConfiguration(configuration) !== null;
}
export function createDefaultStrummingBar(
  timeSignature: StrummingTimeSignature,
  rhythmicValue: RhythmicValue = timeSignature === "6/8" ? "eighth" : "quarter",
): StrummingBar {
  const slots = getBarSlotCount(timeSignature, rhythmicValue);
  return {
    rhythmicValue,
    strokes: Array.from({ length: slots }, (_, index) => ({ action: index % 2 === 0 ? "down" : "up", accent: false })),
    handMotions: Array.from({ length: slots }, (_, index) => (index % 2 === 0 ? "down" : "up")),
  };
}
export function createDefaultStrummingConfiguration(timeSignature: StrummingTimeSignature = "4/4"): StrummingConfiguration {
  return {
    schemaVersion: 1,
    bpm: 80,
    timeSignature,
    bars: [createDefaultStrummingBar(timeSignature)],
    repetitions: 4,
    loop: true,
    metronome: { enabled: true, volume: 0.6 },
    tempoProgression: { enabled: false, increment: 5, every: 4, unit: "repetitions" },
  };
}
/** Returns null if the coarser grid would discard an action between its slots. */
export function changeStrummingGrid(
  bar: StrummingBar,
  timeSignature: StrummingTimeSignature,
  rhythmicValue: RhythmicValue,
): StrummingBar | null {
  const slots = getBarSlotCount(timeSignature, rhythmicValue);
  if (!Number.isInteger(slots)) return null;
  const ratio = beatLengths[bar.rhythmicValue] / beatLengths[rhythmicValue];
  if (
    bar.strokes.some((stroke, index) => !Number.isInteger(index * ratio) && (stroke.action !== "rest" || bar.handMotions[index] !== "rest"))
  )
    return null;
  return {
    rhythmicValue,
    strokes: Array.from({ length: slots }, (_, index) => (Number.isInteger(index / ratio) ? bar.strokes[index / ratio] : undefined)).map(
      (stroke) => stroke ?? { action: "rest", accent: false },
    ),
    handMotions: Array.from({ length: slots }, (_, index) =>
      Number.isInteger(index / ratio) ? (bar.handMotions[index / ratio] ?? "rest") : "rest",
    ),
  };
}
export function getStrummingPlaybackDurationSeconds(configuration: StrummingConfiguration) {
  return getExerciseTimingDurationSeconds({
    bpm: configuration.bpm,
    beatsPerBar: getCountInBeats(configuration.timeSignature),
    quarterNotesPerBeat: 4 / Number(configuration.timeSignature.split("/")[1]),
    slotsPerBar: configuration.bars.map((bar) => bar.strokes.length),
    repetitions: configuration.repetitions,
    loop: false,
    metronome: configuration.metronome,
    tempoProgression: configuration.tempoProgression,
  });
}
