using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace olla
{
    public class BlockOllaFired : Block
    {
        /// <summary>
        /// Liquids an olla accepts, matched on the collectible's code path so the domain does not
        /// matter. Vanilla only has "waterportion"; Hydrate or Diedrate replaces it with a
        /// type-source-pollution variant set, of which we take only the clean, fresh ones.
        /// Salt would poison the soil, and muddy/tainted/poisoned water is meant to be purified
        /// before use - so those fall through to the rejection message below.
        /// </summary>
        private static readonly HashSet<string> AcceptedWaterCodes = new()
        {
            "waterportion",                       // vanilla
            "boilingwaterportion",                // vanilla, off a boiling pot
            "waterportion-fresh-rain-clean",      // Hydrate or Diedrate
            "waterportion-fresh-distilled-clean",
            "waterportion-fresh-well-clean",
            "waterportion-boiled-natural-clean",
            "waterportion-boiled-rain-clean",
        };

        /// <summary>
        /// Whether an olla accepts a liquid, keyed on the collectible's code path so the
        /// domain does not matter - Hydrate or Diedrate's replacements match the same way.
        /// Public so the in-game suite can check it without driving a player through a GUI.
        /// </summary>
        public static bool AcceptsWaterCode(string codePath)
        {
            return codePath != null && AcceptedWaterCodes.Contains(codePath);
        }

        /// <summary>
        /// The olla's block entity, put back on the server if it has gone missing.
        ///
        /// The game discards a block entity whose class is not registered when its chunk
        /// loads, and the next save writes the chunk without it - so a world opened once with
        /// the mod disabled keeps every olla block but loses what made it an olla. Left that
        /// way it shows no water, cannot be filled and never irrigates, until it is dug up
        /// and placed again. A restored olla starts empty: the water went with the original.
        ///
        /// Returns null on the client when there is none - the server's restore reaches the
        /// client as an ordinary block entity update - or when another block entity is in
        /// the way, which is left alone.
        /// </summary>
        public static BlockEntityOllaFired GetOrRestoreBlockEntity(IWorldAccessor world, BlockPos pos)
        {
            BlockEntity existing = world.BlockAccessor.GetBlockEntity(pos);
            if (existing is BlockEntityOllaFired be) return be;
            if (existing != null || world.Side != EnumAppSide.Server) return null;
            if (world.BlockAccessor.GetBlock(pos) is not BlockOllaFired block) return null;

            world.BlockAccessor.SpawnBlockEntity(block.EntityClass, pos);
            world.Logger.Notification("[olla] Restored a missing block entity at {0}; it starts empty", pos);
            return world.BlockAccessor.GetBlockEntity(pos) as BlockEntityOllaFired;
        }

        /// <summary>
        /// Random block ticks, so an olla nobody touches is restored too. This runs off the
        /// main thread, and no vanilla block reads a block entity here, so neither does this
        /// one: every olla the random tick lands on is queued, and OnServerGameTick looks.
        /// Ollas are few enough that the queue never notices.
        /// </summary>
        public override bool ShouldReceiveServerGameTicks(IWorldAccessor world, BlockPos pos, Random offThreadRandom, out object extra)
        {
            base.ShouldReceiveServerGameTicks(world, pos, offThreadRandom, out extra);
            return true;
        }

        public override void OnServerGameTick(IWorldAccessor world, BlockPos pos, object extra = null)
        {
            base.OnServerGameTick(world, pos, extra);
            GetOrRestoreBlockEntity(world, pos);
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            // A client whose olla lost its block entity still accepts burying and filling -
            // neither touches it client-side - because the server only hears about a click the
            // client accepted, and it is the server that restores it.
            BlockEntityOllaFired be = GetOrRestoreBlockEntity(world, blockSel.Position);
            if (be == null && world.Side == EnumAppSide.Server) return base.OnBlockInteractStart(world, byPlayer, blockSel);

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

        // be is null on a client whose olla lost its block entity - use it server-side only.
        private bool TryFillFromWaterContainer(IWorldAccessor world, IPlayer byPlayer, BlockEntityOllaFired be, ItemStack itemStack, ItemSlot slot)
        {
            // Check if the held item is a liquid source
            if (!(itemStack.Collectible is ILiquidSource liquidSource)) return false;

            // Get the content using the interface (no manual attribute access!)
            ItemStack contentStack = liquidSource.GetContent(itemStack);
            string waterCode = contentStack?.Collectible.Code?.Path;
            if (waterCode == null) return false;

            if (!AcceptsWaterCode(waterCode))
            {
                // Held liquid is water of some kind, just not one an olla will take. Say so
                // rather than silently doing nothing - otherwise it reads as a broken mod.
                if (world.Side == EnumAppSide.Server && waterCode.Contains("water"))
                {
                    (byPlayer as IServerPlayer)?.SendIngameError(
                        "olla-badwater", Lang.Get("olla:ingameerror-badwater"));
                }

                return false;
            }

            // Only process the actual transfer on server side
            if (world.Side == EnumAppSide.Server)
            {
                // Water portions are stored in 10ml units: 100 portions = 1 liter
                const float portionsPerLiter = 100f;
                
                // The total number of liters available in the stack is itemStack.Size * contentStack.Size / portionsPerLiter; we want
                float litersAvailable = contentStack.StackSize * itemStack.StackSize / portionsPerLiter;
                float spaceAvailable = be.MaxWaterCapacity - be.CurrentWaterLiters;

                if (spaceAvailable <= 0) return true;
                
                // We want to distribute number of liters to transfer across all the containers in the stack. The
                // ILiquidSource.TryTakeContent removes the same amount from every item in the stack, so we need
                // to calculate how many portions to consume by dividing by the item stack size.
                float litersToTransfer = System.Math.Min(litersAvailable, spaceAvailable);
                int portionsToConsume = (int)System.Math.Ceiling(litersToTransfer * portionsPerLiter / itemStack.StackSize);
                
                // Use the interface to remove water (handles all the attribute manipulation!)
                ItemStack takenStack = liquidSource.TryTakeContent(itemStack, portionsToConsume);
                if (takenStack != null && takenStack.StackSize > 0)
                {
                    slot.MarkDirty();

                    // Add water to olla
                    be.TryAddWater(litersToTransfer);

                    // Play sound
                    world.PlaySoundAt(new AssetLocation("sounds/block/water"), be.Pos.X, be.Pos.Y, be.Pos.Z, byPlayer);
                }
            }

            return true;
        }

        // be is null on a client whose olla lost its block entity - use it server-side only.
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
            dsc.AppendLine("Slowly collects rain when open to the sky");
            dsc.AppendLine("");
            dsc.AppendLine("Overlapping ollas blend their moisture:");
            dsc.AppendLine("two at the edge of each other's range");
            dsc.AppendLine("hold soil at 75% instead of 50%");
            dsc.AppendLine("");
            dsc.AppendLine("Use soil block to bury (dig it up to undo)");
        }
    }
}
