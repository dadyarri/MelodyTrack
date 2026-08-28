#!/bin/sh
set -eu

image=${1:?Usage: verify-unified-image.sh <image>}
suffix=${GITHUB_RUN_ID:-local}-$$
network=melodytrack-image-test-$suffix
database=melodytrack-image-test-db-$suffix
application=melodytrack-image-test-app-$suffix
failed_application=melodytrack-image-test-failed-app-$suffix
temporary_directory=$(mktemp -d)

cleanup() {
    docker rm -f "$application" "$failed_application" "$database" >/dev/null 2>&1 || true
    docker network rm "$network" >/dev/null 2>&1 || true
    rm -rf "$temporary_directory"
}
trap cleanup EXIT

docker network create "$network" >/dev/null
docker run --detach \
    --name "$database" \
    --network "$network" \
    --env POSTGRES_DB=melodytrack \
    --env POSTGRES_USER=melodytrack \
    --env POSTGRES_PASSWORD=image-test-password \
    postgres:16-alpine@sha256:4e6e670bb069649261c9c18031f0aded7bb249a5b6664ddec29c013a89310d50 >/dev/null

database_ready=false
for _ in $(seq 1 60); do
    if docker exec "$database" pg_isready --username melodytrack --dbname melodytrack >/dev/null 2>&1; then
        database_ready=true
        break
    fi
    sleep 1
done

if [ "$database_ready" != true ]; then
    docker logs "$database"
    echo "PostgreSQL did not become ready for the unified-image test." >&2
    exit 1
fi

docker run --detach \
    --name "$application" \
    --network "$network" \
    --publish 127.0.0.1::8080 \
    --env ASPNETCORE_URLS=http://+:8080 \
    --env "Database__ConnectionString=Host=$database;Port=5432;Database=melodytrack;Username=melodytrack;Password=image-test-password" \
    --env AuthenticationSecrets__JwtSigningPrivateKey=base64:MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQg1a+XfTTbRx+lAZXtBVgkgxPy4juOyvu9VuwfrFCy9BihRANCAATHVVdEpzPvwGWCKZ7kcmGIqi6JGlxlaa6/mELjK19tAuNSLWWbhxeWb0LaVYdquLVhzFnyWL1XsTRPxSen4PvA \
    --env AuthenticationSecrets__PasswordPepper=base64:G2UfJdjsXXVuK72YyyE+thhGeWP+luj3S6ifPMqjZtA= \
    --env AuthenticationSecrets__PortalPinPepper=base64:VFWWTyDfkCqiB2TC7OrIQpT8FyXZRCuALw2YJbQDcPw= \
    --env AuthenticationSecrets__RefreshTokenHashKey=base64:5sXZ/oCgEMjrXA1KzQGzAkN88oDl4GZS6gefagjMjW4= \
    --env AuthenticationSecrets__CsrfSigningKey=base64:NWgzsvzLSMFqAg08Nh5+7TE7dbd/paept2GeaGandu0= \
    --env PersonalData__CurrentKey=image-test-pii-key-1234567890-abcdef \
    --env PersonalData__CurrentKeyVersion=v1 \
    --env PublicUrl__BaseUrl=https://localhost \
    "$image" >/dev/null

published_address=$(docker port "$application" 8080/tcp)
base_url=http://$published_address
application_ready=false
for _ in $(seq 1 120); do
    if curl --fail --silent --show-error "$base_url/health" >/dev/null 2>&1; then
        application_ready=true
        break
    fi

    if [ "$(docker inspect --format '{{.State.Running}}' "$application")" != true ]; then
        break
    fi
    sleep 1
done

if [ "$application_ready" != true ]; then
    docker logs "$application"
    echo "The unified application image did not become healthy." >&2
    exit 1
fi

curl --fail --silent --show-error --dump-header "$temporary_directory/root.headers" \
    --output "$temporary_directory/root.html" "$base_url/"
grep --quiet '<div id="root">' "$temporary_directory/root.html"
grep --ignore-case --quiet '^cache-control: no-cache' "$temporary_directory/root.headers"
grep --ignore-case --quiet "^content-security-policy: .*script-src 'self'" "$temporary_directory/root.headers"
grep --ignore-case --quiet '^x-content-type-options: nosniff' "$temporary_directory/root.headers"
grep --ignore-case --quiet '^x-frame-options: DENY' "$temporary_directory/root.headers"
grep --ignore-case --quiet '^referrer-policy: no-referrer' "$temporary_directory/root.headers"
grep --ignore-case --quiet '^permissions-policy: camera=(), microphone=(), geolocation=()' "$temporary_directory/root.headers"

curl --fail --silent --show-error --output "$temporary_directory/nested.html" "$base_url/clients/example/history"
grep --quiet '<div id="root">' "$temporary_directory/nested.html"

curl --fail --silent --show-error --output "$temporary_directory/api.json" "$base_url/api/releases/current"
grep --quiet '"version"' "$temporary_directory/api.json"

calendar_status=$(curl --silent --show-error --output "$temporary_directory/calendar.json" \
    --write-out '%{http_code}' "$base_url/api/calendar-subscriptions/image-test-token.ics")
if [ "$calendar_status" != 404 ]; then
    echo "Expected the unknown public calendar subscription to return 404, received $calendar_status." >&2
    exit 1
fi

missing_status=$(curl --silent --show-error --dump-header "$temporary_directory/missing.headers" \
    --output "$temporary_directory/missing.json" --write-out '%{http_code}' "$base_url/api/does-not-exist")
if [ "$missing_status" != 404 ]; then
    echo "Expected /api/does-not-exist to return 404, received $missing_status." >&2
    exit 1
fi
grep --ignore-case --quiet '^content-type: application/problem+json' "$temporary_directory/missing.headers"
if grep --quiet '<div id="root">' "$temporary_directory/missing.json"; then
    echo "The missing API route returned SPA HTML." >&2
    exit 1
fi

asset_path=$(grep --only-matching --extended-regexp '/assets/[^" ]+\.js' "$temporary_directory/root.html" | head -n 1)
if [ -z "$asset_path" ]; then
    echo "The published SPA did not reference a fingerprinted JavaScript asset." >&2
    exit 1
fi
curl --fail --silent --show-error --dump-header "$temporary_directory/asset.headers" \
    --output /dev/null "$base_url$asset_path"
grep --ignore-case --quiet '^cache-control: public, max-age=31536000, immutable' "$temporary_directory/asset.headers"

curl --fail --silent --show-error --header 'Accept-Encoding: gzip' \
    --dump-header "$temporary_directory/compression.headers" --output /dev/null "$base_url$asset_path"
grep --ignore-case --quiet '^content-encoding: gzip' "$temporary_directory/compression.headers"

curl --fail --silent --show-error --output /dev/null "$base_url/health"
curl --fail --silent --show-error --output /dev/null "$base_url/alive"

otel_status=$(curl --silent --show-error --output "$temporary_directory/otel.json" \
    --write-out '%{http_code}' "$base_url/otel")
if [ "$otel_status" != 404 ] || grep --quiet '<div id="root">' "$temporary_directory/otel.json"; then
    echo "The private /otel namespace was handled by the SPA fallback." >&2
    exit 1
fi

if docker run --rm --entrypoint sh "$image" -c 'command -v node || command -v nginx' >/dev/null 2>&1; then
    echo "The final runtime image contains Node.js or nginx." >&2
    exit 1
fi

if docker run \
    --name "$failed_application" \
    --network "$network" \
    --env "Database__ConnectionString=Host=$database;Port=5432;Database=melodytrack;Username=melodytrack;Password=image-test-password" \
    --env AuthenticationSecrets__JwtSigningPrivateKey=base64:MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQg1a+XfTTbRx+lAZXtBVgkgxPy4juOyvu9VuwfrFCy9BihRANCAATHVVdEpzPvwGWCKZ7kcmGIqi6JGlxlaa6/mELjK19tAuNSLWWbhxeWb0LaVYdquLVhzFnyWL1XsTRPxSen4PvA \
    --env AuthenticationSecrets__PasswordPepper=base64:G2UfJdjsXXVuK72YyyE+thhGeWP+luj3S6ifPMqjZtA= \
    --env AuthenticationSecrets__PortalPinPepper=base64:VFWWTyDfkCqiB2TC7OrIQpT8FyXZRCuALw2YJbQDcPw= \
    --env AuthenticationSecrets__RefreshTokenHashKey=base64:5sXZ/oCgEMjrXA1KzQGzAkN88oDl4GZS6gefagjMjW4= \
    --env AuthenticationSecrets__CsrfSigningKey=base64:NWgzsvzLSMFqAg08Nh5+7TE7dbd/paept2GeaGandu0= \
    --env PersonalData__CurrentKey=image-test-pii-key-1234567890-abcdef \
    --env PersonalData__CurrentKeyVersion=v1 \
    --env PublicUrl__BaseUrl=https://localhost \
    --env Initialization__QuartzSqlPath=/missing/quartz.sql \
    "$image" >"$temporary_directory/failed-init.log" 2>&1; then
    echo "The unified image started Backend after Init was configured to fail." >&2
    exit 1
fi
if ! grep --quiet 'Quartz database initialization script was not found' "$temporary_directory/failed-init.log"; then
    cat "$temporary_directory/failed-init.log"
    echo "The failure-gating check did not reach the intended Init failure." >&2
    exit 1
fi
if grep --quiet 'Now listening on' "$temporary_directory/failed-init.log"; then
    echo "Backend started despite the failed Init process." >&2
    exit 1
fi

echo "Unified image HTTP verification passed."
