using System.Reflection;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Olla.Tests
{
    /// <summary>
    /// Ollas open to the sky slowly collect rain.
    ///
    /// vstestkit forces precipitation to 0 for the whole session, so each test
    /// here sets its own and puts 0 back afterwards. The override applies to any
    /// moment, past or present, which is what lets a calendar jump stand in for a
    /// stretch of rain - and also means these cannot tell whether rain is sampled
    /// at the right time in the past, only that it is counted at all.
    ///
    /// Headless: everything here is server-side.
    /// </summary>
    public class OllaRain
    {
        static BlockPos Olla     => P(9, 0, 8);
        static BlockPos Above    => P(9, 1, 8);
        static BlockPos Farmland => P(8, 0, 8);

        const string BuriedOlla    = "olla:olla-fired-red-buried";
        const string SurfaceOlla   = "olla:olla-fired-red-normal";
        const string FarmlandBlock = "game:farmland-dry-medium";
        const string Cover         = "game:rock-granite";
        const string Roof          = "game:planks-oak-ud";

        // The README's figure, written out rather than read from the mod.
        const float LitresPerHourOfFullRain = 0.5f;

        [BeforeEach]
        public void Reset()
        {
            olla.OllaConfig.Current = new olla.OllaConfig();
        }

        [AfterEach]
        public void StopTheRain()
        {
            World.SetPrecipitation(0f);
            olla.OllaConfig.Current = new olla.OllaConfig();
        }

        [VsTest]
        public async Task ABuriedOllaCollectsRain()
        {
            var be = await PlaceOlla(BuriedOlla);
            World.SetPrecipitation(1f);

            await AdvanceAndTick(4);

            Assert.Close(be.CurrentWaterLiters, 4 * LitresPerHourOfFullRain, 0.05, "litres after four hours of full rain");
        }

        [VsTest]
        public async Task ASurfaceOllaCollectsRainToo()
        {
            var be = await PlaceOlla(SurfaceOlla);
            World.SetPrecipitation(1f);

            await AdvanceAndTick(4);

            Assert.Close(be.CurrentWaterLiters, 4 * LitresPerHourOfFullRain, 0.05, "litres after four hours of full rain");
        }

        [VsTest]
        public async Task LighterRainCollectsLess()
        {
            var be = await PlaceOlla(BuriedOlla);
            World.SetPrecipitation(0.25f);

            await AdvanceAndTick(4);

            Assert.Close(be.CurrentWaterLiters, 0.25 * 4 * LitresPerHourOfFullRain, 0.05,
                "litres after four hours at a quarter of full rain");
        }

        [VsTest]
        public async Task NoRainNoWater()
        {
            var be = await PlaceOlla(BuriedOlla);

            await AdvanceAndTick(24);

            Assert.Equal(0f, be.CurrentWaterLiters, "litres after a dry day");
        }

        [VsTest]
        public async Task ACoveredOllaCollectsNothing()
        {
            var be = await PlaceOlla(BuriedOlla);
            World.SetBlock(Cover, Above);
            await Ticks(2);
            Assert.False(be.IsOpenToTheSky(), "the cover blocks the sky");

            World.SetPrecipitation(1f);
            await AdvanceAndTick(4);

            Assert.Equal(0f, be.CurrentWaterLiters, "litres collected under cover");
        }

        [VsTest]
        public async Task RainStopsAtAFullOlla()
        {
            var be = await PlaceOlla(BuriedOlla);
            be.TryAddWater(be.MaxWaterCapacity - 1f);
            World.SetPrecipitation(1f);

            await AdvanceAndTick(24);

            Assert.Close(be.CurrentWaterLiters, be.MaxWaterCapacity, 0.001, "a full olla, and no more");
        }

        [VsTest]
        public async Task ARateOfZeroTurnsCollectionOff()
        {
            olla.OllaConfig.Current = new olla.OllaConfig { RainLitresPerHour = 0f };
            var be = await PlaceOlla(BuriedOlla);
            World.SetPrecipitation(1f);

            await AdvanceAndTick(4);

            Assert.Equal(0f, be.CurrentWaterLiters, "litres with collection turned off");
        }

        /// <summary>
        /// Rain is collected interval by interval during a catch-up, not added at the
        /// end, so an olla that was empty when the stretch began goes back to work
        /// within it. The farmland is roofed so that its own rain cannot hide whether
        /// the olla watered it - all of its moisture has to come from the olla.
        /// </summary>
        [VsTest]
        public async Task AnEmptyOllaRefilledByRainIrrigatesInTheSameStretch()
        {
            World.SetBlock(FarmlandBlock, Farmland);
            await RoofTheFarmland();
            var be = await PlaceOlla(BuriedOlla);
            World.SetPrecipitation(1f);

            await AdvanceAndTick(24);

            Assert.Less(be.CurrentWaterLiters, 24 * LitresPerHourOfFullRain - 0.1f,
                "some of the day's rain went back out into the farmland");
            Assert.Greater(World.BE<BlockEntityFarmland>(Farmland).MoistureLevel, 0f,
                "the roofed farmland was watered");
        }

        /// <summary>
        /// A drizzle into an empty olla must not pay for more irrigation than it brought.
        /// The irrigation loop only checks that some water is left before each block, so
        /// the last block used to get its full share however little was there, and the
        /// olla's level clamped at zero. A tenth of an hour at precipitation 0.01 brings
        /// half a millilitre; adjacent dry farmland asks for about 30 times that.
        /// Roofed, so that every bit of moisture it gains came out of the olla.
        /// </summary>
        [VsTest]
        public async Task ADrizzleIntoAnEmptyOllaWatersNoMoreThanItCollected()
        {
            World.SetBlock(FarmlandBlock, Farmland);
            await RoofTheFarmland();
            var be = await PlaceOlla(BuriedOlla);

            // Farmland placed with SetBlock has never updated, so its first update dries
            // it over the whole retention window. Take that now, not mid-measurement.
            var soil = World.BE<BlockEntityFarmland>(Farmland);
            soil.WaterFarmland(0f, false);
            float before = soil.MoistureLevel;

            const float level = 0.01f;
            World.SetPrecipitation(level);
            double start = Sapi.World.Calendar.TotalHours;

            await AdvanceAndTick(0.1);

            // An upper bound: the olla's own catch-up spans less than start-to-now.
            double hours = Sapi.World.Calendar.TotalHours - start;
            double collected = level * LitresPerHourOfFullRain * hours;
            double paidFor = (soil.MoistureLevel - before) * LitresPerMoisture;

            Assert.Greater(collected, 0.0, "some rain fell");
            Assert.LessOrEqual(paidFor, collected + 1e-6, "litres of moisture delivered, against litres collected");
            Assert.GreaterOrEqual(be.CurrentWaterLiters, 0f, "the olla's level");
        }

        /// <summary>
        /// A rate that is not a finite number would turn the sum of a wet interval into
        /// NaN or Infinity: 0 * Infinity is NaN, so one dry hour poisons the lot.
        /// Newtonsoft reads 1e100 as Infinity, so this is reachable from the file.
        /// Sanitise is what Load calls on every start; reached by reflection rather than
        /// by writing a config file and restarting the world.
        /// </summary>
        [VsTest]
        public async Task AnUnusableRateFallsBackToTheDefault()
        {
            Assert.Close(Sanitised(float.PositiveInfinity), LitresPerHourOfFullRain, 0.0001, "Infinity");
            Assert.Close(Sanitised(float.NaN), LitresPerHourOfFullRain, 0.0001, "NaN");
            Assert.Equal(0f, Sanitised(-1f), "a negative rate");
            Assert.Equal(2f, Sanitised(2f), "a large but finite rate is the player's call");

            await Task.CompletedTask;
        }

        // 1.25 L for each unit of moisture delivered - the mod's billing rate, as the
        // README and the tooltip state it.
        const double LitresPerMoisture = 1.25;

        static float Sanitised(float rate)
        {
            var config = new olla.OllaConfig { RainLitresPerHour = rate };
            typeof(olla.OllaConfig)
                .GetMethod("Sanitise", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(config, new object[] { Sapi });
            return config.RainLitresPerHour;
        }

        /// <summary>
        /// Keeps rain off the farmland, so that all of its moisture has to come from the
        /// olla. Planks, not the granite used over the olla: rock in 1.22 caves in when
        /// unsupported, and granite on farmland is gone a tick later. Two blocks up, so
        /// nothing sits on the farmland itself; the rain map counts the highest
        /// rain-blocking block, so a roof with a gap under it does the same job. Checked
        /// rather than assumed - a roof that silently fell is how this first passed for
        /// the wrong reason.
        /// </summary>
        static async Task RoofTheFarmland()
        {
            World.SetBlock(Roof, Farmland.UpCopy(2));
            await Ticks(2);

            int rainMap = Sapi.World.BlockAccessor.GetRainMapHeightAt(Farmland.X, Farmland.Z);
            Assert.Greater(rainMap, Farmland.Y, "the roof keeps rain off the farmland");
        }

        static async Task<olla.BlockEntityOllaFired> PlaceOlla(string code)
        {
            World.SetBlock(code, Olla);
            await Ticks(4);

            var be = World.BE<olla.BlockEntityOllaFired>(Olla);
            Assert.Equal(0f, be.CurrentWaterLiters, "a freshly placed olla starts empty");
            return be;
        }

        /// <summary>
        /// A stretch of game time between two firings of the olla's tick - the first
        /// takes a baseline, the second catches up. Same shape as OllaIrrigation's
        /// Irrigate, and a long jump is the same catch-up an unloaded chunk gets.
        /// </summary>
        static async Task AdvanceAndTick(double hours)
        {
            await World.TickNow(Olla);
            await Hours(hours);
            await World.TickNow(Olla);
        }
    }
}
