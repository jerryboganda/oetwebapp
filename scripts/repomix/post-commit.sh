#!/usr/bin/env sh
# Auto-installed by scripts/repomix/install-hook.ps1
# Regenerates repomix-output.xml in the background after every commit so
# GitHub Copilot / agentic AIs always see the latest packed bundle.

# Skip during interactive rebases / merges
if [ -f "$(git rev-parse --git-dir)/MERGE_HEAD" ] || [ -f "$(git rev-parse --git-dir)/REBASE_HEAD" ]; then
  exit 0
fi

# Only run if repomix is available
if ! command -v npx >/dev/null 2>&1; then
  exit 0
fi

# Detached background regen — never blocks the commit
(
  npx -y repomix@latest --quiet >/dev/null 2>&1 &
) >/dev/null 2>&1

exit 0
