Market Voyager 1.0.1.4 — listing eligibility, search focus and walking return

- Activate the search input through its normal collision-node focus event before entering text; wait for actual widget activation. Never force enabled flags.
- Recognize an already opened matching item listing window in both search stages, then run the existing price, tax, quality and quantity checks. A row rejected before dispatch is no longer marked attempted, and the existing timeout remains bounded.
- Display the last checked item's offer/candidate counts and log one eligibility summary per item attempt. No additional external requests or idle polling.
- Allow single/bulk deletion after stopping while retaining unresolved purchase/sale records until reconciliation. Counter reset remains protected for unresolved rows.
- Anchor list settings to the opening button and constrain its width/position; prevent gradual horizontal drift.
- Walk back to the Limsa main aetheryte before requesting a world change. Require Lifestream to recognize aetheryte 8; disable the return-to-gateway option.

Release build: zero warnings/errors. 943 core assertions, 542 native checks, 917 UI/glyph/lifecycle checks and 39 background renders passed. Actual in-game search, purchase and walking behavior remains unverified; public preview.
