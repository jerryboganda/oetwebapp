"""OET agent registry: personas, capabilities, skills, and safety defaults.

Each spec maps a product AI capability (the same names used by the .NET
AiFeatureRouteResolver) to an Antigravity agent configuration:
  * persona: OET domain expert system instructions (skills/<name>/persona.txt)
  * skills: SKILL.md packages distilled from the OET rulebooks
  * model + thinking level
  * tool safety: student-facing agents deny web/file/shell tools
  * response_schema: Pydantic structured output matching product scoring DTOs
"""
from __future__ import annotations

import dataclasses
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from google.antigravity import BuiltinTools, CapabilitiesConfig, LocalAgentConfig

from .config import Settings
from .schemas import SCHEMAS

PACKAGE_ROOT = Path(__file__).resolve().parent
SKILLS_ROOT = PACKAGE_ROOT / "skills"
PERSONA_FALLBACK = (
    "You are an OET expert assistant for the OET with Dr. Hesham platform. "
    "Follow the rulebook skill instructions exactly and produce valid JSON."
)


def _load_persona(name: str) -> str:
    path = SKILLS_ROOT / name / "persona.txt"
    if path.exists():
        return path.read_text(encoding="utf-8")
    return PERSONA_FALLBACK


def _capabilities_for(spec: "AgentSpec", allow_web: bool) -> CapabilitiesConfig:
    disabled = set(spec.disabled_tools)
    if not allow_web:
        disabled.update({BuiltinTools.SEARCH_WEB, BuiltinTools.READ_URL_CONTENT})
    if not spec.allow_filesystem:
        disabled.update(
            {
                BuiltinTools.LIST_DIR,
                BuiltinTools.SEARCH_DIR,
                BuiltinTools.FIND_FILE,
                BuiltinTools.VIEW_FILE,
                BuiltinTools.CREATE_FILE,
                BuiltinTools.EDIT_FILE,
                BuiltinTools.RUN_COMMAND,
            }
        )
    enabled = None if spec.tools == "*" else spec.tools
    return CapabilitiesConfig(
        enabled_tools=list(enabled) if enabled else None,
        disabled_tools=list(disabled) if disabled else None,
        enable_subagents=spec.allow_subagents,
    )


@dataclass(frozen=True)
class AgentSpec:
    name: str
    description: str
    persona: str = ""
    skills: tuple[str, ...] = ()
    model: str = "gemini-3.7-flash"
    thinking_level: str = "MINIMAL"
    tools: tuple[Any, ...] = "*"
    disabled_tools: tuple[BuiltinTools, ...] = ()
    allow_filesystem: bool = False
    allow_subagents: bool = False
    response_schema: type | None = None
    # SCHEMAS key (schemas.py). When the agent is listed in
    # settings.structured_output_agents, list_specs() promotes this schema
    # onto response_schema so the SDK enforces it server-side.
    structured_schema: str = ""
    budget_route: str = ""

    @property
    def system_instructions(self) -> str:
        return self.persona or _load_persona(self.name)


AGENT_SPECS: dict[str, AgentSpec] = {}


def register(spec: AgentSpec) -> None:
    AGENT_SPECS[spec.name] = spec


def register_default_specs() -> None:
    """Idempotent default agent suite.

    Names map 1:1 to product AI features (see backend AiFeatureCodes):
    writing.grade / writing.sample_score -> writing-examiner
    speaking.patient.turn.v1            -> speaking-interlocutor
    pronunciation.tip / .feedback       -> pronunciation-coach
    admin.grammar_draft / grammar coach -> grammar-tutor
    admin.reading_draft / drill.draft   -> reading-item-generator
    admin.listening_draft               -> listening-item-generator
    card.draft.v1 / drill.draft.v1      -> drill-author
    mock.full_grade analysis            -> mock-analysis
    """
    defaults = [
        AgentSpec(
            name="writing-examiner",
            description="OET Writing scoring / dual assessment (writing.grade, writing.sample_score, writing.drill.grade.v1)",
            skills=("writing-examiner",),
            structured_schema="writing-scoring",
            budget_route="writing-examiner",
        ),
        AgentSpec(
            name="speaking-interlocutor",
            description="AI patient role-play loop (speaking.patient.turn.v1, conversation.opening/reply)",
            skills=("speaking-interlocutor",),
            model="gemini-3.7-flash",
            structured_schema="patient-utterance",
        ),
        AgentSpec(
            name="pronunciation-coach",
            description="Transcript-based pronunciation coaching (pronunciation.tip, pronunciation.feedback)",
            skills=("pronunciation-coach",),
        ),
        AgentSpec(
            name="grammar-tutor",
            description="Grammar remediation + admin grammar drafting (admin.grammar_draft, recalls grammar help)",
            skills=("grammar-tutor",),
        ),
        AgentSpec(
            name="reading-item-generator",
            description="OET reading item authoring (admin.reading_draft, reading.explanation.v1)",
            skills=("reading-item-generator",),
        ),
        AgentSpec(
            name="listening-item-generator",
            description="OET listening item authoring (admin.listening_draft, listening.explanation.v1)",
            skills=("listening-item-generator",),
        ),
        AgentSpec(
            name="drill-author",
            description="Speaking role-play card & drill drafting (card.draft.v1, drill.draft.v1)",
            skills=("drill-author",),
            model="gemini-3.7-flash",
        ),
        AgentSpec(
            name="mock-analysis",
            description="Mock full-grade review analysis (mock.full_grade, mock.remediation_draft)",
            skills=("mock-analysis",),
        ),
    ]
    for spec in defaults:
        register(spec)


register_default_specs()


def list_specs(settings: Settings) -> dict[str, AgentSpec]:
    specs = AGENT_SPECS
    allowed = settings.enabled_agent_names
    if allowed is not None:
        specs = {name: spec for name, spec in specs.items() if name in allowed}
    structured = settings.structured_agents
    if structured:
        promoted = {}
        for name, spec in specs.items():
            if (
                spec.response_schema is None
                and spec.structured_schema
                and name in structured
                and spec.structured_schema in SCHEMAS
            ):
                spec = dataclasses.replace(spec, response_schema=SCHEMAS[spec.structured_schema])
            promoted[name] = spec
        specs = promoted
    return dict(specs)


def build_config(spec: AgentSpec, settings: Settings) -> LocalAgentConfig:
    config = LocalAgentConfig(
        system_instructions=spec.system_instructions,
        capabilities=_capabilities_for(spec, settings.allow_web_tools),
        model=spec.model,
    )
    if spec.skills:
        skills_paths = [str(SKILLS_ROOT / name) for name in spec.skills]
        config.skills_paths = skills_paths
    return config


def specs_manifest(settings: Settings) -> list[dict[str, Any]]:
    return [
        {
            "name": spec.name,
            "description": spec.description,
            "model": spec.model,
            "budget_route": spec.budget_route or spec.name,
            "structured": spec.response_schema is not None,
            "schema": (spec.response_schema.model_json_schema() if spec.response_schema else None),
        }
        for spec in list_specs(settings).values()
    ]
