"""
KAI Foundry Prompt Agent — Create/Update Script

Creates a Foundry Prompt Agent (platform-managed) with:
- 11 function calling tools (charter CRUD + coaching)
- Code Interpreter (metrics analysis)
- Memory (persistent across sessions)

The function tools call back to the KAI FastAPI backend (localhost:8001)
for charter operations.

Usage:
    python create_agent.py                  # Create/update the agent
    python create_agent.py --setup-memory   # Also provision memory store
    python create_agent.py --all            # Full setup (agent + memory)
"""

import os
import sys
from pathlib import Path

from dotenv import load_dotenv

# Load .env from repo root
_repo_root = Path(__file__).resolve().parent.parent.parent
load_dotenv(_repo_root / ".env")

from azure.identity import DefaultAzureCredential
from azure.ai.projects import AIProjectClient
from azure.ai.projects.models import (
    PromptAgentDefinition,
    MemorySearchPreviewTool,
    CodeInterpreterTool,
    FunctionTool,
    MemoryStoreDefaultDefinition,
    MemoryStoreDefaultOptions,
)

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

AGENT_NAME = "kai-kaizen-coach"
AGENT_DESCRIPTION = (
    "KAI — Kaizen AI Coach. Coaches users through Lean kaizen charter creation "
    "with real-time guidance, validation, and quality review."
)
MODEL_DEPLOYMENT = os.getenv("AZURE_AI_MODEL_DEPLOYMENT_NAME", "gpt-4o-mini")
MEMORY_STORE_NAME = "kai-agent-memory"
MEMORY_CHAT_MODEL = MODEL_DEPLOYMENT
MEMORY_EMBEDDING_MODEL = "text-embedding-3-small"

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
- When the user starts a new charter or asks about past similar events → find_similar_charters
- When the user wants to copy fields from a past charter → fill_from_similar
- For data analysis on charter metrics, scoring trends → use Code Interpreter

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

LEAN METHODOLOGY KNOWLEDGE:
- You are fluent in A3 Thinking, PDCA, 5-Whys root cause analysis, 8 Wastes (DOWNTIME), \
  and Value Stream Mapping.
- Standard Kaizen events are typically 3-5 days with cross-functional teams.

MEMORY:
- You remember users across sessions — their role, past charters, preferences, and context.
- Use memory to personalize coaching and reference previous work.

RESPONSE FORMAT:
- Use markdown for structure (bold, lists, tables)
- Keep responses concise but actionable
"""

# ---------------------------------------------------------------------------
# Function Tool Definitions
# ---------------------------------------------------------------------------

KAI_FUNCTION_TOOLS = [
    {
        "name": "suggest_for_field",
        "description": "Get tips, examples, and past charter excerpts for a specific charter field.",
        "parameters": {
            "type": "object",
            "properties": {
                "field_name": {
                    "type": "string",
                    "description": "The charter field name (e.g. 'problem_statement', 'kpi_target', 'scope_description')",
                },
                "current_value": {
                    "type": "string",
                    "description": "The user's current input for this field, if any",
                },
                "event_type": {
                    "type": "string",
                    "enum": ["standard_kaizen", "problem_solving", "value_stream_mapping"],
                    "description": "Optional event type to filter relevant examples",
                },
            },
            "required": ["field_name"],
        },
    },
    {
        "name": "validate_input",
        "description": "Validate a charter field input and provide quality feedback with score, issues, and suggestions.",
        "parameters": {
            "type": "object",
            "properties": {
                "field_name": {
                    "type": "string",
                    "description": "The charter field being validated",
                },
                "value": {
                    "type": "string",
                    "description": "The user's input to validate",
                },
            },
            "required": ["field_name", "value"],
        },
    },
    {
        "name": "search_past_charters",
        "description": "Search historical charters for relevant examples and learnings.",
        "parameters": {
            "type": "object",
            "properties": {
                "query": {
                    "type": "string",
                    "description": "Search text to match against charter titles, problem statements, and scope",
                },
                "event_type": {
                    "type": "string",
                    "enum": ["standard_kaizen", "problem_solving", "value_stream_mapping"],
                },
                "limit": {"type": "integer", "default": 5},
            },
            "required": ["query"],
        },
    },
    {
        "name": "get_charter_progress",
        "description": "Get completion progress for a charter including per-section breakdown.",
        "parameters": {
            "type": "object",
            "properties": {
                "charter_id": {"type": "string", "description": "The charter ID"},
            },
            "required": ["charter_id"],
        },
    },
    {
        "name": "generate_content",
        "description": "Get field metadata and examples for content generation. The agent uses this context to draft content.",
        "parameters": {
            "type": "object",
            "properties": {
                "field_name": {"type": "string", "description": "The charter field to generate content for"},
                "context": {"type": "string", "description": "User-provided context or notes"},
                "event_type": {
                    "type": "string",
                    "enum": ["standard_kaizen", "problem_solving", "value_stream_mapping"],
                },
            },
            "required": ["field_name", "context"],
        },
    },
    {
        "name": "review_charter",
        "description": "Perform a full quality review scoring each section (0-100).",
        "parameters": {
            "type": "object",
            "properties": {
                "charter_id": {"type": "string", "description": "The charter ID to review"},
            },
            "required": ["charter_id"],
        },
    },
    {
        "name": "update_charter_field",
        "description": "Write content to a specific charter field. Use when the user asks you to write, generate, or fill in a field.",
        "parameters": {
            "type": "object",
            "properties": {
                "charter_id": {"type": "string"},
                "field_name": {
                    "type": "string",
                    "description": "Field to update (problem_statement, kpi_target, scope_description, title, process_name, facilitator, sponsor, follow_up_plan, notes)",
                },
                "value": {"type": "string", "description": "The content to write"},
            },
            "required": ["charter_id", "field_name", "value"],
        },
    },
    {
        "name": "find_similar_charters",
        "description": "Find past charters similar to the current one with similarity scores and fillable fields.",
        "parameters": {
            "type": "object",
            "properties": {
                "charter_id": {"type": "string"},
                "limit": {"type": "integer", "default": 3},
            },
            "required": ["charter_id"],
        },
    },
    {
        "name": "fill_from_similar",
        "description": "Copy fields from a past charter into the current one. Only fills empty fields.",
        "parameters": {
            "type": "object",
            "properties": {
                "charter_id": {"type": "string", "description": "Target charter to fill"},
                "source_charter_id": {"type": "string", "description": "Source charter to copy from"},
            },
            "required": ["charter_id", "source_charter_id"],
        },
    },
    {
        "name": "create_charter",
        "description": "Create a new Kaizen event charter.",
        "parameters": {
            "type": "object",
            "properties": {
                "title": {"type": "string"},
                "event_type": {
                    "type": "string",
                    "enum": ["standard_kaizen", "problem_solving", "value_stream_mapping"],
                },
                "problem_statement": {"type": "string", "description": "Optional initial problem statement"},
            },
            "required": ["title", "event_type"],
        },
    },
    {
        "name": "list_charters",
        "description": "List existing charters with optional filters.",
        "parameters": {
            "type": "object",
            "properties": {
                "status": {"type": "string", "enum": ["draft", "in_progress", "completed"]},
                "event_type": {"type": "string", "enum": ["standard_kaizen", "problem_solving", "value_stream_mapping"]},
                "limit": {"type": "integer", "default": 20},
            },
            "required": [],
        },
    },
]


# ---------------------------------------------------------------------------
# Agent Creation
# ---------------------------------------------------------------------------

def get_project_client() -> AIProjectClient:
    endpoint = os.environ.get("FOUNDRY_PROJECT_ENDPOINT")
    if not endpoint:
        print("ERROR: FOUNDRY_PROJECT_ENDPOINT not set in .env")
        sys.exit(1)
    return AIProjectClient(
        endpoint=endpoint,
        credential=DefaultAzureCredential(),
        allow_preview=True,
    )


def build_tools() -> list:
    """Build the tool list for the KAI prompt agent."""
    tools = []

    for fn_def in KAI_FUNCTION_TOOLS:
        tools.append(FunctionTool(
            name=fn_def["name"],
            description=fn_def["description"],
            parameters=fn_def["parameters"],
        ))

    tools.append(CodeInterpreterTool())

    tools.append(
        MemorySearchPreviewTool(
            memory_store_name=MEMORY_STORE_NAME,
            scope="{{$userId}}",
        )
    )

    return tools


def create_or_update_agent(client: AIProjectClient, tools: list):
    """Create or update the KAI prompt agent in Foundry."""
    definition = PromptAgentDefinition(
        model=MODEL_DEPLOYMENT,
        instructions=KAI_SYSTEM_PROMPT,
        tools=tools,
        temperature=0.7,
        top_p=0.9,
    )

    print(f"\nCreating/updating agent: {AGENT_NAME} (model={MODEL_DEPLOYMENT})")
    version = client.agents.create_version(
        agent_name=AGENT_NAME,
        definition=definition,
        description=AGENT_DESCRIPTION,
    )
    print(f"Agent version created: {version}")
    return version


def setup_memory_store(client: AIProjectClient):
    """Provision the memory store for persistent agent memory."""
    print(f"\nSetting up memory store: {MEMORY_STORE_NAME}")

    try:
        existing = client.beta.memory_stores.get(name=MEMORY_STORE_NAME)
        print(f"  Memory store '{MEMORY_STORE_NAME}' already exists")
        return existing
    except Exception:
        pass

    store = client.beta.memory_stores.create(
        name=MEMORY_STORE_NAME,
        definition=MemoryStoreDefaultDefinition(
            chat_model=MEMORY_CHAT_MODEL,
            embedding_model=MEMORY_EMBEDDING_MODEL,
            options=MemoryStoreDefaultOptions(
                user_profile_enabled=True,
                user_profile_details=(
                    "Build a profile of the user's kaizen experience level, role, "
                    "business unit, past event types, and coaching preferences."
                ),
                chat_summary_enabled=True,
            ),
        ),
    )
    print(f"  Created memory store: {MEMORY_STORE_NAME}")
    return store


def main():
    import argparse
    parser = argparse.ArgumentParser(description="Create KAI Foundry Prompt Agent")
    parser.add_argument("--setup-memory", action="store_true", help="Provision memory store")
    parser.add_argument("--all", action="store_true", help="Full setup (agent + memory)")
    args = parser.parse_args()

    if args.all:
        args.setup_memory = True

    client = get_project_client()

    # Memory store (optional)
    if args.setup_memory:
        setup_memory_store(client)

    # Build tools and create agent
    tools = build_tools()
    version = create_or_update_agent(client, tools)

    print(f"\n{'='*60}")
    print(f"KAI Foundry Prompt Agent — Setup Complete")
    print(f"{'='*60}")
    print(f"  Agent Name:    {AGENT_NAME}")
    print(f"  Model:         {MODEL_DEPLOYMENT}")
    print(f"  Function Tools: {len(KAI_FUNCTION_TOOLS)}")
    print(f"  Code Interpreter: Yes")
    print(f"  Memory Store:  {MEMORY_STORE_NAME}")
    print(f"\n  Test: python invoke_agent.py")
    print(f"  UI:   http://localhost:3002")


if __name__ == "__main__":
    main()
