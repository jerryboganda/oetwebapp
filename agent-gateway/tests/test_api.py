"""API-level tests. The agent pool is stubbed so no live harness is started.

This proves the gateway surface (auth guard, agent resolution, OpenAI-compat
shape, cache path, quota rejection, native SSE event shape) without network.
"""
from __future__ import annotations

import pytest

from oet_agent_gateway.config import Settings
from oet_agent_gateway.server import _Session, create_app


class Text:
    """Matches the SDK stream chunk type name so _stream_events sees content."""

    def __init__(self, text: str) -> None:
        self.text = text


class FakeResponse:
    async def __aiter__(self):
        yield Text("Overall, you addressed purpose and content well.")


class FakeChunksResponse:
    @property
    def chunks(self):
        async def _gen():
            yield Text("Overall, you addressed purpose and content well.")
            yield Text(" Language is sound.")

        return _gen()


class FakeAgent:
    def __init__(self) -> None:
        self.calls: list[str] = []

    async def __aenter__(self):
        return self

    async def __aexit__(self, *args):
        return False

    async def chat(self, prompt: str):
        self.calls.append(prompt)
        return FakeChunksResponse()


class FakePool:
    """Mirrors the server pool surface (sessions dict keyed by agent name)."""

    def __init__(self) -> None:
        self._sessions: dict[str, _Session] = {}
        self._created = 0
        self._evicted = 0

    async def acquire(self, agent_name: str):
        if agent_name not in self._sessions:
            self._sessions[agent_name] = _Session(id="fake-session", agent_name=agent_name, agent=FakeAgent())
            self._created += 1
        return self._sessions[agent_name]

    @property
    def stats(self) -> dict[str, int]:
        return {"alive": len(self._sessions), "created": self._created, "evicted": self._evicted}


@pytest.fixture
def app(settings):
    return create_app(settings, pool=FakePool())


def _client(app):
    from fastapi.testclient import TestClient

    return TestClient(app)


def test_healthz_ok(settings, app):
    with _client(app) as client:
        r = client.get("/v1/healthz")
    assert r.status_code == 200
    assert r.json()["status"] == "ok"
    assert r.json()["auth_mode"] == "gemini-key"


def test_agents_listing(app):
    with _client(app) as client:
        r = client.get("/v1/agents")
    assert r.status_code == 200
    names = {a["name"] for a in r.json()["agents"]}
    assert "writing-examiner" in names and "mock-analysis" in names
    assert "drill-author" not in names  # filtered by agents_enabled in fixture settings


def test_internal_token_guard():
    s = Settings(
        auth_mode="gemini-key",
        gemini_api_key="k",
        internal_service_token="secret-token",
        agents_enabled="*",
    )
    app = create_app(s, pool=FakePool())
    with _client(app) as client:
        assert client.get("/v1/healthz").status_code == 200
        assert client.get("/v1/agents").status_code == 401
        assert client.get("/v1/agents", headers={"x-oet-internal-token": "secret-token"}).status_code == 200


def test_unknown_agent_rejected(app):
    with _client(app) as client:
        r = client.post(
            "/v1/chat/completions",
            json={"model": "agent:not-an-agent", "messages": [{"role": "user", "content": "hi"}]},
        )
    assert r.status_code == 400
    assert "Unknown agent" in r.json()["detail"]


def test_chat_completions_openai_shape(settings, app):
    with _client(app) as client:
        r = client.post(
            "/v1/chat/completions",
            json={
                "model": "writing-examiner",
                "messages": [
                    {"role": "system", "content": "Grade this letter."},
                    {"role": "user", "content": "Dear Doctor Hopkins... [letter body]"},
                ],
            },
        )
    assert r.status_code == 200
    body = r.json()
    assert body["object"] == "chat.completion"
    assert body["choices"][0]["message"]["content"].startswith("Overall")
    assert body["choices"][0]["finish_reason"] == "stop"
    assert set(body["usage"]) == {"prompt_tokens", "completion_tokens"}
    assert body["cached"] is False


def test_chat_completions_cache_hit_second_time(settings, app):
    with _client(app) as client:
        payload = {
            "model": "mock-analysis",
            "messages": [{"role": "user", "content": "Analyse this mock: lr 320, rd 340, wr 380, sp 290"}],
        }
        first = client.post("/v1/chat/completions", json=payload)
        second = client.post("/v1/chat/completions", json=payload)
    assert first.status_code == 200 and second.status_code == 200
    assert second.json()["cached"] is True


def test_quota_rejection_503(settings, app):
    settings.budget_tokens_per_day = 100
    with _client(app) as client:
        r = client.post(
            "/v1/chat/completions",
            json={
                "model": "writing-examiner",
                "messages": [{"role": "user", "content": "Long answer text " * 200}],
            },
        )
    assert r.status_code == 503
    assert "quota" in r.json()["detail"]


def test_native_session_and_messages_sse_shape(settings, app):
    with _client(app) as client:
        created = client.post("/v1/sessions", json={"agent": "writing-examiner"})
        assert created.status_code == 200
        sid = created.json()["session_id"]
        r = client.post(f"/v1/sessions/{sid}/messages", json={"text": "Grade this."})
    assert r.status_code == 200
    assert "text/event-stream" in r.headers["content-type"]
    assert "event:" in r.text and "data:" in r.text
