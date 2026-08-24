#!/bin/bash
# VEC Auth Server — One-Click Setup
# Usage: chmod +x setup.sh && ./setup.sh
# Requires: Docker + Docker Compose, port 8080 free

set -e

echo "VEC Auth Server Setup"
echo ""

if ! command -v docker &> /dev/null; then
    echo "Docker not installed!"
    echo "  Install: https://docs.docker.com/get-docker/"
    exit 1
fi

if ! command -v docker compose &> /dev/null && ! command -v docker-compose &> /dev/null; then
    echo "Docker Compose not installed!"
    echo "  Install: https://docs.docker.com/compose/install/"
    exit 1
fi

if ss -tlnp 2>/dev/null | grep -q ":8080 " || netstat -tlnp 2>/dev/null | grep -q ":8080 "; then
    echo "Port 8080 is already in use!"
    echo "  Stop the current service or change the port in docker-compose.yml"
    exit 1
fi

mkdir -p data/skins data/capes

echo "Building Docker image..."
if command -v docker compose &> /dev/null; then
    docker compose build
else
    docker-compose build
fi

echo ""
echo "Starting server..."
if command -v docker compose &> /dev/null; then
    docker compose up -d
else
    docker-compose up -d
fi

echo ""
echo "Server started!"
echo ""
echo "  URL:    http://localhost:8080"
echo "  API:    http://localhost:8080/api/info"
echo "  Status: http://localhost:8080/api/status"
echo ""
echo "  Logs:   docker compose logs -f"
echo "  Stop:   docker compose down"
echo ""
echo "  Connect Minecraft server:"
echo "  java -javaagent:authlib-injector.jar=http://YOUR_IP:8080/api/yggdrasil -jar server.jar"
echo ""
