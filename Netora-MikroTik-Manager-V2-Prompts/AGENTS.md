# Shared Kiro + Codex instructions — Netora V2

Read `README.md`, `REQUIREMENTS.md`, `DESIGN_SYSTEM.md`, `MOCKUP_TO_MVC_MAPPING.md`, `IMPLEMENTATION_STATUS.md`, the applicable module prompt, and the corresponding `approved-mockups/*.html` **before coding**.

The user approved all 18 static HTML mockups. Treat the bundled CSS, SVG, fonts, spacing and layouts as the UI contract. Convert them to ASP.NET Core MVC Razor views and reusable partials. Do not generate an independent visual design, replace assets, or introduce arbitrary new font weights.

Architecture: Browser → ASP.NET Core MVC WebUI → authenticated ASP.NET Core Web API → SQL Server and MikroTik RouterOS 7 HTTPS REST. No direct WebUI database/router access. Use Clean Architecture and ASP.NET Core Identity.

Backend business rules and authorization are authoritative: Manager-only customers must be invisible to Staff even in counts and exports; PPPoE changes require Manager approval; termination disables PPPoE secret without deleting history; import is on demand with UNKNOWN NAP; mismatch checks on demand only; preserve approved-versus-provisioned states; retries idempotent.

Mockup data/actions are fictional. Replace them with real validated ViewModels, authorized API endpoints, error/empty/loading states and functional flows. Do not present fake success, real-time data, or unimplemented buttons as operational.

Keep changes incremental, test and screenshot each page, update `IMPLEMENTATION_STATUS.md` with evidence and remaining issues. If a detail is not specified, document it and ask instead of inventing user-approved requirements.
