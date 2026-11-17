using NUnit.Framework;
using OpenWheelRacer.Race;

namespace OpenWheelRacer.Tests.EditMode
{
    /// <summary>
    /// The garage arithmetic, without a scene: no selection exists until one is
    /// resolved, and resolving one always deals the AI three distinct rivals out of
    /// the five cars the player did not take.
    /// </summary>
    [TestFixture]
    public sealed class CarSelectionTests
    {
        [TearDown]
        public void TearDown()
        {
            CarSelection.Reset();
        }

        [Test]
        public void NoSelectionUntilResolved()
        {
            CarSelection.Reset();

            Assert.IsFalse(CarSelection.HasSelection,
                "a fresh run has no business dressing the grid in imported bodies");
            Assert.IsFalse(CarSelection.TryGetAiBodyIndex(0, out int bodyIndex),
                "no selection, no rival body to hand out");
            Assert.AreEqual(-1, bodyIndex,
                "an unresolved slot must read as the procedural body");
        }

        [Test]
        public void ResolveDealsThreeDistinctRivalsNeverThePlayersCar()
        {
            for (int player = 0; player < CarBodyCatalog.Count; player++)
            {
                CarSelection.Resolve(player);

                Assert.IsTrue(CarSelection.HasSelection);
                Assert.AreEqual(player, CarSelection.PlayerIndex);

                var seen = new System.Collections.Generic.HashSet<int>();
                for (int slot = 0; slot < AiField.DefaultOpponentCount; slot++)
                {
                    Assert.IsTrue(CarSelection.TryGetAiBodyIndex(slot, out int bodyIndex),
                        $"player {player}: slot {slot} got no rival");
                    Assert.AreNotEqual(player, bodyIndex,
                        $"player {player}: the AI was dealt the player's own car");
                    Assert.IsTrue(bodyIndex >= 0 && bodyIndex < CarBodyCatalog.Count,
                        $"player {player}: rival index {bodyIndex} is outside the catalog");
                    Assert.IsTrue(seen.Add(bodyIndex),
                        $"player {player}: rival {bodyIndex} was dealt twice");
                }
            }
        }
    }
}
