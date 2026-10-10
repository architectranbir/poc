#!/usr/bin/env bash
set -euo pipefail
echo "Azure deployment is disabled for this unauthenticated local scaffold." >&2
echo "See docs/IMPLEMENTATION_PLAN.md. Entra auth and durable gates must be implemented first." >&2
exit 1
