#!/usr/bin/env bash
# Repository configuration.
app_name="howToCookViewer"
extra_docker_args=()

set -euo pipefail

# Keep the original no-argument and port-only invocation working.
port=""
case $# in
    0) ;;
    1)
        if [[ "$1" =~ ^[0-9]+$ ]]; then
            port="$1"
        else
            app_name="$1"
            port=5000
        fi
        ;;
    2) app_name="$1"; port="$2" ;;
    *) echo "Usage: sudo bash install.sh [port] or <app-name> [port]" >&2; exit 1 ;;
esac
if [[ ! "$app_name" =~ ^[a-zA-Z0-9][a-zA-Z0-9-]*$ ]]; then
    echo "Invalid application name." >&2
    exit 1
fi
if [[ -n "$port" ]]; then
    if [[ ! "$port" =~ ^[0-9]{1,5}$ ]]; then
        echo "Port must be between 1 and 65535." >&2
        exit 1
    fi
    port=$((10#$port))
    if (( port < 1 || port > 65535 )); then
        echo "Port must be between 1 and 65535." >&2
        exit 1
    fi
else
    # Preserve the original automatic port range, and keep it stable on restart.
    if ! command -v ss >/dev/null 2>&1; then
        echo "Install iproute2 to choose a port automatically, or supply a port." >&2
        exit 1
    fi
    while :; do
        port=$(shuf -i 40000-65000 -n 1)
        listeners=$(ss -H -ltn)
        if ! grep -qE ":$port[[:space:]]" <<< "$listeners"; then
            break
        fi
    done
fi
if (( EUID != 0 )); then
    echo "Run this installer with sudo." >&2
    exit 1
fi

image_name="${app_name,,}"

# Existing source/package installations need an explicit data migration.
if [[ -e "/etc/systemd/system/$app_name.service" || -e "/lib/systemd/system/$app_name.service" || -e "/usr/lib/systemd/system/$app_name.service" || -e "/lib/systemd/system/aiursoft-$image_name.service" ]]; then
    echo "An existing systemd installation was found. Migrate its configuration and data before using Docker." >&2
    exit 1
fi

image="docker.io/aiursoft/$image_name:latest"
echo "This will install Docker if needed and run $image (host port: $port)."
echo "Configuration and data will use the '$app_name-data' Docker volume."
echo "Installation starts in three seconds. Press Ctrl+C to cancel."
trap 'echo "Installation cancelled."; exit 130' INT
for seconds in 3 2 1; do
    echo "$seconds..."
    sleep 1
done
trap - INT

if ! command -v docker >/dev/null 2>&1; then
    . /etc/os-release
    case " ${ID:-} ${ID_LIKE:-} " in
        *" debian "*|*" ubuntu "*|*" anduinos "*)
            apt-get update
            apt-get install -y docker.io
            ;;
        *" rhel "*|*" centos "*|*" fedora "*)
            # Docker maintains the distribution-specific repository setup.
            dnf install -y ca-certificates curl
            docker_installer=$(mktemp)
            trap 'rm -f "$docker_installer"' EXIT
            curl --fail --silent --show-error --location https://get.docker.com -o "$docker_installer"
            sh "$docker_installer"
            ;;
        *)
            echo "Install Docker Engine for this distribution, then run this script again." >&2
            exit 1
            ;;
    esac
    systemctl enable --now docker
fi

# Reuse an existing engine without reinstalling it or changing its configuration.
if ! docker info >/dev/null 2>&1; then
    echo "Docker Engine is not available. Start it and check access before retrying." >&2
    exit 1
fi
if docker container inspect "$app_name" >/dev/null 2>&1; then
    echo "Container '$app_name' already exists. It has been left unchanged." >&2
    exit 1
fi

docker pull "$image"
docker run --detach --name "$app_name" --restart unless-stopped \
    --publish "$port:5000" \
    --mount "type=volume,source=$app_name-data,target=/data" \
    "${extra_docker_args[@]}" \
    "$image"

# Older callers use systemctl restart <app> after installation.
if [[ -d /run/systemd/system ]]; then
    docker_command=$(command -v docker)
    cat > "/etc/systemd/system/$app_name.service" <<EOF
[Unit]
Description=$app_name Docker container
Requires=docker.service
After=docker.service

[Service]
Type=oneshot
RemainAfterExit=yes
ExecStart=$docker_command start $app_name
ExecStop=$docker_command stop $app_name

[Install]
WantedBy=multi-user.target
EOF
    systemctl daemon-reload
    systemctl enable --now "$app_name"
fi

echo "Container created. Open http://localhost:$port after the application starts."
echo "Configuration and data are stored in the '$app_name-data' Docker volume."
echo "View startup logs with: sudo docker logs $app_name"
