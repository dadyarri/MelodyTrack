import { afterEach, describe, expect, it, vi } from "vitest";

import { serializePushSubscription, supportsPushNotifications } from "./pushNotifications";

describe("pushNotifications", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("reports unsupported browsers without a push manager", () => {
    vi.stubGlobal("PushManager", undefined);

    expect(supportsPushNotifications()).toBe(false);
  });

  it("serializes subscription keys without retaining browser objects", () => {
    const subscription = {
      endpoint: "https://push.example/subscription",
      expirationTime: null,
      getKey: (name: PushEncryptionKeyName) => {
        const bytes = name === "p256dh" ? [1, 2, 3] : [4, 5, 6];
        return Uint8Array.from(bytes).buffer;
      },
    } as PushSubscription;

    expect(serializePushSubscription(subscription)).toEqual({
      endpoint: "https://push.example/subscription",
      p256Dh: "AQID",
      auth: "BAUG",
      expiresAtUtc: undefined,
    });
  });
});
