#!/usr/bin/env bash
# Writes a static OpenAPI snapshot to dist-openapi/openapi.json.
#
# There is no live deployment to browse /swagger on (Swagger is only mounted in the Development
# environment). The Swashbuckle CLI builds the host in-process without starting the server and
# without touching the database (migrations on startup are switched off below), so this needs no
# Postgres. Any syntactically valid RSA public key satisfies the JWT configuration: no token is
# ever minted or verified here.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

openssl genrsa -out "$tmp/private.pem" 2048 2>/dev/null
openssl rsa -in "$tmp/private.pem" -pubout -out "$tmp/public.pem" 2>/dev/null

dotnet tool restore
dotnet build "$root/src/OrderApi.Api" -c Release

export ASPNETCORE_ENVIRONMENT=Development
export Database__MigrateOnStartup=false
export ConnectionStrings__DefaultConnection="Host=localhost;Database=unused"
export Jwt__PublicKeyPath="$tmp/public.pem"
# The broker options are validated at startup; no connection is ever opened here.
export RabbitMq__UserName=openapi
export RabbitMq__Password=openapi
export RabbitMq__AllowInsecure=true

dotnet swagger tofile --output "$root/dist-openapi/openapi.json" \
  "$root/src/OrderApi.Api/bin/Release/net10.0/OrderApi.Api.dll" v1
