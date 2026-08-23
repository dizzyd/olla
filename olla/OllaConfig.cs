using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace olla
{
    /// <summary>
    /// Player-editable settings, read from and written back to
    /// <c>ModConfig/olla.json</c> in the game's data directory.
    ///
    /// Server-side only: both consumers - the irrigation tick and the moisture
    /// floor patch - run on the server, because that is where farmland moisture
    /// is simulated. Loading it on the client too would leave a config file that
    /// a player could edit with no effect while connected to someone else's game.
    /// </summary>
    public class OllaConfig
    {
        public const string FileName = "olla.json";

        /// <summary>
        /// The moisture level an olla works towards, as a fraction from 0 to 1.
        ///
        /// This caps *both* of the mod's mechanisms, which is the whole point of
        /// there being one number rather than two:
        ///
        ///   - active irrigation stops adding water once farmland reaches it
        ///   - the blended moisture floor cannot be pushed past it, however many
        ///     ollas overlap the same block
        ///
        /// 1.0 is the original behaviour - water to saturation, and let a grid of
        /// overlapping ollas blend towards a 100% floor.
        ///
        /// Farming overhauls that give crops a moisture *band* rather than
        /// "wetter is better" need this lower. Farming Revamped, for instance,
        /// damages crops held too wet and caps its own watering can at 0.75 for
        /// exactly that reason; an uncapped olla pins its whole 5x5 above that
        /// permanently and the floor never decays, so crops never get the dry
        /// spell they need. Somewhere around 0.6 suits it.
        /// </summary>
        public float IrrigationTarget { get; set; } = 1.0f;

        /// <summary>
        /// Below this, vanilla stops distinguishing moisture levels at all:
        /// BlockEntitySoilNutrition.GetGrowthRate computes
        /// <c>Math.Max(0.01, moistureLevel * 100 / 70 - 0.143)</c>, and that
        /// inner term crosses zero at 0.1001. A target of 0.01 and a target of 0
        /// therefore produce an identical growth rate - so a small non-zero value
        /// is not a safety margin, it is just a disabled mod that looks enabled.
        /// Warned about rather than clamped away, because 0 is a legitimate
        /// "turn the mod off" and anything above it is the player's call.
        /// </summary>
        private const float VanillaGrowthFloor = 0.1f;

        /// <summary>
        /// What the rest of the mod reads. Defaults to vanilla behaviour so that
        /// anything running before <see cref="Load"/> - or on the client, which
        /// never loads it - behaves as it always did rather than throwing.
        ///
        /// Settable so the in-game suite can exercise a target without writing a
        /// config file and restarting the world. Nothing caches the value, so a
        /// swap takes effect on the next farmland water check.
        /// </summary>
        public static OllaConfig Current { get; set; } = new OllaConfig();

        public static void Load(ICoreAPI api)
        {
            OllaConfig config = null;

            try
            {
                config = api.LoadModConfig<OllaConfig>(FileName);
            }
            catch (Exception e)
            {
                // Malformed JSON throws rather than returning null. Fall back to
                // defaults instead of taking the mod down, but say so loudly -
                // and do not write over what the player was trying to edit.
                api.Logger.Error("[olla] Could not read ModConfig/{0}, using defaults: {1}", FileName, e.Message);
                Current = new OllaConfig();
                return;
            }

            bool isNew = config == null;
            config ??= new OllaConfig();
            config.Sanitise(api);

            // Written back every load, not just when absent: it creates the file
            // on first run so the settings are discoverable, adds any keys a new
            // version introduces, and makes the file agree with what is actually
            // in effect after clamping.
            try
            {
                api.StoreModConfig(config, FileName);
            }
            catch (Exception e)
            {
                api.Logger.Warning("[olla] Could not write ModConfig/{0}: {1}", FileName, e.Message);
            }

            if (isNew) api.Logger.Notification("[olla] Wrote default config to ModConfig/{0}", FileName);

            // Stated on every start, because "what is your IrrigationTarget set to"
            // is the first question any report about moisture levels needs answered,
            // and a log is easier to ask someone for than a config file.
            api.Logger.Notification("[olla] IrrigationTarget {0}", config.IrrigationTarget);

            Current = config;
        }

        /// <summary>
        /// Bring the loaded values into range, and warn about ones that are in
        /// range but will not do what the player probably expects.
        /// </summary>
        private void Sanitise(ICoreAPI api)
        {
            float requested = IrrigationTarget;

            // NaN survives a naive comparison, so test for it rather than relying
            // on the clamp: GameMath.Clamp(NaN, 0, 1) returns NaN.
            if (float.IsNaN(IrrigationTarget)) IrrigationTarget = 1.0f;
            IrrigationTarget = GameMath.Clamp(IrrigationTarget, 0f, 1f);

            if (IrrigationTarget != requested && !float.IsNaN(requested))
            {
                api.Logger.Warning(
                    "[olla] IrrigationTarget {0} is outside 0-1, using {1}", requested, IrrigationTarget);
            }

            if (IrrigationTarget <= 0f)
            {
                api.Logger.Warning("[olla] IrrigationTarget is 0 - ollas will not water anything.");
            }
            else if (IrrigationTarget < VanillaGrowthFloor)
            {
                api.Logger.Warning(
                    "[olla] IrrigationTarget {0} is below {1}, where vanilla stops distinguishing " +
                    "moisture levels - crops will grow no better than on bone dry soil.",
                    IrrigationTarget, VanillaGrowthFloor);
            }
        }
    }
}
