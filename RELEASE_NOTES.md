Market Voyager 1.0.1.5 — closer market access and faster purchasing

- Walk to within 2m of the market board instead of stopping at 3m. Allow movement to settle for one second, then use ordinary interaction. Retry up to three interactions, at least two seconds apart, while waiting for the market window; retain the original 30-second timeout.
- Reduce purchase-stage transitions from a fixed second to 100ms, and observe local readiness every 100ms while buying. Checkbox changes wait 250ms. Movement and sale pacing are unchanged.
- Remove the fixed two-second delay after purchases. A purchase notification wakes inventory reconciliation; it never credits or advances on its own. Only the exact inventory increase clears the pending transaction, and the next listing waits for the confirmation window to close.
- Preserve price/quantity/quality limits, normal UI event paths, persisted reservations and existing bounded failure retries.

Actual in-game position and throughput remain unverified; public preview. No game input or installed DLL replacement performed.

Validation: clean Release, zero warnings/errors; 943 core assertions, 542 native checks and 929 UI/glyph/lifecycle checks, 39 background renders.
