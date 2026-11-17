using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-shot importer for the six Mustang bodies: launches with -owr-cars (all six) or
/// -owr-cars-one N (just catalog index N). Takes each Blender-exported FBX in
/// Assets/Models/Cars/Source, normalizes it into a prefab under Assets/Resources/Cars —
/// uniform scale to the car's real length, yaw correction if the model needs it, tyres
/// grounded on the vehicle rig's ground plane, body centred on the rig's wheelbase
/// centre — and renders front/rear preview stills of every prefab it built into
/// captures/car-import so a human can check orientation and colour before the game
/// ever wears the bodies. Logs a grep-able [CAR-IMPORT] verdict per car, exits 0 when
/// every requested car imported.
/// </summary>
internal static class CarModelImport
{
    private sealed class Spec
    {
        public string Id;
        public string Fbx;
        public float TargetLength;
        public float YawDegrees;
    }

    // Real overall lengths in metres, measured on the Godot prep of the same models
    // (the '17 is the run-18 Blender envelope). Yaw 0 = the FBX already faces +Z;
    // a model that arrives backwards gets 180 here and nowhere else.
    private static readonly Spec[] Specs =
    {
        new Spec { Id = "Car_2017", Fbx = "Car_2017.fbx", TargetLength = 4.789f, YawDegrees = 0f },
        new Spec { Id = "Car_1965", Fbx = "Car_1965.fbx", TargetLength = 4.61f,  YawDegrees = 0f },
        new Spec { Id = "Car_1968", Fbx = "Car_1968.fbx", TargetLength = 4.71f,  YawDegrees = 0f },
        new Spec { Id = "Car_1970", Fbx = "Car_1970.fbx", TargetLength = 4.81f,  YawDegrees = 0f },
        new Spec { Id = "Car_1993", Fbx = "Car_1993.fbx", TargetLength = 4.56f,  YawDegrees = 0f },
        new Spec { Id = "Car_S197", Fbx = "Car_S197.fbx", TargetLength = 4.77f,  YawDegrees = 0f },
    };

    private const string SourceDir = "Assets/Models/Cars/Source";
    private const string PrefabDir = "Assets/Resources/Cars";
    private const string PreviewDir = "/home/p620/UnityProjects/OpenWheelRacer/captures/car-import";

    // The rig's ground plane in chassis-local space: WheelCollider centres ride at
    // y 0.25 on a 0.33 m radius, so tyre contact is y -0.08; the wheelbase centre is
    // midway between the front (z 1.20) and rear (z -1.25) axle lines.
    private const float GroundLocalY = -0.08f;
    private const float WheelbaseCenterZ = -0.025f;

    [InitializeOnLoadMethod]
    private static void Kick()
    {
        string[] args = Environment.GetCommandLineArgs();
        bool all = args.Contains("-owr-cars");
        int one = -1;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-owr-cars-one" && int.TryParse(args[i + 1], out int n))
            {
                one = n;
            }
        }

        if (!all && one < 0) return;

        int only = one;
        EditorApplication.delayCall += () => Run(all, only);
    }

    private static void Run(bool all, int only)
    {
        int failures = 0;
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Directory.CreateDirectory(PreviewDir);

            for (int i = 0; i < Specs.Length; i++)
            {
                if (!all && i != only) continue;
                if (!ImportOne(Specs[i]))
                {
                    failures++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        catch (Exception e)
        {
            Debug.LogError($"[CAR-IMPORT] aborted: {e}");
            failures++;
        }

        Debug.Log($"[CAR-IMPORT] done failures={failures}");
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    private static bool ImportOne(Spec spec)
    {
        string fbxPath = $"{SourceDir}/{spec.Fbx}";
        AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (source == null)
        {
            Debug.LogError($"[CAR-IMPORT] {spec.Id}: cannot load {fbxPath}");
            return false;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
        if (spec.YawDegrees != 0f)
        {
            instance.transform.localRotation = Quaternion.Euler(0f, spec.YawDegrees, 0f);
        }

        int texturesApplied = MaterializeMaterials(spec.Id, instance);

        if (!TryMeasure(instance, out Bounds raw))
        {
            Debug.LogError($"[CAR-IMPORT] {spec.Id}: no renderers to measure");
            UnityEngine.Object.DestroyImmediate(instance);
            return false;
        }

        float length = raw.size.z;
        if (raw.size.x > length * 1.2f)
        {
            Debug.LogWarning($"[CAR-IMPORT] {spec.Id}: width {raw.size.x:F2} exceeds " +
                             $"length {length:F2} - model may be sideways");
        }

        // Multiply the source root's own scale rather than replacing it: an FBX root
        // can carry the exporter's unit conversion in that scale, and stomping it
        // shrinks the car to centimetres while the bounds math still reads plausible.
        float scale = spec.TargetLength / length;
        instance.transform.localScale = instance.transform.localScale * scale;
        TryMeasure(instance, out Bounds scaled);

        var root = new GameObject(spec.Id);
        instance.transform.SetParent(root.transform, false);
        instance.transform.localPosition = new Vector3(
            -scaled.center.x,
            GroundLocalY - scaled.min.y,
            WheelbaseCenterZ - scaled.center.z);

        // The prefab as the game will wear it, measured at the root: anything that is
        // not within a few percent of the target length is a failed import, said loudly.
        TryMeasure(root, out Bounds final);
        bool lengthOk = Mathf.Abs(final.size.z - spec.TargetLength) < spec.TargetLength * 0.05f;
        if (!lengthOk)
        {
            Debug.LogError($"[CAR-IMPORT] {spec.Id}: final length {final.size.z:F3} is not " +
                           $"the target {spec.TargetLength:F2} - import is broken");
        }

        // Material census: how many distinct materials, and how many carry a texture -
        // the difference between a coloured model and a grey one.
        var renderers = instance.GetComponentsInChildren<Renderer>(true);
        var materials = renderers.SelectMany(r => r.sharedMaterials)
            .Where(m => m != null).Distinct().ToArray();
        int textured = materials.Count(m =>
            (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) ||
            (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null));

        string prefabPath = $"{PrefabDir}/{spec.Id}.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        string materialReport = string.Join(", ", materials.Select(m =>
            $"{m.name}[tex={(m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) || (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null)}]"));
        Debug.Log($"[CAR-IMPORT] {spec.Id}: raw=({raw.size.x:F2}x{raw.size.y:F2}x{raw.size.z:F2}) " +
                  $"scale={scale:F3} target_len={spec.TargetLength:F2} final_len={final.size.z:F2} " +
                  $"renderers={renderers.Length} materials={materials.Length} textured={textured} " +
                  $"texApplied={texturesApplied} prefab={prefabPath} mats: {materialReport}");

        RenderPreview(root, spec.Id);
        UnityEngine.Object.DestroyImmediate(root);
        return lengthOk;
    }

    /// <summary>
    /// Put every material the body wears onto a copy saved as a real asset, fixing on
    /// the way the three things the FBX importer gets wrong for these models:
    /// (1) base-colour texture links are severed — the converter dumps each material's
    /// map as a PNG with a manifest, and the copies get them back;
    /// (2) alpha leaks in from the source materials and whole bodies render ghosted —
    /// every copy is forced opaque (the glass in these models is dark smoked stock;
    /// opaque, it reads as near-black glass instead of an X-ray panel);
    /// (3) an emission link on a source material blows the body out to featureless
    /// white under the preview sun — emission is zeroed on every copy.
    /// The imported materials inside the FBX stay untouched. Returns how many slots
    /// received a texture.
    /// </summary>
    private static int MaterializeMaterials(string carId, GameObject instance)
    {
        string texDir = $"Assets/Models/Cars/Textures/{carId}";
        var map = new System.Collections.Generic.Dictionary<string, string>();
        string manifestPath = $"{texDir}/manifest.json";
        if (File.Exists(manifestPath))
        {
            foreach (System.Text.RegularExpressions.Match match in
                     System.Text.RegularExpressions.Regex.Matches(
                         File.ReadAllText(manifestPath), "\"([^\"]+)\"\\s*:\\s*\"([^\"]+)\""))
            {
                map[match.Groups[1].Value] = match.Groups[2].Value;
            }
        }

        string matDir = $"Assets/Models/Cars/Materials/{carId}";
        Directory.CreateDirectory(matDir);
        AssetDatabase.ImportAsset(matDir);

        var clones = new System.Collections.Generic.Dictionary<string, Material>();
        int textured = 0;
        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            Material[] slots = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < slots.Length; i++)
            {
                Material source = slots[i];
                if (source == null)
                {
                    continue;
                }

                if (!clones.TryGetValue(source.name, out Material clone))
                {
                    clone = new Material(source) { name = source.name };

                    if (map.TryGetValue(source.name, out string texFile))
                    {
                        string texPath = $"{texDir}/{texFile}";
                        AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);
                        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                        if (texture != null)
                        {
                            if (clone.HasProperty("_BaseMap")) clone.SetTexture("_BaseMap", texture);
                            if (clone.HasProperty("_MainTex")) clone.SetTexture("_MainTex", texture);
                            textured++;
                        }
                        else
                        {
                            Debug.LogWarning($"[CAR-IMPORT] {carId}: manifest texture " +
                                             $"{texPath} did not import");
                        }
                    }

                    ForceOpaqueNoEmission(clone);
                    string safe = string.Concat(source.name.Select(
                        c => char.IsLetterOrDigit(c) || c == '_' ? c : '_'));
                    string assetPath = $"{matDir}/{safe}.mat";
                    if (AssetDatabase.LoadAssetAtPath<Material>(assetPath) != null)
                    {
                        AssetDatabase.DeleteAsset(assetPath); // a re-import replaces, never duplicates
                    }

                    AssetDatabase.CreateAsset(clone, assetPath);
                    clones[source.name] = clone;
                }

                slots[i] = clone;
                changed = true;
            }

            if (changed)
            {
                renderer.sharedMaterials = slots;
            }
        }

        return textured;
    }

    /// <summary>One material, made safe to wear: opaque surface, no emission.</summary>
    private static void ForceOpaqueNoEmission(Material material)
    {
        // Community models arrive with mixed triangle winding (one of them was
        // mirrored in an earlier life), and a culled front face reads as a ghosted,
        // see-through body. Double-sided costs nothing at this scene size and makes
        // every body solid regardless of how its triangles wind.
        if (material.HasProperty("_Cull"))
        {
            material.SetFloat("_Cull", 0f);
        }

        if (material.HasProperty("_EmissionColor"))
        {
            material.SetColor("_EmissionColor", Color.black);
        }

        if (material.HasProperty("_EmissionMap"))
        {
            material.SetTexture("_EmissionMap", null);
        }

        material.DisableKeyword("_EMISSION");

        if (material.HasProperty("_Surface")) // URP Lit
        {
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_Blend", 0f);
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

        if (material.HasProperty("_Mode")) // built-in Standard
        {
            material.SetFloat("_Mode", 0f);
            material.DisableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }

        // The subtler half of the ghost: FBX-imported transparent materials carry
        // depth-write OFF, and an opaque material that does not write depth lets
        // whatever draws after it - the seats, the far door - paint straight over
        // the panel in front of it. Force the full opaque state: depth write on,
        // blending factors at the opaque defaults.
        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", 1f);
        }

        if (material.HasProperty("_SrcBlend"))
        {
            material.SetFloat("_SrcBlend", 1f); // BlendMode.One
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetFloat("_DstBlend", 0f); // BlendMode.Zero
        }

        material.renderQueue = -1;
    }

    private static bool TryMeasure(GameObject instance, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return found;
    }

    /// <summary>
    /// Two stills of the normalized prefab on a lit ground plane - front three-quarter
    /// from +Z (the way the car faces) and rear three-quarter from -Z - so orientation
    /// (grille vs tail panel) and colour are checkable at a glance.
    /// </summary>
    private static void RenderPreview(GameObject carRoot, string id)
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.localScale = Vector3.one * 4f;
        ground.transform.position = new Vector3(0f, GroundLocalY, 0f);
        Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (lit != null)
        {
            var groundMaterial = new Material(lit);
            groundMaterial.SetColor(
                groundMaterial.HasProperty("_BaseColor") ? "_BaseColor" : "_Color",
                new Color(0.32f, 0.32f, 0.34f));
            ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
        }

        var lightObject = new GameObject("PreviewSun");
        lightObject.transform.rotation = Quaternion.Euler(52f, -34f, 0f);
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.05f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.34f, 0.36f, 0.40f);

        var cameraObject = new GameObject("PreviewCamera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.55f, 0.62f, 0.72f);

        Shoot(camera, new Vector3(3.6f, 1.7f, 5.4f), $"{id}_front.png");
        Shoot(camera, new Vector3(-3.6f, 1.7f, -5.4f), $"{id}_rear.png");

        UnityEngine.Object.DestroyImmediate(ground);
        UnityEngine.Object.DestroyImmediate(lightObject);
        UnityEngine.Object.DestroyImmediate(cameraObject);
    }

    private static void Shoot(Camera camera, Vector3 position, string fileName)
    {
        camera.transform.position = position;
        camera.transform.LookAt(new Vector3(0f, 0.55f, 0f));

        var target = new RenderTexture(800, 500, 24);
        camera.targetTexture = target;
        camera.Render();

        RenderTexture.active = target;
        var image = new Texture2D(800, 500, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 800, 500), 0, 0);
        image.Apply();
        RenderTexture.active = null;
        camera.targetTexture = null;

        File.WriteAllBytes(Path.Combine(PreviewDir, fileName), image.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(image);
        UnityEngine.Object.DestroyImmediate(target);
    }
}
