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
