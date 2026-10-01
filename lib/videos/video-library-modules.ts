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
  hoverBorder: string;
  accentText: string;
};

// Sub-test identity tokens (DESIGN.md §2): they flip with the theme, so no
// dark: twins. Basic English is not a sub-test and stays neutral.
export const VIDEO_LIBRARY_MODULES: VideoModuleTheme[] = [
  {
    key: 'listening',
    label: 'Listening',
    icon: Headphones,
    iconWrap: 'bg-skill-listening/10 text-skill-listening',
    hoverBorder: 'hover:border-skill-listening/40',
    accentText: 'text-skill-listening',
  },
  {
    key: 'reading',
    label: 'Reading',
    icon: BookOpen,
    iconWrap: 'bg-skill-reading/10 text-skill-reading',
    hoverBorder: 'hover:border-skill-reading/40',
    accentText: 'text-skill-reading',
  },
  {
    key: 'writing',
    label: 'Writing',
    icon: PenLine,
    iconWrap: 'bg-skill-writing/10 text-skill-writing',
    hoverBorder: 'hover:border-skill-writing/40',
    accentText: 'text-skill-writing',
  },
  {
    key: 'speaking',
    label: 'Speaking',
    icon: Mic,
    iconWrap: 'bg-skill-speaking/10 text-skill-speaking',
    hoverBorder: 'hover:border-skill-speaking/40',
    accentText: 'text-skill-speaking',
  },
  {
    key: 'basic-english',
    label: 'Basic English Course',
    icon: GraduationCap,
    iconWrap: 'bg-background-light text-muted',
    hoverBorder: 'hover:border-border-hover',
    accentText: 'text-muted',
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
  }).filter((module) => module.videoCount > 0);
}
