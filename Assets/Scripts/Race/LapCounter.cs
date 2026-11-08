using System;

namespace OpenWheelRacer.Race
{
    /// <summary>
    /// Checkpoint-ordered lap bookkeeping (PLAN.md Stage 3 acceptance criteria 5 and 6):
    /// a lap counts only when checkpoints 1..N-1 are crossed in order and then 0 is
    /// crossed; skipped, repeated or reversed crossings are ignored. Which way the car
    /// crossed a trigger is not something the indices can reveal, so the crossing is
    /// reported with a direction through <see cref="OnCheckpoint(int, bool)"/>.
    ///
    /// Deliberately a plain C# class with no UnityEngine and no MonoBehaviour: the
    /// clock is pushed in through <see cref="Tick"/> so EditMode tests run on an
    /// injected time source, and whatever drives it in-game (a MonoBehaviour, a
    /// fixed-step loop, a replay scrubber) stays somebody else's problem.
    ///
    /// Checkpoint index 0 is always the start/finish line.
    /// </summary>
    public sealed class LapCounter
    {
        /// <summary><see cref="LastValidCheckpoint"/> value before any checkpoint is crossed.</summary>
        public const int NoCheckpoint = -1;

        /// <summary>Value of <see cref="BestLapTime"/>/<see cref="LastLapTime"/> when unset.</summary>
        public const float NoTime = float.PositiveInfinity;

        private readonly int _checkpointCount;

        private bool _started;
        private float _currentLapTime;
        private int _lastValidCheckpoint = NoCheckpoint;
        private int _nextExpectedCheckpoint;

        /// <summary>Fires once per counted lap, with that lap's time in seconds.</summary>
        public event Action<float> LapCompleted;

        /// <param name="checkpointCount">
        /// Number of checkpoints on the track, index 0 being the start/finish line.
        /// A count of 1 is legal (a circuit whose only trigger is the line itself);
        /// every crossing of 0 then completes a lap.
        /// </param>
        public LapCounter(int checkpointCount)
        {
            if (checkpointCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(checkpointCount), checkpointCount,
                    "A track needs at least one checkpoint (the start/finish line, index 0).");
            }

            _checkpointCount = checkpointCount;
        }

        public int CheckpointCount => _checkpointCount;

        /// <summary>True once the line has been crossed for the first time.</summary>
        public bool IsRunning => _started;

        /// <summary>1-based lap in progress; 0 until the race has started.</summary>
        public int CurrentLap => _started ? CompletedLaps + 1 : 0;

        /// <summary>Laps fully completed and counted.</summary>
        public int CompletedLaps { get; private set; }

        /// <summary>Elapsed time on the lap in progress (0 before the race starts).</summary>
        public float CurrentLapTime => _currentLapTime;

        /// <summary>Time of the most recent counted lap; <see cref="NoTime"/> until one exists.</summary>
        public float LastLapTime { get; private set; } = NoTime;

        /// <summary>Fastest counted lap; <see cref="NoTime"/> until one exists.</summary>
        public float BestLapTime { get; private set; } = NoTime;

        /// <summary>Highest-index checkpoint crossed in the correct order, or <see cref="NoCheckpoint"/>.</summary>
        public int LastValidCheckpoint => _lastValidCheckpoint;

        /// <summary>The only checkpoint index that currently counts; used by reset-to-last-checkpoint.</summary>
        public int NextExpectedCheckpoint => _nextExpectedCheckpoint;

        /// <summary>Advance the lap clock. Ignored before the race starts; NaN/negative are dropped.</summary>
        public void Tick(float dt)
        {
            if (!_started)
            {
                return;
            }

            if (float.IsNaN(dt) || dt <= 0f)
            {
                return;
            }

            _currentLapTime += dt;
        }

        /// <summary>
        /// Report a checkpoint crossing in the racing direction
        /// (<see cref="OnCheckpoint(int, bool)"/> with <c>forward</c> true).
        /// Returns true only when that crossing completed a lap.
        /// </summary>
        public bool OnCheckpoint(int index)
        {
            return OnCheckpoint(index, true);
        }

        /// <summary>
        /// Report a checkpoint crossing, including the direction the car crossed it in.
        /// Returns true only when a forward crossing of the next expected checkpoint —
        /// index 0 — completed a lap.
        ///
        /// A counter sitting on indices alone cannot tell a reversed sweep of the lap
        /// (3, 2, 1, line) from a car that simply took the checkpoints out of order: the
        /// second half of that sweep lands exactly on the next expected index. The trigger
        /// volume knows which way the car went through it, so the direction is passed in
        /// here rather than guessed at.
        /// </summary>
        /// <param name="index">Crossed checkpoint; index 0 is the start/finish line.</param>
        /// <param name="forward">
        /// True when the car crossed in the racing direction. False marks a backwards
        /// crossing — reversing over a trigger, or a reversed sweep of the lap — which is
        /// always ignored, whatever the index, and touches nothing: not the expected
        /// checkpoint, the progress marker, the clock or the lap count.
        /// </param>
        public bool OnCheckpoint(int index, bool forward)
        {
            // Backwards first: a reversed crossing is not a crossing at all.
            if (!forward)
            {
                return false;
            }

            if (index < 0 || index >= _checkpointCount)
            {
                return false;
            }

            // Anything that is not the next checkpoint in sequence — skipped, repeated,
            // a second sweep of the line — is dropped without side effects.
            if (index != _nextExpectedCheckpoint)
            {
                return false;
            }

            _lastValidCheckpoint = index;
            _nextExpectedCheckpoint = (index + 1) % _checkpointCount;

            // First sweep of the line: the race starts, no lap is owed yet.
            if (!_started)
            {
                _started = true;
                _currentLapTime = 0f;
                return false;
            }

            if (index != 0)
            {
                return false;
            }

            LastLapTime = _currentLapTime;
            if (LastLapTime < BestLapTime)
            {
                BestLapTime = LastLapTime;
            }

            CompletedLaps++;
            _currentLapTime = 0f;
            LapCompleted?.Invoke(LastLapTime);
            return true;
        }

        /// <summary>Put the counter back to its cold-start state (restart / new session).</summary>
        public void Reset()
        {
            _started = false;
            _currentLapTime = 0f;
            _lastValidCheckpoint = NoCheckpoint;
            _nextExpectedCheckpoint = 0;
            CompletedLaps = 0;
            LastLapTime = NoTime;
            BestLapTime = NoTime;
        }
    }
}
