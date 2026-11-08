using System.Collections.Generic;
using UnityEngine;

namespace OpenWheelRacer.Race
{
    /// <summary>
    /// A map of the circuit in the top-right corner: the centreline as a ribbon of
    /// dots, the player as a bigger white dot, the AI field as dots in their own
    /// body colours. Everything is IMGUI like the rest of the overlay; the only
    /// arithmetic worth testing is the static <see cref="WorldToMap"/>, and it is
    /// deliberately pure — the draw call is that function applied to every car once
    /// a frame.
    /// </summary>
    public sealed class Minimap : MonoBehaviour
    {
        /// <summary>Map edge in pixels at 1080p, the reference height.</summary>
        public const float BaseMapSize = 200f;

        /// <summary>Gap from the screen's top-right corner in pixels at 1080p.</summary>
        public const float BaseMargin = 10f;

        /// <summary>Centreline dot size in pixels at 1080p.</summary>
        public const float BaseTrackDotSize = 3f;

        /// <summary>Player dot size in pixels at 1080p; bigger than the track dots.</summary>
        public const float BasePlayerDotSize = 7f;

        /// <summary>AI dot size in pixels at 1080p.</summary>
        public const float BaseAiDotSize = 5f;

        /// <summary>Reference height every pixel size is measured against.</summary>
        public const float ReferenceHeight = 1080f;

        /// <summary>Extra world metres around the centreline before it is fitted, so the walls and the cars on the road's edge still sit inside the map.</summary>
        public const float BoundsPadding = 12f;

        /// <summary>Panel fill behind the dots.</summary>
        public static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.55f);

        /// <summary>Centreline dot colour.</summary>
        public static readonly Color TrackDotColor = new Color(0.75f, 0.78f, 0.82f, 0.9f);

        /// <summary>Player dot colour.</summary>
        public static readonly Color PlayerDotColor = Color.white;

        /// <summary>Dot colour used for an AI car whose driver gave no colour.</summary>
        public static readonly Color AiFallbackColor = new Color(1f, 0.45f, 0.1f, 1f);

        private static Texture2D _pixel;

        private Bounds _bounds;
        private List<Vector3> _centerline;
        private VehicleController _player;
        private AiDriver[] _field;
        private float _nextBindTime;

        private static Texture2D Pixel => _pixel != null
            ? _pixel
            : _pixel = new Texture2D(1, 1);

        /// <summary>
        /// Where a world point lands inside the map rect: x across, z up (the map
        /// reads like the plan view, +z towards the top of the screen, which in
        /// GUI coordinates — y grows downwards — means the bounds minimum lands on
        /// the bottom of the rect).
        /// </summary>
        public static Vector2 WorldToMap(Vector3 world, Bounds trackBounds, Rect mapRect)
        {
            // Zero-width axis (or an inverted rect) would divide by zero; the
            // middle of the rect is the only sane answer there.
            float u = trackBounds.size.x > 0f
                ? (world.x - trackBounds.min.x) / trackBounds.size.x
                : 0.5f;
            float v = trackBounds.size.z > 0f
                ? (world.z - trackBounds.min.z) / trackBounds.size.z
                : 0.5f;

            return new Vector2(
                mapRect.xMin + u * mapRect.width,
                mapRect.yMax - v * mapRect.height);
        }

        /// <summary>
        /// The fitted world bounds of the centreline points, padded. Public so a
        /// caller with a different centreline (a future second track) can produce
        /// the bounds <see cref="WorldToMap"/> expects.
        /// </summary>
        public static Bounds FitBounds(IEnumerable<Vector3> points, float padding)
        {
            bool any = false;
            Vector3 min = Vector3.positiveInfinity;
            Vector3 max = Vector3.negativeInfinity;

            foreach (Vector3 p in points)
            {
                any = true;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            if (!any)
            {
                return new Bounds(Vector3.zero, Vector3.one);
            }

            Vector3 center = (min + max) * 0.5f;
            Vector3 size = max - min + Vector3.one * (2f * padding);
            return new Bounds(center, size);
        }

        private void Update()
        {
            // Bind lazily and re-bind while the references are missing: the map
            // appears with the scene assembly and the field lands just after it.
            if (_nextBindTime > Time.unscaledTime)
            {
                return;
            }

            _nextBindTime = Time.unscaledTime + 1f;

            if (_centerline == null)
            {
                _centerline = FlattenCenterline();
                _bounds = FitBounds(_centerline, BoundsPadding);
            }

            if (_player == null || _field == null)
            {
                // The player is the one chassis without a driver attached to it;
                // the field is every chassis that has one. One sweep answers both.
                VehicleController[] cars = FindObjectsByType<VehicleController>();
                var drivers = new List<AiDriver>(cars.Length);
                VehicleController player = null;
                for (int i = 0; i < cars.Length; i++)
                {
                    AiDriver driver = cars[i].GetComponent<AiDriver>();
                    if (driver != null)
                    {
                        drivers.Add(driver);
                    }
                    else if (player == null)
                    {
                        player = cars[i];
                    }
                }

                _field = drivers.ToArray();
                if (player != null)
                {
                    _player = player;
                }
            }
        }

        private void OnGUI()
        {
            if (!Application.isPlaying || _centerline == null || _centerline.Count == 0)
            {
                return;
            }

            float scale = Screen.height / ReferenceHeight;
            float size = BaseMapSize * scale;
            float margin = BaseMargin * scale;
            var rect = new Rect(Screen.width - size - margin, margin, size, size);

            GUI.color = PanelColor;
            GUI.DrawTexture(rect, Pixel);

            DrawDots(_centerline, TrackDotColor, BaseTrackDotSize * scale, rect);

            if (_player != null)
            {
                Vector3 position = _player.transform.position;
                DrawDot(position, PlayerDotColor, BasePlayerDotSize * scale, rect);
            }

            if (_field != null)
            {
                for (int i = 0; i < _field.Length; i++)
                {
                    AiDriver driver = _field[i];
                    if (driver == null)
                    {
                        continue;
                    }

                    Color color = driver.Color;
                    DrawDot(driver.transform.position,
                        color.a > 0f ? color : AiFallbackColor,
                        BaseAiDotSize * scale, rect);
                }
            }

            GUI.color = Color.white;
        }

        private void DrawDots(List<Vector3> worldPoints, Color color, float dotSize, Rect rect)
        {
            for (int i = 0; i < worldPoints.Count; i++)
            {
                DrawDot(worldPoints[i], color, dotSize, rect);
            }
        }

        private void DrawDot(Vector3 world, Color color, float dotSize, Rect rect)
        {
            Vector2 center = WorldToMap(world, _bounds, rect);
            GUI.color = color;
            GUI.DrawTexture(new Rect(
                center.x - dotSize * 0.5f,
                center.y - dotSize * 0.5f,
                dotSize,
                dotSize), Pixel);
        }

        private static List<Vector3> FlattenCenterline()
        {
            List<Pose> poses = TrackLayout.Centerline();
            var points = new List<Vector3>(poses.Count);
            for (int i = 0; i < poses.Count; i++)
            {
                points.Add(poses[i].position);
            }

            return points;
        }
    }
}
