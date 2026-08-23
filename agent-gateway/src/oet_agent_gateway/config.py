"""Runtime settings for the OET agent gateway.

All values are overridable via environment variables with an ``AGENTGATEWAY_``
prefix (or the prefix documented per field). Never pass secrets as CLI args.
"""
from __future__ import annotations

from functools import lru_cache
from typing import Literal

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict

AuthMode = Literal["gemini-key", "local-oauth", "sdk-oauth"]


class Settings(BaseSettings):
    model_config = SettingsConfigDict(
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore",
    )

    # --- auth -------------------------------------------------------------
    # gemini-key : official Mode A - GEMINI_API_KEY (hardened server-side key)
    # local-oauth: Mode B (opt-in) - reuse signed-in agy CLI session token.
    #              Personal AI Pro subscription quota. ToS-risk / account-ban
    #              risk accepted by owner; NEVER enable in production compose.
    # sdk-oauth  : Mode C - flip-day stub; activated when Google ships SDK OAuth
    #              (tracked: google-antigravity/antigravity-sdk-python issue #20).
    auth_mode: AuthMode = "gemini-key"
    gemini_api_key: str = Field(default="", alias="GEMINI_API_KEY")
    gemini_model: str = "gemini-3-flash"
    gemini_thinking_level: Literal["MINIMAL", "LOW", "HIGH"] = "MINIMAL"

    # Mode B knobs
    local_oauth_token: str = Field(default="", alias="LOCAL_OAUTH_TOKEN")
    local_oauth_service: str = "gemini"
    local_oauth_account: str = "antigravity"
    local_oauth_model: str = "gemini-3-flash"
    local_oauth_base_url: str = "https://daily-cloudcode-pa.googleapis.com/v1internal"
    local_oauth_allowed: bool = False

    # --- agent capability -------------------------------------------------
    agents_enabled: str = "*"  # comma list or "*"
    max_sessions: int = 16
    allow_web_tools: bool = False  # scoring integrity: SEARCH_WEB/READ_URL_CONTENT

    # --- quota / hardening ------------------------------------------------
    cache_ttl_seconds: int = 3600
    cache_max_entries: int = 512
    budget_tokens_per_day: int = 4_000_000  # global gateway cap (Mode A/B)
    budget_by_route: str = Field(default="{}", alias="BUDGET_BY_ROUTE")  # JSON: {"writing-examiner": 1000000}
    backoff_base_seconds: float = 1.0
    backoff_max_seconds: float = 30.0
    retry_max_attempts: int = 2

    # --- server ------------------------------------------------------------
    host: str = "0.0.0.0"
    port: int = 8305
    internal_service_token: str = ""  # required header X-Oet-Internal-Token when set

    @property
    def budget_routes(self) -> dict[str, int]:
        import json

        try:
            raw = json.loads(self.budget_by_route)
        except json.JSONDecodeError:
            return {}
        return {str(k): int(v) for k, v in raw.items()}

    @property
    def enabled_agent_names(self) -> set[str] | None:
        if self.agents_enabled.strip() == "*":
            return None
        return {n.strip() for n in self.agents_enabled.split(",") if n.strip()}


@lru_cache(maxsize=1)
def get_settings() -> Settings:
    return Settings()
