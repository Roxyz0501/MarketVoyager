Market Voyager 1.0.1.0 — public preview

- Rebuilt the shopping list as an editable one-item-per-row table with frozen headers/name columns, search and paging.
- Separated registration into an Add items tab: search/select, set price/quantity/quality, then add. NPC-based pricing is expandable.
- Added persistent top Start / Pause or Resume / Stop controls. Pause waits for the current movement or transaction to be reconciled, then resumes the same tour without using paused time as a timeout.
- Single-item and selected/all-row operations for price, quantity, quality, NPC price minus margin, purchased-count reset and removal. Dedicated bulk NPC pricing only updates price and tax inclusion.
- Moved auto-sale and list naming into List settings; preserved saved lists, counters, sale ledger and required shared tabs.
- Gil Ledger (Roxyz0501, MIT) was referenced for table/filter layout concepts; no UI source file copied.

Validation: Release build 0 warnings/errors; 931 core assertions; 800 ImGui/glyph/lifecycle/controller checks and 30 offline renders. Unchanged views write zero config files.

Actual in-game purchases, sales, pause/resume, travel and warning handling remain unverified. Lifestream/vnavmesh requirements and cross-logical-DC compatibility limitations remain. This is a public preview.
