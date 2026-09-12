using System.Collections.Generic;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Olla.Tests
{
    /// <summary>
    /// The beehive kiln fires by atmosphere: BEBeeHiveKiln.ConvertItemToBurned reads a
    /// "beehivekiln" attribute off the raw ware, keyed by how many of the three back
    /// doors stand open, and takes that entry ahead of combustibleProps. Vanilla pots
    /// come out tan, orange, red or brown from red clay and cream, gray or black from
    /// blue; the olla carries the same maps.
    ///
    /// A firing takes a whole kiln and nine game hours, so these check the map the
    /// kiln will read rather than running one: every entry resolves to a registered
    /// block, every colour has both a surface and a buried form so burial cannot dead-
    /// end, and every one has a name. A typo in any of those is silent in the JSON and
    /// surfaces as a ware that never converts or a block called "block-olla-fired-...".
    /// </summary>
    public class OllaColours
    {
        static readonly Dictionary<string, string[]> ExpectedByDoorsOpen = new()
        {
            ["red"]  = new[] { "tan", "orange", "red", "brown" },
            ["blue"] = new[] { "cream", "gray", "black", "black" },
            ["fire"] = new[] { "fire", "fire", "fire", "fire" },
        };

        [VsTest]
        public async Task ABeehiveKilnFiresEachClayToVanillasColours()
        {
            foreach (var (clay, colours) in ExpectedByDoorsOpen)
            {
                var raw = Sapi.World.GetBlock(new AssetLocation("olla", $"olla-raw-{clay}"));
                Assert.NotNull(raw, $"olla:olla-raw-{clay} is registered");

                var map = raw.Attributes?["beehivekiln"];
                Assert.True(map?.Exists == true, $"olla-raw-{clay} carries a beehivekiln map");

                for (int doorsOpen = 0; doorsOpen < 4; doorsOpen++)
                {
                    // Exactly what the kiln does with the entry.
                    var stack = map[doorsOpen.ToString()]?.AsObject<JsonItemStack>();
                    Assert.NotNull(stack, $"olla-raw-{clay} has an entry for {doorsOpen} doors open");
                    Assert.True(stack.Resolve(Sapi.World, "beehivekiln-burn"),
                        $"olla-raw-{clay} with {doorsOpen} doors open resolves");

                    var fired = stack.ResolvedItemstack.Block;
                    Assert.NotNull(fired, $"olla-raw-{clay} with {doorsOpen} doors open fires to a block");
                    Assert.Equal(colours[doorsOpen], fired.Variant["color"],
                        $"colour of olla-raw-{clay} fired with {doorsOpen} doors open");
                    Assert.Equal("normal", fired.Variant["state"],
                        "a kiln must hand back a surface olla, never a buried one");
                }
            }

            await Task.CompletedTask;
        }

        [VsTest]
        public async Task EveryFiredColourHasABuriedFormAndAName()
        {
            foreach (var colour in Colours())
            {
                foreach (var state in new[] { "normal", "buried" })
                {
                    var code = new AssetLocation("olla", $"olla-fired-{colour}-{state}");
                    var block = Sapi.World.GetBlock(code);
                    Assert.NotNull(block, $"{code} is registered");
                    Assert.Equal("BlockOllaFired", block.Class, $"{code} is the olla block class");
                    Assert.Equal("BlockEntityOllaFired", block.EntityClass, $"{code} has the olla block entity");
                    Assert.True(Lang.HasTranslation($"olla:block-olla-fired-{colour}-{state}"),
                        $"{code} has a name");
                }
            }

            await Task.CompletedTask;
        }

        static IEnumerable<string> Colours()
        {
            var seen = new HashSet<string>();
            foreach (var colours in ExpectedByDoorsOpen.Values)
                foreach (var c in colours)
                    if (seen.Add(c)) yield return c;
            // The plain clay colours a pit kiln still gives.
            foreach (var c in new[] { "blue", "fire", "red" })
                if (seen.Add(c)) yield return c;
        }
    }
}
