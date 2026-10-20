# References and dependencies

- [Lifestream](https://github.com/NightmareXIV/Lifestream), NightmareXIV: public IPC names/signatures for availability, Limsa gateway selection, travel and abort. No implementation or binary redistributed. A repository-root LICENSE / LICENSE.md was not available when checked; no redistribution right is assumed.
- [ffxiv_navmesh](https://github.com/awgil/ffxiv_navmesh), awgil / veyn: public cancellable pathfinding and movement IPC contract. No implementation or binary redistributed. No repository-root LICENSE was available when checked; no redistribution right is assumed.
- [Dalamud](https://github.com/goatcorp/Dalamud): SDK and public services, used as host-provided references. No Dalamud binary is included in the release.
- [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs): MIT, native UI / inventory type definitions accessed through the host-provided SDK assembly. No assembly or upstream implementation is vendored.
- Lumina / Lumina.Excel: host-provided game-data access. No game-data files or library binaries are redistributed.
- Noto Sans CJK: SIL Open Font License 1.1; loaded from Dalamud's own asset-font API, not bundled.
- [Market Sweep](https://github.com/Roxyz0501/MarketSweep), Roxyz0501, AGPL-3.0-only: conceptual reference for normal listing-click/confirmation flow and recognition of localized purchase dialogs. No repository clone, source file, or implementation copied.
- StandardPluginTabs.cs: Roxyz0501's canonical shared template, included verbatim.
- Tests/UI/Renderer.cs and the test font-host approach: reused from Roxyz0501's Gil Ledger offline verification utilities. These are test infrastructure, not external plugin runtime code.
- MarketVoyager/images/icon.png: original generated artwork, no third-party/game artwork used. Built-in image generation, subsequently resized to 512×512 for packaging. Prompt recorded in ARTWORK.md.

- Retainer Listing Helper / RetainerRecall, Roxyz0501: reference for ordinary inventory context-menu labels and UI ownership validation. No runtime code file copied. Native menu selection here uses the host-provided AddonContextMenu method.

- [Gil Ledger / MarketLedger](https://github.com/Roxyz0501/MarketLedger), Roxyz0501, MIT: conceptual UI reference for fixed table headers, aligned filters and scrollable data grids in 1.0.1.0. No UI source file copied.

- [Gil Beacon](https://github.com/Roxyz0501/GilBeacon), Roxyz0501, Apache-2.0: its recorded read-only investigation established that the market's WaitingForListings flag can remain set after rows are populated. Referenced that observation when implementing independent readiness checks; no source code copied.

- [ECommons](https://github.com/NightmareXIV/ECommons), NightmareXIV contributors, MIT: consulted its addon-button event-dispatch behavior. No code copied or dependency added. Market Voyager keeps validation of registered ownership and enabled state.

Lifestream gateway return (1.0.1.4): references the public `GetActiveAetheryte` IPC and provider route behavior to wait for Limsa main aetheryte recognition before requesting a world change. Provider source: https://github.com/NightmareXIV/Lifestream/blob/main/Lifestream/IPC/IPCProvider.cs . No provider implementation copied.

1.0.1.8 references Lifestream's public `Teleport(uint, byte)` contract (destination 8, sub-index 0), and vnavmesh's `Path.GetAlignCamera` / `Path.SetAlignCamera` IPC. The provider persists the latter setting, so only an owned movement changes it and cleanup restores the prior value. Reviewed provider and movement source: https://github.com/awgil/ffxiv_navmesh/blob/6fc80725eb8290472eee433fc4be7ee06ec79357/vnavmesh/IPCProvider.cs and `Movement/FollowPath.cs`. Reviewed FFXIVClientStructs' ordinary `TargetSystem.InteractWithObject(GameObject*, bool checkLineOfSight)` signature and Lifestream's use of that handler without camera sight checking. No implementation copied, new runtime dependency, hook or packet sender added.
