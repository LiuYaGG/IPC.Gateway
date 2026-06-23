# Docker Deployment

Use Docker when the gateway should be packaged with a repeatable runtime. The compose file includes PostgreSQL for local or single-host deployments.

## Build And Run

```bash
docker compose -f deployment/docker/docker-compose.yml up -d --build
```

Open `http://localhost:5184` after the WebHost is ready.

## Production Notes

- Change `Gateway__Auth__Secret`, `Gateway__Auth__BootstrapAdminPassword`, and PostgreSQL passwords before production.
- Mount `/app/Data` to persistent storage.
- Keep PostgreSQL on a managed service or a separately backed-up volume for production sites.
- Use `GET /health/ready` for load balancer readiness.
- Use a reverse proxy or platform ingress for TLS termination.

## Logs And Operations

```bash
docker compose -f deployment/docker/docker-compose.yml logs -f ipc-gateway
docker compose -f deployment/docker/docker-compose.yml restart ipc-gateway
docker compose -f deployment/docker/docker-compose.yml down
```
