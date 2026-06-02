# KAI UI — AG-UI SSE Client

Minimal single-page AG-UI client for the KAI agent. No build step required.

## Usage

1. Start the KAI agent backend:
   ```bash
   cd ../kai-agent
   uvicorn main:app --port 8001 --reload
   ```

2. Serve the UI (any static file server):
   ```bash
   # Python
   python -m http.server 3002

   # Or npx
   npx serve -p 3002
   ```

3. Open http://localhost:3002

## How It Works

The UI connects to the KAI AG-UI SSE endpoint at `http://localhost:8001/api/kai/agent` and handles these event types:

- `RUN_STARTED` — Resets state for a new agent run
- `TEXT_MESSAGE_CONTENT` — Streams text deltas into the chat
- `TOOL_CALL_START/END` — Shows tool badges (active → done)
- `TOOL_CALL_RESULT` — Detects charter field updates
- `RUN_FINISHED` — Marks completion

## Features

- Dark theme chat UI
- Streaming text with basic markdown rendering
- Tool call visualization (badges)
- Quick action buttons for common tasks
- Auto-resize input
- Connection status indicator
