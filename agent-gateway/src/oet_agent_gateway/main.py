"""uvicorn entry point: `python -m oet_agent_gateway.main`."""
from __future__ import annotations

import json
import logging
import sys

import uvicorn

from .config import get_settings


class JsonFormatter(logging.Formatter):
    """Compact structured logs for Docker log shippers (AGENTGATEWAY_LOG_JSON=1).

    Never logs prompt content, API keys, or tokens - only metadata.
    """

    _RESERVED = {
        "name",
        "msg",
        "args",
        "levelname",
        "levelno",
        "pathname",
        "filename",
        "module",
        "exc_info",
        "exc_text",
        "stack_info",
        "lineno",
        "funcName",
        "created",
        "msecs",
        "relativeCreated",
        "thread",
        "threadName",
        "processName",
        "process",
        "taskName",
    }

    def format(self, record: logging.LogRecord) -> str:
        payload: dict[str, object] = {
            "ts": self.formatTime(record, "%Y-%m-%dT%H:%M:%S%z"),
            "level": record.levelname,
            "logger": record.name,
            "msg": record.getMessage(),
        }
        for key, value in record.__dict__.items():
            if key not in self._RESERVED and not key.startswith("_"):
                try:
                    json.dumps(value)
                    payload[key] = value
                except (TypeError, ValueError):
                    continue
        if record.exc_info:
            payload["exc"] = self.formatException(record.exc_info)[-2000:]
        return json.dumps(payload)


def _configure_logging(log_json: bool) -> None:
    handler = logging.StreamHandler(sys.stdout)
    if log_json:
        handler.setFormatter(JsonFormatter())
    else:
        handler.setFormatter(
            logging.Formatter("%(asctime)s %(levelname)s %(name)s: %(message)s")
        )
    root = logging.getLogger()
    root.handlers[:] = [handler]
    root.setLevel(logging.INFO)


def main() -> None:
    settings = get_settings()
    _configure_logging(settings.log_json)
    uvicorn.run(
        "oet_agent_gateway.server:create_app",
        factory=True,
        host=settings.host,
        port=settings.port,
        log_level="info",
        # Bounded graceful shutdown; the app lifespan drains in-flight turns
        # for drain_timeout_seconds before this ceiling applies.
        timeout_graceful_shutdown=max(5, int(settings.drain_timeout_seconds) + 10),
    )


if __name__ == "__main__":
    main()
