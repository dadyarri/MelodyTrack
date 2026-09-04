import dayjs from "dayjs";
import { describe, expect, it } from "vitest";

import { customTaskDraftCodec } from "./useTasksPageController";

describe("custom task draft codec", () => {
  it("serializes partial form values before a due date is selected", () => {
    const partialValues = {
      recipientMode: "client" as const,
      title: "Call the client",
      messageText: "",
    };

    expect(customTaskDraftCodec.serialize(partialValues)).toEqual({
      recipientMode: "client",
      title: "Call the client",
      messageText: "",
      dueAt: undefined,
    });
  });

  it("serializes a selected due date as ISO text", () => {
    const dueAt = dayjs("2026-07-26T12:30:00.000Z");

    expect(
      customTaskDraftCodec.serialize({
        recipientMode: "external",
        recipientName: "Client",
        title: "Call the client",
        messageText: "Discuss the appointment",
        dueAt,
      }).dueAt,
    ).toBe(dueAt.toISOString());
  });
});
