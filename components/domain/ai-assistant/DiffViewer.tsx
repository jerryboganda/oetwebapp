'use client';

interface Props {
  oldContent?: string;
  newContent?: string;
  path?: string;
}

// TODO Phase 1: render unified diff (e.g. diff2html or custom) with
// add/remove highlighting. Read-only.
export function DiffViewer(_props: Props): React.JSX.Element {
  return (
    <pre className="overflow-x-auto rounded border border-slate-200 bg-slate-50 p-2 text-xs text-slate-700">
      {/* TODO Phase 1: diff output */}
      diff viewer (not implemented)
    </pre>
  );
}
