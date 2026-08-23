from oet_agent_gateway.auth import GeminiKeyAuth, LocalOAuthAuth, get_adapter, AuthAdapterError
from oet_agent_gateway.config import Settings


def test_gemini_key_mode(settings):
    adapter = get_adapter(settings)
    assert adapter.mode == "gemini-key"
    assert isinstance(adapter, GeminiKeyAuth)


def test_gemini_key_requires_key():
    s = Settings(auth_mode="gemini-key", gemini_api_key="", agents_enabled="*")
    try:
        get_adapter(s)
    except AuthAdapterError as exc:
        assert "GEMINI_API_KEY" in str(exc)
    else:
        raise AssertionError("expected AuthAdapterError")


def test_local_oauth_gated_by_default():
    s = Settings(auth_mode="local-oauth", local_oauth_allowed=False, agents_enabled="*")
    try:
        get_adapter(s)
    except AuthAdapterError as exc:
        assert "not allowed" in str(exc).lower()
    else:
        raise AssertionError("expected AuthAdapterError")


def test_local_oauth_requires_token():
    s = Settings(
        auth_mode="local-oauth",
        local_oauth_allowed=True,
        local_oauth_token="",
        agents_enabled="*",
    )
    try:
        LocalOAuthAuth(s)._token()
    except AuthAdapterError as exc:
        assert "no agy oauth token" in str(exc).lower()
    else:
        raise AssertionError("expected AuthAdapterError")


def test_local_oauth_apply_sets_cloudcode_endpoint():
    from google.antigravity import LocalAgentConfig, types

    s = Settings(
        auth_mode="local-oauth",
        local_oauth_allowed=True,
        local_oauth_token="Bearer.testtoken",
        agents_enabled="*",
    )
    adapter = LocalOAuthAuth(s)
    config = LocalAgentConfig(model="gemini-3-flash")
    out = adapter.apply(config, "gemini-3-flash")
    assert out.models and isinstance(out.models[0], types.ModelTarget)
    assert out.models[0].endpoint.base_url.startswith("https://daily-cloudcode")
    assert out.models[0].endpoint.http_headers["Authorization"] == "Bearer Bearer.testtoken"


def test_sdk_oauth_mode_stub():
    from oet_agent_gateway.auth import SdkOAuthAuth

    try:
        SdkOAuthAuth().apply(LocalAgentConfigForTest(), "x")
    except AuthAdapterError as exc:
        assert "flip-day" in str(exc)
    else:
        raise AssertionError("expected AuthAdapterError")


def LocalAgentConfigForTest():
    from google.antigravity import LocalAgentConfig

    return LocalAgentConfig(model="x")
