# Agent Hub — Sequence Diagrams

## 1. `/agents/demo` — Direct Model Inference with Cosmos DB Memory

```mermaid
sequenceDiagram
    participant Client
    participant Route as POST /agents/demo
    participant SessionMgr as IConversationSessionManager<br/>(in-memory + Cosmos DB)
    participant Agent as AIAgent<br/>(direct model inference)
    participant Cosmos as Azure Cosmos DB
    participant Foundry as Azure AI Foundry (model endpoint)

    Note over SessionMgr,Cosmos: Startup (once): Agent created inline via AIProjectClient.AsAIAgent(model,instructions)

    Client->>Route: POST {message, conversationId?}

    alt existing conversationId with cached session
        Route->>SessionMgr: GetOrCreateSessionAsync(conversationId)
        SessionMgr-->>Route: ConversationSessionContext (session reused)
        Note over SessionMgr: Cache HIT — no history replay needed
    else new conversationId or session evicted
        Route->>SessionMgr: GetOrCreateSessionAsync(conversationId)
        SessionMgr->>Cosmos: Query conversation messages container
        Cosmos-->>SessionMgr: prior turns (user + assistant messages)
        SessionMgr->>Agent: CreateSessionAsync()
        Agent->>Foundry: Create new session
        Foundry-->>Agent: session
        SessionMgr-->>Route: ConversationSessionContext (requiresHistoryReplay=true)
        Note over SessionMgr: Cache MISS — history replayed into session
    end

    alt requiresHistoryReplay
        Route->>Agent: RunAsync(historyMessages + message, session)
    else
        Route->>Agent: RunAsync(message, session)
    end

    Agent->>Foundry: Inference request
    Foundry-->>Agent: Response
    Agent-->>Route: AgentResponse

    Route->>SessionMgr: AppendTurnAsync(conversationId, message, response)
    SessionMgr->>Cosmos: Upsert user + assistant turn documents

    Route-->>Client: 200 OK {conversationId, response}
```

---

## 2. `/agents/foundry-demo` — Foundry-Managed Agent with Cosmos DB Memory

```mermaid
sequenceDiagram
    participant Client
    participant Route as POST /agents/foundry-demo
    participant SessionMgr as IConversationSessionManager<br/>(in-memory + Cosmos DB)
    participant Agent as FoundryAgent<br/>(declarative agent on Foundry)
    participant AgentAdmin as AgentAdministrationClient
    participant Cosmos as Azure Cosmos DB
    participant Foundry as Azure AI Foundry

    Note over AgentAdmin,Foundry: Startup (once): resolve or create Foundry agent by name via AgentAdministrationClient

    alt agent exists in Foundry
        AgentAdmin->>Foundry: GetAgentAsync(agentName)
        Foundry-->>AgentAdmin: ProjectsAgentRecord
    else agent does not exist
        AgentAdmin->>Foundry: CreateAgentVersionAsync(agentName, definition)
        Foundry-->>AgentAdmin: created
        AgentAdmin->>Foundry: GetAgentAsync(agentName)
        Foundry-->>AgentAdmin: ProjectsAgentRecord
    end

    Client->>Route: POST {message, conversationId?}

    alt existing conversationId with cached session
        Route->>SessionMgr: GetOrCreateSessionAsync(conversationId)
        SessionMgr-->>Route: ConversationSessionContext (session reused)
        Note over SessionMgr: Cache HIT — no history replay needed
    else new conversationId or session evicted
        Route->>SessionMgr: GetOrCreateSessionAsync(conversationId)
        SessionMgr->>Cosmos: Query conversation messages container
        Cosmos-->>SessionMgr: prior turns
        SessionMgr->>Agent: CreateSessionAsync()
        Agent->>Foundry: Create new session/thread
        Foundry-->>Agent: session
        SessionMgr-->>Route: ConversationSessionContext (requiresHistoryReplay=true)
        Note over SessionMgr: Cache MISS — history replayed into session
    end

    alt requiresHistoryReplay
        Route->>Agent: RunAsync(historyMessages + message, session)
    else
        Route->>Agent: RunAsync(message, session)
    end

    Agent->>Foundry: Execute via declarative agent
    Foundry-->>Agent: Response
    Agent-->>Route: AgentResponse

    Route->>SessionMgr: AppendTurnAsync(conversationId, message, response)
    SessionMgr->>Cosmos: Upsert user + assistant turn documents

    Route-->>Client: 200 OK {conversationId, response}
```

---

## 3. `/agents/foundryMemoryAgent` — Foundry Memory Provider with Conversation Resume

```mermaid
sequenceDiagram
    participant Client
    participant Route as POST /agents/foundryMemoryAgent
    participant Agent as FoundryAgent
    participant ChatAgent as ChatClientAgent
    participant Provider as FoundryMemoryProvider
    participant Foundry as Azure AI Foundry
    participant MemoryAPI as Foundry Memory Store

    Note over Agent,MemoryAPI: Startup (once): create/resolve memory store and memory-enabled Foundry agent

    Client->>Route: POST {message, userId, conversationId?}

    alt conversationId is null
        Route->>Agent: CreateConversationSessionAsync()
        Agent->>Foundry: Create new server-side conversation
        Foundry-->>Agent: ChatClientAgentSession(conversationId)
    else conversationId provided
        Route->>ChatAgent: CreateSessionAsync(conversationId)
        ChatAgent-->>Route: ChatClientAgentSession(conversationId)
    end

    Note over Route,Provider: Set async-local userId for memory scope
    Route->>Agent: RunAsync(message, session)
    Provider->>MemoryAPI: Retrieve scoped memory before run
    Agent->>Foundry: Execute agent turn in conversation

    alt Foundry run succeeds
        Foundry-->>Agent: Response
        Provider->>MemoryAPI: Persist new turn after run
        Route-->>Client: 200 OK {userId, response, conversationId}
    else Foundry blocks request (content_filter)
        Foundry-->>Agent: HTTP 400 invalid_request_error: content_filter
        Route-->>Client: 400 Bad Request {error, code=content_filter}
    end
```

---

## 4. Event Charter UI — KAI Intents over `/agents/foundryMemoryAgent`

The `event-charter.html` SPA (served as the home page) drives the same memory agent endpoint
with a structured JSON envelope. The `intent` field tells the system prompt how to format
its reply. The UI keeps two independent threads: one for per-field interactions
(`field_help` / `review` / `section_review`) and one for the Chat tab.

```mermaid
sequenceDiagram
    participant User
    participant UI as event-charter.html<br/>(React SPA)
    participant Route as POST /agents/foundryMemoryAgent
    participant Memory as FoundryMemoryAgent<br/>(KAI system prompt)
    participant Foundry as Azure AI Foundry

    Note over UI: Two conversation threads kept locally:<br/>• fieldConvId (Ask AI, Review, Section Review)<br/>• chatConvId (Chat tab)

    alt User clicks ✨ Ask AI on a field
        User->>UI: Click "✨ Ask AI" on <field>
        UI->>UI: buildFieldPrompt(section, field, values)<br/>→ {intent:"field_help", section, field, currentValue, sectionValues}
        UI->>Route: POST {userId, message:<json>, conversationId:fieldConvId}
        Route->>Memory: ProcessMessage(...)
        Memory->>Foundry: RunAsync(envelope) with KAI instructions
        Foundry-->>Memory: Markdown: tips bullets + "> Suggested wording"
        Memory-->>Route: {response, conversationId}
        Route-->>UI: 200 OK
        UI->>UI: extractSuggestions(markdown)<br/>render Suggestions panel
        User->>UI: Click "Use this" on Suggested wording
        UI->>UI: handleFieldChange(fieldId, primary text)
        Note over UI: Field value updated → progress %<br/>and chips recomputed live
    end

    alt User clicks 💡 Review (e.g. Problem Statement)
        User->>UI: Click "💡 Review" on <field>
        UI->>UI: buildReviewPrompt(...)<br/>→ {intent:"review", ...}
        UI->>Route: POST {..., conversationId:fieldConvId}
        Memory->>Foundry: RunAsync(envelope)
        Foundry-->>Memory: Rubric (✅/❌/⚠️) + revised "> Suggested wording"
        Memory-->>UI: 200 OK
        UI->>User: Show rubric + "Use this" to apply revised wording
    end

    alt User clicks ✨ Suggest tips for <section>
        User->>UI: Click section-level Suggest button
        UI->>UI: buildSectionPrompt(...)<br/>→ {intent:"section_review", field:null, ...}
        UI->>Route: POST {..., conversationId:fieldConvId}
        Foundry-->>Memory: Section critique markdown
        Memory-->>UI: 200 OK
        UI->>User: Render critique (no per-field "Use this")
    end

    alt User uses 💬 Chat tab
        User->>UI: Type message, press Enter
        UI->>UI: buildChatPrompt(section, values, userText)<br/>→ {intent:"chat", userMessage:userText, ...}
        UI->>Route: POST {..., conversationId:chatConvId}
        Memory->>Foundry: RunAsync(envelope)
        Foundry-->>Memory: 1–3 paragraphs of plain prose<br/>(no rubric, no Suggested wording)
        Memory-->>UI: 200 OK
        UI->>User: Append assistant bubble to chat history
    end

    Note over UI,Memory: First response on each thread sets that thread's<br/>conversationId; subsequent calls reuse it so Foundry<br/>memory + thread context are preserved per intent group.
```
