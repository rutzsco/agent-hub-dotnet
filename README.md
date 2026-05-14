# Agent Hub

Agent Hub is a .NET 10 minimal API for hosting AI agents using Microsoft Agent Framework and Azure AI Foundry. It includes:

- A code-first agent built directly from an Azure AI Foundry model deployment
- A Foundry-managed agent created and resolved through the Foundry project
- A Foundry memory-backed agent that provisions a Foundry memory store
- Conversation memory using `ConversationId` for demo and foundry-demo endpoints
- PostgreSQL-backed conversation history persistence
- Restart-safe conversation rehydration from stored history

## What This Project Does

The API exposes three agent routes with two memory models.

- `POST /agents/demo` and `POST /agents/foundry-demo` accept `message` plus optional `conversationId`
- These two routes persist turns in PostgreSQL and can replay history after restart
- `POST /agents/foundryMemoryAgent` accepts `message`, `userId`, and optional `conversationId`
- This route uses a Foundry memory store and relies on Foundry-managed memory behaviors

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

## Architecture

The solution is a single ASP.NET Core project with organized subfolders.

| Folder | Purpose |
|--------|----------|
| `src/AgentHub.API` | ASP.NET Core minimal API, route handlers, agent registration, configuration |
| `src/AgentHub.API/agents/` | Agent implementations (DemoAgent, FoundryDemoAgent, FoundryMemoryAgent) |
| `src/AgentHub.API/persistence/` | PostgreSQL conversation history storage and memory audit trail |
| `src/AgentHub.API/session/` | In-memory session tracking and history-based session rehydration |

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
- Azure sign-in available to `DefaultAzureCredential` such as `az login`
- A PostgreSQL server reachable from the API
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
| `AgentHub:AzureAIModelDeploymentName` | Yes | Model deployment name in the Foundry project |
| `AgentHub:ApimSubscriptionKey` | No | APIM subscription key for Azure OpenAI calls through APIM; also supported by `APIM_SUBSCRIPTION_KEY` |
| `AgentHub:FoundryAgentName` | No | Name of the Foundry-managed agent; defaults to `DemoAgent` when omitted |
| `AgentHub:MemoryStoreName` | No | Foundry memory store name for `foundryMemoryAgent`; defaults to `agent-hub-memory` |
| `AgentHub:MemoryEmbeddingModel` | No | Embedding deployment/model for Foundry memory store; defaults to `text-embedding-3-small` |

### PostgreSQL Settings

Use either a full connection string or individual connection properties.

Option 1: full connection string

| Setting | Required | Description |
|--------|----------|-------------|
| `AgentHub:Postgres:ConnectionString` | Yes | Full PostgreSQL connection string |

Option 2: individual properties

| Setting | Required | Description |
|--------|----------|-------------|
| `AgentHub:Postgres:Host` | Yes | PostgreSQL server host |
| `AgentHub:Postgres:Port` | No | PostgreSQL server port, default `5432` |
| `AgentHub:Postgres:Database` | Yes | Database name |
| `AgentHub:Postgres:Username` | Yes | Database username |
| `AgentHub:Postgres:Password` | Yes | Database password |
| `AgentHub:Postgres:SslMode` | No | PostgreSQL SSL mode, default `Prefer` |

Environment variable fallbacks are also supported:

- `AZURE_AI_PROJECT_ENDPOINT`
- `AZURE_AI_MODEL_DEPLOYMENT_NAME`
- `APIM_SUBSCRIPTION_KEY`
- `AZURE_AI_FOUNDRY_AGENT_NAME`
- `AZURE_AI_MEMORY_STORE_NAME`
- `AZURE_AI_MEMORY_EMBEDDING_MODEL`
- `POSTGRES_CONNECTION_STRING`
- `POSTGRES_URL`
- `POSTGRES_HOST`
- `POSTGRES_PORT`
- `POSTGRES_DATABASE`
- `POSTGRES_USERNAME`
- `POSTGRES_PASSWORD`
- `POSTGRES_SSL_MODE`

## Example Configuration

Use placeholder values similar to the following in `src/AgentHub.API/appsettings.Development.json`.

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "AgentHub": "Debug"
    }
  },
  "AgentHub": {
    "AzureAIProjectEndpoint": "https://<resource>.services.ai.azure.com/api/projects/<project>",
    "AzureAIModelDeploymentName": "gpt-4o-mini",
    "ApimSubscriptionKey": "<apim-subscription-key>",
    "FoundryAgentName": "foundry-demo-agent",
    "MemoryStoreName": "agent-hub-memory",
    "MemoryEmbeddingModel": "text-embedding-3-small",
    "Postgres": {
      "Host": "<server>.postgres.database.azure.com",
      "Port": "5432",
      "Database": "<database>",
      "Username": "<username>",
      "Password": "<password>",
      "SslMode": "Prefer"
    }
  }
}
```

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
- PostgreSQL initialization and persistence errors

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
    (all test files)
```

## Notes

- The Foundry agent name and model deployment name must match valid resources in your Foundry project
- PostgreSQL connection values containing special characters are handled through `NpgsqlConnectionStringBuilder`
- A colleague in another location cannot use `localhost`; expose the app through a tunnel or deploy it to a reachable host
