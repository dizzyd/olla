using System.Threading;
using HarmonyLib;
using Vintagestory.API.Common;

namespace olla;

public class OllaModSystem : ModSystem
{
    private const string HarmonyId = "com.dizzyd.olla";
    private const string WateringCanHarmonyId = "com.dizzyd.olla.wateringcan";

    // Static because in singleplayer both sides share this assembly, and a Harmony
    // patch is per process: the first side to start applies it for both.
    private static int wateringCanPatched;

    private Harmony harmony;
    private Harmony wateringCanHarmony;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);

        // Register custom block classes
        api.RegisterBlockClass("BlockOllaFired", typeof(BlockOllaFired));

        // Register block entity classes
        api.RegisterBlockEntityClass("BlockEntityOllaFired", typeof(BlockEntityOllaFired));

        // Both sides, unlike the farmland patch below. A can drains on the client as well
        // as the server, so the client has to make the same overflow refund or it runs dry
        // mid-pour on water the server says it still holds. Only the server fills the olla.
        if (Interlocked.CompareExchange(ref wateringCanPatched, 1, 0) == 0)
        {
            wateringCanHarmony = new Harmony(WateringCanHarmonyId);
            wateringCanHarmony.CreateClassProcessor(typeof(Patch_BlockWateringCan_OnHeldInteractStep)).Patch();
        }

        // Server side only. Farmland moisture is simulated on the server, and in
        // singleplayer Start() runs once per side against the same assembly - so
        // patching here registered the postfix twice and every call ran it twice.
        //
        // That was invisible while the patch only did Math.Min, which is idempotent.
        // Blending is not: a second pass folds the same ollas into the already
        // blended result, squaring their contribution.
        if (api.Side != EnumAppSide.Server) return;

        // Same reason, and it has to come before the patch: the postfix reads
        // IrrigationTarget on every farmland water check.
        OllaConfig.Load(api);

        harmony = new Harmony(HarmonyId);
        harmony.CreateClassProcessor(typeof(Patch_BEFarmland_GetNearbyWaterDistance)).Patch();
    }

    public override void Dispose()
    {
        // Clear the olla cache when mod is unloaded
        Patch_BEFarmland_GetNearbyWaterDistance.ClearAllCache();

        harmony?.UnpatchAll(HarmonyId);

        // Only the side that applied the can patch removes it, and it releases the guard
        // so the next world loaded in this process patches again.
        if (wateringCanHarmony != null)
        {
            wateringCanHarmony.UnpatchAll(WateringCanHarmonyId);
            Volatile.Write(ref wateringCanPatched, 0);
        }

        base.Dispose();
    }
}
