import type { AiPackageCreditBucket, AiPackageCreditSnapshot } from './billing-types';

type ApiRecord = Record<string, any>;

function asRecord(value: unknown): ApiRecord {
  return value && typeof value === 'object' ? (value as ApiRecord) : {};
}

function asArray(value: unknown): ApiRecord[] {
  return Array.isArray(value) ? value.map(asRecord) : [];
}

function toNullableString(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null;
}

export function mapAiPackageCreditSnapshot(data: ApiRecord): AiPackageCreditSnapshot {
  const flexibleCredits = Number(data.flexibleCredits ?? 0);
  const sharedCredits = Number(data.sharedCredits ?? 0);
  const writingOnlyCredits = Number(data.writingOnlyCredits ?? 0);
  const speakingOnlyCredits = Number(data.speakingOnlyCredits ?? 0);
  const creditsRemaining = Number(
    data.creditsRemaining ?? flexibleCredits + sharedCredits + writingOnlyCredits + speakingOnlyCredits,
  );
  return {
    userId: String(data.userId ?? ''),
    flexibleCredits,
    writingOnlyCredits,
    speakingOnlyCredits,
    listeningTestsRemaining: data.listeningTestsRemaining == null ? null : Number(data.listeningTestsRemaining),
    readingTestsRemaining: data.readingTestsRemaining == null ? null : Number(data.readingTestsRemaining),
    mockExamsRemaining: Number(data.mockExamsRemaining ?? 0),
    expiresAt: toNullableString(data.expiresAt),
    expiredBecausePassed: Boolean(data.expiredBecausePassed),
    passedAt: toNullableString(data.passedAt),
    transactions: asArray(data.transactions).map((item) => ({
      id: String(item.id ?? ''),
      packageId: toNullableString(item.packageId),
      packageType: toNullableString(item.packageType),
      reason: String(item.reason ?? ''),
      sharedCreditsDelta: Number(item.sharedCreditsDelta ?? 0),
      flexibleCreditsDelta: Number(item.flexibleCreditsDelta ?? 0),
      writingOnlyCreditsDelta: Number(item.writingOnlyCreditsDelta ?? 0),
      speakingOnlyCreditsDelta: Number(item.speakingOnlyCreditsDelta ?? 0),
      listeningTestsDelta: Number(item.listeningTestsDelta ?? 0),
      readingTestsDelta: Number(item.readingTestsDelta ?? 0),
      mockExamsDelta: Number(item.mockExamsDelta ?? 0),
      referenceId: toNullableString(item.referenceId),
      description: String(item.description ?? ''),
      expiresAt: toNullableString(item.expiresAt),
      createdAt: String(item.createdAt ?? ''),
    })),
    creditsGranted: Number(data.creditsGranted ?? 0),
    creditsUsed: Number(data.creditsUsed ?? 0),
    creditsRemaining,
    writingUnlimited: data.writingUnlimited === true,
    speakingUnlimited: data.speakingUnlimited === true,
    sharedCredits,
    sharedCreditsGranted: Number(data.sharedCreditsGranted ?? 0),
    sharedCreditsUsed: Number(data.sharedCreditsUsed ?? 0),
    buckets: mapBuckets(data.buckets),
  };
}

function mapBuckets(value: unknown): AiPackageCreditBucket[] | null {
  if (!Array.isArray(value)) return null;
  return asArray(value).map((bucket) => ({
    key: (String(bucket.key ?? '') || 'shared') as AiPackageCreditBucket['key'],
    label: String(bucket.label ?? ''),
    unlimited: bucket.unlimited === true,
    totalGranted: Number(bucket.totalGranted ?? 0),
    used: Number(bucket.used ?? 0),
    remaining: Number(bucket.remaining ?? 0),
    sourcePackages: toNullableString(bucket.sourcePackages),
    validFrom: toNullableString(bucket.validFrom),
    expiresAt: toNullableString(bucket.expiresAt),
    daysLeft: Number(bucket.daysLeft ?? -1),
    grants: asArray(bucket.grants).map((grant) => ({
      packageId: toNullableString(grant.packageId),
      description: String(grant.description ?? ''),
      totalGranted: Number(grant.totalGranted ?? 0),
      grantedAt: String(grant.grantedAt ?? ''),
      expiresAt: toNullableString(grant.expiresAt),
    })),
  }));
}
