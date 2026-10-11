using System.Collections.Generic;
using System.Linq;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace ValheimFirearms
{
    internal static class Items
    {
        public const string RiflePrefab = "VFA_AK47", AmmoPrefab = "VFA_Ammo762";
        public const string RifleToken = "$vfa_ak47", AmmoToken = "$vfa_ammo";
        public static Material LitTemplate;
        private static CustomItem _rifle, _ammo;

        public static void Register()
        {
            AddTranslations();
            var crossbow = PrefabManager.Instance.GetPrefab("CrossbowArbalest");
            LitTemplate = crossbow.GetComponentsInChildren<MeshRenderer>(true)
                .Select(r => r.sharedMaterial).FirstOrDefault(m => m && m.shader.name == "Custom/Creature");
            if (!LitTemplate) Plugin.Log.LogWarning("Matériau d'arbalète introuvable, repli sur Standard");

            _rifle = new CustomItem(RiflePrefab, "CrossbowArbalest", new ItemConfig
            {
                Name = RifleToken, Description = "$vfa_ak47_desc", Amount = 1,
                CraftingStation = FirearmsConfig.RifleStation.Value, Requirements = ParseRecipe(FirearmsConfig.RifleRecipe.Value),
            });
            BuildRifle(_rifle.ItemPrefab);
            ItemManager.Instance.AddItem(_rifle);

            _ammo = new CustomItem(AmmoPrefab, "BronzeNails", new ItemConfig
            {
                Name = AmmoToken, Description = "$vfa_ammo_desc", Amount = FirearmsConfig.AmmoPerCraft.Value,
                CraftingStation = FirearmsConfig.AmmoStation.Value, Requirements = ParseRecipe(FirearmsConfig.AmmoRecipe.Value),
            });
            BuildAmmo(_ammo.ItemPrefab);
            ItemManager.Instance.AddItem(_ammo);

            // Les recettes et les dégâts affichés suivent la config synchronisée par le serveur.
            SynchronizationManager.OnConfigurationSynchronized += (_, __) => ApplyConfig();
            ItemManager.OnItemsRegistered += ApplyConfig;
        }

        private static void BuildRifle(GameObject prefab)
        {
            var attach = prefab.transform.Find("attach");
            Object.DestroyImmediate(attach.GetComponent<WeaponLoadState>());
            for (int i = attach.childCount - 1; i >= 0; i--) Object.DestroyImmediate(attach.GetChild(i).gameObject);

            var gun = GunModel.BuildRifle(LitTemplate);
            gun.transform.SetParent(attach, false);
            GunVisual.Decorate(gun);
            ApplyModelPose(gun.transform);
            var col = new GameObject("collider");
            col.transform.SetParent(attach, false);
            var box = col.AddComponent<BoxCollider>();
            box.center = gun.transform.localPosition + gun.transform.localRotation * new Vector3(0, -0.04f, 0.12f) * gun.transform.localScale.x;
            box.size = new Vector3(0.06f, 0.2f, 1.03f) * gun.transform.localScale.x;
            SetLayer(attach.gameObject, LayerMask.NameToLayer("item"));

            var s = prefab.GetComponent<ItemDrop>().m_itemData.m_shared;
            s.m_ammoType = "";
            s.m_attack.m_requiresReload = false;
            s.m_attack.m_attackStamina = 0f;
            s.m_attack.m_projectiles = 0;
            s.m_maxQuality = 1;
            s.m_weight = 3.8f;
            s.m_useDurability = true; s.m_maxDurability = 1000f; s.m_durabilityPerLevel = 0f; s.m_canBeReparied = true;
            s.m_equipDuration = 0.6f;
            s.m_movementModifier = -0.05f;
            s.m_backstabBonus = 3f;
            s.m_blockPower = 0f; s.m_deflectionForce = 0f; s.m_timedBlockBonus = 1f;
            s.m_damagesPerLevel = new HitData.DamageTypes();
            s.m_icons = new[] { Icon(prefab, Quaternion.Euler(-30f, 90f, 0f)) };
            ApplyDamageDisplay(s);
        }

        private static void BuildAmmo(GameObject prefab)
        {
            foreach (var c in prefab.GetComponents<Component>())
                if (c is MeshRenderer || c is MeshFilter || c is LODGroup || c is Collider) Object.DestroyImmediate(c);
            for (int i = prefab.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(prefab.transform.GetChild(i).gameObject);
            var model = GunModel.BuildAmmoBox(LitTemplate);
            model.transform.SetParent(prefab.transform, false);
            var box = model.AddComponent<BoxCollider>();
            box.center = new Vector3(0, 0.04f, 0); box.size = new Vector3(0.15f, 0.085f, 0.1f);
            SetLayer(model, LayerMask.NameToLayer("item"));

            var s = prefab.GetComponent<ItemDrop>().m_itemData.m_shared;
            s.m_itemType = ItemDrop.ItemData.ItemType.Material;
            s.m_maxStackSize = 150;
            s.m_weight = 0.017f;
            s.m_teleportable = true;
            s.m_icons = new[] { Icon(prefab, Quaternion.Euler(25f, -35f, 0f)) };
        }

        private static Sprite Icon(GameObject prefab, Quaternion rot)
        {
            try { return RenderManager.Instance.Render(prefab, rot); }
            catch (System.Exception e) { Plugin.Log.LogWarning("Rendu d'icône impossible : " + e.Message); return null; }
        }

        /// <summary>Position de l'arme dans la main (repère « attach » de l'arbalète vanilla : +Z avant, +Y haut).</summary>
        public static void ApplyModelPose(Transform gun)
        {
            gun.localPosition = ParseVec(FirearmsConfig.ModelOffset.Value);
            gun.localRotation = Quaternion.Euler(ParseVec(FirearmsConfig.ModelRotation.Value));
            gun.localScale = Vector3.one * FirearmsConfig.ModelScale.Value;
        }

        public static void ReapplyModelPose()
        {
            var prefab = PrefabManager.Instance.GetPrefab(RiflePrefab);
            var g = prefab ? prefab.transform.Find("attach/vfa_ak47") : null;
            if (g) ApplyModelPose(g);
            foreach (var v in Object.FindObjectsByType<GunVisual>(FindObjectsSortMode.None)) v.SetPose();
        }

        private static void ApplyDamageDisplay(ItemDrop.ItemData.SharedData s)
        {
            s.m_damages = new HitData.DamageTypes { m_pierce = FirearmsConfig.Damage.Value };
            s.m_attackForce = FirearmsConfig.PushForce.Value;
        }

        private static void ApplyConfig()
        {
            if (_rifle?.ItemDrop) ApplyDamageDisplay(_rifle.ItemDrop.m_itemData.m_shared);
            if (!ObjectDB.instance) return;
            UpdateRecipe(_rifle, FirearmsConfig.RifleRecipe.Value, FirearmsConfig.RifleStation.Value, 1);
            UpdateRecipe(_ammo, FirearmsConfig.AmmoRecipe.Value, FirearmsConfig.AmmoStation.Value, FirearmsConfig.AmmoPerCraft.Value);
        }

        private static void UpdateRecipe(CustomItem item, string reqs, string station, int amount)
        {
            if (item?.ItemDrop == null) return;
            var recipe = ObjectDB.instance.m_recipes.FirstOrDefault(r => r && r.m_item == item.ItemDrop);
            if (!recipe) return;
            var list = new List<Piece.Requirement>();
            foreach (var rc in ParseRecipe(reqs))
            {
                var drop = ObjectDB.instance.GetItemPrefab(rc.Item)?.GetComponent<ItemDrop>();
                if (drop) list.Add(new Piece.Requirement { m_resItem = drop, m_amount = rc.Amount, m_recover = true });
                else Plugin.Log.LogWarning($"Ingrédient inconnu dans la recette : {rc.Item}");
            }
            recipe.m_resources = list.ToArray();
            recipe.m_amount = amount;
            var st = ZNetScene.instance ? ZNetScene.instance.GetPrefab(station)?.GetComponent<CraftingStation>() : null;
            if (st) recipe.m_craftingStation = st;
        }

        private static RequirementConfig[] ParseRecipe(string s)
        {
            var list = new List<RequirementConfig>();
            foreach (var part in s.Split(','))
            {
                var kv = part.Split(':');
                if (kv.Length == 2 && int.TryParse(kv[1].Trim(), out int n) && n > 0)
                    list.Add(new RequirementConfig(kv[0].Trim(), n, 0, true));
            }
            return list.ToArray();
        }

        private static Vector3 ParseVec(string s)
        {
            var p = s.Split(',');
            float F(int i) => i < p.Length && float.TryParse(p[i].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 0f;
            return new Vector3(F(0), F(1), F(2));
        }

        private static void SetLayer(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        private static void AddTranslations()
        {
            var loc = LocalizationManager.Instance.GetLocalization();
            loc.AddTranslation("French", new Dictionary<string, string>
            {
                { "vfa_ak47", "AK-47" },
                { "vfa_ak47_desc", "Fusil d'assaut Kalachnikov, 7,62×39 mm. Chargeur de 30 coups, tir automatique ou coup par coup.\n<color=orange>Clic gauche</color> : tirer · <color=orange>Clic droit</color> : viser · <color=orange>R</color> : recharger · <color=orange>B</color> : mode de tir" },
                { "vfa_ammo", "Cartouches 7,62×39 mm" },
                { "vfa_ammo_desc", "Munitions pour l'AK-47." },
                { "vfa_msg_empty", "Chargeur vide — recharger" },
                { "vfa_msg_noammo", "Plus de cartouches" },
                { "vfa_msg_broken", "Arme trop usée — à réparer à la forge" },
                { "vfa_mode_auto", "Tir automatique" },
                { "vfa_mode_semi", "Coup par coup" },
            });
            loc.AddTranslation("English", new Dictionary<string, string>
            {
                { "vfa_ak47", "AK-47" },
                { "vfa_ak47_desc", "Kalashnikov assault rifle, 7.62×39 mm. 30-round magazine, full-auto or semi-auto.\n<color=orange>Left click</color>: fire · <color=orange>Right click</color>: aim · <color=orange>R</color>: reload · <color=orange>B</color>: fire mode" },
                { "vfa_ammo", "7.62×39 mm cartridges" },
                { "vfa_ammo_desc", "Ammunition for the AK-47." },
                { "vfa_msg_empty", "Magazine empty — reload" },
                { "vfa_msg_noammo", "Out of ammunition" },
                { "vfa_msg_broken", "Weapon worn out — repair it at a forge" },
                { "vfa_mode_auto", "Full auto" },
                { "vfa_mode_semi", "Semi-auto" },
            });
        }
    }
}
