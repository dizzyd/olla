using System;
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
        private const int IrrigationRange = 2; // 2 blocks in each direction = 5x5 area (25 blocks max)
        private const float MaxWateringIntensityPerBlock = 0.1f; // Max amount to water per block per tick

        // Water consumption; we use 0.1f intensity when watering, so this
        // maps the amount of water used to an intensity of 1.0f.
        private const float LitersPerIntensity = 1.0f; // 0.1f = 0.1L, so 1.0f = 1L

        private float currentWaterLiters = 0f;
        private int lastBlocksIrrigated = 0;

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
        }

        private void OnServerGameTick(float dt)
        {
            if (Api?.World == null) return;

            // Update irrigation
            UpdateIrrigation();
        }

        private void UpdateIrrigation()
        {
            if (Api?.World?.BlockAccessor == null) return;
            if (!HasWater) return;

            int blocksIrrigated = 0;
            float totalWaterUsed = 0f;

            // Update soil moisture in 5x5 area around olla
            for (int dx = -IrrigationRange; dx <= IrrigationRange; dx++)
            {
                for (int dz = -IrrigationRange; dz <= IrrigationRange; dz++)
                {
                    BlockPos soilPos = Pos.AddCopy(dx, 0, dz);
                    float waterUsed = UpdateSoilMoisture(soilPos);
                    if (waterUsed > 0)
                    {
                        blocksIrrigated++;
                        totalWaterUsed += waterUsed;
                    }
                }
            }

            // Track for display purposes
            lastBlocksIrrigated = blocksIrrigated;

            // Consume water based on actual amount used
            if (totalWaterUsed > 0)
            {
                currentWaterLiters = Math.Max(0, currentWaterLiters - totalWaterUsed);
                MarkDirty();
            }
        }

        private float UpdateSoilMoisture(BlockPos pos)
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

                // Get current moisture level
                float currentMoisture = farmland.MoistureLevel;

                // Calculate how much water needed to reach 1.0 (fully moist)
                float moistureDeficit = 1.0f - currentMoisture;

                // If already at full moisture, nothing to do
                if (moistureDeficit <= 0f) return 0f;

                // Use the smaller of: deficit or max watering intensity
                float wateringAmount = Math.Min(moistureDeficit, MaxWateringIntensityPerBlock);

                // Apply water to soil
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

            float amountToAdd = Math.Min(liters, MaxWaterLiters - currentWaterLiters);
            currentWaterLiters += amountToAdd;
            MarkDirty();
            return true;
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            currentWaterLiters = tree.GetFloat("currentWaterLiters");
            lastBlocksIrrigated = tree.GetInt("lastBlocksIrrigated");
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetFloat("currentWaterLiters", currentWaterLiters);
            tree.SetInt("lastBlocksIrrigated", lastBlocksIrrigated);
        }

        public override void GetBlockInfo(IPlayer forPlayer, System.Text.StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            dsc.AppendLine($"Water: {currentWaterLiters:F1}L / {MaxWaterLiters}L");

            if (HasWater)
            {
                dsc.AppendLine($"Blocks watered this tick: {lastBlocksIrrigated}");

                // Max cost is when soil is completely dry
                float maxCostPerBlock = MaxWateringIntensityPerBlock * LitersPerIntensity;
                dsc.AppendLine($"Max cost: {maxCostPerBlock:F2}L per dry block");
            }
        }
    }
}
