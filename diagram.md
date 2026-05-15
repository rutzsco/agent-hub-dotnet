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

    Note over UI: Two conversation threads kept locally. fieldConvId for Ask AI / Review / Section Review. chatConvId for the Chat tab.

    alt User clicks Ask AI on a field
        User->>UI: Click Ask AI on a field
        Note over UI: buildFieldPrompt(section, field, values) returns intent=field_help with section, field, currentValue, sectionValues
        UI->>Route: POST userId, message, conversationId=fieldConvId
        Route->>Memory: ProcessMessage
        Memory->>Foundry: RunAsync envelope with KAI instructions
        Foundry-->>Memory: Markdown tips bullets and Suggested wording blockquote
        Memory-->>Route: response and conversationId
        Route-->>UI: 200 OK
        Note over UI: extractSuggestions parses markdown and renders the Suggestions panel
        User->>UI: Click Use this on Suggested wording
        Note over UI: handleFieldChange writes the primary text into fieldId
        Note over UI: Field value updated, progress percent and chips recomputed live
    end

    alt User clicks Review on a field
        User->>UI: Click Review on a field
        Note over UI: buildReviewPrompt returns intent=review
        UI->>Route: POST with conversationId=fieldConvId
        Memory->>Foundry: RunAsync envelope
        Foundry-->>Memory: Rubric pass fail warn plus revised Suggested wording
        Memory-->>UI: 200 OK
        UI->>User: Show rubric and Use this to apply revised wording
    end

    alt User clicks Suggest tips for a section
        User->>UI: Click section level Suggest button
        Note over UI: buildSectionPrompt returns intent=section_review with field=null
        UI->>Route: POST with conversationId=fieldConvId
        Foundry-->>Memory: Section critique markdown
        Memory-->>UI: 200 OK
        UI->>User: Render critique without per field Use this
    end

    alt User uses the Chat tab
        User->>UI: Type message and press Enter
        Note over UI: buildChatPrompt returns intent=chat with userMessage set to userText
        UI->>Route: POST with conversationId=chatConvId
        Memory->>Foundry: RunAsync envelope
        Foundry-->>Memory: One to three paragraphs of plain prose, no rubric and no Suggested wording
        Memory-->>UI: 200 OK
        UI->>User: Append assistant bubble to chat history
    end

    Note over UI,Memory: First response on each thread sets that conversationId. Subsequent calls reuse it so Foundry memory and thread context are preserved per intent group.
```

---

## 5. KAI Agent — Decision Flow by Intent

KAI is the Foundry memory agent driven by the system prompt in
`AgentHub:MemoryAgentInstructions`. The UI sends a JSON envelope on every call;
KAI branches on the `intent` field to choose its response format and which hard
rules apply. This flowchart is the same logic whether the request comes from the
Event Charter UI or any other client.

```mermaid
flowchart TD
    Start([Incoming user message JSON envelope]) --> Validate[PromptValidationSkill checks for injection, jailbreak, length]
    Validate -->|invalid| Reject[Return 400 Bad Request with reason]
    Validate -->|valid| ScopeMem[Set asyncLocal userId so FoundryMemoryProvider scopes memory to this user]
    ScopeMem --> Resume{conversationId provided}
    Resume -->|no| NewSession[Create new Foundry session and thread]
    Resume -->|yes| ResumeSession[Resume existing Foundry thread by conversationId]
    NewSession --> Retrieve
    ResumeSession --> Retrieve
    Retrieve[FoundryMemoryProvider injects scoped long term memory before the run] --> Intent{intent}

    Intent -->|field_help| FieldHelp[Apply framework rules for the named field. Output bullet tips and a Suggested wording blockquote tailored to currentValue and sectionValues]
    Intent -->|review| ReviewPath[Run rubric against the framework for the named field. Emit pass fail warn lines and a revised Suggested wording. If currentValue is empty mark every rule as fail]
    Intent -->|section_review| SectionReview[Critique every field in sectionValues. List what is missing, what could be sharper, end with the single most important next step]
    Intent -->|chat| ChatPath[Reply in 1 to 3 paragraphs of plain prose. No rubric, no bullet tips, no Suggested wording. If user asks for form ready content redirect them to Ask AI or Review buttons]
    Intent -->|freeform| Freeform[Answer userMessage directly while staying on topic of current section and field]

    FieldHelp --> Guard
    ReviewPath --> Guard
    SectionReview --> Guard
    ChatPath --> Guard
    Freeform --> Guard

    Guard{Hard rules check} --> G1[Never invent numbers. Prefix examples with Example colon]
    Guard --> G2[Never duplicate values across Problem Statement and KPI fields. Problem Statement is narrative, KPI fields are atomic]
    Guard --> G3[Do not propose solutions inside Problem Statement, only describe current state]
    Guard --> G4[Reuse user words and numbers from sectionValues. Never reveal another user data]

    G1 --> Persist
    G2 --> Persist
    G3 --> Persist
    G4 --> Persist

    Persist[FoundryMemoryProvider persists the new turn to the user scoped memory store] --> Respond([Return userId, response markdown, conversationId])

    Reject:::err
    classDef err fill:#fde2e1,stroke:#b23a2f,color:#7a1f17
```

### Notes

- The same agent and same endpoint serve every intent. Only the system prompt
  decides the response shape.
- `field_help` and `review` always include a `> Suggested wording` blockquote so
  the UI's `Use this` button has something to apply.
- `chat` is the only intent that **suppresses** rubric/blockquote output, so
  chat messages cannot be accidentally pasted into a charter field.
- The hard rules block runs after the intent branch and before the response is
  emitted. They are the guardrails that prevent the cross-field confusion bug
  (e.g. recommending Problem Statement match KPI Actual).
- Memory is **per `userId`** via `FoundryMemoryProvider`, independent of which
  intent or which conversation thread is in use.
