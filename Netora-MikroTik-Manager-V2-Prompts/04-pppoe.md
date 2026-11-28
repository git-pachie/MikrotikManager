# 04 Pppoe

### 04.01 (133 characters)

List MikroTik PPPoE accounts and details via Backend API and RouterOS 7 REST API over local HTTPS. Link accounts to customer records.

### 04.02 (137 characters)

Create PPPoE accounts only after Manager approval. Validate unique username, assigned profile, and provisioning result. Avoid duplicates.

### 04.03 (131 characters)

Support approved suspension, reactivation, plan change, and termination. On termination disable the PPPoE secret; do not delete it.

### 04.04 (133 characters)

Import existing MikroTik PPPoE accounts without duplicates. Preserve account identifiers; assign UNKNOWN when NAP mapping is missing.

### 04.05 (139 characters)

Record approved provisioning failures with error details and retry state. Retry idempotently without repeating successful MikroTik changes.

### 04.06 Manager-only deletion

An explicit Delete action removes the verified PPPoE secret from the router and clears the stored credentials after completing deletion. Netora retains the customer as Terminated and keeps its history. Any pending approval for the account closes when deletion completes. The Audit Trail and file log record completion, and the configured email service notifies all active Managers with email addresses.

## V2 mockup-aligned implementation instructions

Approved UI source(s): `approved-mockups/pppoe.html`, `approved-mockups/import-pppoe.html`.

For each referenced page, reproduce its structure, navigation, colors, typography, icons and responsive behavior in Razor. Replace all fictional data and demo actions with API-backed ViewModels and authorized flows. Verify the rendered result against the source page. See `MOCKUP_TO_MVC_MAPPING.md`.
Import preview must come from a real on-demand router scan, with deduplication and explicit confirmation. Do not import automatically.
