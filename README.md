# Netora MikroTik Manager

ASP.NET Core 9 solution (.NET SDK 9) using Clean Architecture:

Browser → `Netora.WebUI` (MVC) → `Netora.WebApi` → SQL Server and MikroTik RouterOS 7 HTTPS REST.

| Project | Role |
|---|---|
| `Netora.Domain` | Entities and domain rules |
| `Netora.Application` | Business logic, DTOs, validation, abstractions |
| `Netora.Infrastructure` | EF Core, SQL Server, external integrations |
| `Netora.WebApi` | HTTP API (7225 https / 5132 http) |
| `Netora.WebUI` | Razor MVC front end (7067 https / 5020 http) |
| `Netora.Tests` | Tests |

Specs, approved mockups and module prompts are in `Netora-MikroTik-Manager-V2-Prompts/`. Progress is tracked in `IMPLEMENTATION_STATUS.md` there.

## Secrets (never commit real values)

Secrets are stored with .NET user-secrets in Development, and with environment variables elsewhere. Do not put real connection strings or passwords in any `appsettings*.json` file or in this README.

### Development: user-secrets

The Web API needs a SQL Server connection string. Run from the repository root:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=<host>;Database=<db>;User Id=<user>;Password=<password>;TrustServerCertificate=True" --project src/Netora.WebApi
```

Windows authentication example:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=<host>;Database=<db>;Trusted_Connection=True;TrustServerCertificate=True" --project src/Netora.WebApi
```

Useful commands:

```powershell
dotnet user-secrets list   --project src/Netora.WebApi
dotnet user-secrets remove "ConnectionStrings:DefaultConnection" --project src/Netora.WebApi
```

The Web API also needs a JWT signing key (at least 32 characters) and, only for the very first start, the first Manager account:

```powershell
dotnet user-secrets set "Jwt:SigningKey" "<random string of 32+ characters>" --project src/Netora.WebApi
dotnet user-secrets set "Bootstrap:ManagerUserName" "<name>" --project src/Netora.WebApi
dotnet user-secrets set "Bootstrap:ManagerPassword" "<password, 10+ chars with upper, lower, digit and symbol>" --project src/Netora.WebApi
```

The Manager is created on API start only when no Manager exists. After the first start, remove the two `Bootstrap` secrets with `dotnet user-secrets remove`. Apply migrations (see below) before the first start, because the API seeds roles on startup.

The WebUI only needs the API address, which is set in `src/Netora.WebUI/appsettings.Development.json` (`Api:BaseUrl`).

### Production: environment variables

| Variable | App | Purpose |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | WebApi | SQL Server connection string |
| `Jwt__SigningKey` | WebApi | Token signing key, 32+ characters |
| `Bootstrap__ManagerUserName`, `Bootstrap__ManagerPassword` | WebApi | First Manager, first start only |
| `Api__BaseUrl` | WebUI | Absolute URL of the Web API |
| `Attachments__StoragePath` | WebApi | Folder for customer attachments, outside any web root (required; empty in Production settings) |
| `Attachments__MaxFileSizeBytes` | WebApi | Upload limit in bytes (default 10485760 = 10 MB) |
| `DataProtection__KeysPath` | WebApi | Folder for the keys that encrypt stored PPPoE passwords (required). Back it up with the database: without these keys the stored passwords cannot be decrypted. |

## Run locally

Start each app from its folder with a plain `dotnet run`:

```powershell
cd src/Netora.WebApi
dotnet run
```

```powershell
cd src/Netora.WebUI
dotnet run
```

Both listen on all network interfaces over http and https (WebApi: 7225 / 5132, WebUI: 7067 / 5020). This comes from the first profile, `all-interfaces`, in each `Properties/launchSettings.json`; `dotnet run` uses the first profile. Other profiles (`http`, `https`, `IIS Express`) still work with `--launch-profile <name>`.

Other machines need inbound Windows Firewall rules for those four ports. The development certificate is issued for `localhost`, so remote browsers will show a certificate warning.

Trust the development HTTPS certificate once so the WebUI can call the API over https:

```powershell
dotnet dev-certs https --trust
```

Swagger is controlled by the `Swagger:Enabled` setting in `src/Netora.WebApi` (any environment; env var `Swagger__Enabled`). It is `true` in `appsettings.Development.json` and `false` in `appsettings.json` and `appsettings.Production.json`. When `false`, Swagger is not registered and `/swagger` returns 404. Dev URLs: `https://localhost:7225/swagger` or `http://localhost:5132/swagger`.

Check the full path (WebUI → API → database): open `https://localhost:7067/system/api-health`. A healthy result shows `"status":"Healthy"`.

## Database migrations

```powershell
dotnet ef migrations add <Name> --project src/Netora.Infrastructure --startup-project src/Netora.WebApi --output-dir Persistence/Migrations
dotnet ef database update      --project src/Netora.Infrastructure --startup-project src/Netora.WebApi
```
