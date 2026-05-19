"""
KAI Foundry Prompt Agent — Invoke & Test

Sends messages to the Foundry Prompt Agent, handles function tool calls
by routing them to the KAI FastAPI backend, and streams responses.

Usage:
    python invoke_agent.py                        # Interactive chat
    python invoke_agent.py -q "Help me write a problem statement"
    python invoke_agent.py --tool-test            # Run tool integration tests
"""

import os
import sys
import json
import argparse
import urllib.request
import urllib.error
from pathlib import Path

from dotenv import load_dotenv

# Load .env from repo root
_repo_root = Path(__file__).resolve().parent.parent.parent
load_dotenv(_repo_root / ".env")

from azure.identity import DefaultAzureCredential
from azure.ai.projects import AIProjectClient

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

AGENT_NAME = "kai-kaizen-coach"
KAI_API_URL = os.getenv("KAI_API_URL", "http://localhost:8001")


def get_openai_client():
    """Create authenticated OpenAI client via Foundry project."""
    endpoint = os.environ.get("FOUNDRY_PROJECT_ENDPOINT")
    if not endpoint:
        print("ERROR: FOUNDRY_PROJECT_ENDPOINT not set in .env")
        sys.exit(1)

    project = AIProjectClient(
        endpoint=endpoint,
        credential=DefaultAzureCredential(),
        allow_preview=True,
    )
    return project.get_openai_client()


def call_kai_tool(function_name: str, arguments: dict) -> str:
    """Execute a KAI function tool call against the local FastAPI backend.

    Maps Foundry agent function calls to KAI REST API endpoints.
    """
    # Map function names to API calls
    if function_name == "suggest_for_field":
        params = "&".join(f"{k}={v}" for k, v in arguments.items() if v)
        return _http_get(f"/api/kai/templates?{params}")

    elif function_name == "validate_input":
        # Validation runs locally in tools.py — call the agent endpoint
        # For the prompt agent, we simulate via direct import
        return _run_tool_locally(function_name, arguments)

    elif function_name == "search_past_charters":
        query = arguments.get("query", "")
        event_type = arguments.get("event_type", "")
        params = f"status=&event_type={event_type}" if event_type else ""
        return _http_get(f"/api/kai/charters?{params}")

    elif function_name == "get_charter_progress":
        charter_id = arguments["charter_id"]
        return _http_get(f"/api/kai/charters/{charter_id}")

    elif function_name == "generate_content":
        return _run_tool_locally(function_name, arguments)

    elif function_name == "review_charter":
        return _run_tool_locally(function_name, arguments)

    elif function_name == "update_charter_field":
        charter_id = arguments["charter_id"]
        body = {"field_name": arguments["field_name"], arguments["field_name"]: arguments["value"]}
        return _http_put(f"/api/kai/charters/{charter_id}", body)

    elif function_name == "find_similar_charters":
        return _run_tool_locally(function_name, arguments)

    elif function_name == "fill_from_similar":
        return _run_tool_locally(function_name, arguments)

    elif function_name == "create_charter":
        return _http_post("/api/kai/charters", arguments)

    elif function_name == "list_charters":
        params = "&".join(f"{k}={v}" for k, v in arguments.items() if v)
        return _http_get(f"/api/kai/charters?{params}")

    elif function_name == "list_templates":
        params = "&".join(f"{k}={v}" for k, v in arguments.items() if v)
        return _http_get(f"/api/kai/templates?{params}")

    else:
        return json.dumps({"error": f"Unknown function: {function_name}"})


def _run_tool_locally(function_name: str, arguments: dict) -> str:
    """Run a tool function directly (for tools that need local logic)."""
    import asyncio
    sys.path.insert(0, str(Path(__file__).parent))
    import tools as kai_tools

    tool_fn = getattr(kai_tools, function_name, None)
    if not tool_fn:
        return json.dumps({"error": f"Tool {function_name} not found"})

    return asyncio.run(tool_fn(**arguments))


def _http_get(path: str) -> str:
    try:
        url = f"{KAI_API_URL}{path}"
        req = urllib.request.Request(url)
        with urllib.request.urlopen(req, timeout=15) as resp:
            return resp.read().decode()
    except Exception as e:
        return json.dumps({"error": str(e)})


def _http_post(path: str, body: dict) -> str:
    try:
        url = f"{KAI_API_URL}{path}"
        data = json.dumps(body).encode()
        req = urllib.request.Request(url, data=data, method="POST", headers={"Content-Type": "application/json"})
        with urllib.request.urlopen(req, timeout=15) as resp:
            return resp.read().decode()
    except Exception as e:
        return json.dumps({"error": str(e)})


def _http_put(path: str, body: dict) -> str:
    try:
        url = f"{KAI_API_URL}{path}"
        data = json.dumps(body).encode()
        req = urllib.request.Request(url, data=data, method="PUT", headers={"Content-Type": "application/json"})
        with urllib.request.urlopen(req, timeout=15) as resp:
            return resp.read().decode()
    except Exception as e:
        return json.dumps({"error": str(e)})


# ---------------------------------------------------------------------------
# Agent Interaction
# ---------------------------------------------------------------------------

MAX_TOOL_ROUNDS = 5


def send_message(openai_client, message: str, conversation_id: str | None = None) -> tuple[str, str]:
    """Send a message to the KAI prompt agent, handling tool calls."""
    extra_body = {
        "agent_reference": {
            "name": AGENT_NAME,
            "type": "agent_reference",
        },
    }
    if conversation_id:
        extra_body["conversation_id"] = conversation_id

    response = openai_client.responses.create(
        input=message,
        extra_body=extra_body,
    )

    # Handle function tool calls (multi-round)
    for _ in range(MAX_TOOL_ROUNDS):
        if not hasattr(response, "output") or not isinstance(response.output, list):
            break

        tool_calls = [o for o in response.output if getattr(o, "type", None) == "function_call"]
        if not tool_calls:
            break

        tool_outputs = []
        for tc in tool_calls:
            fn_name = tc.name if hasattr(tc, "name") else "unknown"
            fn_args = json.loads(tc.arguments if hasattr(tc, "arguments") else "{}")
            print(f"  🔧 {fn_name}({json.dumps(fn_args)[:80]})")

            result = call_kai_tool(fn_name, fn_args)
            tool_outputs.append({
                "type": "function_call_output",
                "call_id": tc.call_id if hasattr(tc, "call_id") else tc.id,
                "output": result,
            })

        # Submit tool results
        response = openai_client.responses.create(
            input=tool_outputs,
            extra_body=extra_body,
            previous_response_id=response.id,
        )

    response_text = response.output_text if hasattr(response, "output_text") else str(response.output)
    conv_id = getattr(response, "conversation_id", None) or conversation_id
    return response_text, conv_id


def interactive_chat(openai_client):
    """Interactive KAI coaching session."""
    print("=" * 60)
    print("KAI — Kaizen AI Coach (Foundry Prompt Agent)")
    print("Type 'quit' to exit, 'new' for new conversation.")
    print("=" * 60)

    conversation_id = None

    while True:
        try:
            user_input = input("\nYou: ").strip()
        except (EOFError, KeyboardInterrupt):
            break

        if not user_input:
            continue
        if user_input.lower() in ("quit", "exit", "q"):
            break
        if user_input.lower() == "new":
            conversation_id = None
            print("  [Starting new conversation]")
            continue

        try:
            response_text, conversation_id = send_message(openai_client, user_input, conversation_id)
            print(f"\nKAI: {response_text}")
        except Exception as e:
            print(f"\n  ERROR: {e}")

    print("\nGoodbye!")


def single_query(openai_client, query: str):
    """Send a single query."""
    print(f"You: {query}\n")
    response_text, _ = send_message(openai_client, query)
    print(f"KAI: {response_text}")


def tool_test(openai_client):
    """Run targeted tool integration tests."""
    print("=" * 60)
    print("KAI Tool Integration Tests")
    print("=" * 60)

    tests = [
        ("Problem Statement Help", "Help me write a problem statement about high defect rates in our PCB assembly"),
        ("Validate Input", "Validate this problem statement: 'We need to buy new machines because the old ones are broken'"),
        ("KPI Guidance", "What makes a good KPI target for a kaizen event?"),
        ("List Charters", "Show me all existing charters"),
    ]

    for name, query in tests:
        print(f"\n--- Test: {name} ---")
        print(f"  Query: {query}")
        try:
            response_text, _ = send_message(openai_client, query)
            print(f"  Result: {response_text[:200]}...")
            print(f"  Status: ✓ PASS")
        except Exception as e:
            print(f"  Status: ✗ FAIL ({e})")


def main():
    parser = argparse.ArgumentParser(description="Invoke KAI Foundry Prompt Agent")
    parser.add_argument("-q", "--query", help="Single query mode")
    parser.add_argument("--tool-test", action="store_true", help="Run tool integration tests")
    args = parser.parse_args()

    openai_client = get_openai_client()

    if args.query:
        single_query(openai_client, args.query)
    elif args.tool_test:
        tool_test(openai_client)
    else:
        interactive_chat(openai_client)


if __name__ == "__main__":
    main()
