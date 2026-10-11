using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Managers;
using Jotunn.Utils;

namespace ValheimFirearms
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    // Serveur et clients doivent avoir le mod : c'est le serveur qui valide les tirs et calcule les dégâts.
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "jean.valheimfirearms";
        public const string Name = "Valheim Firearms";
        public const string Version = "0.1.1";

        internal static ManualLogSource Log;
        internal static Plugin Instance;
        private FileSystemWatcher _watcher;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            FirearmsConfig.Bind(Config);
            WatchConfig();
            GunAudio.LoadAll(Path.GetDirectoryName(Info.Location));
            PrefabManager.OnVanillaPrefabsAvailable += OnVanillaPrefabs;
            new Harmony(Guid).PatchAll();
            Log.LogInfo($"{Name} {Version} chargé");
        }

        private void OnVanillaPrefabs()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= OnVanillaPrefabs;
            if (FirearmsConfig.DumpPrefabs.Value) Diagnostics.DumpVanilla();
            Ballistics.InitMasks();
            GunAudio.BindVanilla();
            Items.Register();
        }

        /// <summary>Recharge le .cfg quand il est modifié sur le disque (réglages pris en compte sans relancer).</summary>
        private void WatchConfig()
        {
            _watcher = new FileSystemWatcher(BepInEx.Paths.ConfigPath, Path.GetFileName(Config.ConfigFilePath))
            {
                NotifyFilter = NotifyFilters.LastWrite, IncludeSubdirectories = false, EnableRaisingEvents = true,
            };
            _watcher.Changed += (_, __) => _reload = true;
        }

        private volatile bool _reload;

        private void Update()
        {
            if (!_reload) return;
            _reload = false;
            try { Config.Reload(); Log.LogInfo("Configuration rechargée"); }
            catch (System.Exception e) { Log.LogWarning("Rechargement de la config impossible : " + e.Message); }
        }
    }
}
