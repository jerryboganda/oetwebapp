'use client';

import { VocabularyForm, type VocabFormValues } from '../_form';
import { createAdminVocabularyItem } from '@/lib/api';

export default function AdminVocabularyNewPage() {
  async function handleSubmit(values: VocabFormValues) {
    await createAdminVocabularyItem({
      term: values.term,
      definition: values.definition,
      exampleSentence: values.exampleSentence,
      contextNotes: values.contextNotes || undefined,
      examTypeCode: values.examTypeCode,
      professionId: values.professionId || null,
      category: values.category,
      difficulty: values.difficulty,
      ipaPronunciation: values.ipaPronunciation || undefined,
      americanSpelling: values.americanSpelling || undefined,
      audioUrl: values.audioUrl || undefined,
      audioSlowUrl: values.audioSlowUrl || undefined,
      audioSentenceUrl: values.audioSentenceUrl || undefined,
      audioMediaAssetId: values.audioMediaAssetId || undefined,
      imageUrl: values.imageUrl || undefined,
      synonyms: values.synonyms,
      collocations: values.collocations,
      relatedTerms: values.relatedTerms,
      recallSetCodes: values.recallSetCodes,
      commonMistakes: values.commonMistakes,
      similarSounding: values.similarSounding,
      oetSubtestTags: values.oetSubtestTags,
      sourceProvenance: values.sourceProvenance || undefined,
      status: values.status,
    });
  }

  return <VocabularyForm mode="create" onSubmit={handleSubmit} />;
}
