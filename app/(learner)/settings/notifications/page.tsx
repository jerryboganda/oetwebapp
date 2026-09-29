'use client';

import { NotificationsSettingsView } from '@/components/settings/notifications-settings-view';

/**
 * Dedicated notification settings surface. This static segment intentionally
 * takes precedence over the generic `app/settings/[section]` page so the
 * notification workspace can use its own three-column layout without changing
 * how every other settings section renders.
 */
export default function NotificationSettingsPage() {
  return (
    <>
      <NotificationsSettingsView />
    </>
  );
}
