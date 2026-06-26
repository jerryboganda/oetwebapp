// Authored preview — ErrorState. Each named export = one labeled card cell.
import { ErrorState } from 'oet-prep';

export const LoadResultsFailed = () => (
  <div style={{ maxWidth: 520 }}>
    <ErrorState
      title="Couldn't load your results"
      message="We couldn't reach the marking service. Check your connection and try again — your scores are safe."
      onRetry={() => {}}
    />
  </div>
);

export const DefaultsNoRetry = () => (
  <div style={{ maxWidth: 520 }}>
    {/* No props — exercises the built-in default title and message. */}
    <ErrorState />
  </div>
);
