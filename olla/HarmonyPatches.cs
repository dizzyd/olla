using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
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
    ///
    /// Vanilla turns the returned water distance into a moisture floor via
    /// minMoisture = clamp(1 - distance / 4), so distance 0/1/2/3 means 100%/75%/50%/25%.
    /// Because that distance is a float, several water sources can be blended into a single
    /// fractional distance without touching vanilla's own moisture math.
    /// </summary>
    // As of 1.22 GetNearbyWaterDistance and EnumWaterSearchResult live on
    // BlockEntitySoilNutrition, the new base class of BlockEntityFarmland.
    [HarmonyPatch(typeof(BlockEntitySoilNutrition), "GetNearbyWaterDistance")]
    public class Patch_BEFarmland_GetNearbyWaterDistance
    {
        /// <summary>
        /// Distance at which a water source stops contributing any moisture at all.
        /// Mirrors the divisor in vanilla's minMoisture = clamp(1 - distance / 4).
        /// </summary>
        private const float MoistureFalloffDistance = 4f;

        /// <summary>
        /// Cache of the irrigating ollas found for each farmland block, to avoid repeating the
        /// 5x5 search on every water check. BlockPos implements GetHashCode()/Equals() properly,
        /// so it works directly as a key.
        /// Thread-safe: farmland updates may happen on multiple threads during chunk load/unload.
        /// </summary>
        private static readonly ConcurrentDictionary<BlockPos, BlockPos[]> ollaCache = new();

        /// <summary>
        /// Bumped whenever any olla appears, disappears, or crosses the empty/non-empty line.
        /// Cached searches are only valid for the generation they were taken in - without this a
        /// farmland that already cached one olla would never notice a second one being added,
        /// which is precisely the case blending exists to reward.
        /// </summary>
        private static int generation;
        private static int lastClearedGeneration;

        /// <summary>Cached reflection lookup of the protected EnumWaterSearchResult.Found value.</summary>
        private static object foundResult;

        /// <summary>
        /// Retire every cached olla search. Called by ollas on placement, removal, burial and on
        /// the transitions into and out of "has water".
        /// </summary>
        public static void InvalidateOllaCache()
        {
            Interlocked.Increment(ref generation);
        }

        /// <summary>
        /// After farmland searches for water blocks, also search for buried ollas with water and
        /// blend every source it can see into one moisture floor.
        /// </summary>
        static void Postfix(BlockEntitySoilNutrition __instance, ref float __result, ref object result)
        {
            // The patched method now lives on the base class, which BlockEntityBerryBushFarmland
            // also derives from. Restrict to farmland to keep pre-1.22 behaviour.
            if (__instance is not BlockEntityFarmland) return;

            // EnumWaterSearchResult is protected, so we use object and check by name
            string resultStr = result?.ToString();

            // Chunk isn't fully loaded, so neighbouring chunks can't be searched. Leave the
            // deferral intact so vanilla retries rather than committing to a partial answer.
            if (resultStr == "Deferred") return;

            // Distance 0 is already a 100% moisture floor - nothing left for an olla to add.
            // Anything further out can still be improved by blending, including distance 1.
            if (resultStr == "Found" && __result <= 0f) return;

            // A target of 0 turns the mod off; skip the search rather than doing the
            // work and discovering at the end that the blend cannot beat vanilla.
            float target = OllaConfig.Current.IrrigationTarget;
            if (target <= 0f) return;

            // Clearing wholesale on a generation bump both invalidates stale entries and keeps
            // the dictionary from growing without bound as farmland is created and destroyed.
            int currentGeneration = Volatile.Read(ref generation);
            if (currentGeneration != lastClearedGeneration)
            {
                // Clear before recording the generation: a thread that repopulates in between
                // does so from a fresh search, whereas the other order can drop a fresh entry.
                ollaCache.Clear();
                lastClearedGeneration = currentGeneration;
            }

            IBlockAccessor ba = __instance.Api.World.BlockAccessor;
            BlockPos farmlandPos = __instance.Pos;

            // Re-verify the cached ollas rather than trusting them: an olla can run dry between
            // generation bumps only via paths that bump, but chunk unloads are cheap to tolerate.
            if (!ollaCache.TryGetValue(farmlandPos, out BlockPos[] ollaPositions) ||
                !AllStillIrrigating(ba, ollaPositions))
            {
                ollaPositions = FindIrrigatingOllas(ba, farmlandPos);

                if (ollaPositions.Length > 0) ollaCache[farmlandPos] = ollaPositions;
                else ollaCache.TryRemove(farmlandPos, out _);
            }

            if (ollaPositions.Length == 0) return;

            // Blend every source into a single moisture floor as a probabilistic union: each
            // source wets whatever share of the block the others left dry. Two ollas at distance
            // 2 (50% each) give 75% rather than 50%, three give 87.5%, and the result approaches
            // but never reaches 100% - full saturation still needs a source right alongside.
            //
            // The vanilla water distance joins the blend as just another source. When it found
            // nothing it is 99, which clamps to zero moisture and leaves the product untouched.
            float dryness = 1f - MoistureFromDistance(__result);
            foreach (BlockPos ollaPos in ollaPositions)
            {
                dryness *= 1f - MoistureFromDistance(ChebyshevDistance(farmlandPos, ollaPos));
            }

            // Cap the blend at the configured target. This is the ceiling that makes the
            // setting worth having: without it, overlapping ollas walk the floor towards
            // 100% no matter what the target is, and because the floor never decays the
            // farmland can never dry out again. The vanilla source is capped along with
            // the ollas here, but Math.Min below hands back whichever answer is wetter,
            // so a real pond is never worsened by an olla being nearby.
            float blended = Math.Min(1f - dryness, target);

            // Express the blended floor back as the fractional distance vanilla expects.
            float blendedDistance = DistanceFromMoisture(blended);

            // Uncapped this was always an improvement, since every olla contributes at
            // least 50%. Capped it need not be, so leave vanilla's own answer - and its
            // search result - untouched when the blend has nothing to add.
            if (blendedDistance >= __result) return;

            __result = blendedDistance;

            object found = FoundResult();
            if (found != null) result = found;
        }

        /// <summary>
        /// Search the farmland's surroundings for buried ollas that still hold water.
        /// Checking the block entity directly is far cheaper than block code/variant comparisons.
        /// </summary>
        private static BlockPos[] FindIrrigatingOllas(IBlockAccessor ba, BlockPos farmlandPos)
        {
            List<BlockPos> found = null;
            int range = BlockEntityOllaFired.IrrigationRange;

            for (int dx = -range; dx <= range; dx++)
            {
                for (int dz = -range; dz <= range; dz++)
                {
                    BlockPos ollaPos = farmlandPos.AddCopy(dx, 0, dz);

                    if (ba.GetBlockEntity(ollaPos) is BlockEntityOllaFired olla &&
                        olla.IsBuried() &&
                        olla.HasWater)
                    {
                        (found ??= new List<BlockPos>()).Add(ollaPos);
                    }
                }
            }

            return found?.ToArray() ?? Array.Empty<BlockPos>();
        }

        /// <summary>
        /// True only if every cached position still holds a buried olla with water in it.
        /// A single failure rebuilds the whole entry, which also picks up any newly added ollas.
        /// </summary>
        private static bool AllStillIrrigating(IBlockAccessor ba, BlockPos[] ollaPositions)
        {
            foreach (BlockPos ollaPos in ollaPositions)
            {
                if (ba.GetBlockEntity(ollaPos) is not BlockEntityOllaFired olla ||
                    !olla.IsBuried() ||
                    !olla.HasWater)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Vanilla's minMoisture curve: distance 0/1/2/3 gives 100%/75%/50%/25%.</summary>
        private static float MoistureFromDistance(float distance)
        {
            return GameMath.Clamp(1f - distance / MoistureFalloffDistance, 0f, 1f);
        }

        /// <summary>The inverse, so a blended moisture floor can be handed back as a distance.</summary>
        private static float DistanceFromMoisture(float moisture)
        {
            return (1f - GameMath.Clamp(moisture, 0f, 1f)) * MoistureFalloffDistance;
        }

        /// <summary>Matches the metric vanilla uses for its own water block search.</summary>
        private static int ChebyshevDistance(BlockPos a, BlockPos b)
        {
            return Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Z - b.Z));
        }

        /// <summary>
        /// EnumWaterSearchResult is protected, so the Found value has to come from reflection.
        /// It is declared on BlockEntitySoilNutrition itself, which is what GetNestedType needs -
        /// GetNestedType does not search base types.
        /// </summary>
        private static object FoundResult()
        {
            if (foundResult == null)
            {
                Type enumType = typeof(BlockEntitySoilNutrition).GetNestedType(
                    "EnumWaterSearchResult", BindingFlags.NonPublic | BindingFlags.Public);

                if (enumType != null) foundResult = Enum.Parse(enumType, "Found");
            }

            return foundResult;
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
