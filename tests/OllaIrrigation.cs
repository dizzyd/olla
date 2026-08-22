using System.Reflection;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Olla.Tests
{
    /// <summary>
    /// Olla irrigation, end to end in a running world.
    ///
    /// These run headless: everything here is server-side, so no client is
    /// needed and they are the same on macOS and Linux.
    /// </summary>
    public class OllaIrrigation
    {
        // Kept away from the plot edge on purpose. Vanilla's water search looks
        // 4 blocks out and bails with "at chunk edge" if that crosses into an
        // unloaded column, which would make these tests fail for reasons that
        // have nothing to do with the mod.
        static BlockPos Farmland => P(8, 0, 8);
        static BlockPos Olla     => P(9, 0, 8);   // adjacent: full watering rate
        static BlockPos FarOff   => P(8, 0, 2);   // |dz| = 6, outside the 5x5

        const string FarmlandBlock = "game:farmland-dry-medium";
        const string BuriedOlla    = "olla:olla-fired-red-buried";
        const string SurfaceOlla   = "olla:olla-fired-red-normal";

        [BeforeEach]
        public void LayGround()
        {
            World.SetBlock(FarmlandBlock, Farmland);
            World.SetBlock(FarmlandBlock, FarOff);
        }

        [VsTest]
        public async Task ModIsLoaded()
        {
            Assert.True(Sapi.ModLoader.IsModEnabled("olla"), "olla mod enabled");
            Assert.NotNull(World.Block(BuriedOlla), "buried olla block registered");
            await Task.CompletedTask;
        }

        [VsTest(TimeoutMs = 90000)]
        public async Task BuriedWateredOllaMoistensNearbyFarmland()
        {
            var before = Moisture(Farmland);

            await PlaceOlla(BuriedOlla, litres: 60);
            await Irrigate(hours: 12);

            Assert.Greater(Moisture(Farmland), before, "farmland moisture after 12h next to a full olla");
        }

        [VsTest(TimeoutMs = 90000)]
        public async Task IrrigationConsumesWater()
        {
            var be = await PlaceOlla(BuriedOlla, litres: 60);
            var before = be.CurrentWaterLiters;

            await Irrigate(hours: 12);

            Assert.Less(be.CurrentWaterLiters, before, "water left in the olla");
        }

        [VsTest(TimeoutMs = 90000)]
        public async Task AnUnburiedOllaDoesNotIrrigate()
        {
            // The state variant is the whole difference: an olla sat on the
            // surface is decoration until it is buried.
            var before = Moisture(Farmland);

            var be = await PlaceOlla(SurfaceOlla, litres: 60);
            Assert.False(be.IsBuried(), "surface olla should not report as buried");

            await Irrigate(hours: 12);

            Assert.Close(Moisture(Farmland), before, 0.001, "moisture next to an unburied olla");
        }

        [VsTest(TimeoutMs = 90000)]
        public async Task AnEmptyOllaDoesNotIrrigate()
        {
            var before = Moisture(Farmland);

            var be = await PlaceOlla(BuriedOlla, litres: 0);
            Assert.False(be.HasWater, "empty olla");

            await Irrigate(hours: 12);

            Assert.Close(Moisture(Farmland), before, 0.001, "moisture next to an empty olla");
        }

        [VsTest(TimeoutMs = 90000)]
        public async Task FarmlandOutsideTheFiveByFiveIsUntouched()
        {
            var before = Moisture(FarOff);

            await PlaceOlla(BuriedOlla, litres: 60);
            await Irrigate(hours: 12);

            Assert.Greater(Moisture(Farmland), before, "the near farmland did get watered");
            Assert.Close(Moisture(FarOff), before, 0.001, "farmland 6 blocks away");
        }

        [VsTest(TimeoutMs = 90000)]
        public async Task OllaShortensTheWaterDistanceVanillaReports()
        {
            // This is the test that earns its keep after a game update. The mod
            // postfixes BlockEntitySoilNutrition.GetNearbyWaterDistance by name,
            // and in 1.22 that method moved from BlockEntityFarmland to this new
            // base class. A patch aimed at a method that no longer exists there
            // attaches to nothing, compiles fine, and silently does nothing.
            var dry = WaterDistance(Farmland);
            Assert.Equal(99f, dry, "vanilla reports 99 when there is no water in range");

            await PlaceOlla(BuriedOlla, litres: 60);
            await Ticks(10);

            var wet = WaterDistance(Farmland);
            Assert.Less(wet, dry, "a buried, watered olla should shorten the reported water distance");
            Assert.LessOrEqual(wet, 4f, "and bring it inside vanilla's 'found water' threshold");
        }

        // ---------- helpers ----------

        static float Moisture(BlockPos pos) => World.BE<BlockEntityFarmland>(pos).MoistureLevel;

        static async Task<olla.BlockEntityOllaFired> PlaceOlla(string code, float litres)
        {
            World.SetBlock(code, Olla);
            await Ticks(4);

            var be = World.BE<olla.BlockEntityOllaFired>(Olla);
            if (litres > 0) be.TryAddWater(litres);
            return be;
        }

        /// <summary>
        /// Let the olla irrigate for a stretch of game time.
        ///
        /// The block entity ticks on a 5-second listener and works from the
        /// difference in Calendar.TotalHours between firings, discarding the
        /// first one to establish a baseline. So the calendar has to be advanced
        /// *between* two firings - advancing it before the first does nothing at
        /// all, which is a confusing way to watch a test fail.
        ///
        /// TickNow fires those listeners on demand instead of waiting out the
        /// interval. Listener intervals run on a real-time Stopwatch, so waiting
        /// costs ten real seconds per test and no amount of /time speed changes
        /// that.
        /// </summary>
        static async Task Irrigate(double hours)
        {
            await World.TickNow(Olla);      // first firing: baseline only
            await Hours(hours);
            await World.TickNow(Olla);      // second firing: does the work
        }

        /// <summary>
        /// Calls the protected method the mod patches, and reports what vanilla
        /// plus the patch together decided.
        /// </summary>
        static float WaterDistance(BlockPos pos)
        {
            var be = World.BE<BlockEntityFarmland>(pos);
            var method = typeof(BlockEntitySoilNutrition).GetMethod(
                "GetNearbyWaterDistance", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.NotNull(method, "BlockEntitySoilNutrition.GetNearbyWaterDistance still exists");

            var args = new object[] { null, 0f };
            return (float)method.Invoke(be, args);
        }
    }
}
