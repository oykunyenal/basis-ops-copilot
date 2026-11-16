# Interview notes

Talking points for presenting this project, and answers to the questions it usually raises.

## In one sentence

"I built an approval-based, audited system that automates the Basis team's morning checks and recurring user requests.
A local LLM summarizes the daily status and explains short dumps, but it never decides anything."

## A two-minute demo

1. **Aspire dashboard:** one `dotnet run` starts Postgres, the SAP simulator, the API and the worker. Show a `health-scan` trace.
2. **Morning report in Telegram:** status, counts, the LLM summary and the rule-based list. The list is complete even without the LLM.
3. **Self-service chat:** "User MEHMET is locked on PRD." The assistant checks that the user is locked and opens a request.
4. **Approval:** the on-call admin taps Approve in Telegram; seconds later the user is unlocked.
5. **Audit trail:** `GET /audit?target=PRD:MEHMET`: requested by the chat session, approved by the admin, executed, succeeded.
6. **Chaos:** set FakeSap's failure rate to 100%: retries, backoff, and after five attempts the action is `Failed` with an alarm.

## Talking points

- **Why an approval flow:** "On a production SAP system, neither automation nor AI should change anything on its own.
  The four-eyes principle and the audit log are the first things an auditor asks for."
- **Why a local LLM:** "System data never leaves the machine and there is no cost. User names and hosts are still masked
  before the model sees them, because the model does not need them to summarize a problem."
- **Resilience:** "If SAP is in maintenance or slow: retries with jitter, a circuit breaker and an attempt limit. Beyond the
  limit the action becomes `Failed` and raises an alarm; there is no endless loop."
- **Real SAP:** "The gateway interface is the only place that knows SAP. Replacing FakeSap with the SAP .NET Connector and
  BAPI calls does not touch the domain or the approval flow."
- **Impact (as a scenario, not a measurement):** "If the morning check takes 30–45 minutes by hand, the report is ready at 08:00.
  An unlock request goes from waiting for someone to seconds after approval."

## Likely questions

**What happens if the same request arrives twice?**
`POST /actions` requires an `Idempotency-Key`. The same key with the same content returns the existing action
(`Idempotent-Replayed: true`); the same key with different content is a 409. The n8n tool builds the key from the chat session,
the system and the user, so a retried tool call cannot create a second request.

**Two workers pick up the same approved action?**
They cannot. The executor claims an action with `SELECT … FOR UPDATE SKIP LOCKED` in a short transaction and sets it to
`Executing`; a second worker skips the locked row. An integration test runs four executors in parallel against PostgreSQL.

**What if the worker crashes in the middle of an action?**
The action stays `Executing`. After a two-minute lease another executor treats it as an interrupted attempt and runs it again.
That is safe because the SAP operations are idempotent: unlocking a user that is already unlocked returns a warning, not an error.

**Two people approve at the same moment?**
Optimistic concurrency with PostgreSQL's `xmin`: one wins, the other gets 409. There is a test for it.

**Why not a message broker?**
For this load, the action table is the queue. It is transactional with the state change and the audit entry, and `SKIP LOCKED`
gives safe distribution across workers. If the load grew, I would add a broker with an outbox.

**Why no MediatR?**
It moved to a commercial license, and two small interfaces (`ICommandHandler`, `IQueryHandler`) cover what this project needs.

**How do you stop the LLM from doing something dangerous?**
It has no power. The API never acts on an LLM answer: summaries and dump analyses are text for people. The chat assistant has
no approve tool; the workflow, not the model, sets the action type, the requester and the idempotency key. SAP text is passed
as untrusted data inside a delimited block, and the answers are validated (JSON schema, length limits, transaction code format).

**What if the LLM is down?**
Every endpoint still answers with the rule-based result and `aiAvailable: false`. An integration test points the API at a
closed port to prove it.

**How is masking done?**
Before any text goes to the model, user names, host names, IP and e-mail addresses become `USER_1`, `HOST_1`, `IP_1`, `EMAIL_1`.
Known values are replaced as whole words and case-sensitively, so a user called `CAN` does not turn "canceled" into nonsense.
A test runs the output of every health rule through the prompt builder and asserts that no name, host or IP is left.
The chat assistant is the exception: its job is to handle user names, so it sees them, but it runs on the local model.

**How do you test all this?**
Unit tests for the rules, the state machine, masking and prompts; integration tests with Testcontainers PostgreSQL and
`WebApplicationFactory` for the API flows, concurrency, idempotency and four-eyes; contract tests for the SAP gateway against
the simulator. CI runs them on every push.

**What would change in production?**
Entra ID / OAuth2 instead of the shared API key, with the approver taken from the token and role-based approval rights;
the SAP .NET Connector with a least-privilege technical user instead of FakeSap; Teams or ServiceNow instead of Telegram;
a company-hosted LLM with an evaluation set; deployment manifests and Key Vault instead of the local Aspire run.

## Honest limits

- There is no real SAP system; FakeSap simulates the transactions and their failure modes.
- Telegram approvals use a link that only works on the machine running n8n, and the approver name is a setting,
  not an identity. In production, approvals would come with an authenticated identity.
- The time savings are a scenario, not a measurement.
