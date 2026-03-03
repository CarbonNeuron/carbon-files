#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
SPEC="${1:-$SCRIPT_DIR/openapi.json}"

cd "$SCRIPT_DIR"

# Generate into a temp directory, then move the package contents
openapi-python-client generate \
  --path "$SPEC" \
  --config config.yml \
  --meta poetry \
  --output-path ./generated \
  --overwrite

# Move the generated package directory into place
rm -rf carbonfiles_client
cp -r generated/carbonfiles_client .
cp generated/pyproject.toml .
# NOTE: not copying generated/README.md — using hand-written README instead
rm -rf generated

echo "Python client generated successfully"
