# VEC Auth Server — Deploy

Auth server, skins, and Yggdrasil API for **VEC Launcher**.

---

## Quick Start (1 command)

### Linux / macOS
```bash
cd server/VEC.AuthServer
chmod +x setup.sh
./setup.sh
```

### Windows
```
Double-click setup.bat
```

### Docker (manual)
```bash
cd server/VEC.AuthServer
docker compose up -d --build
```

---

## Requirements

- **Docker** + **Docker Compose** (recommended)
- Or **.NET 8 SDK** (for running without Docker)
- Port **8080** must be free

---

## Configuration

### Environment variables (Docker)
```yaml
# docker-compose.yml
environment:
  - Server__PublicUrl=http://YOUR_IP:8080
  - Server__SkinDomains=["localhost","127.0.0.1","YOUR_IP"]
```

### appsettings.json
```json
{
  "Server": {
    "PublicUrl": "http://localhost:8080",
    "SkinDomains": ["localhost", "127.0.0.1"]
  }
}
```

### Environment variables (shell)
```bash
export Server__PublicUrl=http://95.59.233.227:8080
export Server__SkinDomains='["95.59.233.227","localhost"]'
```

---

## Management

```bash
# Start
docker compose up -d

# Stop
docker compose down

# Logs (real-time)
docker compose logs -f

# Restart
docker compose restart

# Rebuild (after code update)
docker compose up -d --build
```

---

## Connect Minecraft Server

1. Download [authlib-injector.jar](https://github.com/yushijinhun/authlib-injector/releases)
2. Place it next to `server.jar`
3. Start server with:

```bash
java -javaagent:authlib-injector.jar=http://YOUR_IP:8080/api/yggdrasil -jar server.jar nogui
```

Where `YOUR_IP` is the IP of the machine running VEC Auth Server.

---

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/info` | Server info |
| GET | `/api/status` | Status (online/offline) |
| POST | `/api/auth/register` | Register account |
| POST | `/api/auth/login` | Login |
| POST | `/api/skin/upload` | Upload skin |
| GET | `/api/skin/{username}.png` | Get skin |
| GET | `/api/cape/{username}.png` | Get cape |
| POST | `/api/skin/model` | Change model (slim/classic) |
| POST | `/api/promo/redeem` | Redeem promo code |
| GET | `/api/yggdrasil` | Yggdrasil metadata |

---

## Data Structure

```
data/
├── vec_auth.db          # SQLite database (users, sessions)
├── yggdrasil_key.pem    # RSA key (auto-generated)
├── skins/               # Player skins
│   └── {username}.png
└── capes/               # Player capes
    └── {username}.png
```

All data in `data/` persists across Docker restarts (volume mount).

---

## Troubleshooting

### Server won't start
```bash
# Check logs
docker compose logs

# Check port
netstat -tlnp | grep 8080
```

### Skin not showing in game
1. Make sure `Server__PublicUrl` is correct (not localhost for remote servers)
2. Test: `curl http://YOUR_IP:8080/api/skin/NAME.png`
3. Make sure authlib-injector is started with the correct URL

### Port 8080 is occupied
Change the port in `docker-compose.yml`:
```yaml
ports:
  - "8081:8080"  # Change 8080 to 8081
```
