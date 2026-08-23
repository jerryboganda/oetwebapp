"""OET Agent Gateway - FastAPI application.

Exposes the Google Antigravity SDK agent runtime as:
  * GET  /v1/healthz                          - liveness/readiness (harness, auth, quota)
  * GET  /v1/agents                           - enabled agent specs (admin, docs)
  * GET  /v1/quota                            - budget governor health
  * POST /v1/chat/completions                 - OpenAI-compatible drop-in (the .NET
    AiProviderRegistry OpenAiCompatible dialect calls this with stream:false)
  * POST /v1/sessions                         - create a native agent session
  * POST /v1/sessions/{id}/messages           - SSE: thoughts, tool_calls, content, done
"""
from __future__ import annotations

import asyncio
import json
import logging
import time
import uuid
from contextlib import asynccontextmanager
from dataclasses import dataclass, field
from typing import Any, AsyncIterator

from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import JSONResponse, StreamingResponse

from . import __version__
from .agents import AgentSpec, build_config, list_specs, specs_manifest
from .auth import AuthAdapter, AuthAdapterError, get_adapter
from .cache import SemanticResponseCache, cache_key
from .config import Settings, get_settings
from .quota import QuotaGovernor, estimate_tokens

logger = logging.getLogger("oet_agent_gateway")


@dataclass
class _Session:
    id: str
    agent_name: str
    agent: Any = None  # google.antigravity.Agent
    lock: asyncio.Lock = field(default_factory=asyncio.Lock)
    last_used: float = field(default_factory=time.monotonic)


class _AgentPool:
    """LRU pool of live agent sessions. One harness process per slot."""

    def __init__(self, settings: Settings, adapter: AuthAdapter) -> None:
        self._settings = settings
        self._adapter = adapter
        self._sessions: dict[str, _Session] = {}
        self._lru: list[str] = []
        self._created = 0
        self._evicted = 0

    @property
    def stats(self) -> dict[str, int]:
        return {"alive": len(self._sessions), "created": self._created, "evicted": self._evicted}

    async def acquire(self, agent_name: str) -> _Session:
        spec = list_specs(self._settings).get(agent_name)
        if spec is None:
            raise HTTPException(
                status_code=400,
                detail=f"Unknown agent '{agent_name}'. Available: {sorted(list_specs(self._settings))}",
            )
        if self._sessions.get(agent_name):
            session = self._sessions[agent_name]
        else:
            await self._evict_if_needed()
            session = _Session(id=uuid.uuid4().hex, agent_name=agent_name)
            config = build_config(spec, self._settings)
            config = self._adapter.apply(config, spec.model)
            try:
                from google.antigravity import Agent

                agent = Agent(config)
                session.agent = await agent.__aenter__()
            except Exception as exc:
                raise HTTPException(
                    status_code=503,
                    detail=f"Agent runtime failed to start: {type(exc).__name__}: {exc}",
                ) from exc
            self._sessions[agent_name] = session
            self._created += 1
        self._touch(agent_name)
        return session

    def _touch(self, agent_name: str) -> None:
        try:
            self._lru.remove(agent_name)
        except ValueError:
            pass
        self._lru.append(agent_name)

    async def _evict_if_needed(self) -> None:
        if len(self._sessions) < self._settings.max_sessions:
            return
        for name in self._lru:
            session = self._sessions.pop(name, None)
            if session is None:
                continue
            if session.agent is not None:
                try:
                    await session.agent.__aexit__(None, None, None)
                except Exception:  # noqa: BLE001 - eviction best-effort
                    pass
            self._evicted += 1
            return


def _openai_usage(prompt_text: str, completion: str) -> dict[str, int]:
    return {
        "prompt_tokens": estimate_tokens(prompt_text),
        "completion_tokens": estimate_tokens(completion),
    }


def _parse_agent_name(model: str | None) -> str:
    """Model string sent by the .NET provider: 'agent:writing-examiner' or a bare agent name."""
    name = (model or "agent:writing-examiner").strip()
    if ":" in name:
        _, _, maybe = name.partition(":")
        if maybe:
            name = maybe
    return name


def _event(event_type: str, **payload: Any) -> str:
    return f"event: {event_type}\ndata: {json.dumps(payload)}\n\n"


def create_app(
    settings: Settings | None = None,
    pool: _AgentPool | None = None,
    quota_governor: QuotaGovernor | None = None,
    cache: SemanticResponseCache | None = None,
    adapter: AuthAdapter | None = None,
) -> FastAPI:
    settings = settings or get_settings()

    @asynccontextmanager
    async def lifespan(app: FastAPI):
        app.state.settings = settings
        app.state.adapter = adapter or get_adapter(settings)
        app.state.pool = pool or _AgentPool(settings, app.state.adapter)
        app.state.quota = quota_governor or QuotaGovernor(settings)
        app.state.cache = cache or SemanticResponseCache(
            ttl_seconds=settings.cache_ttl_seconds,
            max_entries=settings.cache_max_entries,
        )
        yield
        for session in list(app.state.pool._sessions.values()):
            if session.agent is not None:
                try:
                    await session.agent.__aexit__(None, None, None)
                except Exception:  # noqa: BLE001
                    pass

    app = FastAPI(title="OET Agent Gateway", version=__version__, lifespan=lifespan)

    @app.middleware("http")
    async def internal_auth(request: Request, call_next):
        if settings.internal_service_token:
            if request.method == "GET" and request.url.path == "/v1/healthz":
                return await call_next(request)
            provided = request.headers.get("x-oet-internal-token", "")
            if provided != settings.internal_service_token:
                return JSONResponse(status_code=401, content={"error": "invalid internal token"})
        return await call_next(request)

    @app.middleware("http")
    async def request_id(request: Request, call_next):
        rid = uuid.uuid4().hex[:12]
        response = await call_next(request)
        response.headers["x-request-id"] = rid
        return response

    @app.get("/v1/healthz")
    async def healthz():
        q = await request_state_quota(app)
        return {
            "status": "ok",
            "version": __version__,
            "auth_mode": settings.auth_mode,
            "agents": len(list_specs(settings)),
            "pool": app.state.pool.stats,
            "quota": q,
        }

    async def request_state_quota(app: FastAPI) -> dict[str, object]:
        try:
            return await app.state.quota.health()
        except Exception:  # noqa: BLE001
            return {}

    @app.get("/v1/agents")
    async def agents():
        return {"agents": specs_manifest(settings)}

    @app.get("/v1/quota")
    async def quota_endpoint():
        return await request_state_quota(app)

    @app.post("/v1/chat/completions")
    async def chat_completions(body: dict[str, Any]):
        return await _chat_completions_impl(app, body, stream=bool(body.get("stream", False)))

    async def _chat_completions_impl(app: FastAPI, body: dict[str, Any], stream: bool):
        agent_name = _parse_agent_name(body.get("model"))
        spec = list_specs(app.state.settings).get(agent_name)
        if spec is None:
            raise HTTPException(400, f"Unknown agent '{agent_name}'.")
        messages = body.get("messages") or []
        if not isinstance(messages, list) or not messages:
            raise HTTPException(400, "messages must be a non-empty array.")

        system_parts: list[str] = []
        user_parts: list[str] = []
        for m in messages:
            if not isinstance(m, dict):
                raise HTTPException(400, "each message must be an object with role/content")
            role = m.get("role", "user")
            content = m.get("content")
            if content is None:
                continue
            if role == "system":
                system_parts.append(str(content))
            else:
                user_parts.append(f"{role}: {content}")
        prompt = "\n\n".join(user_parts).strip()
        system = "\n\n".join(system_parts).strip()
        if not prompt:
            raise HTTPException(400, "no user content to send")

        convo_key = cache_key(agent_name, messages, system)
        cached = app.state.cache.get(convo_key)
        if cached is not None:
            if stream:
                return StreamingResponse(_sse_from_text(cached), media_type="text/event-stream")
            return _openai_result(cached, spec.model, cached_hit=True)

        est = estimate_tokens(system + prompt)
        route = spec.budget_route or spec.name
        ok, reason = await app.state.quota.can_spend(route, est)
        if not ok:
            raise HTTPException(503, f"quota: {reason}")

        session = await app.state.pool.acquire(agent_name)
        async with session.lock:
            spec_ctx = _TurnContext(app, session, route, spec, system, prompt, est)
            try:
                result = await _run_turn(app, session, prompt, spec_ctx)
            except HTTPException:
                raise
            except Exception as exc:
                logger.warning("agent turn failed (agent=%s): %s", agent_name, exc)
                raise _upgrade_error(exc) from exc

        text = result["text"]
        app.state.cache.put(convo_key, text)
        if stream:
            return StreamingResponse(_sse_from_text(text), media_type="text/event-stream")
        return _openai_result(text, spec.model, cached_hit=False)

    @app.post("/v1/sessions")
    async def create_session(body: dict[str, Any]):
        agent_name = _parse_agent_name(body.get("agent", body.get("model", "")))
        await app.state.pool.acquire(agent_name)
        session = app.state.pool._sessions[agent_name]
        return {"session_id": session.id, "agent": agent_name, "pool": app.state.pool.stats}

    @app.post("/v1/sessions/{session_id}/messages")
    async def session_message(session_id: str, body: dict[str, Any]):
        session: _Session | None = next((s for s in app.state.pool._sessions.values() if s.id == session_id), None)
        if session is None:
            raise HTTPException(404, "unknown session")
        text = str(body.get("text", "")).strip()
        if not text:
            raise HTTPException(400, "text is required")

        spec = list_specs(app.state.settings).get(session.agent_name)
        if spec is None:
            raise HTTPException(400, "session agent is not enabled")
        route = spec.budget_route or spec.name
        est = estimate_tokens(text)
        ok, reason = await app.state.quota.can_spend(route, est)
        if not ok:
            raise HTTPException(503, f"quota: {reason}")

        return StreamingResponse(
            _native_stream(app, session, spec, text, route),
            media_type="text/event-stream",
        )

    return app


async def _native_stream(
    app: FastAPI,
    session: _Session,
    spec: AgentSpec,
    prompt: str,
    route: str,
) -> AsyncIterator[str]:
    async with session.lock:
        yield _event("session", session_id=session.id, agent=spec.name)
        try:
            async for event in _stream_events(app, session, "", prompt):
                yield _event(event["type"], **{k: v for k, v in event.items() if k != "type"})
        except Exception as exc:  # noqa: BLE001 - forward as SSE error
            yield _event("error", error=str(exc))
        finally:
            session.last_used = time.monotonic()

    yield _event("done", pool=app.state.pool.stats)


async def _run_turn(
    app: FastAPI,
    session: _Session,
    prompt: str,
    ctx: "_TurnContext",
) -> dict[str, Any]:
    """Execute a turn and return the aggregated text (+ usage accounting)."""
    text_parts: list[str] = []
    usage: dict[str, int] | None = None
    async for event in _stream_events(app, session, ctx.system, prompt):
        if event["type"] == "content":
            text_parts.append(event["delta"])
        elif event["type"] == "usage":
            usage = event["usage"]
    text = "".join(text_parts)
    await app.state.quota.spend(ctx.route, ctx.est + estimate_tokens(text))
    await app.state.quota.reset(ctx.route)
    result: dict[str, Any] = {"text": text}
    if usage:
        result["usage"] = usage
    return result


async def _stream_events(
    app: FastAPI,
    session: _Session,
    system: str,
    prompt: str,
) -> AsyncIterator[dict[str, Any]]:
    """One agent turn as a stream of typed events (content/thought/tool_call/retry/usage)."""
    agent_name = session.agent_name
    enriched = f"[System context]\n{system}\n\n{prompt}" if system else prompt

    attempt = 0
    while True:
        try:
            response = await session.agent.chat(enriched)
            text_parts: list[str] = []
            async for chunk in response.chunks:
                chunk_type = type(chunk).__name__
                if chunk_type == "Text":
                    text_parts.append(chunk.text)
                    yield {"type": "content", "delta": chunk.text}
                elif chunk_type == "Thought":
                    yield {"type": "thought", "text": chunk.text}
                elif chunk_type == "ToolCall":
                    yield {
                        "type": "tool_call",
                        "name": getattr(chunk, "name", "?"),
                        "args": getattr(chunk, "args", None),
                    }
            text = "".join(text_parts)
            yield {
                "type": "usage",
                "usage": {
                    "prompt_tokens": estimate_tokens(enriched),
                    "completion_tokens": estimate_tokens(text),
                },
            }
            return
        except Exception as exc:  # noqa: BLE001 - retry policy applied
            if _is_quota_related(exc) and attempt < app.state.settings.retry_max_attempts:
                attempt += 1
                delay = await app.state.quota.backoff_delay(agent_name)
                yield {"type": "retry", "attempt": attempt, "delay_s": round(delay, 2)}
                await asyncio.sleep(delay)
                continue
            raise


async def _sse_from_text(text: str) -> AsyncIterator[str]:
    for line in text.split("\n"):
        if line:
            yield f"data: {json.dumps({'choices': [{'delta': {'content': line}}]})}\n\n"
    yield "data: [DONE]\n\n"


def _openai_result(text: str, model: str, cached_hit: bool) -> dict[str, Any]:
    return {
        "id": f"chatcmpl-{uuid.uuid4().hex[:16]}",
        "object": "chat.completion",
        "created": int(time.time()),
        "model": model,
        "choices": [
            {
                "index": 0,
                "message": {"role": "assistant", "content": text},
                "finish_reason": "stop",
            }
        ],
        "usage": {
            "prompt_tokens": estimate_tokens(text),
            "completion_tokens": estimate_tokens(text),
        },
        "cached": cached_hit,
    }


class _TurnContext:
    def __init__(
        self,
        app: FastAPI,
        session: _Session,
        route: str,
        spec: AgentSpec,
        system: str,
        prompt: str,
        est: int,
    ) -> None:
        self.app = app
        self.session = session
        self.route = route
        self.spec = spec
        self.system = system
        self.prompt = prompt
        self.est = est


def _is_quota_related(exc: Exception) -> bool:
    text = f"{type(exc).__name__}: {exc}".lower()
    return any(tag in text for tag in ("429", "resource_exhausted", "rate limit", "quota"))


def _upgrade_error(exc: Exception) -> Exception:
    text = f"{type(exc).__name__}: {exc}"
    if _is_quota_related(exc):
        return HTTPException(503, f"upstream quota exhausted: {text}")
    return HTTPException(500, f"agent error: {text}")
