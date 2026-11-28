# Approved HTML → MVC implementation mapping (V2)

All references are relative to `approved-mockups/`. Suggested controller/view names are **implementation conventions**, not a pre-existing backend contract. API routes must be designed and documented in OpenAPI before use.

| Page | Approved HTML | Suggested MVC controller | Suggested Razor view(s) | Module prompts |
|---|---|---|---|---|
| Dashboard | `dashboard.html` | `DashboardController` | `Dashboard/Index.cshtml` | `11-dashboard.md` |
| Customers List | `index.html` | `CustomersController` | `Customers/Index.cshtml` | `03-customers.md` |
| Customer Details | `customer-details.html` | `CustomersController` | `Customers/Details.cshtml` | `03-customers.md` |
| Add/Edit Customer | `customer-form.html` | `CustomersController` | `Customers/Create.cshtml; Customers/Edit.cshtml` | `03-customers.md` |
| Pending Approvals | `approvals.html` | `ApprovalsController` | `Approvals/Index.cshtml` | `08-approvals.md` |
| PPPoE Management | `pppoe.html` | `PppoeController` | `Pppoe/Index.cshtml` | `04-pppoe.md` |
| Import PPPoE | `import-pppoe.html` | `PppoeController` | `Pppoe/Import.cshtml` | `04-pppoe.md` |
| NAP Boxes | `nap-boxes.html` | `NapBoxesController` | `NapBoxes/Index.cshtml` | `06-nap.md` |
| NAP Details | `nap-details.html` | `NapBoxesController` | `NapBoxes/Details.cshtml` | `06-nap.md` |
| Internet Plans | `plans.html` | `PlansController` | `Plans/Index.cshtml` | `05-plans.md` |
| ONU History | `onu-history.html` | `OnuController` | `Onu/Index.cshtml` | `07-onu.md` |
| Diagnostics | `diagnostics.html` | `DiagnosticsController` | `Diagnostics/Index.cshtml` | `10-diagnostics.md` |
| Audit Trail | `audit.html` | `AuditController` | `Audit/Index.cshtml` | `12-audit-backup.md` |
| User Management | `users.html` | `UsersController` | `Users/Index.cshtml` | `13-settings-users.md` |
| Settings | `settings.html` | `SettingsController` | `Settings/Index.cshtml` | `13-settings-users.md` |
| Reports | `reports.html` | `ReportsController` | `Reports/Index.cshtml` | `16-additional-pages.md` |
| Backups | `backups.html` | `BackupsController` | `Backups/Index.cshtml` | `12-audit-backup.md` |
| Notifications | `notifications.html` | `NotificationsController` | `Notifications/Index.cshtml` | `16-additional-pages.md` |

## Conversion rules

1. Extract the existing sidebar, header, navigation, light/dark switch, typography, SVG sprite and footer into shared Razor layout/partials without visual redesign.
2. Use `approved-mockups/css/netora.css` as the starting source of truth. Preserve custom properties, font smoothing and existing responsive rules; add scoped rules only where necessary.
3. Preserve real assets in `approved-mockups/assets/`; never replace the Netora logo or wave graphic with placeholders.
4. Replace JavaScript sample arrays, static table rows and fake metrics with strongly typed ViewModels populated by the separate Web API.
5. Replace demo toast actions with real authorized operations; use confirmation and pending states. No fake success.
6. Implement Manager-only filtering on the API before counts, search, exports, NAP totals, or any data reaches Staff.
7. Preserve approval state separately from router provisioning state, and show retry/error outcomes.
8. Do not copy `file://` or CDN assumptions into production without checking build and asset delivery.
9. Maintain accessibility and responsive layouts; verify desktop/mobile and light/dark against the approved pages.
10. When a mockup conflicts with the written functional requirements, preserve the visual intent but implement the written security/business rule and record the discrepancy.

## Known prototype limitations to resolve in implementation

- Mockup data and router health are fictional; no actual API, SQL, SMTP, Identity, file upload or RouterOS actions exist.
- The `customer-form.html?mode=edit` parameter does not populate a record; implement true edit workflow.
- Some buttons and tabs are presentation-only; connect each required action to a real workflow or visibly disable it.
- Router settings in mockups are examples, not deployable production defaults.
- Plans and sample pricing are illustrative, not a migration of production subscriber data.
- Detailed API endpoint names, DB schema, retention, attachment limits and permission matrix require explicit design decisions; do not invent user-approved values.
