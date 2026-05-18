import { describe, expect, it } from 'vitest';
import { isGeneratedVocabularyProvenance, vocabularyProvenanceLabel } from './vocabulary-provenance';

describe('vocabulary provenance helpers', () => {
  it('labels generated platform-authored provenance', () => {
    expect(isGeneratedVocabularyProvenance('generated:platform-authored:recalls-content-pack-v1')).toBe(true);
    expect(vocabularyProvenanceLabel('generated:platform-authored:recalls-content-pack-v1')).toBe(
      'Platform-authored practice content',
    );
  });

  it('labels legacy ai-author provenance as generated content', () => {
    expect(vocabularyProvenanceLabel('ai-author:claude-opus-4.7:v1')).toBe('Platform-authored practice content');
  });

  it('labels accepted AI drafts using the platform-authored prefix', () => {
    expect(vocabularyProvenanceLabel('generated:platform-authored:ai-draft:admin-reviewed:v1;reviewedAt=2026-05-18')).toBe(
      'Platform-authored practice content',
    );
  });

  it('does not label source-backed or blank provenance as generated', () => {
    expect(vocabularyProvenanceLabel('admin-reviewed-source:paper-123')).toBeNull();
    expect(vocabularyProvenanceLabel('')).toBeNull();
    expect(vocabularyProvenanceLabel(null)).toBeNull();
  });
});
