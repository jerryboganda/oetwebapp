'use client';

// Mount point intended to be added once to app/layout.tsx.
//
// REQUIRED ONE-LINE EDIT in app/layout.tsx (Phase 1):
//   inside the <body> JSX, just before </body>:
//     <AiAssistantWidgetMount />
//
// Keeping the widget behind this wrapper means app/layout.tsx only
// imports a single named component, so the integration touches one
// line and reverts cleanly if the feature is disabled.

import { AiAssistantWidget } from './AiAssistantWidget';

export function AiAssistantWidgetMount(): React.JSX.Element {
  return <AiAssistantWidget />;
}
