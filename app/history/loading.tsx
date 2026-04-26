export default function HistoryLoading() {
  return (
    <div className="container mx-auto px-4 py-8">
      <div className="space-y-4">
        <div className="h-8 w-48 animate-pulse rounded bg-muted" />
        <div className="h-4 w-72 animate-pulse rounded bg-muted/70" />
        <div className="mt-6 grid gap-3">
          {Array.from({ length: 6 }).map((_, i) => (
            <div key={i} className="h-20 animate-pulse rounded-lg bg-muted/60" />
          ))}
        </div>
      </div>
    </div>
  );
}
