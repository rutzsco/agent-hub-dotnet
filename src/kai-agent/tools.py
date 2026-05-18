"""KAI agent tools — callable functions for charter guidance and coaching."""

from __future__ import annotations

import json
import logging
from typing import Any

from storage import (
    compute_progress,
    get_charter,
    get_template,
    list_templates,
    search_charters,
    update_charter,
)

logger = logging.getLogger(__name__)


async def suggest_for_field(
    field_name: str,
    current_value: str | None = None,
    event_type: str | None = None,
) -> str:
    """Get tips, examples, and past charter excerpts for a specific charter field.

    Args:
        field_name: The charter field to get suggestions for (e.g. "problem_statement", "kpi_target").
        current_value: The user's current input for this field (for validation context).
        event_type: Optional event type to filter relevant examples.

    Returns:
        JSON with tips, field description, examples from past charters.
    """
    templates = await list_templates(event_type=event_type)
    guidance: dict[str, Any] = {"field": field_name, "tips": [], "description": "", "examples": []}

    for tmpl in templates:
        fg = tmpl.get("field_guidance") or {}
        if isinstance(fg, str):
            fg = json.loads(fg)
        if field_name in fg:
            field_info = fg[field_name]
            if isinstance(field_info, str):
                field_info = json.loads(field_info)
            guidance["tips"].extend(field_info.get("tips", []))
            guidance["description"] = field_info.get("description", guidance["description"])

        examples = tmpl.get("example_charters") or []
        if isinstance(examples, str):
            examples = json.loads(examples)
        for ex in examples:
            if field_name in ex and ex[field_name]:
                guidance["examples"].append({
                    "title": ex.get("title", ""),
                    "event_type": ex.get("event_type", ""),
                    "value": ex[field_name],
                    "quality_score": ex.get("quality_score"),
                })

    # Search past charters for additional examples
    if field_name in ("problem_statement", "kpi_target", "scope_description"):
        past = await search_charters(
            query=current_value or field_name,
            event_type=event_type,
            limit=3,
        )
        for c in past:
            if c.get(field_name):
                guidance["examples"].append({
                    "title": c.get("title", ""),
                    "event_type": c.get("event_type", ""),
                    "value": c[field_name],
                    "quality_score": c.get("quality_score"),
                    "source": "past_charter",
                })

    guidance["tips"] = list(dict.fromkeys(guidance["tips"]))

    if current_value:
        guidance["current_value"] = current_value

    return json.dumps(guidance, default=str)


async def validate_input(field_name: str, value: str) -> str:
    """Validate a charter field input and provide quality feedback.

    Args:
        field_name: The charter field being validated (e.g. "problem_statement").
        value: The user's input to validate.

    Returns:
        JSON with quality assessment, issues found, and improvement suggestions.
    """
    issues = []
    suggestions = []
    score = 100

    if field_name == "problem_statement":
        if len(value) < 30:
            issues.append("Problem statement is too short — add specific details")
            score -= 30
        if not any(c.isdigit() for c in value):
            issues.append("Include at least one quantified metric (%, $, days, count)")
            score -= 20
        solution_words = ["need to", "should", "must", "install", "buy", "implement", "upgrade"]
        if any(w in value.lower() for w in solution_words):
            issues.append("Appears to contain an assumed solution — describe the problem, not the fix")
            score -= 25
        blame_words = ["failure to", "not following", "don't care", "lazy"]
        if any(w in value.lower() for w in blame_words):
            issues.append("Avoid blame language — focus on the process, not people")
            score -= 15
        if "target" not in value.lower() and "gap" not in value.lower() and "against" not in value.lower():
            suggestions.append("Consider stating the gap between current and target performance")

    elif field_name == "kpi_target":
        if not any(c.isdigit() for c in value):
            issues.append("KPI target should include a specific number")
            score -= 30
        if "from" not in value.lower() or "to" not in value.lower():
            suggestions.append("Use the format 'Reduce X from [baseline] to [target]' for clarity")

    elif field_name == "scope_description":
        if len(value) < 20:
            issues.append("Scope description is too brief — define boundaries clearly")
            score -= 25
        if "through" not in value.lower() and "from" not in value.lower() and "to" not in value.lower():
            suggestions.append("Define start and end points of the process in scope")

    result = {
        "field": field_name,
        "value": value,
        "score": max(0, score),
        "quality": "good" if score >= 80 else "needs_improvement" if score >= 50 else "poor",
        "issues": issues,
        "suggestions": suggestions,
    }
    return json.dumps(result)


async def search_past_charters(
    query: str,
    event_type: str | None = None,
    limit: int = 5,
) -> str:
    """Search historical charters for relevant examples and learnings.

    Args:
        query: Search text to match against charter problem statements, titles, and scope.
        event_type: Optional filter by event type (e.g. "standard_kaizen", "problem_solving").
        limit: Maximum number of results to return.

    Returns:
        JSON array of matching charters with key fields.
    """
    charters = await search_charters(query, event_type=event_type, limit=limit)

    results = []
    for c in charters:
        results.append({
            "id": c["id"],
            "title": c.get("title"),
            "event_type": c.get("event_type"),
            "status": c.get("status"),
            "problem_statement": c.get("problem_statement"),
            "kpi_target": c.get("kpi_target"),
            "scope_description": c.get("scope_description"),
            "quality_score": c.get("quality_score"),
            "organization": c.get("organization"),
            "business_unit": c.get("business_unit"),
        })

    return json.dumps(results, default=str)


async def get_charter_progress(charter_id: str) -> str:
    """Get completion progress for a charter including per-section breakdown.

    Args:
        charter_id: The ID of the charter to check progress for.

    Returns:
        JSON with total/completed field counts, percentage, and section breakdown.
    """
    charter = await get_charter(charter_id)
    if not charter:
        return json.dumps({"error": f"Charter {charter_id} not found"})

    progress = compute_progress(charter)
    return json.dumps(progress, default=str)


async def generate_content(
    field_name: str,
    context: str,
    event_type: str | None = None,
) -> str:
    """Generate draft content for a charter field based on context provided by the user.

    This tool returns the context and field metadata needed for the LLM to generate
    content. The actual generation happens in the agent's response.

    Args:
        field_name: The charter field to generate content for.
        context: User-provided context, notes, or raw information to base the draft on.
        event_type: Optional event type for more targeted generation.

    Returns:
        JSON with field metadata and context for content generation.
    """
    templates = await list_templates(event_type=event_type)
    field_info: dict[str, Any] = {"field": field_name, "user_context": context}

    for tmpl in templates:
        fg = tmpl.get("field_guidance") or {}
        if isinstance(fg, str):
            fg = json.loads(fg)
        if field_name in fg:
            info = fg[field_name]
            if isinstance(info, str):
                info = json.loads(info)
            field_info["guidance"] = info
            break

    # Include high-quality examples
    examples = []
    past = await search_charters(context, event_type=event_type, limit=3)
    for c in past:
        if c.get(field_name) and c.get("quality_score", 0) and c["quality_score"] >= 75:
            examples.append({
                "value": c[field_name],
                "quality_score": c["quality_score"],
                "title": c.get("title"),
            })
    field_info["high_quality_examples"] = examples

    return json.dumps(field_info, default=str)


async def review_charter(charter_id: str) -> str:
    """Perform a full quality review of a charter, scoring each section.

    Args:
        charter_id: The ID of the charter to review.

    Returns:
        JSON with per-section scores, overall quality score, gaps, and recommendations.
    """
    charter = await get_charter(charter_id)
    if not charter:
        return json.dumps({"error": f"Charter {charter_id} not found"})

    progress = compute_progress(charter)
    review: dict[str, Any] = {
        "charter_id": charter_id,
        "title": charter.get("title"),
        "progress": progress,
        "section_reviews": {},
        "gaps": [],
        "strengths": [],
    }

    # Problem statement review
    ps = charter.get("problem_statement") or ""
    ps_score = 0
    if ps:
        ps_score = 15
        if any(c.isdigit() for c in ps):
            ps_score += 5
        if len(ps) > 80:
            ps_score += 5
    review["section_reviews"]["problem_statement"] = {
        "score": ps_score, "max": 25,
        "present": bool(ps),
        "has_metrics": any(c.isdigit() for c in ps) if ps else False,
    }
    if ps_score < 15:
        review["gaps"].append("Problem statement is missing or lacks data-driven specifics")
    elif ps_score >= 20:
        review["strengths"].append("Strong, data-driven problem statement")

    # Scope & Schedule
    scope_score = 0
    if charter.get("scope_description"):
        scope_score += 5
    if charter.get("schedule_start") and charter.get("schedule_end"):
        scope_score += 5
    if charter.get("process_name"):
        scope_score += 3
    if charter.get("process_mapped"):
        scope_score += 2
    review["section_reviews"]["scope_schedule"] = {"score": scope_score, "max": 15}
    if scope_score < 10:
        review["gaps"].append("Scope or schedule needs more detail")

    # Metrics
    metrics = charter.get("metrics") or []
    if isinstance(metrics, str):
        metrics = json.loads(metrics)
    metrics_score = min(20, len(metrics) * 7) if metrics else 0
    kpi = charter.get("kpi_target")
    if kpi and any(c.isdigit() for c in kpi):
        metrics_score = min(20, metrics_score + 6)
    review["section_reviews"]["metrics"] = {"score": metrics_score, "max": 20, "metric_count": len(metrics)}
    if metrics_score < 10:
        review["gaps"].append("Metrics section needs SMART targets with baselines")

    # Team
    members = charter.get("team_members") or []
    if isinstance(members, str):
        members = json.loads(members)
    team_score = min(15, len(members) * 3)
    if charter.get("facilitator"):
        team_score = min(15, team_score + 3)
    if charter.get("sponsor"):
        team_score = min(15, team_score + 3)
    review["section_reviews"]["team"] = {"score": team_score, "max": 15, "member_count": len(members)}
    if team_score < 10:
        review["gaps"].append("Team composition needs facilitator, sponsor, or more cross-functional members")

    # Obstacles
    obstacles = charter.get("obstacles") or []
    if isinstance(obstacles, str):
        obstacles = json.loads(obstacles)
    obs_score = min(10, len(obstacles) * 5)
    review["section_reviews"]["obstacles"] = {"score": obs_score, "max": 10}

    # Sustainability
    sus_score = 0
    sus = charter.get("sustainability_metrics") or []
    if isinstance(sus, str):
        sus = json.loads(sus)
    if sus:
        sus_score += 8
    if charter.get("follow_up_plan"):
        sus_score += 7
    sus_score = min(15, sus_score)
    review["section_reviews"]["sustainability"] = {"score": sus_score, "max": 15}
    if sus_score < 8:
        review["gaps"].append("Sustainability plan is missing — how will gains be maintained?")

    # Overall score
    total_score = sum(s["score"] for s in review["section_reviews"].values())
    total_max = sum(s["max"] for s in review["section_reviews"].values())
    review["overall_score"] = round(total_score / total_max * 100) if total_max else 0

    if review["overall_score"] >= 90:
        review["quality_tier"] = "excellent"
    elif review["overall_score"] >= 75:
        review["quality_tier"] = "good"
    elif review["overall_score"] >= 50:
        review["quality_tier"] = "needs_improvement"
    else:
        review["quality_tier"] = "incomplete"

    review["recommendation"] = (
        "Ready for execution" if review["overall_score"] >= 90
        else "Minor gaps — can proceed with notes" if review["overall_score"] >= 75
        else "Address gaps before execution" if review["overall_score"] >= 50
        else "Requires substantial revision"
    )

    # Save review to charter
    await update_charter(charter_id, {
        "quality_score": review["overall_score"],
        "quality_review": review,
    })

    return json.dumps(review, default=str)


async def update_charter_field(
    charter_id: str,
    field_name: str,
    value: str,
) -> str:
    """Update a specific field on a charter with AI-generated or user-provided content.

    Use this tool when the user asks you to generate, write, or fill in a charter field.

    Args:
        charter_id: The ID of the charter to update.
        field_name: The charter field to update (e.g. "problem_statement", "kpi_target").
        value: The content to write into the field.

    Returns:
        JSON confirmation with the updated field name and value.
    """
    allowed_fields = {
        "title", "problem_statement", "scope_description",
        "kpi_target", "kpi_actual", "kpi_gap", "kpi_trend",
        "process_name", "facilitator", "sponsor",
        "follow_up_plan", "notes",
    }

    if field_name not in allowed_fields:
        return json.dumps({
            "error": f"Cannot update field '{field_name}'. Allowed fields: {', '.join(sorted(allowed_fields))}",
        })

    charter = await get_charter(charter_id)
    if not charter:
        return json.dumps({"error": f"Charter {charter_id} not found"})

    await update_charter(charter_id, {field_name: value})

    return json.dumps({
        "success": True,
        "charter_id": charter_id,
        "field": field_name,
        "value": value,
    })


async def find_similar_charters(
    charter_id: str,
    limit: int = 3,
) -> str:
    """Find past charters similar to the current one based on its content.

    Args:
        charter_id: The ID of the current charter to find matches for.
        limit: Maximum number of similar charters to return.

    Returns:
        JSON with similar charters, similarity scores, and fillable fields.
    """
    charter = await get_charter(charter_id)
    if not charter:
        return json.dumps({"error": f"Charter {charter_id} not found"})

    # Build search terms from the current charter's content
    search_parts = []
    for field in ["problem_statement", "title", "scope_description", "process_name"]:
        val = charter.get(field)
        if val and isinstance(val, str) and len(val) > 3:
            search_parts.append(val)

    if not search_parts:
        return json.dumps({
            "charter_id": charter_id,
            "similar": [],
            "message": "Charter has too little content to find matches. Fill in the problem statement or title first.",
        })

    # Search using each content field
    all_matches: dict[str, dict] = {}
    for query in search_parts:
        results = await search_charters(query, event_type=charter.get("event_type"), limit=limit + 5)
        for r in results:
            if r["id"] != charter_id:
                all_matches[r["id"]] = r

    # Score matches
    similar = []
    for match in all_matches.values():
        total_score = 0

        if charter.get("event_type") and match.get("event_type") == charter.get("event_type"):
            total_score += 20
        if charter.get("business_unit") and match.get("business_unit"):
            if charter["business_unit"].lower() == match["business_unit"].lower():
                total_score += 15
        if charter.get("problem_statement") and match.get("problem_statement"):
            c_words = set(charter["problem_statement"].lower().split())
            m_words = set(match["problem_statement"].lower().split())
            overlap = len(c_words & m_words)
            if overlap > 5:
                total_score += 30
            elif overlap > 2:
                total_score += 15
        if match.get("quality_score") and match["quality_score"] >= 75:
            total_score += 15
        if match.get("status") == "completed":
            total_score += 10

        # Identify fillable fields
        fillable = []
        for f in ["problem_statement", "kpi_target", "scope_description", "process_name",
                  "facilitator", "sponsor", "follow_up_plan"]:
            if not charter.get(f) and match.get(f):
                fillable.append(f)

        similar.append({
            "id": match["id"],
            "title": match.get("title"),
            "event_type": match.get("event_type"),
            "similarity_score": total_score,
            "quality_score": match.get("quality_score"),
            "status": match.get("status"),
            "fillable_fields": fillable,
        })

    similar.sort(key=lambda x: x["similarity_score"], reverse=True)

    return json.dumps({
        "charter_id": charter_id,
        "similar": similar[:limit],
    }, default=str)


async def fill_from_similar(
    charter_id: str,
    source_charter_id: str,
    fields: list[str] | None = None,
) -> str:
    """Fill empty fields in the current charter from a similar past charter.

    Only fills fields that are currently empty in the target charter.

    Args:
        charter_id: The ID of the charter to fill.
        source_charter_id: The ID of the past charter to copy from.
        fields: Optional list of fields to fill. If not specified, fills all empty fields.

    Returns:
        JSON with the list of fields that were filled.
    """
    charter = await get_charter(charter_id)
    if not charter:
        return json.dumps({"error": f"Charter {charter_id} not found"})

    source = await get_charter(source_charter_id)
    if not source:
        return json.dumps({"error": f"Source charter {source_charter_id} not found"})

    fillable_fields = fields or [
        "problem_statement", "kpi_target", "scope_description", "process_name",
        "facilitator", "sponsor", "follow_up_plan",
    ]

    filled = []
    updates: dict[str, Any] = {}
    for f in fillable_fields:
        if not charter.get(f) and source.get(f):
            updates[f] = source[f]
            filled.append({"field": f, "value": source[f]})

    if updates:
        await update_charter(charter_id, updates)

    return json.dumps({
        "success": True,
        "charter_id": charter_id,
        "source_charter_id": source_charter_id,
        "fields_filled": filled,
        "message": f"Filled {len(filled)} empty fields from '{source.get('title', source_charter_id)}'",
    }, default=str)
