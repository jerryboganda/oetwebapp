#!/usr/bin/env bash
# ============================================================================
# install-admin-deps.sh — OET admin redesign foundation dependencies
#
# What this installs
#   - Radix UI primitives that back the shadcn/ui components used by the
#     admin redesign (dialog, dropdown, tooltip, etc.).
#   - Polished UX libraries that the design system standardises on:
#       sonner            → toast notifications
#       vaul              → bottom-sheet drawer (mobile)
#       cmdk              → ⌘K command palette
#       @tanstack/react-table → headless table for admin grids
#
# How to run (remote dev VPS)
#   ssh oet-dev "cd /opt/oetwebapp && bash scripts/install-admin-deps.sh"
#
# How to run (local Windows / WSL)
#   bash scripts/install-admin-deps.sh
#
# Notes
#   - Idempotent: re-running is safe; npm will no-op if versions are already
#     satisfied.
#   - Pinned with caret (^) ranges via npm's default — adjust here if you
#     need exact versions.
# ============================================================================
set -euo pipefail

echo "==> Installing admin redesign dependencies..."

npm install --save \
  @radix-ui/react-dialog \
  @radix-ui/react-alert-dialog \
  @radix-ui/react-dropdown-menu \
  @radix-ui/react-tooltip \
  @radix-ui/react-select \
  @radix-ui/react-tabs \
  @radix-ui/react-checkbox \
  @radix-ui/react-radio-group \
  @radix-ui/react-switch \
  @radix-ui/react-avatar \
  @radix-ui/react-progress \
  @radix-ui/react-label \
  @radix-ui/react-slot \
  @radix-ui/react-separator \
  @radix-ui/react-popover \
  sonner \
  vaul \
  cmdk \
  @tanstack/react-table

echo "==> Done. Restart the dev server (npm run dev) for the new packages to be picked up."
