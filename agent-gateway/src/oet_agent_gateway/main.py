"""uvicorn entry point: `oet-agent-gateway` console module."""
from __future__ import annotations

import logging

import uvicorn

from .config import get_settings


def main() -> None:
    settings = get_settings()
    logging.basicConfig(level=logging.INFO)
    uvicorn.run(
        "oet_agent_gateway.server:create_app",
        factory=True,
        host=settings.host,
        port=settings.port,
        log_level="info",
    )


if __name__ == "__main__":
    main()
