import type { JSONContent } from "@tiptap/core";

import type { CourseThemePracticeExerciseDto, CourseThemePracticeExerciseRequest } from "@/shared/api/generated/models";

export const homeworkDocumentSchemaVersion = 1;

export type HomeworkExercise = Readonly<{
  id: string;
  type: "strumming";
  configuration: string;
}>;

export type HomeworkDocumentEnvelope = Readonly<{
  schemaVersion: 1;
  document: JSONContent;
}>;

export function parseHomeworkDocument(value?: string | null): JSONContent {
  if (!value) return { type: "doc", content: [{ type: "paragraph" }] };
  try {
    const envelope = JSON.parse(value) as Partial<HomeworkDocumentEnvelope>;
    return envelope.schemaVersion === homeworkDocumentSchemaVersion && envelope.document?.type === "doc"
      ? envelope.document
      : { type: "doc", content: [{ type: "paragraph" }] };
  } catch {
    return { type: "doc", content: [{ type: "paragraph" }] };
  }
}

export function hydrateExerciseNodes(document: JSONContent, exercises: readonly HomeworkExercise[]): JSONContent {
  const byId = new Map(exercises.map((exercise) => [exercise.id, exercise]));
  return mapDocument(document, (node) => {
    if (node.type !== "practiceExercise") return node;
    const id = typeof node.attrs?.exerciseId === "string" ? node.attrs.exerciseId : "";
    const exercise = byId.get(id);
    return exercise ? { ...node, attrs: { ...node.attrs, exerciseType: exercise.type, configuration: exercise.configuration } } : node;
  });
}

export function serializeHomeworkDraft(document: JSONContent): {
  homeworkDocument: string;
  practiceExercises: CourseThemePracticeExerciseRequest[];
} {
  const exercises: CourseThemePracticeExerciseRequest[] = [];
  const persisted = mapDocument(document, (node) => {
    if (node.type !== "practiceExercise") return node;
    const reference = String(node.attrs?.exerciseId ?? "");
    exercises.push({
      reference,
      type: String(node.attrs?.exerciseType ?? "strumming"),
      configuration: String(node.attrs?.configuration ?? "{}"),
    });
    return { type: "practiceExercise", attrs: { exerciseId: reference } };
  });
  return {
    homeworkDocument: JSON.stringify({ schemaVersion: homeworkDocumentSchemaVersion, document: persisted }),
    practiceExercises: exercises,
  };
}

export function mapExerciseDtos(values?: CourseThemePracticeExerciseDto[] | null): HomeworkExercise[] {
  return (values ?? []).flatMap((value) =>
    value.id && value.type === "strumming" && value.configuration
      ? [{ id: value.id, type: "strumming" as const, configuration: value.configuration }]
      : [],
  );
}

export function reconcileDocumentExercises(
  updatedExercises: readonly HomeworkExercise[],
  otherDocument: string | undefined,
  currentExercises: readonly HomeworkExercise[],
): HomeworkExercise[] {
  const otherReferences = collectExerciseReferences(parseHomeworkDocument(otherDocument));
  const reconciled = new Map(
    currentExercises.filter((exercise) => otherReferences.has(exercise.id)).map((exercise) => [exercise.id, exercise]),
  );
  for (const exercise of updatedExercises) reconciled.set(exercise.id, exercise);
  return [...reconciled.values()];
}

function mapDocument(node: JSONContent, transform: (node: JSONContent) => JSONContent): JSONContent {
  const mapped = node.content ? { ...node, content: node.content.map((child) => mapDocument(child, transform)) } : { ...node };
  return transform(mapped);
}

function collectExerciseReferences(node: JSONContent, references = new Set<string>()): Set<string> {
  if (node.type === "practiceExercise" && typeof node.attrs?.exerciseId === "string") references.add(node.attrs.exerciseId);
  for (const child of node.content ?? []) collectExerciseReferences(child, references);
  return references;
}
