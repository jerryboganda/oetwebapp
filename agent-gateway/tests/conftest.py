"""Shared fixtures. Tests never touch the live Antigravity harness."""
from __future__ import annotations

import pytest

from oet_agent_gateway.config import Settings


@pytest.fixture
def settings() -> Settings:
    return Settings(
        auth_mode="gemini-key",
        gemini_api_key="test-key-do-not-use",
        agents_enabled="writing-examiner,mock-analysis",
        budget_tokens_per_day=10000,
        budget_by_route='{"writing-examiner": 4000}',
        internal_service_token="",
        cache_ttl_seconds=60,
        cache_max_entries=16,
        max_sessions=4,
    )
