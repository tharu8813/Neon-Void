# Mythic plugin concepts — DESIGN PROPOSAL ONLY

**Status: NOT APPROVED.** These concepts are not registered in catalog.v1.json, do not have immutable plugin IDs, rarity weights, save migration, or enabled combat effects. User approval, B/A/E contracts, balance tests and accessibility review are required before implementation.

## Design constraints from GDD
- A mythic plugin should significantly change the game loop or combat decisions with one pickup, not merely add larger passive percentages.
- All four rarities exist in the design, but the 27-entry catalog includes **zero approved mythics**. Do not insert fake cards into the RNG pool or pity counter to make test coverage appear complete.
- E owns durable progression and receipts. Mythics must never generate new persistent rewards, plugin IDs, XP or reroll currency outside an approved E transaction.
- A valid hit can create legal multi-hop effects, but derived attacks cannot recursively loop on their own signature. B is the sole HP/status/projectile simulation owner.

## Proposed archetype 1: Quarantine Kernel (격리 커널)
A deliberate defense-first alternative to glass-cannon builds. When the player takes a major hit, a local 'quarantine zone' would form around the player for a short time. Nearby non-boss enemies have slower movement and projectiles entering the zone are weakened. Moving out of the zone ends protection early.

**Gameplay choice:** hold ground to stabilize a dangerous section, or leave the area to chase loot and sacrifice safety. **Technical request:** B owns spatial status rules, projectile interception, immunity and stacking; C supplies a one-shot trigger with per-run cooldown and no self-proc. **Unresolved:** damage threshold, radius, duration, boss exception, whether it interferes with DoT, VFX readability.

## Proposed archetype 2: Sandbox Fork (샌드박스 분기)
Create a temporary phantom that copies the player's direction of attack with reduced damage, but never counts as a real actor for mission progress, kill rewards, extraction, XP or plugin grants. Phantom hits may trigger **no additional on-hit/proc effects** unless B explicitly approves separate ancestry tracking.

**Gameplay choice:** build a corridor of crossfire and sacrifice mobility, instead of a passive permanent damage multiplier. **Technical request:** B owns actor/projectile count, physical collisions, and event ancestry; A owns reduced-effects rendering. **Unresolved:** trigger skill, cooldown, lifetime, projectile cap, interaction with lasers/boomerangs.

## Proposed archetype 3: Emergency Rollback (비상 롤백)
A single once-per-run defensive rewind of the player's **combat-only** position and a bounded portion of HP when a fatal hit would occur. This does **not** rewind E receipts, RNG, cards, boss kills, world rewards, mission clock, corruption, or stored data.

**Gameplay choice:** temporary second chance for high-risk extraction, not infinite retry. **Technical request:** B must resolve lethal-damage interception and collision-safe destination; E must distinguish one combat-use flag from reward transaction rollback. **Unresolved:** whether a boss finisher can trigger it, invulnerability window, HP floor, one-use scope, shutdown/recovery semantics.

## Approval and implementation gates
1. User selects/edits the concept and explicitly approves the effect, risk/reward and boundaries.
2. A/B/C/E agree on immutable IDs, events, cooldowns, rarity/pity interactions and versioned save compatibility.
3. Run C tests for deterministic RNG/cards/levels, B tests for non-cyclic proc events and boss exceptions, and E receipt/recovery tests; verify actual Windows gameplay, audio/VFX and performance.
4. Only then add approved mythics to the catalog, UI and reward pool. **Until that point: mythic count remains 0 and mythic draw weight remains 0.**
