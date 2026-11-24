# Netora V2 — Approved UI design contract

**Approved baseline:** the 18 HTML pages in `approved-mockups/`, especially `index.html` (Customers List). This replaces V1's statement that visual assets and tokens were unapproved.

**Source files**
- `approved-mockups/css/netora.css` — primary CSS source of truth, including exact existing tokens, typography, font smoothing, layout, tables and responsiveness.
- `approved-mockups/index.html` — approved Customers List shell and SVG sprite.
- `approved-mockups/assets/netora-logo.svg`, `approved-mockups/assets/sidebar-waves.svg` — retain these assets.
- `approved-mockups/js/` — demo interaction reference, not production business logic.
- `MOCKUP_TO_MVC_MAPPING.md` — all 18 page mappings.

**Required appearance:** keep approved navy sidebar, blue-and-white surfaces, compact cards/tables, spacing, icons, page hierarchy, responsive layout, and persistent light/dark theme. Keep **Inter** body and **Plus Jakarta Sans** headings; preserve the approved lighter weights and CSS font smoothing. Do not arbitrarily strengthen fonts or introduce a new component library/theme.

**Implementation:** build shared Razor `_Layout`, sidebar/header/notification partials, reusable status pills, cards, buttons, tables and form inputs. Use local, versioned assets and proper accessibility. Translate mockup HTML faithfully, then replace sample data with API-backed ViewModels.

**Conflict policy:** written security/business requirements override any fake data or simulated UI behavior. A visual change needed for a real feature must be minimal and documented; do not silently redesign.

**Visual QA:** compare screenshots against each source HTML at representative desktop/mobile sizes, light/dark; review font weights, spacing, icons, navigation and action states. No claim of pixel-perfect equality without comparison.
