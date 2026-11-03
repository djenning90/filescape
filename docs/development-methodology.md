# Development methodology

## Purpose

Build a useful product through deliberate planning, small working increments, independent review, and real use.

Use enough structure to preserve intent and prevent regressions. Scale the process to the project. Favor progress, and investigate risks in proportion to their consequences.

This method was developed while building Possibility Engine. Other projects can adapt it without reproducing the entire document set.

## 1. Establish product intent

Before choosing implementation details, establish:

- **Charter:** Who the product serves, why it exists, its principles, and what it must avoid.
- **Goals and journeys:** What the user wants to accomplish and how the product helps.
- **Requirements:** Observable behavior, important distinctions, constraints, and quality expectations.
- **MVP:** A substantive, usable version that closes the principal value loop.
- **Success criteria:** What evidence would show that the product is useful.

Give important requirements stable identifiers. Record unresolved questions explicitly.

Preserve nuance. Similar concepts may need separate treatment—for example, a user declining something, postponing it, or expressing uncertainty.

## 2. Develop architecture and implementation plans

Translate the requirements into responsibility boundaries, data ownership, workflows, and contracts.

Record consequential decisions with:

- The problem being solved
- The chosen approach and its rationale
- Alternatives and tradeoffs
- Conditions that would justify revisiting the decision

Distinguish logical components from separately deployed services. Prefer the simplest operational structure that supports the product.

Choose technologies for suitability, maintainability, and the owner’s enjoyment. Add another language, framework, service, or abstraction when its demonstrated benefit justifies its cost.

Keep planning documents consistent. When a decision changes, update affected requirements, architecture, plans, and tests.

## 3. Investigate uncertainty with focused experiments

Use a spike when an important architectural assumption lacks evidence.

State beforehand:

- The question being investigated
- The decision the result will inform
- Representative and difficult cases
- How correctness will be judged
- Limits on time, cost, and external activity
- What the experiment cannot establish

Design experiments to expose limitations. Include negative controls and difficult cases rather than selecting examples likely to pass.

Distinguish a defect in the test instrument from a defect in the implementation under evaluation.

Report results as:

- Demonstrated
- Provisional
- Failed
- Still unknown

Expand an experiment when the decision warrants it. Do not let experimentation indefinitely postpone a usable product.

## 4. Build thin, usable vertical slices

Implement increments that exercise an end-to-end user journey across the necessary layers.

Each slice should:

1. Deliver an observable capability.
2. Include the domain behavior, persistence, interface, and tests it needs.
3. Receive review and correction.
4. Be committed separately.
5. Leave clear instructions for running and trying it.

Prefer a working journey through several components over completing one isolated subsystem in depth.

Demonstrations, fixtures, simulated providers, and live integrations must be clearly distinguished. A polished simulation does not prove that the live product works.

## 5. Use independent review

Have an implementing agent and a reviewing agent when practical.

The implementing agent should explain decisions, provide evidence, report limitations, and correct defects.

The reviewing agent should inspect source code and relevant artifacts, challenge assumptions, and assess whether the tests prove the claimed behavior.

The human owner decides product meaning, desired experience, acceptable tradeoffs, and consequential commitments.

Review findings must remain open to challenge. Reviewers can also be wrong.

## 6. Protect behavior through iteration

Treat regression resistance as a product quality requirement.

Before changing behavior, identify:

- Requirements and journeys affected
- Existing behavior and why it exists
- Invariants that must remain true
- Tests and documents that need adjustment
- Effects on retained data and its meaning

Use several test layers:

- Domain and policy tests that run without a database
- Persistence, transaction, concurrency, and migration tests against the real database
- Behavioral contract tests and representative end-to-end journeys
- Browser tests for important interaction flows

Where useful, annotate tests with their purpose and the requirement they protect.

A green suite is evidence only when the assertions remain meaningful. Deleting a case, weakening an assertion, skipping a test, or narrowing its scope requires an explicit explanation.

Temporary tests that preserve a known defect must be labeled as characterization tests, with conditions for removing them.

## 7. Enforce standards in the repository

Keep the main agent instruction file concise. Link to focused guidance for coding, architecture, testing, documentation, and communication.

Use compiler settings, analyzers, formatting, and architectural checks to enforce rules that tools can verify.

Write strongly typed code. Use explicit contracts at boundaries and make failure states understandable.

Document what each significant type does, why it exists, and terminology needed to understand it. Explain non-obvious decisions close to the code.

Avoid comments that merely repeat names. Write clear, simple technical prose.

## 8. Make limits purposeful and visible

Every limit should have a reason: a real resource constraint, provider restriction, usability concern, or measured operational need.

Avoid arbitrary limits that silently discard information or prevent useful work.

When a limit is reached:

- Preserve recoverable input and completed work.
- Explain what happened.
- Expose incompleteness.
- Provide a useful next action.

Distinguish a test-run budget from normal product policy. Product limits should permit useful behavior.

## 9. Handle data evolution proportionately

Preserve meaningful retained data and its interpretation.

Early in development, resetting disposable fixtures or consolidating migrations may be appropriate. Record that choice explicitly.

Once real user data or valuable experimental evidence exists, use reviewed migrations, recoverable backups, and suitable upgrade tests.

Never silently change the meaning of historical records.

## 10. Dogfood complete workflows

Use the product with realistic material as soon as a meaningful workflow is available.

Test the environments where it will actually be used, including physical mobile devices when relevant.

Observe:

- Whether actions and next steps are understandable
- Whether the product behaves as expected
- Whether work survives refresh, navigation, restart, and device changes
- Whether errors preserve input and explain recovery
- Whether completing a workflow feels clear and satisfying
- Whether the product delivers its intended value

Treat confusion as evidence about the interface, even when the underlying behavior is technically correct.

Fix blockers and important friction as they appear. Collect minor polish without allowing it to consume the entire project.

## 11. Preserve useful autonomy

Give agents substantial, bounded work packages they can complete independently.

Specify:

- The intended outcome
- Requirements to preserve
- Scope and explicit exclusions
- Available authority and resource limits
- Required verification
- What constitutes completion
- Conditions that require human input

Preflight tools, credentials, builds, and local services before unattended work.

Keep approval boundaries concrete. Use human attention for consequential decisions and new authority, while allowing routine implementation and verification to proceed.

## 12. Report honestly and maintain continuity

At handoff, state:

- What changed and why
- What was verified
- What was not verified
- Known limitations
- Commit or artifact identifiers
- How to run and use the result
- The next useful step

Maintain a concise implementation-status document so work can resume after time away or loss of conversational context.

Do not equate test counts with quality or “implemented” with useful. Completion requires evidence appropriate to the claim.
