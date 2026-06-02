# Permissions & Setup Guide

## Prerequisites

| Tool | Version | Purpose |
|------|---------|---------|
| Python | 3.11+ | Agent runtime |
| Azure CLI (`az`) | 2.60+ | Resource management and auth |
| Azure subscription | — | Hosts AI Foundry project |

## Azure Resources Required

### 1. Azure AI Foundry Project

The agent requires an Azure AI Foundry project with:
- A deployed model (e.g., `gpt-4o-mini` or `gpt-4o`)
- Agent service enabled

**Create via Portal:**  
Azure Portal → Azure AI Foundry → Create Project

**Create via CLI:**
```bash
az cognitiveservices account create \
  --name <resource-name> \
  --resource-group <rg> \
  --kind AIServices \
  --sku S0 \
  --location <region>
```

### 2. Model Deployment

Deploy a model in your AI Services resource:
```bash
az cognitiveservices account deployment create \
  --name <resource-name> \
  --resource-group <rg> \
  --deployment-name gpt-4o-mini \
  --model-name gpt-4o-mini \
  --model-version "2024-07-18" \
  --model-format OpenAI \
  --sku-capacity 30 \
  --sku-name Standard
```

### 3. Azure AI Search (for Foundry IQ)

Required only if using knowledge grounding:
```bash
az search service create \
  --name <search-name> \
  --resource-group <rg> \
  --sku basic \
  --location <region>
```

## RBAC Permissions

### Required Roles for the Developer/Service Principal

| Role | Scope | Purpose |
|------|-------|---------|
| **Azure AI Developer** | AI Services resource | Create/manage agents, access Foundry APIs |
| **Cognitive Services OpenAI User** | AI Services resource | Model inference (chat completions) |
| **Cognitive Services User** | AI Services resource | Agent operations, memory, tools |

### Assign via CLI

```bash
# Get your user object ID
USER_ID=$(az ad signed-in-user show --query id -o tsv)

# Get the AI Services resource ID
RESOURCE_ID=$(az resource show \
  --name <resource-name> \
  --resource-group <rg> \
  --resource-type "Microsoft.CognitiveServices/accounts" \
  --query id -o tsv)

# Assign roles
az role assignment create --assignee "$USER_ID" --role "Azure AI Developer" --scope "$RESOURCE_ID"
az role assignment create --assignee "$USER_ID" --role "Cognitive Services OpenAI User" --scope "$RESOURCE_ID"
az role assignment create --assignee "$USER_ID" --role "Cognitive Services User" --scope "$RESOURCE_ID"
```

### Additional Roles for Foundry IQ (Knowledge Grounding)

If using Azure AI Search with Foundry IQ:

| Role | Scope | Purpose |
|------|-------|---------|
| **Search Index Data Contributor** | Search service | Index and query documents |
| **Search Service Contributor** | Search service | Manage indexes and data sources |
| **Cognitive Services OpenAI User** | AI Services (for Search MI) | Search MI calls embedding model |

```bash
SEARCH_ID=$(az resource show \
  --name <search-name> \
  --resource-group <rg> \
  --resource-type "Microsoft.Search/searchServices" \
  --query id -o tsv)

az role assignment create --assignee "$USER_ID" --role "Search Index Data Contributor" --scope "$SEARCH_ID"
az role assignment create --assignee "$USER_ID" --role "Search Service Contributor" --scope "$SEARCH_ID"
```

### Managed Identity Configuration (for Production)

For the Search service to call the embedding model:

1. Enable system-assigned managed identity on the Search service
2. Assign `Cognitive Services OpenAI User` role on the AI Services resource to the Search MI

```bash
# Enable MI on Search
az search service update --name <search-name> --resource-group <rg> --identity-type SystemAssigned

# Get the MI principal ID
SEARCH_MI=$(az search service show --name <search-name> --resource-group <rg> --query identity.principalId -o tsv)

# Grant Search MI access to AI Services
az role assignment create --assignee "$SEARCH_MI" --role "Cognitive Services OpenAI User" --scope "$RESOURCE_ID"
```

## Authentication

### Local Development

The agent uses `DefaultAzureCredential` which tries (in order):
1. Environment variables (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_CLIENT_SECRET`)
2. Azure CLI (`az login`)
3. Visual Studio Code credential
4. Managed Identity (in Azure)

**Recommended for local dev:** Use Azure CLI auth:
```bash
az login
```

### Production Deployment

Use Managed Identity (no secrets in code):
- Assign the same RBAC roles to the Managed Identity of your hosting resource (App Service, Container App, etc.)

## Environment Variables

Create a `.env` file in the repository root:

```env
# Required
FOUNDRY_PROJECT_ENDPOINT=https://<resource>.services.ai.azure.com/api/projects/<project>
AZURE_AI_MODEL_DEPLOYMENT_NAME=gpt-4o-mini

# Optional: Direct Azure OpenAI (alternative to Foundry)
# AZURE_OPENAI_ENDPOINT=https://<resource>.openai.azure.com/
# AZURE_OPENAI_API_KEY=<key>
```

### Finding Your Endpoint

1. Go to [Azure AI Foundry](https://ai.azure.com)
2. Select your project
3. Go to **Settings** → **Project details**
4. Copy the **Project endpoint** (format: `https://<resource>.services.ai.azure.com/api/projects/<project>`)

## Quick Setup (Automated)

The setup script handles everything:

```bash
cd src/kai-agent
chmod +x setup.sh
./setup.sh
```

This will:
1. ✓ Check prerequisites (Python, Azure CLI)
2. ✓ Create `.env` file (interactive)
3. ✓ Set up Python virtual environment
4. ✓ Assign RBAC roles
5. ✓ Create Foundry Prompt Agent

Then start the servers:
```bash
./setup.sh --run
```

## Troubleshooting

### "AuthorizationFailed" or 403 errors

- Ensure all RBAC roles are assigned (they take 5-10 minutes to propagate)
- Verify you're logged in with the correct account: `az account show`
- Check the resource scope matches your AI Services resource

### "Model deployment not found"

- Verify the model name in `.env` matches your deployment: `az cognitiveservices account deployment list --name <resource> --resource-group <rg>`

### "Agent not found"

- Run `python create_agent.py --all` to create the Foundry agent
- Ensure `FOUNDRY_PROJECT_ENDPOINT` is correct

### MCP/Foundry IQ 403

The MCP endpoint for Foundry IQ knowledge bases does not support Bearer token auth (known limitation). The project connection must use `CustomKeys` auth type with the Search admin API key.

### MAF packages not found on pip

The Microsoft Agent Framework Python packages (`agent-framework-core`, `agent-framework-foundry`, `agent-framework-ag-ui`) are currently in preview and may not be on public PyPI. Check the [MAF documentation](https://learn.microsoft.com/en-us/azure/ai-services/agents/) for the latest installation instructions.

## Network Requirements

| Endpoint | Port | Purpose |
|----------|------|---------|
| `*.services.ai.azure.com` | 443 | Foundry agent API |
| `*.openai.azure.com` | 443 | Model inference |
| `*.search.windows.net` | 443 | Azure AI Search (Foundry IQ) |
| `localhost` | 8001 | KAI backend (local dev) |
| `localhost` | 3002 | KAI UI (local dev) |
