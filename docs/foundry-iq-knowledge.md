# Foundry IQ — Knowledge Grounding

## What is Foundry IQ?

Foundry IQ is Azure AI Foundry's built-in knowledge retrieval system. It enables agents to ground their responses in your organization's data using Retrieval-Augmented Generation (RAG) without writing custom retrieval code.

**Key concepts:**
- **Knowledge Source** — Points to a data store (Azure AI Search index, blob storage, etc.)
- **Knowledge Base** — A collection of knowledge sources with a reasoning model
- **MCP Tool** — Model Context Protocol tool registered on the agent for knowledge retrieval

## Architecture

```
User Question
     │
     ▼
┌─────────────────┐
│  Foundry Agent  │
│  (kai-kaizen-   │
│   coach)        │
└────────┬────────┘
         │ MCP tool call
         ▼
┌─────────────────┐     ┌──────────────────────┐
│  Knowledge Base │────▶│  Azure AI Search     │
│  (kai-knowledge │     │  Index: kai-knowledge│
│   -base)        │     │  11 documents        │
└─────────────────┘     └──────────────────────┘
         │
         ▼ Retrieved context
┌─────────────────┐
│  Agent Response  │
│  (grounded in   │
│   knowledge)     │
└─────────────────┘
```

## How It Works

1. The Foundry Prompt Agent has an MCP tool registered that connects to the knowledge base
2. When a user asks about Lean methodology, charter best practices, etc., the agent decides to retrieve relevant knowledge
3. The MCP tool queries Azure AI Search using the knowledge base configuration
4. Retrieved documents are injected into the agent's context
5. The agent generates a response grounded in the retrieved knowledge

## Knowledge Index Contents

The `kai-knowledge` index contains 11 documents covering:

| Document | Topic |
|----------|-------|
| Kaizen Event Planning | End-to-end event planning guide |
| Problem Statement Guide | Data-driven problem statement writing |
| KPI and SMART Metrics | Setting measurable improvement targets |
| A3 Thinking | Structured problem-solving on one page |
| 5-Whys Analysis | Root cause identification technique |
| Value Stream Mapping | Visualizing process flow and waste |
| 8 Wastes (DOWNTIME) | Identifying waste categories |
| Charter Quality Standards | Scoring rubrics for charter review |
| Team Composition | Cross-functional team formation |
| Daily Milestone Planning | Structuring the event day-by-day |
| Sustainability Planning | Maintaining gains post-event |

## Setup Steps

### 1. Create Azure AI Search Service

```bash
az search service create \
  --name <search-service-name> \
  --resource-group <rg> \
  --sku basic \
  --location <region>
```

### 2. Create Search Index

Create an index with these fields:

```json
{
  "name": "kai-knowledge",
  "fields": [
    { "name": "id", "type": "Edm.String", "key": true },
    { "name": "title", "type": "Edm.String", "searchable": true },
    { "name": "content", "type": "Edm.String", "searchable": true },
    { "name": "category", "type": "Edm.String", "filterable": true },
    { "name": "content_vector", "type": "Collection(Edm.Single)", "dimensions": 1536 }
  ]
}
```

### 3. Upload Documents

Index your knowledge documents into the search index (use the Azure AI Search REST API or SDK).

### 4. Create Knowledge Source

```bash
# Via Azure AI Foundry REST API
az rest --method POST \
  --url "<foundry-endpoint>/knowledgesources?api-version=2025-05-01-preview" \
  --body '{
    "name": "kai-knowledge-source",
    "indexName": "kai-knowledge",
    "indexConnectionId": "<search-connection-id>"
  }'
```

### 5. Create Knowledge Base

```bash
az rest --method POST \
  --url "<foundry-endpoint>/knowledgebases?api-version=2025-05-01-preview" \
  --body '{
    "name": "kai-knowledge-base",
    "knowledgeSources": ["kai-knowledge-source"],
    "retrievalReasoningEffort": "Default",
    "azureOpenAIParameters": {
      "modelDeploymentName": "gpt-4o-mini",
      "authentication": { "type": "ManagedIdentity" }
    }
  }'
```

### 6. Create Project Connection (API Key Auth)

> **Important:** The MCP endpoint requires API key authentication. Bearer token/RBAC auth returns 403 (known limitation).

```bash
az rest --method PUT \
  --url "<foundry-endpoint>/connections/kai-knowledge-base-mcp?api-version=2025-05-01-preview" \
  --body '{
    "properties": {
      "category": "CustomKeys",
      "target": "https://<search-name>.search.windows.net",
      "credentials": {
        "keys": {
          "api-key": "<search-admin-key>"
        }
      }
    }
  }'
```

### 7. Register MCP Tool on Agent

In `create_agent.py`, the MCP tool is added to the agent:

```python
from azure.ai.projects.models import McpTool

tools.append(McpTool(
    server_label="knowledge_base_retrieve",
    server_url=f"{endpoint}/knowledgebases/kai-knowledge-base/mcp",
    connection_id="kai-knowledge-base-mcp",
))
```

## Known Limitations

1. **MCP Auth:** Only API key auth works for the MCP endpoint (not RBAC/Bearer). Use `CustomKeys` connection type.
2. **Model Requirement:** If `retrievalReasoningEffort` is not `Minimal`, the knowledge base requires a model deployment for reasoning.
3. **Managed Identity for Search:** The Search service's system-assigned MI needs `Cognitive Services OpenAI User` on the AI Services resource to call the embedding model.
4. **Index Updates:** After uploading new documents, allow a few minutes for indexing before they appear in agent responses.

## Extending the Knowledge Base

To add more knowledge documents:

1. Prepare documents with `id`, `title`, `content`, and optionally `category`
2. Upload to the `kai-knowledge` index via Search REST API
3. (Optional) Generate embeddings for `content_vector` field using the text-embedding-3-small model
4. The agent will automatically retrieve from new documents on next query
