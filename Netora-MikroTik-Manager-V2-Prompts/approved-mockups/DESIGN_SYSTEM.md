# Netora UI Master Template V1
**Approved source of truth**: `index.html` (Customers List) and `css/netora.css`.
- Font: Inter 400 body, Plus Jakarta Sans headings; preserve antialiasing and optimizeLegibility declarations.
- Tokens: reuse CSS variables in `:root` and `[data-bs-theme=dark]`; no unapproved palette changes.
- Shared layout: navy sidebar with wave art, topbar, theme toggle, content grid, compact cards/tables, SVG icon sprite embedded in HTML.
- Assets: `assets/netora-logo.svg`, `assets/sidebar-waves.svg`, icons SVG sprite; CSS `css/netora.css`.
- New pages use `.module-*` classes for reusable sections, tables, form fields and metrics.
- Page-specific actions are prototypes, not real provisioning. Never represent fake status as live router data.
- For production, replace fictional demo data with authorised API responses. Apply Manager-only restrictions server-side, not by hiding DOM nodes.
