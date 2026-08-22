using System.Threading.Tasks;
using Vintagestory.API.Common;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Olla.Tests
{
    /// <summary>
    /// Which liquids an olla will take.
    ///
    /// The check used to be an exact match on "waterportion", which is fine for
    /// vanilla and wrong for every mod that adds water types. Hydrate or Diedrate
    /// replaces the item with a type-source-pollution variant set, so its codes are
    /// "waterportion-fresh-rain-clean" and friends - none of which an exact match
    /// ever accepted.
    ///
    /// Driving an actual fill needs a player holding a bucket, which a headless run
    /// has not got, so these test the predicate the interaction calls plus the codes
    /// the game really registered. That is what catches a typo'd or renamed code.
    /// </summary>
    public class OllaWaterTypes
    {
        [VsTest]
        public async Task PlainVanillaWaterIsAccepted()
        {
            Assert.True(olla.BlockOllaFired.AcceptsWaterCode("waterportion"), "vanilla waterportion");
            await Task.CompletedTask;
        }

        [VsTest]
        public async Task SaltAndLimeWaterAreRefused()
        {
            // Salt water would poison the soil - vanilla farmland even accumulates
            // salt stress from it. Lime water is a tanning reagent, not irrigation.
            Assert.False(olla.BlockOllaFired.AcceptsWaterCode("saltwaterportion"), "vanilla salt water");
            Assert.False(olla.BlockOllaFired.AcceptsWaterCode("limewaterportion"), "vanilla lime water");
            await Task.CompletedTask;
        }

        [VsTest]
        public async Task EveryVanillaCodeWeNameActuallyExists()
        {
            // The real point of running this in-game: a code that has been renamed
            // or typo'd is silently inert in a HashSet, and looks like a mod that
            // simply refuses your bucket.
            foreach (var path in new[] { "waterportion", "boilingwaterportion" })
            {
                var item = Sapi.World.GetItem(new AssetLocation("game", path));
                Assert.NotNull(item, $"game:{path} is a registered item");
                Assert.True(olla.BlockOllaFired.AcceptsWaterCode(path), $"game:{path} is accepted");
            }

            await Task.CompletedTask;
        }

        [VsTest]
        public async Task RefusedVanillaCodesAlsoReallyExist()
        {
            // Same trap in reverse: asserting we refuse a code that no longer exists
            // would pass for ever while proving nothing.
            foreach (var path in new[] { "saltwaterportion", "limewaterportion" })
            {
                Assert.NotNull(Sapi.World.GetItem(new AssetLocation("game", path)),
                    $"game:{path} is a registered item");
            }

            await Task.CompletedTask;
        }

        [VsTest]
        public async Task HydrateOrDiedrateCleanWaterIsAccepted()
        {
            // H&D's allowedVariants, restricted to the clean and fresh ones.
            // These cannot be resolved against the registry here because the mod is
            // not installed - the codes come from its waterportion.json.
            foreach (var path in new[]
            {
                "waterportion-fresh-rain-clean",
                "waterportion-fresh-distilled-clean",
                "waterportion-fresh-well-clean",
                "waterportion-boiled-natural-clean",
                "waterportion-boiled-rain-clean",
            })
            {
                Assert.True(olla.BlockOllaFired.AcceptsWaterCode(path), path);
            }

            await Task.CompletedTask;
        }

        [VsTest]
        public async Task HydrateOrDiedrateSaltAndPollutedWaterIsRefused()
        {
            foreach (var path in new[]
            {
                "waterportion-salt-well-clean",
                "waterportion-salt-well-tainted",
                "waterportion-fresh-well-muddy",
                "waterportion-fresh-well-tainted",
                "waterportion-fresh-well-poisoned",
            })
            {
                Assert.False(olla.BlockOllaFired.AcceptsWaterCode(path), path);
            }

            await Task.CompletedTask;
        }

        [VsTest]
        public async Task TheOldExactMatchWouldHaveRefusedEveryModdedWater()
        {
            // Pins the actual bug: every H&D code fails a "== waterportion" test,
            // which is why none of them ever worked rather than only the odd ones.
            foreach (var path in new[]
            {
                "waterportion-fresh-rain-clean",
                "waterportion-fresh-distilled-clean",
                "waterportion-boiled-rain-clean",
            })
            {
                Assert.NotEqual("waterportion", path, "an H&D code is never the bare vanilla one");
                Assert.True(olla.BlockOllaFired.AcceptsWaterCode(path), $"{path} accepted now");
            }

            await Task.CompletedTask;
        }

        [VsTest]
        public async Task NothingUnrelatedSlipsThrough()
        {
            Assert.False(olla.BlockOllaFired.AcceptsWaterCode("milkportion"), "milk");
            Assert.False(olla.BlockOllaFired.AcceptsWaterCode("vinegarportion"), "vinegar");
            Assert.False(olla.BlockOllaFired.AcceptsWaterCode(""), "empty code");
            Assert.False(olla.BlockOllaFired.AcceptsWaterCode(null), "null code");
            await Task.CompletedTask;
        }
    }
}
