Market Voyager 1.0.1.2 — market search startup fix

- Fix the search-mode gate that prevented entering an item name after opening the market. Initial/previous-category modes now accept name entry once the actual widget is ready.
- Let the input update before activating the normal search button. Dispatch its registered click event, skipping hover/press events and unrelated listeners.
- Accept complete matching listing rows even when WaitingForListings remains set. Incomplete, updating, scrolling and still-loading empty results remain blocked.
- Preserve the retry budget when reopening the same item. Record bounded retry diagnostics without player/retainer names or IDs.
- Existing registered-list bulk editing and purchase/sale accounting are preserved.

Release build: 0 warnings/errors. 931 core assertions, 520 native search/event/readiness checks and 812 offline UI regression checks (31 background renders). Actual game behavior remains unverified; public preview.
