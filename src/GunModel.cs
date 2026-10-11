using UnityEngine;

namespace ValheimFirearms
{
    /// <summary>
    /// Modèle procédural d'AK-47. Repère « arme » : +Z vers la bouche, +Y vers le haut, +X à droite du tireur,
    /// origine au niveau de la détente. Cotes en mètres, proches de l'arme réelle (≈ 1 m hors tout ici, un peu
    /// plus longue que l'original pour garder les proportions à l'échelle des personnages de Valheim).
    /// </summary>
    internal static class GunModel
    {
        public const int Steel = 0, Wood = 1, DarkSteel = 2, Brass = 3;
        public const float BoreY = 0.012f;
        public static readonly Vector3 MuzzlePos = new Vector3(0, BoreY, 0.64f);
        public static readonly Vector3 EjectionPos = new Vector3(0.026f, 0.036f, 0.05f);
        public static readonly Vector3 MagPivot = new Vector3(0, -0.03f, 0.088f);

        private static Material[] _mats;

        public static Material[] Materials(Material litTemplate)
        {
            if (_mats != null) return _mats;
            var steelTex = Procedural.MetalTexture(new Color(0.16f, 0.16f, 0.17f), 1f, 11); steelTex.name = "steel";
            var darkTex = Procedural.MetalTexture(new Color(0.1f, 0.1f, 0.105f), 0.6f, 23); darkTex.name = "darksteel";
            var woodTex = Procedural.WoodTexture(new Color(0.30f, 0.11f, 0.04f), new Color(0.62f, 0.29f, 0.11f), 7); woodTex.name = "wood";
            var brassTex = Procedural.MetalTexture(new Color(0.72f, 0.52f, 0.2f), 0.3f, 41); brassTex.name = "brass";
            _mats = new[]
            {
                Procedural.LitMaterial(litTemplate, steelTex, Color.white, 0.55f, 0.45f),
                Procedural.LitMaterial(litTemplate, woodTex, Color.white, 0f, 0.35f),
                Procedural.LitMaterial(litTemplate, darkTex, Color.white, 0.5f, 0.3f),
                Procedural.LitMaterial(litTemplate, brassTex, Color.white, 0.8f, 0.6f),
            };
            return _mats;
        }

        private static GameObject Part(string name, Transform parent, Mesh mesh, Material[] mats)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            return go;
        }

        public static GameObject BuildRifle(Material litTemplate)
        {
            var mats = Materials(litTemplate);
            var root = new GameObject("vfa_ak47");

            // --- Corps (tout ce qui ne bouge pas)
            var b = new MeshBuilder(3) { UvScale = 4f };
            // Carcasse + couvercle de culasse arrondi
            b.Box(Steel, new Vector3(-0.022f, -0.03f, -0.11f), new Vector3(0.022f, 0.045f, 0.17f));
            b.Frustum(Steel, -0.115f, 0.125f, new Vector3(0.045f, 0.06f, 0.0205f), new Vector3(0.045f, 0.06f, 0.0205f));
            b.Cylinder(Steel, new Vector3(0, 0.058f, 0), 0.0195f, -0.115f, 0.125f, 12);
            // Talon de carcasse et crosse bois
            b.Box(Steel, new Vector3(-0.02f, -0.028f, -0.135f), new Vector3(0.02f, 0.038f, -0.11f));
            b.Frustum(Wood, -0.38f, -0.135f, new Vector3(-0.14f, 0.016f, 0.022f), new Vector3(-0.032f, 0.038f, 0.019f));
            b.Frustum(Steel, -0.392f, -0.38f, new Vector3(-0.142f, 0.018f, 0.023f), new Vector3(-0.142f, 0.018f, 0.023f));
            // Poignée pistolet inclinée
            const float gw = 0.016f;
            b.Hexa(Wood, new[]
            {
                new Vector3(-gw, -0.14f, -0.12f), new Vector3(gw, -0.14f, -0.12f), new Vector3(gw, -0.03f, -0.072f), new Vector3(-gw, -0.03f, -0.072f),
                new Vector3(-gw, -0.14f, -0.075f), new Vector3(gw, -0.14f, -0.075f), new Vector3(gw, -0.03f, -0.03f), new Vector3(-gw, -0.03f, -0.03f),
            });
            // Pontet et détente
            b.Box(Steel, new Vector3(-0.006f, -0.062f, -0.035f), new Vector3(0.006f, -0.056f, 0.058f));
            b.Box(Steel, new Vector3(-0.006f, -0.062f, 0.052f), new Vector3(0.006f, -0.03f, 0.06f));
            b.Box(Steel, new Vector3(-0.003f, -0.052f, 0.004f), new Vector3(0.003f, -0.03f, 0.013f));
            // Sélecteur (côté droit)
            b.Box(Steel, new Vector3(0.022f, 0.004f, -0.085f), new Vector3(0.026f, 0.028f, 0.1f));
            b.Box(Steel, new Vector3(0.022f, -0.012f, -0.1f), new Vector3(0.027f, 0.006f, -0.07f));
            // Hausse
            b.Box(Steel, new Vector3(-0.016f, 0.045f, 0.15f), new Vector3(0.016f, 0.07f, 0.2f));
            b.Box(Steel, new Vector3(-0.012f, 0.07f, 0.15f), new Vector3(0.012f, 0.077f, 0.225f));
            // Garde-main inférieur (bois) + bagues
            b.Frustum(Wood, 0.175f, 0.39f, new Vector3(-0.036f, 0.022f, 0.026f), new Vector3(-0.03f, 0.02f, 0.022f));
            b.Frustum(Steel, 0.165f, 0.178f, new Vector3(-0.038f, 0.025f, 0.027f), new Vector3(-0.038f, 0.025f, 0.027f));
            b.Frustum(Steel, 0.388f, 0.402f, new Vector3(-0.032f, 0.022f, 0.023f), new Vector3(-0.032f, 0.022f, 0.023f));
            // Garde-main supérieur (bois) sur le tube à gaz
            b.Frustum(Wood, 0.205f, 0.37f, new Vector3(0.026f, 0.07f, 0.019f), new Vector3(0.03f, 0.067f, 0.016f));
            b.Cylinder(Steel, new Vector3(0, 0.05f, 0), 0.0125f, 0.36f, 0.47f, 10);
            // Canon, bloc d'emprunt de gaz, guidon, frein de bouche, baguette
            b.Cylinder(Steel, new Vector3(0, BoreY, 0), 0.0115f, 0.17f, 0.6f, 12, 0.0095f);
            b.Box(Steel, new Vector3(-0.015f, -0.004f, 0.46f), new Vector3(0.015f, 0.064f, 0.49f));
            b.Box(Steel, new Vector3(-0.012f, 0.0f, 0.548f), new Vector3(0.012f, 0.05f, 0.575f));
            b.Box(Steel, new Vector3(-0.0022f, 0.05f, 0.558f), new Vector3(0.0022f, 0.074f, 0.565f));
            b.Box(Steel, new Vector3(-0.0125f, 0.05f, 0.555f), new Vector3(-0.008f, 0.078f, 0.568f));
            b.Box(Steel, new Vector3(0.008f, 0.05f, 0.555f), new Vector3(0.0125f, 0.078f, 0.568f));
            b.Cylinder(DarkSteel, new Vector3(0, BoreY, 0), 0.0135f, 0.6f, 0.635f, 10);
            b.Cylinder(Steel, new Vector3(0, -0.014f, 0), 0.004f, 0.395f, 0.585f, 6);
            Part("body", root.transform, b.Build("vfa_ak47_body"), new[] { mats[Steel], mats[Wood], mats[DarkSteel] });

            // --- Ensemble mobile : levier d'armement + porte-culasse (recule à chaque tir)
            var bolt = new MeshBuilder(1) { UvScale = 4f };
            bolt.Box(0, new Vector3(0.018f, 0.022f, 0.0f), new Vector3(0.0235f, 0.04f, 0.115f));
            bolt.Box(0, new Vector3(0.0235f, 0.024f, 0.105f), new Vector3(0.042f, 0.034f, 0.12f));
            bolt.Cylinder(0, new Vector3(0.044f, 0.029f, 0), 0.008f, 0.1f, 0.125f, 8);
            Part("bolt", root.transform, bolt.Build("vfa_ak47_bolt"), new[] { mats[Steel] });

            // --- Chargeur courbe de 30 coups (pivot au puits de chargeur)
            var mag = new MeshBuilder(1) { UvScale = 4f };
            const int N = 8; const float mw = 0.015f;
            for (int i = 0; i < N; i++)
            {
                float t0 = i / (float)N, t1 = (i + 1) / (float)N;
                float y0 = -t0 * 0.22f, y1 = -t1 * 0.22f;
                float c0 = 0.085f * t0 * t0, c1 = 0.085f * t1 * t1;   // courbure vers l'avant
                float d0 = Mathf.Lerp(0.058f, 0.072f, t0) / 2, d1 = Mathf.Lerp(0.058f, 0.072f, t1) / 2;
                mag.Hexa(0, new[]
                {
                    new Vector3(-mw, y1, c1 - d1), new Vector3(mw, y1, c1 - d1), new Vector3(mw, y0, c0 - d0), new Vector3(-mw, y0, c0 - d0),
                    new Vector3(-mw, y1, c1 + d1), new Vector3(mw, y1, c1 + d1), new Vector3(mw, y0, c0 + d0), new Vector3(-mw, y0, c0 + d0),
                });
            }
            mag.Box(0, new Vector3(-0.018f, -0.232f, 0.085f - 0.04f), new Vector3(0.018f, -0.218f, 0.085f + 0.04f));
            var magGo = Part("magazine", root.transform, mag.Build("vfa_ak47_mag"), new[] { mats[DarkSteel] });
            magGo.transform.localPosition = MagPivot;

            new GameObject("muzzle").transform.SetParent(root.transform, false);
            root.transform.Find("muzzle").localPosition = MuzzlePos;
            new GameObject("ejection").transform.SetParent(root.transform, false);
            root.transform.Find("ejection").localPosition = EjectionPos;
            return root;
        }

        /// <summary>Boîte de cartouches : boîte vert olive + quelques cartouches posées dessus.</summary>
        public static GameObject BuildAmmoBox(Material litTemplate)
        {
            var mats = Materials(litTemplate);
            var olive = Procedural.LitMaterial(litTemplate, Procedural.MetalTexture(new Color(0.24f, 0.27f, 0.16f), 0.8f, 5), Color.white, 0.3f, 0.3f);
            var root = new GameObject("vfa_ammo");
            var b = new MeshBuilder(2) { UvScale = 6f };
            b.Box(0, new Vector3(-0.07f, 0f, -0.045f), new Vector3(0.07f, 0.06f, 0.045f));
            b.Box(0, new Vector3(-0.072f, 0.06f, -0.047f), new Vector3(0.072f, 0.066f, 0.047f));
            for (int i = 0; i < 5; i++)
            {
                float x = -0.04f + i * 0.02f;
                b.Cylinder(1, new Vector3(x, 0.072f, 0), 0.0056f, -0.028f, 0.01f, 8);          // étui laiton
                b.Cylinder(1, new Vector3(x, 0.072f, 0), 0.0045f, 0.01f, 0.03f, 8, 0.0012f);     // ogive
            }
            Part("box", root.transform, b.Build("vfa_ammo_box"), new[] { olive, mats[Brass] });
            return root;
        }

        /// <summary>Étui éjecté (petit cylindre laiton).</summary>
        public static Mesh CasingMesh()
        {
            var b = new MeshBuilder(1);
            b.Cylinder(0, Vector3.zero, 0.0056f, -0.019f, 0.019f, 6, 0.0045f);
            return b.Build("vfa_casing");
        }
    }
}
