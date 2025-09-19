# DialerHub on DigitalOcean App Platform

This folder contains a Dockerfile to deploy the hub to DigitalOcean App Platform with a custom domain.

Quick deploy (dashboard)
- Push this repo to GitHub (or any Git provider).
- In DigitalOcean, create an App -> pick this repo -> select the DialerHub directory -> choose “Dockerfile” build.
- Port: 8080 (App Platform expects the container to listen on 8080).
- Environment variables:
  - AdminApiKey = your-strong-admin-key
  - Agent__Token = your-agent-token
  - ASPNETCORE_URLS = http://0.0.0.0:8080
  - Cors__AllowedOrigins = https://admin.yourdomain.com (comma-separated if multiple)
    - If empty or unset, all browser origins are blocked (desktop Admin is unaffected)
- Enable WebSockets (default is supported).
- Deploy. DO will issue an HTTPS endpoint automatically like https://dialerhub-xyz.ondigitalocean.app.
- Add a custom domain (e.g. hub.yourdomain.com) under App -> Settings -> Domains. Create the required CNAME/ALIAS at your DNS provider.

Deploy with doctl (optional)
- Install doctl and auth: `doctl auth init`
- From the repository root: `doctl apps create --spec do-app.yaml`
- To update: `doctl apps update <app-id> --spec do-app.yaml`

Client configuration
- Agent: set `Agent:HubUrl` to your custom domain, e.g. `https://hub.yourdomain.com`.
  - In appsettings.json or via environment variable `Agent__HubUrl`.
- Admin app: set Hub to your custom domain (same URL).
- The REST header must include `X-Admin-Key` equal to the AdminApiKey you configured.

Notes
- The hub honors `X-Forwarded-Proto` so it knows the original HTTPS scheme behind DO’s proxy.
- Health endpoint is `/healthz` (returns `{ status: "ok" }`).
- CORS is currently restricted via `Cors:AllowedOrigins`. Set it to your Admin UI origin.

Scaling and multi-instance (MariaDB)
- The hub uses MariaDB for presence and a DB-backed command queue, so you can scale to multiple instances without Redis.
- Set the environment variable `MariaDb__ConnectionString` with your DSN, e.g.:
  - `server=db;port=3306;database=dialerhub;user id=app;password=${MARIA_PASSWORD};SslMode=Preferred;`
- Each instance runs a dispatcher that reads claimed commands for agents connected to that instance and sends them to the agent’s SignalR group.
- Use `/whoami` to observe instance IDs behind your load balancer.
