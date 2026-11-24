# Netora V2 implementation tracking

All 18 HTML designs approved as visual references; **shared MVC shell started (see Foundation progress); no page is complete**.

| Page | Mockup approved | Razor view | API integrated | Permissions tested | Visual QA | Status |
|---|---|---|---|---|---|---|
| Dashboard | Yes | No | No | No | No | Not started |
| Customers List | Yes | No | No | No | No | Not started |
| Customer Details | Yes | No | No | No | No | Not started |
| Add/Edit Customer | Yes | No | No | No | No | Not started |
| Pending Approvals | Yes | No | No | No | No | Not started |
| PPPoE Management | Yes | No | No | No | No | Not started |
| Import PPPoE | Yes | No | No | No | No | Not started |
| NAP Boxes | Yes | No | No | No | No | Not started |
| NAP Details | Yes | No | No | No | No | Not started |
| Internet Plans | Yes | No | No | No | No | Not started |
| ONU History | Yes | No | No | No | No | Not started |
| Diagnostics | Yes | No | No | No | No | Not started |
| Audit Trail | Yes | No | No | No | No | Not started |
| User Management | Yes | No | No | No | No | Not started |
| Settings | Yes | No | No | No | No | Not started |
| Reports | Yes | No | No | No | No | Not started |
| Backups | Yes | No | No | No | No | Not started |
| Notifications | Yes | No | No | No | No | Not started |

## Foundation progress (2026-11-24)
| Item | Status | Evidence / remaining issues |
|---|---|---|
| Clean Architecture solution (.NET 8) | Done | `Netora.sln`; Domain, Application, Infrastructure, WebApi, WebUI, Tests build with 0 warnings and 0 errors. |
| Shared Razor shell (`_Layout`, sidebar, topbar, icon sprite) | Done for light mode, desktop 1440px | Headless Chrome screenshot of `/customers` compared by eye with `approved-mockups/index.html`: sidebar, logo, wave, icons, fonts, topbar and heading match. No pixel diff run. Dark mode and mobile not yet checked. |
| Self-hosted fonts and Bootstrap | Done | Inter and Plus Jakarta Sans (latin, latin-ext) and Bootstrap 5.3.3 CSS served from `wwwroot`; no CDN requests. |
| Known differences | Documented | Topbar shows "Not signed in" until authentication exists. Active nav is set server-side (mockup `index.html` did not highlight it). Sidebar links other than `/customers` return 404 until those pages exist. |
| SQL Server connection | Configured, untested | Stored in user-secrets for `Netora.WebApi`; port 1433 reachable. Login not verified until EF Core is added. |
| Identity, login, roles | Not started | |
| Mock MikroTik service | Not started | |
| Logging module | Spec written | `18-logging.md`. Not implemented. Open decisions: log folder, retention, time zone, shared or separate API/WebUI files, unknown-user failed-login rule. |
