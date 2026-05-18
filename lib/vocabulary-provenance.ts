const GENERATED_PREFIXES = ['generated:', 'ai-author:'] as const;

export function isGeneratedVocabularyProvenance(sourceProvenance?: string | null) {
  const value = sourceProvenance?.trim().toLowerCase();
  return Boolean(value && GENERATED_PREFIXES.some((prefix) => value.startsWith(prefix)));
}

export function vocabularyProvenanceLabel(sourceProvenance?: string | null) {
  return isGeneratedVocabularyProvenance(sourceProvenance) ? 'Platform-authored practice content' : null;
}
