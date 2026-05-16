import type { Metadata } from 'next';
import { RuntimeSettingsClient } from './RuntimeSettingsClient';

export const metadata: Metadata = {
  title: 'Runtime Settings · Admin',
  description:
    "Configure production secrets without editing the server's .env file. Changes apply within ~30 seconds.",
};

export default function RuntimeSettingsPage() {
  return <RuntimeSettingsClient />;
}
