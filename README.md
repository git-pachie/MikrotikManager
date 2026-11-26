# Netora MikroTik Manager

An ISP management application for customers, PPPoE accounts and Manager approvals, built with ASP.NET Core 9 and Clean Architecture:

Browser → `Netora.WebUI` (MVC) → `Netora.WebApi` → SQL Server, and later MikroTik RouterOS 7 HTTPS REST.

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
| Sign-in | `/login` | Manager and Staff roles, lockout after 5 failed attempts for 15 minutes, cookie session in the WebUI, JWT between WebUI and API |
| Customers | `/customers`, `/customers/{id}`, `/customers/new`, `/customers/{id}/edit` | Search, filters, sorting, paging, details, add/edit, attachments (JPG, PNG, PDF up to 10 MB). Manager-only customers are hidden from Staff, including in counts |
| PPPoE | `/pppoe`, `/pppoe/{id}` | Accounts list and edit, encrypted password. Import is layout only |
| Approvals | `/approvals`, `/approvals/{id}` | Create, Disable, Enable, Change Plan and Terminate requests; only Managers decide; approval emails |
| Users and Settings | `/users`, `/settings` (Managers), `/profile` (everyone) | Add, edit, enable/disable and password reset with a temporary password; router details (password encrypted) and email settings with a test email; own name, email, theme and password |

| MikroTik | `/settings`, `/approvals/{id}`, `/customers/{id}`, `/pppoe/import` | Approved changes sent to the router with Retry, live status and Configuration Mismatch on the customer profile, Test Connection, Import. Uses a mock router until `Router:Mode` is `RouterOS` |
| Internet Plans | `/plans`, `/plans/new`, `/plans/{id}/edit` | Plan catalogue with fee, speeds and the MikroTik PPP profile each plan maps to; Check Router Profiles. Managers add, edit and deactivate; Staff view |
| NAP Boxes | `/nap-boxes`, `/nap-boxes/new`, `/nap-boxes/{id}` | NAP list with customer counts, details with assigned customers, move customers between NAP boxes, UNKNOWN review. Managers add and edit; Staff view and move |
| Logging | none (server files) | Daily text files `netora-api-yyyy-MM-dd.txt` and `netora-web-yyyy-MM-dd.txt`, see [Log files](#log-files) |

The other sidebar pages are not built yet.

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

# SMTP login for approval emails (optional; nothing is sent until Email:Enabled is true)
dotnet user-secrets set "Email:Smtp:UserName" "<smtp user name>" --project src/Netora.WebApi
dotnet user-secrets set "Email:Smtp:Password" "<smtp password>" --project src/Netora.WebApi
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
| `DataProtection:KeysPath` | `App_Data/keys` | Keys that encrypt stored PPPoE passwords. Empty in Production settings, so it must be set there. Back it up with the database: without these keys the stored passwords cannot be decrypted |
| `Email:Enabled` | `false` | Turns approval emails on or off. When `false`, nothing is sent and each skipped email is logged with `Reason=Disabled` |
| `Email:Smtp:Host`, `Email:Smtp:Port` | `smtp.gmail.com`, `587` | SMTP server. Emails are also skipped (`Reason=NotConfigured`) while Host or From is empty |
| `Email:Smtp:UseSsl` | `true` | `true`: always encrypted. Port 465 uses SSL from the start, any other port (587) uses STARTTLS, and sending fails if the server cannot encrypt. `false`: no encryption, only for an internal mail relay |
| `Email:Smtp:UserName`, `Email:Smtp:Password` | none | **Secret.** SMTP login |
| `Email:Smtp:TimeoutSeconds` | `30` | SMTP timeout |
| `Email:Smtp:CheckCertificateRevocation` | `true` | Set `false` only on a PC where antivirus email scanning (for example AVG Mail Shield) replaces the server certificate; trust and host name are still checked |
| `Email:From`, `Email:FromName` | empty, `Netora` | Sender of approval emails |
| `Email:AppBaseUrl` | empty | Optional public WebUI address for links in emails, for example `https://netora.example/` |

`App_Data/` is git-ignored.

| `Router:Mode` | `Mock` (empty in Production) | `Mock` = a pretend router for development, `RouterOS` = the real MikroTik over the RouterOS 7 REST API. Required: the API does not start while it is empty |
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
- **Content:** request and customer IDs only, no customer names or contact details. Recipients are in BCC.
- **Failures:** emails are sent in the background. A failed or unconfigured email is logged on the console (`ApprovalEmailFailed`, `ApprovalEmailSkipped`) and never changes the approval. A failed send also writes `EmailDeliveryFailed` (error type only) to the log file.

Managers set email addresses under User Management, and every user can set their own in My Profile (`/profile`). The **Settings** page can switch emails on or off and change the sender without editing `appsettings`; values saved there override `appsettings`. Its **Send Test Email to Me** button sends one email to your own address at once.

### Turning emails on with Gmail

1. In the Google account that will send the emails, turn on 2-Step Verification, then create an **app password** (Google Account > Security > App passwords). Gmail refuses the normal account password for SMTP.
2. Put the login in user-secrets (or in `appsettings.Secrets.json`):

```powershell
dotnet user-secrets set "Email:Smtp:UserName" "<your-address>@gmail.com" --project src/Netora.WebApi
dotnet user-secrets set "Email:Smtp:Password" "<16-character app password>" --project src/Netora.WebApi
```

3. In `src/Netora.WebApi/appsettings.json` (or `appsettings.Production.json` on a server) set:

```json
"Email": {
  "Enabled": true,
  "Smtp": { "Host": "smtp.gmail.com", "Port": 587, "UseSsl": true },
  "From": "<your-address>@gmail.com"
}
```

Port 465 with `"UseSsl": true` also works with Gmail. Use the same address in `From` as in `UserName`. Gmail replaces any other sender address unless it is set up as a "Send mail as" alias in that Gmail account. To stop sending, set `"Enabled": false`; nothing else needs to change.

## MikroTik router

Netora talks to one RouterOS 7 router over the REST API (HTTPS). It never deletes a PPP secret.

- **When the router is contacted:** when a Manager approves a request (create, disable, enable, change plan, terminate), when Retry is pressed on a request, when a customer profile is opened or refreshed (live status and Configuration Mismatch), on Test Connection, and on an Import scan. There is no background synchronization.
- **What is sent:** the account's current Netora values (desired state), so sending twice changes nothing more. A request that already succeeded is never sent again. A failed send is recorded on the request with the error; the approval stands and a Manager can retry.
- **Safety:** creation never overwrites a secret Netora did not create (Netora marks its own secrets with `[netora:NET-0000]` in the comment). Mismatches are shown, never fixed automatically.
- **Mock mode:** `Router:Mode = Mock` uses a pretend router. Every page that shows router data says "Mock router".

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
2. Switch the API to the real router and restart it. The Settings page stops showing "Mock router".
   - Dev PC (only this machine, the repo keeps `Mock`): `dotnet user-secrets set "Router:Mode" "RouterOS" --project src/Netora.WebApi`. Back to the mock: `dotnet user-secrets remove "Router:Mode" --project src/Netora.WebApi`.
   - Server: `"Router": { "Mode": "RouterOS" }` in `appsettings.Production.json`.
3. In **Settings**, enter the router address, HTTPS port, API user name and password. If the router uses a self-signed certificate, also enter its SHA-256 fingerprint; only that exact certificate is then accepted.
4. Press **Test Connection**. It should report the router's identity and RouterOS version.
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

```powershell
dotnet ef migrations add <Name> --project src/Netora.Infrastructure --startup-project src/Netora.WebApi --output-dir Persistence/Migrations
dotnet ef database update      --project src/Netora.Infrastructure --startup-project src/Netora.WebApi
```

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| Build error MSB3021/MSB3026 "file is locked by Netora.WebApi" or "Netora.WebUI" | The app is still running from an earlier `dotnet run`. Stop it with Ctrl+C in its terminal, or `Get-Process Netora.WebApi, Netora.WebUI \| Stop-Process`, then build again |
| "Sign-in is unavailable right now" | The WebUI cannot reach the API. Check the `Web API base URL` line printed at WebUI startup. If it shows an unexpected address, a leftover `Api__BaseUrl` variable in that terminal is overriding `appsettings`: open a new terminal and run the WebUI there |
| API stops with "Connection string ... is not configured" | The secret is missing on this machine. Check `dotnet user-secrets list --project src/Netora.WebApi`, or `appsettings.Secrets.json` in the API folder |
| `https://<host>:7225/` returns 401 | Expected: the API has no page at `/` and requires sign-in. Use `/swagger` or `/api/v1/health` |
| The WebUI cannot call the API over https | Run `dotnet dev-certs https --trust` once |
| Test Connection still says "Mock router" | The API was started before `Router:Mode` was changed. Restart it |
| "The SSL connection could not be established" and an AVG popup about `https://<router-ip>/` | AVG Web Shield intercepts the HTTPS connection and replaces the router's certificate, so the fingerprint never matches. Add an exception for `https://<router-ip>/*` in AVG (Web Shield > Exceptions) |
| AVG or Netora says the router certificate (or its issuer) has expired | The router's clock was wrong when the certificate was made. Set the clock (NTP), remove and recreate both certificates, and enter the new fingerprint |
| No log file appears | Check the console for `Netora file log: could not write to ...`, and that the app account may write to `Logging:File:Path` |
