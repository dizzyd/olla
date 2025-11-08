using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace olla
{
    /// <summary>
    /// Harmony patches to integrate ollas with Vintage Story's farmland water detection system.
    /// This allows farmland to "see" buried ollas as water sources during catch-up,
    /// preventing the race condition where farmland dries out before olla irrigation runs.
    /// </summary>
    [HarmonyPatch(typeof(BlockEntityFarmland), "GetNearbyWaterDistance")]
    public class Patch_BEFarmland_GetNearbyWaterDistance
    {
        /// <summary>
        /// Cache of olla positions for each farmland block to avoid repeated searches.
        /// Key: farmland position, Value: (olla position, distance)
        /// BlockPos implements GetHashCode() and Equals() properly, so we can use it directly as a key.
        /// Thread-safe: Uses ConcurrentDictionary because farmland updates may happen on multiple threads
        /// during chunk loading/unloading operations.
        /// </summary>
        private static readonly ConcurrentDictionary<BlockPos, (BlockPos ollaPos, float distance)> ollaCache = new();
        
        /// <summary>
        /// After farmland searches for water blocks, also search for buried ollas with water.
        /// This ensures farmland maintains a minimum moisture level (minMoisture) based on
        /// proximity to ollas, just like it does with ponds/water blocks.
        /// Uses caching to avoid repeated 5x5 searches for the same farmland blocks.
        /// </summary>
        static void Postfix(BlockEntityFarmland __instance, ref float __result, ref object result)
        {
            // EnumWaterSearchResult is protected, so we use object and check by name
            string resultStr = result?.ToString();

            // Early exit conditions:
            // 1. Deferred: Chunk isn't fully loaded, can't search neighboring chunks
            if (resultStr == "Deferred") return;

            // 2. Found close water: Distance 0-1 gives minMoisture 0.75-1.0, ollas can't improve
            // But if water is far (distance 2-4), an olla might be closer and provide better moisture
            if (resultStr == "Found" && __result <= 1f) return;

            IBlockAccessor ba = __instance.Api.World.BlockAccessor;
            BlockPos farmlandPos = __instance.Pos;

            float closestOllaDistance = 99f;
            bool foundOlla = false;
            BlockPos cachedOllaPos = null;

            // Step 1: Check cache first
            if (ollaCache.TryGetValue(farmlandPos, out var cached))
            {
                // Verify cached olla is still valid (still exists, buried, and has water)
                // Just check block entity directly - much faster than block code/variant checks
                if (ba.GetBlockEntity(cached.ollaPos) is BlockEntityOllaFired cachedOlla &&
                    cachedOlla.IsBuried() &&
                    cachedOlla.HasWater)
                {
                    // Cache hit! Use cached result
                    closestOllaDistance = cached.distance;
                    foundOlla = true;
                    cachedOllaPos = cached.ollaPos;
                }
                else
                {
                    // Cache miss or invalid - remove from cache
                    ollaCache.TryRemove(farmlandPos, out _);
                }
            }

            // Step 2: If no valid cache, do full search
            if (!foundOlla)
            {
                // Search for buried ollas in 5x5 area around farmland (olla range is 2 blocks)
                for (int dx = -2; dx <= 2; dx++)
                {
                    for (int dz = -2; dz <= 2; dz++)
                    {
                        BlockPos ollaPos = farmlandPos.AddCopy(dx, 0, dz);

                        // Check block entity directly - skip all block code/variant string checks
                        if (ba.GetBlockEntity(ollaPos) is BlockEntityOllaFired olla &&
                            olla.IsBuried() &&
                            olla.HasWater)
                        {
                            // Use Chebyshev distance (max of absolute differences) to match vanilla behavior
                            int ollaDistance = Math.Max(Math.Abs(dx), Math.Abs(dz));
                            if (ollaDistance < closestOllaDistance)
                            {
                                closestOllaDistance = ollaDistance;
                                cachedOllaPos = ollaPos.Copy();
                                foundOlla = true;
                            }
                        }
                    }
                }

                // Cache the result if we found an olla
                if (foundOlla && cachedOllaPos != null)
                {
                    ollaCache[farmlandPos] = (cachedOllaPos, closestOllaDistance);
                }
            }

            // Step 3: If we found an olla, use the closer of: existing water source or olla
            if (foundOlla)
            {
                __result = Math.Min(__result, closestOllaDistance);

                // Set result to "Found" using reflection (EnumWaterSearchResult is protected)
                var enumType = typeof(BlockEntityFarmland).GetNestedType("EnumWaterSearchResult",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (enumType != null)
                {
                    result = Enum.Parse(enumType, "Found");
                }
            }
        }

        /// <summary>
        /// Clear entire cache (useful for world unload or mod reload).
        /// </summary>
        public static void ClearAllCache()
        {
            ollaCache.Clear();
        }
    }
}
