Market Voyager 1.0.1.7 — edit shopping lists while paused

- Unlock list editing only after the controller is fully paused. Waiting for an active transaction or movement to finish keeps editing locked; Start remains disabled during a paused tour.
- Resume validates and reloads the currently selected list, including additions, deletions, price/tax/quality/quantity changes and list switching. Preserve purchased counts, skip newly completed rules and search the current tour world's list from the beginning using fresh listings.
- Keep the remaining world itinerary. A pause at the aetheryte continues to the already queued next world. A paused vendor visit uses the edited list when auto-sell remains enabled, or returns to the market if disabled. Previously visited worlds are not revisited.
- Deleting the active list uses the replacement selection on resume. An empty or fully purchased list ends the tour; invalid overlapping rules stop before another purchase.
- Validate again on resume without introducing external API requests. Existing purchase caps, inventory reconciliation and normal UI routes remain.

Validation: clean Release with zero warnings/errors; 943 core assertions, 542 native checks, 1022 UI/glyph/lifecycle checks and 39 background renders. Production pause/edit/resume tests and Japanese paused UI inspected. Actual in-game continuation remains unverified; public preview.
