# Netora MikroTik Manager

An ISP management application for customers, PPPoE accounts and Manager approvals, built with ASP.NET Core 9 and Clean Architecture:

Browser → `Netora.WebUI` (MVC) → `Netora.WebApi` → SQL Server, and later MikroTik RouterOS 7 HTTPS REST.

The WebUI never touches the database or the router. It calls the Web API, which owns all business rules and permissions.

| Project | Role |
|---|---|
| `Netora.Domain` | Entities and domain rules |
| `Netora.Application` | Business logic, DTOs, validation, abstractions |
| `Netora.Infrastructure` | EF Core and SQL Server, Identity, email, file storage |
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

Not connected yet: the MikroTik router. Approved changes are saved in the database and shown as "Not sent to router". The other sidebar pages are not built yet.

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
| `Email:From`, `Email:FromName` | empty, `Netora` | Sender of approval emails |
| `Email:AppBaseUrl` | empty | Optional public WebUI address for links in emails, for example `https://netora.example/` |

`App_Data/` is git-ignored.

### WebUI (`src/Netora.WebUI`)

| Setting | Default | Purpose |
|---|---|---|
| `Api:BaseUrl` | `https://localhost:7225/` in Development, empty elsewhere | Absolute URL of the Web API (required) |

The WebUI prints `Web API base URL: ...` at startup. If sign-in says "Sign-in is unavailable", check that line first.

### Production

No environment variables are needed.

1. **Secrets:** create `appsettings.Secrets.json` in the API folder on the server (see [Secrets file](#secrets-file)).
2. **Web API, non-secret:** in the API's `appsettings.Production.json`, set `Attachments:StoragePath` and `DataProtection:KeysPath` (folders outside the web root), plus the `Email` server settings if you want emails.
3. **WebUI:** in the WebUI's `appsettings.Production.json`, set `Api:BaseUrl` to the API address.

The apps refuse to start while a required setting is missing, and the error names the missing setting.

## Approval emails

- **New request:** every active Manager with an email address on their account is emailed. The requester is not.
- **Decision:** the requester is emailed.
- **Content:** request and customer IDs only, no customer names or contact details. Recipients are in BCC.
- **Failures:** emails are sent in the background. A failed or unconfigured email is logged (`ApprovalEmailFailed`, `ApprovalEmailSkipped`) and never changes the approval.

User accounts get email addresses through User Management (module 13). Until then there are no recipients, so nothing is sent.

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
