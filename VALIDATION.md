# Validation — 2026-10-20

## Measured offline results

- Clean Release rebuild: 0 warnings, 0 errors, .NET 10 / Dalamud SDK 15.
- Core suite: 931 assertions passed, with 10,000 generated purchase candidates. Covers inclusive price, whole-stack quantity cap, HQ/NQ/Any, combined quality counts, missing HQ, overlapping rules, inventory delta reconciliation, persistence and language resolution. Also covers actual-tax price boundaries, batch add/edit atomicity, search/sort/filter, independent purchased/unsold ledgers, full-inventory threshold, sale inventory/gil reconciliation and resumed purchase limits. Pause tests cover pending transactions, movement, safe phase entry, resume deadlines and cancellation; dedicated bulk NPC pricing retains quantity/quality/counts.
- Native ImGui host: 800 glyph, rendering, lifecycle and unchanged-save checks. Produced 30 background images across seven languages. Rendered 45 registered items over two pages, catalogue, seven-language layouts, paused toolbar and the dedicated NPC bulk popup. The production runner pause/resume path was also invoked in the offline host before any native game calls. Inspected Japanese and German tables, Japanese catalogue and NPC popup. No native game window or user input was used.
- 20 unchanged rendered frames produced zero config saves. Search catalog contains 16,845 marketable game items in the installed build. Font glyph coverage passed for all translated UI text.
- Installed game data `2026.10.06.0000.0000`: Aetheryte 8 / territory 129 and market-board EObjName IDs 2000402, 2000440, 2000442 verified in Japanese, English, German and French.
- Lifestream 2.6.0.8 installed assembly inspected as metadata only: expected IPC signatures present. Its legacy DC route remains distinct; Market Voyager passes false for the legacy logout route. Same-region cross-logical-DC selection still requires a compatible Lifestream implementation and live validation.
- vnavmesh IPC reviewed against provider source. Path requests use a dedicated cancellation token; stopping before path completion does not schedule later movement.
- Canonical StandardPluginTabs.cs byte identity verified before packaging. Source/ZIP manifests contain author, version, repository and icon URLs.

## Data and communication budget

These are design bounds unless specifically marked measured above; they are not measurements of the live game's network protocol.

- No plugin HTTP client, remote database or price-service polling. No plugin-owned external HTTP reads/writes while displaying or editing lists.
- Item catalogue: one host sheet acquisition at plugin initialization; item-name lookup during rendering uses an in-memory dictionary. Search scans that bounded local catalogue only when a filter or sort changes, with 30 rows rendered per page; filters include category/minimum NPC price and stable sort.
- List limits: 50 lists × 1,000 rows; one active list. World sheet traversed once per run. Per-world visible listings bounded to the game's 100-entry buffer.
- Framework controller: at most four local checks/second while active. Idle controller returns immediately. Travel IPC checks do not themselves query a remote database.
- A world/item search is triggered once per stage; an accepted request advances the state. Listing-fetch timeout permits three reopening attempts per item. A purchase result timeout permits two additional searches of the same listing, separated by 20-second result deadlines and a 3-second retry delay.
- Configuration numeric/name edits debounce at 600 ms. Purchase reservations and completed quantity updates save immediately, since these are necessary for restart consistency. Unchanged views do not save.
- Config and catalogue are local. Inventory baselines contain the character content ID locally; no account/config data is uploaded by the plugin.

## Unverified in game

Actual market-board interaction and search-input propagation, native result readiness, listing/confirmation event wiring, equipment warnings, tax/display correlation, notification timing, reconnect behavior, vendor context menu / numeric / confirmation wiring, vendor eligibility and sale-return route, world/DC transfer, route geometry and live font/DPI handling. No gil was spent during development. The release is a public preview, not a claim of end-to-end game acceptance.

Automatic-sale design bounds: four loaded ordinary bags / 140 slots per local inventory scan, only while active; no added external API. Vendor sheet rows are inspected only when a sale detour begins. At most one outstanding purchase or sale. A sale timeout never repeats a sale; manual resolution drops the unresolved amount from sale eligibility without decreasing Purchased. A normal vendor candidate, Unsynrael (ENpcResident 1001207, direct GilShop event 0x4002F), was confirmed from offline game sheets. Actual vendor UI behavior remains unverified.

1.0.1.0 UI: top transport stays outside the scrollable table; one row per registered item, 30 rows submitted per page, frozen header/name columns. Search and selection are local only. Paused execution performs no native action until resume; an in-flight transaction is reconciled before entering pause. Real-game pause/return timing remains unverified.
