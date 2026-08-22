export function describeOtpDelivery(
  deliveryChannel: string | null | undefined,
  destinationHint: string | null | undefined,
  emailFallback = 'your email',
): string {
  const hint = destinationHint?.trim() || emailFallback;
  return deliveryChannel === 'sms' ? `SMS to ${hint}` : `email at ${hint}`;
}

export function isSmsOtpChannel(deliveryChannel: string | null | undefined): boolean {
  return deliveryChannel === 'sms';
}
