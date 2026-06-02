# KAI Agent — Architecture

## Overview

KAI (Kaizen AI Coach) is a domain-specific AI agent that coaches users through Lean Kaizen event charter creation. It uses the Microsoft Agent Framework (MAF) Python SDK with AG-UI streaming for real-time frontend communication.

## System Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                          Browser (AG-UI Client)                      │
│                       src/kai-ui/index.html                          │
└───────────────┬───────────────────────────────────┬─────────────────┘
                │ SSE (EventSource)                  │ SSE (EventSource)
                │ /api/kai/agent                     │ /api/kai/foundry-agent
                ▼                                    ▼
┌───────────────────────────┐    ┌────────────────────────────────────┐
│  MAF Direct Agent         │    │  FoundryAgent (MAF)                │
│  (Local GPT-4o call)      │    │  → Foundry Prompt Agent            │
│  agent.py                 │    │  → Foundry IQ (Knowledge)          │
│                           │    │  → Memory (Persistent)             │
│  Tools executed locally   │    │  → Code Interpreter                │
└───────────────────────────┘    │  → Tools (client-side execution)   │
                                 └──────────────┬─────────────────────┘
                                                │ Responses API
                                                ▼
                                 ┌──────────────────────────────────┐
                                 │  Azure AI Foundry                │
                                 │  Agent: kai-kaizen-coach         │
                                 │  ┌────────────────────────┐      │
                                 │  │ Foundry IQ (MCP)       │      │
                                 │  │ → Azure AI Search      │      │
                                 │  │ → kai-knowledge index  │      │
                                 │  └────────────────────────┘      │
                                 └──────────────────────────────────┘
```

## Two Operating Modes

### 1. MAF Direct (`/api/kai/agent`)

The agent runs entirely locally. FastAPI receives the AG-UI request, creates a MAF Agent with system prompt and tools, calls Azure OpenAI for inference, and streams the response back as AG-UI SSE events.

**Pros:** Simple, fast, no Foundry agent required.  
**Cons:** No persistent memory, no Foundry IQ knowledge, no Code Interpreter.

### 2. Foundry Prompt Agent (`/api/kai/foundry-agent`)

The agent is a managed Foundry Prompt Agent with server-side capabilities. The local FastAPI server acts as a bridge: it receives AG-UI requests from the frontend, forwards them to the Foundry agent via the Responses API, and streams results back.

**Pros:** Persistent memory across sessions, Foundry IQ knowledge grounding, Code Interpreter, centrally managed agent definition.  
**Cons:** Requires Foundry project, slightly higher latency, more Azure permissions.

## Key Components

| Component | File | Purpose |
|-----------|------|---------|
| FastAPI Server | `main.py` | HTTP server, AG-UI endpoints, REST routes |
| Agent Factory | `agent.py` | Creates MAF Agent with system prompt and tools |
| Tools | `tools.py` | 9 charter coaching tool functions |
| Storage | `storage.py` | In-memory charter/template store (seed data) |
| Models | `models.py` | Pydantic models for charter domain |
| Skills | `skills/` | File-based domain expertise (SKILL.md files) |
| Agent Setup | `create_agent.py` | Creates Foundry Prompt Agent with all tools |
| Agent Client | `invoke_agent.py` | CLI client for testing the Foundry agent |

## Tool Functions

Tools are defined as Python functions decorated with type hints. They are registered both:
- **Server-side** (on the Foundry Prompt Agent) — as function tool definitions
- **Client-side** (on the FastAPI server) — for actual execution when the agent calls them

When the Foundry agent decides to call a tool, it returns a `function_call` event. The local FoundryAgent SDK intercepts this, executes the corresponding Python function, and sends the result back.

| Tool | Description |
|------|-------------|
| `suggest_for_field` | Tips and examples for a specific charter field |
| `validate_input` | Quality scoring and feedback on user input |
| `search_past_charters` | Search historical charters by keyword |
| `get_charter_progress` | Completion percentage by section |
| `generate_content` | Metadata/context for content drafting |
| `review_charter` | Full quality review with per-section scores |
| `update_charter_field` | Write content to a charter field |
| `find_similar_charters` | Find matching past charters by similarity |
| `fill_from_similar` | Auto-fill empty fields from a similar charter |

## Foundry IQ Knowledge Grounding

Foundry IQ provides retrieval-augmented generation (RAG) via Azure AI Search. The agent's knowledge base contains Lean methodology documents:

- Kaizen event planning guides
- Problem statement writing standards
- KPI and SMART metrics guidance
- A3 Thinking templates
- 5-Whys root cause analysis
- Value Stream Mapping principles
- Charter quality rubrics

The knowledge is accessed via MCP (Model Context Protocol) tool on the Foundry agent. When a user asks a methodology question, the agent retrieves relevant documents from the index before generating its response.

## Data Flow (Foundry Mode)

```
1. User types message in UI
2. UI sends POST to /api/kai/foundry-agent (AG-UI format)
3. FastAPI receives request, forwards to FoundryAgent
4. FoundryAgent sends to Foundry Responses API
5. Foundry agent processes:
   a. Retrieves from Foundry IQ knowledge base (if relevant)
   b. Checks memory for user context
   c. Decides on tool calls or direct response
6. If tool call: SDK executes local Python function, sends result back
7. Response streams as SSE events back through FastAPI to UI
8. UI renders events incrementally (text, tool calls, etc.)
```

## Skills System

Skills are markdown files in `skills/` that provide domain expertise. The agent reads these at startup and incorporates them into its reasoning:

- `skills/charter-quality/SKILL.md` — Scoring rubrics
- `skills/problem-statement/SKILL.md` — Problem statement guide
- `skills/kpi-guidance/SKILL.md` — Metrics and targets
- `skills/lean-methodology/SKILL.md` — Lean tools and methods
