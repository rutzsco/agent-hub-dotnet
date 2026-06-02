# AG-UI Protocol — Server-Sent Events Streaming

## What is AG-UI?

AG-UI (Agent-User Interface) is a streaming protocol built on Server-Sent Events (SSE) that enables real-time communication between AI agents and frontend clients. It is part of the Microsoft Agent Framework (MAF) ecosystem.

Unlike simple chat completions that return a single response, AG-UI streams granular events that allow the frontend to render agent activity in real-time — including thinking, tool calls, partial text, and structured data.

## Why AG-UI?

| Feature | REST API | AG-UI SSE |
|---------|----------|-----------|
| Real-time streaming | ❌ | ✓ |
| Tool call visibility | ❌ | ✓ (start/args/end) |
| Incremental rendering | ❌ | ✓ (token-by-token) |
| Multi-turn tool loops | Manual polling | ✓ (automatic) |
| Connection simplicity | Multiple requests | Single EventSource |

## Event Types

### Lifecycle Events

| Event | Description |
|-------|-------------|
| `RUN_STARTED` | Agent run begins. Contains `run_id` and `thread_id`. |
| `RUN_FINISHED` | Agent run completes successfully. |
| `RUN_ERROR` | Agent run failed with an error. |

### Text Message Events

| Event | Description |
|-------|-------------|
| `TEXT_MESSAGE_START` | A new text message begins. Contains `message_id` and `role`. |
| `TEXT_MESSAGE_CONTENT` | Incremental text content (token-by-token). |
| `TEXT_MESSAGE_END` | Text message is complete. |

### Tool Call Events

| Event | Description |
|-------|-------------|
| `TOOL_CALL_START` | Agent begins a tool invocation. Contains `tool_call_id` and `tool_name`. |
| `TOOL_CALL_ARGS` | Streaming tool arguments (JSON). |
| `TOOL_CALL_END` | Tool call definition complete. |
| `TOOL_CALL_RESULT` | Tool execution result returned. |

### State Events

| Event | Description |
|-------|-------------|
| `MESSAGES_SNAPSHOT` | Full conversation state snapshot. |
| `STATE_SNAPSHOT` | Agent state snapshot (custom metadata). |
| `STATE_DELTA` | Incremental state change. |

## Request Format

```http
POST /api/kai/foundry-agent
Content-Type: application/json

{
  "thread_id": "optional-thread-id",
  "run_id": "optional-run-id",
  "messages": [
    {
      "role": "user",
      "content": "Help me write a problem statement for our shipping delays"
    }
  ],
  "state": {}
}
```

## Response Format (SSE Stream)

```
event: RUN_STARTED
data: {"run_id": "run_abc123", "thread_id": "thread_xyz"}

event: TEXT_MESSAGE_START
data: {"message_id": "msg_1", "role": "assistant"}

event: TEXT_MESSAGE_CONTENT
data: {"message_id": "msg_1", "content": "I'd be happy to help"}

event: TEXT_MESSAGE_CONTENT
data: {"message_id": "msg_1", "content": " you craft a problem statement"}

event: TOOL_CALL_START
data: {"tool_call_id": "tc_1", "tool_name": "suggest_for_field"}

event: TOOL_CALL_ARGS
data: {"tool_call_id": "tc_1", "args": "{\"field_name\": \"problem_statement\"}"}

event: TOOL_CALL_END
data: {"tool_call_id": "tc_1"}

event: TOOL_CALL_RESULT
data: {"tool_call_id": "tc_1", "result": "{\"tips\": [...], \"examples\": [...]}"}

event: TEXT_MESSAGE_CONTENT
data: {"message_id": "msg_1", "content": "\n\nBased on best practices..."}

event: TEXT_MESSAGE_END
data: {"message_id": "msg_1"}

event: RUN_FINISHED
data: {"run_id": "run_abc123"}
```

## Frontend Integration

### JavaScript (EventSource)

```javascript
const response = await fetch('/api/kai/foundry-agent', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({
    messages: [{ role: 'user', content: userMessage }]
  })
});

const reader = response.body.getReader();
const decoder = new TextDecoder();

while (true) {
  const { done, value } = await reader.read();
  if (done) break;

  const chunk = decoder.decode(value);
  const lines = chunk.split('\n');

  for (const line of lines) {
    if (line.startsWith('event: ')) {
      const eventType = line.slice(7);
      // Handle event type
    }
    if (line.startsWith('data: ')) {
      const data = JSON.parse(line.slice(6));
      switch (eventType) {
        case 'TEXT_MESSAGE_CONTENT':
          appendToChat(data.content);
          break;
        case 'TOOL_CALL_START':
          showToolIndicator(data.tool_name);
          break;
        // ... handle other events
      }
    }
  }
}
```

### React/TypeScript

The AG-UI protocol has official client libraries:

```typescript
import { AgentUIClient } from '@ag-ui/client';

const client = new AgentUIClient({
  endpoint: '/api/kai/foundry-agent'
});

client.run({
  messages: [{ role: 'user', content: 'Help me...' }],
  onTextContent: (content) => setResponse(prev => prev + content),
  onToolCallStart: (name) => setToolStatus(`Using ${name}...`),
  onRunFinished: () => setLoading(false),
});
```

## MAF SDK Integration

In the backend, AG-UI is added with a single line using the MAF SDK:

```python
from agent_framework.ag_ui import add_agent_framework_fastapi_endpoint

# Register any MAF agent as an AG-UI SSE endpoint
add_agent_framework_fastapi_endpoint(app, agent, "/api/kai/agent")
```

This automatically:
- Accepts AG-UI formatted POST requests
- Runs the agent with the provided messages
- Streams all events (text, tools, state) as SSE
- Handles errors gracefully with RUN_ERROR events

## Connection Management

- **Timeout:** AG-UI connections stay open for the duration of the agent run. Long tool calls may extend this.
- **Reconnection:** If the connection drops, the client should retry the full request (AG-UI is not resumable).
- **CORS:** The server must allow the frontend origin. Our setup uses `allow_origins=["*"]` for local development.
- **Buffering:** Disable any reverse proxy buffering (nginx: `proxy_buffering off`) to ensure real-time streaming.
