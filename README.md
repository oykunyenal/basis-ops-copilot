# Basis Ops Copilot

An **approval-based, fully audited** operations assistant that automates the morning checks SAP Basis teams do by hand and the user requests they handle again and again during the day. Runs entirely locally: .NET 10 + Aspire + n8n + a local LLM (Ollama).

> **Status:** Work in progress. See the [roadmap](PLAN.md).

## What it does

| Manual task | SAP transaction | In this project |
|---|---|---|
| Canceled background jobs | SM37 | Automatic scan + finding |
| Short dumps (ABAP runtime errors) | ST22 | Scan + AI-assisted analysis |
| Locked users | SU01 | Scan + self-service unlock (with approval) |
| Transport queue, failed imports | STMS | Scan + approved import |
| Expiring certificates | STRUST | Scan + warning |
| Work process utilization | SM50 / SM66 | Scan + threshold alert |

> There is no access to a real SAP system. The `FakeSap` service simulates one. The code talks to it through the `ISapGateway` interface; in a real landscape, the SAP .NET Connector (RFC/BAPI) or SAP OData services plug in behind that interface.

## Architecture

```
src/
├── BasisOps.AppHost/          Aspire orchestration (Postgres, FakeSap, Api, Worker)
├── BasisOps.ServiceDefaults/  OpenTelemetry, health checks, resilience
├── BasisOps.Domain/           Entities, rules, state machine (no package dependencies)
├── BasisOps.Application/      Use cases, ports
├── BasisOps.Infrastructure/   EF Core, SAP gateway, LLM adapter
├── BasisOps.Api/              Minimal API (:5081)
├── BasisOps.Worker/           Periodic health scan + approved action executor
└── FakeSap/                   SAP simulator (:5090)
tests/
├── BasisOps.UnitTests/
└── BasisOps.IntegrationTests/
```

Dependencies point inward: `Domain ← Application ← Infrastructure ← Api / Worker`.

## Getting started

Prerequisites: .NET 10 SDK, Docker.

```bash
dotnet build
dotnet test                                   # integration tests start a PostgreSQL container
dotnet user-secrets set "Parameters:api-key" "<a long random value>" --project src/BasisOps.AppHost
dotnet run --project src/BasisOps.AppHost     # opens the Aspire dashboard
```

- API: `http://localhost:5081/api/v1`, interactive docs at `http://localhost:5081/scalar` (Development only).
- Every `/api/v1` call needs the `X-Api-Key` header. For the `.http` file, put the key into
  `src/BasisOps.Api/http-client.private.env.json` (gitignored): `{ "local": { "apiKey": "..." } }`.
- Actions are asynchronous: `POST /api/v1/actions` (with an `Idempotency-Key` header) returns `202 Accepted`,
  a second person approves, and the worker executes it.

### Security notes

The shared API key is a local-only shortcut for n8n. In production the API would sit behind
Entra ID / OAuth2, approvers would come from the token instead of the request body, and approval
rights would be role-based.
