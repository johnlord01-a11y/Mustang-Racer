using System.Collections.Generic;
using UnityEngine;

namespace OpenWheelRacer.Race
{
    public enum RacePhase { Countdown, Racing, Finished }

    /// <summary>One line of the results table. Unfinished cars are reported as such, never given a time.</summary>
    public readonly struct RaceResult
    {
        public readonly string Id; public readonly bool IsPlayer; public readonly bool Finished;
        public readonly int Position; public readonly float Time;
        public RaceResult(string id, bool isPlayer, bool finished, int position, float time)
        { Id = id; IsPlayer = isPlayer; Finished = finished; Position = position; Time = time; }
        public string Label => Finished ? $"{Position}. {Id}  {TimeFormat.Format(Time)}" : $"-  {Id}  {(IsPlayer ? "DNF" : "running")}";
    }

    /// <summary>
    /// Pure race-flow model (no Unity objects): countdown 3,2,1,GO, race clock from GO, a configurable
    /// lap count, per-car finish recording in the order finishes are reported, and restart.
    /// Driving is locked while <see cref="Phase"/> is Countdown and for each car once it has finished.
    /// </summary>
    public sealed class RaceFlow
    {
        public const int DefaultLaps = 3;
        public const float DefaultCountdownSeconds = 3f;
        public const float GoLabelSeconds = 1f;

        private sealed class Entry { public string Id; public bool IsPlayer; public bool Finished; public float Time; public int Order; }

        private readonly List<Entry> _entries = new List<Entry>();
        private int _finishCount;
        private float _countdownLeft;

        public int TotalLaps { get; }
        public float CountdownSeconds { get; }
        public RacePhase Phase { get; private set; }
        public float RaceTime { get; private set; }
        public bool PlayerFinished { get; private set; }

        public RaceFlow(int totalLaps = DefaultLaps, float countdownSeconds = DefaultCountdownSeconds)
        {
            TotalLaps = Mathf.Max(1, totalLaps);
            CountdownSeconds = Mathf.Max(0f, countdownSeconds);
            Restart();
        }

        public void Register(string id, bool isPlayer)
        {
            if (Find(id) != null) return;
            _entries.Add(new Entry { Id = id, IsPlayer = isPlayer });
        }

        public void Restart()
        {
            foreach (Entry e in _entries) { e.Finished = false; e.Time = 0f; e.Order = 0; }
            _finishCount = 0;
            RaceTime = 0f;
            PlayerFinished = false;
            _countdownLeft = CountdownSeconds;
            Phase = CountdownSeconds > 0f ? RacePhase.Countdown : RacePhase.Racing;
        }

        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            if (Phase == RacePhase.Countdown)
            {
                _countdownLeft -= dt;
                if (_countdownLeft <= 0f)
                {
                    RaceTime = -_countdownLeft; // carry the overshoot so the clock is exact from GO
                    _countdownLeft = 0f;
                    Phase = RacePhase.Racing;
                }
                return;
            }
            if (Phase == RacePhase.Racing) RaceTime += dt;
        }

        /// <summary>"3", "2", "1", then "GO" for <see cref="GoLabelSeconds"/>, then empty.</summary>
        public string CountdownLabel
        {
            get
            {
                if (Phase == RacePhase.Countdown) return Mathf.CeilToInt(_countdownLeft).ToString();
                return CountdownSeconds > 0f && RaceTime < GoLabelSeconds && Phase == RacePhase.Racing ? "GO" : "";
            }
        }

        public bool CanDrive(string id)
        {
            if (Phase != RacePhase.Racing) return false;
            Entry e = Find(id);
            return e == null || !e.Finished;
        }

        public bool IsFinished(string id) { Entry e = Find(id); return e != null && e.Finished; }

        /// <summary>Report a car's completed-lap count; returns true the first time it reaches the lap total.</summary>
        public bool OnLapCompleted(string id, int completedLaps)
        {
            Entry e = Find(id);
            if (e == null || e.Finished || Phase != RacePhase.Racing || completedLaps < TotalLaps) return false;
            e.Finished = true; e.Time = RaceTime; e.Order = ++_finishCount;
            if (e.IsPlayer) PlayerFinished = true;
            if (_finishCount == _entries.Count) Phase = RacePhase.Finished;
            return true;
        }

        public IReadOnlyList<RaceResult> Results()
        {
            var finished = new List<Entry>(); var open = new List<Entry>();
            foreach (Entry e in _entries) (e.Finished ? finished : open).Add(e);
            finished.Sort((a, b) => a.Order.CompareTo(b.Order));
            var list = new List<RaceResult>();
            foreach (Entry e in finished) list.Add(new RaceResult(e.Id, e.IsPlayer, true, e.Order, e.Time));
            foreach (Entry e in open) list.Add(new RaceResult(e.Id, e.IsPlayer, false, 0, 0f));
            return list;
        }

        private Entry Find(string id)
        {
            foreach (Entry e in _entries) if (e.Id == id) return e;
            return null;
        }
    }
}
