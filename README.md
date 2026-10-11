# Valheim Firearms

Mod BepInEx et Jötunn qui ajoute des armes à feu à Valheim, à commencer par l'**AK-47**.

Le tir ne réutilise pas les `Projectile` d'arc ou d'arbalète : le mod a sa propre balistique, et c'est le
serveur qui valide les touches.

## Contenu

| Objet | Prefab | Fabrication (par défaut) |
|---|---|---|
| AK-47 | `VFA_AK47` | Forge : 20 fer, 8 bois fin, 6 bronze, 4 chutes de cuir |
| Cartouches 7,62×39 mm (×30) | `VFA_Ammo762` | Forge : 1 bronze, 4 charbon |

**Commandes**

| Action | Touche |
|---|---|
| Tirer | Clic gauche |
| Viser (zoom, moins de dispersion et de recul) | Clic droit |
| Recharger | `R` |
| Passer en auto ou en coup par coup | `B` |

Les touches se changent dans la config.

## Fonctionnement

### Balistique ([src/Ballistics.cs](src/Ballistics.cs))

- **Simulation** : chaque balle est simulée pas à pas, à 120 Hz. Elle part à 715 m/s (1 unité Valheim = 1 m),
  subit la gravité et une traînée quadratique (`dv/dt = -k·v²`, k = 0,0017 : il reste environ 430 m/s à 300 m).
  Un raycast couvre chaque segment parcouru, donc aucune collision n'est sautée malgré la vitesse.
- **Point de départ et hausse** : la balle part de l'œil du personnage, vers le point visé sous le réticule. La
  hausse est réglée à 100 m. La traçante part de la bouche du canon et rejoint la trajectoire réelle en 10 m environ.
- **Pénétration** :
  - Une créature dont la vie max est sous `PenetrationMaxTargetHealth` (150 par défaut) est traversée. La balle
    perd de l'énergie selon la vie de la cible, puis continue.
  - Les pièces de construction en bois fin sont traversées, avec un trou de sortie.
  - Pierre, terrain et créatures solides arrêtent la balle.
- **Ricochets** : sur la pierre ou le métal, en incidence rasante, la balle ricoche. Le ricochet est uniquement
  visuel : il n'inflige pas de dégâts.
- **Dégâts** :
  - Ce sont des dégâts perforants, multipliés par √(énergie restante), avec un plancher.
  - Un **headshot** est détecté par la distance au point de tête du personnage (`Character.GetHeadPoint`). Il
    applique un multiplicateur de 2,5 par défaut.
  - Les **points faibles** natifs du jeu (`WeakSpot`, par collider) sont transmis via `HitData.m_weakSpot`. Le jeu
    applique alors leurs résistances propres.

### Multijoueur ([src/Network.cs](src/Network.cs))

Le serveur fait autorité. Le mod est donc requis sur le serveur et sur tous les clients (Jötunn le vérifie à la
connexion).

1. Le client tire. Il simule sa balle tout de suite (aucune latence ressentie) et envoie le tir au serveur :
   origine et direction, dispersion incluse.
2. Le serveur vérifie le tir :
   - l'identité du tireur (le ZDOID de son personnage) ;
   - la distance entre l'origine et la position connue du joueur ;
   - la cadence (seau à jetons) ;
   - le chargeur et le temps de rechargement.
   Il refuse le tir sinon. Il renvoie ensuite le tir à tous les autres clients, qui rejouent la balle pour
   l'affichage : traçante, flash, son et impacts.
3. Le client signale chaque impact sur une créature. Le serveur vérifie ensuite :
   - que le tir existe et que l'ordre des pénétrations est cohérent ;
   - que le point touché est sur la trajectoire annoncée, chute balistique comprise ;
   - qu'il est proche de la cible telle que le serveur la connaît (ZDO) ;
   - que le headshot est plausible.
4. Le serveur **recalcule les dégâts** à partir de sa propre config. Il envoie ensuite le `RPC_Damage` vanilla au
   propriétaire de la cible, ce qui applique résistances, armure, PvP, étourdissement et mort comme pour une
   arme du jeu. Enfin, il renvoie au tireur une confirmation (marqueur de touche, progression de la compétence
   Arbalètes).

Les valeurs de gameplay (dégâts, cadence, chargeur, balistique, validation, recettes) sont `IsAdminOnly`. Jötunn
les synchronise depuis le serveur.

### Arme ([src/WeaponController.cs](src/WeaponController.cs), [src/GunVisual.cs](src/GunVisual.cs))

- **Chargeur et munitions** : chargeur de 30 coups, mémorisé dans l'objet (`m_customData`). Une cartouche reste
  chambrée lors d'un rechargement tactique (30+1).
- **Rechargement** :
  - Durée de 2,9 s à vide, 2,1 s chargeur entamé.
  - Barre de progression et animation de rechargement d'arbalète du jeu.
  - Chargeur animé, sons d'éjection, d'insertion et de manœuvre de culasse.
- **Modes de tir** : auto à 600 coups/min, ou coup par coup. Le clic à vide est sonorisé.
- **Recul** :
  - Relevé vertical appliqué sur environ 40 ms.
  - Dérive horizontale aléatoire, avec une légère tendance à droite.
  - Retour progressif d'une partie du relevé, en tenant compte de la compensation du joueur à la souris.
  - Dispersion qui augmente pendant une rafale puis redescend.
- **Flash de bouche** :
  - Une vraie `Light` ponctuelle dynamique (rendu par pixel), avec un pic puis une décroissance en 20 ms environ.
    Elle éclaire réellement le sol et les murs proches.
  - Une étoile et deux plans additifs.
  - De la fumée, des étincelles et une douille éjectée qui rebondit avec un son.
- **Sons** :
  - Tir sec et queue de réverbération différente en intérieur et en extérieur (détection d'abri du jeu,
    `Cover.GetCoverForPoint`).
  - Délai de propagation de 343 m/s et filtre passe-bas avec la distance : un tir lointain arrive en retard et
    étouffé.
  - Claquement d'une balle qui passe près de soi.
- **Impacts par matériau** (bois, pierre, métal, terre, chair, eau, feuillage) : particules dédiées, son, et
  impacts persistants (decals) rattachés à l'objet touché.
- **HUD** : munitions, mode de tir, indicateur de dispersion autour du réticule, marqueur de touche (rouge pour
  un headshot).

Le modèle 3D, la boîte de cartouches et toutes les textures sont **générés par le code** : aucun AssetBundle, aucun
Unity Editor nécessaire. Voir [CREDITS.md](CREDITS.md) pour les sons (tous CC0).

## Compilation

Prérequis :

- **SDK .NET 8**. Il n'est pas nécessaire d'installer Visual Studio. Installation sans droits admin :

  ```powershell
  Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1
  ./dotnet-install.ps1 -Channel 8.0 -InstallDir "$env:LOCALAPPDATA\Microsoft\dotnet"
  ```

- **Valheim** installé, et un profil Thunderstore Mod Manager avec BepInExPack et Jötunn.

Compilation :

```powershell
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build -c Release
```

- Le projet cible **.NET Framework 4.6.2**, mais compile contre la BCL du jeu (`valheim_Data\Managed`, Mono /
  Unity 6). Les DLL Unity 6 exigent `netstandard` 2.1 et `Span<T>`, absents des reference assemblies net462.
- `assembly_valheim` et `assembly_utils` sont rendues publiques à la compilation par
  `BepInEx.AssemblyPublicizer.MSBuild`.
- Après la compilation, la DLL et le dossier `sounds\` sont copiés dans
  `...\Thunderstore Mod Manager\DataFolder\Valheim\profiles\luigi\BepInEx\plugins\ValheimFirearms\`.

Pour cibler d'autres chemins :

```powershell
dotnet build -c Release -p:ValheimDir="D:\Steam\steamapps\common\Valheim" -p:BepInExDir="C:\chemin\vers\BepInEx"
```

Pour désactiver la copie automatique : `-p:DeployToProfile=false`.

## Installation

### Clients (Thunderstore Mod Manager)

1. Le profil doit avoir **BepInExPack_Valheim** et **Jötunn**.
2. Copier le dossier `ValheimFirearms` (contenant `ValheimFirearms.dll` et `sounds\`) dans
   `profiles\<profil>\BepInEx\plugins\`. La commande `dotnet build` le fait pour le profil `luigi`.
3. Lancer le jeu avec « Start modded ». Sans passer par TMM : `run-game.ps1 -Profile luigi`.

### Serveur dédié

Le mod doit aussi être installé sur le serveur : c'est lui qui valide les tirs. Copier le même dossier dans
`BepInEx/plugins/` du serveur, puis redémarrer le serveur. Jötunn refuse la connexion si la version (majeure ou
mineure) diffère entre client et serveur.

### Tester en solo

Ouvrir la console (F5), puis :

```
devcommands
spawn VFA_AK47
spawn VFA_Ammo762 150
```

## Configuration

Le fichier `BepInEx/config/jean.valheimfirearms.cfg` est créé au premier lancement. Il est rechargé à chaud
quand on le modifie. Sections :

| Section | Contenu |
|---|---|
| 1 - Balistique | Dégâts, headshot, vitesse initiale, gravité, traînée, portée, pénétration |
| 2 - Arme | Cadence, capacité du chargeur, durées de rechargement |
| 3 - Visée et recul | Dispersion (base, visée, mouvement, rafale), recul vertical et horizontal, retour, zoom |
| 4 - Effets | Lumière du flash (intensité, portée, ombres), traçantes, volume, distance d'audition, impacts |
| 5 - Touches | Recharger, mode de tir |
| 6 - Serveur | Validation activée, tolérances (origine, impact, cadence) |
| 7 - Recettes | Ingrédients et stations (format `Objet:quantité,Objet:quantité`) |
| 8 - Modèle | Position, rotation et échelle de l'arme dans la main, ajustables en jeu |
| 9 - Debug | Dump des prefabs vanilla, log de chaque tir côté client et serveur |

Les sections 1, 2, 6 et 7 viennent du serveur en multijoueur.

## Limites connues

- Les animations du personnage sont celles de l'arbalète (tenue, rechargement). Une animation propre au fusil
  demanderait Unity Editor 6000.0.75f1 et un AssetBundle.
- Seules les créatures et les joueurs reçoivent des dégâts. Les constructions, arbres et rochers reçoivent les
  impacts visuels mais ne sont pas endommagés.
- La validation serveur repose sur les positions ZDO, qui sont synchronisées avec un léger retard : les
  tolérances (`HitTolerance`, `MaxOriginError`) sont volontairement larges.
