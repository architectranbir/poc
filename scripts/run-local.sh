#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
export SEED_PATH="$ROOT/seed"
export WEB_ORIGIN=http://localhost:5173
export ASPNETCORE_ENVIRONMENT=Development
cd "$ROOT/src/Copily.Api"
dotnet run --urls http://localhost:8080
