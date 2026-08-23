using System;
using System.Threading.Tasks;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Olla.Tests
{
    /// <summary>
    /// IrrigationTarget - the one config value, and the ceiling it puts on both
    /// of the mod's mechanisms.
    ///
    /// The floor is the reason this exists. Active irrigation adds moisture that
    /// then decays, so an overshoot corrects itself; the blended floor does not
    /// decay at all, and overlapping ollas walk it towards 100% no matter what
    /// the watering was aiming for. On vanilla that is a feature. On a farming
    /// overhaul that gives crops a moisture *band* - Farming Revamped damages
    /// anything held too wet, and caps its own watering can at 0.75 for exactly
    /// that reason - farmland that can never dry out kills the crop on it.
    ///
    /// So the same number caps both, and these check that it does, that the
    /// default is a no-op, and that a capped olla is never worse than no olla.
    ///
    /// All server-side, so they run headless.
    /// </summary>
    public class OllaIrrigationTarget
    {
        // Clear of the plot edge: vanilla's water search looks 4 blocks out and
        // answers "Deferred" if that reaches an unloaded column.
        static BlockPos Farmland  => P(8, 0, 8);

        // Chebyshev 2 - the edge of the range, worth a 50% floor each.
        static BlockPos EastOlla  => P(10, 0, 8);
        static BlockPos WestOlla  => P(6, 0, 8);
        static BlockPos SouthOlla => P(8, 0, 10);
        static BlockPos NorthOlla => P(8, 0, 6);

        // Adjacent: full watering rate, and a 75% floor before any cap.
        static BlockPos NearOlla  => P(9, 0, 8);

        // Chebyshev 3: inside vanilla's search, outside the olla's. Worth 25%.
        // Walled in because a water source block flows, and one that creeps a
        // block closer mid-test fails arithmetic that was never wrong.
        static BlockPos Pond => P(8, 0, 11);
        static BlockPos[] PondWalls => new[]
        {
            P(7, 0, 11), P(9, 0, 11), P(8, 0, 10), P(8, 0, 12),
        };

        const string FarmlandBlock = "game:farmland-dry-medium";
        const string BuriedOlla    = "olla:olla-fired-red-buried";
        const string Water         = "game:water-still-7";
        const string Wall          = "game:rock-granite";

        [BeforeEach]
        public void LayGround()
        {
            World.SetBlock(FarmlandBlock, Farmland);
            olla.OllaConfig.Current = new olla.OllaConfig();
        }

        [AfterEach]
        public void RestoreDefaults()
        {
            // The config is a static, so a test that left it lowered would quietly
            // change the answer for every test that ran after it.
            olla.OllaConfig.Current = new olla.OllaConfig();
        }

        [VsTest]
        public async Task TheDefaultTargetChangesNothing()
        {
            // The regression guard for the whole feature: 1.0 must leave the blend
            // exactly where it was before there was a config at all.
            Assert.Close(olla.OllaConfig.Current.IrrigationTarget, 1.0, 0.0001, "default IrrigationTarget");

            await Bury(EastOlla);
            await Bury(WestOlla);

            var (distance, result) = WaterSearch();

            Assert.Equal("Found", result, "search result with two ollas in range");
            Assert.Close(distance, 1.0, 0.001, "two ollas 2 away still blend to 75%");
            Assert.Close(Floor(distance), 0.75, 0.001, "moisture floor at the default target");
        }

        [VsTest]
        public async Task TheTargetCapsTheBlendedFloor()
        {
            // Two ollas at the edge are worth 75% uncapped. At 0.6 they are worth
            // 0.6, handed back as the distance vanilla's own curve maps to it.
            Target(0.6f);

            await Bury(EastOlla);
            await Bury(WestOlla);

            var (distance, result) = WaterSearch();

            Assert.Equal("Found", result, "a capped blend is still a water source");
            Assert.Close(Floor(distance), 0.60, 0.001, "moisture floor capped at the target");
            Assert.Close(distance, 1.6, 0.001, "0.6 expressed back as a fractional distance");
        }

        [VsTest]
        public async Task MoreOllasCannotClimbPastTheTarget()
        {
            // This is the one the config was asked for. Uncapped these four reach
            // 93.75%, and because the floor never decays that is where the soil
            // stays for good - which on Farming Revamped is a dead crop rather
            // than a well watered one. Burying more pottery must not defeat the
            // setting.
            Target(0.6f);

            await Bury(EastOlla);
            var (one, _) = WaterSearch();
            Assert.Close(Floor(one), 0.50, 0.001, "one olla, still under the cap, is unaffected by it");

            await Bury(WestOlla);
            await Bury(SouthOlla);
            await Bury(NorthOlla);

            var (four, _) = WaterSearch();
            Assert.Close(Floor(four), 0.60, 0.001, "four ollas held at the target rather than 93.75%");
        }

        [VsTest]
        public async Task ACappedOllaNeverDriesOutNaturalWater()
        {
            // The cap applies to the blend, and the vanilla distance is folded
            // into that blend - so a low target must not let an olla make farmland
            // *drier* than the pond beside it already made it. Burying pottery can
            // never be a downgrade.
            Target(0.2f);

            foreach (var wall in PondWalls) World.SetBlock(Wall, wall);
            World.SetBlock(Water, Pond);
            await Ticks(4);

            var (pondOnly, _) = WaterSearch();
            Assert.Equal(3, NearestWaterBlock(), "the pond is walled in 3 blocks away");
            Assert.Close(pondOnly, 3.0, 0.001, "vanilla distance to a pond 3 blocks away");

            await Bury(EastOlla);

            var (withOlla, _) = WaterSearch();
            Assert.Equal(3, NearestWaterBlock(), "the pond stayed put while the olla went in");
            Assert.Close(withOlla, 3.0, 0.001, "an olla below the cap leaves the pond's own floor alone");
        }

        [VsTest]
        public async Task ATargetOfZeroLeavesTheFloorAlone()
        {
            // 0 is a legitimate "turn it off" rather than something to clamp away.
            // It has to be a clean no-op, not a floor of nearly nothing.
            Target(0f);

            await Bury(EastOlla);
            await Bury(WestOlla);

            var (distance, result) = WaterSearch();

            Assert.Equal(99f, distance, "no water reported when the target is 0");
            Assert.NotEqual("Found", result, "...and the search result is left as vanilla set it");
        }

        [VsTest(TimeoutMs = 90000)]
        public async Task FarmlandSettlesAtTheTargetRatherThanSaturating()
        {
            // What a player actually reads on the block. Adjacent, so watering runs
            // at full rate and the uncapped floor would be 75% before irrigation
            // took it the rest of the way to 100%.
            Target(0.6f);

            await Bury(NearOlla);

            // Farmland only re-searches for water on its long update, which needs
            // 3-4 game hours to have passed. Short ticks reuse the distance cached
            // when the block was made - before the olla existed.
            await Irrigate(hours: 6);
            await Irrigate(hours: 6);
            await Irrigate(hours: 6);

            float moisture = Moisture(Farmland);
            Log($"moisture with IrrigationTarget 0.6: {moisture:F3}");

            Assert.Close(moisture, 0.60, 0.03, "farmland settles at the configured target");
        }

        [VsTest(TimeoutMs = 90000)]
        public async Task TheSameOllaSaturatesWhenUncapped()
        {
            // The other half of the pair: identical setup at the default target,
            // so the test above is measuring the cap and not some other ceiling.
            await Bury(NearOlla);

            await Irrigate(hours: 6);
            await Irrigate(hours: 6);
            await Irrigate(hours: 6);

            float moisture = Moisture(Farmland);
            Log($"moisture at the default target: {moisture:F3}");

            Assert.Greater(moisture, 0.85f, "the same olla uncapped takes farmland well past 0.6");
        }

        // ---------- helpers ----------

        static void Target(float value)
        {
            olla.OllaConfig.Current = new olla.OllaConfig { IrrigationTarget = value };
        }

        static float Moisture(BlockPos pos) => World.BE<BlockEntityFarmland>(pos).MoistureLevel;

        /// <summary>Bury a filled olla. 60L is a full one; 0 leaves it empty.</summary>
        static async Task<olla.BlockEntityOllaFired> Bury(BlockPos pos, float litres = 60)
        {
            World.SetBlock(BuriedOlla, pos);
            await Ticks(2);

            var be = World.BE<olla.BlockEntityOllaFired>(pos);
            if (litres > 0) be.TryAddWater(litres);
            return be;
        }

        /// <summary>
        /// Let the olla irrigate for a stretch of game time. The block entity works
        /// from the difference in Calendar.TotalHours between two firings and
        /// discards the first to take a baseline, so the calendar has to advance
        /// *between* them. TickNow fires the listeners without waiting out the
        /// 5-second real-time interval.
        ///
        /// The farmland has to be ticked as well, and not only for tidiness. Its
        /// moisture floor comes from lastWaterDistance, which is refreshed by its
        /// own long update - WaterFarmland reuses the cached value rather than
        /// re-searching. Tick just the olla and the floor never engages: watering
        /// races drying on its own and settles *below* the target instead of at
        /// it, which reads exactly like a cap that is set too low.
        /// </summary>
        static async Task Irrigate(double hours)
        {
            await World.TickNow(NearOlla);
            await Hours(hours);
            await World.TickNow(NearOlla);
            await World.TickNow(Farmland);
        }

        /// <summary>
        /// Calls the protected method the mod postfixes and reports both the
        /// distance and the search result it decided on.
        /// </summary>
        static (float distance, string result) WaterSearch()
        {
            var be = World.BE<BlockEntityFarmland>(Farmland);
            var method = typeof(BlockEntitySoilNutrition).GetMethod(
                "GetNearbyWaterDistance", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.NotNull(method, "BlockEntitySoilNutrition.GetNearbyWaterDistance still exists");

            var args = new object[] { null, 0f };
            var distance = (float)method.Invoke(be, args);
            return (distance, args[0]?.ToString());
        }

        /// <summary>
        /// Chebyshev distance to the nearest water block, over the same box vanilla
        /// searches. Water flows, so a test that places some has to be able to say
        /// whether it is still where it was put.
        /// </summary>
        static int NearestWaterBlock()
        {
            var acc = Sapi.World.BlockAccessor;
            int nearest = 99;

            for (int dx = -4; dx <= 4; dx++)
            {
                for (int dz = -4; dz <= 4; dz++)
                {
                    var at = Farmland.AddCopy(dx, 0, dz);
                    if (acc.GetBlock(at, BlockLayersAccess.Fluid).LiquidCode == "water")
                    {
                        nearest = Math.Min(nearest, Math.Max(Math.Abs(dx), Math.Abs(dz)));
                    }
                }
            }

            return nearest;
        }

        /// <summary>Vanilla's own curve, so the tests state the number a player would read.</summary>
        static double Floor(float distance) => GameMath.Clamp(1f - distance / 4f, 0f, 1f);
    }
}
