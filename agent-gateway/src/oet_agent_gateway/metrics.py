"""Minimal Prometheus-compatible metrics (no external dependency).

Counters and gauges are process-local; with N gateway replicas each exposes
its own /v1/metrics and Prometheus aggregates by instance. Label cardinality
is bounded by design: agent/route names come from the fixed spec registry,
outcomes from a fixed enum, HTTP paths from matched route templates only.
"""
from __future__ import annotations

from threading import Lock

Labels = tuple[tuple[str, str], ...]

_MAX_LABEL_VALUE_LEN = 120


def _escape_label(value: str) -> str:
    value = value[:_MAX_LABEL_VALUE_LEN]
    return value.replace("\\", "\\\\").replace('"', '\\"').replace("\n", " ")


class Metrics:
    def __init__(self) -> None:
        self._lock = Lock()
        self._counters: dict[tuple[str, Labels], float] = {}
        self._gauges: dict[tuple[str, Labels], float] = {}

    def inc(self, name: str, value: float = 1.0, **labels: str) -> None:
        key = (name, tuple(sorted(labels.items())))
        with self._lock:
            self._counters[key] = self._counters.get(key, 0.0) + value

    def set_gauge(self, name: str, value: float, **labels: str) -> None:
        key = (name, tuple(sorted(labels.items())))
        with self._lock:
            self._gauges[key] = value

    @staticmethod
    def _render_group(
        family: str, kind: str, help_text: str, store: dict[tuple[str, Labels], float]
    ) -> list[str]:
        lines = [f"# HELP {family} {help_text}", f"# TYPE {family} {kind}"]
        for (name, labels), value in sorted(store.items()):
            if name != family:
                continue
            label_str = ""
            if labels:
                label_str = "{" + ",".join(f'{k}="{_escape_label(v)}"' for k, v in labels) + "}"
            shown = int(value) if float(value).is_integer() else round(value, 6)
            lines.append(f"{family}{label_str} {shown}")
        return lines

    def render(self) -> str:
        with self._lock:
            counters = dict(self._counters)
            gauges = dict(self._gauges)
        lines: list[str] = []
        for family in sorted({name for name, _ in counters}):
            lines.extend(
                self._render_group(
                    family, "counter", f"{family.removeprefix('oetgw_')} total", counters
                )
            )
        for family in sorted({name for name, _ in gauges}):
            lines.extend(self._render_group(family, "gauge", family.removeprefix("oetgw_"), gauges))
        return "\n".join(lines) + ("\n" if lines else "")
