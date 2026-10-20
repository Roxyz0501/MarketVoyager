Market Voyager 1.0.1.6 — configurable purchase delay and list deletion

- Settings now provides a persisted purchase delay from 50 to 2000ms, defaulting to 100ms for both new and existing configurations. Reset to 100ms is available; changes apply to subsequent scheduling during a run.
- Apply the setting to search, listing selection, confirmation and result checks. Checkbox settling retains a minimum 250ms; movement, error backoffs, outcome timeouts and inventory reconciliation requirements remain unchanged.
- List settings can delete an entire shopping list after an inline confirmation. Deleting the last list creates an empty one. Unresolved purchase/sale ledgers remain available until reconciliation; other lists and their counters are preserved. Deletion is unavailable while running.
- All controls and help are localized in seven languages. No added external requests.

Actual in-game timing remains unverified; public preview.

1.0.1.6 validation: clean Release, zero warnings/errors; 943 core assertions, 542 native checks, 1005 UI/glyph/lifecycle checks and 39 background renders. Japanese settings and deletion-confirmation images inspected.
