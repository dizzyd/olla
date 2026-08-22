using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace olla
{
    public class BlockEntityOllaFired : BlockEntity
    {
        // Water storage
        private const float MaxWaterLiters = 60f;
        /// <summary>
        /// 2 blocks in each direction = 5x5 area (25 blocks max).
        /// Public so the farmland water patch searches the same radius it irrigates.
        /// </summary>
        public const int IrrigationRange = 2;

        // Watering rate: max moisture intensity per game hour (per block at distance 0-1)
        // ~0.48 per hour means fully saturating a block takes about 2 game hours
        // Farther blocks get reduced rate (rate / distance)
        private const float MaxWateringIntensityPerGameHour = 0.48f;

        // Water consumption: intensity × 1.25 = liters
        // Fully saturating one block (0 to 1.0) costs 1.25L
        // 24 blocks * 1.25L = 30L total to fully saturate the area
        private const float LitersPerIntensity = 1.25f;

        // Max catch-up time when chunk loads after being unloaded (like BEFarmland)
        // Prevents extreme fast-forwarding if world time jumps significantly
        private const double MaxCatchUpDays = 365.0;

        private float currentWaterLiters = 0f;
        private int lastBlocksIrrigated = 0;
        private double lastTickTotalHours = 0;

        public float CurrentWaterLiters => currentWaterLiters;
        public float MaxWaterCapacity => MaxWaterLiters;
        public bool HasWater => currentWaterLiters > 0;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            if (api.Side == EnumAppSide.Server)
            {
                RegisterGameTickListener(OnServerGameTick, 5000); // Check every 5 seconds
            }

            // A new olla in the world may be inside some farmland's search radius.
            Patch_BEFarmland_GetNearbyWaterDistance.InvalidateOllaCache();
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            Patch_BEFarmland_GetNearbyWaterDistance.InvalidateOllaCache();
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            Patch_BEFarmland_GetNearbyWaterDistance.InvalidateOllaCache();
        }

        private void OnServerGameTick(float dt)
        {
            // Only update when chunk is fully loaded (like BEFarmland)
            if (!(Api as ICoreServerAPI).World.IsFullyLoadedChunk(Pos)) return;

            if (Api?.World == null) return;

            double currentTotalHours = Api.World.Calendar.TotalHours;

            // Initialize last tick time on first run
            if (lastTickTotalHours == 0)
            {
                lastTickTotalHours = currentTotalHours;
                return;
            }

            // Calculate elapsed game time (includes time when chunk was unloaded!)
            double hoursElapsed = currentTotalHours - lastTickTotalHours;

            // Time rollback safety: handle cases where saved time is ahead of current time
            // (can happen with schematic imports or world time manipulation)
            if (hoursElapsed < 0)
            {
                lastTickTotalHours = currentTotalHours;
                return;
            }

            lastTickTotalHours = currentTotalHours;

            // Catch up irrigation with time scaling (handles both real-time and unloaded time)
            CatchUpIrrigation(hoursElapsed);
        }

        /// <summary>
        /// Process irrigation for elapsed time using interval-based catch-up.
        /// This simulates olla irrigation even when the chunk was unloaded,
        /// similar to how BEFarmland catches up crop growth.
        /// </summary>
        private void CatchUpIrrigation(double totalHoursElapsed)
        {
            if (Api?.World?.BlockAccessor == null) return;
            if (!HasWater) return;
            if (!IsBuried()) return;

            // Cap catch-up time to prevent extreme fast-forwarding (same as BEFarmland)
            double maxCatchUpHours = MaxCatchUpDays * Api.World.Calendar.HoursPerDay;
            totalHoursElapsed = Math.Min(totalHoursElapsed, maxCatchUpHours);

            // Process irrigation in intervals (3-4 hour intervals like BEFarmland)
            // This provides more realistic simulation than doing it all at once
            double hoursProcessed = 0;
            double intervalHours = 3.0 + Api.World.Rand.NextDouble(); // Random 3-4 hours

            while (hoursProcessed < totalHoursElapsed && HasWater)
            {
                // Calculate hours for this interval (don't exceed remaining time)
                double hoursThisInterval = Math.Min(intervalHours, totalHoursElapsed - hoursProcessed);

                // Do the irrigation for this interval
                UpdateIrrigation(hoursThisInterval);

                // Advance time
                hoursProcessed += hoursThisInterval;

                // Randomize next interval (3-4 hours)
                intervalHours = 3.0 + Api.World.Rand.NextDouble();

                // Early exit if we ran out of water
                if (!HasWater) break;
            }
        }

        public bool IsBuried()
        {
            if (Api?.World?.BlockAccessor == null) return false;

            var block = Api.World.BlockAccessor.GetBlock(Pos);
            if (block == null) return false;

            // Check if the block has a "state" variant and if it's "buried"
            return block.Variant?.ContainsKey("state") == true && block.Variant["state"] == "buried";
        }

        private void UpdateIrrigation(double hoursElapsed)
        {
            if (Api?.World?.BlockAccessor == null) return;
            if (!HasWater) return;

            // Only irrigate if buried - ollas work through subsurface irrigation
            if (!IsBuried()) return;

            int blocksIrrigated = 0;
            float totalWaterUsed = 0f;

            // Build list of all positions in 5x5 area with their distances
            var positions = new List<(BlockPos pos, int distance)>();
            for (int dx = -IrrigationRange; dx <= IrrigationRange; dx++)
            {
                for (int dz = -IrrigationRange; dz <= IrrigationRange; dz++)
                {
                    BlockPos soilPos = Pos.AddCopy(dx, 0, dz);
                    int distance = Math.Abs(dx) + Math.Abs(dz); // Manhattan distance
                    positions.Add((soilPos, distance));
                }
            }

            // Sort by distance (closest first) to prioritize blocks near the olla
            var sortedPositions = positions.OrderBy(p => p.distance).ThenBy(p => p.pos.X).ThenBy(p => p.pos.Z);

            // Update soil moisture, starting with closest blocks
            foreach (var (soilPos, distance) in sortedPositions)
            {
                // Stop if we're about to run out of water
                if (currentWaterLiters - totalWaterUsed <= 0) break;

                float waterUsed = UpdateSoilMoisture(soilPos, distance, hoursElapsed);
                if (waterUsed > 0)
                {
                    blocksIrrigated++;
                    totalWaterUsed += waterUsed;
                }
            }

            // Track for display purposes
            lastBlocksIrrigated = blocksIrrigated;

            // Consume water based on actual amount used
            if (totalWaterUsed > 0)
            {
                bool hadWater = HasWater;
                currentWaterLiters = Math.Max(0, currentWaterLiters - totalWaterUsed);
                MarkDirty();

                // Running dry removes this olla from every nearby farmland's moisture blend.
                if (hadWater && !HasWater) Patch_BEFarmland_GetNearbyWaterDistance.InvalidateOllaCache();
            }
        }

        private float UpdateSoilMoisture(BlockPos pos, int distance, double hoursElapsed)
        {
            var block = Api.World.BlockAccessor.GetBlock(pos);
            if (block == null) return 0f;

            // Check if this is farmland
            if (block.Code?.Path?.Contains("farmland") != true) return 0f;

            // Get the farmland block entity
            var farmlandBE = Api.World.BlockAccessor.GetBlockEntity(pos);
            if (farmlandBE == null) return 0f;

            try
            {
                // Using dynamic to avoid hard reference to BlockEntityFarmland
                dynamic farmland = farmlandBE;

                // Calculate how much water needed to reach 1.0 (fully moist)
                float moistureDeficit = 1.0f - farmland.MoistureLevel;

                // If already at full moisture, nothing to do
                if (moistureDeficit <= 0f) return 0f;

                // Scale watering rate by distance (distance 0 and 1 get full rate)
                // Farther blocks get reduced rate to simulate slower water seepage
                float hourlyRate = distance <= 1
                    ? MaxWateringIntensityPerGameHour
                    : MaxWateringIntensityPerGameHour / distance;

                // Calculate water to add based on elapsed game time
                // Use the smaller of: deficit or (rate per hour × hours elapsed)
                float wateringAmount = Math.Min(moistureDeficit, hourlyRate * (float)hoursElapsed);

                // Apply water to soil - don't water neighbors since we're already handling the full 5x5 area
                farmland.WaterFarmland(wateringAmount, false);

                // Return liters consumed (intensity × liters per unit intensity)
                return wateringAmount * LitersPerIntensity;
            }
            catch
            {
                // If moisture check or watering fails, don't count as watered
                return 0f;
            }
        }

        public bool TryAddWater(float liters)
        {
            if (currentWaterLiters >= MaxWaterLiters) return false;

            bool hadWater = HasWater;
            float amountToAdd = Math.Min(liters, MaxWaterLiters - currentWaterLiters);
            currentWaterLiters += amountToAdd;
            MarkDirty();

            // Filling an empty olla adds it back into every nearby farmland's moisture blend.
            if (!hadWater && HasWater) Patch_BEFarmland_GetNearbyWaterDistance.InvalidateOllaCache();

            return true;
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            currentWaterLiters = tree.GetFloat("currentWaterLiters");
            lastBlocksIrrigated = tree.GetInt("lastBlocksIrrigated");
            lastTickTotalHours = tree.GetDouble("lastTickTotalHours");
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetFloat("currentWaterLiters", currentWaterLiters);
            tree.SetInt("lastBlocksIrrigated", lastBlocksIrrigated);
            tree.SetDouble("lastTickTotalHours", lastTickTotalHours);
        }

        public override void GetBlockInfo(IPlayer forPlayer, System.Text.StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            dsc.AppendLine($"Water: {currentWaterLiters:F1}L / {MaxWaterLiters}L");

            bool isBuried = IsBuried();

            if (!isBuried)
            {
                dsc.AppendLine("Not buried - cannot irrigate");
                dsc.AppendLine("Use soil block to bury");
            }
            else if (HasWater)
            {
                dsc.AppendLine("Irrigating");
                dsc.AppendLine($"Blocks watered last tick: {lastBlocksIrrigated}");
            }
            else
            {
                dsc.AppendLine("Buried - ready to irrigate when filled");
            }
        }
    }
}
