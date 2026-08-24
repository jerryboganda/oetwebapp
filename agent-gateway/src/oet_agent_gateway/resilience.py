"""Resilience primitives: circuit breaker and upstream error classification.

Circuit breaker semantics (per budget route):
  * closed        - normal operation; consecutive failures are counted.
  * open          - after `threshold` consecutive failures every call fails
                    fast for `cooldown_seconds`. The .NET
                    AiFeatureRouteResolver treats HTTP 503 as the fallback
                    signal, so students are served by the fallback provider
                    immediately instead of waiting on a dead harness/quota.
  * half-open     - after cooldown one probe call is allowed; success closes
                    the circuit, failure re-opens it.
"""
from __future__ import annotations

import asyncio
import time
from dataclasses import dataclass


def is_quota_related(exc: BaseException) -> bool:
    text = f"{type(exc).__name__}: {exc}".lower()
    return any(tag in text for tag in ("429", "resource_exhausted", "rate limit", "quota"))


def is_timeout_exception(exc: BaseException) -> bool:
    return isinstance(exc, asyncio.TimeoutError) or isinstance(
        exc, TimeoutError
    )


@dataclass
class _RouteState:
    failures: int = 0
    opened_at: float | None = None


class CircuitBreaker:
    def __init__(self, threshold: int = 8, cooldown_seconds: float = 90.0) -> None:
        self._threshold = max(1, threshold)
        self._cooldown = max(0.0, cooldown_seconds)
        self._routes: dict[str, _RouteState] = {}
        self._lock = asyncio.Lock()

    async def check(self, route: str) -> tuple[bool, float]:
        """Return (allowed, retry_after_seconds). retry_after is 0 when allowed."""
        async with self._lock:
            state = self._routes.setdefault(route, _RouteState())
            if state.opened_at is None:
                return True, 0.0
            elapsed = time.monotonic() - state.opened_at
            if elapsed >= self._cooldown:
                # half-open: allow a single probe (failure path re-opens).
                return True, 0.0
            return False, round(self._cooldown - elapsed, 2)

    async def record_success(self, route: str) -> None:
        async with self._lock:
            self._routes[route] = _RouteState()

    async def record_failure(self, route: str) -> bool:
        """Count a failure. Returns True when this failure opened the circuit."""
        async with self._lock:
            state = self._routes.setdefault(route, _RouteState())
            if state.opened_at is not None:
                # failed probe while half-open: restart cooldown window.
                state.opened_at = time.monotonic()
                return False
            state.failures += 1
            if state.failures >= self._threshold:
                state.opened_at = time.monotonic()
                return True
            return False

    async def snapshot(self) -> dict[str, dict[str, object]]:
        async with self._lock:
            now = time.monotonic()
            out: dict[str, dict[str, object]] = {}
            for route, state in self._routes.items():
                if state.opened_at is not None:
                    remaining = round(max(0.0, self._cooldown - (now - state.opened_at)), 2)
                    status = "open" if remaining > 0 else "half-open"
                else:
                    remaining = 0.0
                    status = "closed"
                out[route] = {
                    "state": status,
                    "consecutive_failures": state.failures,
                    "retry_after_s": remaining,
                }
            return out
