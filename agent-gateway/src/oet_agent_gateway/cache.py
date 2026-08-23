"""Semantic response cache to cut token spend on repeated scoring prompts.

Keying: agent name + normalized prompt. Repeat submissions of the same
answer (retries, drafts, demo replays) return instantly and cost zero.
Production note: keep the LRU in-process; distribute with a Redis-oriented
backend in Phase 6 if multiple gateway replicas are deployed.
"""
from __future__ import annotations

import hashlib
import json
import time
from collections import OrderedDict


def cache_key(agent: str, messages: list[dict[str, str]], system: str | None) -> str:
    payload = json.dumps(
        {"agent": agent, "messages": messages, "system": system},
        sort_keys=True,
        separators=(",", ":"),
    )
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()


class SemanticResponseCache:
    def __init__(self, ttl_seconds: int = 3600, max_entries: int = 512) -> None:
        self._ttl = ttl_seconds
        self._max = max_entries
        self._store: OrderedDict[str, tuple[float, str]] = OrderedDict()

    def get(self, key: str) -> str | None:
        item = self._store.get(key)
        if item is None:
            return None
        ts, value = item
        if time.time() - ts > self._ttl:
            self._store.pop(key, None)
            return None
        self._store.move_to_end(key)
        return value

    def put(self, key: str, value: str) -> None:
        if len(self._store) >= self._max:
            self._store.popitem(last=False)
        self._store[key] = (time.time(), value)

    @property
    def size(self) -> int:
        return len(self._store)
