using System.Linq;
using System.Text;
using UnityEngine;

namespace ValheimFirearms
{
    /// <summary>Dump optionnel (config Debug.DumpPrefabs) de ce dont le mod dépend dans les prefabs vanilla.</summary>
    internal static class Diagnostics
    {
        public static void DumpVanilla()
        {
            var sb = new StringBuilder("\n===== ValheimFirearms dump =====\n");
            foreach (var name in new[] { "CrossbowArbalest", "BoltIron" })
            {
                var go = Jotunn.Managers.PrefabManager.Instance.GetPrefab(name);
                if (go == null) { sb.AppendLine("missing " + name); continue; }
                var id = go.GetComponent<ItemDrop>()?.m_itemData?.m_shared;
                if (id != null)
                    sb.AppendLine($"{name}: type={id.m_itemType} anim={id.m_animationState} atk={id.m_attack.m_attackAnimation} reloadAnim={id.m_attack.m_reloadAnimation} equipDur={id.m_equipDuration} skill={id.m_skillType} icons={id.m_icons?.Length}");
                DumpHierarchy(go.transform, "", sb);
            }

            sb.AppendLine("--- shaders");
            foreach (var s in Resources.FindObjectsOfTypeAll<Shader>().Select(s => s.name).Distinct().OrderBy(n => n))
                if (s.Contains("Particle") || s.Contains("Unlit") || s.StartsWith("Custom") || s.Contains("Standard") || s.Contains("Lux"))
                    sb.AppendLine("  " + s);

            sb.AppendLine("--- fx prefabs");
            foreach (var g in Jotunn.Managers.PrefabManager.Cache.GetPrefabs(typeof(GameObject)).Keys.OrderBy(k => k))
            {
                var l = g.ToLowerInvariant();
                if ((l.StartsWith("vfx_") || l.StartsWith("sfx_") || l.StartsWith("fx_")) &&
                    (l.Contains("hit") || l.Contains("spark") || l.Contains("blood") || l.Contains("splash") || l.Contains("smoke") || l.Contains("arrow") || l.Contains("bolt") || l.Contains("crossbow")))
                    sb.AppendLine("  " + g);
            }
            Plugin.Log.LogInfo(sb.ToString());
        }

        private static void DumpHierarchy(Transform t, string indent, StringBuilder sb)
        {
            sb.Append($"{indent}{t.name} lp={t.localPosition:F3} le={t.localEulerAngles:F1} ls={t.localScale:F3} layer={LayerMask.LayerToName(t.gameObject.layer)} active={t.gameObject.activeSelf}");
            foreach (var c in t.GetComponents<Component>())
                if (!(c is Transform)) sb.Append(" [" + c.GetType().Name + "]");
            sb.AppendLine();
            var mf = t.GetComponent<MeshFilter>();
            if (mf && mf.sharedMesh)
            {
                var m = mf.sharedMesh;
                sb.AppendLine($"{indent}  mesh={m.name} bounds c={m.bounds.center:F3} s={m.bounds.size:F3} readable={m.isReadable} verts={m.vertexCount}");
                if (m.isReadable) sb.AppendLine(indent + "  " + FrontHint(m));
            }
            var r = t.GetComponent<Renderer>();
            if (r)
                foreach (var mat in r.sharedMaterials.Where(x => x))
                {
                    var sh = mat.shader;
                    var props = Enumerable.Range(0, sh.GetPropertyCount()).Select(i => sh.GetPropertyName(i));
                    sb.AppendLine($"{indent}  mat={mat.name} shader={sh.name} props={string.Join(",", props)} kw={string.Join(",", mat.shaderKeywords)}");
                }
            var l = t.GetComponent<Light>();
            if (l) sb.AppendLine($"{indent}  light type={l.type} range={l.range} int={l.intensity} shadows={l.shadows}");
            foreach (Transform ch in t) DumpHierarchy(ch, indent + "  ", sb);
        }

        // Pour l'axe le plus long : l'extrémité où la section transversale est la plus large
        // (les branches de l'arbalète) est l'avant de l'arme.
        private static string FrontHint(Mesh m)
        {
            var v = m.vertices; var b = m.bounds;
            int ax = b.size.x >= b.size.y && b.size.x >= b.size.z ? 0 : (b.size.y >= b.size.z ? 1 : 2);
            float lo = b.min[ax], len = b.size[ax];
            float wLo = 0, wHi = 0;
            foreach (var p in v)
            {
                float u = (p[ax] - lo) / len, w = 0;
                for (int k = 0; k < 3; k++) if (k != ax) w = Mathf.Max(w, Mathf.Abs(p[k] - b.center[k]));
                if (u < 0.25f) wLo = Mathf.Max(wLo, w); else if (u > 0.75f) wHi = Mathf.Max(wHi, w);
            }
            return $"longAxis={"xyz"[ax]} widthAtMin={wLo:F3} widthAtMax={wHi:F3}";
        }
    }
}
