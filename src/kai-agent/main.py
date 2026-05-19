"""KAI — Kaizen AI Coach: FastAPI + AG-UI SSE + Foundry Hosted Agent.

Entry point supporting two modes:
1. Local dev: FastAPI with AG-UI streaming endpoint
2. Foundry: ResponsesHostServer for Foundry Agent Service deployment

Usage:
  Local:   uvicorn main:app --port 8001
  Foundry: python main.py --foundry
"""

import logging
import os
import sys

from dotenv import load_dotenv

# Load .env from repo root (two levels up from src/kai-agent/)
_repo_root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
load_dotenv(os.path.join(_repo_root, ".env"))
load_dotenv()  # Also load local .env if present

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s %(levelname)s %(name)s: %(message)s",
)
logger = logging.getLogger(__name__)


def _create_fastapi_app():
    """Create the FastAPI application with AG-UI endpoint and REST routes."""
    from fastapi import FastAPI
    from fastapi.middleware.cors import CORSMiddleware

    from agent import create_kai_agent

    app = FastAPI(
        title="KAI — Kaizen AI Coach",
        description="AG-UI streaming agent for Kaizen event charter coaching",
        version="1.0.0",
    )

    # CORS for local dev
    app.add_middleware(
        CORSMiddleware,
        allow_origins=["*"],
        allow_credentials=True,
        allow_methods=["*"],
        allow_headers=["*"],
    )

    # Health check
    @app.get("/health")
    async def health():
        return {"status": "healthy", "agent": "kai-coach"}

    # Create the KAI agent
    kai_agent = create_kai_agent()

    # Register AG-UI SSE endpoint
    try:
        from agent_framework.ag_ui import add_agent_framework_fastapi_endpoint
        add_agent_framework_fastapi_endpoint(app, kai_agent, "/api/kai/agent")
        logger.info("KAI AG-UI endpoint registered at /api/kai/agent")
    except ImportError as e:
        logger.warning("AG-UI endpoint not available (missing agent_framework.ag_ui): %s", e)

    # Foundry Prompt Agent via FoundryAgent (proper MAF pattern)
    _register_foundry_agent(app)

    # Charter REST routes
    _register_charter_routes(app)

    return app


def _register_foundry_agent(app):
    """Register a FoundryAgent endpoint that connects to the Foundry Prompt Agent via AG-UI SSE."""
    try:
        from azure.identity import DefaultAzureCredential
        from agent_framework.foundry import FoundryAgent
        from agent_framework.ag_ui import add_agent_framework_fastapi_endpoint
        import tools as kai_tools

        endpoint = os.environ.get("FOUNDRY_PROJECT_ENDPOINT")
        if not endpoint:
            logger.warning("FOUNDRY_PROJECT_ENDPOINT not set — Foundry agent endpoint disabled")
            return

        # Tool functions for client-side execution (definitions live server-side on the agent)
        tool_functions = [
            kai_tools.suggest_for_field,
            kai_tools.validate_input,
            kai_tools.search_past_charters,
            kai_tools.get_charter_progress,
            kai_tools.generate_content,
            kai_tools.review_charter,
            kai_tools.update_charter_field,
            kai_tools.find_similar_charters,
            kai_tools.fill_from_similar,
        ]

        foundry_agent = FoundryAgent(
            project_endpoint=endpoint,
            agent_name="kai-kaizen-coach",
            agent_version="4",
            credential=DefaultAzureCredential(),
            tools=tool_functions,
            allow_preview=True,
        )

        # Strip tools/tool_choice from the API request body. Tool definitions already
        # exist on the Foundry agent; sending them again triggers a 400 ("model required").
        # The tools remain registered locally for client-side function invocation.
        _original_prepare = foundry_agent.client._prepare_options

        async def _prepare_options_no_tools(*args, **kwargs):
            opts = await _original_prepare(*args, **kwargs)
            opts.pop("tools", None)
            opts.pop("tool_choice", None)
            return opts

        foundry_agent.client._prepare_options = _prepare_options_no_tools

        add_agent_framework_fastapi_endpoint(app, foundry_agent, "/api/kai/foundry-agent")
        logger.info("Foundry Prompt Agent endpoint registered at /api/kai/foundry-agent")

    except ImportError as e:
        logger.warning("Foundry agent endpoint not available: %s", e)
    except Exception as e:
        logger.error("Failed to register Foundry agent endpoint: %s", e)


def _register_charter_routes(app):
    """Register charter CRUD REST endpoints."""
    from fastapi import HTTPException

    from models import CharterCreate, CharterUpdate
    import storage

    @app.get("/api/kai/templates")
    async def list_templates(event_type: str | None = None):
        templates = await storage.list_templates(event_type=event_type)
        return {"templates": templates}

    @app.post("/api/kai/charters", status_code=201)
    async def create_charter(body: CharterCreate):
        data = body.model_dump(exclude_none=True)
        charter = await storage.create_charter(data)
        return charter

    @app.get("/api/kai/charters")
    async def list_charters(status: str | None = None, event_type: str | None = None):
        charters = await storage.list_charters(status=status, event_type=event_type)
        return {"charters": charters, "count": len(charters)}

    @app.get("/api/kai/charters/{charter_id}")
    async def get_charter(charter_id: str):
        charter = await storage.get_charter(charter_id)
        if not charter:
            raise HTTPException(status_code=404, detail="Charter not found")
        progress = storage.compute_progress(charter)
        return {**charter, "progress": progress}

    @app.put("/api/kai/charters/{charter_id}")
    async def update_charter(charter_id: str, body: CharterUpdate):
        data = body.model_dump(exclude_none=True)
        if not data:
            raise HTTPException(status_code=400, detail="No fields to update")
        result = await storage.update_charter(charter_id, data)
        if not result:
            raise HTTPException(status_code=404, detail="Charter not found")
        return result


def _run_foundry():
    """Run as a Foundry Hosted Agent using ResponsesHostServer."""
    from agent_framework_foundry_hosting import ResponsesHostServer
    from agent import create_kai_agent

    agent = create_kai_agent()
    logger.info("Starting KAI as Foundry Hosted Agent (ResponsesHostServer)")

    server = ResponsesHostServer(agent)
    server.run()


# FastAPI app instance (used by uvicorn)
app = _create_fastapi_app()


if __name__ == "__main__":
    if "--foundry" in sys.argv:
        _run_foundry()
    else:
        import uvicorn
        port = int(os.getenv("KAI_PORT", "8001"))
        logger.info("Starting KAI FastAPI server on port %d", port)
        uvicorn.run(app, host="0.0.0.0", port=port)
