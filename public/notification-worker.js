self.addEventListener('push', (event) => {
  if (!event.data) {
    return;
  }

  let payload = {};
  try {
    payload = event.data.json();
  } catch {
    payload = {
      title: 'OET Prep notification',
      body: event.data.text(),
    };
  }

  const title = payload.title || 'OET Prep notification';
  const options = {
    body: payload.body || '',
    icon: '/icon.svg',
    badge: '/icon.svg',
    data: {
      actionUrl: payload.actionUrl || '/',
      notificationId: payload.notificationId || null,
    },
  };

  event.waitUntil(self.registration.showNotification(title, options));
});

self.addEventListener('notificationclick', (event) => {
  event.notification.close();

  const actionUrl = event.notification?.data?.actionUrl || '/';
  const targetUrl = new URL(actionUrl, self.location.origin).href;

  event.waitUntil((async () => {
    const allClients = await clients.matchAll({ includeUncontrolled: true, type: 'window' });
    const matchingClient = allClients.find((client) => 'focus' in client && client.url === targetUrl);
    if (matchingClient) {
      await matchingClient.focus();
      return;
    }

    await clients.openWindow(targetUrl);
  })());
});
