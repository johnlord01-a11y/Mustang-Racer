using System.Collections.Generic;
using UnityEngine;

namespace OpenWheelRacer
{
    /// <summary>
    /// Procedural, visual-only muscle-car convertible: generic fastback-stallion
    /// look-alike (2015-2017 generation silhouette), top DOWN.
    ///
    /// Real Mesh objects (vertices + triangles) built here - BodyShell, Hood and
    /// the fender haunches are lofted from the side profile; only tiny details
    /// (lenses, handles, exhaust, interior) are boxes emitted into meshes.
    /// NO GameObject.CreatePrimitive, NO colliders anywhere, NO logos, badges or
    /// license plates.
    ///
    /// +Z is the nose. Player paint is gloss white; AI repaints the body-panel
    /// material and enables the (disabled-by-default) stripe group.
    /// </summary>
    public static class MustangMeshBuilder
    {
        // ------------------------------------------------------------------
        // Group names (children of the BodyVisual root)
        // ------------------------------------------------------------------
        public const string PaintGroupName = "Paint";     // body panels: paintable
        public const string GlassRootName = "Windshield"; // glass, excluded from paint
        public const string TrimRootName = "Trim";        // black/chrome trim + roll hoops
        public const string LampsRootName = "Lamps";      // lights, black trunk panel
        public const string InteriorRootName = "Interior";
        public const string StripeRootName = "Stripes";   // AI cars only, starts disabled
        public const string WheelsRootName = "Wheels";    // visual wheels

        /// <summary>Groups whose renderers AiField must never tint team colour.</summary>
        public static readonly string[] NonPaintedGroups =
        {
            GlassRootName, TrimRootName, LampsRootName,
            InteriorRootName, StripeRootName, WheelsRootName
        };

        /// <summary>Gloss white player paint, per John's reference photo.</summary>
        public static readonly Color GlossWhite = new Color(0.92f, 0.92f, 0.93f);

        // Wheel visual anchors match the WheelCollider anchors exactly.
        public static readonly Vector3 FrontLeftPos = new Vector3(-0.75f, 0.25f, 1.20f);
        public static readonly Vector3 FrontRightPos = new Vector3(0.75f, 0.25f, 1.20f);
        public static readonly Vector3 RearLeftPos = new Vector3(-0.75f, 0.25f, -1.25f);
        public static readonly Vector3 RearRightPos = new Vector3(0.75f, 0.25f, -1.25f);

        // ------------------------------------------------------------------
        // Materials. The body material is created per car so each AI can be
        // repainted without touching the others; the rest are shared.
        // ------------------------------------------------------------------
        static Shader _shader;

        static Shader BodyShader
        {
            get
            {
                if (_shader == null)
                {
                    _shader = Shader.Find("Universal Render Pipeline/Lit");
                    if (_shader == null) _shader = Shader.Find("Standard");
                }
                return _shader;
            }
        }

        static Material _glass, _black, _chrome, _red, _amber, _interior,
                        _darkTrim, _tire, _rim, _stripe, _head;

        static Material Surface(string name, Color c, float metallic, float gloss)
        {
            Shader shader = BodyShader;
            if (shader == null) return null;
            var m = new Material(shader)
            {
                name = name,
                hideFlags = HideFlags.DontSave
            };
            // URP Lit keeps its albedo in _BaseColor, Standard in _Color.
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            else m.color = c;
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", gloss);
            return m;
        }

        static Material GlassMat()
        {
            if (_glass == null)
            {
                _glass = Surface("OwrGlass", new Color(0.45f, 0.55f, 0.62f, 0.55f), 0.1f, 0.9f);
                if (_glass == null) return null;
                _glass.SetFloat("_Mode", 3);
                _glass.renderQueue = 3000;
            }
            return _glass;
        }

        static Material Cached(ref Material slot, string name, Color c, float metal, float gloss)
        {
            if (slot == null) slot = Surface(name, c, metal, gloss);
            return slot;
        }

        static Material BlackMat() { return Cached(ref _black, "OwrBlack", new Color(0.045f, 0.045f, 0.05f), 0.25f, 0.8f); }
        static Material ChromeMat() { return Cached(ref _chrome, "OwrChrome", new Color(0.75f, 0.76f, 0.78f), 1f, 0.95f); }
        static Material RedMat() { return Cached(ref _red, "OwrRedLens", new Color(0.55f, 0.03f, 0.04f), 0.1f, 0.7f); }
        static Material AmberMat() { return Cached(ref _amber, "OwrAmber", new Color(0.85f, 0.35f, 0.05f), 0.1f, 0.7f); }
        static Material InteriorMat() { return Cached(ref _interior, "OwrInterior", new Color(0.10f, 0.10f, 0.115f), 0.05f, 0.35f); }
        static Material DarkTrimMat() { return Cached(ref _darkTrim, "OwrDarkTrim", new Color(0.09f, 0.09f, 0.10f), 0.2f, 0.5f); }
        static Material TireMat() { return Cached(ref _tire, "OwrTire", new Color(0.035f, 0.035f, 0.038f), 0f, 0.2f); }
        static Material RimMat() { return Cached(ref _rim, "OwrRim", new Color(0.06f, 0.06f, 0.065f), 0.75f, 0.75f); }
        static Material StripeMat() { return Cached(ref _stripe, "OwrStripe", new Color(0.85f, 0.86f, 0.88f), 0.2f, 0.8f); }
        static Material HeadMat() { return Cached(ref _head, "OwrHeadLens", new Color(0.88f, 0.90f, 0.94f), 0.4f, 0.9f); }

        // ==================================================================
        // Public build entry points
        // ==================================================================

        /// <summary>
        /// Builds every visual group under <paramref name="root"/> (the
        /// BodyVisual root): lofted body, hood, fenders, open cabin, lamps,
        /// trim, interior, disabled stripe set and four visual wheels bound to
        /// <paramref name="wheels"/> (order 0=FL 1=FR 2=RL 3=RR, nullable).
        /// Returns the per-car body-panel material so the caller can tint it
        /// (player gloss white, AI team colours).
        /// </summary>
        public static Material BuildBody(Transform root, WheelCollider[] wheels = null)
        {
            var bodyMat = Surface("OwrBody", GlossWhite, 0.35f, 0.85f);

            BuildBodyShell(root, bodyMat);
            BuildHood(root, bodyMat);
            BuildFenders(root, bodyMat);
            BuildBumperBits(root, bodyMat);
            BuildWindshield(root);
            BuildTrim(root);
            BuildLamps(root);
            BuildInterior(root);
            BuildStripes(root);
            BuildWheels(root, wheels);

            // Belt and suspenders: visuals must never collide with the world
            // or add a sixth collider to the car. Mesh parts never grow one,
            // but if a future edit reaches for a primitive, this stays true.
            foreach (Collider col in root.GetComponentsInChildren<Collider>(true))
            {
                if (col != null) Object.Destroy(col);
            }
            return bodyMat;
        }

        /// <summary>
        /// Builds one visual wheel (black multi-spoke aftermarket rim +
        /// low-profile tire, real meshes) in the layout WheelVisuals expects:
        /// anchor "name" carrying the component, mesh children under it.
        /// </summary>
        public static GameObject BuildWheelVisual(Transform parent, string name,
            Vector3 localPos, WheelCollider source = null)
        {
            var anchor = new GameObject(name);
            anchor.transform.SetParent(parent, false);
            anchor.transform.localPosition = localPos;
            WheelVisuals visuals = anchor.AddComponent<WheelVisuals>();
            if (source != null) visuals.Bind(source);

            var b = new Builder();
            AddCylinderX(b, Vector3.zero, 0.33f, 0.33f, 0.255f, 20, true); // tread
            AddDiskX(b, new Vector3(0.128f, 0, 0), 0.33f, 20, true);       // sidewall R
            AddDiskX(b, new Vector3(-0.128f, 0, 0), 0.33f, 20, false);     // sidewall L
            PartFromBuilder(anchor.transform, "Tire", b, TireMat());

            var r = new Builder();
            AddCylinderX(r, Vector3.zero, 0.205f, 0.205f, 0.20f, 18, true);            // barrel
            AddCylinderX(r, new Vector3(0.105f, 0, 0), 0.085f, 0.085f, 0.045f, 12, true); // hub cap
            for (int i = 0; i < 5; i++)
            {
                // black twin-spoke aftermarket rim: five arms, two bars each
                float a = i * 72f * Mathf.Deg2Rad;
                float cy = Mathf.Cos(a), sz = Mathf.Sin(a);
                Quaternion q = Quaternion.LookRotation(Vector3.right, new Vector3(0f, cy, sz));
                AddBox(r, new Vector3(0.108f, cy * 0.135f, sz * 0.135f), q,
                    new Vector3(0.025f, 0.20f, 0.036f));
                AddBox(r, new Vector3(0.108f, cy * 0.135f + sz * 0.035f, sz * 0.135f - cy * 0.035f), q,
                    new Vector3(0.020f, 0.20f, 0.030f));
            }
            PartFromBuilder(anchor.transform, "Rim", r, RimMat());
            return anchor;
        }

        // ==================================================================
        // Lofted shell: BodyShell. One U-shaped cross-section (floor + two
        // side walls + a top return for deck/nose) swept along Z; the cabin
        // stays open on top - this car wears no roof and no folded soft top.
        // ==================================================================
        struct Sec { public float z, wBot, wTop, yTop; }

        static void BuildBodyShell(Transform root, Material mat)
        {
            var secs = new List<Sec>
            {
                new Sec { z = -1.965f, wBot = 1.45f, wTop = 0.00f, yTop = 0.615f }, // tail closure
                new Sec { z = -1.905f, wBot = 1.66f, wTop = 1.66f, yTop = 0.635f }, // rear face
                new Sec { z = -1.500f, wBot = 1.74f, wTop = 1.74f, yTop = 0.650f }, // deck
                new Sec { z = -0.900f, wBot = 1.84f, wTop = 1.80f, yTop = 0.635f }, // rear haunch
                new Sec { z = -0.200f, wBot = 1.80f, wTop = 1.56f, yTop = 0.605f }, // cabin belt
                new Sec { z =  0.420f, wBot = 1.76f, wTop = 1.50f, yTop = 0.610f }, // cowl
                new Sec { z =  0.900f, wBot = 1.80f, wTop = 1.62f, yTop = 0.575f }, // front arch
                new Sec { z =  1.450f, wBot = 1.76f, wTop = 1.54f, yTop = 0.480f }, // nose rise
                new Sec { z =  1.800f, wBot = 1.60f, wTop = 1.32f, yTop = 0.415f }, // nose
                new Sec { z =  1.900f, wBot = 1.46f, wTop = 0.00f, yTop = 0.335f }, // nose drop
                new Sec { z =  1.955f, wBot = 1.40f, wTop = 0.00f, yTop = 0.085f }, // chin
            };

            var b = new Builder();
            const float yBot = 0.085f;

            for (int s = 0; s < secs.Count - 1; s++)
            {
                Sec a = secs[s], c = secs[s + 1];
                for (int side = -1; side <= 1; side += 2)
                {
                    // side skin, bottom rail up to the beltline
                    b.Quad(
                        new Vector3(side * a.wBot * 0.5f, yBot, a.z),
                        new Vector3(side * c.wBot * 0.5f, yBot, c.z),
                        new Vector3(side * c.wTop * 0.5f, c.yTop, c.z),
                        new Vector3(side * a.wTop * 0.5f, a.yTop, a.z));
                    // top skin (deck / hood edge / nose) from centre out to wall
                    if (a.wTop > 0.02f && c.wTop > 0.02f)
                    {
                        b.Quad(
                            new Vector3(-a.wTop * 0.5f + 0.06f, a.yTop, a.z),
                            new Vector3(-c.wTop * 0.5f + 0.06f, c.yTop, c.z),
                            new Vector3(c.wTop * 0.5f - 0.06f, c.yTop, c.z),
                            new Vector3(a.wTop * 0.5f - 0.06f, a.yTop, a.z));
                    }
                }
                // floor
                b.Quad(
                    new Vector3(-a.wBot * 0.5f, yBot, a.z),
                    new Vector3(-c.wBot * 0.5f, yBot, c.z),
                    new Vector3(c.wBot * 0.5f, yBot, c.z),
                    new Vector3(a.wBot * 0.5f, yBot, a.z));
            }
            // rear face cap
            b.Quad(
                new Vector3(-0.83f, yBot, -1.965f), new Vector3(0.83f, yBot, -1.965f),
                new Vector3(0.83f, 0.60f, -1.965f), new Vector3(-0.83f, 0.60f, -1.965f));
            PartFromBuilder(root, "BodyShell", b, mat);
        }

        // ==================================================================
        // Hood with power bulge: its own lofted object so the seam around it
        // reads as a real bolted-on hood, twin raised centre sections.
        // ==================================================================
        struct HS { public float z, w, y; }

        // Crown line of the hood, nose -> cowl. Shared by the hood and its stripes.
        static readonly float[] CrownZ = { 1.790f, 1.450f, 1.050f, 0.600f, 0.150f, -0.044f };
        static readonly float[] CrownY = { 0.398f, 0.452f, 0.520f, 0.575f, 0.618f, 0.612f };
        // Deck line, cabin -> tail. Shared by the stripes.
        static readonly float[] DeckZ = { -0.55f, -1.00f, -1.50f, -1.89f };
        static readonly float[] DeckY = { 0.641f, 0.646f, 0.656f, 0.641f };

        static void BuildHood(Transform root, Material mat)
        {
            var pts = new List<HS>();
            for (int i = 0; i < CrownZ.Length; i++)
            {
                pts.Add(new HS { z = CrownZ[i], w = 1.46f - 0.12f * CrownZ[i] / 1.79f, y = CrownY[i] + 0.010f });
            }
            pts.Sort((p, q) => q.z.CompareTo(p.z)); // nose -> cowl

            var b = new Builder();
            for (int i = 0; i < pts.Count - 1; i++)
            {
                HS a = pts[i], c = pts[i + 1];
                const float inset = 0.045f; // width of the drop at the bulge edge
                const float drop = 0.150f;  // bulge height above the body line
                // crown of the bulge
                b.Quad(
                    new Vector3(-a.w * 0.5f + inset, a.y, a.z),
                    new Vector3(-c.w * 0.5f + inset, c.y, c.z),
                    new Vector3(c.w * 0.5f - inset, c.y, c.z),
                    new Vector3(a.w * 0.5f - inset, a.y, a.z));
                for (int side = -1; side <= 1; side += 2)
                {
                    // shoulder of the bulge, dropping away
                    b.Quad(
                        new Vector3(side * (a.w * 0.5f - inset), a.y, a.z),
                        new Vector3(side * (c.w * 0.5f - inset), c.y, c.z),
                        new Vector3(side * c.w * 0.5f, c.y - 0.05f, c.z),
                        new Vector3(side * a.w * 0.5f, a.y - 0.05f, a.z));
                    // riser back down to the hood flat
                    b.Quad(
                        new Vector3(side * a.w * 0.5f, a.y - 0.05f, a.z),
                        new Vector3(side * c.w * 0.5f, c.y - 0.05f, c.z),
                        new Vector3(side * c.w * 0.5f, c.y - drop, c.z),
                        new Vector3(side * a.w * 0.5f, a.y - drop, a.z));
                }
            }
            PartFromBuilder(root, "Hood", b, mat);
        }

        // ==================================================================
        // Fenders: flared arc bands over the arches; the rears carry the
        // muscular haunch the reference photo shows.
        // ==================================================================
        static void BuildFenders(Transform root, Material mat)
        {
            BuildFender(root, mat, 1, 1.20f, 0.445f, 0.30f, 0.27f, false);
            BuildFender(root, mat, -1, 1.20f, 0.445f, 0.30f, 0.27f, false);
            BuildFender(root, mat, 1, -1.25f, 0.475f, 0.34f, 0.30f, true);
            BuildFender(root, mat, -1, -1.25f, 0.475f, 0.34f, 0.30f, true);
        }

        static void BuildFender(Transform root, Material mat, int side, float zc,
            float rIn, float flare, float width, bool haunch)
        {
            var b = new Builder();
            const int n = 14;
            const float yc = 0.25f;
            float xIn = side * (width - 0.03f);
            float xOut = side * (width + (haunch ? 0.12f : 0.08f));

            for (int i = 0; i < n; i++)
            {
                float a0 = Mathf.PI * (0.5f + i / (float)n);
                float a1 = Mathf.PI * (0.5f + (i + 1) / (float)n);
                // haunch: the flare swells toward the rear of the arch
                float f0 = flare * (haunch ? 0.55f + 0.45f * (1f - Mathf.Cos(a0)) : 1f);
                float f1 = flare * (haunch ? 0.55f + 0.45f * (1f - Mathf.Cos(a1)) : 1f);
                Vector3 oi0 = ArcPt(xOut, yc, zc, rIn, a0), oi1 = ArcPt(xOut, yc, zc, rIn, a1);
                Vector3 oo0 = ArcPt(xOut, yc, zc, rIn + f0, a0), oo1 = ArcPt(xOut, yc, zc, rIn + f1, a1);
                Vector3 ii0 = ArcPt(xIn, yc, zc, rIn, a0), ii1 = ArcPt(xIn, yc, zc, rIn, a1);
                b.Quad(oi0, oi1, oo1, oo0);  // flare face seen from outside
                b.Quad(ii0, ii1, oi1, oi0);  // arch lip turned to the wheel
            }

            if (haunch)
            {
                // rear-quarter haunch mass above and behind the arch
                AddBox(b, new Vector3(side * (width + 0.05f), 0.47f, zc + 0.50f),
                    Quaternion.identity, new Vector3(0.10f, 0.20f, 0.92f));
                AddBox(b, new Vector3(side * (width + 0.04f), 0.53f, zc + 0.98f),
                    Quaternion.identity, new Vector3(0.08f, 0.12f, 0.32f));
            }
            string nm = zc > 0f
                ? (side > 0 ? "FrontFender_R" : "FrontFender_L")
                : (side > 0 ? "RearFender_R" : "RearFender_L");
            PartFromBuilder(group(root, PaintGroupName), nm, b, mat);
        }

        static Vector3 ArcPt(float x, float yc, float zc, float r, float a)
        {
            return new Vector3(x, yc + r * Mathf.Cos(a), zc + r * Mathf.Sin(a));
        }

        // ==================================================================
        // Bumper masses, splitter, grille, diffuser, fuel door
        // ==================================================================
        static void BuildBumperBits(Transform root, Material mat)
        {
            Transform paint = group(root, PaintGroupName);
            Transform trim = group(root, TrimRootName);

            // body-coloured bumper masses
            AddPartBox(paint, "FrontBumper", mat, new Vector3(0f, 0.26f, 1.83f), new Vector3(1.68f, 0.24f, 0.24f));
            AddPartBox(paint, "RearBumper", mat, new Vector3(0f, 0.38f, -1.88f), new Vector3(1.70f, 0.26f, 0.22f));
            // ducktail lip spoiler on the trunk-lid edge, slight trailing angle
            AddPartBox(paint, "Spoiler", mat, new Vector3(0f, 0.648f, -1.845f), new Vector3(1.56f, 0.035f, 0.16f), new Vector3(-6f, 0f, 0f));
            // body-colour centre section of the dark rear diffuser
            AddPartBox(paint, "DiffuserCentre", mat, new Vector3(0f, 0.135f, -1.95f), new Vector3(0.64f, 0.12f, 0.12f));

            // round fuel door on the LEFT rear quarter (nose is +Z, left is -X)
            var fd = new Builder();
            AddDiskX(fd, Vector3.zero, 0.085f, 12, false);
            Transform fuelDoor = PartFromBuilder(paint, "FuelDoor", fd, mat).transform;
            fuelDoor.localPosition = new Vector3(-0.915f, 0.56f, -0.78f);

            // black lower valances / diffuser / splitter / rockers
            AddPartBox(trim, "FrontValance", DarkTrimMat(), new Vector3(0f, 0.14f, 1.88f), new Vector3(1.52f, 0.14f, 0.16f));
            AddPartBox(trim, "RearValance", DarkTrimMat(), new Vector3(0f, 0.18f, -1.93f), new Vector3(1.66f, 0.16f, 0.12f));
            AddPartBox(trim, "Diffuser", BlackMat(), new Vector3(0f, 0.11f, -1.99f), new Vector3(1.42f, 0.10f, 0.08f));
            AddPartBox(trim, "Splitter", BlackMat(), new Vector3(0f, 0.055f, 1.90f), new Vector3(1.60f, 0.05f, 0.22f));
            AddPartBox(trim, "Rocker_L", BlackMat(), new Vector3(-0.90f, 0.09f, -0.02f), new Vector3(0.10f, 0.09f, 1.90f));
            AddPartBox(trim, "Rocker_R", BlackMat(), new Vector3(0.90f, 0.09f, -0.02f), new Vector3(0.10f, 0.09f, 1.90f));

            // grille: plain black mouth in the lower nose - no badge, no logo
            var g = new Builder();
            g.Quad(
                new Vector3(-0.30f, 0.14f, 0f), new Vector3(0.30f, 0.14f, 0f),
                new Vector3(0.42f, 0.30f, 0f), new Vector3(-0.42f, 0.30f, 0f));
            g.Quad(
                new Vector3(0.42f, 0.30f, 0.02f), new Vector3(-0.42f, 0.30f, 0.02f),
                new Vector3(-0.30f, 0.14f, 0.02f), new Vector3(0.30f, 0.14f, 0.02f));
            Transform grille = PartFromBuilder(trim, "Grille", g, BlackMat()).transform;
            grille.localPosition = new Vector3(0f, 0f, 1.946f);
        }

        // ==================================================================
        // Windshield: raked glass in a black frame. Open cabin; no soft top.
        // ==================================================================
        static void BuildWindshield(Transform root)
        {
            Transform glassRoot = group(root, GlassRootName);
            var b = new Builder();
            b.Quad(
                new Vector3(-0.70f, 0.615f, 0.440f),
                new Vector3(0.70f, 0.615f, 0.440f),
                new Vector3(0.62f, 1.290f, 0.060f),
                new Vector3(-0.62f, 1.290f, 0.060f));
            PartFromBuilder(glassRoot, "WindshieldGlass", b, GlassMat());
        }

        // ==================================================================
        // Trim: windshield frame, mirrors, two roll hoops, exhaust tips
        // ==================================================================
        static void BuildTrim(Transform root)
        {
            Transform trim = group(root, TrimRootName);
            Material dark = DarkTrimMat();

            // A-pillars and header of the frame behind the folded-down top
            AddPartBox(trim, "Pillar_L", dark, new Vector3(-0.665f, 0.95f, 0.245f), new Vector3(0.05f, 0.72f, 0.05f), new Vector3(17f, 0f, 0f));
            AddPartBox(trim, "Pillar_R", dark, new Vector3(0.665f, 0.95f, 0.245f), new Vector3(0.05f, 0.72f, 0.05f), new Vector3(17f, 0f, 0f));
            AddPartBox(trim, "Header", dark, new Vector3(0f, 1.295f, 0.062f), new Vector3(1.30f, 0.055f, 0.06f));

            // mirrors on the beltline
            AddPartBox(trim, "Mirror_L", dark, new Vector3(-1.00f, 0.70f, 0.56f), new Vector3(0.16f, 0.08f, 0.10f));
            AddPartBox(trim, "Mirror_R", dark, new Vector3(1.00f, 0.70f, 0.56f), new Vector3(0.16f, 0.08f, 0.10f));

            // two roll hoops behind the seats — this car has no roof to hide them
            for (int s = -1; s <= 1; s += 2)
            {
                string side = s > 0 ? "_R" : "_L";
                float x = s * 0.35f;
                AddPartBox(trim, "HoopBase" + side, dark, new Vector3(x, 0.655f, -0.420f), new Vector3(0.10f, 0.06f, 0.14f));
                AddPartBox(trim, "HoopPost" + side, dark, new Vector3(x, 0.76f, -0.420f), new Vector3(0.07f, 0.20f, 0.09f));
                AddPartBox(trim, "HoopTop" + side, dark, new Vector3(x, 0.865f, -0.420f), new Vector3(0.08f, 0.06f, 0.20f));
            }

            // two large round chrome exhaust tips at the OUTER bumper corners
            foreach (int s in new[] { -1, 1 })
            {
                var e = new Builder();
                AddCylinderX(e, new Vector3(0f, 0f, -0.08f), 0.075f, 0.068f, 0.16f, 14, true);
                Transform tip = PartFromBuilder(trim, "ExhaustTip" + (s > 0 ? "_R" : "_L"), e, ChromeMat()).transform;
                tip.localPosition = new Vector3(s * 0.82f, 0.22f, -1.99f);
            }
        }

        // ==================================================================
        // Lamps: vertical tri-bar taillights, gloss black trunk panel, red
        // third-brake-light strip, corner markers, headlights
        // ==================================================================
        static void BuildLamps(Transform root)
        {
            Transform lamps = group(root, LampsRootName);
            Material red = RedMat();

            for (int s = -1; s <= 1; s += 2)
            {
                string side = s > 0 ? "_R" : "_L";
                // tri-bar cluster: three vertical red bars per corner
                AddPartBox(lamps, "TaillightBarIn" + side, red,
                    new Vector3(s * 0.765f, 0.495f, -1.795f), new Vector3(0.33f, 0.150f, 0.040f));
                AddPartBox(lamps, "TaillightBarMid" + side, red,
                    new Vector3(s * 0.765f, 0.495f, -1.867f), new Vector3(0.33f, 0.150f, 0.040f));
                AddPartBox(lamps, "TaillightBarOut" + side, red,
                    new Vector3(s * 0.765f, 0.495f, -1.939f), new Vector3(0.33f, 0.150f, 0.040f));

                AddPartBox(lamps, "Headlight" + side, HeadMat(),
                    new Vector3(s * 0.56f, 0.455f, 1.905f), new Vector3(0.30f, 0.075f, 0.05f));

                // side marker on the bumper corner, outboard of the exhaust tip
                AddPartBox(lamps, "Marker" + side, AmberMat(),
                    new Vector3(s * 0.945f, 0.30f, -1.90f), new Vector3(0.05f, 0.07f, 0.14f));
            }

            // gloss black panel spanning the trunk lid between the taillights
            AddPartBox(lamps, "TrunkPanel", BlackMat(),
                new Vector3(0f, 0.50f, -1.975f), new Vector3(1.20f, 0.155f, 0.045f));

            // small red third-brake-light strip on the trunk lip, centred,
            // just under the black panel
            AddPartBox(lamps, "ThirdBrakeLight", red,
                new Vector3(0f, 0.405f, -1.985f), new Vector3(0.30f, 0.030f, 0.030f));
        }

        // ==================================================================
        // Interior: dash, console, two seats
        // ==================================================================
        static void BuildInterior(Transform root)
        {
            Transform it = group(root, InteriorRootName);
            Material dark = InteriorMat();
            AddPartBox(it, "Dash", dark, new Vector3(0f, 0.66f, 0.30f), new Vector3(1.34f, 0.16f, 0.30f));
            AddPartBox(it, "CentreConsole", dark, new Vector3(0f, 0.575f, -0.12f), new Vector3(0.20f, 0.10f, 0.60f));
            for (int s = -1; s <= 1; s += 2)
            {
                string side = s > 0 ? "_R" : "_L";
                AddPartBox(it, "SeatPan" + side, dark, new Vector3(s * 0.35f, 0.545f, -0.14f), new Vector3(0.42f, 0.10f, 0.46f));
                AddPartBox(it, "SeatBack" + side, dark, new Vector3(s * 0.35f, 0.75f, -0.33f), new Vector3(0.40f, 0.42f, 0.10f), new Vector3(-8f, 0f, 0f));
                AddPartBox(it, "SeatHead" + side, dark, new Vector3(s * 0.35f, 1.005f, -0.375f), new Vector3(0.28f, 0.14f, 0.10f), new Vector3(-8f, 0f, 0f));
            }
        }

        // ==================================================================
        // Stripes: twin hood + deck ribbons following the body lines. Built
        // but disabled — AiField enables them on AI cars only, never on the
        // gloss-white player.
        // ==================================================================
        static void BuildStripes(Transform root)
        {
            Transform st = group(root, StripeRootName);
            Material m = StripeMat();
            for (int s = -1; s <= 1; s += 2)
            {
                string side = s > 0 ? "_R" : "_L";
                float x0 = s * 0.135f, x1 = s * 0.305f;
                Ribbon(st, "StripeHood" + side, m, x0, x1, CrownZ, CrownY, 0.016f);
                Ribbon(st, "StripeDeck" + side, m, x0, x1, DeckZ, DeckY, 0.012f);
            }
            st.gameObject.SetActive(false);
        }

        static void Ribbon(Transform parent, string name, Material m,
            float x0, float x1, float[] zs, float[] ys, float lift)
        {
            var b = new Builder();
            for (int i = 0; i < zs.Length - 1; i++)
            {
                b.Quad(
                    new Vector3(x0, ys[i] + lift, zs[i]),
                    new Vector3(x0, ys[i + 1] + lift, zs[i + 1]),
                    new Vector3(x1, ys[i + 1] + lift, zs[i + 1]),
                    new Vector3(x1, ys[i] + lift, zs[i]));
            }
            PartFromBuilder(parent, name, b, m);
        }

        // ==================================================================
        // Wheels: black multi-spoke aftermarket rims, low-profile tires,
        // bound to the sim poses they shadow. Visual only.
        // ==================================================================
        static void BuildWheels(Transform root, WheelCollider[] wheels)
        {
            Transform w = group(root, WheelsRootName);
            WheelCollider fl = wheels != null && wheels.Length > 0 ? wheels[0] : null;
            WheelCollider fr = wheels != null && wheels.Length > 1 ? wheels[1] : null;
            WheelCollider rl = wheels != null && wheels.Length > 2 ? wheels[2] : null;
            WheelCollider rr = wheels != null && wheels.Length > 3 ? wheels[3] : null;
            BuildWheelVisual(w, "Wheel_FL_Vis", FrontLeftPos, fl);
            BuildWheelVisual(w, "Wheel_FR_Vis", FrontRightPos, fr);
            BuildWheelVisual(w, "Wheel_RL_Vis", RearLeftPos, rl);
            BuildWheelVisual(w, "Wheel_RR_Vis", RearRightPos, rr);
        }

        // ==================================================================
        // Mesh plumbing
        // ==================================================================
        sealed class Builder
        {
            public readonly List<Vector3> Verts = new List<Vector3>();
            public readonly List<int> Tris = new List<int>();
            public readonly List<Vector2> Uvs = new List<Vector2>();

            public void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                int i = Verts.Count;
                Verts.Add(a); Verts.Add(b); Verts.Add(c);
                Uvs.Add(Vector2.zero); Uvs.Add(Vector2.zero); Uvs.Add(Vector2.zero);
                // emitted in both windings so surfaces read from inside the
                // open cabin as well as from the street
                Tris.Add(i); Tris.Add(i + 2); Tris.Add(i + 1);
                Tris.Add(i); Tris.Add(i + 1); Tris.Add(i + 2);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                Tri(a, b, c);
                Tri(a, c, d);
            }
        }

        static Transform group(Transform root, string name)
        {
            Transform t = root.Find(name);
            if (t == null)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                t = go.transform;
            }
            return t;
        }

        static GameObject PartFromBuilder(Transform parent, string name, Builder b, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mesh = new Mesh { name = name + "_Mesh" };
            if (b.Verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(b.Verts);
            mesh.SetUVs(0, b.Uvs);
            mesh.SetTriangles(b.Tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var f = go.AddComponent<MeshFilter>();
            f.sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            if (mat != null) r.sharedMaterial = mat;
            return go;
        }

        static void AddPartBox(Transform parent, string name, Material mat, Vector3 pos,
            Vector3 size, Vector3? euler = null)
        {
            var b = new Builder();
            AddBox(b, pos, Quaternion.Euler(euler ?? Vector3.zero), size);
            PartFromBuilder(parent, name, b, mat);
        }

        static void AddBox(Builder b, Vector3 c, Quaternion rot, Vector3 size)
        {
            Vector3 hx = rot * new Vector3(size.x * 0.5f, 0f, 0f);
            Vector3 hy = rot * new Vector3(0f, size.y * 0.5f, 0f);
            Vector3 hz = rot * new Vector3(0f, 0f, size.z * 0.5f);
            Vector3 p000 = c - hx - hy - hz, p100 = c + hx - hy - hz,
                   p110 = c + hx + hy - hz, p010 = c - hx + hy - hz,
                   p001 = c - hx - hy + hz, p101 = c + hx - hy + hz,
                   p111 = c + hx + hy + hz, p011 = c - hx + hy + hz;
            b.Quad(p001, p101, p111, p011); // +z
            b.Quad(p100, p000, p010, p110); // -z
            b.Quad(p100, p101, p111, p110); // +x
            b.Quad(p000, p010, p011, p001); // -x
            b.Quad(p010, p011, p111, p110); // +y
            b.Quad(p000, p100, p101, p001); // -y
        }

        /// <summary>Cylinder whose axis lies along X (the wheel axle).</summary>
        static void AddCylinderX(Builder b, Vector3 c, float r0, float r1, float len, int segs, bool capped)
        {
            float hx = len * 0.5f;
            for (int i = 0; i < segs; i++)
            {
                float a0 = i / (float)segs * Mathf.PI * 2f;
                float a1 = (i + 1) / (float)segs * Mathf.PI * 2f;
                Vector3 s0 = new Vector3(0f, Mathf.Cos(a0), Mathf.Sin(a0));
                Vector3 s1 = new Vector3(0f, Mathf.Cos(a1), Mathf.Sin(a1));
                b.Quad(
                    c + s0 * r0 - Vector3.right * hx,
                    c + s1 * r0 - Vector3.right * hx,
                    c + s1 * r1 + Vector3.right * hx,
                    c + s0 * r1 + Vector3.right * hx);
            }
            if (capped)
            {
                AddDiskX(b, c + Vector3.right * hx, r1, segs, true);
                AddDiskX(b, c - Vector3.right * hx, r0, segs, false);
            }
        }

        static void AddDiskX(Builder b, Vector3 c, float r, int segs, bool facePlusX)
        {
            for (int i = 0; i < segs; i++)
            {
                float a0 = i / (float)segs * Mathf.PI * 2f;
                float a1 = (i + 1) / (float)segs * Mathf.PI * 2f;
                Vector3 e0 = c + new Vector3(0f, Mathf.Cos(a0), Mathf.Sin(a0)) * r;
                Vector3 e1 = c + new Vector3(0f, Mathf.Cos(a1), Mathf.Sin(a1)) * r;
                if (facePlusX) b.Tri(c, e0, e1);
                else b.Tri(c, e1, e0);
            }
        }
    }
}
