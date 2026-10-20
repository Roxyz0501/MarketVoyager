# Market Voyager publication state

## 2026-10-21 — Market Voyager 1.0.1.6 configurable wait and list deletion

- Release: https://github.com/Roxyz0501/MarketVoyager/releases/tag/v1.0.1.6
- ZIP: https://github.com/Roxyz0501/MarketVoyager/releases/download/v1.0.1.6/MarketVoyager-1.0.1.6.zip
- Source/tag: `c665015bbf20f6364c6b64ce1c88481629a510b0`.
- ZIP: 510,515 bytes; SHA-256 `ED285F20C6484BD7CDE69645873D180B17C8D60911847BB71443ABE6C866FCA3`; eight allowlisted files.
- Purchase delay is persisted and editable in Settings, 50–2000ms, default/legacy fallback 100ms, with a reset button. Live changes apply to subsequent purchase scheduling. Checkbox minimum 250ms, inventory reconciliation, error backoffs and timeouts remain.
- List settings offers inline-confirmed whole-list deletion while stopped. Other lists are retained; deleting the final list creates an empty list. Both unresolved purchase/sale ledgers survive deletion and restart until reconciliation.
- Clean Release zero warnings/errors; 943 core assertions, 542 native checks, 1005 UI/glyph/lifecycle checks and 39 renders. Delay bounds/migration/persistence and all five phases at four delays tested; whole-list deletion, pending ledgers and last-list fallback tested. Japanese settings/confirmation images inspected; canonical tabs and source/package scans passed.
- Public repository/icon HTTP 200 (image/png); anonymous ZIP hash/metadata verified. Shared index `de96201b851e54c7abb221758e672bf50c0bc4a3`, connector and pinned raw URL verified 1.0.1.6; 12 other entries preserved. Main raw initially cached 1.0.1.5.
- Published under standing authorization using connector for source/index/records, CLI only for unsupported release/assets and local history sync. No game input or installed DLL replacement. Actual in-game timing remains unverified.

## 2026-10-21 — Market Voyager 1.0.1.5 closer market access and faster purchasing

- Release: https://github.com/Roxyz0501/MarketVoyager/releases/tag/v1.0.1.5
- ZIP: https://github.com/Roxyz0501/MarketVoyager/releases/download/v1.0.1.5/MarketVoyager-1.0.1.5.zip
- Source/tag: `849d2393503c7123d7b09beae18db782f2dcf186`.
- ZIP: 507,988 bytes; SHA-256 `2713D96F7BA13DDE30E9FD6C53DCF15B31CE6D87F58AEC12633F0A1D99820339`; eight allowlisted files.
- Stop within 2m instead of 3m; settle one second and attempt ordinary market interaction up to three times at two-second intervals without renewing the Open timeout.
- Purchase-stage checks/transitions use 100ms; checkbox updates wait 250ms. Removed fixed two-second post-purchase delay. Receipts only wake reconciliation; the exact inventory increase must be observed before the reservation clears, and the next action waits for the purchase dialog to close. No change to normal UI routes, caps, persistence or bounded error retries.
- Clean Release zero warnings/errors; 943 core assertions, 542 native checks, 929 UI/glyph/lifecycle checks and 39 background renders. Scheduler timeout and early-receipt regressions passed. Canonical tabs and source/UTF-8/UTF-16 package scans passed.
- Public repository/icon HTTP 200 (image/png); anonymous ZIP hash and manifest matched. Shared index commit `d39ce50c70cd3319099706165237f8304edc5c9e`, connector, main raw and pinned raw verified 1.0.1.5; 12 other entries preserved.
- Published under standing authorization. Connector used for source/index/records; CLI only for unsupported Release/assets and local history sync. No installed DLL replacement or game input. Actual game position and throughput remain unverified; timing values describe scheduling, not measured purchases per second.

## 2026-10-20 — Market Voyager 1.0.1.4 listing eligibility and walking return

- Release: https://github.com/Roxyz0501/MarketVoyager/releases/tag/v1.0.1.4
- ZIP: https://github.com/Roxyz0501/MarketVoyager/releases/download/v1.0.1.4/MarketVoyager-1.0.1.4.zip
- Source/tag: `9a8b0d93f584d78fe1f00f051122ed4641e16938`.
- ZIP: 507,094 bytes; SHA-256 `00472813E52F1C80C8FBA9C9BD6F96D30C400407DEBA24DEC56FBD79BFE37C08`; eight allowlisted files.
- Search focuses the ordinary input collision node before typing. Already-open matching results advance to eligibility. Pre-dispatch failures no longer exclude the listing or renew the deadline, and perform no reservation disk writes. Last offer/candidate counts are visible.
- Stopped single/bulk deletion retains only unresolved transaction ledgers until reconciliation. List settings keep a fixed anchor. World changes require walking back to Limsa main aetheryte and Lifestream recognition of aetheryte 8; return-to-gateway is false.
- Clean Release zero warnings/errors; 943 core assertions, 542 native checks, 917 UI/glyph/lifecycle checks, 39 renders. Sixty popup frames remained stationary; Japanese image inspected. Canonical tabs and source/UTF-8/UTF-16 package scans passed.
- Public repository/icon HTTP 200 (image/png); anonymous release download matched hash and manifest. Shared index `c9cebfabd3031bbe7761091b828d750e3a8d0d6b`, connector, main raw URL and pinned raw URL verified 1.0.1.4; 12 unrelated entries preserved.
- Published under standing authorization. Connector used for source/index/records; CLI only for unsupported release/assets and local Git synchronization. No installed DLL replacement or game input. Actual in-game purchase/search/walking behavior remains unverified.

## 2026-10-20 — Market Voyager 1.0.1.3 search/confirmation and stopped editing

- Release: https://github.com/Roxyz0501/MarketVoyager/releases/tag/v1.0.1.3
- ZIP: https://github.com/Roxyz0501/MarketVoyager/releases/download/v1.0.1.3/MarketVoyager-1.0.1.3.zip
- Source/tag: `8d31c2778e61e258ce8fd102eeb333871ce3bc8d`.
- ZIP: 504,015 bytes; SHA-256 `21B3E5344B5D05791B61C8B9F4EFC488F7BD0E7BB69378BD81272A326888459D`; eight allowlisted files.
- Search sends the registered TextChanged callback with the normal widget buffers/owner. Pre-OK validation compares the selected listing, not the last-purchase record populated after acceptance. Submission state is persisted before the ordinary OK event; disabled buttons remain disabled.
- Stopping unlocks settings and bulk editing; only deletion/reset of the unresolved row is protected. Owned unsent dialogs are canceled when possible. Stopped inventory reconciliation credits successful purchases; confirmed cancellations and an explicit legacy not-purchased recovery path do not inflate purchased counts. Conservative manual counts do not create resale stock.
- Clean Release zero warnings/errors; 943 core assertions, 534 native ABI/search/selection checks, 847 UI/glyph/lifecycle checks and 38 renders. Inspected Japanese stopped-pending screen. Canonical tabs, source and UTF-8/UTF-16 binary scans passed.
- Read-only installed-client analysis confirmed the late last-purchase update. No game code/disassembly/data redistributed. No game input or installed DLL replacement performed; live-game confirmation remains outstanding.
- Public repo/icon HTTP 200 (image/png); anonymous ZIP hash matched. Shared index commit `16e35611c4e35740ba6e0f5ac371d7b2541b749a` and connector verified 1.0.1.3; 12 other entries preserved. Main raw URL initially cached 1.0.1.2; pinned URL served 1.0.1.3.
- Published under standing authorization. Connector used for source/index/records; CLI only for unsupported Release/assets and local history synchronization.


## 2026-10-20 — Market Voyager 1.0.1.2 search startup fix

- Release: https://github.com/Roxyz0501/MarketVoyager/releases/tag/v1.0.1.2
- ZIP: https://github.com/Roxyz0501/MarketVoyager/releases/download/v1.0.1.2/MarketVoyager-1.0.1.2.zip
- Source/tag: `21840366bae4c505501551f9a0cd1779e87d9e68`.
- ZIP: 500,575 bytes; SHA-256 `11D0900CB8F5B682446EA96422DC6F60EC58AAC7B3EBD39B2C896A1FE8F978A2`; eight allowlisted files.
- Removed the Normal-only gate before name entry; input update precedes the ordinary registered search-button click. Button event lookup skips hover/press and other listeners. Complete matching listings no longer wait on a stale WaitingForListings flag; incomplete/loading UI remains blocked. Reopening the same item preserves the retry budget. Bounded, non-personal diagnostic logging added.
- Clean Release: zero warnings/errors; 931 core assertions, 520 native-layout search/event/readiness regressions, 812 UI/lifecycle/retry checks and 31 background renders. Canonical tabs and source/binary scans passed.
- Public repo/icon HTTP 200 (image/png); anonymously downloaded ZIP hash matched. Shared index commit `164683b44a9161e237d77718d212ded779693ff6` and connector verified 1.0.1.2 with all 12 other entries preserved. A concurrent index update was reread and retained. Main raw URL initially served cached 1.0.1.1; pinned URL served 1.0.1.2.
- Published under standing user authorization. Connector used for source/index/records; CLI only for unsupported Release/assets and local Git synchronization. No installed DLL replacement or game input. The reported live state could not be inspected because no game process was present; actual in-game fix confirmation remains outstanding.


## 2026-10-20 — Market Voyager 1.0.1.1 registered-list bulk editing

- Release: https://github.com/Roxyz0501/MarketVoyager/releases/tag/v1.0.1.1
- ZIP: https://github.com/Roxyz0501/MarketVoyager/releases/download/v1.0.1.1/MarketVoyager-1.0.1.1.zip
- Source/tag: `13ceec38afa148c1f724abdcc64a85af3edbb8ac`.
- ZIP: 499,075 bytes; SHA-256 `DA60D4EB357BE9DD117608DAE5FA801BD72250880514D9D9D45A0E6EB3447205`; eight allowlisted files.
- Visible Batch edit button above registered items; explicit selected/all-registered scope, including off-page/search-excluded rows. Fields start unchecked and only opted-in fields change. Dedicated NPC pricing also supports both scopes. Purchased and unsold counts preserved.
- Release zero warnings/errors; 931 core assertions, 810 offline UI/glyph/lifecycle/scope checks, 31 renders. Direct editor tested against 45 registered rows; opening it changes no saved values. Canonical shared tabs and source/binary scans passed.
- Public repo/icon HTTP 200, image/png; anonymous ZIP hash matched. Shared index commit `7c6a60057d0df844444ab8c3c37270dabae1c080`: connector/pinned URL verified 1.0.1.1 and all 12 other entries preserved. Normal main URL initially returned cached 1.0.1.0 (HTTP 200).
- Published under the user's standing instruction, connector used for source/index/records and CLI only for unsupported Release/asset operations. Actual game behavior remains unverified. No installed plugin DLL replaced.


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

