# Netora Customers List — implementation design specification

Source reference: `docs/approved-customers-reference.png` (approved revised visual from conversation).

## Status
High-fidelity *approximation* of approved generated mockup, not mathematically pixel-identical. CSS HEX colors and SVG artwork are explicitly authored; the reference PNG did not contain original source assets. The supplied logo and wave are new SVG recreations and may need visual approval.

## CSS tokens
See `css/netora.css` `:root`: `--navy:#082b60`, `--navy-dark:#061f48`, `--blue:#0666f5`, `--blue-dark:#0055d8`, `--green:#07894b`, `--bg:#f5f9ff`, `--surface:#fff`, `--ink:#0a1531`, `--muted:#475b7d`, `--border:#d9e5f6`, `--thead:#e7f0fe`, `--sidebar-width:260px`, `--radius:13px`.

## Typography
Plus Jakarta Sans 800 for brand, headings, metric values; Inter 500-800 for UI. Font styles are loaded from Google Fonts; for a fully offline production build, obtain and self-host licensed font files. No font files included.

## Assets
- `assets/netora-logo.svg`: local vector recreation of Netora mark
- `assets/sidebar-waves.svg`: local vector wave decoration
- `assets/icons.svg`: locally bundled icon symbol sprite
- `docs/approved-customers-reference.png`: reference screenshot only, not an implementation asset

## Layout
Desktop sidebar fixed 260px, topbar 62px, compact table row height ~39px, 5 summary cards, 6 filter controls. Responsive breakpoints at 1400, 1100, 800, 460px. Horizontal scroll for the table on small screens. Theme toggle persists in browser localStorage *only in this static demo*; production per-user preference belongs in backend.

## Handoff
WebUI should render from authorized Backend API responses. Restricted customers must be excluded at API/database query level, including totals and pagination. Static mockup contains sample data only. Never treat the sample dashboard counts as live metrics.
