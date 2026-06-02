# KAI — Kaizen AI Coach

AI-powered Kaizen event charter coach built with Microsoft Agent Framework (MAF) and AG-UI streaming protocol.

## Architecture

```
Browser (AG-UI client) ←→ FastAPI + AG-UI SSE ←→ MAF Agent (GPT-4o) ←→ 9 Tools
```

**Two deployment modes:**
- **Local dev** — FastAPI with AG-UI SSE endpoint on port 8001
- **Foundry Hosted Agent** — Deployed to Azure Foundry Agent Service via ResponsesHostServer

## Quick Start

### Prerequisites
- Python 3.11+
- Azure AI Foundry project with a deployed model (gpt-4o-mini or gpt-4o)

### Setup

```bash
cd src/kai-agent
python -m venv .venv
source .venv/bin/activate  # or .venv\Scripts\activate on Windows
pip install -r requirements.txt
```

### Environment Variables

Create a `.env` file:

```env
# Option 1: Foundry project (recommended)
AZURE_AI_PROJECT_ENDPOINT=https://<resource>.services.ai.azure.com/api/projects/<project>
AZURE_AI_MODEL_DEPLOYMENT_NAME=gpt-4o-mini

# Option 2: Direct Azure OpenAI
AZURE_OPENAI_ENDPOINT=https://<resource>.openai.azure.com/
AZURE_OPENAI_API_KEY=<key>  # or use DefaultAzureCredential
AZURE_AI_MODEL_DEPLOYMENT_NAME=gpt-4o-mini
```

### Run Locally

```bash
# FastAPI with AG-UI endpoint
uvicorn main:app --port 8001 --reload

# Or directly
python main.py
```

### Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/health` | GET | Health check |
| `/api/kai/agent` | POST | AG-UI SSE streaming endpoint |
| `/api/kai/templates` | GET | List charter templates |
| `/api/kai/charters` | GET/POST | List/create charters |
| `/api/kai/charters/{id}` | GET/PUT | Get/update a charter |

### AG-UI Client Usage

Send messages to `/api/kai/agent` using the AG-UI protocol:

```json
POST /api/kai/agent
Content-Type: application/json

{
  "messages": [
    {"role": "user", "content": "Help me write a problem statement about shipping delays"}
  ]
}
```

Response is an SSE stream with AG-UI events:
- `RUN_STARTED` — Agent run begins
- `TEXT_MESSAGE_CONTENT` — Streaming text response
- `TOOL_CALL_START/END` — Tool invocations
- `RUN_FINISHED` — Agent run complete

## Tools

| Tool | Purpose |
|------|---------|
| `suggest_for_field` | Field-specific tips and examples |
| `validate_input` | Quality checks on user input |
| `search_past_charters` | Find relevant historical charters |
| `get_charter_progress` | Completion status breakdown |
| `generate_content` | Context for content generation |
| `review_charter` | Full quality review with scoring |
| `update_charter_field` | Write content to a charter field |
| `find_similar_charters` | Find matching past charters |
| `fill_from_similar` | Auto-fill empty fields from a match |

## Skills

File-based skills in `skills/` directory provide domain expertise:
- **charter-quality** — Scoring rubrics and quality standards
- **problem-statement** — Writing effective problem statements
- **kpi-guidance** — SMART metrics and target setting
- **lean-methodology** — A3, 5-Whys, PDCA, Value Stream Mapping

## Foundry Deployment

```bash
# Build and push to ACR
docker build -t kai-agent .
docker tag kai-agent <acr>.azurecr.io/kai-agent:latest
docker push <acr>.azurecr.io/kai-agent:latest

# Deploy via Foundry Agent Service (uses agent.yaml)
```
