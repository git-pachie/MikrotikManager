# Netora MikroTik Manager

An ISP management application for customers, PPPoE accounts and Manager approvals, built with ASP.NET Core 9 and Clean Architecture:

Browser → `Netora.WebUI` (MVC) → `Netora.WebApi` → SQL Server and MikroTik RouterOS 7 HTTPS REST.

The WebUI never touches the database or the router. It calls the Web API, which owns all business rules and permissions.

| Project | Role |
|---|---|
| `Netora.Domain` | Entities and domain rules |
| `Netora.Application` | Business logic, DTOs, validation, abstractions |
| `Netora.Infrastructure` | EF Core and SQL Server, Identity, email, file storage |
| `Netora.Logging` | Daily log files, correlation IDs, the activity log used for business events |
| `Netora.WebApi` | HTTP API (7225 https / 5132 http) |
| `Netora.WebUI` | Razor MVC front end (7067 https / 5020 http) |
| `Netora.Tests` | xUnit tests |

Specs, approved mockups and module prompts are in `Netora-MikroTik-Manager-V2-Prompts/`. Detailed progress, decisions and test evidence are in `Netora-MikroTik-Manager-V2-Prompts/IMPLEMENTATION_STATUS.md`.

## What works today

| Area | Pages | Notes |
|---|---|---|
| Sign-in | `/login` | Manager and Staff roles, login rate limits, account lockout, browser cookie session, and per-token API logout revocation |
| Customers | `/customers`, `/customers/{id}`, `/customers/new`, `/customers/{id}/edit`, `/customers/{id}/delete` | Search, filters, sorting, details, add/edit, attachments, and Manager-only customer deletion after PPPoE removal. Manager-only customers are hidden from Staff, including in counts |
| PPPoE | `/pppoe`, `/pppoe/{id}` | Accounts, live session status for Staff and Managers, encrypted passwords, and router import. Managers can disconnect a live session or delete an account |
| Approvals | `/approvals`, `/approvals/{id}` | Create, Disable, Enable, Change Plan and Terminate requests; only Managers decide; approval emails |
| Users and Settings | `/users`, `/settings` (Managers), `/profile` (everyone) | Add, edit, enable, disable, delete, and reset passwords. Router and email settings, profile, theme, and password |
| MikroTik | `/settings`, `/approvals/{id}`, `/customers/{id}`, `/pppoe/import` | Approved router changes with Retry, customer router status and Configuration Mismatch, Test Connection, and Import. Managers select the router mode in Settings |
| Audit Trail | `/audit` (Managers) | Sign-ins, user and settings changes, customer and PPPoE changes, approval steps with router results, plan and NAP changes; filters by date, category, actor and text. Backups are not built yet |
| Internet Plans | `/plans`, `/plans/new`, `/plans/{id}/edit` | Plan catalogue with fee, speeds and the MikroTik PPP profile each plan maps to; Check Router Profiles. Managers add, edit and deactivate; Staff view |
| NAP Boxes | `/nap-boxes`, `/nap-boxes/new`, `/nap-boxes/{id}` | NAP list with customer counts, details with assigned customers, move customers between NAP boxes, UNKNOWN review. Managers add and edit; Staff view and move |
| Logging | none (server files) | Daily text files `netora-api-yyyy-MM-dd.txt` and `netora-web-yyyy-MM-dd.txt`, see [Log files](#log-files) |
| ONU History | `/onu-history` | Current devices and replacement history with date, reason, and operator |
| Diagnostics | `/diagnostics` | On-demand PPPoE session, available RouterOS log events, and configuration mismatch checks |
| Dashboard | `/dashboard` | Role-filtered subscriber totals, pending approvals, recent activity, and live router health |
| Reports | `/reports` | Role-filtered customer status, NAP, internet plan, and provisioning summaries with CSV export |
| Notifications | `/notifications` | Approval decisions, provisioning outcomes, and recorded approval-email delivery status |
| Backups | `/backups` (Managers) | SQL Server history, backup directory, manual runs, and an optional daily schedule |

## Prerequisites

- .NET 9 SDK (`global.json` pins 9.0.100 with `latestFeature`)
- SQL Server reachable from this machine
- EF Core tools for migrations: `dotnet tool install --global dotnet-ef`

## First-time setup

### 1. Secrets (never commit real values)

The Web API's secrets are kept in one of two places. Neither uses environment variables or needs administrator rights:

- **On your development PC: dotnet user-secrets** (commands below).
- **On a server, or anywhere user-secrets are not available: `appsettings.Secrets.json`** in the API folder (see [Secrets file](#secrets-file)).

Never put secrets in the committed `appsettings*.json` files or in this README. The WebUI has no secrets.

Run from the repository root:

```powershell
# SQL Server (SQL login, or use Trusted_Connection=True for Windows authentication)
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=<host>;Database=<db>;User Id=<user>;Password=<password>;TrustServerCertificate=True" --project src/Netora.WebApi

# Token signing key, at least 32 random characters
dotnet user-secrets set "Jwt:SigningKey" "<random string of 32+ characters>" --project src/Netora.WebApi

# First Manager account, only for the very first start
dotnet user-secrets set "Bootstrap:ManagerUserName" "<name>" --project src/Netora.WebApi
dotnet user-secrets set "Bootstrap:ManagerPassword" "<password, 10+ chars with upper, lower, digit and symbol>" --project src/Netora.WebApi

```

Useful commands:

```powershell
dotnet user-secrets list --project src/Netora.WebApi
dotnet user-secrets remove "<key>" --project src/Netora.WebApi
```

`list` prints the values in plain text. They are stored in `%APPDATA%\Microsoft\UserSecrets\45b1ddea-c84f-4d23-93b2-a09208cd6926\secrets.json` and exist only on this machine, so set them again on every new machine. .NET reads user-secrets only in the Development environment.

### Secrets file

Use this on a server, or whenever you prefer a file over user-secrets. It works in every environment.

1. In the folder the API runs from, copy `appsettings.Secrets.example.json` to `appsettings.Secrets.json`. For `dotnet run` that is `src/Netora.WebApi`; for a published copy it is the publish folder.
2. Fill in the values and delete the ones you don't need. Use the same keys as user-secrets, written as JSON:

```json
{
  "ConnectionStrings": { "DefaultConnection": "Server=<host>;Database=<db>;User Id=<user>;Password=<password>;TrustServerCertificate=True" },
  "Jwt": { "SigningKey": "<random string of 32+ characters>" },
  "Email": { "Smtp": { "UserName": "<smtp user name>", "Password": "<smtp password>" } }
}
```

3. Restrict the file so only the account that runs the API (and administrators) can read it. In File Explorer use Properties > Security.

The file is git-ignored, and build and publish never copy it, so it never leaves the machine it was created on. If a value is set in both places, user-secrets win in Development.

### 2. Database

Create or update the schema before the first start, because the API seeds the Manager and Staff roles at startup:

```powershell
dotnet ef database update --project src/Netora.Infrastructure --startup-project src/Netora.WebApi
```

### 3. First Manager

Start the API once (see [Run locally](#run-locally)). It creates the first Manager from the `Bootstrap` secrets, but only when no Manager exists. Then remove those two secrets (or delete the `Bootstrap` section from `appsettings.Secrets.json`):

```powershell
dotnet user-secrets remove "Bootstrap:ManagerUserName" --project src/Netora.WebApi
dotnet user-secrets remove "Bootstrap:ManagerPassword" --project src/Netora.WebApi
```

In Development the API also inserts 20 fictional sample customers into an empty Customers table. They are flagged `IsSampleData = 1` and can be removed with `DELETE FROM Customers WHERE IsSampleData = 1`.

### 4. HTTPS development certificate

Trust it once, so the WebUI can call the API over https:

```powershell
dotnet dev-certs https --trust
```

## Configuration

Non-secret settings live in `appsettings.json`, with overrides in `appsettings.Development.json` and `appsettings.Production.json`. Settings marked **Secret** below go in user-secrets or `appsettings.Secrets.json`, never in those files.

### Web API (`src/Netora.WebApi`)

| Setting | Default | Purpose |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | none | **Secret.** SQL Server connection string (required) |
| `Jwt:SigningKey` | none | **Secret.** Token signing key, 32+ characters (required) |
| `Jwt:Issuer`, `Jwt:Audience`, `Jwt:LifetimeMinutes` | `Netora.WebApi`, `Netora.WebUI`, `480` | Token settings |
| `Bootstrap:ManagerUserName`, `Bootstrap:ManagerPassword` | none | **Secret.** First Manager, first start only |
| `Swagger:Enabled` | `true` in Development, `false` elsewhere | When `false`, `/swagger` returns 404 |
| `Attachments:StoragePath` | `App_Data/attachments` | Folder for customer attachments, outside any web root. Empty in Production settings, so it must be set there |
| `Attachments:MaxFileSizeBytes` | `10485760` (10 MB) | Upload limit |
| `DataProtection:KeysPath` | `App_Data/keys` | Keys that protect stored PPPoE, router, and SMTP passwords. Empty in Production settings, so it must be set there. Back it up with the database: without these keys the stored passwords cannot be decrypted |

`App_Data/` is git-ignored.

| `Router:Mode` | `Mock` (empty in Production) | Required startup default. A Manager's mode selection in Settings takes precedence once saved |
| `Router:TimeoutSeconds` | `10` | Timeout per router call |
| `Router:Mock:StatePath` | `App_Data/mock-router.json` | Where the mock keeps its secrets. Delete the file to reset the mock (it reseeds from the customers) |
| `Router:Mock:FailWrites` | `false` | Makes every mock change fail, to try failures and Retry |

The router address, port, API user, password and optional certificate fingerprint are entered on the **Settings** page (password stored encrypted), not in appsettings.

### WebUI (`src/Netora.WebUI`)

| Setting | Default | Purpose |
|---|---|---|
| `Api:BaseUrl` | `https://localhost:7225/` in Development, empty elsewhere | Absolute URL of the Web API (required) |

The WebUI prints `Web API base URL: ...` at startup. If sign-in says "Sign-in is unavailable", check that line first.

### Log files (both apps)

| Setting | Default | Purpose |
|---|---|---|
| `Logging:File:Path` | `logs/api` (API), `logs/web` (WebUI) | Folder for the daily log files. Relative paths are under the app folder. Created when missing. Must be outside `wwwroot`, or the app does not start |
| `Logging:File:LogLevel:Default` | `Warning` | Lowest level of ordinary log lines (framework warnings and errors) written to the file |
| `Logging:File:LogLevel:Netora.Activity` | `Information` | Business events (sign-in, customers, approvals, router). Keep it at `Information` |

### Production

No environment variables are needed.

1. **Secrets:** create `appsettings.Secrets.json` in the API folder on the server (see [Secrets file](#secrets-file)).
2. **Web API, non-secret:** in the API's `appsettings.Production.json`, set `Attachments:StoragePath` and `DataProtection:KeysPath` (folders outside the web root), plus the `Email` server settings if you want emails.
3. **WebUI:** in the WebUI's `appsettings.Production.json`, set `Api:BaseUrl` to the API address.
4. **Log folders (optional):** set `Logging:File:Path` in each app's `appsettings.Production.json` to choose where the log files go, and allow only administrators and the service account to read that folder. Without it, the files go to `logs/api` and `logs/web` inside each app folder.

The apps refuse to start while a required setting is missing, and the error names the missing setting.

## Log files

Each app writes one plain-text file per day, named by the server's local date: `netora-api-2026-11-24.txt` in the API's log folder and `netora-web-2026-11-24.txt` in the WebUI's. The first event after midnight starts the next file. Old files are never deleted or rotated; remove them by hand when no longer needed. These files are separate from the Audit Trail, which will read the database (module 12). There is no log viewer in the app.

One event per line:

```text
2026-11-24 09:42:17.123 +08:00 [INF] [Customers] CustomerCreated actor=manager01 role=Manager target=Customer:1042 outcome=Success ip=192.168.7.20 corr=8f3a1c
```

- **Fields, in order:** time with UTC offset, level (`INF`, `WRN`, `ERR`), category, event, `actor` (user name or `anonymous`), `role`, `target` (entity and ID), `outcome`, `ip`, `corr`, `detail`. Optional fields are left out when empty. Values with spaces are in quotes. An exception follows on indented lines.
- **Correlation ID (`corr`):** the same value appears in the WebUI file, the API file and the `traceId` of API responses, so one action can be followed across both apps. The WebUI sends it to the API in the `X-Correlation-ID` header.
- **API events:** LoginSucceeded, LoginFailed, LockoutTriggered, Logout, PasswordChanged; CustomerCreated, CustomerUpdated (changed field names), CustomerAccessDenied (Staff opening a Manager-only customer), attachments; PppoeUpdated, ImportStarted, ImportCompleted, AccountCreated, AccountSuspended, AccountReactivated, PlanChanged, AccountTerminated; RequestSubmitted, RequestApproved, RequestRejected, ProvisioningSucceeded, ProvisioningFailed, RetryAttempted; UserCreated, RoleChanged, UserDeactivated, UserReactivated and other user changes, SettingsUpdated; PlanCreated, PlanUpdated, PlanDeactivated, PlanReactivated, NapCreated, NapUpdated, NapDeactivated, NapReactivated (moving a customer is CustomerUpdated with `Changed: NAP Box`); ConnectionTested, RouterError; UnhandledException, ValidationFailed (field names), AuthorizationDenied, EmailDeliveryFailed.
- **WebUI events:** LoginSucceeded, LoginFailed, Logout and UnhandledException.
- **Never written:** passwords, tokens, cookies, connection strings, router or SMTP credentials, request bodies, customer names, phone numbers, addresses, free-text reasons and notes. A failed login records the user name exactly as typed (control characters removed, at most 256 characters), so a password typed into the user name box would appear: keep the folder restricted.
- **If the file cannot be written** (disk full, no permission), the request still completes and the problem is printed to the console.

## Approval emails

- **New request:** every active Manager with an email address on their account is emailed. The requester is not.
- **Decision:** the requester is emailed.
- **New PPPoE account approved:** the active `admin` Manager receives the account name, installation address, mobile number, PPPoE username, password, and remote IP. Set the admin email address in User Management and enable approval emails with an SMTP TLS mode.
- **PPPoE session disconnect:** after MikroTik confirms a Manager’s force disconnect, Netora records the action and emails active Managers when approval emails are enabled. The subscriber can reconnect using the existing account.
- **PPPoE deletion:** after the router secret is removed or confirmed absent, all active Managers with email addresses receive a completion notice, including the Manager who performed the deletion.
- **Recipients:** email addresses are sent in BCC. The new-account details are included in the admin email.
- **Failures:** emails are sent in the background. A failed or unconfigured email is logged on the console (`ApprovalEmailFailed`, `ApprovalEmailSkipped`) and never changes the approval. A failed send also writes `EmailDeliveryFailed` (error type only) to the log file.

Managers set recipient email addresses under User Management. Each user can also set their own address in My Profile (`/profile`).

### Configure SMTP in Settings

Open **Settings → Email Configuration** as a Manager. Set the SMTP server and port, the mail login and password or app password, the connection security mode, the From address, and the sender name. The timeout, certificate revocation check, and optional link address are on the same form.

Save the settings, then use **Send Test Email to Me** to test delivery to the address in My Profile. You can test with approval emails off. Use **Send approval emails** to enable automated messages.

Netora encrypts the password using its existing Data Protection keys. Leave the password blank to keep it when the connection and login stay the same. Saving the complete form overrides legacy SMTP configuration. If the form still uses the same legacy connection and login, saving it copies the password into encrypted storage.

For Gmail, use `smtp.gmail.com` with port `587` and STARTTLS, or port `465` and TLS on connect. Enter the Gmail address as the login and use a Google app password. Use that address or an authorized alias for the From address.

## MikroTik router

Netora talks to one RouterOS 7 router over the REST API (HTTPS). It never deletes a PPP secret.

- **When the router is contacted:** when a Manager approves a request (create, disable, enable, change plan, terminate), when Retry is pressed on a request, when a customer profile is opened or refreshed (live status and Configuration Mismatch), on Test Connection, and on an Import scan. There is no background synchronization.
- **What is sent:** the account's current Netora values (desired state), so sending twice changes nothing more. A request that already succeeded is never sent again. A failed send is recorded on the request with the error; the approval stands and a Manager can retry.
- **Safety:** creation never overwrites a secret Netora did not create (Netora marks its own secrets with `[netora:NET-0000]` in the comment). Mismatches are shown, never fixed automatically.
- **Mock mode:** Netora uses a simulated router when the saved mode is Mock or when Mock is the startup default and no mode has been saved. Router data from this mode is labeled "Mock router".

## Internet Plans and NAP Boxes

- **Plans:** each plan has a name, monthly fee (₱), download and upload speed, and the name of a PPP profile on the router. Netora sends that profile name when it creates or updates a customer's PPPoE secret.
- **Send to Router** (Managers only, per plan, after a confirmation) creates the plan's PPP profile on the router if it is missing, or sets its `rate-limit` to the plan's speeds, written as upload/download (`60M/110M` for 110 down, 60 up). Nothing else on the profile changes and nothing is deleted. Saving a plan alone never changes the router. Customers already connected get the new speed when they reconnect. **Check Router Profiles** lists every plan's profile as Found, Missing or Speed differs.
- The plan list sorts by **Plan**, **Monthly Fee** or **MikroTik Profile** (click a column header; click again to reverse). Plans without a fee are listed last.
- **Copy** on a plan (or **Copy Plan** on its edit page) opens Add Internet Plan filled in from that plan. Each plan needs its own MikroTik profile, so saving a copy (or an edit) with a profile another plan already uses is refused with the name of that plan.
- **Changing a plan** in the catalogue needs no approval and does not resend customers. If the profile changes, customers on the plan show a Configuration Mismatch until a Manager-approved change is sent for them. Moving a customer to another plan is still a Change Plan request.
- **NAP boxes:** code, area, description and status. No ports or capacity are tracked. **UNKNOWN** is built in and cannot be edited; imported accounts start there. Staff and Managers move customers between NAP boxes from the NAP details page or the customer form; no approval, no router change.
- **Inactive** plans and NAP boxes cannot be chosen for new customers, plan changes or moves; customers already on them keep them. Nothing is deleted.
- **Import** now needs a plan whose MikroTik profile matches the secret's profile; secrets with an unmapped profile show "Cannot import" until such a plan exists.
- **Upgrading an existing database:** the migration `PlansAndNapBoxes` turns every plan name and NAP code in use into catalogue entries (profile = plan name, fees and speeds empty) and links customers to them by ID.

### Connecting a real router

1. On the router (Winbox Terminal), set the clock first (`/system ntp client set enabled=yes servers=pool.ntp.org`), because certificates take their dates from it. Then create a certificate, enable `www-ssl` with it for the Netora server's address only, and add a dedicated user:
   ```
   /certificate add name=netora-ca common-name=netora-ca key-usage=key-cert-sign,crl-sign days-valid=3650
   /certificate sign netora-ca
   /certificate add name=router-https common-name=<router-ip> subject-alt-name=IP:<router-ip> days-valid=3650
   /certificate sign router-https ca=netora-ca
   /ip service set www-ssl certificate=router-https disabled=no address=<server-ip>/32
   /user group add name=netora policy=read,write,api,rest-api
   /user add name=netora group=netora address=<server-ip>/32 password=<strong password>
   /certificate print detail where name=router-https
   ```
   The last command shows the `fingerprint` to enter in Settings. Netora uses the REST API on `www-ssl` only; `api`, `api-ssl` and plain `www` are not needed.
2. In **Settings → MikroTik Connection**, select **RouterOS** and enter the router address, HTTPS port, API user name and password. For a self-signed certificate, enter its SHA-256 fingerprint.
3. Click **Save Router Settings**. The selected mode applies to subsequent router actions without restarting the API.
4. Click **Test Connection**. A successful live test shows the router's identity and RouterOS version.
5. On **Internet Plans**, enter each plan's speeds and profile name, press **Send to Router** on each plan (creates or updates the profile's rate-limit), then **Check Router Profiles**.
6. Use **PPPoE Management > Import Accounts** to bring existing secrets into Netora (scan, review, confirm).

## Run locally

Start the API first, then the WebUI, each from its own folder with a plain `dotnet run`:

```powershell
cd src/Netora.WebApi
dotnet run
```

```powershell
cd src/Netora.WebUI
dotnet run
```

Then open `https://localhost:7067` and sign in.

Both apps listen on all network interfaces over http and https (WebApi: 7225 / 5132, WebUI: 7067 / 5020). This comes from the first profile, `all-interfaces`, in each `Properties/launchSettings.json`, which `dotnet run` uses. The other profiles (`http`, `https`, `IIS Express`) work with `--launch-profile <name>`.

Other machines need inbound Windows Firewall rules for those four ports. The development certificate is issued for `localhost`, so remote browsers show a certificate warning.

| Check | URL |
|---|---|
| Swagger (Development) | `https://localhost:7225/swagger` or `http://localhost:5132/swagger` |
| API health | `https://localhost:7225/api/v1/health` |
| Full path, WebUI → API → database (signed in) | `https://localhost:7067/system/api-health` shows `"status":"Healthy"` |

## Tests

```powershell
dotnet test tests/Netora.Tests
```

The tests use the EF Core in-memory provider, so they need no database. That provider has no unique indexes or real transactions, so those cases are checked against SQL Server separately.

## Database migrations

Apply pending migrations before starting the updated API. `OnuHistoryAndTokenRevocation` adds ONU history and access token revocation tables. `PppoeAccountDeletion` records PPPoE deletion authorization and completion. `CustomerDeletion` archives deleted customers. `OperationsPageHistory` adds backup runs and approval-email delivery history.

```powershell
dotnet ef migrations add <Name> --project src/Netora.Infrastructure --startup-project src/Netora.WebApi --output-dir Persistence/Migrations
dotnet ef database update      --project src/Netora.Infrastructure --startup-project src/Netora.WebApi
```

## SQL Server backups

Use **Backups** as a Manager. Enter a directory on a separate volume or storage mount on the SQL Server host. The SQL Server service account needs write access to that directory. The API login needs permission to back up the Netora database and read its backup history in `msdb`. Netora queues full copy-only backups with checksums. The WebAPI runs queued and scheduled jobs. The daily schedule uses Singapore time.

Set retention on the backup storage host using your retention policy. Keep separate copies of the Data Protection key ring and customer attachments alongside the SQL backups. To validate recovery, have a SQL Server administrator run `RESTORE VERIFYONLY` and restore a copy to an isolated database to test it with the corresponding keys and attachments.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| Build error MSB3021/MSB3026 "file is locked by Netora.WebApi" or "Netora.WebUI" | The app is still running from an earlier `dotnet run`. Stop it with Ctrl+C in its terminal, or `Get-Process Netora.WebApi, Netora.WebUI \| Stop-Process`, then build again |
| "Sign-in is unavailable right now" | The WebUI cannot reach the API. Check the `Web API base URL` line printed at WebUI startup. If it shows an unexpected address, a leftover `Api__BaseUrl` variable in that terminal is overriding `appsettings`: open a new terminal and run the WebUI there |
| API stops with "Connection string ... is not configured" | The secret is missing on this machine. Check `dotnet user-secrets list --project src/Netora.WebApi`, or `appsettings.Secrets.json` in the API folder |
| `https://<host>:7225/` returns 401 | Expected: the API has no page at `/` and requires sign-in. Use `/swagger` or `/api/v1/health` |
| The WebUI cannot call the API over https | Run `dotnet dev-certs https --trust` once |
| Test Connection or PPPoE import says "Mock router" | In **Settings → MikroTik Connection**, select **RouterOS**, enter the router credentials, save, and test before scanning |
| The saved MikroTik password cannot be read | Re-enter the API password under **Settings → Router Settings**. Save and use **Test Connection** in RouterOS mode |
| "The SSL connection could not be established" and an AVG popup about `https://<router-ip>/` | AVG Web Shield intercepts the HTTPS connection and replaces the router's certificate, so the fingerprint never matches. Add an exception for `https://<router-ip>/*` in AVG (Web Shield > Exceptions) |
| AVG or Netora says the router certificate (or its issuer) has expired | The router's clock was wrong when the certificate was made. Set the clock (NTP), remove and recreate both certificates, and enter the new fingerprint |
| No log file appears | Check the console for `Netora file log: could not write to ...`, and that the app account may write to `Logging:File:Path` |
