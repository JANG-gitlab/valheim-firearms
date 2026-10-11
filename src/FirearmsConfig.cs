using BepInEx.Configuration;
using UnityEngine;

namespace ValheimFirearms
{
    /// <summary>
    /// Toutes les valeurs réglables. Les entrées « serveur » sont IsAdminOnly : Jötunn les synchronise
    /// depuis le serveur, et c'est de toute façon le serveur qui calcule les dégâts finaux.
    /// </summary>
    internal static class FirearmsConfig
    {
        // Balistique / dégâts (synchronisés)
        public static ConfigEntry<float> Damage, HeadshotMultiplier, MuzzleVelocity, GravityScale, Drag, MaxRange,
            MinDamageFraction, PushForce;
        public static ConfigEntry<float> PenetrationMaxTargetHealth, PenetrationEnergyLoss, WoodPenetrationEnergyLoss;
        public static ConfigEntry<int> MaxPenetrations;

        // Arme (synchronisés)
        public static ConfigEntry<int> RoundsPerMinute, MagazineSize;
        public static ConfigEntry<float> ReloadTime, TacticalReloadTime;

        // Visée / recul (côté client)
        public static ConfigEntry<float> BaseSpread, AimSpreadMultiplier, MoveSpreadMultiplier, SpreadPerShot, MaxSpread,
            SpreadRecovery, RecoilVertical, RecoilHorizontal, RecoilRecovery, RecoilReturnFraction, AimRecoilMultiplier,
            AimFov;

        // Effets (côté client)
        public static ConfigEntry<float> MuzzleLightIntensity, MuzzleLightRange, Volume, AudibleDistance, DecalLifetime;
        public static ConfigEntry<int> TracerEveryNth, MaxDecals;
        public static ConfigEntry<bool> MuzzleLightShadows;

        // Touches
        public static ConfigEntry<KeyCode> ReloadKey, FireModeKey;

        // Serveur (synchronisés)
        public static ConfigEntry<bool> ServerValidation, IgnoreResistances, IgnorePvP;
        public static ConfigEntry<float> MaxOriginError, HitTolerance, FireRateTolerance;

        // Recettes (synchronisées)
        public static ConfigEntry<string> RifleRecipe, RifleStation, AmmoRecipe, AmmoStation;
        public static ConfigEntry<int> AmmoPerCraft;

        // Debug
        public static ConfigEntry<bool> DumpPrefabs, DebugHits;
        public static ConfigEntry<string> ModelOffset, ModelRotation;
        public static ConfigEntry<float> ModelScale;

        public static void Bind(ConfigFile c)
        {
            Damage = Synced(c, "1 - Balistique", "Damage", 80f, "Dégâts d'une balle à la bouche du canon (avant armure des joueurs).", 1f, 500f);
            HeadshotMultiplier = Synced(c, "1 - Balistique", "HeadshotMultiplier", 2.5f, "Multiplicateur appliqué si l'impact est sur la tête (ou un point faible du monstre).", 1f, 10f);
            MuzzleVelocity = Synced(c, "1 - Balistique", "MuzzleVelocity", 715f, "Vitesse initiale en m/s (1 unité Valheim = 1 m ; 7,62x39 réel ≈ 715 m/s).", 50f, 2000f);
            GravityScale = Synced(c, "1 - Balistique", "GravityScale", 1f, "Multiplicateur de gravité sur la balle (1 = 9,81 m/s²).", 0f, 10f);
            Drag = Synced(c, "1 - Balistique", "Drag", 0.0017f, "Traînée quadratique k (dv/dt = -k·v²). 0,0017 ≈ 430 m/s restants à 300 m.", 0f, 0.05f);
            MaxRange = Synced(c, "1 - Balistique", "MaxRange", 450f, "Distance maximale simulée, en mètres.", 20f, 2000f);
            MinDamageFraction = Synced(c, "1 - Balistique", "MinDamageFraction", 0.35f, "Plancher de dégâts quand la balle a perdu son énergie (fraction des dégâts de base).", 0f, 1f);
            PushForce = Synced(c, "1 - Balistique", "PushForce", 4f, "Recul infligé à la cible.", 0f, 100f);
            PenetrationMaxTargetHealth = Synced(c, "1 - Balistique", "PenetrationMaxTargetHealth", 150f, "Une balle traverse une créature dont la vie max est inférieure à cette valeur.", 0f, 5000f);
            PenetrationEnergyLoss = Synced(c, "1 - Balistique", "PenetrationEnergyLoss", 0.45f, "Fraction d'énergie perdue en traversant une créature (pondérée par sa vie max).", 0f, 1f);
            WoodPenetrationEnergyLoss = Synced(c, "1 - Balistique", "WoodPenetrationEnergyLoss", 0.6f, "Fraction d'énergie perdue en traversant une pièce en bois (planches, murs). 1 = jamais.", 0f, 1f);
            MaxPenetrations = SyncedInt(c, "1 - Balistique", "MaxPenetrations", 3, "Nombre maximal d'objets traversés par une balle.", 0, 10);

            RoundsPerMinute = SyncedInt(c, "2 - Arme", "RoundsPerMinute", 600, "Cadence de tir (AK-47 réel ≈ 600 coups/min).", 60, 1200);
            MagazineSize = SyncedInt(c, "2 - Arme", "MagazineSize", 30, "Capacité du chargeur.", 1, 200);
            ReloadTime = Synced(c, "2 - Arme", "ReloadTime", 2.9f, "Rechargement à vide (chargeur + manœuvre de la culasse), en secondes.", 0.3f, 10f);
            TacticalReloadTime = Synced(c, "2 - Arme", "TacticalReloadTime", 2.1f, "Rechargement avec une balle encore chambrée, en secondes.", 0.3f, 10f);

            BaseSpread = c.Bind("3 - Visée et recul", "BaseSpread", 0.35f, "Dispersion de base au jugé, en degrés.");
            AimSpreadMultiplier = c.Bind("3 - Visée et recul", "AimSpreadMultiplier", 0.35f, "Multiplicateur de dispersion en visée (clic droit).");
            MoveSpreadMultiplier = c.Bind("3 - Visée et recul", "MoveSpreadMultiplier", 2.2f, "Multiplicateur de dispersion en courant / en l'air.");
            SpreadPerShot = c.Bind("3 - Visée et recul", "SpreadPerShot", 0.32f, "Dispersion ajoutée à chaque tir d'une rafale, en degrés.");
            MaxSpread = c.Bind("3 - Visée et recul", "MaxSpread", 4.5f, "Dispersion maximale accumulée, en degrés.");
            SpreadRecovery = c.Bind("3 - Visée et recul", "SpreadRecovery", 7f, "Retour de la dispersion, en degrés par seconde.");
            RecoilVertical = c.Bind("3 - Visée et recul", "RecoilVertical", 0.85f, "Relevé vertical par tir, en degrés.");
            RecoilHorizontal = c.Bind("3 - Visée et recul", "RecoilHorizontal", 0.35f, "Recul horizontal aléatoire max par tir, en degrés.");
            RecoilRecovery = c.Bind("3 - Visée et recul", "RecoilRecovery", 5f, "Vitesse du retour progressif du recul (1/s).");
            RecoilReturnFraction = c.Bind("3 - Visée et recul", "RecoilReturnFraction", 0.75f, "Part du relevé qui revient automatiquement (0 = aucune, 1 = tout).");
            AimRecoilMultiplier = c.Bind("3 - Visée et recul", "AimRecoilMultiplier", 0.7f, "Multiplicateur de recul en visée.");
            AimFov = c.Bind("3 - Visée et recul", "AimFovMultiplier", 0.72f, "Zoom en visée (multiplicateur du champ de vision).");

            MuzzleLightIntensity = c.Bind("4 - Effets", "MuzzleLightIntensity", 4f, "Intensité de la lumière dynamique du flash de bouche.");
            MuzzleLightRange = c.Bind("4 - Effets", "MuzzleLightRange", 10f, "Portée de la lumière du flash, en mètres.");
            MuzzleLightShadows = c.Bind("4 - Effets", "MuzzleLightShadows", false, "Ombres portées par le flash (coûteux).");
            TracerEveryNth = c.Bind("4 - Effets", "TracerEveryNth", 1, "Une balle traçante toutes les N balles (1 = toutes).");
            Volume = c.Bind("4 - Effets", "Volume", 0.9f, "Volume des sons de l'arme.");
            AudibleDistance = c.Bind("4 - Effets", "AudibleDistance", 300f, "Distance à laquelle on entend un tir, en mètres.");
            DecalLifetime = c.Bind("4 - Effets", "DecalLifetime", 45f, "Durée de vie des impacts sur les surfaces, en secondes.");
            MaxDecals = c.Bind("4 - Effets", "MaxDecals", 120, "Nombre maximal d'impacts affichés.");

            ReloadKey = c.Bind("5 - Touches", "Reload", KeyCode.R, "Recharger.");
            FireModeKey = c.Bind("5 - Touches", "FireMode", KeyCode.B, "Basculer auto / coup par coup.");

            IgnoreResistances = SyncedBool(c, "1 - Balistique", "IgnoreResistances", true, "Les balles ignorent les résistances et immunités des créatures et des boss (dégâts bruts). false = dégâts perforants classiques.");
            IgnorePvP = SyncedBool(c, "6 - Serveur", "IgnorePvP", true, "Les balles touchent aussi les joueurs qui n'ont pas activé le PvP.");
            ServerValidation = SyncedBool(c, "6 - Serveur", "ServerValidation", true, "Le serveur vérifie chaque tir (position, cadence, chargeur, cohérence des impacts) avant d'appliquer les dégâts.");
            MaxOriginError = Synced(c, "6 - Serveur", "MaxOriginError", 6f, "Écart maximal toléré entre la position du tireur connue du serveur et l'origine du tir, en mètres.", 1f, 50f);
            HitTolerance = Synced(c, "6 - Serveur", "HitTolerance", 4f, "Écart maximal toléré entre l'impact annoncé et la position de la cible connue du serveur, en mètres.", 0.5f, 50f);
            FireRateTolerance = Synced(c, "6 - Serveur", "FireRateTolerance", 1.3f, "Tolérance sur la cadence (gigue réseau).", 1f, 5f);

            RifleRecipe = SyncedString(c, "7 - Recettes", "RifleRecipe", "Iron:20,FineWood:8,Bronze:6,LeatherScraps:4", "Ingrédients de l'AK-47 (Objet:quantité, séparés par des virgules).");
            RifleStation = SyncedString(c, "7 - Recettes", "RifleStation", "forge", "Station de fabrication de l'AK-47.");
            AmmoRecipe = SyncedString(c, "7 - Recettes", "AmmoRecipe", "Bronze:1,Coal:4", "Ingrédients d'un lot de cartouches 7,62x39.");
            AmmoStation = SyncedString(c, "7 - Recettes", "AmmoStation", "forge", "Station de fabrication des cartouches.");
            AmmoPerCraft = SyncedInt(c, "7 - Recettes", "AmmoPerCraft", 30, "Cartouches produites par fabrication.", 1, 200);

            DumpPrefabs = c.Bind("9 - Debug", "DumpPrefabs", false, "Écrit dans le log la structure des prefabs vanilla utilisés.");
            DebugHits = c.Bind("9 - Debug", "DebugHits", false, "Log détaillé de chaque tir côté client et serveur.");
            ModelOffset = c.Bind("8 - Modèle", "Offset", "0,0,0", "Position de l'arme dans la main (x,y,z en mètres, repère de l'arbalète). Modifiable en jeu.");
            ModelRotation = c.Bind("8 - Modèle", "Rotation", "0,0,0", "Rotation de l'arme dans la main (degrés x,y,z).");
            ModelScale = c.Bind("8 - Modèle", "Scale", 1f, "Échelle du modèle.");
            ModelOffset.SettingChanged += (_, __) => Items.ReapplyModelPose();
            ModelRotation.SettingChanged += (_, __) => Items.ReapplyModelPose();
            ModelScale.SettingChanged += (_, __) => Items.ReapplyModelPose();
        }

        private static ConfigDescription Admin(string desc, AcceptableValueBase range = null) =>
            new ConfigDescription(desc, range, new ConfigurationManagerAttributes { IsAdminOnly = true });

        private static ConfigEntry<float> Synced(ConfigFile c, string s, string k, float v, string d, float min, float max) =>
            c.Bind(s, k, v, Admin(d, new AcceptableValueRange<float>(min, max)));

        private static ConfigEntry<int> SyncedInt(ConfigFile c, string s, string k, int v, string d, int min, int max) =>
            c.Bind(s, k, v, Admin(d, new AcceptableValueRange<int>(min, max)));

        private static ConfigEntry<bool> SyncedBool(ConfigFile c, string s, string k, bool v, string d) => c.Bind(s, k, v, Admin(d));

        private static ConfigEntry<string> SyncedString(ConfigFile c, string s, string k, string v, string d) => c.Bind(s, k, v, Admin(d));
    }
}
