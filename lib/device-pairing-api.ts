import { completeDevicePairing, initiateDevicePairing, type DevicePairingInitiateResponse } from './auth-client';

export type { DevicePairingInitiateResponse };

export async function startDevicePairing(accessToken?: string | null): Promise<DevicePairingInitiateResponse> {
  return initiateDevicePairing(accessToken);
}

export async function redeemDevicePairingCode(code: string): Promise<void> {
  await completeDevicePairing(code);
}
