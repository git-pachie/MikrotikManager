# 18 Logging (V2 addition)
Daily text-file logging for operational events. This module is separate from the SQL-based Audit Trail in `12-audit-backup.md`. The Audit Trail page reads the database. Log files are for operators and troubleshooting on the server.

### 18.01 Daily log files
Write plain-text log files, one file per calendar date. File name pattern: `netora-yyyy-MM-dd.txt` (example: `netora-2026-11-24.txt`). A new file starts at the first event after midnight. Never append a different date's events to an existing file. The WebApi and WebUI write to their own files (`netora-api-...` and `netora-web-...`) or share one location with a source field. Choose one and document it.

### 18.02 Events to log
| Category | Events |
|---|---|
| Authentication | login success, login failed (with reason class), logout, lockout, password reset requested and completed |
| Customers | created, updated (changed field names), deleted or terminated, viewed by Manager-only rule denial |
| PPPoE | account created, suspended, reactivated, plan changed, terminated, import started and completed |
| Approvals | request submitted, approved, rejected, provisioning succeeded, provisioning failed, retry attempted |
| NAP / ONU / Plans | created, updated, deactivated, ONU replaced |
| Users and settings | user created, role changed, enabled, disabled, settings changed (names only, never values) |
| MikroTik | connection test result, timeout, unreachable, API error |
| Application | unhandled exceptions, validation failures at the API boundary, authorization denials, email delivery failures, backup run result |

### 18.03 Line format
One event per line, UTF-8, fields separated by a single space and in a fixed order:

`2026-11-24 09:42:17.123 +08:00 [INF] [Customers] CustomerCreated actor=manager01 role=Manager target=Customer:1042 outcome=Success ip=192.168.7.20 corr=8f3a1c`

Required fields: timestamp with offset, level (`INF`, `WRN`, `ERR`), category, event name, actor (user name or `anonymous`), outcome, correlation ID. Include `target` (entity type and ID) and `ip` where they apply. Exceptions go on the following lines, indented, with the stack trace. Use the correlation ID required by `12.03` so one request can be traced across WebUI, API, SQL and MikroTik.

### 18.04 What must never be logged
- Passwords, password hashes, tokens, cookies, anti-forgery values, API keys, connection strings, router credentials, SMTP passwords.
- Full request or response bodies.
- Customer personal data beyond the entity ID. Log the customer ID, not the name, phone or address. Log changed field names, not old and new values.
- On failed login, log the attempted user name (truncated to a fixed length, control characters removed) and the IP address. Users sometimes type a password into the user name field, so treat that field as sensitive and do not log it in clear text for unknown users. Decide the rule (for example, log only a hash or the first characters for unknown accounts) and document it.
- Escape or strip line breaks in every logged value so input cannot forge extra log lines.

### 18.05 Storage, access and retention
- The log folder is configured by setting (`Logging:File:Path`), outside the web root, never served over HTTP.
- The application account has write access to that folder only. Operators and Managers read logs through server access, not the browser, unless a later requirement adds a log viewer.
- Logging must not break requests. If the file cannot be written, the request continues and the failure goes to the console or the OS event log.
- Retention is configurable (`Logging:File:RetainDays`). The value is an open decision (see below). Delete files only by the retention job, never from request code.
- Log files hold operational data about Manager-only customers (IDs and actions). Restrict folder permissions to administrators and the service account.

### 18.06 Implementation notes
- Use a maintained logging library with daily rolling file output (Serilog with the `Serilog.Sinks.File` sink and `rollingInterval: Day` is the suggested option) behind `ILogger<T>`. Keep the file sink configuration in `appsettings`, so path, level and retention change without code changes.
- Define a small `IActivityLog` abstraction in Application (for example `LogEvent(category, eventName, actor, target, outcome)`). Controllers and services call it for business events, and the infrastructure implementation writes through the logging library. This keeps event names consistent and testable.
- Log in the API for business events, since the API owns the rules. The WebUI logs only its own events, such as login and unhandled UI errors.
- Write file logs asynchronously or buffered so a slow disk does not slow requests. Flush on shutdown.
- Event names are stable identifiers (`LoginSucceeded`, `LoginFailed`, `CustomerCreated`, `CustomerUpdated`, `CustomerDeleted`). Do not rename them without updating this document.

### 18.07 Tests
- A login success and a login failure each write exactly one line with the right level, event and outcome.
- Create, update and delete of a customer each write one line, and none contains the customer's name, phone or address.
- A value containing a line break produces one line, not two.
- No test log contains a password, token or connection string.
- An event after midnight goes to the next date's file (use a fake clock).
- A failing file write does not fail the request.

## Open specifications (do not invent)
- Log folder location on the server and the account that writes to it.
- Retention period in days. A proposal is 90 days, pending approval.
- Time zone for the file date: server local time or UTC.
- Whether WebApi and WebUI write separate files or one shared file.
- Failed-login rule for unknown user names (see 18.04).
- Whether Managers need an in-app log viewer. None is in the 18 approved mockups, and adding one needs a design.
