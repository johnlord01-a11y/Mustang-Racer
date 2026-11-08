using System.Collections;
using NUnit.Framework;
using OpenWheelRacer;
using OpenWheelRacer.Race;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace OpenWheelRacer.Tests.PlayMode
{
    /// <summary>
    /// The race flow in the real scene: the countdown holds every car, player and AI, until GO;
    /// and a restart, twice, brings back a fresh countdown, a zero race clock and the cockpit view.
    /// The default countdown and lap count are used, so no override hook is set here; the
    /// finally blocks still clear both hooks, the clock and the pause.
    /// </summary>
    [TestFixture]
    public sealed class RaceFlowPlayTests
    {
        private const int SceneLoadFrames = 10;
        private const float HeldToleranceM = 0.5f;
        private const float ReleasedMinimumM = 2f;

        [UnityTest]
        public IEnumerator Countdown_LocksAllCars_ThenReleasesThemAtGo()
        {
            try
            {
                ResetHooks();
                SceneManager.LoadScene(RaceBootstrap.TrackSceneName);
                yield return WaitForFrames(SceneLoadFrames);

                RaceSession session = Object.FindFirstObjectByType<RaceSession>();
                Assert.IsNotNull(session, "no RaceSession in the track scene");
                Assert.IsNotNull(session.Flow, "the session has no race flow");
                Assert.AreEqual(RaceFlow.DefaultLaps, session.Flow.TotalLaps, "default race is 3 laps");
                Assert.AreEqual(3, session.Flow.TotalLaps);
                Assert.AreEqual(RacePhase.Countdown, session.Flow.Phase, "the race must open on the countdown");
                Assert.Greater(session.Opponents.Count, 0, "the scene should bring an AI field");

                // Full throttle held from the very start: it must only take effect after GO.
                session.Car.SetTestControls(1f, 0f, 0f, handbrake: false);

                Vector3 playerStart = Flat(session.Car.transform.position);
                var aiStart = new Vector3[session.Opponents.Count];
                for (int i = 0; i < aiStart.Length; i++)
                {
                    Assert.IsTrue(session.Opponents[i].Car.DriveLocked, $"AI {i} not locked during countdown");
                    aiStart[i] = Flat(session.Opponents[i].Car.transform.position);
                }

                Assert.IsTrue(session.Car.DriveLocked, "player not locked during countdown");

                yield return new WaitForSeconds(2f);
                Assert.AreEqual(RacePhase.Countdown, session.Flow.Phase, "countdown ended too early");
                Assert.Less(Vector3.Distance(playerStart, Flat(session.Car.transform.position)), HeldToleranceM,
                    "the player's car moved during the countdown");
                for (int i = 0; i < aiStart.Length; i++)
                {
                    Assert.Less(Vector3.Distance(aiStart[i], Flat(session.Opponents[i].Car.transform.position)),
                        HeldToleranceM, $"AI {i} moved during the countdown");
                }

                float waited = 0f;
                while (session.Flow.Phase == RacePhase.Countdown && waited < 3f)
                {
                    waited += Time.deltaTime;
                    yield return null;
                }

                Assert.AreEqual(RacePhase.Racing, session.Flow.Phase, "the countdown never reached GO");
                Assert.IsFalse(session.Car.DriveLocked, "player still locked after GO");

                yield return new WaitForSeconds(2.5f);
                Assert.Greater(Vector3.Distance(playerStart, Flat(session.Car.transform.position)), ReleasedMinimumM,
                    "the held throttle did not drive the player's car after GO");
                for (int i = 0; i < aiStart.Length; i++)
                {
                    Assert.IsFalse(session.Opponents[i].Car.DriveLocked, $"AI {i} still locked after GO");
                    Assert.Greater(Vector3.Distance(aiStart[i], Flat(session.Opponents[i].Car.transform.position)),
                        ReleasedMinimumM, $"AI {i} did not drive away after GO");
                }

                Assert.Greater(session.Flow.RaceTime, 0f, "the race clock is not running");
            }
            finally
            {
                Cleanup();
            }
        }

        [UnityTest]
        public IEnumerator Restart_Twice_ResetsCountdownClockAndCockpit()
        {
            try
            {
                ResetHooks();
                SceneManager.LoadScene(RaceBootstrap.TrackSceneName);
                yield return WaitForFrames(SceneLoadFrames);
                yield return new WaitForSeconds(1f);

                for (int round = 1; round <= 2; round++)
                {
                    RaceSession before = Object.FindFirstObjectByType<RaceSession>();
                    Assert.IsNotNull(before, $"round {round}: no session before restart");

                    CockpitView oldCockpit = Object.FindFirstObjectByType<CockpitView>();
                    if (oldCockpit != null) oldCockpit.SetCockpit(false);

                    GamePause.RestartActiveScene();
                    yield return WaitForFrames(SceneLoadFrames);

                    RaceSession[] sessions = Object.FindObjectsByType<RaceSession>(FindObjectsSortMode.None);
                    Assert.AreEqual(1, sessions.Length, $"round {round}: expected exactly one session");
                    RaceSession session = sessions[0];
                    Assert.AreNotSame(before, session, $"round {round}: the session was not rebuilt");
                    Assert.AreEqual(RacePhase.Countdown, session.Flow.Phase, $"round {round}: no fresh countdown");
                    Assert.Less(session.Flow.RaceTime, 0.001f, $"round {round}: race clock not reset");
                    Assert.AreEqual(3, session.Flow.TotalLaps, $"round {round}: lap count not 3");
                    Assert.IsFalse(session.Flow.PlayerFinished, $"round {round}: finish state survived");
                    Assert.AreEqual(0, session.Counter.CompletedLaps, $"round {round}: laps survived");
                    Assert.IsTrue(session.Car.DriveLocked, $"round {round}: player not held for the countdown");
                    Assert.AreEqual(1f, Time.timeScale, 1e-4f, $"round {round}: clock stopped");

                    CockpitView[] cockpits = Object.FindObjectsByType<CockpitView>(FindObjectsSortMode.None);
                    Assert.AreEqual(1, cockpits.Length, $"round {round}: expected one cockpit view");
                    Assert.IsTrue(cockpits[0].CockpitActive, $"round {round}: cockpit is not the default view again");

                    yield return new WaitForSeconds(1f);
                }
            }
            finally
            {
                Cleanup();
            }
        }

        private static void ResetHooks()
        {
            RaceSession.CountdownOverrideSeconds = null;
            RaceSession.LapsOverride = null;
            GamePause.SetPaused(false);
            Time.timeScale = 1f;
        }

        private static void Cleanup()
        {
            ResetHooks();
            Scene scene = SceneManager.GetActiveScene();
            if (scene.name != RaceBootstrap.TrackSceneName) return;
            foreach (GameObject go in Object.FindObjectsByType<GameObject>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (go != null && go.scene == scene && go.transform.parent == null) Object.Destroy(go);
            }
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static IEnumerator WaitForFrames(int frames)
        {
            for (int i = 0; i < frames; i++) yield return null;
        }
    }
}
