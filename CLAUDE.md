# olla

Buried ceramic pots that irrigate nearby farmland.

Environment, build, and release conventions are workspace-wide and live in
`../CLAUDE.md` — .NET and game installs (via Cairn), the `net10.0` retarget, the
`dependencies.game` minimum-version trap, per-repo tag and bump styles. This file
is only what is specific to olla.

---

## Two mechanisms, and they are independent

Almost every confusion about this mod comes from not separating them.

**1. Active irrigation** — `BlockEntityOllaFired` ticks, walks the 5x5 area, and
calls `farmland.WaterFarmland(...)`. This *adds* moisture and consumes water.

**2. The moisture floor** — a Harmony postfix on
`BlockEntitySoilNutrition.GetNearbyWaterDistance` makes farmland see a buried,
watered olla as a water source. This *sets a minimum* and costs nothing.

The floor is what survives a chunk unload: without it, farmland dries out during
catch-up before the olla's own tick gets a chance to run. It is also what a player
actually sees most of the time, because ollas run dry faster than the floor decays.

A change to one is not a change to the other. Blending is entirely mechanism 2.

## The vanilla moisture model

From `VSSurvivalMod/BlockEntity/BESoilNutrition.cs`, worth having in front of you:

```csharp
minMoisture = GameMath.Clamp(1 - waterDistance / 4f, 0, 1);   // 0/1/2/3 -> 100/75/50/25%
totalHoursWaterRetention = HoursPerDay * 4;                   // dries 1/96 per hour
moistureLevel = Math.Max(minMoisture, moistureLevel - hoursPassed / totalHoursWaterRetention);
```

Two things that are easy to get wrong:

- **`waterDistance` is a `float`.** That is the whole reason blending needs no
  patch to vanilla's arithmetic — any moisture floor can be expressed as a
  fractional distance and handed back.
- **`WaterFarmland(dt)` adds `dt / 2`**, not `dt`. See *Water accounting*.

Vanilla searches +/-4 for water and reports `Found` only under 4.

## Blending

Sources combine as a probabilistic union — each wets whatever share the others
left dry — and the result is converted back to a fractional distance:

```
dryness  = (1 - m_vanilla) * (1 - m_olla1) * (1 - m_olla2) * ...
moisture = 1 - dryness
__result = (1 - moisture) * 4        // which is just dryness * 4
```

Verified in-game (`tests/OllaBlending.cs`): two ollas 2 away give 75%, three
87.5%, four 93.75%; a pond 3 away plus an olla 2 away gives 62.5%. Asymptotic on
purpose — a 100% floor still needs a source right alongside.

A source found at distance 99 (nothing) clamps to zero moisture and leaves the
product untouched, so the vanilla distance can be folded in unconditionally.

## The olla cache

Keyed per farmland position, holding every irrigating olla in range. Two rules:

- **Re-validation** catches an olla that has gone or run dry, on read.
- **A generation counter** catches an olla that has *appeared*. Validation alone
  cannot — nothing in a stale entry knows about a block that is not in it.

That asymmetry is real: removing the counter fails exactly one test
(`ASecondOllaIsSeenImmediately`) and nothing else. Ollas bump it on placement,
removal, burial, and the transitions into and out of holding water. A bump clears
the whole dictionary, which also stops it growing without bound.

## The Harmony postfix

Two constraints on it, both easy to undo by accident:

- **Server side only.** `PatchAll()` in `Start()` registers it twice in
  singleplayer — see *Harmony in singleplayer* in `../CLAUDE.md` for why.
  `ThePatchIsRegisteredExactlyOnce` guards it.
- **Farmland only.** It early-returns unless `__instance is BlockEntityFarmland`,
  because `BlockEntityBerryBushFarmland` shares the base class and should not be
  affected.

## Water accounting

`WaterFarmland(dt)` applies `dt / 2`, so a request lands as half that much
moisture. `UpdateSoilMoisture` bills the delivery, not the request:

```csharp
float delivered = Math.Min(wateringAmount * WaterFarmlandDeliveryFactor, moistureDeficit);
return delivered * LitersPerIntensity;      // 1.25 L per unit of moisture
```

It billed the request until Aug 2026, which charged double — 1.25 L bought half a
unit, so a 60 L olla saturated its area once where the tooltip promised twice.

`WaterFarmlandDeliveryFactor` hardcodes vanilla's `dt / 2`. That coupling is the
price of not measuring the delta directly — `WaterFarmland` also runs
`updateMoistureLevel`, which applies drying *and* the `minMoisture` floor, and
that floor is the olla's own doing and costs nothing, so farmland sitting below it
would bill the olla for water it never poured.
`WateringIsBilledAtWhatItDelivers` pins the ratio, so if vanilla changes the
factor a test fails rather than everyone's water use silently doubling.

## Geometry

- The olla sits at the **same Y** as the farmland it waters (`AddCopy(dx, 0, dz)`
  in both the irrigation loop and the patch search). It is not on top of it.
- Active irrigation weights by **Manhattan** distance; the patch uses
  **Chebyshev**, to match vanilla. So a corner of the 5x5 is Manhattan 4 (quarter
  rate) but Chebyshev 2 (50% floor). Intentional or not, it is longstanding — do
  not "fix" one to match the other without deciding which is right.
- Burial is one-way: `SetBlock` to the `-buried` variant, block entity data copied
  across by tree attributes. `IsBuried()` reads the block's `state` variant.

## Testing

`README.md` has the commands and why the `--client` form matters before a release.
The vstestkit skill has the harness rules (tick listeners on a real-time clock,
`TickNow`, weather off by default).

Two traps are specific to farmland and cost real time here:

- **Farmland placed with `World.SetBlock` never ran `OnCreatedFromSoil`**, so
  `lastMoistureLevelUpdateTotalDays` is 0. The first `updateMoistureLevel` then
  dries it by the whole 96-hour retention window and swallows the first watering
  entirely — moisture reads 0 while litres visibly drain. Warm up with one
  irrigation cycle before measuring anything.
- **A newly buried olla is not seen until the farmland's next long update.**
  `ShortUpdate` reuses the cached `lastWaterDistance`; only `beginIntervalledUpdate`
  re-searches, and that needs 3-4 game hours to have passed. Advance the calendar
  between two `TickNow` calls or the floor never appears.

And one about the world: **water flows.** A test that places a pond and measures
twice will watch it creep a block closer between readings. Wall it in.

## Two things that fail silently

`UpdateSoilMoisture`'s `dynamic` + `catch { return 0f; }` turns any farmland API
change into a no-op rather than an exception (README explains why it is written
that way). `BuriedWateredOllaMoistensNearbyFarmland` is the test that notices —
keep it.

Accepted water is matched on the collectible's **code path**, so the domain does
not matter and Hydrate or Diedrate's replacements match the same way. Attribute
sniffing is not an option here: H&D's `whenSpilled` action is `DropContents`, not
a water block, so there is nothing to inspect.
