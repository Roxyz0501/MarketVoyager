# Persistence implementation — 2026-10-22

This implements the workspace persistence policy for Market Voyager 1.0.1.9. Shared specifications/templates were not edited.

## Settings and safety boundaries

- `settings-v2.json` contains ordinary preferences and list/rule definitions. `safety-v2.json` contains current purchased/unsold counts, pending purchase/sale reservations, rule membership and bounded latest world timestamps. It is a current checkpoint, not an append-only transaction history.
- Ordinary edits apply in memory immediately. One framework scheduler checks marked changes every 60 seconds. Unmarked frames do not serialize or enumerate storage. Byte equality suppresses writes after edits are reverted. Purchase checkpoints do not flush ordinary slider/text edits early.
- Tour start and resume explicitly flush changed rule definitions once before reservations can refer to their IDs. This is a referential-integrity boundary, not a per-purchase settings save. Transaction reservation, pre-confirmation and inventory reconciliation use synchronous durable checkpoints before the next native action. A failed checkpoint throws into the controller's stop path; no automatic purchase replay is introduced.
- Normal flush writes a changed safety checkpoint before changed settings. Current membership prevents old settings backups from resurrecting deleted purchase rows. A pending row absent from restored settings remains a detached ledger. Safety corruption/missing safety for an existing v2 settings file blocks buying rather than reverting counts to an older backup.
- Logout and Dispose flush marked data, except after a latched storage failure (where automatic retries are prohibited). The writer is synchronous and serialized with one lock; there is no worker, timer, server or save queue to outlive shutdown. Input subscriptions are removed, movement is stopped/cancelled through existing dependency IPC, receipt callbacks are closed and drained, then persistence/font resources are disposed. External navigation provider cancellation completion is controlled by that provider; no later movement dispatch is scheduled by this plugin.

## Atomic save, backups and bounded failure

- A unique same-directory temporary file is written using `FileStream`, `Flush(true)`, byte verification and JSON validation, then moved/replaced atomically. Handles use `using`/`finally` cleanup.
- Changed ordinary settings retain three previous valid generations using replacement/rename, not three full rewrites per save. Invalid primaries/generations are retained as `settings-corrupt-*`, never promoted as valid backups. A valid primary takes precedence; corrupt/missing primaries use the newest readable valid generation and notify. Failed or unchanged saves do not rotate generations before a primary commit.
- On failure, automatic saving and buying are latched off. The exact captured settings/safety byte arrays are enclosed in one `recovery-*.json` attempt. Both successful fallback path and double failure have seven-language messages. Ten thousand later framework ticks and normal exit do not retry.
- Explicit retry persists current in-memory state without restarting a tour. On next startup, a retained fallback is offered for explicit validation/application of settings, preserving the current safety checkpoint. An unreadable safety checkpoint requires file recovery/reload and cannot be cleared by pressing Retry. Failed in-process state cannot be replaced with an older recovery candidate.
- The previous Dalamud configuration file is read only when initializing the new store. It is not deleted or rewritten. A successful new settings file makes migration idempotent. No live configuration migration or installed DLL replacement was performed during development.

## Bounded latest-world state; daily-history policy applicability

Market Voyager does not retain an ever-growing list of transactions or visits. Each shopping list retains only the latest check/purchase timestamp per world, bounded to 512 worlds; lists are bounded to 50 and rules to 1,000 per list. This existing state is preserved, including timestamps from before/after midnight and delayed timestamps. UTC is stored; existing local-time presentation remains.

There is no all-period event history to partition, migrate or page. Therefore no daily event files or empty HistoryFiles tab are added. `StandardHistoryFilesTab.cs` is not adopted because the delegated requirement was conditional on growing visit/price-check history. The existing world-history table remains. If a full event-history feature is requested later, it must use date-partitioned records, all-date paging and the unchanged shared HistoryFiles template; it must not expand this checkpoint into an event log.

## Measured synthetic results

- Release rebuild: zero warnings/errors. 960 core assertions, 548 native checks, 38 persistence fault/lifecycle checks, 1170 UI/glyph/lifecycle checks and 53 background renders.
- Small normal fixture: 15 committed file payloads, 5,234 logical payload bytes across migration, ten unchanged periods, 600 edits, rollback of an edit, five settings changes, reservation/acceptance/result checkpoints, midnight/delayed timestamps and shutdown.
- Ten unchanged periods: zero writes/bytes. 600 edits in one interval: zero writes until the next interval, then one settings payload. A reverted edit writes zero. A reservation changes only the safety file. An ordinary preference edited during a purchase checkpoint leaves the settings file byte-identical until its normal flush boundary.
- Write/rename fault injection, simulated disk-full, an actual sharing lock, one successful alternate save, double failure, explicit retry, next-start candidate application, corrupt primary recovery, corrupt safety refusal, deleted-rule resurrection prevention, edit-during-write, ten concurrent flush requests and post-Dispose write suppression pass.
- Counters distinguish attempted file commits, committed logical payload bytes and payload bytes actually supplied to temporary streams (including failed attempts). They exclude filesystem metadata, OS/device write amplification and SSD physical writes. Three-generation rotation moves existing data rather than copying every generation. No physical SSD I/O claim is made.
- Unchanged rendering remains zero-save. Seven-language recovery messages and existing history/settings screens are rendered without using the game window. Actual game logout/exit, live disk failures, dependency cancellation and transactions remain unverified.
