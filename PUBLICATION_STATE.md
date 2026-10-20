# Market Voyager publication state

## 2026-10-20 — Market Voyager 1.0.1.0 public preview

- Published under the user's standing publication instruction. Repository: https://github.com/Roxyz0501/MarketVoyager
- Release: https://github.com/Roxyz0501/MarketVoyager/releases/tag/v1.0.1.0
- ZIP: https://github.com/Roxyz0501/MarketVoyager/releases/download/v1.0.1.0/MarketVoyager-1.0.1.0.zip
- Source/tag commit: `a1d983a9089dd1b0db268d56efd6d5e86b9e8a66`.
- ZIP: 498,021 bytes; SHA-256 `05C4D883CA6BC47067E2C080B4ADA57E5091C083347ED6DF6B186E318333D782`; eight allowlisted files.
- One-item-per-row editable grid, frozen header/name columns, separate simplified Add items tab, compact filters and list-settings popup. Referenced Gil Ledger's table/filter layout concepts (Roxyz0501, MIT); no UI source file copied.
- Persistent top Start / Pause or Resume / Stop controls. Pause waits for an in-flight transaction/movement to resolve; resume excludes paused duration from deadlines and retains the tour cursor.
- Single/selected/all-row price, quantity, quality, NPC-minus-margin price, reset and remove operations. Dedicated NPC batch action preserves quantity limits, quality, purchased counts and sale ledger.
- Release build zero warnings/errors; 931 core assertions, 800 offline ImGui/glyph/lifecycle/controller checks, 30 renders. Production runner safe-boundary pause/resume invoked in offline host. Unchanged rendering saves zero config files.
- Canonical tabs unchanged; source/binary credential and local-path scans passed. Public repo/icon HTTP 200, image/png; anonymous ZIP hash matched.
- Shared index commit `d893ac9d704571a9e53a76c5f0a8e3bcd32386d3`: connector and pinned anonymous URL verified 13 entries with 1.0.1.0; all 12 other entries preserved. Normal main URL initially served cached 1.0.0.0.
- Actual game purchase/sale, pause timing, navigation and DC transfer remain unverified. Preview disclosure and dependency requirements retained. No installed plugin binary replaced.


## 2026-10-20 — Market Voyager 1.0.0.0 public preview

- Published at the user's explicit request. New independent Dalamud API 15 plugin by Roxyz0501, MIT; separate from Market Sweep.
- Repository: https://github.com/Roxyz0501/MarketVoyager
- Release: https://github.com/Roxyz0501/MarketVoyager/releases/tag/v1.0.0.0
- ZIP: https://github.com/Roxyz0501/MarketVoyager/releases/download/v1.0.0.0/MarketVoyager-1.0.0.0.zip
- Source/tag commit: `7273c1a0e0f09b0e32dbaeee0138659b15395280`.
- ZIP: 493,267 bytes; SHA-256 `260F07DA200C539DF9F2C4CFA0FE0A1C55302C4ECA0BB213F037A2097D18394E`; eight allowlisted files.
- Shopping lists with HQ/NQ/Any, tax-inclusive limits from NPC resale price minus a margin, category/name/price filters, multi-add, batch edits and purchased-count resets. Lifestream/vnavmesh dependency checks.
- Optional NPC resale at a free-slot threshold, including full inventory; returns to the same world's market and resumes. Purchased counts are never reduced by sales. Only fully accounted stacks of confirmed plugin-acquired items are eligible; sale journal is separate by character and quality.
- Normal purchase/sale UI events; no direct purchase packets. Inventory reconciliation, bounded same-listing purchase retries and listing-fetch reopening. Unknown sale outcomes are never automatically retried.
- Release build: zero warnings/errors. 922 core assertions, 772 native ImGui/glyph/lifecycle checks, 28 offline renders. Unchanged views save zero config files. Canonical tab source byte identity, manifest URLs, source/packaged-path and credential-pattern scans verified.
- Actual in-game purchase, sale, warnings, return/resume, navigation and DC transfer are unverified. Cross-logical-DC aetheryte travel needs a compatible Lifestream; explicit public-preview disclosure in manifest, README and Release notes.
- Anonymous public repository and PNG icon HTTP 200; downloaded release ZIP hash matched. Shared index commit `00fcd9575a8e613777d2029cacc2b6f1393d7376`: pinned and normal URLs HTTP 200, 12 entries, correct MarketVoyager version/links; all 11 prior entries preserved.
- Source, icon, index and publication-record writes used the connected GitHub integration. CLI used for unsupported repository creation and Release/asset operations. No installed plugin binaries were replaced.

