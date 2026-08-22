using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Olla.Tests
{
    /// <summary>
    /// Filling an olla the way a player does it: hold a bucket, aim at the pot,
    /// right-click. Everything else about water types can be checked by asking the
    /// predicate, but only this exercises the whole path - the interaction handler,
    /// the ILiquidSource transfer arithmetic, and the stack write-back.
    ///
    /// These need a real client for block selection and the use key, so they are
    /// [RequiresClient] and are skipped rather than failed on a headless run.
    ///
    /// The Hydrate or Diedrate cases need that mod loaded too:
    ///
    ///     cairn-cli sync ollahod
    ///     run.sh ../olla/tests --mod ../olla/olla --mods ~/.cairn/packs/ollahod/Mods
    ///
    /// Without it they log and pass trivially, so read the log line rather than the
    /// green tick if you care whether the compat path was really covered.
    /// </summary>
    public class OllaFilling
    {
        // One above the plot floor, i.e. sat *on* the ground the way a player
        // places one, not sunk into it. A block flush with the terrain has its
        // centre below the surface plane, so the aim ray clips the soil in
        // front and selects that instead.
        static BlockPos Olla => P(8, 1, 8);

        const string SurfaceOlla = "olla:olla-fired-red-normal";
        const string Bucket      = "game:woodbucket";

        // 100 portions to the litre, and a wood bucket holds 10.
        const int PortionsPerLitre = 100;
        const int BucketPortions   = 10 * PortionsPerLitre;

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task ABucketOfVanillaWaterFillsTheOlla()
        {
            var be = await PlaceOlla();
            await HoldBucketOf("game:waterportion");

            await Use();

            await Until(() => be.CurrentWaterLiters > 0, 120);
            Assert.Close(be.CurrentWaterLiters, 10.0, 0.01, "litres in the olla after one bucket");
            Assert.Equal(0, HeldPortions(), "portions left in the bucket");
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task ABucketOfSaltWaterIsRefused()
        {
            var be = await PlaceOlla();
            await HoldBucketOf("game:saltwaterportion");

            await Use();
            await Ticks(20);

            Assert.Equal(0f, be.CurrentWaterLiters, "salt water must not go into an olla");
            Assert.Equal(BucketPortions, HeldPortions(), "and must not be taken out of the bucket");
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task HydrateOrDiedrateRainWaterFillsTheOlla()
        {
            if (!RequireHod()) return;

            var be = await PlaceOlla();
            await HoldBucketOf("hydrateordiedrate:waterportion-fresh-rain-clean");

            await Use();

            await Until(() => be.CurrentWaterLiters > 0, 120);
            Assert.Close(be.CurrentWaterLiters, 10.0, 0.01, "litres from a bucket of H&D rain water");
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task HydrateOrDiedrateDistilledWaterFillsTheOlla()
        {
            if (!RequireHod()) return;

            var be = await PlaceOlla();
            await HoldBucketOf("hydrateordiedrate:waterportion-fresh-distilled-clean");

            await Use();

            await Until(() => be.CurrentWaterLiters > 0, 120);
            Assert.Greater(be.CurrentWaterLiters, 0f, "litres from a bucket of H&D distilled water");
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task HydrateOrDiedrateSaltWaterIsRefused()
        {
            if (!RequireHod()) return;

            var be = await PlaceOlla();
            await HoldBucketOf("hydrateordiedrate:waterportion-salt-well-clean");

            await Use();
            await Ticks(20);

            Assert.Equal(0f, be.CurrentWaterLiters, "H&D salt water must not go into an olla");
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task HydrateOrDiedrateTaintedWaterIsRefused()
        {
            if (!RequireHod()) return;

            var be = await PlaceOlla();
            await HoldBucketOf("hydrateordiedrate:waterportion-fresh-well-tainted");

            await Use();
            await Ticks(20);

            Assert.Equal(0f, be.CurrentWaterLiters, "tainted water is meant to be purified first");
        }

        // ---------- helpers ----------

        static bool RequireHod()
        {
            if (Sapi.ModLoader.IsModEnabled("hydrateordiedrate")) return true;

            Log("hydrateordiedrate is not loaded - rerun with --mods ~/.cairn/packs/ollahod/Mods. " +
                "This test proved nothing.");
            return false;
        }

        static async Task<olla.BlockEntityOllaFired> PlaceOlla()
        {
            // Deliberately no SetGameMode: nothing here needs creative, and
            // Player.SetGameMode runs /gamemode as the console, which NREs in
            // CmdPlayer.handleGameMode looking up a calling player that is null.
            World.SetBlock(SurfaceOlla, Olla);
            await Ticks(4);

            var be = World.BE<olla.BlockEntityOllaFired>(Olla);
            Assert.Equal(0f, be.CurrentWaterLiters, "a freshly placed olla starts empty");
            return be;
        }

        /// <summary>
        /// Puts a full bucket of the given liquid in the player's hand, on both sides.
        ///
        /// Writing the liquid into the server-side stack is not enough: the client
        /// keeps its own copy of the inventory, and the interaction starts there. Skip
        /// the MarkDirty and the client goes on holding an empty bucket - which makes
        /// the fill tests fail *and* the refusal tests pass for entirely the wrong
        /// reason. Hence the client-side check before any test acts on it.
        /// </summary>
        static async Task HoldBucketOf(string liquidCode)
        {
            await Player.Hold(Bucket);

            var slot = Player.Me.InventoryManager.ActiveHotbarSlot;
            var bucket = slot.Itemstack;
            Assert.NotNull(bucket, "a bucket in hand");

            var sink = bucket.Collectible as ILiquidSink;
            Assert.NotNull(sink, "the bucket is a liquid container");

            sink.SetContent(bucket, World.Stack(liquidCode, BucketPortions));
            slot.MarkDirty();
            await Ticks(4);

            Assert.Equal(BucketPortions, HeldPortions(), $"server sees a full bucket of {liquidCode}");
            Assert.Equal(BucketPortions, await ClientHeldPortions(), $"client sees a full bucket of {liquidCode}");
        }

        /// <summary>
        /// What the client thinks it is holding. The negative tests are only worth
        /// anything if this is non-zero - otherwise they assert that an empty bucket
        /// fails to fill an olla, which was never in doubt.
        /// </summary>
        static async Task<int> ClientHeldPortions()
        {
            await OnClient();

            var stack = Capi.World.Player.InventoryManager.ActiveHotbarSlot?.Itemstack;
            var source = stack?.Collectible as ILiquidSource;
            var portions = source?.GetContent(stack)?.StackSize ?? 0;

            await OnServer();
            return portions;
        }

        static async Task Use()
        {
            await Player.StandNear(Olla);
            await Interact.UseBlock(Olla);
        }

        static int HeldPortions()
        {
            var bucket = Player.Me.InventoryManager.ActiveHotbarSlot?.Itemstack;
            var source = bucket?.Collectible as ILiquidSource;
            return source?.GetContent(bucket)?.StackSize ?? 0;
        }
    }
}
