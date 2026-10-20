# Market Voyager publication state

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

