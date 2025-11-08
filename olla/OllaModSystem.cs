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
