"""Quota governor: daily token budgets, per-route caps, exponential backoff.

Modes A/B share one credential source. This governor protects the product
from a single-account daily cap, and gives admins per-route budgets so
high-value Antigravity features can be prioritized when traffic grows
(multi-user SaaS reality: reserve Antigravity for scoring/writing; let
fallback providers absorb baseline load - see AiFeatureRouteResolver).
"""
from __future__ import annotations

import asyncio
import datetime as dt
import random
from dataclasses import dataclass, field

from .config import Settings


def estimate_tokens(text: str) -> int:
    """Rough token estimate (english + code text, ~4 chars/token)."""
    return max(1, len(text) // 4)


@dataclass
class _RouteBudget:
    allowed: int = 0
    used: int = 0


class QuotaGovernor:
    def __init__(self, settings: Settings) -> None:
        self._global_day: str | None = None
        self._global_used = 0
        self._global_limit = settings.budget_tokens_per_day
        self._routes: dict[str, _RouteBudget] = {
            route: _RouteBudget(allowed=cap)
            for route, cap in settings.budget_routes.items()
        }
        self._routes_global_default = 0  # fallback: route with no explicit cap shares global
        self._backoff_base = settings.backoff_base_seconds
        self._backoff_max = settings.backoff_max_seconds
        self._failures: dict[str, int] = {}
        self._lock = asyncio.Lock()

    def _roll_day(self) -> None:
        today = dt.date.today().isoformat()
        if self._global_day != today:
            self._global_day = today
            self._global_used = 0
            for budget in self._routes.values():
                budget.used = 0
            self._failures.clear()

    async def can_spend(self, route: str, est: int) -> tuple[bool, str]:
        """Check whether `est` tokens may be spent on `route` today."""
        async with self._lock:
            self._roll_day()
            remaining_global = self._global_limit - self._global_used
            if remaining_global <= 0:
                return False, "global daily budget exhausted"
            route_budget = self._routes.get(route)
            if route_budget is not None:
                remaining_route = route_budget.allowed - route_budget.used
                if remaining_route <= 0:
                    return False, f"route budget exhausted for '{route}'"
                return est <= min(remaining_global, remaining_route), "ok"
            return est <= remaining_global, "ok"

    async def spend(self, route: str, actual: int) -> None:
        async with self._lock:
            self._roll_day()
            self._global_used += actual
            budget = self._routes.get(route)
            if budget is not None:
                budget.used += actual

    async def health(self) -> dict[str, object]:
        async with self._lock:
            self._roll_day()
            return {
                "global_used": self._global_used,
                "global_limit": self._global_limit,
                "global_remaining": self._global_limit - self._global_used,
                "routes": {
                    route: {
                        "used": b.used,
                        "allowed": b.allowed,
                        "remaining": b.allowed - b.used,
                    }
                    for route, b in self._routes.items()
                },
                "day": self._global_day,
            }

    async def backoff_delay(self, route: str) -> float:
        """Exponential backoff with jitter; resets on success (call `reset`)."""
        async with self._lock:
            n = self._failures.get(route, 0)
            self._failures[route] = n + 1
        delay = min(
            self._backoff_max,
            self._backoff_base * (2 ** min(n, 10)),
        )
        return delay * (0.5 + random.random() / 2)

    async def reset(self, route: str) -> None:
        async with self._lock:
            self._failures.pop(route, None)
