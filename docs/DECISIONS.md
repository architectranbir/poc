# Initial implementation decisions

- Keep .NET 10 and React/TypeScript. Use Microsoft Agent Framework with a verified, pinned Foundry SDK package.
- The Foundry setting is FOUNDRY_PROJECT_ENDPOINT, not an Azure OpenAI resource endpoint. FOUNDRY_MODEL is a deployed model name.
- Source arithmetic and adjustment replay are independent of AI/provider/persistence packages.
- Round display/policy money using decimal, two places, midpoint away from zero. This is a labeled PoC policy pending Finance validation.
- The initial financial hash is culture-independent, but is NOT the complete commercial/approval hash.
- This branch is a local-only engineering baseline. It has no authenticated shared deployment yet.
- Durable Functions, Service Bus, transactional database, all-rule coverage and live model tests are later milestones.
- A-01 policy ambiguity remains open. The inherited proposed-minus-WIP trigger is clearly labeled in the UI and not approved business policy.
- Documentation tracks technical behavior without embedding the original client requirement files in this public repository.

