"""KAI charter storage — in-memory store with Foundry IQ integration points.

This module provides charter/template CRUD operations. In production, these
operations map to Foundry IQ knowledge store. For local dev, an in-memory
store is used with seed data.
"""

from __future__ import annotations

import json
import logging
import uuid
from datetime import datetime, timezone
from typing import Any

logger = logging.getLogger(__name__)

# In-memory stores (replaced by Foundry IQ in production)
_charters: dict[str, dict[str, Any]] = {}
_templates: dict[str, dict[str, Any]] = {}


def _seed_data():
    """Seed the in-memory store with sample templates and charters."""
    if _templates:
        return

    # Templates
    _templates["tmpl-standard"] = {
        "id": "tmpl-standard",
        "event_type": "standard_kaizen",
        "name": "Standard Kaizen Event",
        "description": "A focused 3-5 day improvement event targeting a specific process.",
        "field_guidance": {
            "problem_statement": {
                "description": "A data-driven statement of the gap between current and target performance.",
                "tips": [
                    "Include at least one quantified metric (%, $, days, count)",
                    "Describe current state vs. target state",
                    "Do NOT embed a solution in the problem statement",
                    "Focus on the process, not people",
                ],
            },
            "kpi_target": {
                "description": "The primary measurable target for this event.",
                "tips": [
                    "Use format: 'Reduce X from [baseline] to [target]'",
                    "Include unit of measure",
                    "Base target on data, not guesses",
                ],
            },
            "scope_description": {
                "description": "Clear boundaries of what is in and out of scope.",
                "tips": [
                    "Define start and end points of the process",
                    "State what is explicitly out of scope",
                    "Keep scope achievable in the event timeframe",
                ],
            },
        },
        "example_charters": [
            {
                "title": "Shipping Label Error Reduction",
                "event_type": "standard_kaizen",
                "problem_statement": "Shipping label errors on Line 3 are occurring at a rate of 4.2% (84 errors per 2,000 shipments/week) against a target of <1%, resulting in $12,400/month in re-shipping costs and 2-day delivery delays for affected customers.",
                "kpi_target": "Reduce shipping label error rate from 4.2% to below 1% within 30 days",
                "scope_description": "From order entry through label print on Line 3, Warehouse B. Excludes carrier-side errors and returns processing.",
                "quality_score": 92,
            },
            {
                "title": "Cycle Time Reduction - Assembly Cell 7",
                "event_type": "standard_kaizen",
                "problem_statement": "Assembly Cell 7 cycle time averages 47 seconds per unit against a takt time of 38 seconds, creating a bottleneck that limits daily output to 680 units vs. demand of 820 units.",
                "kpi_target": "Reduce cycle time from 47s to 38s or below to meet daily demand of 820 units",
                "scope_description": "Assembly Cell 7 operations from component staging through final inspection. Excludes upstream fabrication and downstream packaging.",
                "quality_score": 88,
            },
        ],
    }

    _templates["tmpl-problem-solving"] = {
        "id": "tmpl-problem-solving",
        "event_type": "problem_solving",
        "name": "Problem Solving (A3)",
        "description": "A structured approach to complex problems using A3 methodology.",
        "field_guidance": {
            "problem_statement": {
                "description": "A clear articulation of the problem requiring root cause analysis.",
                "tips": [
                    "Separate the symptom from the root cause",
                    "Include data showing the trend over time",
                    "Quantify the business impact",
                ],
            },
        },
        "example_charters": [],
    }

    _templates["tmpl-vsm"] = {
        "id": "tmpl-vsm",
        "event_type": "value_stream_mapping",
        "name": "Value Stream Mapping",
        "description": "End-to-end process mapping to identify waste and design future state.",
        "field_guidance": {
            "scope_description": {
                "description": "The value stream boundaries from trigger to delivery.",
                "tips": [
                    "Define the product family or service being mapped",
                    "Identify the starting trigger and ending delivery point",
                    "Include both information and material flows",
                ],
            },
        },
        "example_charters": [],
    }

    # Sample charters
    _charters["charter-001"] = {
        "id": "charter-001",
        "title": "Reduce Patient Wait Times in ED Triage",
        "event_type": "standard_kaizen",
        "status": "completed",
        "problem_statement": "Average patient wait time from arrival to triage assessment is 23 minutes against a target of 10 minutes, with 15% of patients waiting over 45 minutes. This contributes to 8 LWBS (Left Without Being Seen) cases per week.",
        "kpi_target": "Reduce average triage wait time from 23 minutes to 10 minutes",
        "kpi_actual": "Achieved 12 minutes average (48% reduction)",
        "scope_description": "From patient arrival at ED registration through completion of initial triage assessment. Excludes waiting for physician and treatment.",
        "process_name": "ED Triage Process",
        "process_mapped": True,
        "facilitator": "Sarah Chen",
        "sponsor": "Dr. James Wilson",
        "team_members": [
            {"name": "Sarah Chen", "role": "Facilitator"},
            {"name": "Maria Rodriguez", "role": "Charge Nurse"},
            {"name": "Tom Harris", "role": "Registration Lead"},
        ],
        "quality_score": 85,
        "organization": "Memorial Health",
        "business_unit": "Emergency Department",
        "created_at": "2024-11-01T10:00:00Z",
        "updated_at": "2024-12-15T14:30:00Z",
    }

    _charters["charter-002"] = {
        "id": "charter-002",
        "title": "First Pass Yield Improvement - PCB Assembly",
        "event_type": "standard_kaizen",
        "status": "in_progress",
        "problem_statement": "First pass yield on PCB assembly line is 91.2% against a target of 98%, resulting in approximately $67,000/month in rework labor and material waste.",
        "kpi_target": "Improve first pass yield from 91.2% to 98%",
        "scope_description": "SMT placement through automated optical inspection on Lines 1 and 2.",
        "process_name": "PCB Assembly",
        "facilitator": "Mike Johnson",
        "team_members": [
            {"name": "Mike Johnson", "role": "Facilitator"},
            {"name": "Lin Wei", "role": "Process Engineer"},
        ],
        "quality_score": 72,
        "organization": "TechManufacturing Inc",
        "business_unit": "Electronics Division",
        "created_at": "2025-01-15T09:00:00Z",
        "updated_at": "2025-02-01T11:00:00Z",
    }

    logger.info("Seeded %d templates and %d sample charters", len(_templates), len(_charters))


# Ensure seed data is loaded
_seed_data()


# ---------------------------------------------------------------------------
# Templates
# ---------------------------------------------------------------------------

async def list_templates(event_type: str | None = None) -> list[dict[str, Any]]:
    """List charter templates, optionally filtered by event type."""
    results = list(_templates.values())
    if event_type:
        results = [t for t in results if t.get("event_type") == event_type]
    return sorted(results, key=lambda t: t.get("name", ""))


async def get_template(template_id: str) -> dict[str, Any] | None:
    return _templates.get(template_id)


# ---------------------------------------------------------------------------
# Charters
# ---------------------------------------------------------------------------

async def create_charter(data: dict[str, Any]) -> dict[str, Any]:
    """Create a new charter and return it with its generated ID."""
    charter_id = f"charter-{uuid.uuid4().hex[:8]}"
    now = datetime.now(timezone.utc).isoformat()
    charter = {
        "id": charter_id,
        "status": "draft",
        "created_at": now,
        "updated_at": now,
        **data,
    }
    _charters[charter_id] = charter
    return charter


async def get_charter(charter_id: str) -> dict[str, Any] | None:
    return _charters.get(charter_id)


async def list_charters(
    status: str | None = None,
    event_type: str | None = None,
    limit: int = 50,
    offset: int = 0,
) -> list[dict[str, Any]]:
    """List charters with optional filters."""
    results = list(_charters.values())
    if status:
        results = [c for c in results if c.get("status") == status]
    if event_type:
        results = [c for c in results if c.get("event_type") == event_type]
    results.sort(key=lambda c: c.get("updated_at", ""), reverse=True)
    return results[offset:offset + limit]


async def update_charter(charter_id: str, data: dict[str, Any]) -> dict[str, Any] | None:
    """Update charter fields. Returns the updated charter or None if not found."""
    charter = _charters.get(charter_id)
    if not charter:
        return None
    charter.update(data)
    charter["updated_at"] = datetime.now(timezone.utc).isoformat()
    return charter


async def search_charters(
    query: str,
    event_type: str | None = None,
    limit: int = 5,
) -> list[dict[str, Any]]:
    """Search charters by text matching on key fields."""
    query_lower = query.lower()
    results = []

    for charter in _charters.values():
        if event_type and charter.get("event_type") != event_type:
            continue

        # Simple text search across key fields
        searchable = " ".join(
            str(charter.get(f, "") or "")
            for f in ["title", "problem_statement", "scope_description", "process_name", "kpi_target"]
        ).lower()

        if query_lower in searchable or any(word in searchable for word in query_lower.split()):
            results.append(charter)

    results.sort(key=lambda c: c.get("quality_score") or 0, reverse=True)
    return results[:limit]


# ---------------------------------------------------------------------------
# Progress computation
# ---------------------------------------------------------------------------

_SECTION_FIELDS = {
    "summary_scope": ["title", "problem_statement", "scope_description", "schedule_start", "schedule_end", "process_name", "process_mapped", "kpi_target", "kpi_actual", "kpi_gap", "kpi_trend"],
    "metrics": ["metrics", "deliverables"],
    "milestones": ["daily_milestones"],
    "team": ["team_members", "facilitator", "sponsor"],
    "obstacles": ["obstacles"],
    "sustainability": ["sustainability_metrics", "follow_up_plan"],
    "supporting": ["notes"],
}


def compute_progress(charter: dict[str, Any]) -> dict[str, Any]:
    """Compute completion progress for a charter."""
    total = 0
    completed = 0
    sections: dict[str, dict[str, Any]] = {}

    for section, fields in _SECTION_FIELDS.items():
        section_total = len(fields)
        section_done = 0
        for f in fields:
            total += 1
            val = charter.get(f)
            if val is not None and val != "" and val != [] and val != {}:
                completed += 1
                section_done += 1
        sections[section] = {
            "total": section_total,
            "completed": section_done,
            "percentage": round(section_done / section_total * 100) if section_total else 0,
        }

    return {
        "total_fields": total,
        "completed_fields": completed,
        "percentage": round(completed / total * 100) if total else 0,
        "sections": sections,
    }
