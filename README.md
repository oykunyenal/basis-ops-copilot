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
dotnet test
dotnet run --project src/BasisOps.AppHost   # opens the Aspire dashboard
```
