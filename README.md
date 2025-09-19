# DialerOne Management (Hub + Agent + Admin)

This repository contains:
- DialerHub (ASP.NET Core + SignalR): central hub for agents and admin
- DialerAgent (Windows service/worker): runs on each DialerOne PC, heartbeats to the hub and executes commands (Enable/Disable/Restart/Update SIP)
- DialerAdmin (WinForms): lists agents and sends commands
- Dockerfile + DigitalOcean App Platform spec for deploying the Hub with a custom domain

Deploy Hub on DigitalOcean App Platform
- Now auto-detected at repo root via the top-level Dockerfile (listens on port 8080)
  - Alternatively, you can point the source directory to `DialerHub/` (subfolder Dockerfile also exists)
- Environment variables:
  - AdminApiKey = your-strong-admin-key (used by Admin app as X-Admin-Key)
  - Agent__Token = your-agent-token (checked when agents Register)
  - ASPNETCORE_URLS = http://0.0.0.0:8080
- Add custom domain to the app (e.g., https://hub.yourdomain.com)
- Health check: GET /healthz → { "status": "ok" }

Scale-out and persistence (MariaDB)
- The hub now uses MariaDB for presence (Agents table) and a command queue (Commands table). No Redis is required.
- Set MariaDb:ConnectionString (env var MariaDb__ConnectionString) to your MariaDB DSN.
- On startup the hub will auto-create the required tables if they don't exist.
- Admin commands are enqueued to DB; each hub instance runs a background dispatcher that claims and delivers commands to the correct agent.

Configure clients
- Agent: set Agent:HubUrl to the hub domain (e.g., https://hub.yourdomain.com) and Token to match Agent__Token
- Admin: set Hub to the hub domain and API Key to AdminApiKey

Local dev quickstart
```
# Hub
dotnet run --project DialerHub -c Release

# Agent (edit appsettings first to point to your local hub)
dotnet run --project DialerAgent -c Release

# Admin app
bin/Release/net8.0-windows/DialerAdmin.exe
```
