export interface ListeningResultConversionFields {
  maxRawScore: number;
  scaledScore: number | null;
  scoreConversionTableVersionKey?: string | null | undefined;
  passed: boolean | null;
}

/**
 * Owner-table Listening conversion is valid only for the complete 42-item
 * paper. Subset drills must remain raw practice evidence even if a stale or
 * overly broad API payload includes conversion fields.
 */
export function hasApprovedListeningConversion({
  maxRawScore,
  scaledScore,
  scoreConversionTableVersionKey,
  passed,
}: ListeningResultConversionFields): boolean {
  return maxRawScore === 42
    && scaledScore != null
    && scoreConversionTableVersionKey != null
    && passed != null;
}
