"""Response cache to cut token spend on repeated scoring prompts.

Keying: agent name + normalized messages. Repeat submissions of the same
answer (retries, drafts, demo replays) return instantly and cost zero.

Backends:
  * SemanticResponseCache      - in-process LRU (default; zero deps).
  * RedisResponseCache         - optional shared cache for multi-replica
    deployments (AGENTGATEWAY_REDIS_URL). Falls back to the in-process LRU
    when redis is unreachable or the `redis` package is not installed, so a
    cache outage can never take scoring down.
"""
from __future__ import annotations

import hashlib
import json
import logging
import time
from collections import OrderedDict

logger = logging.getLogger("oet_agent_gateway")


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


class RedisResponseCache:
    """Redis-backed shared cache with graceful in-memory fallback."""

    _FALLBACK_TTL = 60  # short local TTL while Redis is degraded

    def __init__(self, url: str, ttl_seconds: int = 3600, max_entries: int = 512) -> None:
        try:
            from redis import asyncio as aioredis
        except ImportError as exc:  # pragma: no cover - depends on extras install
            raise RuntimeError(
                "AGENTGATEWAY_REDIS_URL is set but the 'redis' package is not "
                "installed. pip install 'oet-agent-gateway[redis]' or unset "
                "AGENTGATEWAY_REDIS_URL."
            ) from exc
        self._ttl = ttl_seconds
        self._prefix = "oetgw:cache:"
        self._client = aioredis.from_url(url, socket_timeout=0.5, socket_connect_timeout=0.5)
        self._fallback = SemanticResponseCache(
            ttl_seconds=self._FALLBACK_TTL, max_entries=max_entries
        )
        self.degraded = False

    def _key(self, key: str) -> str:
        return self._prefix + key

    async def get(self, key: str) -> str | None:
        try:
            value = await self._client.get(self._key(key))
            self.degraded = False
            if isinstance(value, bytes):
                return value.decode("utf-8")
            return value
        except Exception as exc:  # noqa: BLE001 - cache must never fail requests
            if not self.degraded:
                logger.warning("Redis cache get failed; serving from local fallback (%s)", exc)
            self.degraded = True
            return self._fallback.get(key)

    async def put(self, key: str, value: str) -> None:
        try:
            await self._client.set(self._key(key), value, ex=self._ttl)
            self.degraded = False
        except Exception as exc:  # noqa: BLE001
            if not self.degraded:
                logger.warning("Redis cache put failed; using local fallback (%s)", exc)
            self.degraded = True
            self._fallback.put(key, value)

    @property
    def size(self) -> int:
        return -1  # remote size unknown; health reports backend instead


def create_cache(settings) -> object:
    """Backend factory: Redis when configured, else in-process LRU."""
    if settings.redis_url:
        try:
            return RedisResponseCache(
                url=settings.redis_url,
                ttl_seconds=settings.cache_ttl_seconds,
                max_entries=settings.cache_max_entries,
            )
        except RuntimeError:
            logger.warning("Redis cache unavailable (missing package); using in-process LRU.")
        except Exception as exc:  # noqa: BLE001 - bad URL etc.
            logger.warning("Redis cache init failed (%s); using in-process LRU.", exc)
    return SemanticResponseCache(
        ttl_seconds=settings.cache_ttl_seconds,
        max_entries=settings.cache_max_entries,
    )
