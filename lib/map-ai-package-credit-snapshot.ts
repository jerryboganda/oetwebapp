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

function mapNamedBucket(value: unknown) {
  const data = asRecord(value);
  if (!data || Object.keys(data).length === 0) return null;
  return {
    totalGranted: Number(data.totalGranted ?? 0),
    used: Number(data.used ?? 0),
    remaining: Number(data.remaining ?? 0),
    unlimited: data.unlimited === true,
    sourcePackages: Array.isArray(data.sourcePackages)
      ? data.sourcePackages.filter((item): item is string => typeof item === 'string')
      : [],
    expiresAt: toNullableString(data.expiresAt),
    daysLeft: data.daysLeft == null ? null : Number(data.daysLeft),
  };
}

export function mapAiPackageCreditSnapshot(data: ApiRecord): AiPackageCreditSnapshot {
  const sharedCredits = Number(data.sharedCredits ?? 0);
  const flexibleCredits = Number(data.flexibleCredits ?? 0);
  const writingOnlyCredits = Number(data.writingOnlyCredits ?? 0);
  const speakingOnlyCredits = Number(data.speakingOnlyCredits ?? 0);
  const creditsRemaining = Number(
    data.creditsRemaining ?? sharedCredits + flexibleCredits + writingOnlyCredits + speakingOnlyCredits,
  );
  return {
    userId: String(data.userId ?? ''),
    sharedCredits,
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
      sourceReferenceId: toNullableString(item.sourceReferenceId),
      description: String(item.description ?? ''),
      validFrom: toNullableString(item.validFrom),
      expiresAt: toNullableString(item.expiresAt),
      createdAt: String(item.createdAt ?? ''),
    })),
    creditsGranted: Number(data.creditsGranted ?? 0),
    creditsUsed: Number(data.creditsUsed ?? 0),
    creditsRemaining,
    writingUnlimited: data.writingUnlimited === true,
    speakingUnlimited: data.speakingUnlimited === true,
    sharedCreditsGranted: Number(data.sharedCreditsGranted ?? 0),
    sharedCreditsUsed: Number(data.sharedCreditsUsed ?? 0),
    buckets: mapBuckets(data.buckets),
    listeningUnlimited: data.listeningUnlimited === true,
    readingUnlimited: data.readingUnlimited === true,
    shared: mapNamedBucket(data.shared),
    flexible: mapNamedBucket(data.flexible),
    writing: mapNamedBucket(data.writing),
    speaking: mapNamedBucket(data.speaking),
    listening: mapNamedBucket(data.listening),
    reading: mapNamedBucket(data.reading),
    mocks: mapNamedBucket(data.mocks),
    activities: asArray(data.activities).map((item) => ({
      id: String(item.id ?? ''),
      title: String(item.title ?? ''),
      subtest: String(item.subtest ?? ''),
      status: String(item.status ?? 'opened'),
      startedAt: String(item.startedAt ?? ''),
      authorizingPackage: toNullableString(item.authorizingPackage),
      creditsUsed: Number(item.creditsUsed ?? 0),
      remainingAfterStart: Number(item.remainingAfterStart ?? 0),
    })),
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
      sourceReferenceId: toNullableString(grant.sourceReferenceId),
      validFrom: toNullableString(grant.validFrom),
      daysLeft: grant.daysLeft == null ? null : Number(grant.daysLeft),
    })),
  }));
}
