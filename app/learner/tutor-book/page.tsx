'use client';

import { useEffect, useState } from 'react';
import { BookOpen, Download, ExternalLink, Headphones, Megaphone } from 'lucide-react';
import {
  fetchTutorBookAudioScripts,
  fetchTutorBookUpdates,
  fetchTutorBookTelegram,
  tutorBookDownloadUrl,
  type TutorBookAudioScript,
  type TutorBookUpdate,
} from '@/lib/api';
import { useAuth } from '@/contexts/auth-context';

type Tab = 'reader' | 'audio' | 'updates';

export default function TutorBookPage() {
  const { user } = useAuth();
  const [tab, setTab] = useState<Tab>('reader');
  const [audio, setAudio] = useState<TutorBookAudioScript[]>([]);
  const [updates, setUpdates] = useState<TutorBookUpdate[]>([]);
  const [telegramUrl, setTelegramUrl] = useState<string | null>(null);
  const [forbidden, setForbidden] = useState(false);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!user) return;
    void (async () => {
      try {
        const [audioRes, updatesRes, telegramRes] = await Promise.all([
          fetchTutorBookAudioScripts().catch(() => []),
          fetchTutorBookUpdates().catch(() => []),
          fetchTutorBookTelegram().catch(() => ({ inviteUrl: null })),
        ]);
        setAudio(audioRes);
        setUpdates(updatesRes);
        setTelegramUrl(telegramRes.inviteUrl);
        setForbidden(false);
      } catch (err) {
        if (err instanceof Error && err.message.toLowerCase().includes('forbid')) {
          setForbidden(true);
        }
      } finally {
        setLoading(false);
      }
    })();
  }, [user]);

  if (!user) {
    return (
      <div className="min-h-screen bg-background p-12">
        <div className="mx-auto max-w-2xl rounded-2xl border border-border bg-surface p-8 text-center">
          <h1 className="text-xl font-bold text-navy">Sign in to read The Tutor Book</h1>
          <p className="mt-2 text-sm text-muted">This module is available only to buyers with an active enrolment.</p>
        </div>
      </div>
    );
  }

  if (forbidden) {
    return (
      <div className="min-h-screen bg-background p-12">
        <div className="mx-auto max-w-2xl rounded-2xl border border-border bg-surface p-8 text-center">
          <BookOpen className="mx-auto h-12 w-12 text-muted opacity-50" />
          <h1 className="mt-4 text-xl font-bold text-navy">The Tutor Book is locked</h1>
          <p className="mt-2 text-sm text-muted">
            Purchase The Tutor Book (£45) or the £32 add-on alongside an eligible course to unlock the reader, audio scripts and updates.
          </p>
          <a
            href="/marketplace/packages/tutor-book"
            className="mt-6 inline-flex items-center gap-2 rounded-lg bg-primary px-4 py-2 text-sm font-medium text-white dark:bg-violet-700"
          >
            View The Tutor Book
          </a>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-background text-navy">
      <header className="border-b border-border bg-gradient-to-r from-[#0E2841] to-[#156082] px-6 py-8 text-white">
        <div className="mx-auto flex max-w-6xl flex-wrap items-end justify-between gap-4">
          <div>
            <p className="text-xs font-semibold uppercase tracking-[0.3em] text-[#D4A44F]">OET 2026 · Reader</p>
            <h1 className="mt-1 text-3xl font-bold">The Tutor Book: First Edition 2026</h1>
            <p className="mt-1 text-sm text-white/75">Personalised PDF, audio scripts and live updates.</p>
          </div>
          <a
            href={tutorBookDownloadUrl()}
            target="_blank"
            rel="noopener noreferrer"
            className="inline-flex items-center gap-2 rounded-lg bg-[#D4A44F] px-4 py-2 text-sm font-bold text-[#0E2841] transition-colors hover:bg-[#bf8e3d]"
          >
            <Download className="h-4 w-4" /> Download my copy
          </a>
        </div>
      </header>

      <nav className="border-b border-border bg-surface">
        <div className="mx-auto flex max-w-6xl gap-1 px-6">
          <TabButton active={tab === 'reader'} onClick={() => setTab('reader')} icon={<BookOpen className="h-4 w-4" />} label="Reader" />
          <TabButton active={tab === 'audio'} onClick={() => setTab('audio')} icon={<Headphones className="h-4 w-4" />} label={`Audio Scripts (${audio.length})`} />
          <TabButton active={tab === 'updates'} onClick={() => setTab('updates')} icon={<Megaphone className="h-4 w-4" />} label={`Updates (${updates.length})`} />
        </div>
      </nav>

      <main className="mx-auto max-w-6xl px-6 py-8">
        {loading ? (
          <div className="h-96 animate-pulse rounded-xl bg-surface" />
        ) : tab === 'reader' ? (
          <ReaderTab buyerEmail={user.email ?? ''} buyerName={(user as { name?: string }).name ?? ''} telegramUrl={telegramUrl} />
        ) : tab === 'audio' ? (
          <AudioTab audio={audio} />
        ) : (
          <UpdatesTab updates={updates} />
        )}
      </main>
    </div>
  );
}

function TabButton({ active, onClick, icon, label }: { active: boolean; onClick: () => void; icon: React.ReactNode; label: string }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`inline-flex items-center gap-2 border-b-2 px-4 py-3 text-sm font-medium transition-colors ${
        active ? 'border-[#D4A44F] text-[#0E2841]' : 'border-transparent text-muted hover:text-navy'
      }`}
    >
      {icon} {label}
    </button>
  );
}

function ReaderTab({ buyerEmail, buyerName, telegramUrl }: { buyerEmail: string; buyerName: string; telegramUrl: string | null }) {
  return (
    <div className="space-y-6">
      <div className="relative rounded-2xl border border-border bg-surface shadow-sm">
        {/* Server-side PDF served watermarked with buyer name + email + HMAC signature */}
        <iframe
          src={tutorBookDownloadUrl()}
          title="The Tutor Book PDF"
          className="h-[80vh] w-full rounded-2xl"
        />
        {/* Defense-in-depth: tiled CSS overlay watermark. Pointer-events disabled so it doesn't block scrolling. */}
        <div className="pointer-events-none absolute inset-0 select-none overflow-hidden rounded-2xl">
          <div className="absolute inset-0 grid grid-cols-3 grid-rows-6 gap-12 opacity-[0.05]">
            {Array.from({ length: 18 }).map((_, i) => (
              <span
                key={i}
                className="rotate-[-30deg] whitespace-nowrap text-center text-xs font-semibold text-[#0E2841]"
              >
                {buyerName || 'OET 2026'} · {buyerEmail || 'tutor-book'}
              </span>
            ))}
          </div>
        </div>
      </div>

      {telegramUrl && (
        <a
          href={telegramUrl}
          target="_blank"
          rel="noopener noreferrer"
          className="inline-flex items-center gap-2 rounded-lg border border-border bg-surface px-4 py-2 text-sm font-medium text-navy hover:bg-background-light"
        >
          <ExternalLink className="h-4 w-4" /> Join the private Telegram channel
        </a>
      )}
    </div>
  );
}

function AudioTab({ audio }: { audio: TutorBookAudioScript[] }) {
  if (audio.length === 0) {
    return (
      <div className="rounded-2xl border border-border bg-surface p-8 text-center text-sm text-muted">
        Audio scripts will appear here as soon as the content team publishes them.
      </div>
    );
  }
  return (
    <ul className="space-y-3">
      {audio.map((script, i) => (
        <li key={`${script.chapter}-${i}`} className="rounded-2xl border border-border bg-surface p-5">
          <div className="flex flex-wrap items-start justify-between gap-2">
            <div>
              <p className="text-[10px] uppercase tracking-wider text-muted">Chapter {script.chapter}</p>
              <h3 className="text-base font-bold">{script.title}</h3>
            </div>
            {script.transcriptUrl && (
              <a href={script.transcriptUrl} className="text-xs font-medium text-primary hover:underline">
                Transcript
              </a>
            )}
          </div>
          <audio controls src={script.audioUrl} className="mt-3 w-full" preload="none" />
        </li>
      ))}
    </ul>
  );
}

function UpdatesTab({ updates }: { updates: TutorBookUpdate[] }) {
  if (updates.length === 0) {
    return (
      <div className="rounded-2xl border border-border bg-surface p-8 text-center text-sm text-muted">
        No updates yet. New recalls and content amendments will appear here.
      </div>
    );
  }
  return (
    <ul className="space-y-4">
      {updates.map((update) => (
        <li key={update.id} className="rounded-2xl border border-border bg-surface p-5">
          <header className="flex flex-wrap items-baseline justify-between gap-2">
            <h3 className="text-base font-bold">{update.title}</h3>
            <time className="text-xs text-muted">{new Date(update.publishedAt).toLocaleDateString()}</time>
          </header>
          <p className="mt-2 whitespace-pre-line text-sm text-navy/90">{update.bodyMarkdown}</p>
          <p className="mt-2 text-[10px] uppercase tracking-wider text-muted">Audience: {update.audience}</p>
        </li>
      ))}
    </ul>
  );
}
