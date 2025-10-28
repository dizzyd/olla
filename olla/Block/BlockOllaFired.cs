using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace olla
{
    public class BlockOllaFired : Block
    {
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            BlockEntityOllaFired be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityOllaFired;
            if (be == null) return base.OnBlockInteractStart(world, byPlayer, blockSel);

            ItemSlot activeSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
            if (activeSlot?.Empty != false) return base.OnBlockInteractStart(world, byPlayer, blockSel);

            ItemStack itemStack = activeSlot.Itemstack;

            // Check if player is holding a water container
            if (TryFillFromWaterContainer(world, byPlayer, be, itemStack, activeSlot))
            {
                return true;
            }

            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        private bool TryFillFromWaterContainer(IWorldAccessor world, IPlayer byPlayer, BlockEntityOllaFired be, ItemStack itemStack, ItemSlot slot)
        {
            // Check if item contains water
            var props = itemStack.ItemAttributes?["waterTightContainerProps"];
            if (props == null) return false;

            // Get the water content
            float litresPerItem = props["itemsPerLitre"].AsFloat(1);
            if (litresPerItem <= 0) return false;

            // Check if container has water
            var contents = itemStack.Attributes?.GetTreeAttribute("contents");
            if (contents == null) return false;

            ItemStack contentStack = contents.GetItemstack("0");
            if (contentStack == null) return false;

            // Check if it's water
            if (contentStack.Collectible.Code?.Path != "waterportion") return false;

            // Only process the actual transfer on server side
            if (world.Side == EnumAppSide.Server)
            {
                int waterPortions = contentStack.StackSize;
                float litersAvailable = waterPortions / litresPerItem;

                // Try to add water to olla
                float spaceAvailable = be.MaxWaterCapacity - be.CurrentWaterLiters;
                if (spaceAvailable <= 0)
                {
                    return true;
                }

                float litersToTransfer = System.Math.Min(litersAvailable, spaceAvailable);
                int portionsToConsume = (int)System.Math.Ceiling(litersToTransfer * litresPerItem);

                // Remove water from container
                contentStack.StackSize -= portionsToConsume;

                if (contentStack.StackSize <= 0)
                {
                    // Container is now empty
                    contents.RemoveAttribute("0");
                }
                else
                {
                    contents.SetItemstack("0", contentStack);
                }

                itemStack.Attributes["contents"] = contents;
                slot.MarkDirty();

                // Add water to olla
                float actualLiters = portionsToConsume / litresPerItem;
                be.TryAddWater(actualLiters);

                // Play sound
                world.PlaySoundAt(new AssetLocation("sounds/block/water"), be.Pos.X, be.Pos.Y, be.Pos.Z, byPlayer);
            }

            return true;
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            dsc.AppendLine("Capacity: 60 liters");
            dsc.AppendLine("Irrigation: 5x5 area (25 blocks max)");
            dsc.AppendLine("Smart watering: Adds only what's needed");
            dsc.AppendLine("Water amount varies by soil dryness");
        }
    }
}
