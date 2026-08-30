self.addEventListener("push", (event) => {
  let payload = {};

  try {
    payload = event.data?.json() ?? {};
  } catch {
    payload = {};
  }

  const title = typeof payload.title === "string" ? payload.title : "MelodyTrack";
  const body = typeof payload.body === "string" ? payload.body : "У вас новое уведомление";
  const url = typeof payload.url === "string" && payload.url.startsWith("/") && !payload.url.startsWith("//") ? payload.url : "/";
  const notificationId = typeof payload.notificationId === "string" ? payload.notificationId : undefined;

  event.waitUntil(
    self.registration.showNotification(title, {
      body,
      icon: "/web-app-manifest-192x192.png",
      badge: "/favicon-96x96.png",
      data: { url },
      tag: notificationId ? `melodytrack-notification-${notificationId}` : undefined,
    }),
  );
});

self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  const path = event.notification.data?.url;
  const safePath = typeof path === "string" && path.startsWith("/") && !path.startsWith("//") ? path : "/";
  const targetUrl = new URL(safePath, self.location.origin).href;

  event.waitUntil(
    self.clients.matchAll({ type: "window", includeUncontrolled: true }).then(async (clients) => {
      const existingClient = clients.find((client) => new URL(client.url).origin === self.location.origin);
      if (existingClient) {
        await existingClient.navigate(targetUrl);
        return existingClient.focus();
      }

      return self.clients.openWindow(targetUrl);
    }),
  );
});
