# Agent Hub

Agent Hub is a .NET 10 minimal API for hosting AI agents using Microsoft Agent Framework and Azure AI Foundry. It includes:

- A code-first agent built directly from an Azure AI Foundry model deployment
- A Foundry-managed agent created and resolved through the Foundry project
- A Foundry memory-backed agent ("KAI") that provisions a Foundry memory store and is driven by a configurable system prompt
- An interactive Event Charter web UI (React via CDN, no build pipeline) that talks to the memory agent
- Conversation memory using `ConversationId` for demo and foundry-demo endpoints
- Cosmos DB-backed conversation history persistence and memory deletion audit log
- Restart-safe conversation rehydration from stored history
- Tenant-aware authentication via `DefaultAzureCredential` with explicit tenant pinning

## What This Project Does

The API exposes three agent routes with two memory models.

- `POST /agents/demo` and `POST /agents/foundry-demo` accept `message` plus optional `conversationId`
- These two routes persist turns in PostgreSQL and can replay history after restart
- `POST /agents/foundryMemoryAgent` accepts `message`, `userId`, and optional `conversationId`
- This route uses a Foundry memory store and relies on Foundry-managed memory behaviors
- All prompts sent to `foundryMemoryAgent` are validated for security before processing

## Security Features

### Prompt Validation Skill

The Foundry Memory Agent includes a **Prompt Validation Skill** that provides comprehensive safety validation before prompts are processed. This skill detects and blocks:

- **Prompt Injection** - Attempts to override system instructions (e.g., "ignore previous instructions", "system: ignore")
- **Jailbreak Attempts** - Patterns that try to bypass safety constraints (e.g., "DAN mode", "developer mode")
- **Role Manipulation** - Attempts to alter agent behavior (e.g., "pretend to be", "forget you are")
- **Input Quality Issues** - Invalid characters, excessive repetition, length violations

When validation fails, the API returns a `400 Bad Request` with a descriptive error message explaining why the prompt was rejected.

For detailed documentation on the validation skill, see [`src/AgentHub.API/services/skills/validation/README.md`](src/AgentHub.API/services/skills/validation/README.md).

## Basic Getting Started

Use these steps to run the API locally with the same style of settings used in this workspace.

### 1. Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Access to the Azure OpenAI or Azure AI Foundry deployment you want the agents to use
- For Azure identity-based Foundry calls, sign in locally with `az login`
- Optional: a PostgreSQL database if you want persisted conversation history for `demo` and `foundry-demo`

### 2. Configure Local Settings

Local development settings live in `src/AgentHub.API/appsettings.Development.json` under the `AgentHub` section.

This example mirrors the current workspace settings, with the subscription key replaced by a placeholder:

```json
{
  "AgentHub": {
    "ApimSubscriptionKey": "<your-apim-subscription-key>",
    "AzureOpenAIEndpoint": "https://dev.aiapim.jci.com",
    "AzureAIModelDeploymentName": "gpt-5.1-2025-11-13"
  }
}
```

For Foundry-managed routes such as `/agents/foundry-demo` and `/agents/foundryMemoryAgent`, also set `AzureAIProjectEndpoint`:

```json
{
  "AgentHub": {
    "AzureAIProjectEndpoint": "https://<resource>.services.ai.azure.com/api/projects/<project>",
    "AzureOpenAIEndpoint": "https://dev.aiapim.jci.com",
    "AzureAIModelDeploymentName": "gpt-5.1-2025-11-13",
    "ApimSubscriptionKey": "<your-apim-subscription-key>",
    "FoundryAgentName": "DemoAgent"
  }
}
```

To enable PostgreSQL-backed conversation history, add either a full connection string or the individual PostgreSQL values shown later in this README.

### 3. Restore and Build

From the repository root:

```powershell
dotnet restore AgentHub.slnx
dotnet build AgentHub.slnx
```

### 4. Run the API

```powershell
dotnet run --project src/AgentHub.API/AgentHub.API.csproj --launch-profile http
```

Open Swagger at:

- `http://localhost:5023/swagger`

### 5. Try a Request

```powershell
curl -X POST http://localhost:5023/agents/demo `
  -H "Content-Type: application/json" `
  -d '{"message":"Hello, introduce yourself."}'
```

## Agents

| Agent | Endpoint | Type | Description |
|-------|----------|------|-------------|
| DemoAgent | `POST /agents/demo` | Code-first agent | Uses `AIProjectClient.AsAIAgent()` with runtime-defined model and instructions |
| FoundryDemoAgent | `POST /agents/foundry-demo` | Foundry-managed agent | Uses `AgentAdministrationClient` and creates the configured Foundry agent if it does not already exist |
| FoundryMemoryAgent | `POST /agents/foundryMemoryAgent` | Foundry-managed memory agent | Uses a Foundry memory store and a dedicated Foundry agent (`<FoundryAgentName>-memory` by default) |

## Endpoints

| Method | Route | Description |
|--------|-------|-------------|
| `POST` | `/agents/demo` | Sends a message to the code-first agent |
| `POST` | `/agents/foundry-demo` | Sends a message to the Foundry-managed agent |
| `POST` | `/agents/foundryMemoryAgent` | Sends a message to the Foundry memory-backed agent (`message`, `userId`, `conversationId?`) |
| `GET` | `/conversations/{conversationId}/history` | Returns persisted message history for a conversation |
| `GET` | `/health` | Health check |
| `GET` | `/swagger` | Swagger UI |

## Common API UI

The project ships with two browser UIs out of `src/AgentHub.API/wwwroot`:

- **Event Charter (default home page)** — `/` (served from `event-charter.html`)
  - Interactive 6-section Kaizen Event Charter form: Summary & Schedule, Metrics & Deliverables, Daily Milestones, Team & On-Call, Obstacles & Resources, Sustainability Metrics.
  - Each field has an **✨ Ask AI** button (and **💡 Review** for fields tagged for framework review) that calls `POST /agents/foundryMemoryAgent` with a structured JSON envelope (see [KAI System Prompt](#kai-system-prompt)).
  - Right panel shows live progress (6 sections weighted equally), a clickable AI-suggestion list with **Use this** to populate the field, and a **💬 Chat** tab for free-form conversation with the agent (separate `conversationId` from per-field threads).
  - Single-file React + Babel-standalone via CDN — no npm/build step.
- **Legacy API console** — `/index.html`
  - Forms for all agent, memory inspect/delete, conversation history, and health endpoints.
  - Displays HTTP status and JSON responses inline.

Swagger remains available at `/swagger`. `Program.cs` configures `UseDefaultFiles` to prefer `event-charter.html`, falling back to `index.html`.

## KAI System Prompt

The Foundry Memory Agent is configured with a structured system prompt ("KAI — the Kaizen Charter Guide") sourced from configuration so it can be edited without recompiling.

- **Source of truth**: `AgentHub:MemoryAgentInstructions` in `appsettings.json` / `appsettings.Local.json`.
  - Accepts either a single string or a JSON array of strings (joined with `\n` for readability).
  - Falls back to the env var `AZURE_AI_MEMORY_AGENT_INSTRUCTIONS`.
  - If config is empty, falls back to the `KaiCharterSystemPrompt` constant in `FoundryMemoryAgent.cs`.
- **Request envelope**: the UI sends a JSON object as the agent message:

  ```json
  {
    "intent": "field_help" | "review" | "section_review" | "chat" | "freeform",
    "section": { "id": "...", "title": "..." },
    "field":   { "id": "...", "label": "..." } | null,
    "currentValue": "<what the user has typed>" | "",
    "sectionValues": { "<fieldLabel>": "<value>", ... },
    "userMessage": "<optional free text>" | ""
  }
  ```

- **Response style by intent**:
  - `field_help` — Markdown bullet tips + a `> Suggested wording` blockquote (paste-ready).
  - `review` — Strict rubric (✅ / ❌ / ⚠️) per framework rule, plus a revised `> Suggested wording`.
  - `section_review` — Critique of the whole section.
  - `chat` — Plain conversational prose; no rubric, no paste-ready suggestions.
  - `freeform` — Direct answer to `userMessage`, on-topic.

### Important: instructions are baked at agent creation

Foundry agent instructions are persisted on the server when the agent is first created. `GetOrCreateAgentAsync` only creates on a 404, so an existing agent keeps its original prompt. To roll out a new prompt:

- Bump `AgentHub:FoundryAgentName` (e.g. `kai-charter-v1` → `kai-charter-v2`) so a fresh agent is created on next startup, **or**
- Delete the existing agent in the Azure AI Foundry portal.

## Tenant Configuration

`DefaultAzureCredential` may default to your home tenant (e.g. Microsoft corp) even when the target Azure resource lives in a customer tenant, producing errors like `Tenant provided in token does not match resource token`. To avoid this, the project supports an explicit tenant override:

- **Setting**: `AgentHub:AzureTenantId` (or env var `AZURE_TENANT_ID`).
- When set, `Settings.CreateAzureCredential()` pins `TenantId`, `VisualStudioTenantId`, `SharedTokenCacheTenantId`, and `InteractiveBrowserTenantId` on `DefaultAzureCredentialOptions`.
- Used by all three agents (`DemoAzureOpenAIAgent`, `FoundryDemoAgent`, `FoundryMemoryAgent`).

## Architecture

The solution is a single ASP.NET Core project with organized subfolders.

| Folder | Purpose |
|--------|----------|
| `src/AgentHub.API` | ASP.NET Core minimal API, route handlers, agent registration, configuration |
| `src/AgentHub.API/agents/` | Agent implementations (DemoAgent, FoundryDemoAgent, FoundryMemoryAgent) |
| `src/AgentHub.API/persistence/` | PostgreSQL conversation history storage and memory audit trail |
| `src/AgentHub.API/session/` | In-memory session tracking and history-based session rehydration |
| `src/AgentHub.API/services/skills/` | Reusable skills for agent flows (validation, etc.) |
| `tests/AgentHub.Tests/` | Unit and integration tests |

## Memory Model

The project uses two different memory paths.

### Path A: `demo` and `foundry-demo`

Conversation memory is keyed by `ConversationId`.

1. Client sends a message without a `ConversationId`
2. API creates a new conversation and returns the generated `ConversationId`
3. Client sends later messages with the same `ConversationId`
4. The session manager reuses the existing in-memory session when possible
5. Every user and assistant turn is also written to PostgreSQL
6. If the app restarts, the next request with the same `ConversationId` reloads the stored history and replays it before generating the next response

This means the conversation can survive process restarts as long as PostgreSQL history is available.

### Path B: `foundryMemoryAgent`

1. On startup, the API resolves or creates a Foundry memory store (persists in Azure)
2. On startup, the API resolves or creates a dedicated Foundry memory agent
3. Requests include `message`, `userId`, and optional `conversationId`
4. The route creates or resumes a Foundry conversation session:
   - **No `conversationId` supplied** — creates a new Foundry server-side conversation session
   - **`conversationId` supplied** — resumes that existing Foundry conversation via `CreateSessionAsync(conversationId)`
5. The route sets `userId` into async-local state so `FoundryMemoryProvider` scopes memory retrieval and persistence to the correct user
6. **Run** — `RunAsync(message, session)` executes against the Foundry agent
7. `FoundryMemoryProvider` automatically handles memory lifecycle for each run:
   - Retrieves relevant memory before the run
   - Persists new conversation turns after the run
8. The API returns the resolved `conversationId` from the session so clients can continue the same thread

This path does not use the PostgreSQL conversation pipeline.

### Content Filter Handling (`foundryMemoryAgent`)

If Azure OpenAI content management blocks a run (for example, `invalid_request_error: content_filter`), the API now returns a handled `400 Bad Request` instead of an unhandled server error.

Response shape:

```json
{
  "error": "The request was blocked by content filtering. Please rephrase your message and retry.",
  "code": "content_filter"
}
```

Notes:

- The content filter decision is based on the effective prompt, which can include current message text, resumed conversation thread context, and memory retrieved by `FoundryMemoryProvider`.
- A request can fail on a resumed conversation even if the latest user message appears safe in isolation.
- To isolate context-related blocks, test with a new `conversationId` and, if needed, a different `userId`.

### Prompt Validation Errors

Before reaching Azure OpenAI, prompts are validated by the Prompt Validation Skill. If validation fails, the API returns `400 Bad Request` with an error message:

```json
"Potential prompt injection detected. The message contains patterns that attempt to override system instructions."
```

Common validation error examples:

- `"Message cannot be empty."`
- `"Message exceeds maximum length of 4000 characters (current: 4523)."`
- `"Invalid control characters detected in the message."`
- `"Potential jailbreak attempt detected. The message contains patterns that attempt to bypass safety constraints."`

To debug validation failures, check the server logs for the specific validation rule that failed.

## Foundry Memory Conversation Flow

For `POST /agents/foundryMemoryAgent`, conversation continuity is session-based:

1. Client sends `{ message, userId }` for a new thread and receives `conversationId`
2. Client sends later turns as `{ message, userId, conversationId }`
3. The API recreates a session bound to that `conversationId` and runs the same Foundry thread

The `conversationId` is not passed as text in the prompt. It is carried by `AgentSession`, which is passed to `RunAsync`.

### First turn — start a new conversation

Request:

```http
POST /agents/foundryMemoryAgent
Content-Type: application/json

{
  "message": "My name is Alex and I prefer dark mode in all apps.",
  "userId": "alex@example.com"
}
```

Response:

```json
{
  "userId": "alex@example.com",
  "response": "Got it, Alex! I'll remember that you prefer dark mode.",
  "conversationId": "thread_abc123"
}
```

### Follow-up turn — continue the same conversation

Request:

```http
POST /agents/foundryMemoryAgent
Content-Type: application/json

{
  "message": "What UI preference did I mention?",
  "userId": "alex@example.com",
  "conversationId": "thread_abc123"
}
```

Response:

```json
{
  "userId": "alex@example.com",
  "response": "You mentioned that you prefer dark mode in all apps.",
  "conversationId": "thread_abc123"
}
```

> `conversationId` is the Foundry server-side thread ID returned by the first call. Pass it back on every subsequent turn to continue the same conversation. Omit it (or send `null`) to start a fresh thread.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- An Azure AI Foundry project with a deployed model
- An Azure OpenAI resource endpoint for `demo-aoai-agent` (for example `https://<resource>.openai.azure.com/`)
- Azure sign-in available to `DefaultAzureCredential` such as `az login`
- A PostgreSQL server reachable from the API
- For `demo-aoai-agent`: your signed-in identity must have **Cognitive Services OpenAI User** on the Azure OpenAI resource configured in `AzureOpenAIEndpoint`
- For `foundryMemoryAgent`: the Foundry project's managed identity must have the **Cognitive Services OpenAI User** role on the Azure OpenAI resource hosting the `text-embedding-3-small` deployment

## Configuration

Configuration is loaded in this order:

1. `appsettings.json`
2. `appsettings.Development.json`
3. Environment variables

The application uses the `AgentHub` configuration section.

### Required Settings

| Setting | Required | Description |
|--------|----------|-------------|
| `AgentHub:AzureAIProjectEndpoint` | For Foundry routes | Azure AI Foundry project endpoint. If omitted, Foundry-backed endpoints return a configuration error when invoked. |
| `AgentHub:AzureOpenAIEndpoint` | For APIM/OpenAI routes | Azure OpenAI or APIM endpoint used for Azure OpenAI calls. If omitted, the app falls back to `AzureAIProjectEndpoint` when available. |
| `AgentHub:AzureAIModelDeploymentName` | Yes | Model deployment name in the Foundry project |
| `AgentHub:AzureTenantId` | No | Pins `DefaultAzureCredential` to a specific Entra tenant. Required when the target resource is in a different tenant than your default sign-in. |
| `AgentHub:FoundryAgentName` | No | Name of the Foundry-managed agent; the memory agent uses `<FoundryAgentName>-memory`. **Bump this name to roll out a new system prompt.** |
| `AgentHub:AzureAIApiKey` | No | Azure OpenAI API key when using key-based authentication; also supported by `AZURE_AI_API_KEY` and `AZURE_OPENAI_API_KEY` |
| `AgentHub:ApimSubscriptionKey` | No | APIM subscription key for Azure OpenAI calls through APIM; also supported by `APIM_SUBSCRIPTION_KEY` |
| `AgentHub:FoundryAgentName` | No | Name of the Foundry-managed agent; defaults to `DemoAgent` when omitted |
| `AgentHub:MemoryStoreName` | No | Foundry memory store name for `foundryMemoryAgent`; defaults to `agent-hub-memory` |
| `AgentHub:MemoryEmbeddingModel` | No | Embedding deployment/model for Foundry memory store; defaults to `text-embedding-3-small` |
| `AgentHub:MemoryAgentInstructions` | No | System prompt for the memory agent. Single string or array of strings (joined with `\n`). Falls back to the in-code `KaiCharterSystemPrompt` constant. |

### Cosmos DB Settings

Conversation history and the memory deletion audit log are persisted to Azure Cosmos DB.

| Setting | Required | Description |
|--------|----------|-------------|
| `AgentHub:Cosmos:AccountEndpoint` | Yes | Cosmos DB account endpoint, e.g. `https://<account>.documents.azure.com:443/` |
| `AgentHub:Cosmos:DatabaseName` | Yes | Cosmos database name |
| `AgentHub:Cosmos:ConversationContainerName` | No | Container for conversation messages, default `conversation-messages` |
| `AgentHub:Cosmos:MemoryAuditContainerName` | No | Container for memory deletion audit entries, default `memory-audit` |

Authentication uses `DefaultAzureCredential` (RBAC), so assign **Cosmos DB Built-in Data Contributor** to the running identity — no connection strings or keys are needed.

Environment variable fallbacks are also supported:

- `AZURE_AI_PROJECT_ENDPOINT`
- `AZURE_OPENAI_ENDPOINT`
- `AZURE_AI_MODEL_DEPLOYMENT_NAME`
- `AZURE_AI_API_KEY`
- `AZURE_OPENAI_API_KEY`
- `APIM_SUBSCRIPTION_KEY`
- `AZURE_AI_FOUNDRY_AGENT_NAME`
- `AZURE_AI_MEMORY_STORE_NAME`
- `AZURE_AI_MEMORY_EMBEDDING_MODEL`
- `AZURE_AI_MEMORY_AGENT_INSTRUCTIONS`
- `AZURE_TENANT_ID`
- `COSMOS_ACCOUNT_ENDPOINT`
- `COSMOS_DATABASE_NAME`
- `COSMOS_CONVERSATION_CONTAINER_NAME`
- `COSMOS_MEMORY_AUDIT_CONTAINER_NAME`

## Example Configuration

Use placeholder values similar to the following in `src/AgentHub.API/appsettings.Development.json`. This mirrors the current local APIM-backed settings, with secrets replaced by placeholders.

```json
{
  "AgentHub": {
    "ApimSubscriptionKey": "<apim-subscription-key>",
    "AzureOpenAIEndpoint": "https://dev.aiapim.jci.com",
    "AzureAIModelDeploymentName": "gpt-5.1-2025-11-13"
  }
}
```

For Foundry-managed agents and PostgreSQL persistence, expand the same section as needed:

```json
{
  "AgentHub": {
    "AzureAIProjectEndpoint": "https://<resource>.services.ai.azure.com/api/projects/<project>",
    "AzureTenantId": "<entra-tenant-guid>",
    "FoundryAgentName": "kai-charter-v1",
    "AzureOpenAIEndpoint": "https://dev.aiapim.jci.com",
    "AzureAIModelDeploymentName": "gpt-5.1-2025-11-13",
    "ApimSubscriptionKey": "<apim-subscription-key>",
    "FoundryAgentName": "DemoAgent",
    "MemoryStoreName": "agent-hub-memory",
    "MemoryEmbeddingModel": "text-embedding-3-small",
    "Cosmos": {
      "AccountEndpoint": "https://<account>.documents.azure.com:443/",
      "DatabaseName": "<database>",
      "ConversationContainerName": "conversation-messages",
      "MemoryAuditContainerName": "memory-audit"
    }
  }
}
```

> The full KAI system prompt lives under `AgentHub:MemoryAgentInstructions` as a JSON array of strings. See `appsettings.json` in the repo for the canonical version.

## Restore and Build

From the repository root:

```powershell
dotnet restore AgentHub.slnx
dotnet build AgentHub.slnx
```

## Run the API

From the repository root:

```powershell
dotnet run --project src/AgentHub.API/AgentHub.API.csproj --launch-profile http
```

The default local URLs are defined in `src/AgentHub.API/Properties/launchSettings.json`.

- `http://localhost:5023`
- `https://localhost:7132`

Swagger UI is available at:

- `http://localhost:5023/swagger`

The API Console UI is available at:

- `http://localhost:5023/`

## Hot Reload

For local development, use:

```powershell
dotnet watch --project src/AgentHub.API/AgentHub.API.csproj
```

Hot reload can apply many code changes automatically, but changes to startup wiring, DI registrations, route shape, or some constructor signatures may still require a restart.

## Example Requests

### Start a New Conversation

```powershell
curl -X POST http://localhost:5023/agents/foundry-demo `
  -H "Content-Type: application/json" `
  -d '{"message":"Hello, introduce yourself."}'
```

Example response:

```json
{
  "conversationId": "7f4c0cf7-f6ab-4c32-9d82-7c61d9f25a8a",
  "response": "Hello, I am your assistant..."
}
```

### Continue an Existing Conversation

```powershell
curl -X POST http://localhost:5023/agents/foundry-demo `
  -H "Content-Type: application/json" `
  -d '{"message":"What did I just ask you?","conversationId":"7f4c0cf7-f6ab-4c32-9d82-7c61d9f25a8a"}'
```

### Call the Foundry Memory Agent

```powershell
curl -X POST http://localhost:5023/agents/foundryMemoryAgent `
  -H "Content-Type: application/json" `
  -d '{"message":"My favorite color is teal.","userId":"user-123"}'
```

### Fetch Conversation History

```powershell
curl http://localhost:5023/conversations/7f4c0cf7-f6ab-4c32-9d82-7c61d9f25a8a/history
```

### Health Check

```powershell
curl http://localhost:5023/health
```

## PostgreSQL Behavior

> **Note:** Earlier versions of this project used PostgreSQL for persistence. The current implementation uses Azure Cosmos DB — see [Cosmos DB Settings](#cosmos-db-settings). The section below is retained for historical reference and may not match the running code.

The persistence project automatically creates the required tables on first use.

Tables created:

- `conversations`
- `conversation_messages`
- `memory_deletion_audit`

Each message is stored with:

- conversation id
- role
- content
- timestamp

### Memory Audit Schema Update

For existing databases that already have `memory_deletion_audit` but do not yet include the `audit_message` column, run:

`src/AgentHub.Persistence/sql/20260505_add_audit_message_to_memory_deletion_audit.sql`

This migration is idempotent and safe to run multiple times.

## Logging

The API logs:

- startup configuration details
- request start and completion
- memory store creation and resolution flow
- agent creation flow
- session reuse and session rehydration
- Cosmos DB initialization and persistence errors

Logs are written to the **console only** (no file sink). Under Visual Studio they appear in the Debug Output window; under `dotnet run` they go to the terminal; in App Service / containers they are picked up by the standard log stream.

For development, set `AgentHub` logging to `Debug` in `appsettings.Development.json`.

## Project Structure

```text
AgentHub.slnx
src/
  AgentHub.API/
    Program.cs
    Settings.cs
    appsettings.json
    appsettings.Development.json
    agents/
      DemoAgent.cs
      FoundryDemoAgent.cs
      FoundryMemoryAgent.cs
      MemoryAuditService.cs
    routes/
      AgentRoutes.cs
    services/
      skills/
        ISkill.cs
        validation/
          PromptValidationSkill.cs
          PromptValidationResult.cs
          ValidationRule.cs
          README.md
    persistence/
      ConversationMessage.cs
      IConversationHistoryRepository.cs
      PostgresConversationHistoryRepository.cs
      PostgresConversationOptions.cs
      PostgresMemoryAuditRepository.cs
      sql/
        20260505_add_audit_message_to_memory_deletion_audit.sql
    session/
      ConversationSessionContext.cs
      ConversationSessionManager.cs
      IConversationSessionManager.cs
tests/
  AgentHub.Tests/
    PromptValidationSkillTests.cs
    (other test files)
```

## Notes

- The Foundry agent name and model deployment name must match valid resources in your Foundry project
- PostgreSQL connection values containing special characters are handled through `NpgsqlConnectionStringBuilder`
- A colleague in another location cannot use `localhost`; expose the app through a tunnel or deploy it to a reachable host
