# Basis Ops Copilot: Roadmap

A locally running .NET + n8n + local LLM project that automates the work SAP Basis teams do by hand every day.
It is both a portfolio project and a concrete example to walk through in technical interviews.

## 1. Problem and solution

**Problem:** every morning the Basis team runs the same checks by hand, and during the day it handles the same user requests again and again.

| Manual task | Where in SAP | In this project |
|---|---|---|
| Canceled background jobs | SM37 | Automatic scan + finding |
| Short dumps (ABAP runtime errors) | ST22 | Scan + AI-assisted analysis |
| Locked users | SU01 | Scan + self-service unlock (with approval) |
| Transport queue, failed imports | STMS | Scan + approved import |
| Expiring certificates | STRUST | Scan + warning |
| Work process utilization | SM50 / SM66 | Scan + threshold alert |

**Solution:**
1. The worker scans the systems periodically and turns rule violations into findings.
2. Every morning the local LLM summarizes the findings and n8n sends the report to Telegram.
3. A user writes "my user is locked" in a chat; the AI opens a request, a person approves it, the system applies it.
   Every step is written to the audit log.

> **Honesty note:** there is no access to a real SAP system. The `FakeSap` service simulates one.
> The code talks to it through the `ISapGateway` interface. In a real landscape, the **SAP .NET Connector (NCo) with RFC/BAPI**
> (`BAPI_USER_UNLOCK`, `BAPI_XBP_JOB_*`, `TMS_*`) or SAP OData services plug in behind that interface. The README says so openly.

## 2. Architecture

```
                       ┌──────────────── Aspire AppHost (dotnet run) ─────────────────┐
                       │                                                              │
  n8n (existing, :5678)┼─HTTP─► BasisOps.Api (:5081) ──► PostgreSQL (Aspire container)│
   ▲  Schedule/Chat    │            ▲                        ▲                        │
   │  Telegram         │            │ same database          │                        │
   │                   │      BasisOps.Worker ───────────────┘                        │
   │                   │        │ scans + executes approved actions                   │
   │                   │        ▼                                                     │
   │                   │      FakeSap (:5090)  ← simulates SM37/ST22/SU01/STMS/STRUST/SM50│
   │                   └──────────────────────────────────────────────────────────────┘
   └──── Ollama (existing, native, :11434, qwen3) ◄── Api (Microsoft.Extensions.AI)
```

- **n8n and Ollama are not installed by this project**; an existing installation is used.
  - From the n8n container to the Api: `http://host.docker.internal:5081`
  - From the Api to Ollama: `http://localhost:11434`
- Aspire only runs the project's own parts: Postgres, FakeSap, Api, Worker. Logs, traces and metrics show up in the dashboard.

### Folder structure

```
basis-ops-copilot/
├── BasisOps.slnx
├── src/
│   ├── BasisOps.AppHost/            Aspire orchestration
│   ├── BasisOps.ServiceDefaults/    OpenTelemetry, health checks, resilience (Aspire template)
│   ├── BasisOps.Domain/             Entities, rules, state machine. No package dependencies
│   ├── BasisOps.Application/        Use cases (command / query handlers), ports (ISapGateway, IAiAnalyzer)
│   ├── BasisOps.Infrastructure/     EF Core + Npgsql, SAP gateway (HttpClient), Ollama analyzer
│   ├── BasisOps.Api/                Minimal API, /api/v1
│   ├── BasisOps.Worker/             Health scan + action executor (BackgroundService)
│   └── FakeSap/                     SAP simulator with fault injection (chaos settings)
├── tests/
│   ├── BasisOps.UnitTests/          Rules, state machine, masking, prompts
│   ├── BasisOps.IntegrationTests/   Testcontainers (Postgres) + WebApplicationFactory
│   └── FakeSap.Tests/               Gateway contract tests against the simulator
├── n8n/workflows/                   Exported workflow JSON files
├── docs/
│   ├── architecture.md
│   └── interview-notes.md           Talking points and likely questions
└── .github/workflows/ci.yml
```

### Key decisions (the README has a "Why" section)

| Decision | Why |
|---|---|
| Clean / onion layers | The domain is independent of SAP and the database; FakeSap can be swapped for real SAP |
| Commands and queries **without MediatR** | MediatR moved to a commercial license in 2025. Simple `ICommandHandler<T>` / `IQueryHandler<T,R>` interfaces are enough |
| `ISapGateway` = anti-corruption layer | SAP terms (BNAME, JOBNAME, TRKORR) do not leak into the domain |
| Actions = approval-based state machine | On production, neither automation nor AI changes anything on its own; a person approves |
| Action table as queue (`FOR UPDATE SKIP LOCKED`) | Safe work distribution for several workers without a separate message broker |
| Idempotency-Key | If n8n or a client sends the same request twice, only one action is created |
| Local LLM (Ollama) | Customer data does not leave the machine, no cost |
| Masking before the LLM | User names and hosts are masked before they reach the model (`USER_1`, `HOST_1`) |

## 3. Prerequisites (macOS)

```bash
dotnet --list-sdks          # .NET 10 SDK
docker info                 # Docker Desktop / OrbStack running
ollama list                 # qwen3:8b pulled
curl -s localhost:5678 >/dev/null && echo "n8n is up"
```

- Create an empty GitHub repository `basis-ops-copilot` (without a README). The user pushes.

## 4. Steps

Each step is one commit. Check the **"Done when"** items at the end of every step.
Prompt for the assistant: *"Do Step N in PLAN.md. Explain your plan briefly first, then implement it, then run build and test."*

### Step 0: Repository and skeleton
- [x] `git init`, `.gitignore` (dotnet), draft `README.md`, `PLAN.md` and `CLAUDE.md`.
- [x] Solution (`.slnx`) and all projects, with project references following the layer rules:
  - Domain ← Application ← Infrastructure ← Api / Worker.
  - Api and Worker reference ServiceDefaults.
- [x] Aspire AppHost: Postgres (`basisops` database), FakeSap, Api (fixed port **5081**), Worker. Api and Worker wait for Postgres (`WaitFor`).
- [x] `Directory.Build.props`: `net10.0`, `Nullable enable`, `TreatWarningsAsErrors`, `ImplicitUsings`.
- [x] Central package management with `Directory.Packages.props`.

**Done when:** `dotnet build` succeeds. `dotnet run --project src/BasisOps.AppHost` opens the dashboard and every resource is "Running".

### Step 1: FakeSap
- [x] Systems: `PRD` (production), `QAS`, `DEV`. Every request starts with `/sap/{sid}/...`.
  - Extra: `GET /sap/systems` (T000 / CCCATEGORY: P/T/C) and `GET /sap/{sid}/dumps/{dumpId}` (ST22 detail, for the AI analysis).
- [x] Read endpoints (with the SAP equivalent in a comment):
  - `GET /sap/{sid}/jobs?status=canceled` (SM37): JOBNAME, JOBCOUNT, STATUS, STRTDATE, user.
  - `GET /sap/{sid}/dumps?since=` (ST22): runtime error (`TIME_OUT`, `DBSQL_DUPLICATE_KEY_ERROR`, `MESSAGE_TYPE_X`…), program, user, short text.
  - `GET /sap/{sid}/users/locked` (SU01): BNAME, lock reason (wrong password / administrator), time.
  - `GET /sap/{sid}/transports/queue` (STMS): TRKORR, owner, status, return code (0/4/8/12).
  - `GET /sap/{sid}/certificates` (STRUST): PSE name, expiry date.
  - `GET /sap/{sid}/workprocesses` (SM50): type (DIA/BTC/UPD), total / busy.
- [x] Action endpoints (idempotent, BAPIRET2-like answer):
  - `POST /sap/{sid}/users/{bname}/unlock` (like BAPI_USER_UNLOCK).
  - `POST /sap/{sid}/jobs/{jobname}/{jobcount}/restart`.
  - `POST /sap/{sid}/transports/{trkorr}/import`.
- [x] Data is realistic and reproducible from a fixed seed. New dumps and lock events appear over time.
- [x] Chaos settings (`appsettings`): `FailurePercent` (503), `RateLimitPercent` (429 + Retry-After), `LatencyMs`.
- [x] SAP field names stay upper case in JSON (BNAME, TRKORR). Keeping them out of the domain is the ACL's job.

**Done when:** every request in `FakeSap.http` answers. With chaos at 100% the simulator returns 503.

### Step 2: Domain + Application
- [x] Domain:
  - `SapSystem` (Sid, Role: Production / Quality / Development).
  - `HealthScan` (time, system, duration, status).
  - `Finding`: Category (Job, Dump, User, Transport, Certificate, WorkProcess), Severity (Info, Warning, Critical),
    Key (prevents duplicates, e.g. `JOB:PRD:Z_BILLING:1234`), FirstSeen, LastSeen, Resolved.
  - `ActionRequest` state machine: `Requested → Approved → Executing → Succeeded | Failed`, `Requested → Rejected`.
    An invalid transition throws.
    - Extra transition: `Executing → Approved` (transient error, retried at `NextAttemptAt`). Beyond `MaxAttempts` → `Failed`.
  - Fields: Type, Target, RequestedBy, DecidedBy, Attempts, LastError, IdempotencyKey.
  - **Rule:** on production, the requester cannot approve their own request (four-eyes principle).
  - `AuditEntry`: who, what, when, previous → next state. Every transition returns its own audit entry.
  - `FindingReconciler`: merges a new scan with the open findings (open / update / resolve).
    A resolved finding that shows up again opens a new finding.
- [x] Rules (`IHealthRule`, one class each, testable):
  - Canceled job: critical job list (`Z_BILLING*`, `SAP_COLLECTOR*`) → Critical, others → Warning.
    A later successful run of the same job resolves the finding.
  - Dumps: ≥ 5 of the same type in the last hour → Warning, ≥ 20 → Critical.
  - Certificate: < 30 days → Warning, < 7 days → Critical.
  - Transport: RC ≥ 8 → Critical, RC 4 → Info.
  - Work processes: DIA utilization > 80% → Warning, > 95% → Critical.
  - Locked user: Info (not an alarm on its own; listed for self-service).
- [x] Application:
  - Ports: `ISapGateway`, `IAiAnalyzer`, `TimeProvider`. Extra: `ISystemRegistry` (system + role, so four-eyes works without SAP),
    repositories and `IUnitOfWork`.
  - Commands: `RunHealthScan`, `RequestAction`, `ApproveAction`, `RejectAction`.
  - Queries: `GetSystemHealth`, `ListFindings`, `GetAction`, `GetDailyReport`. Extra (for the Api): `ListActions`, `GetAuditLog`.
  - Simple `ICommandHandler<TCommand, TResult>` and `IQueryHandler<TQuery, TResult>`. Validation happens at the handler entry.
  - Handlers return `Result<T>` (Validation → 400, NotFound → 404, Conflict → 409, Forbidden → 403). Four-eyes violation → 403.

**Done when:** rule and state machine tests in the unit test project are green (at least 15 tests).

### Step 3: Infrastructure + Api
- [x] EF Core + Npgsql: `BasisOpsDbContext`, migrations. Postgres connection through Aspire (`AddNpgsqlDbContext`).
- [x] SAP gateway:
  - Typed HttpClient, `AddStandardResilienceHandler` (retry with jitter, circuit breaker, timeout).
  - SAP DTO → domain translation (ACL) happens here.
- [x] Api endpoints (`/api/v1`):
  - `GET /systems`, `GET /systems/{sid}/health`.
  - `GET /findings?sid=&severity=&open=true` (paged).
  - `POST /actions` (`Idempotency-Key` header required; same key + different content → 409) → `202 Accepted` + Location.
  - `POST /actions/{id}/approve`, `POST /actions/{id}/reject` (body: who, why).
  - `GET /actions/{id}`, `GET /actions?status=`.
  - `GET /audit?target=`.
- [x] Error format: ProblemDetails. Validation error → 400. Invalid state transition → 409.
- [x] Identity: a simple `X-Api-Key` for n8n (local). The README notes "Entra ID / OAuth2 in production".
- [x] OpenAPI + Scalar at `/scalar`.

> **Implementation notes:**
> - Systems and their roles come from configuration (`Landscape:Systems`), not from SAP: the four-eyes rule works while SAP is down.
> - Extra endpoint: `POST /systems/{sid}/scans` (scan now, for manual use and n8n).
> - `ActionRequest` uses `xmin` for optimistic concurrency; open findings have a partial unique index `(sid, key) WHERE resolved_at IS NULL`.
>   A concurrency conflict or unique violation → 409.
> - Same Idempotency-Key + same content → same action, `202` + `Idempotent-Replayed: true`.
> - The default resilience handler from ServiceDefaults is removed for the SAP client and replaced by one tuned pipeline
>   (tested: with chaos at 100%, 1 + N retry requests).
> - BAPI `TYPE = W` counts as success (idempotent repeat). 404/409 from SAP → `Rejected`.
> - In Development, migrations run when the Api starts (`Database:MigrateOnStartup`). The Worker waits for the Api.
> - Testcontainers integration tests were brought forward from Step 7 (action flow, idempotency, four-eyes, xmin, partial index, api key).

**Done when:** in `BasisOps.Api.http`, request → approve → get status works. Sending the same Idempotency-Key again returns the same action.

### Step 4: Worker
- [x] `HealthScanWorker`: scans every system with a `PeriodicTimer` (setting `ScanIntervalSeconds`, default 60).
  - Existing findings get a new `LastSeen`, new ones are opened, vanished ones are `Resolved`.
- [x] `ActionExecutorWorker`:
  - Picks `Approved` actions with `FOR UPDATE SKIP LOCKED` and applies them to SAP.
  - On transient errors: `Attempts++` and retry with backoff. Beyond `MaxAttempts` (5) the action is `Failed`, with an alarm in audit and log.
  - While the circuit breaker is open, wait without counting an attempt.
- [x] Each worker uses its DbContext inside a scope (no captive dependency, `IServiceScopeFactory`).
- [x] Log correlation: action id and SID are structured log fields.

> **Implementation notes:**
> - The execution logic lives in the Application layer (`ActionExecutor`); the Worker is only the loop. The action is claimed in a short
>   transaction with `FOR UPDATE SKIP LOCKED` and set to `Executing`; the SAP call happens outside the lock.
> - Lease: an action that stays in `Executing` longer than `ExecutionLease` (2 min, executor crashed) counts as an interrupted attempt
>   and is retried. Safe because the SAP actions are idempotent.
> - While the circuit is open: `ActionRequest.Defer` (no attempt counted). The gateway only reports "circuit open" if no request
>   actually reached SAP; otherwise an action never became `Failed` during a long outage (found in a live chaos 100% test).
> - Backoff: 5 s × 2^(attempt−1), at most 2 min, ±20% jitter. Beyond `MaxAttempts`: `Failed` + `LogCritical` "ALARM".
> - Domain fix: `RecordFailure` / `Defer` are only allowed from `Executing` (otherwise `Requested → Approved` without approval was possible).
> - Tracing: source `BasisOps.Application` (`health-scan`, `execute-action` spans).
> - Live check: chaos 50% → actions end `Succeeded` (HTTP retries absorbed the errors); 100% → `Failed` after 5 attempts + ALARM.
> - Brought forward from Step 7: request → approve → execute → Succeeded, 4 parallel executors apply each action once (SKIP LOCKED), lease recovery.

**Done when:** scan traces show up in the Aspire dashboard. With FakeSap chaos at 50%, approved actions end `Succeeded` after retries; at 100% they end `Failed`.

### Step 5: AI (Ollama + Microsoft.Extensions.AI)
- [x] `IChatClient` registration: `OllamaSharp`, model and address from configuration (`Ai:Endpoint=http://localhost:11434`, `Ai:Model=qwen3:8b`).
- [x] `OllamaAiAnalyzer : IAiAnalyzer`:
  - `AnalyzeDump(dump)`: probable cause, impact, first steps for the Basis team, related transaction codes. **JSON schema output** parsed into a record.
  - `SummarizeDaily(findings)`: 5–8 line management summary in priority order, in the configured language.
- [x] **Masking** before the model: user names, hosts and IPs → `USER_1`, `HOST_1`. Not translated back in the answer.
- [x] Prompt injection: dump text is marked as data, never as instructions (stated in the system prompt).
- [x] **Fallback:** if Ollama is down or times out, the report is returned without AI (rule-based list), no exception.
- [x] Api: `POST /systems/{sid}/dumps/{dumpId}/analyze`, `GET /reports/daily?sid=PRD`.

> **Implementation notes:**
> - Packages: `OllamaSharp` 5.5.2 + `Microsoft.Extensions.AI` 10.11.0 (11.0 pulls the runtime packages to 11, so we stay on 10.x).
> - The ServiceDefaults resilience handler is removed from the Ollama HttpClient (its 10 s attempt timeout would cut every generation).
>   One attempt, `Ai:Timeout` 2 min (the first call loads the model).
> - `think: false` (qwen3) and the JSON schema `format` are verified by a test on the request body sent to Ollama.
> - Masking: known values are case-sensitive and whole-word (user `CAN` does not break "canceled"); "user/owner X" and "server X"
>   patterns; IP, FQDN, e-mail. The finding `Key` (contains user names) is not sent to the model. A test feeds the output of every rule
>   through the prompt and asserts that nothing leaks.
> - The summary prompt includes the rule-based overall status: in a live run the model called a system with critical findings "healthy".
> - Endpoint change: dump ids are unique only within one system (ST22), hence `POST /systems/{sid}/dumps/{dumpId}/analyze`;
>   plus `GET /systems/{sid}/dumps?hours=24`. Without Ollama the dump analysis returns `aiAvailable: false` too.
> - `Ai:Enabled` false (Worker, tests) → `DisabledAiAnalyzer`. Integration tests never call Ollama; the fallback test points to a closed port.
> - Live: PRD summary ~13 s, dump analysis ~7 s (qwen3:8b on an Apple Silicon Mac).

**Done when:** with Ollama running, the daily report includes a summary. With Ollama stopped, the report still arrives with `aiAvailable: false`.

### Step 6: n8n workflows (in the existing n8n)
- [x] Credential: Header Auth (`X-Api-Key`), base URL `http://host.docker.internal:5081`.
- [x] **Daily report:** Schedule (weekdays 08:00) → `GET /reports/daily?sid=PRD` → Telegram (HTML). On error a "report could not be created" message.
- [x] **Self-service assistant:** Chat Trigger → AI Agent (Ollama `qwen3`) with tools:
  - `list_locked_users` → `GET /systems/{sid}/locked-users`.
  - `request_user_unlock` → `POST /actions` (Idempotency-Key = chat session + system + user).
  - `get_request_status` → `GET /actions/{id}`.
  - System prompt: **the agent never approves**, it only opens requests and reports their status.
- [x] **Approval flow:** new request → Telegram "Send and Wait for Response" (Approve / Reject) → `POST /actions/{id}/approve|reject`.
- [ ] (Optional) Runbook RAG: `docs/runbooks/*.md` → pgvector → the agent consults runbooks for "what should I do about this dump". *(Postponed.)*
- [x] Export the workflows → `n8n/workflows/*.json` (no credentials).

> **Implementation notes:**
> - Dedicated endpoint for locked users: `GET /systems/{sid}/locked-users` (name, reason, time only). The agent never sees the host
>   and certificate details of `/health`.
> - The rules live in the workflow, not only in the prompt: the agent has no approval tool; type (`UnlockUser`), `requestedBy`
>   (`self-service:<session>`) and `Idempotency-Key` are built in the sub-workflow. The tool first checks with `locked-users` that the user
>   is really locked. Only new requests (not replays) start an approval, and the chat does not wait for it.
> - The agent sees user names in clear text (that is its job); the model is local and the data does not leave the machine.
>   Masking applies to the analysis texts the Api sends to the LLM.
> - The Telegram approval buttons open a signed `http://localhost:5678` link: locally, approval only works from the machine running n8n.
>   A phone needs public HTTPS. The approver name is `approverName` from the Settings node (a link click carries no identity).
> - n8n 2.x only publishes a workflow after the sub-workflows it calls are published. Order: Approval → Unlock → Agent → Report.
>   The CLI import (`n8n/import-workflows.sh`) keeps the workflow ids.
> - Verified in a throwaway n8n 2.47.5 container: report (with AI summary and the failure path), unlock (new / replay / invalid / 404),
>   approve and reject, agent with the real qwen3 (request, status, "approve it now" pressure, listing), the timeout branch,
>   a user who is not locked, and the audit trail. A real Telegram send is left to the user.

**Done when:** writing "User MEHMET is locked on PRD" in the chat opens a request, an approval arrives in Telegram,
after approval the user is unlocked within seconds and the audit log shows it.

### Step 7: Tests and CI
- [x] Unit: rules, state machine, masking, idempotency content comparison.
- [x] Integration (Testcontainers PostgreSQL + `WebApplicationFactory`, an `ISapGateway` test double instead of FakeSap):
  - Request → approve → execute → Succeeded.
  - Same Idempotency-Key → one record. Different content → 409.
  - Approving your own request on production → 403.
  - Two executors at the same time never apply the same action twice (`SKIP LOCKED`).
  - Without Ollama the report falls back.
- [x] `.github/workflows/ci.yml`: push / PR → restore, build, test (ubuntu-latest, .NET 10). Test summary.
- [x] CI badge in the README.

> **Implementation notes:**
> - All test scenarios were written in earlier steps (195 tests: unit, integration, FakeSap contract tests). Approving your own request
>   on production returns 403; of two approvers racing, one gets 409; four parallel executors apply each action once.
> - CI: `ubuntu-latest`, .NET version from `global.json`. Release build (warnings are errors), tests, xUnit markdown report → job summary,
>   TRX + markdown → artifact.
> - Actions are pinned to commit SHAs (release in a comment), `permissions: contents: read`, older runs on the same branch are canceled.
> - Also run locally with `TZ=UTC` and invariant culture (close to the CI environment).
> - The badge is only visible to others once the repository is public.

**Done when:** `dotnet test` is green locally and in GitHub Actions.

### Step 8: Showcase (the most important part for the portfolio)
- [x] README:
  - One-sentence purpose, problem → solution table, architecture diagram (Mermaid).
  - "Run it in 5 minutes", screenshots (Aspire dashboard, Telegram report, approval message, n8n flows).
  - "Why these decisions", "Connecting to a real SAP system", "What changes in production".
- [x] `docs/architecture.md`: flow diagrams (scan, action approval, AI).
- [x] `docs/interview-notes.md`: talking points (section 6 below).
- [ ] Screenshots in `docs/images/` and a short demo GIF (chat → approval → user unlocked). *(User: needs the real Telegram setup.)*
- [ ] GitHub repository description + topics: `dotnet`, `aspire`, `sap-basis`, `n8n`, `ollama`, `clean-architecture`. *(User.)*

> **Implementation notes:**
> - The whole repository is English, including `PLAN.md` and `CLAUDE.md`. The default LLM summary language is English
>   (`Ai:SummaryLanguage`); another language is a local setting.

## 5. What changes in production

| Here | In a real landscape |
|---|---|
| FakeSap | SAP NCo (RFC/BAPI) or SAP OData; technical user with least privilege (S_RFC, S_USER_GRP) |
| X-Api-Key | Entra ID / OAuth2, role-based approval rights |
| Action table as queue | RabbitMQ / Service Bus + outbox if the load grows |
| Telegram | Teams / ServiceNow / Jira integration, tickets |
| One local Ollama | Company-hosted LLM or Azure OpenAI, with an evaluation set |
| Aspire locally | Compose / Kubernetes manifests generated by Aspire, Key Vault, deployment pipeline |

## 6. Talking points

- **One sentence:** "I built an approval-based, audited system that automates the Basis team's morning checks and recurring user requests.
  A local LLM summarizes the dumps and the daily status."
- **Why an approval flow:** "On a production SAP system, neither automation nor AI should change anything on its own.
  The four-eyes principle and the audit log are the first things an auditor asks for."
- **Why a local LLM:** "Customer system data never leaves the machine and there is no cost. User names are still masked before the model sees them."
- **Resilience:** "If SAP is in maintenance or slow: retry with jitter, a circuit breaker and an attempt limit. Beyond the limit the action
  becomes Failed and raises an alarm; no endless loop."
- **Real SAP:** "Thanks to the gateway interface, the SAP .NET Connector with BAPI calls replaces FakeSap. The domain does not change."
- **Impact:** "If the morning check takes 30–45 minutes by hand, the report is ready at 08:00. An unlock request goes from minutes to seconds."
  (Present these numbers as a scenario, not as a measurement.)

## 7. Next phase (optional): HR automation

The same building blocks (approved actions, audit, n8n, local LLM) for onboarding and offboarding:
new employee → accounts, equipment, training list, first-day e-mail; on exit, all access is closed with a checklist.
