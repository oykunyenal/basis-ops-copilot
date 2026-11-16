#!/bin/sh
# Imports the Basis Ops workflows into a running n8n container.
# The CLI import keeps the workflow ids, so the sub-workflow references (agent -> unlock -> approval) keep working.
# Importing again overwrites these four workflows (including the credentials selected in them) and unpublishes them.
#
# Usage: n8n/import-workflows.sh [container-name]    (default: ai-assistant-n8n-1)
set -eu

CONTAINER="${1:-ai-assistant-n8n-1}"
SOURCE="$(cd "$(dirname "$0")" && pwd)/workflows"
TARGET=/home/node/.basis-ops-import

docker exec "$CONTAINER" sh -c "rm -rf $TARGET && mkdir -p $TARGET"
for file in "$SOURCE"/*.json; do
  docker cp "$file" "$CONTAINER:$TARGET/"
done
docker exec "$CONTAINER" n8n import:workflow --separate --input="$TARGET"
docker exec "$CONTAINER" rm -rf "$TARGET"

echo "Imported. Next: select the credentials, set telegramChatId and publish (see n8n/README.md)."
