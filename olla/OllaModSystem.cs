using Vintagestory.API.Common;

namespace olla;

public class OllaModSystem : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        base.Start(api);

        // Register custom block classes
        api.RegisterBlockClass("BlockOllaFired", typeof(BlockOllaFired));

        // Register block entity classes
        api.RegisterBlockEntityClass("BlockEntityOllaFired", typeof(BlockEntityOllaFired));
    }
}
