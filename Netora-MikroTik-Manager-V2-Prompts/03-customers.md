# 03 Customers

### 03.01 (138 characters)

Build compact Customers List with search, sorting, filtering, pagination, PPPoE username, internet plan, NAP, account status, and actions.

### 03.02 (134 characters)

Build Customer Details showing profile, plan, PPPoE, NAP, ONU, attachments, approval requests, connection state, and activity history.

### 03.03 (132 characters)

Build validated Add/Edit Customer forms and API endpoints. Store customer data in SQL Server; WebUI must not call MikroTik directly.

### 03.04 (140 characters)

Retain customer profile, attachments, ONU history, and audit records after termination. Define lifecycle statuses before coding transitions.

### 03.05 (145 characters)

Support secure customer attachments with authorization, upload/download validation, and retention. Confirm permitted types and size limits first.

### 03.06 Manager-only customer deletion

Delete Customer is available from the Customers list and details. It requires completed PPPoE deletion in Netora and a fresh check that the configured router has no matching secret. The operation removes the customer from operational views and counts, retains an archived record and history, and records `CustomerDeleted` in the Audit Trail and file log.

## V2 mockup-aligned implementation instructions

Approved UI source(s): `approved-mockups/index.html`, `approved-mockups/customer-details.html`, `approved-mockups/customer-form.html`.

For each referenced page, reproduce its structure, navigation, colors, typography, icons and responsive behavior in Razor. Replace all fictional data and demo actions with API-backed ViewModels and authorized flows. Verify the rendered result against the source page. See `MOCKUP_TO_MVC_MAPPING.md`.
Ensure staff cannot infer restricted customers from any list, count, search, NAP total or export. Make Create/Edit a real persisted workflow, not the mockup preview.
