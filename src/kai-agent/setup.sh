#!/usr/bin/env bash
# KAI Agent — Setup Script
# Provisions Azure resources, configures permissions, and starts the agent.
#
# Usage:
#   ./setup.sh              # Full setup (interactive)
#   ./setup.sh --env-only   # Just create .env template
#   ./setup.sh --agent-only # Just create/update Foundry agent
#   ./setup.sh --run        # Start backend + UI servers

set -euo pipefail
cd "$(dirname "$0")"
REPO_ROOT="$(cd ../.. && pwd)"

# Colors
RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; NC='\033[0m'
info() { echo -e "${GREEN}[INFO]${NC} $1"; }
warn() { echo -e "${YELLOW}[WARN]${NC} $1"; }
err()  { echo -e "${RED}[ERROR]${NC} $1"; }

# ---------------------------------------------------------------------------
# Prerequisites Check
# ---------------------------------------------------------------------------
check_prereqs() {
    info "Checking prerequisites..."
    local missing=()

    command -v python3 >/dev/null 2>&1 || missing+=("python3 (3.11+)")
    command -v az >/dev/null 2>&1 || missing+=("az (Azure CLI)")
    command -v pip >/dev/null 2>&1 || missing+=("pip")

    if [ ${#missing[@]} -gt 0 ]; then
        err "Missing required tools:"
        for tool in "${missing[@]}"; do echo "  - $tool"; done
        echo ""
        echo "Install Azure CLI: https://learn.microsoft.com/en-us/cli/azure/install-azure-cli"
        echo "Install Python 3.11+: https://www.python.org/downloads/"
        exit 1
    fi

    # Check Python version
    PY_VERSION=$(python3 -c "import sys; print(f'{sys.version_info.major}.{sys.version_info.minor}')")
    if python3 -c "import sys; exit(0 if sys.version_info >= (3, 11) else 1)"; then
        info "Python $PY_VERSION ✓"
    else
        err "Python 3.11+ required (found $PY_VERSION)"
        exit 1
    fi

    # Check Azure login
    if az account show >/dev/null 2>&1; then
        local acct=$(az account show --query name -o tsv)
        info "Azure CLI logged in: $acct ✓"
    else
        warn "Not logged in to Azure CLI. Running 'az login'..."
        az login
    fi
}

# ---------------------------------------------------------------------------
# Environment Setup
# ---------------------------------------------------------------------------
setup_env() {
    local ENV_FILE="$REPO_ROOT/.env"

    if [ -f "$ENV_FILE" ]; then
        info ".env already exists at $ENV_FILE"
        source "$ENV_FILE" 2>/dev/null || true
        return
    fi

    info "Creating .env file..."
    echo ""
    echo "Enter your Azure AI Foundry project endpoint."
    echo "Format: https://<resource>.services.ai.azure.com/api/projects/<project>"
    read -rp "FOUNDRY_PROJECT_ENDPOINT: " FOUNDRY_ENDPOINT

    echo ""
    echo "Enter your model deployment name (default: gpt-4o-mini):"
    read -rp "AZURE_AI_MODEL_DEPLOYMENT_NAME [gpt-4o-mini]: " MODEL_NAME
    MODEL_NAME="${MODEL_NAME:-gpt-4o-mini}"

    cat > "$ENV_FILE" <<EOF
# Azure AI Foundry Configuration
FOUNDRY_PROJECT_ENDPOINT=$FOUNDRY_ENDPOINT
AZURE_AI_MODEL_DEPLOYMENT_NAME=$MODEL_NAME
EOF

    info ".env created at $ENV_FILE"
}

# ---------------------------------------------------------------------------
# Python Environment
# ---------------------------------------------------------------------------
setup_venv() {
    info "Setting up Python virtual environment..."

    if [ -d ".venv" ] || [ -L ".venv" ]; then
        info "Virtual environment already exists"
    else
        python3 -m venv .venv
        info "Created .venv"
    fi

    source .venv/bin/activate

    info "Installing dependencies..."
    pip install --quiet --upgrade pip
    pip install --quiet -r requirements.txt 2>&1 | tail -5

    info "Python environment ready ✓"
}

# ---------------------------------------------------------------------------
# Azure Permissions
# ---------------------------------------------------------------------------
setup_permissions() {
    info "Configuring Azure permissions..."

    source "$REPO_ROOT/.env" 2>/dev/null || true

    if [ -z "${FOUNDRY_PROJECT_ENDPOINT:-}" ]; then
        err "FOUNDRY_PROJECT_ENDPOINT not set. Run setup with --env-only first."
        exit 1
    fi

    # Get current user info
    local USER_ID=$(az ad signed-in-user show --query id -o tsv 2>/dev/null || echo "")
    if [ -z "$USER_ID" ]; then
        warn "Could not determine signed-in user. Skipping role assignments."
        return
    fi
    local USER_UPN=$(az ad signed-in-user show --query userPrincipalName -o tsv)
    info "Configuring roles for: $USER_UPN"

    # Extract resource group and resource from endpoint
    # Format: https://<resource>.services.ai.azure.com/api/projects/<project>
    local RESOURCE_NAME=$(echo "$FOUNDRY_PROJECT_ENDPOINT" | sed -n 's|https://\([^.]*\)\.services\.ai\.azure\.com.*|\1|p')

    if [ -z "$RESOURCE_NAME" ]; then
        warn "Could not parse resource name from endpoint. Manual permission setup required."
        echo "  See docs/permissions-and-setup.md for required roles."
        return
    fi

    info "AI Services resource: $RESOURCE_NAME"

    # Find the resource ID
    local RESOURCE_ID=$(az resource list --name "$RESOURCE_NAME" --resource-type "Microsoft.CognitiveServices/accounts" --query "[0].id" -o tsv 2>/dev/null || echo "")

    if [ -z "$RESOURCE_ID" ]; then
        warn "Could not find resource '$RESOURCE_NAME'. Ensure it exists and you have access."
        echo "  Required roles (assign manually):"
        echo "    - Azure AI Developer (on AI Services resource)"
        echo "    - Cognitive Services OpenAI User (on AI Services resource)"
        return
    fi

    echo ""
    info "Assigning required RBAC roles..."

    # Azure AI Developer — required for Foundry Agent API
    echo -n "  Azure AI Developer... "
    az role assignment create \
        --assignee "$USER_ID" \
        --role "Azure AI Developer" \
        --scope "$RESOURCE_ID" \
        --only-show-errors >/dev/null 2>&1 && echo "✓" || echo "(already assigned or insufficient privileges)"

    # Cognitive Services OpenAI User — required for model inference
    echo -n "  Cognitive Services OpenAI User... "
    az role assignment create \
        --assignee "$USER_ID" \
        --role "Cognitive Services OpenAI User" \
        --scope "$RESOURCE_ID" \
        --only-show-errors >/dev/null 2>&1 && echo "✓" || echo "(already assigned or insufficient privileges)"

    # Cognitive Services User — required for agent operations
    echo -n "  Cognitive Services User... "
    az role assignment create \
        --assignee "$USER_ID" \
        --role "Cognitive Services User" \
        --scope "$RESOURCE_ID" \
        --only-show-errors >/dev/null 2>&1 && echo "✓" || echo "(already assigned or insufficient privileges)"

    info "Permissions configured ✓"
    echo ""
    warn "Note: Role assignments can take 5-10 minutes to propagate."
}

# ---------------------------------------------------------------------------
# Create Foundry Agent
# ---------------------------------------------------------------------------
setup_agent() {
    info "Creating/updating KAI Foundry Prompt Agent..."
    source .venv/bin/activate 2>/dev/null || true

    python3 create_agent.py --all

    info "Foundry agent setup complete ✓"
}

# ---------------------------------------------------------------------------
# Run Servers
# ---------------------------------------------------------------------------
run_servers() {
    info "Starting KAI agent servers..."
    source .venv/bin/activate 2>/dev/null || true

    # Start backend
    info "Starting backend on port 8001..."
    uvicorn main:app --port 8001 --host 0.0.0.0 &
    BACKEND_PID=$!

    # Start UI
    info "Starting UI on port 3002..."
    python3 -m http.server 3002 --directory ../kai-ui &
    UI_PID=$!

    sleep 2
    echo ""
    info "═══════════════════════════════════════════════"
    info "  KAI Agent is running!"
    info "═══════════════════════════════════════════════"
    info "  Backend API:  http://localhost:8001"
    info "  Swagger UI:   http://localhost:8001/docs"
    info "  Chat UI:      http://localhost:3002"
    info "  Health:       http://localhost:8001/health"
    info ""
    info "  Press Ctrl+C to stop all servers"
    info "═══════════════════════════════════════════════"

    trap "kill $BACKEND_PID $UI_PID 2>/dev/null; exit" INT TERM
    wait
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
main() {
    echo ""
    echo "╔══════════════════════════════════════════════╗"
    echo "║     KAI — Kaizen AI Coach Setup             ║"
    echo "╚══════════════════════════════════════════════╝"
    echo ""

    case "${1:-}" in
        --env-only)
            setup_env
            ;;
        --agent-only)
            setup_agent
            ;;
        --run)
            run_servers
            ;;
        --permissions)
            setup_permissions
            ;;
        *)
            check_prereqs
            setup_env
            setup_venv
            setup_permissions
            setup_agent
            echo ""
            info "═══════════════════════════════════════════════"
            info "  Setup complete! Start servers with:"
            info "    ./setup.sh --run"
            info "═══════════════════════════════════════════════"
            ;;
    esac
}

main "$@"
