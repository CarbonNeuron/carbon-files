#!/usr/bin/env bash
set -euo pipefail

OUTPUT="${1:-openapi.json}"

dotnet build src/CarbonFiles.Api --configuration Release --verbosity quiet

# Start the API in the background with a temp database
export CarbonFiles__DbPath="/tmp/carbonfiles-openapi-$$.db"
export CarbonFiles__DataDir="/tmp/carbonfiles-openapi-$$"
export CarbonFiles__AdminKey="openapi-export-key"
export ASPNETCORE_URLS="http://localhost:5000"
mkdir -p "$CarbonFiles__DataDir"

dotnet run --project src/CarbonFiles.Api --configuration Release --no-build --no-launch-profile &
API_PID=$!

cleanup() {
  kill "$API_PID" 2>/dev/null || true
  rm -rf "$CarbonFiles__DbPath" "$CarbonFiles__DataDir"
}
trap cleanup EXIT

# Wait for the API to start
for i in $(seq 1 30); do
  if curl -sf http://localhost:5000/healthz > /dev/null 2>&1; then
    break
  fi
  if [ "$i" -eq 30 ]; then
    echo "ERROR: API failed to start within 30 seconds" >&2
    exit 1
  fi
  sleep 1
done

# Download the spec
curl -sf http://localhost:5000/openapi/v1.json -o "$OUTPUT"
echo "OpenAPI spec exported to $OUTPUT"
