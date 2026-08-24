"""Hardening tests: timeouts, circuit breaker, limits, metrics, pool hygiene,
keepalive, structured-output opt-in, usage accuracy. No live harness."""
from __future__ import annotations

import asyncio
import time
from types import SimpleNamespace

import pytest

from oet_agent_gateway.config import Settings
from oet_agent_gateway.quota import estimate_tokens
from oet_agent_gateway.server import _AgentPool, _Session, create_app


# ---------------------------------------------------------------- fakes


class Text:
    def __init__(self, text: str) -> None:
        self.text = text


class FakeChunksResponse:
    def __init__(self, text: str = "Scored.") -> None:
        self._text = text

    @property
    def chunks(self):
        async def _gen():
            yield Text(self._text)

        return _gen()


class FakeAgent:
    """Configurable stand-in: delay, repeated failure, call counting."""

    def __init__(self, delay_s: float = 0.0, fail_with: Exception | None = None) -> None:
        self.delay_s = delay_s
        self.fail_with = fail_with
        self.calls = 0

    async def __aenter__(self):
        return self

    async def __aexit__(self, *args):
        return False

    async def chat(self, prompt: str):
        self.calls += 1
        if self.fail_with is not None:
            raise self.fail_with
        if self.delay_s:
            await asyncio.sleep(self.delay_s)
        return FakeChunksResponse()


class ScriptedPool:
    """Pool stub handing out provided agents keyed by name."""

    def __init__(self, agents: dict[str, FakeAgent]) -> None:
        self.agents = agents
        self.sessions: dict[str, _Session] = {}
        self.created = 0
        self.evicted = 0

    async def acquire(self, agent_name: str):
        if agent_name not in self.sessions:
            if agent_name not in self.agents:
                raise AssertionError(f"unexpected agent {agent_name}")
            self.sessions[agent_name] = _Session(
                id=f"fake-{agent_name}", agent_name=agent_name, agent=self.agents[agent_name]
            )
            self.created += 1
        return self.sessions[agent_name]

    def get(self, agent_name: str):
        return self.sessions.get(agent_name)

    def all_sessions(self):
        return list(self.sessions.values())

    @property
    def stats(self):
        return {
            "alive": len(self.sessions),
            "created": self.created,
            "evicted": self.evicted,
            "reaped": 0,
        }


def make_settings(**over) -> Settings:
    base = dict(
        auth_mode="gemini-key",
        gemini_api_key="test-key-do-not-use",
        agents_enabled="*",
        budget_tokens_per_day=10_000_000,
        internal_service_token="",
        max_sessions=4,
        retry_max_attempts=0,
    )
    base.update(over)
    return Settings(**base)


def client(app):
    from fastapi.testclient import TestClient

    return TestClient(app)


def post_chat(c, model="writing-examiner", content="Dear Doctor Hopkins, ...", **kw):
    return c.post(
        "/v1/chat/completions",
        json={"model": model, "messages": [{"role": "user", "content": content}], **kw},
    )


# ---------------------------------------------------------------- timeout


def test_turn_timeout_returns_504_and_releases_lock():
    agent = FakeAgent(delay_s=5.0)
    s = make_settings(turn_timeout_seconds=0.2)
    app = create_app(s, pool=ScriptedPool({"writing-examiner": agent}))
    with client(app) as c:
        r = post_chat(c)
    assert r.status_code == 504
    assert "timed out" in r.json()["detail"]
    pool_session = app.state.pool.sessions["writing-examiner"]
    assert not pool_session.lock.locked()
    assert app.state.inflight == 0


# ---------------------------------------------------------------- circuit


class QuotaError(Exception):
    """Mimics an SDK 429/RESOURCE_EXHAUSTED failure."""


def test_circuit_breaker_opens_and_fast_fails():
    agent = FakeAgent(fail_with=QuotaError("429 RESOURCE_EXHAUSTED: quota exceeded"))
    s = make_settings(circuit_failure_threshold=2, circuit_cooldown_seconds=60)
    app = create_app(s, pool=ScriptedPool({"writing-examiner": agent}))
    with client(app) as c:
        r1, r2 = post_chat(c), post_chat(c)
        # Both hit the (failing) harness: quota-related 503s.
        assert r1.status_code == 503 and r2.status_code == 503
        calls_after_failures = agent.calls
        # Third request must be rejected by the OPEN circuit: no new call.
        r3 = post_chat(c)
    assert r3.status_code == 503
    assert "circuit open" in r3.json()["detail"]
    assert "retry-after" in r3.headers
    assert agent.calls == calls_after_failures  # fast-failed, no harness call
    body = c.get("/v1/healthz").json()
    assert body["circuit_open"] is True
    assert body["circuits"]["writing-examiner"]["state"] == "open"


def test_circuit_breaker_resets_on_success():
    agent = FakeAgent(fail_with=QuotaError("429 resource_exhausted"))
    s = make_settings(circuit_failure_threshold=3, circuit_cooldown_seconds=0)
    app = create_app(s, pool=ScriptedPool({"writing-examiner": agent}))
    with client(app) as c:
        post_chat(c), post_chat(c)  # two failures
        agent.fail_with = None  # recovery
        ok = post_chat(c)
        assert ok.status_code == 200
    snap = asyncio.run(app.state.circuit.snapshot())
    assert snap["writing-examiner"]["state"] == "closed"
    assert snap["writing-examiner"]["consecutive_failures"] == 0


# ---------------------------------------------------------------- limits


def test_message_count_limit_413():
    s = make_settings(max_messages_per_request=3)
    app = create_app(s, pool=ScriptedPool({}))
    with client(app) as c:
        msgs = [{"role": "user", "content": "x"}] * 4
        r = c.post("/v1/chat/completions", json={"model": "writing-examiner", "messages": msgs})
    assert r.status_code == 413


def test_prompt_char_limit_413():
    s = make_settings(max_prompt_chars=100)
    app = create_app(s, pool=ScriptedPool({}))
    with client(app) as c:
        r = post_chat(c, content="y" * 200)
    assert r.status_code == 413


# ---------------------------------------------------------------- usage


def test_usage_accounting_is_accurate():
    agent = FakeAgent()
    app = create_app(make_settings(), pool=ScriptedPool({"writing-examiner": agent}))
    system_text = "You are the examiner."
    user_text = "Letter body for scoring." * 10
    with client(app) as c:
        r = c.post(
            "/v1/chat/completions",
            json={
                "model": "writing-examiner",
                "messages": [
                    {"role": "system", "content": system_text},
                    {"role": "user", "content": user_text},
                ],
            },
        )
    body = r.json()
    # Matches the gateway's internal accounting: the enriched prompt actually
    # sent upstream ([System context] header + persona-free system + joined
    # non-system messages) is what usage reports and quota spends.
    expected_prompt = estimate_tokens(
        "[System context]\n" + system_text + "\n\n" + f"user: {user_text}"
    )
    assert body["usage"]["prompt_tokens"] == expected_prompt
    assert body["usage"]["completion_tokens"] == estimate_tokens("Scored.")
    assert body["cached"] is False


# ---------------------------------------------------------------- metrics


def test_metrics_endpoint_prometheus_shape():
    app = create_app(make_settings(), pool=ScriptedPool({"writing-examiner": FakeAgent()}))
    with client(app) as c:
        post_chat(c)
        post_chat(c)  # second identical -> cache hit
        m = c.get("/v1/metrics")
    assert m.status_code == 200
    assert m.headers["content-type"].startswith("text/plain")
    text = m.text
    assert "# TYPE oetgw_http_requests_total counter" in text
    assert 'oetgw_http_requests_total{method="POST"' in text
    assert 'oetgw_cache_hits_total{agent="writing-examiner"} 1' in text
    assert 'oetgw_turns_total{agent="writing-examiner",outcome="ok"}' in text
    assert "oetgw_sessions_alive" in text
    assert 'oetgw_quota_global_remaining_tokens' in text


def test_metrics_counts_auth_failures():
    s = make_settings(internal_service_token="tok")
    app = create_app(s, pool=ScriptedPool({}))
    with client(app) as c:
        assert c.get("/v1/agents").status_code == 401
        assert c.get("/v1/agents", headers={"authorization": "Bearer wrong"}).status_code == 401
        m = c.get("/v1/metrics", headers={"x-oet-internal-token": "tok"})
    assert "oetgw_auth_failures_total 2" in m.text


# ---------------------------------------------------------------- pool


def test_pool_eviction_never_drops_locked_session():
    s = make_settings(max_sessions=1)

    class NoopAdapter:
        mode = "test"

        def apply(self, config, model):
            return config

    pool = _AgentPool(s, NoopAdapter(), agent_factory=lambda config: FakeAgent())

    async def scenario():
        s_a = await pool.acquire("writing-examiner")
        async with s_a.lock:  # simulate in-flight turn
            await pool.acquire("mock-analysis")  # over-cap allowed, locked kept
            assert len(pool._sessions) == 2
            assert "writing-examiner" in pool._sessions
        # Unlocked now: acquiring a third agent must evict (capacity 1).
        await asyncio.sleep(0)  # let pending teardown tasks schedule
        await pool.acquire("grammar-tutor")
        return set(pool._sessions)

    names = asyncio.run(scenario())
    # Normal LRU eviction applies once nothing is locked: the oldest touch
    # (writing-examiner) goes, newer entries survive.
    assert "writing-examiner" not in names
    assert "mock-analysis" in names
    assert "grammar-tutor" in names


def test_pool_reaps_idle_sessions():
    s = make_settings(session_idle_ttl_seconds=0.05)

    class NoopAdapter:
        mode = "test"

        def apply(self, config, model):
            return config

    closed = {"n": 0}

    class SpyAgent(FakeAgent):
        async def __aexit__(self, *args):
            closed["n"] += 1
            return False

    pool = _AgentPool(s, NoopAdapter(), agent_factory=lambda config: SpyAgent())

    async def scenario():
        sess = await pool.acquire("writing-examiner")
        assert "writing-examiner" in pool._sessions
        await asyncio.sleep(0.08)  # exceed TTL (lock free)
        n = await pool.reap_idle(s.session_idle_ttl_seconds)
        return sess, n

    sess, n = asyncio.run(scenario())
    assert n == 1
    assert closed["n"] == 1  # teardown awaited inline by reap_idle
    assert "writing-examiner" not in pool._sessions
    assert sess.agent is not None


# ---------------------------------------------------------------- keepalive


def test_native_sse_emits_keepalive_on_slow_turn():
    agent = FakeAgent(delay_s=0.4)
    s = make_settings(sse_keepalive_seconds=0.05)
    app = create_app(s, pool=ScriptedPool({"writing-examiner": agent}))
    with client(app) as c:
        created = c.post("/v1/sessions", json={"agent": "writing-examiner"})
        sid = created.json()["session_id"]
        r = c.post(f"/v1/sessions/{sid}/messages", json={"text": "Grade this."})
    assert r.status_code == 200
    assert ": keepalive" in r.text
    assert "event: done" in r.text


# ---------------------------------------------------------------- structured


def test_structured_output_opt_in_manifest():
    s = make_settings(structured_output_agents="writing-examiner")
    app = create_app(s, pool=ScriptedPool({}))
    with client(app) as c:
        agents = {a["name"]: a for a in c.get("/v1/agents").json()["agents"]}
    assert agents["writing-examiner"]["structured"] is True
    assert isinstance(agents["writing-examiner"]["schema"], dict)
    assert "criteria" in agents["writing-examiner"]["schema"]["properties"]
    # Opt-in only applies to listed agents.
    assert agents["mock-analysis"]["structured"] is False


def test_structured_output_off_by_default():
    app = create_app(make_settings(), pool=ScriptedPool({}))
    with client(app) as c:
        agents = {a["name"]: a for a in c.get("/v1/agents").json()["agents"]}
    assert all(a["structured"] is False for a in agents.values())


# ---------------------------------------------------------------- cache


def test_cache_factory_defaults_to_memory():
    from oet_agent_gateway.cache import SemanticResponseCache, create_cache

    backend = create_cache(make_settings())
    assert isinstance(backend, SemanticResponseCache)


def test_cache_factory_redis_missing_package_falls_back():
    try:
        import redis  # noqa: F401

        has_redis = True
    except ImportError:
        has_redis = False
    from oet_agent_gateway.cache import create_cache

    backend = create_cache(make_settings(redis_url="redis://localhost:6399/0"))
    if has_redis:
        assert hasattr(backend, "get") and hasattr(backend, "put")
    else:
        from oet_agent_gateway.cache import SemanticResponseCache

        assert isinstance(backend, SemanticResponseCache)


# ---------------------------------------------------------------- misc


def test_healthz_reports_new_fields():
    app = create_app(make_settings(), pool=ScriptedPool({"writing-examiner": FakeAgent()}))
    with client(app) as c:
        post_chat(c)
        h = c.get("/v1/healthz").json()
    assert h["cache"]["backend"] == "SemanticResponseCache"
    assert h["cache"]["degraded"] is False
    assert "circuits" in h and h["circuits"]["writing-examiner"]["state"] == "closed"
    assert h["inflight"] == 0


def test_request_ids_unique_per_response():
    app = create_app(make_settings(), pool=ScriptedPool({"writing-examiner": FakeAgent()}))
    with client(app) as c:
        r1 = post_chat(c)
        r2 = post_chat(c, content="different prompt entirely")
    assert r1.headers["x-request-id"] != r2.headers["x-request-id"]


def test_create_app_production_path_no_args_serves_healthz():
    """Regression: the deployed entrypoint calls create_app() with no arguments
    (uvicorn factory). Must not raise at startup even without a Gemini key —
    it must degrade, not crash (a crash here blocked a blue/green promote)."""
    app = create_app()
    with client(app) as c:
        r = c.get("/v1/healthz")
    assert r.status_code == 200
    body = r.json()
    assert body["version"]
    assert body["auth_mode"] in ("gemini-key", "local-oauth", "sdk-oauth")
    assert isinstance(body["circuits"], dict)


@pytest.fixture(scope="module")
def _sanity():
    # Import side-effect sanity: resilience helpers classify correctly.
    from oet_agent_gateway.resilience import is_quota_related, is_timeout_exception

    assert is_quota_related(QuotaError("HTTP 429 rate limit"))
    assert not is_quota_related(ValueError("nope"))
    assert is_timeout_exception(asyncio.TimeoutError())
    assert is_timeout_exception(TimeoutError())
    return SimpleNamespace(ok=True)
