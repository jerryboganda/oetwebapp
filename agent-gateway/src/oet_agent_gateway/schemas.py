"""Structured-output schemas for OET agent responses.

These mirror the product's parse contracts so downstream services can
consume agent output without translation:
  * writing.grade  -> WritingCriterionScore list (array shape expected by
    WritingDualAssessmentService.ParseAiCriteria - criterionCode, score,
    maxScore, rationale, evidenceQuotes).
  * speaking.patient.turn.v1 -> patient utterance (role-play loop).
  * card.draft.v1 -> role-play card spec consumed by admin draft preview.

response_schema (SDK structured output) is wired per-agent via specs;
if a route needs schema-validated JSON but the SDK schema gate is risky
for a given model, keep response_schema=None and rely on the persona's
explicit JSON contract instead.
"""
from __future__ import annotations

from typing import Literal

from pydantic import BaseModel, Field


class WritingCriterionScore(BaseModel):
    criterionCode: str = Field(description="One of: purpose, content, conciseness, genre, organization, language")
    score: int = Field(ge=0, description="Score awarded for this criterion (0..maxScore).")
    maxScore: int = Field(ge=1, description="Maximum score for the criterion (purpose=3, others=7).")
    rationale: str | None = Field(default=None, description="Two-sentence rationale tying score to evidence.")
    evidenceQuotes: list[str] = Field(default_factory=list, description="Verbatim quoted evidence from the letter.")


class WritingScoringArray(BaseModel):
    """Wrapper for a top-level array of WritingCriterionScore objects.
    The product parser accepts a bare JSON array of criterion objects."""

    criteria: list[WritingCriterionScore] = Field(default_factory=list)


class PatientUtterance(BaseModel):
    """One AI-patient turn in the OET Speaking role-play loop."""

    text: str = Field(description="What the patient says next (role-appropriate, medicine domain).")
    severity: Literal["low", "medium", "high"] = Field(default="medium", description="Clinical urgency hint for the candidate.")


class RoleplayCardSpec(BaseModel):
    """Admin authoring spec for an OET Speaking role-play card."""

    profession: str
    scenario: str = Field(description="One-line clinical scenario.")
    patientOpening: str
    cues: list[str] = Field(default_factory=list)
    expectedCues: list[str] = Field(default_factory=list)
    difficulty: Literal["A", "B", "C"] = "B"


class DrillSpec(BaseModel):
    """Admin authoring spec for a drill item."""

    drillType: Literal["pronunciation", "grammar", "vocabulary", "reading", "listening"]
    item: str
    answerType: Literal["choice", "spoken", "written"]
    distractors: list[str] = Field(default_factory=list)
    feedback: str | None = None


class MockAnalysis(BaseModel):
    """Admin mock-review analysis output."""

    summary: str
    strengths: list[str] = Field(default_factory=list)
    weaknesses: list[str] = Field(default_factory=list)
    nextSteps: list[str] = Field(default_factory=list)


SCHEMAS = {
    "writing-scoring": WritingScoringArray,
    "patient-utterance": PatientUtterance,
    "roleplay-card": RoleplayCardSpec,
    "drill-spec": DrillSpec,
    "mock-analysis": MockAnalysis,
}
