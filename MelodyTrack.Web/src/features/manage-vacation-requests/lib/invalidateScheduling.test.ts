import { QueryClient } from "@tanstack/react-query";
import { describe, expect, it } from "vitest";

import { invalidateScheduling } from "./invalidateScheduling";

describe("schedule changes", () => {
  it("invalidates affected views while preserving unrelated reference data", async () => {
    const client = new QueryClient();
    const keys = [
      ["schedule", "appointments"],
      ["users", "availability"],
      ["portal", "schedule"],
      ["tasks"],
      ["reports"],
      ["vacations"],
      ["services"],
    ];
    for (const key of keys) client.setQueryData(key, []);
    await invalidateScheduling(client);
    for (const key of keys.slice(0, -1)) expect(client.getQueryState(key)?.isInvalidated).toBe(true);
    expect(client.getQueryState(["services"])?.isInvalidated).toBe(false);
    client.clear();
  });
});
