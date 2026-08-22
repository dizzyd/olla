using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Olla.Tests
{
    /// <summary>
    /// Blending several water sources into one moisture floor.
    ///
    /// Vanilla turns the distance to the nearest water into a floor with
    /// minMoisture = clamp(1 - distance / 4), so 0/1/2/3 blocks away means
    /// 100%/75%/50%/25%. Taking only the *closest* source meant two ollas
    /// overlapping at the edge of their radii were worth exactly one, and the
    /// soil between them sat at 50% no matter how much pottery was buried.
    ///
    /// Sources now combine as a probabilistic union - each wets whatever share
    /// the others left dry - and the blend is handed back as a fractional
    /// distance, so vanilla's own arithmetic does the rest untouched.
    ///
    /// These assert on the distance rather than on moisture because that is
    /// where the mod's arithmetic actually lives: it is exact, and it does not
    /// depend on how much irrigation happened to run first.
    ///
    /// All server-side, so they run headless.
    /// </summary>
    public class OllaBlending
    {
        // Same reasoning as OllaIrrigation: kept clear of the plot edge, because
        // vanilla's water search looks 4 blocks out and answers "Deferred" if
        // that reaches an unloaded column.
        static BlockPos Farmland => P(8, 0, 8);

        // Chebyshev distance 2 - the outer edge of an olla's range, and the case
        // that used to cap at 50% however many ollas were in reach.
        static BlockPos EastOlla  => P(10, 0, 8);
        static BlockPos WestOlla  => P(6, 0, 8);
        static BlockPos SouthOlla => P(8, 0, 10);

        // Chebyshev distance 3: inside vanilla's 4-block search, outside the
        // olla's 2-block one. A pond here is worth 25% on its own.
        //
        // Placed on the far side from the ollas and walled in, because a water
        // source block *flows*. Left open it creeps a block closer partway
        // through the test, vanilla starts reporting 2 instead of 3, and the
        // assertion fails on arithmetic that was never wrong.
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
        }

        [VsTest, RequiresClient]
        public async Task ThePatchIsRegisteredExactlyOnce()
        {
            // Only a singleplayer run can catch this. ModSystem.Start() is called
            // once per side against the same assembly, so a PatchAll() there gives
            // Harmony two identical postfixes and every call runs both - folding the
            // same ollas into the already blended result and squaring their effect.
            //
            // Math.Min hid it for as long as the patch only did that, because
            // applying it twice changes nothing. Blending does not have that luxury.
            // Headless there is only one side, so this passes there regardless:
            // it is [RequiresClient] to be skipped rather than to pass vacuously.
            var method = typeof(BlockEntitySoilNutrition).GetMethod(
                "GetNearbyWaterDistance", BindingFlags.NonPublic | BindingFlags.Instance);

            var patches = Harmony.GetPatchInfo(method);
            Assert.NotNull(patches, "the method is patched at all");

            var ours = patches.Postfixes.Where(p => p.owner == "com.dizzyd.olla").ToList();
            Assert.Equal(1, ours.Count, "olla postfixes registered on GetNearbyWaterDistance");

            await Task.CompletedTask;
        }

        [VsTest]
        public async Task OneOllaAtTheEdgeIsUnchanged()
        {
            // The regression guard for everything below: a lone source has
            // nothing to blend with and must still report exactly its distance.
            await Bury(EastOlla);

            var (distance, result) = WaterSearch();

            Assert.Equal("Found", result, "search result with one olla in range");
            Assert.Close(distance, 2.0, 0.001, "distance to a single olla 2 blocks away");
            Assert.Close(Floor(distance), 0.50, 0.001, "moisture floor from one olla");
        }

        [VsTest]
        public async Task TwoOllasAtTheEdgeBlendToSeventyFivePercent()
        {
            // The reported bug. Both ollas are 2 away, so the old min() answered
            // 2 for either one alone or both together, and the soil stuck at 50%.
            await Bury(EastOlla);
            await Bury(WestOlla);

            var (distance, result) = WaterSearch();

            Assert.Equal("Found", result, "search result with two ollas in range");
            Assert.Close(distance, 1.0, 0.001, "blended distance for two ollas 2 blocks away");
            Assert.Close(Floor(distance), 0.75, 0.001, "moisture floor for two overlapping ollas");
        }

        [VsTest]
        public async Task ThreeOllasBlendFurtherStill()
        {
            await Bury(EastOlla);
            await Bury(WestOlla);
            await Bury(SouthOlla);

            var (distance, _) = WaterSearch();

            Assert.Close(distance, 0.5, 0.001, "blended distance for three ollas 2 blocks away");
            Assert.Close(Floor(distance), 0.875, 0.001, "moisture floor for three overlapping ollas");
        }

        [VsTest]
        public async Task BlendingNeverReachesAFullFloorFromTheEdge()
        {
            // The union is asymptotic on purpose: overlap always pays, but a
            // 100% floor still has to be earned with a source right alongside.
            await Bury(EastOlla);
            await Bury(WestOlla);
            await Bury(SouthOlla);
            await Bury(P(8, 0, 6));

            var (distance, _) = WaterSearch();

            Assert.Greater(distance, 0f, "four edge ollas should still not reach distance 0");
            Assert.Less(Floor(distance), 1.0, "...nor a 100% moisture floor");
            Assert.Close(Floor(distance), 0.9375, 0.001, "moisture floor for four overlapping ollas");
        }

        [VsTest]
        public async Task AnEmptyOllaContributesNothing()
        {
            await Bury(EastOlla);
            await Bury(WestOlla, litres: 0);

            var (distance, _) = WaterSearch();

            Assert.Close(distance, 2.0, 0.001, "an empty olla must not count as a source");
        }

        [VsTest]
        public async Task NaturalWaterBlendsWithAnOlla()
        {
            // Ollas do not merely compete with a pond for "closest" any more.
            foreach (var wall in PondWalls) World.SetBlock(Wall, wall);
            World.SetBlock(Water, Pond);
            await Ticks(4);

            var (pondOnly, _) = WaterSearch();
            Assert.Equal(3, NearestWaterBlock(), "the pond is walled in 3 blocks away");
            Assert.Close(pondOnly, 3.0, 0.001, "vanilla distance to a pond 3 blocks away");

            await Bury(EastOlla);

            var (blended, _) = WaterSearch();
            Assert.Equal(3, NearestWaterBlock(), "the pond stayed put while the olla went in");
            Assert.Close(blended, 1.5, 0.001, "pond 3 away blended with an olla 2 away");
            Assert.Close(Floor(blended), 0.625, 0.001, "moisture floor for pond plus olla");
        }

        [VsTest]
        public async Task ASecondOllaIsSeenImmediately()
        {
            // The cache used to key a single olla per farmland block and never
            // notice another being added, which would have made the whole
            // feature invisible to anyone who buried their ollas one at a time.
            await Bury(EastOlla);

            var (before, _) = WaterSearch();
            Assert.Close(before, 2.0, 0.001, "distance with the first olla, now cached");

            await Bury(WestOlla);

            var (after, _) = WaterSearch();
            Assert.Close(after, 1.0, 0.001, "distance after burying a second olla next to the first");
        }

        [VsTest]
        public async Task RemovingAnOllaLeavesTheBlend()
        {
            await Bury(EastOlla);
            await Bury(WestOlla);

            var (both, _) = WaterSearch();
            Assert.Close(both, 1.0, 0.001, "both ollas blending");

            World.SetBlock("game:air", WestOlla);
            await Ticks(2);

            var (one, _) = WaterSearch();
            Assert.Close(one, 2.0, 0.001, "distance after one of the two is dug up");
        }

        [VsTest(TimeoutMs = 90000)]
        public async Task TheBlendedFloorActuallyLiftsFarmlandMoisture()
        {
            // Everything above measures the number the mod hands vanilla. This
            // one checks vanilla does with it what we think: the floor is
            // applied to real soil, which is what a player sees on the block.
            await Bury(EastOlla);
            await Bury(WestOlla);

            // Farmland only re-searches for water on its long update, which needs
            // 3-4 game hours to have passed since its last one. A short tick reuses
            // the distance it cached when the block was created - before the ollas
            // existed - so ticking without advancing the calendar reads 0 moisture
            // and looks exactly like a broken patch.
            await Hours(6);
            await World.TickNow(Farmland);

            var moisture = World.BE<BlockEntityFarmland>(Farmland).MoistureLevel;
            Assert.GreaterOrEqual(moisture, 0.749f, "farmland moisture held up by two blended ollas");
        }

        // ---------- helpers ----------

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
