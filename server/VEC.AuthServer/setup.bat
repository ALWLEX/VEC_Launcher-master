@echo off
REM VEC Auth Server — One-Click Setup (Windows)
REM Usage: double-click setup.bat or run in CMD
REM Requires: Docker Desktop installed and running

echo.
echo   VEC Auth Server Setup
echo.

docker --version >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: Docker not installed!
    echo Install Docker Desktop: https://docs.docker.com/desktop/install/windows-install/
    pause
    exit /b 1
)

if not exist data\skins mkdir data\skins
if not exist data\capes mkdir data\capes

echo Building Docker image...
docker compose build
if %errorlevel% neq 0 (
    docker-compose build
)

echo.
echo Starting server...
docker compose up -d
if %errorlevel% neq 0 (
    docker-compose up -d
)

echo.
echo   Server started!
echo.
echo   URL:    http://localhost:8080
echo   API:    http://localhost:8080/api/info
echo   Status: http://localhost:8080/api/status
echo.
echo   Logs:   docker compose logs -f
echo   Stop:   docker compose down
echo.

pause
