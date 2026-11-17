using UnityEngine;

namespace OpenWheelRacer
{
    /// <summary>
    /// The six real Mustang bodies a car can wear instead of the procedural
    /// <see cref="MustangMeshBuilder"/> body: the player's 2017 GT convertible and the five
    /// CC-BY community models from the Godot build, converted GLB -&gt; FBX and imported under
    /// <c>Assets/Models/Cars</c>. Each entry is a prefab under <c>Resources/Cars</c>, already
    /// normalized by the editor import pass — real-world length, nose towards +Z, grounded
    /// so its tyres sit on the rig's ground plane — so wearing one is a plain instantiate
    /// under the car's <see cref="VehicleController.BodyRootName"/> root.
    ///
    /// The bodies are single joined meshes with their wheels baked in: they bring no
    /// colliders (any that sneak in are stripped here, the same rule the procedural
    /// builder lives by) and their wheels neither spin nor steer — the rig's
    /// WheelVisuals are simply never built for a car wearing one, so nothing doubles.
    /// </summary>
    public static class CarBodyCatalog
    {
        /// <summary>How many bodies the catalog holds: the player's car plus five rivals.</summary>
        public const int Count = 6;

        /// <summary>Index of the player's own car in the catalog.</summary>
        public const int PlayerCarIndex = 0;

        /// <summary>Catalog ids, in menu order; also the Resources prefab names.</summary>
        public static readonly string[] CarIds =
        {
            "Car_2017", "Car_1965", "Car_1968", "Car_1970", "Car_1993", "Car_S197"
        };

        /// <summary>Names the select screen shows, in menu order.</summary>
        public static readonly string[] DisplayNames =
        {
            "2017 Mustang GT Convertible (Yours)",
            "1965 Mustang Convertible",
            "1968 Mustang GT Convertible",
            "1970 Mustang",
            "1993 Mustang GT Foxbody",
            "Mustang S197 (PS1 Style)"
        };

        /// <summary>
        /// Attribution for the five community bodies, shown on the select screen; the full
        /// text lives in the project's CREDITS.md.
        /// </summary>
        public const string CreditsLine =
            "Rival models (CC BY 4.0, via Sketchfab): 1965 Convertible - Kristian LaGrange " +
            "(@kristian123) - 1968 GT Convertible - shindedhirendra780 - 1970 - mmorton1 - " +
            "1993 Foxbody GT - Randomness (@throwbackthursdaymodels) - S197 PS1 - _gun (@mrgun)";

        /// <summary>Whether a usable prefab exists for <paramref name="index"/>.</summary>
        public static bool IsAvailable(int index)
        {
            return index >= 0 && index < Count && LoadPrefab(index) != null;
        }

        /// <summary>
        /// Instantiate body <paramref name="index"/> under <paramref name="parent"/> with
        /// every collider stripped. Returns false — and instantiates nothing the caller has
        /// to clean up beyond the usual — when the index is out of range or the prefab is
        /// missing, so the caller can fall back to the procedural body.
        /// </summary>
        public static bool TryBuild(int index, Transform parent)
        {
            if (index < 0 || index >= Count || parent == null)
            {
                return false;
            }

            GameObject prefab = LoadPrefab(index);
            if (prefab == null)
            {
                Debug.LogWarning($"[CarBodyCatalog] no prefab for body {index} " +
                                 $"({CarIds[index]}); falling back to the procedural body.");
                return false;
            }

            GameObject instance = Object.Instantiate(prefab, parent);
            instance.name = CarIds[index];
            StripColliders(instance.transform);
            return true;
        }

        private static GameObject LoadPrefab(int index)
        {
            return Resources.Load<GameObject>($"Cars/{CarIds[index]}");
        }

        /// <summary>
        /// The car already has exactly one physics shape — its chassis box and four wheel
        /// colliders — so a body never brings another. The procedural builder destroys
        /// colliders on sight for the same reason.
        /// </summary>
        private static void StripColliders(Transform node)
        {
            Collider collider = node.GetComponent<Collider>();
            if (collider != null)
            {
                Object.Destroy(collider);
            }

            for (int i = 0; i < node.childCount; i++)
            {
                StripColliders(node.GetChild(i));
            }
        }
    }
}
