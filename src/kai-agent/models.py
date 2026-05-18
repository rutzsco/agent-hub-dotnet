"""KAI Pydantic models for charter, template, and skill data structures."""

from datetime import date, datetime
from typing import Any, Optional

from pydantic import BaseModel, Field


class TeamMember(BaseModel):
    name: str
    role: str | None = None


class Obstacle(BaseModel):
    description: str
    mitigation: str | None = None


class Metric(BaseModel):
    name: str
    unit: str | None = None
    baseline: str | None = None
    target: str | None = None


class Charter(BaseModel):
    id: str
    title: str | None = None
    event_type: str | None = None
    template_id: str | None = None
    status: str = "draft"
    problem_statement: str | None = None
    scope_description: str | None = None
    schedule_start: date | None = None
    schedule_end: date | None = None
    kpi_target: str | None = None
    kpi_actual: str | None = None
    kpi_gap: str | None = None
    kpi_trend: str | None = None
    process_name: str | None = None
    process_mapped: bool | None = None
    metrics: list[Metric] | None = None
    deliverables: list[str] | None = None
    daily_milestones: list[dict[str, Any]] | None = None
    team_members: list[TeamMember] | None = None
    facilitator: str | None = None
    sponsor: str | None = None
    obstacles: list[Obstacle] | None = None
    sustainability_metrics: list[dict[str, Any]] | None = None
    follow_up_plan: str | None = None
    quality_score: float | None = None
    quality_review: dict[str, Any] | None = None
    organization: str | None = None
    business_unit: str | None = None
    location: str | None = None
    notes: str | None = None
    created_at: datetime | None = None
    updated_at: datetime | None = None


class CharterCreate(BaseModel):
    title: str | None = None
    event_type: str | None = None
    template_id: str | None = None
    problem_statement: str | None = None
    scope_description: str | None = None
    organization: str | None = None
    business_unit: str | None = None
    location: str | None = None


class CharterUpdate(BaseModel):
    title: str | None = None
    event_type: str | None = None
    status: str | None = None
    problem_statement: str | None = None
    scope_description: str | None = None
    schedule_start: date | None = None
    schedule_end: date | None = None
    kpi_target: str | None = None
    kpi_actual: str | None = None
    kpi_gap: str | None = None
    kpi_trend: str | None = None
    process_name: str | None = None
    process_mapped: bool | None = None
    metrics: list[Metric] | None = None
    deliverables: list[str] | None = None
    daily_milestones: list[dict[str, Any]] | None = None
    team_members: list[TeamMember] | None = None
    facilitator: str | None = None
    sponsor: str | None = None
    obstacles: list[Obstacle] | None = None
    sustainability_metrics: list[dict[str, Any]] | None = None
    follow_up_plan: str | None = None
    notes: str | None = None
    organization: str | None = None
    business_unit: str | None = None
    location: str | None = None


class Skill(BaseModel):
    name: str
    description: str
    instructions: str
    source: str = "file"


class SkillCreate(BaseModel):
    name: str
    description: str
    instructions: str
