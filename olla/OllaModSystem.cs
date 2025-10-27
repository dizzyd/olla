using Vintagestory.API.Common;

namespace olla;

public class OllaModSystem : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        // Register custom item/block classes here as needed
        // Example: api.RegisterItemClass(Mod.Info.ModID + ".itemname", typeof(ItemClassName));
    }
}
