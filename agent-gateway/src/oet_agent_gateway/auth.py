"""Pluggable authentication adapters for the Antigravity SDK agent runtime.

Mode A  - gemini-key : official; GEMINI_API_KEY (server-side, hardened).
Mode B  - local-oauth: opt-in personal reuse of the signed-in ``agy`` CLI OAuth
          session (Windows Credential Manager / macOS Keychain) against the
          Cloud Code consumer endpoint. Uses the owner's Google AI Pro
          Antigravity quota. ToS-risk accepted by owner; disabled in production.
Mode C  - sdk-oauth  : flip-day stub for when Google ships first-class SDK OAuth
          (tracked in google-antigravity/antigravity-sdk-python issue #20).
"""
from __future__ import annotations

import base64
import json
from typing import Protocol

from google.antigravity import LocalAgentConfig, types
from google.antigravity.models import GeminiAPIEndpoint

from .config import Settings


class AuthAdapterError(RuntimeError):
    """Raised when the configured auth mode cannot be initialized."""


class AuthAdapter(Protocol):
    mode: str

    def apply(self, config: LocalAgentConfig, model: str) -> LocalAgentConfig: ...


class GeminiKeyAuth:
    """Mode A. Official Gemini API key (hardened server-side, never client-side)."""

    mode = "gemini-key"

    def __init__(self, api_key: str) -> None:
        if not api_key:
            raise AuthAdapterError(
                "auth_mode=gemini-key requires GEMINI_API_KEY (or "
                "AGENTGATEWAY_GEMINI_API_KEY). Set it in the environment or "
                "…/agent-gateway/.env."
            )
        self._api_key = api_key

    def apply(self, config: LocalAgentConfig, model: str) -> LocalAgentConfig:
        config.model = model
        config.api_key = self._api_key
        return config


class LocalOAuthAuth:
    """Mode B. Personal Google account session (AI Pro Antigravity quota).

    Reads the OAuth token stored by the ``agy`` CLI login from the OS secure
    keyring, then routes model calls through that account's session:
    GeminiAPIEndpoint(base_url=<Cloud Code endpoint>, Bearer header,
    api_key=<token>). Opt-in and gated by ``local_oauth_allowed``.

    Owner directive: this is personal-use only; account-ban risk accepted.
    """

    mode = "local-oauth"

    def __init__(self, settings: Settings) -> None:
        if not settings.local_oauth_allowed:
            raise AuthAdapterError(
                "auth_mode=local-oauth is not allowed. Set "
                "AGENTGATEWAY_LOCAL_OAUTH_ALLOWED=true to opt in (personal "
                "use only; see docs/antigravity/auth.md risk notice)."
            )
        self._settings = settings

    def _token(self) -> str:
        if self._settings.local_oauth_token:
            return self._settings.local_oauth_token
        try:
            import keyring

            token = keyring.get_password(
                self._settings.local_oauth_service,
                self._settings.local_oauth_account,
            )
        except Exception as exc:  # pragma: no cover - platform-dependent
            raise AuthAdapterError(
                "Failed to read the agy CLI session from the OS keyring "
                f"({type(exc).__name__}). Sign in with `agy` first, or set "
                "LOCAL_OAUTH_TOKEN."
            ) from exc
        if not token:
            raise AuthAdapterError(
                "No agy OAuth token found in the OS keyring "
                f"(service={self._settings.local_oauth_service!r}, "
                f"account={self._settings.local_oauth_account!r}). Run `agy` "
                "and sign in with the Google AI Pro account first."
            )
        return _decode_token_maybe(token)

    def apply(self, config: LocalAgentConfig, model: str) -> LocalAgentConfig:
        # The SDK also sends the token as api_key so the harness can talk to
        # the Cloud Code consumer endpoint. Bearer header does the real auth.
        token = self._token()
        endpoint = GeminiAPIEndpoint(
            base_url=self._settings.local_oauth_base_url.rstrip("/") + "/",
            http_headers={"Authorization": f"Bearer {token}"},
            api_key=token,
        )
        config.model = None
        config.models = [
            types.ModelTarget(
                name=self._settings.local_oauth_model or model,
                types=[types.ModelType.TEXT],
                endpoint=endpoint,
            )
        ]
        return config


class SdkOAuthAuth:
    """Mode C. Flip-day stub; activated when Google ships SDK OAuth support.

    Flip procedure (see docs/antigravity/roadmap.md):
      1. Confirm release in antigravity.google/changelog and GitHub issue #20.
      2. Implement delegation to the official SDK OAuth client here.
      3. Set AGENTGATEWAY_AUTH_MODE=sdk-oauth everywhere.
      4. Run pytest + golden-set parity; promote via 10% route rollout.
    """

    mode = "sdk-oauth"

    def apply(self, config: LocalAgentConfig, model: str) -> LocalAgentConfig:
        raise AuthAdapterError(
            "auth_mode=sdk-oauth is the flip-day stub. Google has not shipped "
            "first-class SDK OAuth yet (issue #20). Currently use gemini-key "
            "(official) or local-oauth (opt-in, personal)."
        )


def _decode_token_maybe(token: str) -> str:
    """The keyring value is often a base64 envelope; a raw JSON/Bearer-dict
    envelope may embed the access token. Best-effort unwrap; fall back raw."""
    if not token:
        return token
    try:
        padded = token + "=" * (-len(token) % 4)
        decoded = base64.b64decode(padded)
        payload = json.loads(decoded)
        if isinstance(payload, dict):
            for key in ("access_token", "token", "accessToken"):
                if payload.get(key):
                    return str(payload[key])
    except Exception:
        pass
    return token


def get_adapter(settings: Settings) -> AuthAdapter:
    if settings.auth_mode == "gemini-key":
        return GeminiKeyAuth(settings.gemini_api_key)
    if settings.auth_mode == "local-oauth":
        return LocalOAuthAuth(settings)
    if settings.auth_mode == "sdk-oauth":
        return SdkOAuthAuth()
    raise AuthAdapterError(f"Unknown AGENTGATEWAY_AUTH_MODE: {settings.auth_mode!r}")
