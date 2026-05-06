'use client';

import { useCallback, useEffect, useState } from 'react';
import {
  fetchExpertMessageThreads,
  createExpertMessageThread,
  fetchExpertMessageThread as fetchThread,
  postExpertMessageReply,
} from '@/lib/api';
import type { ExpertMessageThread, ExpertMessageThreadDetail } from '@/lib/types/expert';

export function useExpertMessageThreads() {
  const [threads, setThreads] = useState<ExpertMessageThread[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await fetchExpertMessageThreads() as ExpertMessageThread[];
      setThreads(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load messages');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  return { threads, loading, error, refresh: load };
}

export function useExpertMessageThread(threadId: string) {
  const [thread, setThread] = useState<ExpertMessageThreadDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!threadId) return;
    setLoading(true);
    setError(null);
    try {
      const data = await fetchThread(threadId) as ExpertMessageThreadDetail;
      setThread(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load thread');
    } finally {
      setLoading(false);
    }
  }, [threadId]);

  useEffect(() => { load(); }, [load]);

  return { thread, loading, error, refresh: load };
}

export function useCreateExpertThread() {
  const [saving, setSaving] = useState(false);

  const create = useCallback(async (payload: {
    title: string; body: string; linkedReviewRequestId?: string;
    linkedCalibrationCaseId?: string; linkedLearnerId?: string;
  }) => {
    setSaving(true);
    try {
      return await createExpertMessageThread(payload) as ExpertMessageThreadDetail;
    } finally {
      setSaving(false);
    }
  }, []);

  return { create, saving };
}

export function usePostExpertReply() {
  const [sending, setSending] = useState(false);

  const reply = useCallback(async (threadId: string, body: string) => {
    setSending(true);
    try {
      return await postExpertMessageReply(threadId, body);
    } finally {
      setSending(false);
    }
  }, []);

  return { reply, sending };
}
