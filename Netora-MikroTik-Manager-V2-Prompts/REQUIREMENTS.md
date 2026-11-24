# Netora V1 Requirements Summary

Modules: Foundation; Authentication/Security; Customers; PPPoE; Internet Plans; NAP; ONU; Approvals; MikroTik Integration; Diagnostics; Dashboard; Audit/Backup; Settings/Users; Design; Handoff/Testing; Logging (V2, see `18-logging.md`).

See README.md and each numbered prompt file.

## Open specifications (do not invent)
Customer field schema, exact NAP port/capacity model, ONU fields and validation, detailed diagnostics, complete role-permission matrix, attachment file limits, dashboard formulas, (see module prompts for unresolved details). The 18 HTML designs and their CSS/assets are now approved and bundled.

## Architecture
Browser -> MVC WebUI (secure cookie session) -> separately authenticated Web API -> SQL Server and MikroTik local HTTPS REST API.
Backend owns business logic. No direct browser access to MikroTik or database.


## Approved UI baseline (V2)
All 18 HTML mockups in `approved-mockups/` are approved as **visual references**. See `MOCKUP_TO_MVC_MAPPING.md`. Visual approval does not mean demo buttons, fictional data or business logic are implemented or authoritative.

## Additional UI modules
Reports and Notifications have approved HTML reference pages. Implement them within the same API-first architecture, with permission-aware aggregation and notifications based on real approval/provisioning outcomes.

## Design / functional priority
Security, data visibility, Manager approvals and backend rules remain authoritative; mockups govern layout, typography and interaction presentation. Do not mistake sample customer names, IPs, plans, amounts, credentials or statuses for production data.
