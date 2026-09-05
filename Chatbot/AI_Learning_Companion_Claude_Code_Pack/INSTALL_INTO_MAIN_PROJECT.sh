#!/usr/bin/env sh
set -eu

if [ "$#" -ne 1 ]; then
  echo "Usage: $0 /path/to/main-repo" >&2
  exit 2
fi

PACK_ROOT=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
TARGET=$1

if [ ! -d "$TARGET" ]; then
  echo "Target repository does not exist: $TARGET" >&2
  exit 1
fi

mkdir -p "$TARGET/docs" "$TARGET/traceability" "$TARGET/.claude" "$TARGET/scripts/ai-learning-companion"
cp -R "$PACK_ROOT/docs/ai-learning-companion" "$TARGET/docs/"
cp -R "$PACK_ROOT/traceability/." "$TARGET/traceability/"
mkdir -p "$TARGET/.claude/commands"
cp -R "$PACK_ROOT/.claude/commands/." "$TARGET/.claude/commands/"
cp "$PACK_ROOT/AI_LEARNING_COMPANION_CLAUDE_CODE_MASTER_PLAN.markdown.md" "$TARGET/"
cp "$PACK_ROOT/START_HERE.md" "$TARGET/AI_COMPANION_START_HERE.md"
mkdir -p "$TARGET/docs/ai-learning-companion/source"
cp "$PACK_ROOT/source/Talk_to_Jana_or_Sami_AI_Master_Specification_v3_FINAL.pdf" "$TARGET/docs/ai-learning-companion/source/"

if [ -f "$TARGET/CLAUDE.md" ]; then
  cp "$PACK_ROOT/CLAUDE.md" "$TARGET/CLAUDE_AI_COMPANION_ADDENDUM.md"
  echo "Existing CLAUDE.md preserved. Merge CLAUDE_AI_COMPANION_ADDENDUM.md into it without weakening existing rules."
else
  cp "$PACK_ROOT/CLAUDE.md" "$TARGET/CLAUDE.md"
fi

cp "$PACK_ROOT/scripts/validate_traceability.py" "$TARGET/scripts/ai-learning-companion/validate_traceability.py"
echo "Pack copied. Start with .claude/commands/ai-companion-audit.md in Claude Code."
