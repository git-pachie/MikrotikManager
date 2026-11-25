# Netora MikroTik Manager: development handoff

Written 2026-11-25 at commit `83f6d89` on `main`, the same as `origin/main` on GitHub (`git-pachie/MikrotikManager`). Read this first in a new conversation, then `README.md` for setup and `Netora-MikroTik-Manager-V2-Prompts/IMPLEMENTATION_STATUS.md` for per-page evidence.

**Verified** below means the item was built and then checked by build, automated tests or live checks against the dev database. **Planned** means it exists only in the spec or as a disabled placeholder.

## 1. Project overview

Netora is an ISP management web application. Staff and Managers use it to manage customers, their PPPoE accounts on a MikroTik router, NAP boxes, plans and ONUs, with Manager approval for sensitive changes.

Goals from the spec (`Netora-MikroTik-Manager-V2-Prompts/`):
- Implement the 18 approved HTML mockups as ASP.NET Core MVC Razor pages, without redesigning them.
- Keep business rules and authorization in the backend.
- Require Manager approval for PPPoE changes, and keep the approval decision separate from the router outcome.
- Never connect to a production router during development.

## 2. Requirements and agreed specifications

### Source documents

All are in `Netora-MikroTik-Manager-V2-Prompts/`:
- `AGENTS.md`: the contract. Mockups are the UI contract, backend rules are authoritative, mockup data is fictional, and unspecified details must be asked, not invented. It also says to update `IMPLEMENTATION_STATUS.md` with evidence.
- `REQUIREMENTS.md`, `DESIGN_SYSTEM.md`, `MOCKUP_TO_MVC_MAPPING.md`.
- Module prompts `01`–`18`, and `17-ui-page-prompts.md` for each page.
- `approved-mockups/`: the 18 HTML pages, `css/netora.css` and the SVG assets.

### Business rules from the spec, all enforced in the API

- Manager-only customers are invisible to Staff, including in counts, search, filter options, approvals and details. Staff get a 404, the same as for a missing record.
- PPPoE changes need Manager approval.
- Termination keeps all data. Nothing is deleted (03.04).
- Approval status and provisioning status are stored separately (08.04). Retries must be idempotent (module 09).
- Import is on demand with an UNKNOWN NAP. Mismatch checks run on demand only (module 04 and 10, not built).

### Decisions made by the user in conversation

| Date | Decision |
|---|---|
| 2026-11-24 | Customer fields are those on the approved form (`customer-form.html`). The list is provisional until a full schema is given |
| 2026-11-24 | Staff and Managers can add customers. Only Managers can set "Managers only" visibility |
| 2026-11-24 | New customers start as **Pending Approval**. Only a Manager approves |
| 2026-11-24 | Attachments: JPG, PNG, PDF, max 10 MB. Staff and Managers upload and delete (delete hides, keeps the file) |
| 2026-11-24 | Terminated customers are view-only for Staff. Managers may edit details and attachments |
| 2026-11-25 | PPPoE: no router yet, so database only. The password is stored encrypted and sent to MikroTik after approval. An Active account must be disabled first, and disabling needs Manager approval |
| 2026-11-25 | Secrets go in dotnet user-secrets or `appsettings.Secrets.json`. **Never environment variables**, because the user may not have permission to set them on the server |
| 2026-11-25 | The SMTP user name and password are secrets. Email has an `Enabled` on/off switch and a `UseSsl` setting. Gmail is the provider (`smtp.gmail.com`) |
| 2026-11-24 | .NET 9. Apps listen on all interfaces over http and https with a plain `dotnet run` |
| Logging spec | Configurable path per app, separate API and WebUI files, no retention logic, typed user name logged on failed login, no log viewer (`18-logging.md`) |

### Assumptions made in module 08, not yet confirmed by the user

1. Creation always waits for a Manager, even when a Manager adds the customer or resubmits the request.
2. A creation request can be approved only after a PPPoE password is set.
3. Change Plan requests are for Active accounts only. Disabled accounts are edited directly.
4. "Deletion" in the spec means Terminate. There is no delete.

### Open specifications (do not invent; ask)

- The full customer field schema.
- The NAP port and capacity model.
- ONU fields and validation.
- Diagnostics details.
- The complete role-permission matrix. Today there are only Manager and Staff, and rules are set per module.
- Dashboard formulas.
- The production log folder path.
- Whether Managers may change a terminated customer's status. It would bypass approval and router sync.

## 3. Architecture, technologies and dependencies

```
Browser → Netora.WebUI (MVC, cookie session) → Netora.WebApi (JWT) → SQL Server
                                                                   → MikroTik RouterOS 7 REST (planned, module 09)
```

- The WebUI never touches SQL Server or the router. It calls the API through `INetoraApiClient` (`src/Netora.WebUI/Services/NetoraApiClient.cs`).
- **Projects:**
  - `Netora.Domain`: entities, with no dependencies.
  - `Netora.Application`: business logic, DTOs, FluentValidation and interfaces.
  - `Netora.Infrastructure`: EF Core, Identity, repositories, queries, email and file storage.
  - `Netora.WebApi`: controllers and setup.
  - `Netora.WebUI`: Razor views.
  - `tests/Netora.Tests`: xUnit tests.
- **Runtime:** .NET 9. `global.json` pins SDK 9.0.100 with `latestFeature`, and the machine has SDK 9.0.321. All projects target `net9.0`.
- **Packages:**
  - EF Core SqlServer, Identity.EntityFrameworkCore, JwtBearer, EF Design, Extensions.Http and EF InMemory (tests) are at 9.0.23.
  - FluentValidation.DependencyInjectionExtensions 12.2.0.
  - MailKit 4.18.2. Version 5.0 is a new major release with breaking changes and was deliberately not adopted.
  - Swashbuckle 6.6.2, and xUnit 2.5.3.
- **Front end:**
  - Bootstrap 5.3.3 CSS and self-hosted Inter and Plus Jakarta Sans fonts, with no CDN.
  - The shared `netora.css` was copied from the mockups.
  - `auth.css` for the login page.
  - `site.js` handles theme, sidebar, `data-autosubmit` and `data-confirm`. `attachments.js` and `details.js` are the other scripts.
- **Ports:** the API uses 7225 (https) and 5132 (http). The WebUI uses 7067 (https) and 5020 (http). The first launch profile `all-interfaces` binds `0.0.0.0`.
- **Database:** SQL Server database `MikrotikManagerDB_Dev`. The server and login are in user-secrets on the dev PC and are never committed. All 8 migrations are applied.

## 4. Implemented features and completed modules

| Module | State | Verified |
|---|---|---|
| 01 Foundation | **Completed** | `ApiResponse<T>` envelope, global exception handler, validation envelope, `/api/v1/health` (anonymous), `/system/api-health` in the WebUI, environment configs. Build 0 warnings |
| 02 Auth and Security | Partial | Identity (EF Core) with Manager and Staff roles, and a first-Manager bootstrap from `Bootstrap:*`. Lockout after 5 failed attempts for 15 minutes, and passwords need 10+ characters. API JWT (HS256, 8 h) is re-checked against the database on every request (active flag, lockout, security stamp; roles read from the database). WebUI cookie is HttpOnly, SameSite=Lax and Secure outside Development, and expires with the token. CSRF is validated on every POST. Fallback policy requires sign-in. `SecurityEvents` table. **Not done:** user administration, password reset (module 13), login rate limiting beyond lockout, OIDC/PKCE for mobile (prepared only). The login page has no mockup and is not yet approved |
| 03 Customers | **Completed** | List with search, status/NAP/plan filters, sorting and paging. Details page with tabs and activity history (`CustomerActivities`, field names only). Add and Edit with a concurrency stamp and locking rules. Attachments with type checked by content, 10 MB limit, image viewer and soft delete. 20 fictional sample customers in Development (`IsSampleData = 1`) |
| 04 PPPoE | Partial (database only) | Accounts list and Edit PPPoE page. Password encrypted with Data Protection and write-only (only "set / not set" is returned). Every change is marked `RouterSyncStatus = NotSent`. **Not done:** anything on the router, Import (layout only, buttons disabled), live session data |
| 08 Approvals | Partial | All request types (Create, Disable, Enable, Change Plan, Terminate), before/after summaries, Manager decision once, one pending request per customer, Pending Approvals page per `approvals.html`, Approval Details page (no mockup), emails (MailKit background queue, `Email:Enabled`, `UseSsl`), and a provisioning status per request. **Not done:** sending to the router and Retry (module 09). The Retry button is shown disabled |
| 13 Settings and Users | **Completed** (router connection test waits for 09) | User Management (add, edit, role, enable/disable, temporary-password reset, last-Manager and self-change guards, sessions end on role change, deactivation and reset), My Profile (name, email, theme, own password), Settings (router details with encrypted password, email on/off and sender stored in `AppSettings`, test email, setup checklist), forced password change in WebUI and API |
| 09 MikroTik | **Completed against the mock** (REST client not yet tried on a real router) | `IRouterClient` (RouterOS REST or mock, `Router:Mode`), provisioning after approval with Retry, live status and mismatch on the customer profile, Test Connection, Import. Code: `src/Netora.Infrastructure/Router/`, `src/Netora.Application/Router/RouterContracts.cs` |
| 15 Testing | Partial | 98 automated tests, all passing (`ApprovalTests` 18 methods, `PppoeTests` 20, `CustomerCommandTests` 15, `CustomerAttachmentTests` 12, `CustomerListSortingTests` 5, `CustomerVisibilityTests` 5; theories expand to 98 cases) |

Not started: 05 Plans, 06 NAP Boxes, 07 ONU, 10 Diagnostics, 11 Dashboard, 12 Audit Trail and Backups, 14 Design QA, 16 Reports and Notifications, 18 Logging. Their sidebar links return 404.

### Pages (WebUI)

`/login`, `/customers`, `/customers/new`, `/customers/{id}`, `/customers/{id}/edit`, `/pppoe`, `/pppoe/{id}`, `/pppoe/import` (layout only), `/approvals`, `/approvals/{id}`, `/access-denied`, `/error`.

### API endpoints (all under `/api/v1`, all require sign-in except `health` and `auth/login`)

| Area | Endpoints |
|---|---|
| Auth | `POST auth/login`, `GET auth/me`, `POST auth/logout` |
| Health | `GET health` |
| Customers | `GET customers` (search, status, nap, plan, sortBy, sortDir, page, pageSize), `GET customers/form-options`, `POST customers`, `GET customers/{id}`, `PUT customers/{id}` |
| Attachments | `POST customers/{id}/attachments`, `GET` and `DELETE customers/{id}/attachments/{attachmentId}` |
| PPPoE | `GET pppoe`, `GET pppoe/{id}`, `PUT pppoe/{id}`, `POST pppoe/{id}/requests` (Operation: Create, Disable, Enable, ChangePlan with TargetPlan, Terminate), `POST pppoe/{id}/requests/{requestId}/decision` (Manager) |
| Approvals | `GET approvals` (status: pending, approved, rejected, all; operation; search; page), `GET approvals/{id}`, `POST approvals/{id}/decision` (Manager) |

## 5. Current progress and pending tasks

- **Nothing uncommitted** except `.kiro/` (the user's handoff instructions) and this file.
- **Pending decisions:** the four module 08 assumptions, the login page approval, terminated-status changes, the AVG fix (section 7), and the production log path.
- **Pending setup for email:** an email address on user accounts (there is no UI until module 13, and `admin` has none). Also `Email:Enabled = true` and `Email:From` in `appsettings.json`, which are currently `false` and empty.

## 6. Important technical decisions and configuration

### Configuration and secrets

See `README.md` for the full table.
- **Secrets:**
  - On the dev PC they are in user-secrets, ID `45b1ddea-c84f-4d23-93b2-a09208cd6926`.
  - On a server they go in `appsettings.Secrets.json` in the API folder, loaded by `src/Netora.WebApi/Infrastructure/SecretsFileSetup.cs` after `appsettings.{Environment}.json`. It is git-ignored and never copied by build or publish. `appsettings.Secrets.example.json` is the template.
  - Secret keys: `ConnectionStrings:DefaultConnection`, `Jwt:SigningKey`, `Bootstrap:ManagerUserName`, `Bootstrap:ManagerPassword`, `Email:Smtp:UserName` and `Email:Smtp:Password`.
  - The dev PC has the Gmail login `Email:Smtp:*` in user-secrets.
- **Required settings:** `Attachments:StoragePath` and `DataProtection:KeysPath` default to `App_Data/...` (git-ignored) and are empty in `appsettings.Production.json`, so the API refuses to start there until they are set. The Data Protection keys must be backed up with the database, or stored PPPoE passwords are lost.
- **Swagger:** `Swagger:Enabled` is `true` in Development and `false` elsewhere.
- **WebUI:** `Api:BaseUrl` points to `https://localhost:7225/` in Development. It is logged at startup as `Web API base URL: ...`.

### Conventions to keep

- **API responses:** every API response uses `ApiResponse<T>` (`Success`, `Data`, `Error{Code, Message, Details}`, `TraceId`). Errors carry stable codes such as `validation_failed`, `not_found`, `already_decided` and `password_required`. Application services return an outcome enum (`PppoeOutcome`, `CreateCustomerOutcome`, …), and controllers map it to HTTP. PPPoE and approval outcomes are mapped once, in `src/Netora.WebApi/Infrastructure/PppoeResultMapper.cs`.
- **Validation:** field rules live in FluentValidation validators in Application. MVC implicit `[Required]` is switched off. WebUI view models carry matching DataAnnotations for quick feedback only.
- **Visibility:** apply the Manager-only filter first in every query, before counts, options and paging (`CustomerQueries`, `PppoeQueries`, `ApprovalQueries`).
- **Concurrency:**
  - Customers have a `ConcurrencyStamp`, rotated on each save; a stale save returns 409.
  - Approval decisions use `Status` as a concurrency token, plus the filtered unique index `IX_ApprovalRequests_OnePendingPerCustomer`.
- **History and logs:**
  - `CustomerActivities` and logs hold IDs and field names only, never customer names, contact details or passwords.
  - Log lines use stable event names (`CustomerCreated`, `ApprovalRequestSubmitted`, `ApprovalRequestDecided`, `ApprovalEmailSent`, `ApprovalEmailFailed`, `ApprovalEmailSkipped`, `SecurityEvent …`).
- **Display IDs:** customers show as `NET-0000` (`Customer.DisplayId`) and requests as `REQ-0000` (`ApprovalRequest.DisplayId`).
- **UI:**
  - Reuse classes from `netora.css`.
  - Any new CSS is appended at the end, under a comment starting `/* Implementation addition`.
  - Features not built yet are shown as disabled buttons with a `title` explaining why. Never show fake success.
  - Messages after a POST use `TempData[CustomersController.ToastKey]` and `ErrorKey`, rendered as `.form-success` and `.form-alert`.
- **Tests:**
  - xUnit, with method names as snake-case sentences.
  - `TestDb` in `tests/Netora.Tests/TestSupport.cs` gives an isolated in-memory database per test, a `Seed()` of four customers, the `Manager`/`Staff` actors, a `Commands(db)` / `PppoeCommands(db)` builder and a `RecordingNotifier`.
  - The in-memory provider has no unique indexes or transactions, so check those against SQL Server.
- **Migrations:** they live in `src/Netora.Infrastructure/Persistence/Migrations`. Data changes go in the migration as SQL, with a working `Down`.

### How live checks were done, and lessons learned

- **Isolated test runs:** run the apps on spare ports with settings as command-line arguments, never as environment variables in a shell the user may reuse. An `Api__BaseUrl` left in a reused terminal once broke sign-in.
  ```
  dotnet run -c Release --no-build --no-launch-profile -- --environment Development --urls http://localhost:5232
  ```
  The WebUI runs the same way on 5220 with `--Api:BaseUrl=http://localhost:5232/`. Building Release avoids locking the Debug DLLs of the user's running instances.
- **Temporary Staff user:** for Staff checks, a temporary `TmpVerifyController` (anonymous, creates `staff_test`) was added, then deleted with its users and test customers before committing. Search for `TmpVerify` before any commit.
- **Form checks must use the real form:** submit it in a browser (headless Chrome fills and clicks), or take the anti-forgery token from that form only. A PowerShell check once reused the top bar's theme-form token and missed three forms that had no token (fixed 2026-11-25). Forms with a hand-written `action="..."` need `@Html.AntiForgeryToken()`; forms using `asp-controller`/`asp-action` get it automatically.
- **Screenshots:** done with headless Chrome over the DevTools protocol from a Node script, at 1440px light and dark and at 390px.
- **SMTP checks:** a local PowerShell fake SMTP server on port 2525 was used to check emails.

## 7. Known issues and unresolved problems

| Issue | Detail |
|---|---|
| **AVG Antivirus blocks Netora's emails on the dev PC** | AVG "Web/Mail Shield" replaces Gmail's certificate with its own, which has no revocation information, so MailKit's revocation check fails with `SslHandshakeException`. A throwaway program with the check off sent a test email through Gmail (`2.0.0 OK`), and the user confirmed it arrived on 2026-11-25. **Fix not chosen:** either an AVG exception for SMTP, or a new setting `Email:Smtp:CheckCertificateRevocation` (default `true`). Servers without AVG are not affected |
| Email setup on the dev PC | `admin` has no email address yet (set it in My Profile). Approval emails are off until switched on in Settings. On this PC, set `Email:Smtp:CheckCertificateRevocation` to `false` in user-secrets or `appsettings.Development.json` because of AVG |
| Router not connected | All approved changes stay `ProvisioningStatus = NotSent` and `RouterSyncStatus = NotSent`. Import, Retry, live session and diagnostics are disabled |
| Plans and NAP are free text | `Customer.PlanName` and `NapBox` are strings. Dropdowns list values already in use. They should become foreign keys with modules 05 and 06 |
| Test data in the dev database | Customers 25–28 are non-sample test records left by earlier checks (`e2e_…`, `ui_…`, `testppeo1`). Each has a pending Create request (REQ-0005…0008) |
| Staff UI checks for module 04 | Covered by automated tests and the module 08 live checks, not by a separate live Staff run |
| `.kiro/HANDOFF.md` | It holds the user's instructions for writing this document, not project state. It is left untouched and uncommitted |
| Dark mode and mobile | Checked by screenshot only for Customers, PPPoE and Approvals. Not checked for the login page. There was no pixel diff against the mockups |

## 8. Modified files and source locations

| Concern | Location |
|---|---|
| Entities | `src/Netora.Domain/Customers/Customer.cs` (also `CustomerActivity`, `RouterSyncStatus`), `Approvals/ApprovalRequest.cs`, `Security/SecurityEvent.cs` |
| Customer logic | `src/Netora.Application/Customers/` (`CustomerCommands`, validators, DTOs, `Attachments/`) |
| PPPoE and approval rules | `src/Netora.Application/Pppoe/PppoeCommands.cs` (submit, decide, apply), `PppoeContracts.cs` (`PppoeStates.Fits`, `ApproveBlocker`, `NewRequest`) |
| Approvals list and details | `src/Netora.Application/Approvals/`, `src/Netora.Infrastructure/Approvals/ApprovalQueries.cs` |
| Email | `src/Netora.Infrastructure/Approvals/ApprovalEmailNotifier.cs` (queue), `ApprovalEmailDispatcher.cs` (sender), `EmailOptions.cs` |
| Persistence | `src/Netora.Infrastructure/Persistence/NetoraDbContext.cs`, `Configurations/`, `Migrations/` |
| Identity and JWT | `src/Netora.Infrastructure/Identity/`, `src/Netora.WebApi/Infrastructure/AuthenticationSetup.cs` |
| DI wiring | `src/Netora.Application/DependencyInjection.cs`, `src/Netora.Infrastructure/DependencyInjection.cs`, `src/Netora.WebApi/Program.cs`, `src/Netora.WebUI/Program.cs` |
| WebUI | `src/Netora.WebUI/Controllers/`, `Models/` (view models and label helpers such as `ApprovalLabels`, `PppoeLabels`), `Views/`, `Navigation/NavItems.cs` |
| Docs | `README.md`, `Netora-MikroTik-Manager-V2-Prompts/IMPLEMENTATION_STATUS.md` |

## 9. Next steps, in priority order

1. **Get the open decisions** in section 5, starting with the AVG fix and the four module 08 assumptions.
2. **09 MikroTik (recommended next module):**
   - Start with a mock router service behind an interface, and never touch a real router without explicit approval.
   - Send approved requests (`ProvisioningStatus = NotSent`) to it.
   - Record `Succeeded` or `Failed`, attempts and the last error, and update `RouterSyncStatus`.
   - Enable the Retry button with idempotent retries.
   - Ask first: is a RouterOS 7 test router available, and how are its credentials stored? Use the secrets file or user-secrets.
3. **05 Internet Plans and 06 NAP Boxes:** turn plan and NAP text into real lists, using `plans.html`, `nap-boxes.html` and `nap-details.html`. The NAP capacity model is an open spec.
4. ~~13 Settings and Users~~: done on 2026-11-25.
5. **18 Logging and 12 Audit Trail:** daily file logs per spec, and an Audit Trail page over `SecurityEvents`, `CustomerActivities` and `ApprovalRequests`.
6. **The rest:** 07 ONU, 10 Diagnostics, 11 Dashboard, 12 Backups, 16 Reports and Notifications, 14 Design QA, and the 15 handoff tests.

Before each commit:
- Run `dotnet build Netora.sln` (0 warnings) and `dotnet test tests/Netora.Tests`.
- Search for leaked secrets and `TmpVerify`.
- Update `IMPLEMENTATION_STATUS.md` with evidence.


_Updated 2026-11-25 after module 13 (116 tests passing; migration `UsersAndSettings` applied)._

_Updated 2026-11-25 after module 09 (133 tests passing; migration `RouterIntegration` applied). Next: 05 Plans and 06 NAP, then 18 Logging and 12 Audit._
