self.addEventListener("install", () => {
  self.skipWaiting();
});

self.addEventListener("activate", (event) => {
  event.waitUntil(self.clients.claim());
});

async function hasFocusedClient() {
  const allClients = await self.clients.matchAll({ type: "window", includeUncontrolled: true });
  return allClients.some((c) => c.focused);
}

self.addEventListener("message", (event) => {
  const data = event.data || {};
  if (data.type !== "SHOW_NOTIFICATION") return;
  const title = data.title || "New message";
  const options = {
    body: data.body || "You received a new message",
    tag: data.tag || "textzy-inbox",
    renotify: false,
    data: data.data || {},
    icon: "/favicon.ico",
    badge: "/favicon.ico"
  };

  event.waitUntil(
    hasFocusedClient().then((focused) => {
      if (!focused) return self.registration.showNotification(title, options);
      return Promise.resolve();
    })
  );
});

self.addEventListener("push", (event) => {
  let payload = {};
  try {
    payload = event.data ? event.data.json() : {};
  } catch {
    payload = {};
  }
  const title = payload.title || "New WhatsApp Message";
  const options = {
    body: payload.body || "You received a new customer message",
    tag: payload.tag || "textzy-inbox",
    renotify: false,
    data: payload.data || {},
    icon: "/favicon.ico",
    badge: "/favicon.ico",
    vibrate: [120, 50, 120]
  };

  event.waitUntil(
    hasFocusedClient().then((focused) => {
      if (!focused) return self.registration.showNotification(title, options);
      return Promise.resolve();
    })
  );
});

self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  event.waitUntil(
    self.clients.matchAll({ type: "window", includeUncontrolled: true }).then((clientsArr) => {
      const existing = clientsArr.find((c) => c.url.includes("/dashboard/inbox"));
      if (existing) return existing.focus();
      return self.clients.openWindow("/dashboard/inbox");
    })
  );
});

