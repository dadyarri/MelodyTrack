import type { MetronomeConfiguration, TempoProgression } from "./practiceExercise";
import { advanceExerciseTempo } from "./tempoProgression";

export type ExerciseTimingConfiguration = Readonly<{
  bpm: number;
  beatsPerBar: number;
  quarterNotesPerBeat: number;
  slotsPerBar: readonly number[];
  repetitions: number;
  loop: boolean;
  metronome: MetronomeConfiguration;
  tempoProgression: TempoProgression;
}>;
export type ExerciseTimingPosition = Readonly<{
  bar: number | null;
  slot: number | null;
  countIn: number | null;
  repetition: number;
  bpm: number;
  time: number;
}>;

/** Shared musical cursor; trainer-specific actions do not participate in timing. */
export function createExerciseTimingCursor(configuration: ExerciseTimingConfiguration, startedAt = 0.06) {
  if (
    !Number.isFinite(configuration.bpm) ||
    configuration.bpm < 30 ||
    configuration.bpm > 240 ||
    !Number.isInteger(configuration.beatsPerBar) ||
    configuration.beatsPerBar < 1 ||
    configuration.beatsPerBar > 12 ||
    !Number.isFinite(configuration.quarterNotesPerBeat) ||
    configuration.quarterNotesPerBeat <= 0 ||
    configuration.quarterNotesPerBeat > 4 ||
    !Number.isInteger(configuration.repetitions) ||
    configuration.repetitions < 1 ||
    configuration.repetitions > 100 ||
    configuration.slotsPerBar.length < 1 ||
    configuration.slotsPerBar.length > 16 ||
    configuration.slotsPerBar.some(
      (slots) => !Number.isInteger(slots) || slots < 1 || slots > 64 || slots % configuration.beatsPerBar !== 0,
    ) ||
    !Number.isFinite(configuration.metronome.volume) ||
    configuration.metronome.volume < 0 ||
    configuration.metronome.volume > 1 ||
    !Number.isInteger(configuration.tempoProgression.every) ||
    configuration.tempoProgression.every < 1 ||
    configuration.tempoProgression.every > 100 ||
    !Number.isInteger(configuration.tempoProgression.increment) ||
    configuration.tempoProgression.increment < 1 ||
    configuration.tempoProgression.increment > 60 ||
    !Number.isFinite(startedAt) ||
    startedAt < 0
  )
    throw new Error("Некорректные параметры ритма упражнения.");
  let time = startedAt;
  let countIn = 1;
  let bar = 0;
  let slot = 0;
  let repetition = 1;
  let completedBars = 0;
  let bpm = configuration.bpm;
  let complete = false;
  return {
    get nextTime() {
      return time;
    },
    get complete() {
      return complete;
    },
    next() {
      if (complete) return null;
      const beatDuration = (configuration.quarterNotesPerBeat * 60) / bpm;
      if (countIn <= configuration.beatsPerBar) {
        const position: ExerciseTimingPosition = { time, countIn, bar: null, slot: null, repetition: 1, bpm };
        time += beatDuration;
        countIn += 1;
        return { position, click: true, accented: position.countIn === 1 };
      }
      const position: ExerciseTimingPosition = { time, countIn: null, bar, slot, repetition, bpm };
      const slotsPerBeat = configuration.slotsPerBar[bar] / configuration.beatsPerBar;
      const click = slot % slotsPerBeat === 0;
      const accented = slot === 0;
      time += (configuration.beatsPerBar * beatDuration) / configuration.slotsPerBar[bar];
      slot += 1;
      if (slot === configuration.slotsPerBar[bar]) {
        slot = 0;
        bar += 1;
        completedBars += 1;
        const completesRepetition = bar === configuration.slotsPerBar.length;
        bpm = advanceExerciseTempo(configuration.tempoProgression, bpm, completedBars, repetition, completesRepetition);
        if (completesRepetition) {
          bar = 0;
          repetition += 1;
          complete = !configuration.loop && repetition > configuration.repetitions;
        }
      }
      return { position, click, accented };
    },
  };
}

/** Exercise-only duration; count-in is calculated separately by the host. */
export function getExerciseTimingDurationSeconds(configuration: ExerciseTimingConfiguration) {
  let duration = 0;
  let bpm = configuration.bpm;
  let completedBars = 0;
  for (let repetition = 1; repetition <= configuration.repetitions; repetition += 1) {
    for (let bar = 0; bar < configuration.slotsPerBar.length; bar += 1) {
      duration += (configuration.beatsPerBar * configuration.quarterNotesPerBeat * 60) / bpm;
      completedBars += 1;
      bpm = advanceExerciseTempo(
        configuration.tempoProgression,
        bpm,
        completedBars,
        repetition,
        bar === configuration.slotsPerBar.length - 1,
      );
    }
  }
  return duration;
}
