# Netora V2 implementation tracking

All 18 HTML designs approved as visual references; **shared MVC shell started (see Foundation progress); no page is complete**.

| Page | Mockup approved | Razor view | API integrated | Permissions tested | Visual QA | Status |
|---|---|---|---|---|---|---|
| Dashboard | Yes | No | No | No | No | Not started |
| Customers List | Yes | Yes (read-only) | Yes (`GET /api/v1/customers`) | Yes (Manager 20 / Staff 18 rows and counts; search hides restricted) | Screenshot 1440px light, by eye | Partial: provisional schema, 20 sample rows (Development only, `IsSampleData = 1`, 2 Manager-only). Search, status/NAP/plan filters, server-side paging work. Not done: sorting, Add/Edit/Import/row actions (shown disabled), details link, dark/mobile check. |
| Customer Details | Yes | No | No | No | No | Not started |
| Add/Edit Customer | Yes | Add only (`/customers/new`) | Yes (`POST /api/v1/customers`, `GET /api/v1/customers/form-options`) | Yes (Staff and Manager can add; only Manager can set Manager-only, Staff attempt returns 403; Staff cannot see Manager-only rows created by a Manager) | Screenshot 1440px light, by eye | Partial (2026-11-24). Decisions from user: fields = approved form; Staff and Managers can add; new customers start as Pending Approval and only a Manager can approve. Tested: field validation (WebUI and API), duplicate PPPoE username (case-insensitive) returns 409 and a field error, CSRF, success message on list, `Pending Approval` status filter and pill. Migration `CustomerFormFields` adds Email, Notes, OnuModel, CreatedByUserId. Not done: Edit, the approval action itself (module 08), router provisioning (module 09), audit trail entry (module 12; a structured log line with IDs only is written), Plan and NAP dropdowns use values already in use (provisional until modules 05 and 06), no client-side validation script, dark/mobile unchecked. Deviation from mockup: button reads "Submit for Approval" instead of "Preview Approval Request" because it now saves. |
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
| Clean Architecture solution (.NET 9) | Done | `Netora.sln`; Domain, Application, Infrastructure, WebApi, WebUI, Tests build with 0 warnings and 0 errors. |
| Shared Razor shell (`_Layout`, sidebar, topbar, icon sprite) | Done for light mode, desktop 1440px | Headless Chrome screenshot of `/customers` compared by eye with `approved-mockups/index.html`: sidebar, logo, wave, icons, fonts, topbar and heading match. No pixel diff run. Dark mode and mobile not yet checked. |
| Self-hosted fonts and Bootstrap | Done | Inter and Plus Jakarta Sans (latin, latin-ext) and Bootstrap 5.3.3 CSS served from `wwwroot`; no CDN requests. |
| Known differences | Documented | Topbar shows "Not signed in" until authentication exists. Active nav is set server-side (mockup `index.html` did not highlight it). Sidebar links other than `/customers` return 404 until those pages exist. |
| EF Core + SQL Server | Done, verified 2026-11-24 | Live check: user-secret set for `Netora.WebApi` (database `MikrotikManagerDB_Dev` on the dev SQL Server); `InitialBaseline` applied (creates only `__EFMigrationsHistory`); `/api/v1/health` and WebUI `/system/api-health` both returned `Healthy`, `databaseReachable: true`. Earlier notes: `NetoraDbContext`, `AddInfrastructure`, baseline migration `InitialBaseline` (empty model, not applied). Connection string key `ConnectionStrings:DefaultConnection`; the app fails fast with a clear message if missing. The user-secrets store for `Netora.WebApi` was empty on the machine used on 2026-11-24, so the real login was not tested. Against an unreachable dummy server `/api/v1/health` correctly returned 503 `Degraded`. |
| DI, async, validation, error handling, response envelope | Done | `AddApplication`/`AddInfrastructure`; FluentValidation registered; `ApiResponse<T>` envelope; `GlobalExceptionHandler` (500, no internals); model-validation 400 in the envelope; JSON console logs outside Development. Unhandled-exception and validation paths have no endpoint yet, so they were not exercised at runtime. |
| WebUI → API client | Done | `INetoraApiClient` (typed `HttpClient`, `Api:BaseUrl`) and `GET /system/api-health` on the WebUI. Verified over http: WebUI :5020 → API :5132 returned the API health envelope. Over https the WebUI rejects the API's dev certificate until `dotnet dev-certs https --trust` is run on the machine. |
| Template cleanup | Done | `WeatherForecast` files removed. |
| Environment config | Done | `appsettings.Production.json` placeholders (empty connection string / `Api:BaseUrl`, no secrets) for both apps; `Api:BaseUrl` set in WebUI Development settings. |
| .NET 9 upgrade | Done (2026-11-24) | All projects target net9.0; `global.json` pins SDK 9.0.100 with `latestFeature`; EF Core, EF Design, Extensions.Http and Configuration.Abstractions pinned to 9.0.23. Build 0 warnings / 0 errors; API and WebUI ran on http and https without any roll-forward workaround and health returned `Healthy`. The applied `InitialBaseline` migration is unchanged. |
| 02 Auth and Security | Partial (2026-11-24) | **Done and tested** (API on :5232 and WebUI on :5220, Release build, dev DB): Identity (EF Core, `AspNetUsers`/`AspNetRoles`, migration `AddIdentityAndSecurityEvents` applied); Manager and Staff roles seeded; first Manager bootstrap from `Bootstrap:*` settings; lockout after 5 failed attempts for 15 min (423 `locked_out`, correct password refused while locked); password minimum length 10; API JWT (HS256, 8 h) issued by `POST /api/v1/auth/login`, every request re-checks the account in the database (deactivation, lockout, security stamp, and roles read from the database, not the token); 401/403 in the standard envelope; `ManagerOnly` policy (Staff got 403, Manager 200); fallback policy requires sign-in on API and WebUI; tampered token rejected; WebUI cookie sign-in (HttpOnly, SameSite=Lax, Secure except in Development, expires with the API token, no sliding), CSRF on all POSTs (missing token returned 400), open-redirect-safe `returnUrl`, sign-out, topbar shows display name and role; `SecurityEvents` table records LoginSucceeded/LoginFailed (typed user name, reason class, IP), LockoutTriggered, Logout, ManagerBootstrapped. Login page screenshot (1440px, light) reviewed by eye. **Not done:** user administration and password reset (planned for module 13, no SMTP assumed); OAuth2/OIDC with PKCE is only prepared (JWT bearer API boundary), no mobile flow; login page has no approved mockup, so it is a minimal page in the shared style (needs your approval); dark mode and mobile for the login page unchecked; no login rate limit beyond account lockout; client IP shown in events is the WebUI server's address; no automated tests. |
| Mock MikroTik service | Not started | |
| Logging module | Spec updated with decisions | `18-logging.md`. Not implemented. Decided: configurable path per app, separate API/WebUI files, no retention logic, typed user name logged on failed login, no viewer. Open: production folder path; time zone assumed server local time. |
