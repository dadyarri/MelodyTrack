export type BrowserPushSubscription = {
  endpoint: string;
  p256Dh: string;
  auth: string;
  expiresAtUtc?: string;
};

export function supportsPushNotifications() {
  if (typeof window === "undefined" || !window.isSecureContext) {
    return false;
  }

  return (
    typeof Notification !== "undefined" &&
    typeof navigator !== "undefined" &&
    "serviceWorker" in navigator &&
    typeof PushManager !== "undefined"
  );
}

export function preparePushRegistration() {
  if (!supportsPushNotifications()) {
    return Promise.resolve(null);
  }

  return navigator.serviceWorker.register("/service-worker.js", { scope: "/" });
}

export function requestPushPermission() {
  if (!supportsPushNotifications()) {
    return Promise.resolve<NotificationPermission>("denied");
  }

  return Notification.requestPermission();
}

export async function getBrowserPushSubscription(registration: ServiceWorkerRegistration) {
  return registration.pushManager.getSubscription();
}

export async function subscribeBrowserToPush(registration: ServiceWorkerRegistration, publicKey: string) {
  const existing = await registration.pushManager.getSubscription();
  const subscription =
    existing ??
    (await registration.pushManager.subscribe({
      userVisibleOnly: true,
      applicationServerKey: decodeBase64Url(publicKey),
    }));
  return serializePushSubscription(subscription);
}

export function serializePushSubscription(subscription: PushSubscription): BrowserPushSubscription {
  const p256Dh = subscription.getKey("p256dh");
  const auth = subscription.getKey("auth");
  if (!p256Dh || !auth) {
    throw new Error("Push subscription keys are unavailable.");
  }

  return {
    endpoint: subscription.endpoint,
    p256Dh: encodeBase64Url(p256Dh),
    auth: encodeBase64Url(auth),
    expiresAtUtc: subscription.expirationTime ? new Date(subscription.expirationTime).toISOString() : undefined,
  };
}

function decodeBase64Url(value: string) {
  const padding = "=".repeat((4 - (value.length % 4)) % 4);
  const raw = window.atob(value.replaceAll("-", "+").replaceAll("_", "/") + padding);
  const buffer = new ArrayBuffer(raw.length);
  const bytes = new Uint8Array(buffer);
  for (let index = 0; index < raw.length; index++) {
    bytes[index] = raw.charCodeAt(index);
  }
  return buffer;
}

function encodeBase64Url(value: ArrayBuffer) {
  const bytes = new Uint8Array(value);
  let binary = "";
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }
  return window.btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replaceAll("=", "");
}
