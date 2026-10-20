Market Voyager 1.0.1.3 — search notification, purchase confirmation and stopped editing

- Notify the registered input callback after changing the search text, allowing the normal game UI to enable Search. No button flags are forced.
- Before accepting a purchase, validate the currently selected listing ID, item, quantity, price, tax and quality. The last-purchase record is updated after acceptance and no longer blocks the confirmation dialog.
- Persist submission state immediately before the ordinary OK click. Cancel owned, unsubmitted purchase dialogs on stop when possible; reconcile successful purchases after stopping.
- Keep settings and bulk editing available after stop. Protect only deletion/reset of the row with an unresolved transaction. Add a not-purchased recovery action for old unresolved records without increasing purchased counts or inventing resale stock.
- Preserve existing purchase caps, inventory reconciliation and registered-list bulk editing.

Clean Release: 0 warnings/errors. 943 core assertions, 534 native ABI/search/selection checks, 847 UI/glyph/lifecycle checks and 38 background renders. In-game verification of this fix remains outstanding; public preview.
