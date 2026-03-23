const RESET_PASSWORD_TOKEN = "verified-reset";

type SearchParamValue = string | string[] | undefined;
type SearchParamRecord = Record<string, SearchParamValue>;

function firstValue(value: SearchParamValue): string {
  return Array.isArray(value) ? (value[0] ?? "") : (value ?? "");
}

function withQuery(
  path: string,
  values: Record<string, string | undefined | null>
): string {
  const params = new URLSearchParams();

  Object.entries(values).forEach(([key, value]) => {
    if (value) {
      params.set(key, value);
    }
  });

  const query = params.toString();
  return query ? `${path}?${query}` : path;
}

export function normalizeResetEmail(email: string | null | undefined): string {
  return String(email ?? "")
    .trim()
    .toLowerCase();
}

export function getResetEmailFromSearchParams(
  searchParams: SearchParamRecord
): string {
  return normalizeResetEmail(firstValue(searchParams.email));
}

export function getResetErrorFromSearchParams(
  searchParams: SearchParamRecord
): string {
  return firstValue(searchParams.error);
}

export function hasVerifiedResetToken(
  searchParams: SearchParamRecord
): boolean {
  return firstValue(searchParams.token) === RESET_PASSWORD_TOKEN;
}

export function buildPasswordResetHref(options?: {
  email?: string;
  error?: string;
}) {
  return withQuery("/forgot-password", {
    email: options?.email,
    error: options?.error,
  });
}

export function buildPasswordResetOtpHref(options: {
  email: string;
  error?: string;
}) {
  return withQuery("/forgot-password/verify", {
    email: options.email,
    error: options.error,
  });
}

export function buildPasswordCreateHref(options: {
  email: string;
  error?: string;
}) {
  return withQuery("/reset-password", {
    email: options.email,
    error: options.error,
    token: RESET_PASSWORD_TOKEN,
  });
}

export function buildPasswordResetSuccessHref(options: { email: string }) {
  return withQuery("/reset-password/success", {
    email: options.email,
  });
}
