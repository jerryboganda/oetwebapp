import type { LucideIcon } from 'lucide-react';
import { BookOpen, GraduationCap, Headphones, Mic, PenLine } from 'lucide-react';
import type { VideoLibraryCategory } from '@/lib/types/videos';

export type VideoModuleKey =
  | 'listening'
  | 'reading'
  | 'writing'
  | 'speaking'
  | 'basic-english';

export type VideoModuleTheme = {
  key: VideoModuleKey;
  label: string;
  icon: LucideIcon;
  iconWrap: string;
  gradient: string;
  hoverBorder: string;
  accentText: string;
};

export const VIDEO_LIBRARY_MODULES: VideoModuleTheme[] = [
  {
    key: 'listening',
    label: 'Listening',
    icon: Headphones,
    iconWrap: 'bg-sky-100 text-sky-600 dark:bg-sky-900/50 dark:text-sky-300',
    gradient: 'from-sky-50/80 to-surface dark:from-sky-950/30 dark:to-surface',
    hoverBorder: 'hover:border-sky-300 dark:hover:border-sky-700',
    accentText: 'text-sky-600 dark:text-sky-300',
  },
  {
    key: 'reading',
    label: 'Reading',
    icon: BookOpen,
    iconWrap: 'bg-emerald-100 text-emerald-600 dark:bg-emerald-900/50 dark:text-emerald-300',
    gradient: 'from-emerald-50/80 to-surface dark:from-emerald-950/30 dark:to-surface',
    hoverBorder: 'hover:border-emerald-300 dark:hover:border-emerald-700',
    accentText: 'text-emerald-600 dark:text-emerald-300',
  },
  {
    key: 'writing',
    label: 'Writing',
    icon: PenLine,
    iconWrap: 'bg-violet-100 text-violet-600 dark:bg-violet-900/50 dark:text-violet-300',
    gradient: 'from-violet-50/80 to-surface dark:from-violet-950/30 dark:to-surface',
    hoverBorder: 'hover:border-violet-300 dark:hover:border-violet-700',
    accentText: 'text-violet-600 dark:text-violet-300',
  },
  {
    key: 'speaking',
    label: 'Speaking',
    icon: Mic,
    iconWrap: 'bg-rose-100 text-rose-600 dark:bg-rose-900/50 dark:text-rose-300',
    gradient: 'from-rose-50/80 to-surface dark:from-rose-950/30 dark:to-surface',
    hoverBorder: 'hover:border-rose-300 dark:hover:border-rose-700',
    accentText: 'text-rose-600 dark:text-rose-300',
  },
  {
    key: 'basic-english',
    label: 'Basic English Course',
    icon: GraduationCap,
    iconWrap: 'bg-slate-100 text-slate-600 dark:bg-slate-900/50 dark:text-slate-300',
    gradient: 'from-slate-50/80 to-surface dark:from-slate-950/30 dark:to-surface',
    hoverBorder: 'hover:border-slate-300 dark:hover:border-slate-700',
    accentText: 'text-slate-600 dark:text-slate-300',
  },
];

const BASIC_ENGLISH_TITLE_RE = /basic\s*english|general\s*english/;

export function isBasicEnglishSubtest(subtestCode: string | null | undefined): boolean {
  const code = subtestCode?.trim().toLowerCase() ?? '';
  return code === 'basic-english' || code === 'general' || code === 'general-english' || code === 'general_english';
}

export function moduleKeyOf(title: string): string {
  const first = (title.split('/')[0] ?? '').trim().toLowerCase();
  if (BASIC_ENGLISH_TITLE_RE.test(first) || isBasicEnglishSubtest(first)) return 'basic-english';
  return first;
}

export function subTitleOf(title: string): string {
  const parts = title.split('/').map((part) => part.trim()).filter(Boolean);
  return parts.slice(1).join(' / ') || 'General';
}

export function groupVideoCategoriesByModule(
  categories: VideoLibraryCategory[],
  videosOf: (category: VideoLibraryCategory) => VideoLibraryCategory['videos'],
): Array<{ meta: VideoModuleTheme; categories: VideoLibraryCategory[]; videoCount: number }> {
  const byModule = new Map<string, VideoLibraryCategory[]>();
  for (const category of categories) {
    const videos = videosOf(category);
    if (videos.length === 0) continue;
    const key = moduleKeyOf(category.title);
    const scoped = { ...category, videos };
    const bucket = byModule.get(key);
    if (bucket) bucket.push(scoped);
    else byModule.set(key, [scoped]);
  }

  return VIDEO_LIBRARY_MODULES.map((meta) => {
    const moduleCategories = (byModule.get(meta.key) ?? []).sort((a, b) =>
      subTitleOf(a.title).localeCompare(subTitleOf(b.title)),
    );
    const videoCount = moduleCategories.reduce((sum, category) => sum + category.videos.length, 0);
    return { meta, categories: moduleCategories, videoCount };
  }).filter((module) => module.videoCount > 0 || module.meta.key === 'basic-english');
}
