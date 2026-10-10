using System;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Olla.Tests
{
    /// <summary>
    /// An olla block that has lost its block entity, and getting it back - see
    /// BlockOllaFired.GetOrRestoreBlockEntity for how a world gets that way.
    ///
    /// The tests strip the block entity by hand - on both sides, as a reload would leave it -
    /// rather than reloading a world without the mod, which the harness cannot do.
    /// </summary>
    public class OllaRestore
    {
        static BlockPos Olla => P(8, 1, 8);
        const string SurfaceOlla = "olla:olla-fired-red-normal";
        const string BuriedOlla  = "olla:olla-fired-red-buried";

        [VsTest(TimeoutMs = 60000)]
        public async Task ARandomTickRestoresAnOllaThatLostItsBlockEntity()
        {
            var block = await PlaceStripped(BuriedOlla);

            Assert.True(block.ShouldReceiveServerGameTicks(Sapi.World, Olla, new Random(1), out object extra),
                "an olla asks for random ticks");
            block.OnServerGameTick(Sapi.World, Olla, extra);

            var be = ServerOllaOrNull();
            Assert.NotNull(be, "the block entity is back");
            Assert.Equal(0f, be.CurrentWaterLiters, "a restored olla starts empty");
            Assert.True(be.IsBuried(), "and still knows it is buried");
            Assert.True(be.TryAddWater(10f), "and takes water");
        }

        [VsTest(TimeoutMs = 60000)]
        public async Task ARandomTickLeavesAHealthyOllaAlone()
        {
            World.SetBlock(BuriedOlla, Olla);
            await Ticks(4);
            var be = World.BE<olla.BlockEntityOllaFired>(Olla);
            be.TryAddWater(25f);

            var block = Sapi.World.BlockAccessor.GetBlock(Olla);
            block.OnServerGameTick(Sapi.World, Olla, null);

            Assert.True(ReferenceEquals(be, Sapi.World.BlockAccessor.GetBlockEntity(Olla)),
                "the same block entity, not a fresh one");
            Assert.Close(be.CurrentWaterLiters, 25.0, 0.001, "with its water");
        }

        [VsTest(TimeoutMs = 60000), RequiresClient]
        public async Task TheClientSeesARestoredOlla()
        {
            var block = await PlaceStripped(BuriedOlla);
            block.OnServerGameTick(Sapi.World, Olla, null);

            await UntilClient(true, "the client receives the restored block entity");
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task ABucketFillsAnOllaThatLostItsBlockEntity()
        {
            await PlaceStripped(SurfaceOlla);

            await OllaFilling.HoldBucketOf("game:waterportion");

            await Player.StandNear(Olla);
            await Interact.UseBlock(Olla);

            await Until(() => ServerOllaOrNull()?.CurrentWaterLiters > 0, 120);
            Assert.Close(ServerOllaOrNull().CurrentWaterLiters, 10.0, 0.01, "the first bucket lands in the restored olla");
        }

        [VsTest(TimeoutMs = 60000)]
        public async Task AWateringCanFillsAnOllaThatLostItsBlockEntity()
        {
            await PlaceStripped(SurfaceOlla);

            OllaFilling.StepCanAtOlla(10f);

            var be = ServerOllaOrNull();
            Assert.NotNull(be, "the first pour restores the block entity");
            Assert.Greater(be.CurrentWaterLiters, 0f, "and lands in it");
        }

        /// <summary>
        /// An olla block with no block entity on either side, the state a reload without the
        /// mod leaves behind.
        /// </summary>
        static async Task<Block> PlaceStripped(string code)
        {
            World.SetBlock(code, Olla);
            await Ticks(4);

            // The placement has to have reached the client before either side is stripped, or
            // it arrives afterwards and puts the client's block entity back.
            if (Capi != null) await UntilClient(true, "the placement reaches the client");

            Sapi.World.BlockAccessor.RemoveBlockEntity(Olla);
            if (Capi != null)
            {
                await OnClient();
                Capi.World.BlockAccessor.RemoveBlockEntity(Olla);
                await OnServer();
            }
            await Ticks(4);

            Assert.Null(Sapi.World.BlockAccessor.GetBlockEntity(Olla), "no block entity on the server");
            if (Capi != null) Assert.False(await ClientHasOlla(), "none on the client");

            var block = Sapi.World.BlockAccessor.GetBlock(Olla);
            Assert.Equal(code, block.Code.ToString(), "the olla block itself is still there");
            return block;
        }

        /// <summary>
        /// Waits for the client to have, or not have, an olla block entity. Not Until: its
        /// condition runs on the server thread, and this has to be asked of the client's world.
        /// </summary>
        static async Task UntilClient(bool hasOlla, string what)
        {
            for (int i = 0; i < 100; i++)
            {
                if (await ClientHasOlla() == hasOlla) return;
                await Ticks(1);
            }
            Assert.Fail(what);
        }

        static olla.BlockEntityOllaFired ServerOllaOrNull() =>
            Sapi.World.BlockAccessor.GetBlockEntity(Olla) as olla.BlockEntityOllaFired;

        static async Task<bool> ClientHasOlla()
        {
            await OnClient();
            bool has = Capi.World.BlockAccessor.GetBlockEntity(Olla) is olla.BlockEntityOllaFired;
            await OnServer();
            return has;
        }
    }
}
