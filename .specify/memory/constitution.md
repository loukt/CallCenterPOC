# CallCenterPOC Constitution

## Core Principles

### I. POC-First Simplicity
Prefer the simplest implementation that demonstrates the user scenario end-to-end.

- Avoid “platform” abstractions and over-engineering.
- If a requirement is ambiguous, choose the smallest reasonable interpretation and document assumptions.

### II. Azure-First (When Cloud Services Are Needed)
When the feature requires telephony or AI, prioritize the Azure services already used in this repository.

- Telephony + call events/media streaming: Azure Communication Services (Call Automation).
- Real-time voice AI: Azure OpenAI.

### III. No Secrets in Git
Credentials, connection strings, keys, and phone numbers must not be committed.

- Use `appsettings.json` for non-secret configuration.
- Use User Secrets or environment variables for secrets.
- Redact logs that could contain secrets.

### IV. Observable-by-Default
Any demo failure must be diagnosable from logs without attaching a debugger.

- Log call lifecycle transitions and correlation IDs.
- Log external service failures with enough context to remediate.

### V. Tests Where They Pay Off
This is a POC, but critical flows should have at least lightweight automated coverage.

- Prefer unit tests for request validation and decision logic.
- Add contract-level tests for HTTP endpoints when feasible.

## Security & Compliance Constraints

- Treat phone numbers as sensitive personal data.
- Do not persist recordings/transcripts unless explicitly enabled for the demo.
- If recordings are enabled, store them behind authenticated access and document retention expectations.

## Development Workflow & Quality Gates

- Any change that modifies a public endpoint must update the OpenAPI contract in the relevant `specs/*/contracts/` folder.
- Any change that adds new configuration keys must update the feature `quickstart.md`.
- Keep PRs scoped to one feature spec/plan at a time.

## Governance

- This constitution applies to all `specs/*` deliverables and code changes.
- Amendments require updating this file and referencing the change in the relevant feature plan.

**Version**: 1.0.0 | **Ratified**: 2026-02-20 | **Last Amended**: 2026-02-20
