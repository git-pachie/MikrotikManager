# 08 Approvals

### 08.01 (161 characters)

Create approval requests for PPPoE creation, plan changes, suspension, reactivation, and deletion-related actions. Store actor, target, before/after, and status.

### 08.02 (123 characters)

Build Pending Approvals and Approval Details. Managers approve or reject. Record decisions and prevent duplicate execution.

### 08.03 (145 characters)

Email Managers when requests are submitted and notify requesters of approval or rejection. Log email failures without changing approval outcomes.

### 08.04 (129 characters)

Separate approval status from provisioning status. Execute authorized MikroTik actions only after approval; support safe retries.


## V2 mockup-aligned implementation instructions

Approved UI source(s): `approved-mockups/approvals.html`.

For each referenced page, reproduce its structure, navigation, colors, typography, icons and responsive behavior in Razor. Replace all fictional data and demo actions with API-backed ViewModels and authorized flows. Verify the rendered result against the source page. See `MOCKUP_TO_MVC_MAPPING.md`.
Implement real approval decision UI and a separate provisioning status/error/retry view; do not treat a toast as approval.
