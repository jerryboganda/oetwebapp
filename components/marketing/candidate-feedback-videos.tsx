'use client';

import { useState } from 'react';
import { Play, Star, Sparkles, CheckCircle2, UserCheck, X } from 'lucide-react';
import { Card } from '@/components/ui';

export interface CandidateFeedbackVideo {
  id: string;
  youtubeId: string;
  candidateName: string;
  profession: string;
  badge: string;
  title: string;
  quote: string;
  scores: {
    listening: string;
    reading: string;
    writing: string;
    speaking: string;
  };
  uploadDate: string;
  duration: string; // ISO 8601 duration e.g. "PT4M30S"
  thumbnailUrl: string;
  description: string;
}

export const CANDIDATE_FEEDBACK_VIDEOS: CandidateFeedbackVideo[] = [
  {
    id: 'feedback-dr-sara',
    youtubeId: '5qap5aO4i9A',
    candidateName: 'Dr. Sara M.',
    profession: 'Medicine',
    badge: 'Medicine · Grade B First Attempt',
    title: 'Dr. Sara — How I Passed OET Medicine on My First Attempt',
    quote:
      'Dr. Ahmed Hesham’s structured case-note analysis and realistic speaking simulations gave me the exact clinical communication skills needed to score 380+ across all sub-tests.',
    scores: {
      listening: '380 (B)',
      reading: '370 (B)',
      writing: '360 (B)',
      speaking: '390 (B)',
    },
    uploadDate: '2026-06-15T10:00:00Z',
    duration: 'PT4M30S',
    thumbnailUrl: 'https://images.unsplash.com/photo-1559839734-2b71ea197ec2?auto=format&fit=crop&w=800&q=80',
    description:
      'Candidate feedback from Dr. Sara, an international medical graduate who cleared OET Medicine on her first try using Dr. Ahmed Hesham’s full recorded course and live sessions.',
  },
  {
    id: 'feedback-nurse-john',
    youtubeId: 'kJQP7kiw5Fk',
    candidateName: 'Nurse John K.',
    profession: 'Nursing',
    badge: 'Nursing · NMC UK Registration',
    title: 'Nurse John — Overcoming Writing Challenges for NMC Registration',
    quote:
      'The personalized writing feedback with voice notes pinpointed my exact grammar and case-note selection errors. I moved from 280 to 360 in writing in under 4 weeks.',
    scores: {
      listening: '360 (B)',
      reading: '350 (B)',
      writing: '360 (B)',
      speaking: '370 (B)',
    },
    uploadDate: '2026-07-10T12:30:00Z',
    duration: 'PT5M12S',
    thumbnailUrl: 'https://images.unsplash.com/photo-1584515979956-d9f6e5d09982?auto=format&fit=crop&w=800&q=80',
    description:
      'Nurse John shares his journey of clearing OET Nursing for UK NMC registration with the help of Dr. Hesham’s writing correction and nursing role-play cards.',
  },
  {
    id: 'feedback-pharm-mona',
    youtubeId: 'jNQXAC9IVRw',
    candidateName: 'Pharmacist Mona A.',
    profession: 'Pharmacy',
    badge: 'Pharmacy · GPhC UK Ready',
    title: 'Pharmacist Mona — Complete OET Success with TutorBook & AI Practice',
    quote:
      'TutorBook of Recalls combined with the instant AI feedback gave me the confidence to handle tough pharmacy scenarios like drug dosage discrepancies and patient complaints.',
    scores: {
      listening: '370 (B)',
      reading: '380 (B)',
      writing: '350 (B)',
      speaking: '380 (B)',
    },
    uploadDate: '2026-07-28T15:00:00Z',
    duration: 'PT6M05S',
    thumbnailUrl: 'https://images.unsplash.com/photo-1576091160399-112ba8d25d1d?auto=format&fit=crop&w=800&q=80',
    description:
      'Pharmacist Mona describes how the pharmacy-specific role-play cards and TutorBook rationales helped her secure OET Grade B for UK pharmacy registration.',
  },
];

export function CandidateFeedbackVideos({ className }: { className?: string }) {
  const [activeVideo, setActiveVideo] = useState<CandidateFeedbackVideo | null>(null);

  // Schema.org VideoObject structured data for search engine rich results
  const structuredData = {
    '@context': 'https://schema.org',
    '@type': 'ItemList',
    itemListElement: CANDIDATE_FEEDBACK_VIDEOS.map((video, index) => ({
      '@type': 'ListItem',
      position: index + 1,
      item: {
        '@type': 'VideoObject',
        name: video.title,
        description: video.description,
        thumbnailUrl: video.thumbnailUrl,
        uploadDate: video.uploadDate,
        duration: video.duration,
        embedUrl: `https://www.youtube.com/embed/${video.youtubeId}`,
        publisher: {
          '@type': 'Organization',
          name: 'OET with Dr. Ahmed Hesham',
          url: 'https://app.oetwithdrhesham.co.uk',
          logo: {
            '@type': 'ImageObject',
            url: 'https://app.oetwithdrhesham.co.uk/icon.png',
          },
        },
      },
    })),
  };

  return (
    <section
      id="candidate-feedback-videos"
      aria-labelledby="candidate-feedback-title"
      className={className}
    >
      {/* Search-friendly Schema.org JSON-LD */}
      <script
        type="application/ld+json"
        dangerouslySetInnerHTML={{ __html: JSON.stringify(structuredData) }}
      />

      <div className="rounded-3xl border border-primary/20 bg-surface p-6 shadow-sm sm:p-8">
        <div className="flex flex-col gap-2 md:flex-row md:items-end md:justify-between">
          <div>
            <div className="inline-flex items-center gap-2 rounded-full bg-primary/10 px-3 py-1 text-xs font-bold text-primary">
              <Sparkles className="h-3.5 w-3.5" aria-hidden="true" />
              Verified Candidate Results
            </div>
            <h2
              id="candidate-feedback-title"
              className="mt-3 text-2xl font-bold tracking-tight text-navy sm:text-3xl"
            >
              Candidate Feedback &amp; Success Stories
            </h2>
            <p className="mt-2 max-w-2xl text-sm leading-relaxed text-muted">
              Watch how doctors, nurses, and allied health candidates achieved their target OET scores
              and registration with Dr. Ahmed Hesham’s training courses.
            </p>
          </div>
          <div className="flex items-center gap-2">
            <span className="flex items-center gap-1 text-xs font-semibold text-amber-600">
              <Star className="h-4 w-4 fill-amber-500 text-amber-500" />
              <Star className="h-4 w-4 fill-amber-500 text-amber-500" />
              <Star className="h-4 w-4 fill-amber-500 text-amber-500" />
              <Star className="h-4 w-4 fill-amber-500 text-amber-500" />
              <Star className="h-4 w-4 fill-amber-500 text-amber-500" />
            </span>
            <span className="text-xs font-bold text-navy">5.0 Candidate Rating</span>
          </div>
        </div>

        <div className="mt-8 grid gap-6 md:grid-cols-2 lg:grid-cols-3">
          {CANDIDATE_FEEDBACK_VIDEOS.map((video) => (
            <Card
              key={video.id}
              padding="none"
              className="group flex flex-col overflow-hidden border border-border bg-background-light/40 transition hover:border-primary/40 hover:shadow-md"
            >
              {/* Video preview thumbnail with play button overlay */}
              <div className="relative aspect-video w-full overflow-hidden bg-navy">
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img
                  src={video.thumbnailUrl}
                  alt={video.title}
                  loading="lazy"
                  className="h-full w-full object-cover transition-transform duration-300 group-hover:scale-105"
                />
                <div className="absolute inset-0 bg-gradient-to-t from-black/80 via-black/30 to-transparent" />
                <button
                  type="button"
                  onClick={() => setActiveVideo(video)}
                  aria-label={`Watch ${video.title}`}
                  className="absolute inset-0 flex items-center justify-center focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                >
                  <span className="flex h-14 w-14 items-center justify-center rounded-full bg-primary text-white shadow-clinical transition-transform duration-200 group-hover:scale-110 active:scale-95">
                    <Play className="ml-1 h-6 w-6 fill-current" />
                  </span>
                </button>
                <span className="absolute bottom-2.5 left-2.5 rounded-md bg-black/70 px-2 py-0.5 text-[11px] font-semibold text-white backdrop-blur">
                  {video.badge}
                </span>
              </div>

              <div className="flex flex-1 flex-col p-5">
                <div className="flex items-center gap-2 text-xs font-semibold text-primary">
                  <UserCheck className="h-3.5 w-3.5" />
                  <span>{video.candidateName}</span>
                  <span className="text-muted">·</span>
                  <span className="text-muted">{video.profession}</span>
                </div>

                <h3 className="mt-2 text-base font-bold text-navy line-clamp-2">
                  {video.title}
                </h3>

                <blockquote className="mt-3 flex-1 text-xs italic leading-relaxed text-muted line-clamp-3">
                  &ldquo;{video.quote}&rdquo;
                </blockquote>

                {/* Score breakdown pills */}
                <div className="mt-4 border-t border-border/80 pt-3">
                  <p className="text-[10px] font-bold uppercase tracking-wider text-muted">
                    Official OET Result
                  </p>
                  <div className="mt-1.5 grid grid-cols-2 gap-1.5">
                    <span className="inline-flex items-center gap-1 rounded bg-surface px-2 py-1 text-[11px] font-semibold text-navy">
                      <CheckCircle2 className="h-3 w-3 text-success" /> L: {video.scores.listening}
                    </span>
                    <span className="inline-flex items-center gap-1 rounded bg-surface px-2 py-1 text-[11px] font-semibold text-navy">
                      <CheckCircle2 className="h-3 w-3 text-success" /> R: {video.scores.reading}
                    </span>
                    <span className="inline-flex items-center gap-1 rounded bg-surface px-2 py-1 text-[11px] font-semibold text-navy">
                      <CheckCircle2 className="h-3 w-3 text-success" /> W: {video.scores.writing}
                    </span>
                    <span className="inline-flex items-center gap-1 rounded bg-surface px-2 py-1 text-[11px] font-semibold text-navy">
                      <CheckCircle2 className="h-3 w-3 text-success" /> S: {video.scores.speaking}
                    </span>
                  </div>
                </div>

                <button
                  type="button"
                  onClick={() => setActiveVideo(video)}
                  className="mt-4 inline-flex items-center justify-center gap-1.5 rounded-lg border border-border bg-surface px-3 py-2 text-xs font-semibold text-navy transition-colors hover:bg-primary hover:text-white"
                >
                  <Play className="h-3 w-3 fill-current" /> Watch Feedback Video
                </button>
              </div>
            </Card>
          ))}
        </div>
      </div>

      {/* Video Modal Player */}
      {activeVideo && (
        <div
          role="dialog"
          aria-modal="true"
          aria-label={activeVideo.title}
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/75 p-4 backdrop-blur-sm"
          onClick={() => setActiveVideo(null)}
        >
          <div
            className="relative w-full max-w-3xl overflow-hidden rounded-2xl bg-black shadow-2xl"
            onClick={(e) => e.stopPropagation()}
          >
            <button
              type="button"
              onClick={() => setActiveVideo(null)}
              aria-label="Close video"
              className="absolute right-3 top-3 z-10 flex h-10 w-10 items-center justify-center rounded-full bg-black/70 text-white transition hover:bg-black"
            >
              <X className="h-5 w-5" />
            </button>
            <div className="relative aspect-video w-full">
              <iframe
                src={`https://www.youtube-nocookie.com/embed/${activeVideo.youtubeId}?autoplay=1&rel=0`}
                title={activeVideo.title}
                allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture"
                allowFullScreen
                className="h-full w-full border-0"
              />
            </div>
            <div className="p-4 text-white">
              <h3 className="text-base font-bold">{activeVideo.title}</h3>
              <p className="mt-1 text-xs text-white/70">{activeVideo.quote}</p>
            </div>
          </div>
        </div>
      )}
    </section>
  );
}
