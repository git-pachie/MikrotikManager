# Netora MikroTik Manager — Complete Static HTML UI (V1)
Open `index.html` or `dashboard.html` in a browser. All HTML pages share the same approved Customers List palette, typography (Inter / Plus Jakarta Sans), font smoothing, sidebar and SVG assets.
## Pages
- `index.html` — Customers List (approved visual baseline)
- `customer-details.html` — Customer Details
- `customer-form.html` — Add/Edit Customer
- `dashboard.html` — Dashboard
- `approvals.html` — Pending Approvals
- `pppoe.html` — PPPoE Management
- `import-pppoe.html` — Import Existing PPPoE
- `nap-boxes.html` — NAP Inventory
- `nap-details.html` — NAP Details
- `plans.html` — Internet Plans
- `onu-history.html` — ONU History
- `diagnostics.html` — Diagnostics
- `audit.html` — Audit Trail
- `users.html` — User Management
- `settings.html` — Settings
- `reports.html` — Reports
- `backups.html` — Backups
- `notifications.html` — Notifications
All additional pages beyond the approved Customers List are **for design review**.
## Important limitations
This is a frontend prototype with fictional data. No backend API, SQL Server, MikroTik REST, SMTP, Identity login, real approvals, or file upload is implemented. Most action buttons show an honest demo notice. Customer form validates and previews locally only. `?mode=edit` is not connected to customer data. Light/dark theme persists in localStorage. Fonts and Bootstrap use CDNs and may require internet access.
## Architecture for production
Separate ASP.NET Core MVC frontend, ASP.NET Core Web API backend, SQL Server/EF Core, ASP.NET Core Identity, RouterOS 7 HTTPS REST, Manager-only server-side visibility, approval-before-provisioning, audit and safe retries. See `DESIGN_SYSTEM.md`.
