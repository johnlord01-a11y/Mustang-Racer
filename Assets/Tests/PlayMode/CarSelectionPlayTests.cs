using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using OpenWheelRacer.Race;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace OpenWheelRacer.Tests.PlayMode
{
    /// <summary>
    /// The garage selection, end to end: resolve a pick (the Foxbody), load the track
    /// the way the game does, and the grid that <see cref="RaceBootstrap"/> assembles
    /// must be four imported bodies, all different — the player's chosen car and three
    /// distinct rivals — while every car keeps the same rig and tuning it always had.
    /// The selection is reset on the way out so the rest of the suite keeps assembling
    /// the all-procedural grid it expects.
    /// </summary>
    [TestFixture]
    public sealed class CarSelectionPlayTests
    {
        /// <summary>Catalog index of the 1993 Foxbody, the pick this fixture races.</summary>
        private const int FoxbodyIndex = 4;

        /// <summary>Frames to wait for the boot to assemble before giving up on it.</summary>
        private const int AssemblyTimeoutFrames = 600;

        [TearDown]
        public void TearDown()
        {
            CarSelection.Reset();
        }

        [UnityTest]
        public IEnumerator SelectedFieldWearsFourDistinctImportedBodies()
        {
            GamePause.SetPaused(false);
            Time.timeScale = 1f;

            CarSelection.Resolve(FoxbodyIndex);
            SceneManager.LoadScene(RaceBootstrap.TrackSceneName);

            RaceSession session = null;
            for (int frame = 0; frame < AssemblyTimeoutFrames && session == null; frame++)
            {
                yield return null;

                RaceSession[] sessions = Object.FindObjectsByType<RaceSession>();
                if (sessions.Length == 1 &&
                    sessions[0].Opponents.Count == AiField.DefaultOpponentCount)
                {
                    session = sessions[0];
                }
            }

            Assert.IsNotNull(session, "the boot never assembled the race");
            yield return new WaitForSeconds(0.5f);

            VehicleController player = session.Car;
            Assert.IsNotNull(player, "the session has no player car");
            Assert.IsTrue(player.WearingImportedBody,
                "the player picked a Foxbody and is wearing the procedural body");
            Assert.AreEqual(CarBodyCatalog.CarIds[FoxbodyIndex], BodyName(player),
                "the player's body is not the car the garage picked");

            var worn = new HashSet<string> { BodyName(player) };
            foreach (AiDriver driver in session.Opponents)
            {
                Assert.IsTrue(driver.Car.WearingImportedBody,
                    $"{driver.Name} is not wearing an imported body");
                string body = BodyName(driver.Car);
                Assert.IsTrue(worn.Add(body),
                    $"{driver.Name} duplicates a body already on the grid: {body}");
            }

            Assert.AreEqual(1 + AiField.DefaultOpponentCount, worn.Count,
                "the grid is not four distinct cars");
        }

        /// <summary>
        /// The name of the body a car is wearing: the single child the body builder —
        /// imported or procedural — hangs under the body root. An imported body names
        /// that child after its catalog id.
        /// </summary>
        private static string BodyName(VehicleController car)
        {
            Transform root = car.transform.Find(VehicleController.BodyRootName);
            if (root == null || root.childCount == 0)
            {
                return null;
            }

            return root.GetChild(0).name;
        }
    }
}
