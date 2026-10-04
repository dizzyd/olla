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

The result is then capped at `IrrigationTarget` — see below, and note that the cap
is what makes the final `Math.Min` against vanilla's own answer matter.

## IrrigationTarget

A value in `ModConfig/olla.json` (beside `RainLitresPerHour`, below), capping **both** mechanisms. That is the
point of it being one number: capping only the watering leaves overlapping ollas
walking the floor towards 100% anyway, and the floor is the half that never decays.

```
active irrigation:  deficit = IrrigationTarget - MoistureLevel
moisture floor:     blended = Math.Min(1 - dryness, IrrigationTarget)
```

Requested for [Farming Revamped](https://mods.vintagestory.at/show/mod/52211), which
gives crops a moisture *band* and damages anything held too wet — it caps its own
watering can at 0.75 for that reason, and kills waterlogged rye in about nine days.
An uncapped olla pins its 5x5 above that permanently, so the complaint was not
"too easy", it was "my crops die". ~0.6 suits it.

Three things not to undo:

- **The cap can make the blend worse than vanilla.** Uncapped, blending only ever
  added moisture, so the old `Math.Min(__result, ...)` was a formality. Capped it is
  load-bearing: the patch now returns early when the blended distance is no better
  than what vanilla found, so an olla next to a pond can never *dry* the soil.
  `ACappedOllaNeverDriesOutNaturalWater`.
- **0 means off, and there is no useful value just above it.**
  `GetGrowthRate` computes `Math.Max(0.01, moistureLevel * 100 / 70 - 0.143)`, whose
  inner term crosses zero at 0.1001 — so 0.01 and 0 give an identical growth rate.
  A small minimum would be a disabled mod that looks enabled. Warned about, not
  clamped.
- **Server-side only**, like the patch, and for the same reason — farmland moisture
  is simulated there. Loading it on the client too would leave an editable file that
  does nothing on someone else's server.

`OllaConfig.Current` is settable so the suite can swap a target without restarting a
world; the tests reset it in `[BeforeEach]` *and* `[AfterEach]` because it is static.
That deliberately insulates them from whatever is in the config file, which is why
"does the JSON actually reach the mod" is verified by reading the startup log line
rather than by a test.

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

**Each request is also capped at what the olla still holds.** The loop only checks
that *some* water is left before each block, so the last block watered used to get its
full share regardless and the level clamped at zero. Harmless while that happened once
per olla; rain made it recur, every few millilitres into an empty pot buying a full
watering - about 57 times what fell, measured. The order is unchanged: still nearest
first, so a trickle goes to the closest block. `ADrizzleIntoAnEmptyOllaWatersNoMoreThanItCollected`.

## Watering cans

A can is not an `ILiquidSource` — it stores seconds of pouring (`wateringSeconds`), and
its `OnHeldInteractStart` claims the click before `BlockOllaFired.OnBlockInteractStart`
runs. So it is a second Harmony patch (`WateringCanPatch.cs`), prefix + postfix around
`BlockWateringCan.OnHeldInteractStep`: vanilla drains the can as usual, the postfix
converts the drained seconds to litres and refunds whatever a full olla could not take.

5 L per full can is our number, not vanilla's — the only volume vanilla implies is that
refilling a can from a placed bucket is meant to take 5 L. It does not: 1.22.0 computes
`(int)(5 / itemsPerLitre)`, which is 0, so that refill takes nothing and leaves the can
empty. The intent is the anchor, not the behaviour.

**Unlike the farmland patch, this one runs on both sides**, and that is load-bearing.
Vanilla drains the can on the client too, so a refund made only on the server leaves the
client's copy running dry: the pour stops, the server's count comes back, it restarts,
over and over, with water in the can throughout. Only the server touches the olla; the
client makes the same refund from its own copy of the olla, which can trail by a sync as
the olla fills — `OnHeldInteractStop`'s `MarkDirty` settles that on release.
`ANearlyEmptyCanKeepsPouringOverAFullOlla` is the test that notices.

**The refund also has to undo vanilla's return value.** The step that runs a can dry
returns vanilla's stop-pouring result *before* the postfix puts the water back, so the
postfix sets `__result = true` when it refunded a can that vanilla had emptied. Only that
path: the drain is followed by no other return. Without it a can holding one step's worth
or less stops over a full olla on every press. The `ACanHolding...OneStep...` tests step
the method directly at a fixed 0.05 s, headless, because frame-timed input never lands on
the boundary reliably; `ACanThatEmptiesIntoAnOllaStopsPouring` holds the other side.

What is left is drift, not a loop. As the olla fills, the client's copy of it trails the
server's, so the client drains a little past the server: measured 0.03-0.07 s of can in
singleplayer, and in multiplayer it scales with latency. If the server is left holding
less than that, the client's can reads empty and the pour visibly ends as the olla
fills, with the server still holding that sliver - which nothing pushes back mid-hold, so
no stop/restart cycle (a probe saw 0 refill jumps over 4 cases; one 3-sample blip). The
release resyncs it. Predicting the olla's level on the client would close it; not
judged worth a second copy of the olla's state.

Both sides means the singleplayer double-registration trap applies in full, so the patch
is applied once per process behind a static guard, under its own Harmony id
(`com.dizzyd.olla.wateringcan`) so that either side's `Dispose` removes only what it
applied. `TheCanPatchIsRegisteredExactlyOnce` guards it. The farmland patch is applied
by class rather than `PatchAll()` for the same reason — `PatchAll()` would drag the other
patch along to whichever side called it.

The tests' 5 L is written out, not read from the mod, so a changed conversion fails them.

**Water type is not checked.** Vanilla refills a can by right-click only from `water`
blocks, but a can left lying in any liquid refills too (`OnGroundIdle`, `FeetInLiquid`) —
salt water included — and the stack keeps no record of which. So a can is a way round
the clean-fresh-water rule, and **that is accepted on purpose** (decided Oct 2026): cans
count as fresh water. Closing it would mean tracking provenance on the stack, which vanilla
does not do, to stop a deliberate trick that yields 5 L at a time. Do not add a water
check here without revisiting that.

The can tests hold the use button for the whole 32-second pour, and about one full-suite
run in four or five on vsclient used to see it let go partway through. It is the window
losing focus: `ClientMain.OnFocusChanged(false)` clears the held button without raising
`MouseUp`, and nothing else in the client clears it. Caught in the act - button up, no
`MouseUp` recorded, both sides agreeing on the can - and reproduced by invoking that
handler mid-pour. `PourUntil` now presses again when the button reads up and logs
"released under the test"; the pour still has to deliver the whole can. A held button
that survives focus loss would be the real fix, and it belongs in vstestkit.

## Rain

An olla with nothing rain-blocking above it collects `RainLitresPerHour` (config,
default 0.5) per game hour of full precipitation, scaled by the level. Surface and buried
alike; only burial gates irrigation. Exposure is vanilla farmland's own test,
`GetRainMapHeightAt <= Pos.Y` - the olla is itself the topmost rain-blocking block when
uncovered - and it is judged *now*, as vanilla does, not for each past hour.

It is part of the catch-up, not a separate tick: each 3-4 hour interval collects its
rain and then irrigates, so an olla that was empty when a long unloaded stretch began
works again within it. Rain is sampled an hour at a time with
`WeatherSystemBase.GetPrecipitation(pos, totalDays, climate)`, the same way
`BlockEntitySoilNutrition` counts rain on farmland since its last update. The
interleaving is what `AnEmptyOllaRefilledByRainIrrigatesInTheSameStretch` checks; adding
the whole stretch's rain at the end fails it.

0.5 is a compromise, chosen in Oct 2026. Through a real olla's 5-10 cm neck it would
need 2.5-10 in/h of rain, against about 1 in/h for heavy rain; a rate true to the neck
is about a tenth of it and does nothing visible in play. 0.5 reads as a small catchment
around the mouth: a day-long downpour adds about 12 L.

Snow counts as rain, as it does for farmland - vanilla's precipitation level does not
distinguish them.

vstestkit forces precipitation to 0 for the session, which the farmland tests depend on.
`OllaRain` sets its own and puts 0 back in `[AfterEach]`. `RainLitresPerHour` that is
NaN or Infinity (Newtonsoft reads `1e100` as Infinity, and `0 * Infinity` is NaN) falls
back to the default with a warning; a large finite rate is left alone. That override applies to any
moment, past or present, so the suite cannot tell whether rain is sampled at the right
past hours - only that it is counted.

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

Two about the world:

- **Water flows.** A test that places a pond and measures twice will watch it creep
  a block closer between readings. Wall it in.
- **Unsupported rock does not stay.** Granite set directly on farmland, or two blocks
  above it over air, is gone a tick later - most likely 1.22's `UnstableRock`
  collapse, though the mechanism was not traced. Granite resting on an olla stays.
  `OllaRain` roofs farmland with planks and asserts the rain map, because a vanished
  roof is how the irrigation-from-rain test first passed for the wrong reason.

## Two things that fail silently

`UpdateSoilMoisture`'s `dynamic` + `catch { return 0f; }` turns any farmland API
change into a no-op rather than an exception (README explains why it is written
that way). `BuriedWateredOllaMoistensNearbyFarmland` is the test that notices —
keep it.

Accepted water is matched on the collectible's **code path**, so the domain does
not matter and Hydrate or Diedrate's replacements match the same way. Attribute
sniffing is not an option here: H&D's `whenSpilled` action is `DropContents`, not
a water block, so there is nothing to inspect.
