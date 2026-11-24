# Netora MikroTik Manager V2 — Mockup-Aligned Development Prompt Pack

This package **updates the original 50 V1 prompts**, preserves all 15 original modules, adds Reports/Notifications, and adds **18 page-specific Razor implementation prompts**.

## Included
- `approved-mockups/` — exact approved 18-page static HTML source, CSS, JS and SVG assets.
- `AGENTS.md` — shared Kiro and Codex implementation contract.
- `.kiro/steering/netora.md` — matching Kiro steering.
- `REQUIREMENTS.md` — preserved architecture/business requirements and updated UI status.
- `DESIGN_SYSTEM.md` — approved UI styling and asset rules.
- `MOCKUP_TO_MVC_MAPPING.md` — mapping for every HTML page to suggested MVC controller/views.
- `01-...md` through `15-...md` — original 50 prompts with relevant V2 addenda.
- `16-additional-pages.md` — reports and notifications.
- `17-ui-page-prompts.md` — 18 targeted UI conversion prompts.
- `18-logging.md` — daily text-file logging module (login, client create/update/delete, errors).
- `IMPLEMENTATION_STATUS.md` — page-by-page acceptance tracker.

## How to use with Kiro / Codex
1. Extract the ZIP into the project root (or copy these docs and `approved-mockups/` to the root).
2. Ask the coding agent to read `AGENTS.md`, `REQUIREMENTS.md`, `DESIGN_SYSTEM.md`, and `MOCKUP_TO_MVC_MAPPING.md`.
3. Implement foundation, auth and API architecture from the numbered module prompts before connecting real operations.
4. Convert each approved HTML page using `17-ui-page-prompts.md`; retain shared CSS/assets.
5. Require security tests, API tests, visual comparisons, and a status update after each module.

## Key constraints
Visual approval applies to appearance, not fictional data or stubbed interactions. Do not use the static HTML as a backend specification. No router credentials or production secrets in source control. Do not call demo actions completed features.

## Recommended first instruction to agent
> Read AGENTS.md, REQUIREMENTS.md, DESIGN_SYSTEM.md, MOCKUP_TO_MVC_MAPPING.md and approved-mockups/index.html. Build the Clean Architecture solution and shared MVC layout using the approved design without redesigning it. Do not connect to a production router yet. Report completed files, tests and any unresolved specifications.
