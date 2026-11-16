# CLAUDE.md: basis-ops-copilot

Instructions for AI coding assistants (Claude Code) working in this repository.

This is a portfolio project: SAP Basis operations automation with .NET 10, Aspire, n8n and a local LLM (Ollama).
The roadmap is in `PLAN.md`. At the start of every session, read `PLAN.md` and check with `git log` which step we are on.

## Way of working
- **One step at a time** (Step N in `PLAN.md`). Before starting, write a short plan of 5–10 items, then implement it.
- At the end of every step: run `dotnet build` and `dotnet test`, fix what fails, then propose a commit message.
  The user reviews and makes the commit and the push.
- Mark finished items in `PLAN.md` as `[x]`.
- Do not guess package or API versions; check with `dotnet package search` or the official documentation.

## Architecture rules
- Dependencies point inward: Domain ← Application ← Infrastructure ← Api / Worker. Domain depends on no NuGet package.
- Do **not** use MediatR (commercial license). We have our own `ICommandHandler<TCommand,TResult>` / `IQueryHandler<TQuery,TResult>`.
- SAP field names (BNAME, TRKORR, JOBNAME…) live only in FakeSap and in the DTOs under `Infrastructure/Sap`.
  The domain uses its own names (anti-corruption layer).
- No `new HttpClient()`: typed clients with `AddStandardResilienceHandler`.
- Background services get their DbContext inside a scope from `IServiceScopeFactory`.
- Async methods take and pass on a `CancellationToken`. No `.Result` / `.Wait()`.
- Errors are returned as ProblemDetails. Invalid state transition → 409, validation → 400.
- Use `TimeProvider` for time (testability).
- Actions are never applied without approval. On production, the requester cannot approve their own request.
- User names, hosts and IPs are masked in text sent to the LLM. If the LLM fails, the system continues without AI.

## Environment
- macOS (Apple Silicon) with Docker.
- **n8n and Ollama already exist outside this repo** and are not installed by it: n8n at `http://localhost:5678`
  (in Docker), Ollama native at `http://localhost:11434`, model `qwen3:8b`.
- The n8n container reaches the Api at `http://host.docker.internal:5081`, so the Api port is fixed to **5081**.
- FakeSap port: 5090.

## Security
- Never put secrets in the repository. Local settings go into `dotnet user-secrets` or a gitignored `.env`.
- Never print API keys, tokens or passwords in the conversation; the user enters them.
- Leave `git push`, creating GitHub repositories and any write to an external service to the user.

## Code style
- Latest C# features are welcome (primary constructors, collection expressions, records).
- **Everything in the repository is English**: code, identifiers, comments, log messages, error and ProblemDetails texts,
  API responses, configuration keys, test data, documentation (including this file and `PLAN.md`) and commit messages.
  The LLM summary language is a setting (`Ai:SummaryLanguage`, English by default); prompts are always English.
- Keep comments short; where there is an SAP equivalent, name the transaction code (`// SM37`).
- Test names: `Method_Condition_Expected`.
- Commit messages: short, imperative, no Conventional Commits prefix, no co-author trailer.
