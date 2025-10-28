using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
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

            // Check if player is holding a soil block to bury the olla
            if (TryBuryWithSoil(world, byPlayer, blockSel, be, itemStack, activeSlot))
            {
                return true;
            }

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
                // Water portions are stored in 10ml units: 100 portions = 1 liter
                const float portionsPerLiter = 100f;

                int waterPortions = contentStack.StackSize;
                float litersAvailable = waterPortions / portionsPerLiter;

                // Try to add water to olla
                float spaceAvailable = be.MaxWaterCapacity - be.CurrentWaterLiters;
                if (spaceAvailable <= 0)
                {
                    return true;
                }

                float litersToTransfer = System.Math.Min(litersAvailable, spaceAvailable);
                int portionsToConsume = (int)System.Math.Ceiling(litersToTransfer * portionsPerLiter);

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
                be.TryAddWater(litersToTransfer);

                // Play sound
                world.PlaySoundAt(new AssetLocation("sounds/block/water"), be.Pos.X, be.Pos.Y, be.Pos.Z, byPlayer);
            }

            return true;
        }

        private bool TryBuryWithSoil(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, BlockEntityOllaFired be, ItemStack itemStack, ItemSlot slot)
        {
            // Check if this is already buried
            if (Variant["state"] == "buried") return false;

            // Check if item is a soil block
            if (itemStack.Collectible.Code?.Path?.Contains("soil") != true) return false;

            // Only process on server side
            if (world.Side == EnumAppSide.Server)
            {
                // Get the color variant
                string color = Variant["color"];

                // Create new block code for buried state
                string newBlockCode = $"olla:olla-fired-{color}-buried";
                Block newBlock = world.GetBlock(new AssetLocation(newBlockCode));

                if (newBlock == null) return false;

                // Save block entity data
                ITreeAttribute beData = new TreeAttribute();
                be.ToTreeAttributes(beData);

                // Replace block
                world.BlockAccessor.SetBlock(newBlock.BlockId, blockSel.Position);

                // Restore block entity data
                BlockEntityOllaFired newBe = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityOllaFired;
                if (newBe != null)
                {
                    newBe.FromTreeAttributes(beData, world);
                    newBe.MarkDirty(true);
                }

                // Consume soil block if not in creative mode
                if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
                {
                    slot.TakeOut(1);
                    slot.MarkDirty();
                }

                // Play sound
                world.PlaySoundAt(new AssetLocation("sounds/block/dirt"), blockSel.Position.X, blockSel.Position.Y, blockSel.Position.Z, byPlayer);
            }

            return true;
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            dsc.AppendLine("Capacity: 60 liters");
            dsc.AppendLine("Irrigation: 5x5 area");
            dsc.AppendLine("Slow, steady watering over time");
            dsc.AppendLine("Closest blocks get more water");
            dsc.AppendLine("Farther blocks get less water");
            dsc.AppendLine("~30L to fully saturate 24 blocks");
            dsc.AppendLine("");
            dsc.AppendLine("Use soil block to bury (permanent)");
        }
    }
}
