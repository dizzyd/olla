using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Olla.Tests
{
    /// <summary>
    /// Filling an olla the way a player does it: aim at the pot with something
    /// in hand and right-click. Which water an olla takes can be checked by asking
    /// the predicate; only this exercises the paths a player actually uses.
    ///
    /// The bucket cases cover the liquid transfer - the interaction handler, the
    /// ILiquidSource arithmetic and the stack write-back. A watering can bypasses
    /// all of that, so its cases cover what the can patch does instead: a pour
    /// held for as long as the button is down, seconds of pouring turned into
    /// litres, and the refund of whatever a full olla cannot take - on the client
    /// as well as the server.
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

        // Where a player actually buries one: in the ground, level with the
        // farmland it waters, so only its rim stands proud of the surface.
        static BlockPos BuriedOllaPos => P(8, 0, 8);
        const string BuriedOlla = "olla:olla-fired-red-buried";
        const string Bucket      = "game:woodbucket";

        // 100 portions to the litre, and a wood bucket holds 10.
        const int PortionsPerLitre = 100;
        const int BucketPortions   = 10 * PortionsPerLitre;

        const string WateringCan = "game:wateringcan-red-fired";

        // What the README promises for a full can. Written out rather than read from
        // the mod, so that changing the mod's conversion fails these tests.
        const float CanLitres = 5f;

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

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task AWateringCanPoursIntoTheOlla()
        {
            var be = await PlaceOlla();
            float capacity = await HoldWateringCan();

            await PourUntil(() => CanSeconds() <= 0f);

            Assert.Close(be.CurrentWaterLiters, CanLitres, 0.01, "litres in the olla after one full can");
            Assert.LessOrEqual(CanSeconds(), 0f, $"the can ({capacity}s) is empty");
        }

        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task AWateringCanKeepsWhatAFullOllaCannotTake()
        {
            var be = await PlaceOlla();
            be.TryAddWater(be.MaxWaterCapacity - 1f);
            float capacity = await HoldWateringCan();

            // Keep pouring well past the point the olla fills, so an overflow that
            // is not refunded would show as a can drained further than one litre.
            await PourUntil(() => be.CurrentWaterLiters >= be.MaxWaterCapacity, extraTicks: 40);

            Assert.Close(be.CurrentWaterLiters, be.MaxWaterCapacity, 0.01, "the olla is full");
            Assert.Close(CanSeconds(), capacity * (CanLitres - 1f) / CanLitres, 0.05,
                "the can gave up one litre and kept the rest");
        }

        /// <summary>
        /// A buried olla is how one is used, and it is a different block with a
        /// different shape, sunk into the ground. The pour lands only if aiming at
        /// it from above selects the olla and not the soil around it, and the can
        /// patch finds its block entity there. Interact.Aim checks the first; the
        /// litres check the second.
        /// </summary>
        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task AWateringCanPoursIntoABuriedOlla()
        {
            var be = await PlaceOlla(BuriedOlla, BuriedOllaPos);
            Assert.True(be.IsBuried(), "the olla is buried");
            await HoldWateringCan();

            await PourUntil(() => CanSeconds() <= 0f, at: BuriedOllaPos, face: BlockFacing.UP);

            Assert.Close(be.CurrentWaterLiters, CanLitres, 0.01, "litres in the buried olla after one full can");
        }

        /// <summary>
        /// The refund has to happen on the client too. Vanilla drains the can on both
        /// sides, so a refund made only on the server leaves the client's copy running
        /// dry - and a client that thinks the can is empty stops the pour, then picks
        /// it up again when the server's count comes back, over and over, with water
        /// in the can the whole time. A nearly empty can over a full olla makes that
        /// happen within a second; the release that resyncs the two would hide it.
        /// </summary>
        [VsTest(TimeoutMs = 120000), RequiresClient]
        public async Task ANearlyEmptyCanKeepsPouringOverAFullOlla()
        {
            var be = await PlaceOlla();
            be.TryAddWater(be.MaxWaterCapacity);
            const float seconds = 1f;
            await HoldWateringCan(seconds);

            float lowestClient = seconds;
            int notPouring = 0;

            await Pouring(async () =>
            {
                await Until(() => Player.Me.Entity.Controls.HandUse == EnumHandInteract.HeldItemInteract,
                    200, "the pour starting");

                // Several times the can's one second, so an unrefunded client empties.
                for (int i = 0; i < 60; i++)
                {
                    await Ticks(2);
                    var client = await ClientCan();
                    lowestClient = System.Math.Min(lowestClient, client.Seconds);
                    if (client.HandUse != EnumHandInteract.HeldItemInteract) notPouring++;
                }

                Assert.Close(CanSeconds(), seconds, 0.01, "server can, mid-pour");
                Assert.Close((await ClientCan()).Seconds, seconds, 0.01, "client can, mid-pour");
            });

            Assert.Equal(0, notPouring, "samples where the client had stopped pouring");
            Assert.Greater(lowestClient, seconds - 0.05f, "the client can never ran down while held");
            Assert.Close(be.CurrentWaterLiters, be.MaxWaterCapacity, 0.01, "the olla stayed full");
            Assert.Close(CanSeconds(), seconds, 0.01, "server can after release");
            Assert.Close((await ClientCan()).Seconds, seconds, 0.01, "client can after release");
        }

        /// <summary>
        /// The step that runs a can dry is where vanilla returns its stop-pouring
        /// result - before the refund puts the water back. A can holding a step's
        /// worth or less over a full olla must still be told to keep pouring.
        /// ANearlyEmptyCanKeepsPouringOverAFullOlla starts at a full second, well
        /// clear of that boundary, so these step the patched method directly with a
        /// fixed step rather than at whatever the frame rate gives.
        ///
        /// No client needed: anything that can hold a can will do as the pourer.
        /// </summary>
        [VsTest]
        public async Task ACanHoldingExactlyOneStepKeepsPouringOverAFullOlla()
        {
            var (keepPouring, left) = await StepCanOverOlla(canSeconds: Step, ollaLitres: OllaFull);

            Assert.True(keepPouring, "told to keep pouring");
            Assert.Close(left, Step, 0.0001, "seconds left in the can");
        }

        [VsTest]
        public async Task ACanHoldingLessThanOneStepKeepsPouringOverAFullOlla()
        {
            var (keepPouring, left) = await StepCanOverOlla(canSeconds: Step / 5, ollaLitres: OllaFull);

            Assert.True(keepPouring, "told to keep pouring");
            Assert.Close(left, Step / 5, 0.0001, "seconds left in the can");
        }

        /// <summary>
        /// The other side of the same boundary: with room in the olla the water goes
        /// in, the can is really empty, and vanilla's stop must stand.
        /// </summary>
        [VsTest]
        public async Task ACanThatEmptiesIntoAnOllaStopsPouring()
        {
            var (keepPouring, left) = await StepCanOverOlla(canSeconds: Step, ollaLitres: 0f);

            Assert.False(keepPouring, "told to stop pouring");
            Assert.LessOrEqual(left, 0f, "seconds left in the can");
        }

        /// <summary>
        /// Unlike the farmland patch, the can patch is wanted on both sides, so
        /// singleplayer is exactly where it could be applied twice - once per side's
        /// Start(), against one shared assembly. Twice would fill the olla twice per
        /// pour step. Headless there is one side and nothing to catch.
        /// </summary>
        [VsTest, RequiresClient]
        public async Task TheCanPatchIsRegisteredExactlyOnce()
        {
            var patches = Harmony.GetPatchInfo(
                typeof(BlockWateringCan).GetMethod(nameof(BlockWateringCan.OnHeldInteractStep)));
            Assert.NotNull(patches, "OnHeldInteractStep is patched at all");

            const string owner = "com.dizzyd.olla.wateringcan";
            Assert.Equal(1, patches.Prefixes.Count(p => p.owner == owner), "olla prefixes on the can");
            Assert.Equal(1, patches.Postfixes.Count(p => p.owner == owner), "olla postfixes on the can");

            await Task.CompletedTask;
        }

        // ---------- helpers ----------

        static bool RequireHod()
        {
            if (Sapi.ModLoader.IsModEnabled("hydrateordiedrate")) return true;

            Log("hydrateordiedrate is not loaded - rerun with --mods ~/.cairn/packs/ollahod/Mods. " +
                "This test proved nothing.");
            return false;
        }

        static async Task<olla.BlockEntityOllaFired> PlaceOlla(string code = SurfaceOlla, BlockPos at = null)
        {
            at ??= Olla;

            // Deliberately no SetGameMode: nothing here needs creative, and
            // Player.SetGameMode runs /gamemode as the console, which NREs in
            // CmdPlayer.handleGameMode looking up a calling player that is null.
            World.SetBlock(code, at);
            await Ticks(4);

            var be = World.BE<olla.BlockEntityOllaFired>(at);
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

        // One pour step of a twentieth of a second, and a marker for "fill it to the top".
        const float Step = 0.05f;
        const float OllaFull = float.MaxValue;

        /// <summary>
        /// Runs one patched OnHeldInteractStep of a can holding canSeconds against an
        /// olla holding ollaLitres. Returns what vanilla-plus-patch said about carrying
        /// on, and the seconds then left in the can.
        /// </summary>
        static async Task<(bool KeepPouring, float SecondsLeft)> StepCanOverOlla(float canSeconds, float ollaLitres)
        {
            var be = await PlaceOlla();
            if (ollaLitres > 0f) be.TryAddWater(System.Math.Min(ollaLitres, be.MaxWaterCapacity));

            var pourer = World.SpawnEntity("game:chicken-hen", P(8, 1, 6)) as EntityAgent;
            Assert.NotNull(pourer, "something to hold the can");

            var slot = new DummySlot(World.Stack(WateringCan, 1));
            var can = slot.Itemstack.Collectible as BlockWateringCan;
            Assert.NotNull(can, "a watering can");
            can.SetRemainingWateringSeconds(slot.Itemstack, canSeconds);
            slot.Itemstack.TempAttributes.SetFloat("secondsUsed", 0f);

            var aim = new BlockSelection
            {
                Position = Olla,
                Face = BlockFacing.UP,
                HitPosition = new Vec3d(0.5, 1, 0.5)
            };

            bool keepPouring = can.OnHeldInteractStep(Step, slot, pourer, aim, null);
            return (keepPouring, can.GetRemainingWateringSeconds(slot.Itemstack));
        }

        /// <summary>
        /// Puts a watering can in hand with the given seconds of pouring in it, full
        /// by default, on both sides - same reason as HoldBucketOf. Returns its
        /// capacity in seconds.
        /// </summary>
        static async Task<float> HoldWateringCan(float? seconds = null)
        {
            await Player.Hold(WateringCan);

            var slot = Player.Me.InventoryManager.ActiveHotbarSlot;
            var can = slot.Itemstack?.Collectible as BlockWateringCan;
            Assert.NotNull(can, "a watering can in hand");

            float fill = seconds ?? can.CapacitySeconds;
            can.SetRemainingWateringSeconds(slot.Itemstack, fill);
            slot.MarkDirty();
            await Ticks(4);

            Assert.Close(CanSeconds(), fill, 0.01, "server sees the can's water");
            Assert.Close((await ClientCan()).Seconds, fill, 0.01, "client sees the can's water");
            return can.CapacitySeconds;
        }

        /// <summary>
        /// Holds right-click on the olla until the condition holds.
        ///
        /// A long pour outlasts the window's focus now and then, and the client
        /// clears a held button when focus goes - no MouseUp, the button just reads
        /// up (ClientMain.OnFocusChanged). That is the test rig, not the mod, so this
        /// presses again the way a player would, and logs it. The pour still has to
        /// deliver the whole can; only the input is retried.
        /// </summary>
        static Task PourUntil(System.Func<bool> done, int extraTicks = 0,
            BlockPos at = null, BlockFacing face = null) => Pouring(async () =>
        {
            const int maxTicks = 2000;
            for (int tick = 0; !done(); tick++)
            {
                if (tick >= maxTicks)
                    throw new AssertionException(
                        $"condition did not hold within {maxTicks} ticks: pouring the watering can");

                await Ticks(1);
                if (tick % 10 == 9 && !await ClientButtonDown())
                {
                    Log($"right button released under the test after {tick + 1} ticks; pressing again");
                    await Input.MouseDown(EnumMouseButton.Right);
                }
            }
            await Ticks(extraTicks);
        }, at, face);

        static async Task<bool> ClientButtonDown()
        {
            await OnClient();
            bool down = Capi.Input.InWorldMouseButton.Right;
            await OnServer();
            return down;
        }

        /// <summary>
        /// Holds right-click on the olla for as long as whileHeld runs. A can pours
        /// for as long as the button is down, which Interact.UseBlock's short click
        /// cannot do. A failure logs where the pour got to before it propagates.
        /// </summary>
        static async Task Pouring(System.Func<Task> whileHeld, BlockPos at = null, BlockFacing face = null)
        {
            at ??= Olla;
            await Player.StandNear(at);
            await Interact.Aim(at, face);

            // Anything that lets go of the button mid-pour, recorded with where it came
            // from. The client clears a held button without raising MouseUp when the
            // window loses focus, so a release with nothing recorded here is that.
            var releases = new System.Collections.Generic.List<string>();
            MouseEventDelegate onMouseUp = e =>
            {
                if (e.Button == EnumMouseButton.Right) releases.Add(System.Environment.StackTrace);
            };
            await OnClient();
            Capi.Event.MouseUp += onMouseUp;
            await OnServer();

            await Input.MouseDown(EnumMouseButton.Right);
            try
            {
                await whileHeld();
            }
            catch (AssertionException)
            {
                Log($"stalled: {await DescribePour(at)}");
                Log(releases.Count == 0
                    ? "no MouseUp while held - the client cleared the button itself (focus loss?)"
                    : $"{releases.Count} MouseUp while held, first from:\n{releases[0]}");
                throw;
            }
            finally
            {
                await OnClient();
                Capi.Event.MouseUp -= onMouseUp;
                await OnServer();
                await Input.MouseUp(EnumMouseButton.Right);
            }

            // OnHeldInteractStop marks the slot dirty; let that settle.
            await Ticks(4);
        }

        /// <summary>
        /// Both sides' view of the pour, for a failure message. Does not assert, so a
        /// missing can is reported rather than replacing the failure being explained.
        /// </summary>
        static async Task<string> DescribePour(BlockPos at)
        {
            var serverStack = Player.Me.InventoryManager.ActiveHotbarSlot?.Itemstack;
            var server = Describe(serverStack);
            var litres = World.BE<olla.BlockEntityOllaFired>(at)?.CurrentWaterLiters;
            var serverUse = Player.Me.Entity.Controls.HandUse;

            await OnClient();
            var player = Capi.World.Player;
            var client = Describe(player.InventoryManager.ActiveHotbarSlot?.Itemstack);
            var clientUse = player.Entity.Controls.HandUse;
            var button = Capi.Input.InWorldMouseButton.Right ? "down" : "up";
            var selection = player.CurrentBlockSelection?.Position;
            await OnServer();

            return $"server can {server}, hand use {serverUse}; client can {client}, " +
                   $"hand use {clientUse}, right button {button}, selection {selection}; " +
                   $"olla at {litres:0.##} L";
        }

        static string Describe(ItemStack stack) =>
            stack == null ? "(empty hand)"
            : stack.Collectible is BlockWateringCan can ? $"{can.GetRemainingWateringSeconds(stack):0.##}s"
            : $"(holding {stack.Collectible.Code}, not a can)";

        /// <summary>Seconds left in the server's held can. Fails if it is not holding one.</summary>
        static float CanSeconds()
        {
            var stack = Player.Me.InventoryManager.ActiveHotbarSlot?.Itemstack;
            Assert.NotNull(stack, "server: something in hand");

            var can = stack.Collectible as BlockWateringCan;
            Assert.NotNull(can, $"server: the held {stack.Collectible.Code} is a watering can");
            return can.GetRemainingWateringSeconds(stack);
        }

        /// <summary>
        /// The client's held can and whether it is pouring. Read on the client, then
        /// asserted back on the server, so a failure never strands the test there.
        /// </summary>
        static async Task<(float Seconds, EnumHandInteract HandUse)> ClientCan()
        {
            await OnClient();

            var player = Capi.World.Player;
            var stack = player.InventoryManager.ActiveHotbarSlot?.Itemstack;
            var code = stack?.Collectible.Code;
            var seconds = (stack?.Collectible as BlockWateringCan)?.GetRemainingWateringSeconds(stack);
            var use = player.Entity.Controls.HandUse;

            await OnServer();

            Assert.NotNull(code, "client: something in hand");
            Assert.NotNull(seconds, $"client: the held {code} is a watering can");
            return (seconds.Value, use);
        }

        static int HeldPortions()
        {
            var bucket = Player.Me.InventoryManager.ActiveHotbarSlot?.Itemstack;
            var source = bucket?.Collectible as ILiquidSource;
            return source?.GetContent(bucket)?.StackSize ?? 0;
        }
    }
}
