import pytest

from oet_agent_gateway.cache import SemanticResponseCache, cache_key
from oet_agent_gateway.quota import QuotaGovernor, estimate_tokens


def test_estimate_tokens_roughly_linear():
    assert estimate_tokens("a" * 400) == 100
    assert estimate_tokens("") == 1


@pytest.mark.asyncio
async def test_global_budget_exhaustion(settings):
    gov = QuotaGovernor(settings)
    ok, reason = await gov.can_spend("mock-analysis", 9000)
    assert ok
    await gov.spend("mock-analysis", 9000)
    ok, _ = await gov.can_spend("writing-examiner", 2000)
    assert not ok  # 10000 - 9000 = 1000 remaining, 2000 requested


@pytest.mark.asyncio
async def test_route_budget_isolated(settings):
    gov = QuotaGovernor(settings)
    ok, _ = await gov.can_spend("writing-examiner", 4000)
    assert ok
    await gov.spend("writing-examiner", 4000)
    ok, reason = await gov.can_spend("writing-examiner", 1)
    assert not ok
    assert "route budget" in reason
    ok, _ = await gov.can_spend("mock-analysis", 1000)
    assert ok


@pytest.mark.asyncio
async def test_backoff_exponential(settings):
    gov = QuotaGovernor(settings)
    first = await gov.backoff_delay("a")
    second = await gov.backoff_delay("a")
    assert first <= second * 2.0
    await gov.reset("a")
    third = await gov.backoff_delay("a")
    assert third < second


def test_cache_roundtrip_and_lru(settings):
    cache = SemanticResponseCache(ttl_seconds=30, max_entries=2)
    key1 = cache_key("writing-examiner", [{"role": "user", "content": "letter A"}], "s1")
    key2 = cache_key("mock-analysis", [{"role": "user", "content": "scores A"}], "s2")
    cache.put(key1, "v1")
    cache.put(key2, "v2")
    assert cache.get(key1) == "v1"  # moves key1 to most-recent -> key2 becomes LRU
    cache.put(cache_key("writing-examiner", [{"role": "user", "content": "letter C"}], "s1"), "v3")
    assert cache.get(key2) is None  # evicted LRU
    assert cache.get(key1) == "v1"
    assert cache.get(key3 := cache_key("writing-examiner", [{"role": "user", "content": "letter C"}], "s1")) == "v3"


def test_cache_keys_differ_by_system():
    a = cache_key("g", [{"role": "user", "content": "x"}], "sys-a")
    b = cache_key("g", [{"role": "user", "content": "x"}], "sys-b")
    assert a != b
