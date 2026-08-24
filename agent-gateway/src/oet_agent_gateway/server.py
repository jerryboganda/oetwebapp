"""OET Agent Gateway - FastAPI application.

Exposes the Google Antigravity SDK agent runtime as:
  * GET  /v1/healthz                          - liveness/readiness (harness, auth, quota)
  * GET  /v1/readyz                           - deployment gate (503 until auth ready)
  * GET  /v1/metrics                          - Prometheus exposition
  * GET  /v1/agents                           - enabled agent specs (admin, docs)
  * GET  /v1/quota                            - budget governor health
  * POST /v1/chat/completions                 - OpenAI-compatible drop-in (the .NET
    AiProviderRegistry OpenAiCompatible dialect calls this with stream:false)
  * POST /v1/sessions                         - create a native agent session
  * POST /v1/sessions/{id}/messages           - SSE: thoughts, tool_calls, content, done

Hardening: constant-time service-token compare, request size caps, per-turn
timeout, per-route circuit breaker (fast-fail lets the .NET resolver fall back
instantly), SSE keepalives, session idle reaping, graceful-shutdown drain,
accurate usage accounting, optional shared Redis cache, Prometheus metrics.
"""
from __future__ import annotations

import asyncio
import inspect
import json
import logging
import secrets
import time
import uuid
from contextlib import asynccontextmanager
from dataclasses import dataclass, field
from typing import Any, AsyncIterator

from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import JSONResponse, PlainTextResponse, StreamingResponse

from . import __version__
from .agents import AgentSpec, build_config, list_specs, specs_manifest
from .auth import AuthAdapter, AuthAdapterError, get_adapter
from .cache import cache_key, create_cache
from .config import Settings, get_settings
from .metrics import Metrics
from .quota import QuotaGovernor, estimate_tokens
from .resilience import CircuitBreaker, is_quota_related, is_timeout_exception

logger = logging.getLogger("oet_agent_gateway")


class _UnavailableAuthAdapter:
    """Stand-in when get_adapter() cannot initialize (e.g. missing GEMINI_API_KEY).

    Process stays up so /v1/healthz can return HTTP 200. Agent calls fail on apply().
    """

    mode = "unavailable"

    def __init__(self, error: AuthAdapterError) -> None:
        self._error = error

    def apply(self, config: Any, model: str) -> Any:
        raise AuthAdapterError(str(self._error)) from self._error


@dataclass
class _Session:
    id: str
    agent_name: str
    agent: Any = None  # google.antigravity.Agent
    lock: asyncio.Lock = field(default_factory=asyncio.Lock)
    last_used: float = field(default_factory=time.monotonic)


class _AgentPool:
    """LRU pool of live agent sessions. One harness process per slot."""

    def __init__(
        self,
        settings: Settings,
        adapter: AuthAdapter,
        agent_factory: Any | None = None,
    ) -> None:
        self._settings = settings
        self._adapter = adapter
        # Test seam: defaults to google.antigravity.Agent at first use so the
        # SDK is imported lazily and unit tests can inject a stub factory.
        self._agent_factory = agent_factory
        self._sessions: dict[str, _Session] = {}
        self._lru: list[str] = []
        self._created = 0
        self._evicted = 0
        self._reaped = 0

    @property
    def stats(self) -> dict[str, int]:
        return {
            "alive": len(self._sessions),
            "created": self._created,
            "evicted": self._evicted,
            "reaped": self._reaped,
        }

    def get(self, agent_name: str) -> _Session | None:
        return self._sessions.get(agent_name)

    def all_sessions(self) -> list[_Session]:
        return list(self._sessions.values())

    def _close(self, session: _Session) -> None:
        """Drop bookkeeping for a session. Caller owns harness teardown."""
        self._sessions.pop(session.agent_name, None)
        try:
            self._lru.remove(session.agent_name)
        except ValueError:
            pass

    @staticmethod
    async def _teardown_agent(agent: Any) -> None:
        try:
            await agent.__aexit__(None, None, None)
        except Exception:  # noqa: BLE001 - teardown best-effort
            pass

    async def acquire(self, agent_name: str) -> _Session:
        spec = list_specs(self._settings).get(agent_name)
        if spec is None:
            raise HTTPException(
                status_code=400,
                detail=f"Unknown agent '{agent_name}'. Available: {sorted(list_specs(self._settings))}",
            )
        session = self._sessions.get(agent_name)
        if session is None:
            await self._evict_if_needed()
            session = _Session(id=uuid.uuid4().hex, agent_name=agent_name)
            try:
                config = build_config(spec, self._settings)
                config = self._adapter.apply(config, spec.model)
                factory = self._agent_factory
                if factory is None:
                    from google.antigravity import Agent

                    factory = Agent
                agent = factory(config)
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
        """Make room for a new session. Never evicts a session whose lock is
        held (in-flight turn): an over-cap moment beats corrupting live work."""
        if len(self._sessions) < self._settings.max_sessions:
            return
        for name in list(self._lru):
            session = self._sessions.get(name)
            if session is None:
                continue
            if session.lock.locked():
                continue
            self._close(session)
            if session.agent is not None:
                # Fire-and-forget: the request path must not block on harness
                # shutdown; errors are swallowed inside.
                asyncio.create_task(self._teardown_agent(session.agent))
            self._evicted += 1
            return

    async def reap_idle(self, idle_ttl_seconds: float) -> int:
        """Close sessions unused for longer than the TTL. Called by the
        lifespan background sweeper; teardowns are awaited (bounded count)."""
        now = time.monotonic()
        stale = [
            s
            for s in list(self._sessions.values())
            if now - s.last_used > idle_ttl_seconds and not s.lock.locked()
        ]
        for session in stale:
            agent = session.agent
            self._close(session)
            if agent is not None:
                await self._teardown_agent(agent)
            self._reaped += 1
        return len(stale)


async def _maybe_await(value: Any) -> Any:
    return await value if inspect.isawaitable(value) else value


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


async def _with_keepalive(
    source: AsyncIterator[str], keepalive_seconds: float
) -> AsyncIterator[str]:
    """Forward SSE chunks, emitting comment pings while upstream is silent so
    intermediaries (nginx read timeouts, LB idle drains) never cut the stream."""
    queue: asyncio.Queue[Any] = asyncio.Queue(maxsize=512)
    done = object()

    async def pump() -> None:
        try:
            async for item in source:
                await queue.put(item)
        except BaseException as exc:  # noqa: BLE001 - forwarded to consumer
            await queue.put(exc)
        finally:
            await queue.put(done)

    task = asyncio.create_task(pump())
    try:
        while True:
            try:
                item = await asyncio.wait_for(queue.get(), timeout=max(0.05, keepalive_seconds))
            except asyncio.TimeoutError:
                yield ": keepalive\n\n"
                continue
            if item is done:
                break
            if isinstance(item, BaseException):
                raise item
            yield item
    finally:
        if not task.done():
            task.cancel()
        try:
            await task
        except asyncio.CancelledError:
            pass
        except Exception:  # noqa: BLE001 - teardown best-effort
            pass


async def _turn_guard(app: FastAPI, agent_name: str, spec: AgentSpec, est: int) -> str:
    """Shared pre-flight: circuit check + quota check. Returns budget route.
    Raises HTTPException when the turn may not proceed."""
    settings: Settings = app.state.settings
    route = spec.budget_route or spec.name
    metrics: Metrics = app.state.metrics
    allowed, retry_after = await app.state.circuit.check(route)
    if not allowed:
        metrics.inc("oetgw_turns_total", agent=agent_name, outcome="circuit_open")
        raise HTTPException(
            503,
            f"circuit open for '{route}' (upstream failing); retry after {retry_after}s",
            headers={"retry-after": str(int(retry_after) + 1)},
        )
    ok, reason = await app.state.quota.can_spend(route, est)
    if not ok:
        metrics.inc("oetgw_turns_total", agent=agent_name, outcome="quota_rejected")
        raise HTTPException(503, f"quota: {reason}")
    return route


def _track_inflight(app: FastAPI, delta: int) -> None:
    app.state.inflight = max(0, getattr(app.state, "inflight", 0)) + delta
    app.state.metrics.set_gauge("oetgw_inflight_turns", app.state.inflight)


async def _record_turn_result(
    app: FastAPI, route: str, agent_name: str, exc: BaseException | None
) -> None:
    settings: Settings = app.state.settings
    metrics: Metrics = app.state.metrics
    if exc is None:
        await app.state.circuit.record_success(route)
        metrics.inc("oetgw_turns_total", agent=agent_name, outcome="ok")
        return
    if is_timeout_exception(exc):
        outcome = "timeout"
    elif is_quota_related(exc):
        outcome = "quota"
    else:
        outcome = "error"
    opened = await app.state.circuit.record_failure(route)
    if opened:
        metrics.inc("oetgw_circuit_opened_total", route=route)
        logger.error(
            "Circuit OPENED for route '%s' after %d consecutive failure(s)",
            route,
            settings.circuit_failure_threshold,
        )
    metrics.inc("oetgw_turns_total", agent=agent_name, outcome=outcome)


def create_app(
    settings: Settings | None = None,
    pool: _AgentPool | None = None,
    quota_governor: QuotaGovernor | None = None,
    cache: Any | None = None,
    adapter: AuthAdapter | None = None,
) -> FastAPI:
    settings = settings or get_settings()

    @asynccontextmanager
    async def lifespan(app: FastAPI):
        app.state.settings = settings
        app.state.metrics = Metrics()
        app.state.circuit = CircuitBreaker(
            threshold=settings.circuit_failure_threshold,
            cooldown_seconds=settings.circuit_cooldown_seconds,
        )
        app.state.inflight = 0
        if adapter is not None:
            app.state.adapter = adapter
            app.state.auth_ready = True
        else:
            try:
                app.state.adapter = get_adapter(settings)
                app.state.auth_ready = True
            except AuthAdapterError as exc:
                logger.error(
                    "Auth adapter failed to initialize; serving degraded healthz: %s",
                    exc,
                )
                app.state.adapter = _UnavailableAuthAdapter(exc)
                app.state.auth_ready = False
        app.state.pool = pool or _AgentPool(settings, app.state.adapter)
        app.state.quota = quota_governor or QuotaGovernor(settings)
        app.state.cache = cache if cache is not None else create_cache(settings)
        app.state.specs = list_specs(settings)

        async def reaper() -> None:
            while True:
                await asyncio.sleep(max(5.0, settings.session_reap_interval_seconds))
                reap = getattr(app.state.pool, "reap_idle", None)
                if reap is None:
                    continue
                try:
                    closed = await reap(settings.session_idle_ttl_seconds)
                    if closed:
                        logger.info("Reaped %d idle harness session(s)", closed)
                    app.state.metrics.set_gauge(
                        "oetgw_sessions_alive", app.state.pool.stats["alive"]
                    )
                except Exception:  # noqa: BLE001 - sweeper must not die
                    logger.exception("Session reaper iteration failed")

        reaper_task = asyncio.create_task(reaper())
        yield

        # Graceful drain: stop taking new turns implicitly (uvicorn stops
        # accepting connections) and give in-flight turns a bounded window.
        deadline = time.monotonic() + max(0.0, settings.drain_timeout_seconds)
        while getattr(app.state, "inflight", 0) > 0 and time.monotonic() < deadline:
            await asyncio.sleep(0.1)
        if getattr(app.state, "inflight", 0) > 0:
            logger.warning(
                "Shutdown drain elapsed with %d turn(s) still in flight",
                app.state.inflight,
            )
        reaper_task.cancel()
        try:
            await reaper_task
        except asyncio.CancelledError:
            pass
        for session in app.state.pool.all_sessions():
            if session.agent is not None:
                try:
                    await session.agent.__aexit__(None, None, None)
                except Exception:  # noqa: BLE001
                    pass

    app = FastAPI(title="OET Agent Gateway", version=__version__, lifespan=lifespan)

    @app.middleware("http")
    async def http_metrics(request: Request, call_next):
        start = time.perf_counter()
        try:
            response = await call_next(request)
            status = response.status_code
        except HTTPException as exc:
            status = exc.status_code
            raise
        except Exception:
            status = 500
            raise
        finally:
            route_obj = request.scope.get("route")
            path = getattr(route_obj, "path", "unmatched")
            metrics: Metrics = request.app.state.metrics
            metrics.inc(
                "oetgw_http_requests_total",
                method=request.method,
                path=path,
                status=str(status),
            )
            metrics.set_gauge(
                "oetgw_http_request_duration_last_seconds",
                round(time.perf_counter() - start, 6),
                path=path,
            )
        return response

    @app.middleware("http")
    async def internal_auth(request: Request, call_next):
        if settings.internal_service_token:
            if request.method == "GET" and request.url.path == "/v1/healthz":
                return await call_next(request)
            provided = request.headers.get("x-oet-internal-token", "")
            # RegistryBackedProvider uses the existing OpenAI-compatible
            # contract and sends the provider key as Authorization: Bearer.
            # Accept it as the service token without requiring a new .NET
            # provider implementation. Compare in constant time.
            if not provided:
                authorization = request.headers.get("authorization", "")
                if authorization.lower().startswith("bearer "):
                    provided = authorization[7:].strip()
            if not secrets.compare_digest(provided, settings.internal_service_token):
                request.app.state.metrics.inc("oetgw_auth_failures_total")
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
        q = {}
        try:
            q = await app.state.quota.health()
        except Exception:  # noqa: BLE001
            pass
        auth_ready = bool(getattr(app.state, "auth_ready", True))
        circuits: dict[str, dict[str, object]] = {}
        try:
            circuits = await app.state.circuit.snapshot()
        except Exception:  # noqa: BLE001
            pass
        degraded_cache = bool(getattr(app.state.cache, "degraded", False))
        any_open = any(c["state"] != "closed" for c in circuits.values())
        return {
            "status": "ok" if (auth_ready and not degraded_cache) else "degraded",
            "version": __version__,
            "auth_mode": settings.auth_mode,
            "auth_ready": auth_ready,
            "agents": len(list_specs(settings)),
            "pool": app.state.pool.stats,
            "quota": q,
            "circuits": circuits,
            "circuit_open": any_open,
            "cache": {
                "backend": type(app.state.cache).__name__,
                "degraded": degraded_cache,
                "size": app.state.cache.size,
            },
            "inflight": getattr(app.state, "inflight", 0),
        }

    @app.get("/v1/readyz")
    async def readyz():
        """Readiness gate used by Docker and blue/green deployment health checks."""
        payload = await healthz()
        if not payload["auth_ready"]:
            return JSONResponse(status_code=503, content=payload)
        return payload

    @app.get("/v1/metrics", response_class=PlainTextResponse)
    async def metrics_endpoint():
        metrics: Metrics = app.state.metrics
        try:
            alive = app.state.pool.stats["alive"]
        except Exception:  # noqa: BLE001
            alive = 0
        metrics.set_gauge("oetgw_sessions_alive", alive)
        metrics.set_gauge("oetgw_inflight_turns", getattr(app.state, "inflight", 0))
        remaining = None
        try:
            health = await app.state.quota.health()
            remaining = health.get("global_remaining")
        except Exception:  # noqa: BLE001
            pass
        if remaining is not None:
            metrics.set_gauge("oetgw_quota_global_remaining_tokens", remaining)
        return PlainTextResponse(
            metrics.render(),
            media_type="text/plain; version=0.0.4; charset=utf-8",
        )

    @app.get("/v1/agents")
    async def agents():
        return {"agents": specs_manifest(settings)}

    @app.get("/v1/quota")
    async def quota_endpoint():
        try:
            return await app.state.quota.health()
        except Exception:  # noqa: BLE001
            return {}

    @app.post("/v1/chat/completions")
    async def chat_completions(body: dict[str, Any]):
        return await _chat_completions_impl(app, body, stream=bool(body.get("stream", False)))

    async def _validate_messages(messages: Any) -> tuple[list[str], list[str]]:
        if not isinstance(messages, list) or not messages:
            raise HTTPException(400, "messages must be a non-empty array.")
        if len(messages) > settings.max_messages_per_request:
            raise HTTPException(
                413,
                f"too many messages ({len(messages)} > {settings.max_messages_per_request})",
            )
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
        if len(prompt) + len(system) > settings.max_prompt_chars:
            raise HTTPException(
                413,
                f"payload too large ({len(prompt) + len(system)} chars > {settings.max_prompt_chars})",
            )
        return system, prompt

    async def _chat_completions_impl(app: FastAPI, body: dict[str, Any], stream: bool):
        agent_name = _parse_agent_name(body.get("model"))
        spec = app.state.specs.get(agent_name) or list_specs(settings).get(agent_name)
        if spec is None:
            raise HTTPException(400, f"Unknown agent '{agent_name}'.")

        system, prompt = await _validate_messages(body.get("messages"))

        convo_key = cache_key(agent_name, body.get("messages") or [], system)
        cached = await _maybe_await(app.state.cache.get(convo_key))
        if cached is not None:
            app.state.metrics.inc("oetgw_cache_hits_total", agent=agent_name)
            if stream:
                return StreamingResponse(_sse_from_text(cached), media_type="text/event-stream")
            return _openai_result(cached, spec.model, cached_hit=True)

        enriched = f"[System context]\n{system}\n\n{prompt}" if system else prompt
        est = estimate_tokens(enriched)
        route = await _turn_guard(app, agent_name, spec, est)

        session = await app.state.pool.acquire(agent_name)
        _track_inflight(app, +1)
        async with session.lock:
            spec_ctx = _TurnContext(app, session, route, spec, enriched, est)
            try:
                result = await _run_turn(app, session, spec_ctx)
            except asyncio.CancelledError:
                app.state.metrics.inc("oetgw_turns_total", agent=agent_name, outcome="cancelled")
                raise
            except Exception as exc:  # noqa: BLE001
                await _record_turn_result(app, route, agent_name, exc)
                logger.warning(
                    "agent turn failed (agent=%s, outcome=%s)",
                    agent_name,
                    "timeout" if is_timeout_exception(exc) else ("quota" if is_quota_related(exc) else "error"),
                )
                raise _upgrade_error(exc, settings.turn_timeout_seconds) from exc
            finally:
                _track_inflight(app, -1)
        await _record_turn_result(app, route, agent_name, None)

        text = result["text"]
        if text:
            await _maybe_await(app.state.cache.put(convo_key, text))
        if stream:
            return StreamingResponse(_sse_from_text(text), media_type="text/event-stream")
        return _openai_result(text, spec.model, cached_hit=False, usage=result.get("usage"))

    @app.post("/v1/sessions")
    async def create_session(body: dict[str, Any]):
        agent_name = _parse_agent_name(body.get("agent", body.get("model", "")))
        await app.state.pool.acquire(agent_name)
        session = app.state.pool.get(agent_name)
        if session is None:
            raise HTTPException(500, "session vanished immediately after acquire")
        return {"session_id": session.id, "agent": agent_name, "pool": app.state.pool.stats}

    @app.post("/v1/sessions/{session_id}/messages")
    async def session_message(session_id: str, body: dict[str, Any]):
        session: _Session | None = next(
            (s for s in app.state.pool.all_sessions() if s.id == session_id), None
        )
        if session is None:
            raise HTTPException(404, "unknown session")
        text = str(body.get("text", "")).strip()
        if not text:
            raise HTTPException(400, "text is required")
        if len(text) > settings.max_prompt_chars:
            raise HTTPException(413, f"payload too large ({len(text)} chars)")

        spec = app.state.specs.get(session.agent_name) or list_specs(settings).get(session.agent_name)
        if spec is None:
            raise HTTPException(400, "session agent is not enabled")
        est = estimate_tokens(text)
        route = await _turn_guard(app, session.agent_name, spec, est)

        _track_inflight(app, +1)

        async def stream() -> AsyncIterator[str]:
            try:
                # NOTE: _native_stream owns session.lock (non-reentrant), so the
                # wrapper here must not acquire it again.
                async for chunk in _with_keepalive(
                    _native_stream(app, session, spec, text, route),
                    settings.sse_keepalive_seconds,
                ):
                    yield chunk
            except asyncio.CancelledError:
                app.state.metrics.inc(
                    "oetgw_turns_total", agent=session.agent_name, outcome="cancelled"
                )
                raise
            finally:
                _track_inflight(app, -1)
                session.last_used = time.monotonic()

        return StreamingResponse(stream(), media_type="text/event-stream")

    return app


async def _native_stream(
    app: FastAPI,
    session: _Session,
    spec: AgentSpec,
    prompt: str,
    route: str,
) -> AsyncIterator[str]:
    error: Exception | None = None
    async with session.lock:
        yield _event("session", session_id=session.id, agent=spec.name)
        try:
            async for event in _stream_events(app, session, prompt, route):
                yield _event(event["type"], **{k: v for k, v in event.items() if k != "type"})
        except Exception as exc:  # noqa: BLE001 - forward as SSE error; CancelledError propagates
            error = exc
            yield _event("error", error=str(exc))
        finally:
            session.last_used = time.monotonic()
    await _record_turn_result(app, route, spec.name, error)
    yield _event("done", pool=app.state.pool.stats)


async def _run_turn(
    app: FastAPI,
    session: _Session,
    ctx: "_TurnContext",
) -> dict[str, Any]:
    """Execute a turn and return the aggregated text (+ usage accounting)."""
    text_parts: list[str] = []
    usage: dict[str, int] | None = None
    async for event in _stream_events(app, session, ctx.enriched, ctx.route):
        if event["type"] == "content":
            text_parts.append(event["delta"])
        elif event["type"] == "usage":
            usage = event["usage"]
    text = "".join(text_parts)
    completion_est = estimate_tokens(text)
    await app.state.quota.spend(ctx.route, ctx.est + completion_est)
    await app.state.quota.reset(ctx.route)
    resolved = usage or {
        "prompt_tokens": ctx.est,
        "completion_tokens": completion_est,
    }
    return {"text": text, "usage": resolved}


async def _stream_events(
    app: FastAPI,
    session: _Session,
    enriched: str,
    route: str,
) -> AsyncIterator[dict[str, Any]]:
    """One agent turn as a stream of typed events (content/thought/tool_call/retry/usage).

    `enriched` is the exact upstream prompt (system context already merged),
    so quota accounting and reported usage measure what is really sent.
    """
    agent_name = session.agent_name

    attempt = 0
    while True:
        try:
            response = await asyncio.wait_for(
                session.agent.chat(enriched),
                timeout=app.state.settings.turn_timeout_seconds,
            )
            break
        except asyncio.CancelledError:
            raise
        except Exception as exc:  # noqa: BLE001 - retry policy applied
            retryable = is_quota_related(exc) and attempt < app.state.settings.retry_max_attempts
            if isinstance(exc, asyncio.TimeoutError) and attempt < app.state.settings.retry_max_attempts:
                # A single hung harness call retried once is often transient.
                retryable = True
            if not retryable:
                raise
            attempt += 1
            delay = await app.state.quota.backoff_delay(route)
            app.state.metrics.inc("oetgw_retries_total", route=route)
            yield {"type": "retry", "attempt": attempt, "delay_s": round(delay, 2)}
            await asyncio.sleep(delay)

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


async def _sse_from_text(text: str) -> AsyncIterator[str]:
    for line in text.split("\n"):
        if line:
            yield f"data: {json.dumps({'choices': [{'delta': {'content': line}}]})}\n\n"
    yield "data: [DONE]\n\n"


def _openai_result(
    text: str,
    model: str,
    cached_hit: bool,
    usage: dict[str, int] | None = None,
) -> dict[str, Any]:
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
        "usage": usage or _openai_usage(text, text),
        "cached": cached_hit,
    }


class _TurnContext:
    def __init__(
        self,
        app: FastAPI,
        session: _Session,
        route: str,
        spec: AgentSpec,
        enriched: str,
        est: int,
    ) -> None:
        self.app = app
        self.session = session
        self.route = route
        self.spec = spec
        self.enriched = enriched
        self.est = est


def _upgrade_error(exc: Exception, turn_timeout_seconds: float = 120.0) -> Exception:
    if is_timeout_exception(exc):
        return HTTPException(504, f"upstream turn timed out after {turn_timeout_seconds}s")
    if is_quota_related(exc):
        return HTTPException(503, f"upstream quota exhausted: {exc}")
    return HTTPException(500, f"agent error: {type(exc).__name__}")
