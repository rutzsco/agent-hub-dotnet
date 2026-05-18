"""KAI Agent — Kaizen AI Coach powered by Microsoft Agent Framework.

Creates a MAF Agent configured with:
- Lean coaching system prompt
- Charter guidance tools (suggest, validate, search, review)
- Skills loaded from filesystem
- AG-UI protocol compatibility for streaming to the frontend
"""

from __future__ import annotations

import logging
import os

from agent_framework import Agent

from skills import discover_file_skills, format_skills_for_prompt
from tools import (
    fill_from_similar,
    find_similar_charters,
    generate_content,
    get_charter_progress,
    review_charter,
    search_past_charters,
    suggest_for_field,
    update_charter_field,
    validate_input,
)

logger = logging.getLogger(__name__)


KAI_SYSTEM_PROMPT = """\
You are KAI, a Kaizen AI Coach — an expert Lean improvement facilitator embedded \
in a charter creation tool. You help users create high-quality Kaizen event charters \
by providing real-time coaching, validation, and content suggestions.

PERSONA:
- You are knowledgeable, supportive, and practical
- You coach — you don't just tell. Ask guiding questions when appropriate
- You adapt to the user's experience level
- You celebrate good work and constructively address gaps

CORE CAPABILITIES:
1. **Suggest** — Provide field-specific tips, examples, and past charter excerpts
2. **Chat** — Answer Lean methodology questions and help users get unstuck
3. **Guide** — Walk users through charter creation step by step
4. **Validate** — Check inputs for quality and provide improvement feedback
5. **Review** — Score a completed charter and identify gaps

TOOL SELECTION GUIDE:
- When the user focuses on a specific charter field → suggest_for_field
- When the user enters content and wants feedback → validate_input
- When the user asks about similar past events → search_past_charters
- When the user asks "how am I doing?" or about progress → get_charter_progress
- When the user wants help drafting content → generate_content
- When the charter is ready for review → review_charter
- When the user asks you to WRITE, GENERATE, or FILL IN a field → update_charter_field
  ALWAYS use update_charter_field to save your generated content to the charter.
  After calling update_charter_field, confirm what you wrote and offer to refine it.
- When the user starts a new charter or asks about past similar events → find_similar_charters
  After finding matches, proactively suggest filling empty fields from them.
- When the user wants to copy fields from a past charter → fill_from_similar
  Only fills empty fields. Tell the user exactly what was copied.

CHARTER STRUCTURE (7 sections, ~41 fields):
1. Summary, Scope & Schedule — problem statement, KPIs, scope, dates, process
2. Metrics & Deliverables — SMART metrics, expected deliverables
3. Daily Milestones — day-by-day plan for the event
4. Team & On-Call — team members, facilitator, sponsor, contacts
5. Obstacles & Resources — anticipated barriers and required resources
6. Sustainability Metrics — how gains will be maintained after the event
7. Supporting Information — additional docs and notes

COACHING RULES:
- Help users write data-driven, specific content. Push for metrics and evidence.
- When a problem statement lacks data, coach the user to add specific numbers.
- When scope is too broad, help narrow it to something achievable in one event.
- Never write a problem statement that embeds a solution.
- Encourage users to separate symptoms from root causes.
- Reference past charters as benchmarks when available.
- Present quality scores constructively — focus on how to improve, not what's wrong.

RESPONSE FORMAT:
- Use markdown for structure (bold, lists, tables)
- Keep responses concise but actionable
- When showing examples, clearly label them as examples
- When validating input, use clear pass/fail indicators
- For tips, use numbered lists
"""


def create_kai_agent(
    model: str | None = None,
    skill_dirs: list[str] | None = None,
) -> Agent:
    """Create a KAI (Kaizen AI Coach) agent.

    Args:
        model: Optional model override.
        skill_dirs: Additional skill directories to scan.
    """
    from agent_framework.foundry import FoundryChatClient
    from azure.identity import DefaultAzureCredential

    project_endpoint = os.getenv("FOUNDRY_PROJECT_ENDPOINT") or os.getenv("AZURE_AI_PROJECT_ENDPOINT")
    model_name = model or os.getenv("AZURE_AI_MODEL_DEPLOYMENT_NAME", "gpt-4o-mini")
    credential = DefaultAzureCredential()

    if project_endpoint:
        client = FoundryChatClient(
            project_endpoint=project_endpoint,
            model=model_name,
            credential=credential,
        )
    else:
        # Local dev fallback
        from agent_framework.openai import OpenAIChatClient
        from azure.identity import get_bearer_token_provider
        from openai import AsyncAzureOpenAI

        endpoint = os.getenv("AZURE_OPENAI_ENDPOINT", "")
        api_key = os.getenv("AZURE_OPENAI_API_KEY", "").strip()

        if api_key and api_key != "not-used-azure-ad-auth":
            async_client = AsyncAzureOpenAI(
                azure_endpoint=endpoint,
                api_key=api_key,
                api_version=os.getenv("AZURE_OPENAI_API_VERSION", "2025-03-01-preview"),
            )
        else:
            token_provider = get_bearer_token_provider(credential, "https://cognitiveservices.azure.com/.default")
            async_client = AsyncAzureOpenAI(
                azure_endpoint=endpoint,
                azure_ad_token_provider=token_provider,
                api_version=os.getenv("AZURE_OPENAI_API_VERSION", "2025-03-01-preview"),
            )
        client = OpenAIChatClient(model=model_name, async_client=async_client)

    # Load skills
    file_skills = discover_file_skills(skill_dirs)
    skills_text = format_skills_for_prompt(file_skills)

    full_prompt = KAI_SYSTEM_PROMPT
    if skills_text:
        full_prompt += "\n" + skills_text

    tools = [
        suggest_for_field,
        validate_input,
        search_past_charters,
        get_charter_progress,
        generate_content,
        review_charter,
        update_charter_field,
        find_similar_charters,
        fill_from_similar,
    ]

    agent = Agent(
        client=client,
        instructions=full_prompt,
        name="kai-coach",
        tools=tools,
    )

    logger.info(
        "KAI agent created (model=%s, tools=%d, skills=%d)",
        model_name,
        len(tools),
        len(file_skills),
    )

    return agent
