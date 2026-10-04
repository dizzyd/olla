using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace olla
{
    /// <summary>
    /// Lets a watering can pour into an olla.
    ///
    /// The can cannot go through BlockOllaFired's fill path. It is not an ILiquidSource -
    /// it holds seconds of pouring, not a liquid - and its own OnHeldInteractStart claims
    /// the click before the olla's OnBlockInteractStart is ever called. Left alone it
    /// drains onto the pot and nothing happens, which reads as a broken mod.
    ///
    /// Vanilla still runs in full, particles and sound included. The prefix notes how much
    /// the can held, the postfix turns whatever vanilla drained into litres, and any of it
    /// the olla had no room for goes back into the can.
    ///
    /// Patched on both sides, because vanilla drains the can on both. The server fills the
    /// olla; the client only predicts the refund from its copy of the olla, so its can does
    /// not run dry mid-pour on water the server says it still holds. That copy can trail the
    /// server by a sync as the olla fills, and the client then drains a little more than the
    /// server did - OnHeldInteractStop's MarkDirty settles that when the button comes up.
    /// </summary>
    [HarmonyPatch(typeof(BlockWateringCan), nameof(BlockWateringCan.OnHeldInteractStep))]
    public class Patch_BlockWateringCan_OnHeldInteractStep
    {
        /// <summary>
        /// Litres in a full can. Vanilla never gives the can a volume; this follows the one
        /// place it implies one - refilling from a placed bucket is written to take 5 L
        /// (though as of 1.22.0 it computes (int)(5 / itemsPerLitre) and takes nothing).
        /// It puts the can between a jug (3 L) and a bucket (10 L), so buckets stay the way
        /// to fill an olla.
        /// </summary>
        private const float LitresPerWateringCan = 5f;

        static void Prefix(BlockWateringCan __instance, ItemSlot slot, EntityAgent byEntity,
            BlockSelection blockSel, out float __state)
        {
            // -1 means "not pouring into an olla" and leaves the postfix with nothing to do.
            __state = -1f;

            if (blockSel == null || slot?.Itemstack == null) return;
            if (byEntity.World.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityOllaFired) return;

            __state = __instance.GetRemainingWateringSeconds(slot.Itemstack);
        }

        static void Postfix(BlockWateringCan __instance, ItemSlot slot, EntityAgent byEntity,
            BlockSelection blockSel, float __state, ref bool __result)
        {
            // An empty can has nothing to give; vanilla's own empty-can handling stands.
            if (__state <= 0f) return;

            if (byEntity.World.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityOllaFired olla) return;

            // Vanilla lets the remaining seconds go negative on the last step, so the most
            // that can have been poured is what the can held going in.
            float remaining = Math.Max(0f, __instance.GetRemainingWateringSeconds(slot.Itemstack));
            float pouredSeconds = __state - remaining;
            if (pouredSeconds <= 0f) return;

            float secondsPerLitre = __instance.CapacitySeconds / LitresPerWateringCan;
            float litres = pouredSeconds / secondsPerLitre;
            // Cans keep no record of where their water came from - a dropped can refills from
            // salt water too - so their contents are deliberately taken as fresh, without the
            // AcceptsWaterCode check that containers get. A known way round it, accepted.
            float accepted = Math.Min(litres, olla.MaxWaterCapacity - olla.CurrentWaterLiters);

            // Olla water is server state; the client only keeps its can in step.
            if (accepted > 0f && byEntity.World.Side == EnumAppSide.Server) olla.TryAddWater(accepted);

            // Whatever overflowed stays in the can, so topping up a nearly full olla does not
            // waste the rest.
            if (accepted < litres)
            {
                float refunded = remaining + (litres - accepted) * secondsPerLitre;
                __instance.SetRemainingWateringSeconds(slot.Itemstack, refunded);

                // A step that ran the can dry made vanilla return its stop-pouring result
                // before the refund put water back. Undo that, or a can holding less than
                // one step's worth stops over a full olla on every press. Only that path:
                // remaining is 0 exactly when vanilla took it, and it is the only return
                // after the drain, so no other cancellation is overridden.
                if (remaining <= 0f && refunded > 0f) __result = true;
            }
        }
    }
}
