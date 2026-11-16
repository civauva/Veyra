#!/bin/sh
# Installs the Veyra agent on Linux (ADR 0019): the official OpenTelemetry Collector run by its OpAMP supervisor, as
# the systemd service "veyra-agent". Veyra serves this script with its own address filled in.
#
# Run as root, with the code from Settings -> Agents:
#   curl -fsSL __VEYRA_URL__/v1/agents/install.sh | sudo sh -s -- --code XXXX-XXXX-XXXX
# Remove the agent again with:
#   curl -fsSL __VEYRA_URL__/v1/agents/install.sh | sudo sh -s -- --uninstall
#
# The programs come from OpenTelemetry's releases on GitHub and are checked against the SHA-256 values below, which
# Veyra pins with the version it tests. The agent only connects out: to Veyra, and to the servers it reads.
set -eu

VEYRA_URL='__VEYRA_URL__'
VERSION='__VERSION__'
PROGRAMS='__LINUX_PROGRAMS__'
STATE='__LINUX_DATA__'
CONFIG_DIR='/etc/veyra-agent'
CACHE='/var/cache/veyra-agent'
SERVICE='veyra-agent'
RELEASE='https://github.com/open-telemetry/opentelemetry-collector-releases/releases/download'

fail() {
    echo "veyra-agent: $1" >&2
    exit 1
}

uninstall() {
    systemctl disable --now "$SERVICE" 2>/dev/null || true
    rm -f "/etc/systemd/system/$SERVICE.service"
    systemctl daemon-reload
    rm -rf "$PROGRAMS" "$CONFIG_DIR" "$STATE" "$CACHE"
    userdel "$SERVICE" 2>/dev/null || true
    echo "The Veyra agent is removed. Remove it under Settings -> Agents too, which revokes its key."
}

# Downloads a file unless an intact copy is there already, and checks its SHA-256.
fetch() {
    url=$1
    file=$2
    sha=$3

    if [ -f "$file" ] && echo "$sha  $file" | sha256sum -c - >/dev/null 2>&1; then
        return
    fi

    echo "Downloading $(basename "$file")..."
    curl -fsSL -o "$file" "$url"

    if ! echo "$sha  $file" | sha256sum -c - >/dev/null 2>&1; then
        rm -f "$file"
        fail "$(basename "$file") does not match the SHA-256 Veyra expects. Nothing was installed."
    fi
}

CODE=''

while [ $# -gt 0 ]; do
    case $1 in
        --code) CODE=${2:-}; shift 2 ;;
        --uninstall) [ "$(id -u)" -eq 0 ] || fail "run as root, for example with sudo."; uninstall; exit 0 ;;
        *) fail "unknown option $1. Use --code XXXX-XXXX-XXXX, or --uninstall." ;;
    esac
done

[ "$(id -u)" -eq 0 ] || fail "run as root, for example with sudo."
case $CODE in
    ''|*[!A-Za-z0-9-]*) fail "give the one-time code from Settings -> Agents: --code XXXX-XXXX-XXXX." ;;
esac
command -v systemctl >/dev/null || fail "this installer needs systemd."
command -v curl >/dev/null || fail "this installer needs curl."
command -v sha256sum >/dev/null || fail "this installer needs sha256sum."

case $(uname -m) in
    x86_64|amd64)
        ARCH=amd64
        SUPERVISOR_SHA='__SUPERVISOR_LINUX_AMD64__'
        COLLECTOR_SHA='__COLLECTOR_LINUX_AMD64__'
        ;;
    aarch64|arm64)
        ARCH=arm64
        SUPERVISOR_SHA='__SUPERVISOR_LINUX_ARM64__'
        COLLECTOR_SHA='__COLLECTOR_LINUX_ARM64__'
        ;;
    *) fail "the agent runs on amd64 and arm64 machines, not on $(uname -m)." ;;
esac

mkdir -p "$CACHE"
SUPERVISOR_FILE="$CACHE/opampsupervisor_${VERSION}_linux_$ARCH"
COLLECTOR_FILE="$CACHE/otelcol-contrib_${VERSION}_linux_$ARCH.tar.gz"
fetch "$RELEASE/cmd%2Fopampsupervisor%2Fv$VERSION/$(basename "$SUPERVISOR_FILE")" "$SUPERVISOR_FILE" "$SUPERVISOR_SHA"
fetch "$RELEASE/v$VERSION/$(basename "$COLLECTOR_FILE")" "$COLLECTOR_FILE" "$COLLECTOR_SHA"

echo "Asking Veyra for the agent's settings..."
STARTUP=$(mktemp)
trap 'rm -f "$STARTUP"' EXIT
chmod 600 "$STARTUP"

if ! curl -fsS -o "$STARTUP" -X POST -H 'Content-Type: application/json' \
    -d "{\"code\":\"$CODE\",\"platform\":\"Linux\"}" "$VEYRA_URL/v1/agents/enroll"; then
    fail "Veyra did not accept the code: it is wrong, used, or older than an hour. Make a new one under Settings -> Agents."
fi

# The agent runs as its own unprivileged user, the only one that may read its start-up file and its key.
id "$SERVICE" >/dev/null 2>&1 || useradd --system --no-create-home --shell /usr/sbin/nologin "$SERVICE"

systemctl stop "$SERVICE" 2>/dev/null || true
mkdir -p "$PROGRAMS" "$CONFIG_DIR" "$STATE"
install -m 755 "$SUPERVISOR_FILE" "$PROGRAMS/opampsupervisor"
tar -xzf "$COLLECTOR_FILE" -C "$PROGRAMS" otelcol-contrib
chmod 755 "$PROGRAMS/otelcol-contrib"
install -m 600 -o "$SERVICE" -g "$SERVICE" "$STARTUP" "$CONFIG_DIR/supervisor.yaml"
chown -R "$SERVICE:$SERVICE" "$STATE"
chmod 700 "$CONFIG_DIR" "$STATE"
chown "$SERVICE:$SERVICE" "$CONFIG_DIR"

cat > "/etc/systemd/system/$SERVICE.service" <<UNIT
[Unit]
Description=Veyra agent: the OpenTelemetry Collector run by its OpAMP supervisor
Wants=network-online.target
After=network-online.target

[Service]
User=$SERVICE
ExecStart=$PROGRAMS/opampsupervisor --config $CONFIG_DIR/supervisor.yaml
Restart=always
RestartSec=60

[Install]
WantedBy=multi-user.target
UNIT

systemctl daemon-reload
systemctl enable "$SERVICE" >/dev/null 2>&1
systemctl restart "$SERVICE"
echo "The Veyra agent is installed and running. Settings -> Agents shows it online within a minute."
