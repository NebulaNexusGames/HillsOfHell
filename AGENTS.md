# Project rules

## Hand items: never destroy the player's hand version

**Directive from the project owner. Do not relax this.**

Any item version created for the player's hand (the `handVariant` /
runtime hand copy produced by `PlayerHand` / `PickupItem`) must **never**
be destroyed. The player has to see the raised item in their hand.

When a one-time item is consumed it disappears **only for the player**:

- never call `Destroy(...)` on the hand variant, and never destroy the asset it
  was instantiated from;
- during the use animation the hand variant must stay visible — block dropping
  with `PlayerHand.SetDropBlocked(true)` until the animation finishes;
- once the use sequence has finished, release the item from the hand via
  `PlayerHand.ReleaseUsedItem()` so the player is not left holding a used item;
- the hand variant object itself stays alive and is simply hidden/kept, and is
  re-shown the next time the player picks that item up (`ShowHandVariant`
  re-activates it);
- the world source object may be deactivated the same way, never destroyed while
  the hand reference is still needed.

Do not reintroduce `Destroy(gameObject)` / `Destroy(pickup.gameObject)` in item
`Consume()` paths. Prefer `SetActive(false)` plus a used/consumed flag, and
release the hand through `ReleaseUsedItem()` instead of destroying anything.
