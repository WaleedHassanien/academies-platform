#!/usr/bin/env bash
# Runs on the server (called by the deploy workflow, or by hand from ~/academies).
# Needs: .env, docker-compose.prod.yml, Caddyfile and mysql/init.sql in this directory.
set -euo pipefail
cd "$(dirname "$0")"

[ -f .env ] || { echo "Missing $(pwd)/.env: copy .env.prod.example and fill it in." >&2; exit 1; }

# Identity refuses to start without a signing key outside Development. Create it once and keep it:
# replacing it signs everyone out. The container user (uid 1654) must be able to read it.
if [ ! -f keys/signing.pem ]; then
  mkdir -p keys
  openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out keys/signing.pem
  sudo chown 1654:1654 keys/signing.pem
  sudo chmod 600 keys/signing.pem
fi

compose() { docker compose -f docker-compose.prod.yml "$@"; }
compose pull
compose up -d --remove-orphans
docker image prune -f
compose ps
