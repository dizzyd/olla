using HarmonyLib;
using Vintagestory.API.Common;

namespace olla;

public class OllaModSystem : ModSystem
{
    private Harmony harmony;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);

        // Register custom block classes
        api.RegisterBlockClass("BlockOllaFired", typeof(BlockOllaFired));

        // Register block entity classes
        api.RegisterBlockEntityClass("BlockEntityOllaFired", typeof(BlockEntityOllaFired));

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

        // Apply Harmony patches to integrate ollas with farmland water detection
        harmony = new Harmony("com.dizzyd.olla");
        harmony.PatchAll();
    }

    public override void Dispose()
    {
        // Clear the olla cache when mod is unloaded
        Patch_BEFarmland_GetNearbyWaterDistance.ClearAllCache();

        harmony?.UnpatchAll("com.dizzyd.olla");
        base.Dispose();
    }
}
