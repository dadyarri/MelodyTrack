import { describe, expect, it } from "vitest";

import { hydrateExerciseNodes, parseHomeworkDocument, reconcileDocumentExercises, serializeHomeworkDraft } from "./document";

describe("Structured homework document", () => {
  it("stores exercise configuration separately from the document reference", () => {
    const result = serializeHomeworkDraft({
      type: "doc",
      content: [
        { type: "paragraph", content: [{ type: "text", text: "Отработать бой" }] },
        {
          type: "practiceExercise",
          attrs: { exerciseId: "draft-one", exerciseType: "strumming", configuration: '{"bpm":80}' },
        },
      ],
    });

    expect(JSON.parse(result.homeworkDocument)).toEqual({
      schemaVersion: 1,
      document: {
        type: "doc",
        content: [
          { type: "paragraph", content: [{ type: "text", text: "Отработать бой" }] },
          { type: "practiceExercise", attrs: { exerciseId: "draft-one" } },
        ],
      },
    });
    expect(result.practiceExercises).toEqual([{ reference: "draft-one", type: "strumming", configuration: '{"bpm":80}' }]);
  });

  it("hydrates referenced exercises for editor and runtime rendering", () => {
    const document = parseHomeworkDocument(
      JSON.stringify({
        schemaVersion: 1,
        document: { type: "doc", content: [{ type: "practiceExercise", attrs: { exerciseId: "01EXERCISE" } }] },
      }),
    );

    expect(hydrateExerciseNodes(document, [{ id: "01EXERCISE", type: "strumming", configuration: '{"bpm":100}' }])).toEqual({
      type: "doc",
      content: [
        {
          type: "practiceExercise",
          attrs: { exerciseId: "01EXERCISE", exerciseType: "strumming", configuration: '{"bpm":100}' },
        },
      ],
    });
  });

  it("rejects legacy and unsupported envelopes at the rendering boundary", () => {
    expect(parseHomeworkDocument("[b]старый BBCode[/b]")).toEqual({ type: "doc", content: [{ type: "paragraph" }] });
    expect(parseHomeworkDocument('{"schemaVersion":2,"document":{"type":"doc"}}')).toEqual({
      type: "doc",
      content: [{ type: "paragraph" }],
    });
  });

  it("keeps exercises referenced by the other theme document", () => {
    const otherDocument = JSON.stringify({
      schemaVersion: 1,
      document: { type: "doc", content: [{ type: "practiceExercise", attrs: { exerciseId: "lesson-exercise" } }] },
    });

    expect(
      reconcileDocumentExercises([{ id: "homework-exercise", type: "strumming", configuration: '{"bpm":100}' }], otherDocument, [
        { id: "lesson-exercise", type: "strumming", configuration: '{"bpm":80}' },
        { id: "removed-homework-exercise", type: "strumming", configuration: '{"bpm":60}' },
      ]),
    ).toEqual([
      { id: "lesson-exercise", type: "strumming", configuration: '{"bpm":80}' },
      { id: "homework-exercise", type: "strumming", configuration: '{"bpm":100}' },
    ]);
  });
});
