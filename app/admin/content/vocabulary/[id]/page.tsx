'use client';

import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import { VocabularyForm, type VocabFormValues } from '../_form';
import {
  fetchAdminVocabularyItem,
  updateAdminVocabularyItem,
} from '@/lib/api';
import { Skeleton } from '@/components/admin/ui/skeleton';

type Detail = {
  id: string;
  term: string;
  definition: string;
  exampleSentence: string;
  contextNotes: string | null;
  examTypeCode: string;
  professionId: string | null;
  category: string;
  ipaPronunciation: string | null;
  americanSpelling: string | null;
  audioUrl: string | null;
  audioSlowUrl: string | null;
  audioSentenceUrl: string | null;
  audioMediaAssetId: string | null;
  imageUrl: string | null;
  synonymsJson: string;
  collocationsJson: string;
  relatedTermsJson: string;
  recallSetCodesJson: string;
  commonMistakesJson: string;
  similarSoundingJson: string;
  sourceProvenance: string | null;
  isFreePreview?: boolean;
  status: 'draft' | 'active' | 'archived';
};

function parseJsonArray(raw: string | undefined | null): string[] {
  if (!raw) return [];
  try {
    const v = JSON.parse(raw);
    return Array.isArray(v) ? v.filter((x): x is string => typeof x === 'string') : [];
  } catch { return []; }
}

export default function AdminVocabularyEditPage() {
  const params = useParams();
  const id = typeof params?.id === 'string' ? params.id : Array.isArray(params?.id) ? params.id[0] : '';
  const [detail, setDetail] = useState<Detail | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!id) return;
    void (async () => {
      try {
        const d = await fetchAdminVocabularyItem(id);
        setDetail(d as Detail);
      } finally {
        setLoading(false);
      }
    })();
  }, [id]);

  if (loading) {
    return (
      <>
        <Skeleton className="h-8 w-48 rounded-admin-sm" />
        <Skeleton className="mt-4 h-96 rounded-admin-lg" />
      </>
    );
  }

  if (!detail) {
    return (
      <>
        <div className="p-8 text-center text-admin-fg-muted">Term not found.</div>
      </>
    );
  }

  const initial: Partial<VocabFormValues> = {
    term: detail.term,
    definition: detail.definition,
    exampleSentence: detail.exampleSentence,
    contextNotes: detail.contextNotes ?? '',
    examTypeCode: detail.examTypeCode,
    professionId: detail.professionId ?? '',
    category: detail.category,
    ipaPronunciation: detail.ipaPronunciation ?? '',
    americanSpelling: detail.americanSpelling ?? '',
    audioUrl: detail.audioUrl ?? '',
    audioSlowUrl: detail.audioSlowUrl ?? '',
    audioSentenceUrl: detail.audioSentenceUrl ?? '',
    audioMediaAssetId: detail.audioMediaAssetId ?? '',
    imageUrl: detail.imageUrl ?? '',
    synonyms: parseJsonArray(detail.synonymsJson),
    collocations: parseJsonArray(detail.collocationsJson),
    relatedTerms: parseJsonArray(detail.relatedTermsJson),
    recallSetCodes: parseJsonArray(detail.recallSetCodesJson),
    commonMistakes: parseJsonArray(detail.commonMistakesJson),
    similarSounding: parseJsonArray(detail.similarSoundingJson),
    sourceProvenance: detail.sourceProvenance ?? '',
    isFreePreview: detail.isFreePreview ?? false,
    status: detail.status,
  };

  async function handleSubmit(values: VocabFormValues) {
    await updateAdminVocabularyItem(id, {
      term: values.term,
      definition: values.definition,
      exampleSentence: values.exampleSentence,
      contextNotes: values.contextNotes,
      examTypeCode: values.examTypeCode,
      professionId: values.professionId || null,
      category: values.category,
      ipaPronunciation: values.ipaPronunciation,
      americanSpelling: values.americanSpelling,
      audioUrl: values.audioUrl,
      audioSlowUrl: values.audioSlowUrl,
      audioSentenceUrl: values.audioSentenceUrl,
      audioMediaAssetId: values.audioMediaAssetId,
      imageUrl: values.imageUrl,
      synonyms: values.synonyms,
      collocations: values.collocations,
      relatedTerms: values.relatedTerms,
      recallSetCodes: values.recallSetCodes,
      commonMistakes: values.commonMistakes,
      similarSounding: values.similarSounding,
      sourceProvenance: values.sourceProvenance,
      isFreePreview: values.isFreePreview,
      status: values.status,
    });
  }

  async function handlePublish(values: VocabFormValues) {
    await updateAdminVocabularyItem(id, {
      term: values.term,
      definition: values.definition,
      exampleSentence: values.exampleSentence,
      contextNotes: values.contextNotes,
      examTypeCode: values.examTypeCode,
      professionId: values.professionId || null,
      category: values.category,
      ipaPronunciation: values.ipaPronunciation,
      americanSpelling: values.americanSpelling,
      audioUrl: values.audioUrl,
      audioSlowUrl: values.audioSlowUrl,
      audioSentenceUrl: values.audioSentenceUrl,
      audioMediaAssetId: values.audioMediaAssetId,
      imageUrl: values.imageUrl,
      synonyms: values.synonyms,
      collocations: values.collocations,
      relatedTerms: values.relatedTerms,
      recallSetCodes: values.recallSetCodes,
      commonMistakes: values.commonMistakes,
      similarSounding: values.similarSounding,
      sourceProvenance: values.sourceProvenance,
      isFreePreview: values.isFreePreview,
      status: 'active',
    });
    setDetail(prev => prev ? { ...prev, isFreePreview: values.isFreePreview, status: 'active' } : prev);
  }

  return (
    <VocabularyForm
      mode="edit"
      initial={initial}
      onSubmit={handleSubmit}
      onPublish={handlePublish}
      itemId={id}
    />
  );
}
