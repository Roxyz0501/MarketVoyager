Market Voyager 1.0.0.0 — public preview

New standalone plugin by Roxyz0501, separate from Market Sweep.

- Multiple world-tour shopping lists; inclusive unit caps with optional actual tax, cumulative quantity caps, HQ/NQ/Any.
- Local catalogue by market category, name/ID, minimum NPC resale price, and several sort orders. Multi-select or add all results; batch edits and selective/all purchased-count reset.
- Set a tax-inclusive cap from the conservative NQ NPC resale price minus a gil margin.
- Optional per-list NPC auto-sale at a free-slot threshold (0 means full), followed by return to the same world's market and resumption. Sales never reduce purchased counts. Only fully accounted stacks of plugin-acquired unsold items are eligible.
- Normal game UI purchase and sale actions; no direct purchase packets. Inventory reconciliation, bounded same-listing retries and listing-fetch reopening.
- Lifestream and vnavmesh required; configuration shows loaded state. Seven UI languages, canonical language/support tabs and original icon.

Validation: clean Release with no warnings/errors; 922 core assertions; 772 native ImGui/glyph/lifecycle checks and 28 offline renders.

Limitations: actual in-game purchases, vendor selling, warnings, return/resume route, search UI and travel remain unverified. Cross-logical-DC aetheryte travel needs a compatible Lifestream version. This is a public preview. No installed plugin binaries were replaced.
