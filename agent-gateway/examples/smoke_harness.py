"""Phase 0 smoke: verify google-antigravity install + localharness binary discovery.

Expected outcome on a machine with NO GEMINI_API_KEY set:
  - imports OK
  - Agent starts (binary discovered and launches)
  - chat() raises AntigravityValidationError "A Gemini API key is required"
    -> this proves the harness binary works on this platform.

With GEMINI_API_KEY set, the run completes a real turn instead.
"""
from __future__ import annotations

import asyncio
import os
import sys

from google.antigravity import Agent, LocalAgentConfig


async def main() -> int:
    print(f"agent: google-antigravity import OK")
    print(f"GEMINI_API_KEY present: {bool(os.environ.get('GEMINI_API_KEY'))}")
    config = LocalAgentConfig(
        model="gemini-3.7-flash",
        system_instructions="You are a smoke-test agent. Reply with exactly: PONG",
    )
    print("starting Agent (localharness discovery) ...")
    async with Agent(config) as agent:
        print(f"Agent started. conversation_id={agent.conversation_id}")
        if not os.environ.get("GEMINI_API_KEY"):
            try:
                response = await agent.chat("ping")
            except Exception as exc:  # noqa: BLE001 - smoke script intentionally broad
                print(f"chat raised as expected: {type(exc).__name__}: {exc}")
                return 0
            print(f"unexpected success: {await response.text()}")
            return 1
        response = await agent.chat("ping")
        text = await response.text()
        print(f"agent replied: {text}")
        return 0 if text.strip() == "PONG" else 1


if __name__ == "__main__":
    sys.exit(asyncio.run(main()))
