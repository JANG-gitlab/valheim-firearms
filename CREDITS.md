# Crédits et licences

Tous les sons livrés avec le mod viennent de [freesound.org](https://freesound.org) et sont sous licence
**CC0 1.0 (domaine public)**, licence vérifiée sur la page de chaque son le 2026-10-11. On a utilisé les aperçus
HQ, puis on les a découpés, normalisés et convertis en WAV mono 44,1 kHz avec ffmpeg. L'attribution n'est pas
obligatoire en CC0, mais on cite les auteurs.

| Fichier du mod | Son d'origine | Auteur |
|---|---|---|
| `shot_1.wav` | [An AK-47 being shot // punchy sounding (#855842)](https://freesound.org/s/855842/) | serøutōnin--deprivəd |
| `shot_2.wav` | [An AK-47 being shot // distinctive metallic report (#855841)](https://freesound.org/s/855841/) | serøutōnin--deprivəd |
| `tail_outdoor.wav` | [An AK-47 being shot // w/ distant trailing reverb (#855843)](https://freesound.org/s/855843/) (queue seule) | serøutōnin--deprivəd |
| `tail_indoor.wav` | [Kalashnikov 1 (indoor) (#812210)](https://freesound.org/s/812210/) (queue seule) | mahecic |
| `mag_out.wav`, `mag_in.wav` | [AK-47 Assault Rifle being unloaded and reloaded (#674742)](https://freesound.org/s/674742/) | serøutōnin--deprivəd |
| `charge.wav` | [AK-47 assault rifle being cocked 3x (#675008)](https://freesound.org/s/675008/) | serøutōnin--deprivəd |
| `dryfire.wav` | [A rifle being dry fired once (#725402)](https://freesound.org/s/725402/) | serøutōnin--deprivəd |
| `impact_flesh_1.wav`, `impact_flesh_2.wav` | [VisceralBulletImpacts.wav (#423301)](https://freesound.org/s/423301/) | u1769092 |
| `impact_dirt_1.wav` | [Single Rock hit Dirt.wav (#319222)](https://freesound.org/s/319222/) | worthahep88 |
| `impact_dirt_2.wav` | [Stick into soft dirt.wav (#319216)](https://freesound.org/s/319216/) | worthahep88 |
| `ricochet_1.wav` | [Ricochet 2.wav (#392975)](https://freesound.org/s/392975/) | morganpurkis |
| `ricochet_2.wav` | [Bullet_Ricochet_01 (#345413)](https://freesound.org/s/345413/) | Artmasterrich |
| `flyby.wav` | [Real Bullet Flyby Sound (#855248)](https://freesound.org/s/855248/) | qubodup |
| `casing.wav` | [GUN_AK47_SHELL_BULLET_FLOOR_TILES.WAV (#507048)](https://freesound.org/s/507048/) | kaniaplania |

## Modèles 3D et textures

- Le modèle de l'AK-47, la boîte de cartouches, les textures (bois, métal), les sprites de flash, de traçante
  et d'impact sont **générés par le code du mod** (`src/GunModel.cs`, `src/Procedural.cs`). Aucun fichier
  externe n'est utilisé, et ces éléments sont sous la même licence que le mod.

## Ressources du jeu utilisées à l'exécution (non redistribuées)

- Le mod réutilise à l'exécution des éléments présents dans l'installation Valheim du joueur : shaders,
  prefab de l'arbalète comme base de l'objet, animations de rechargement d'arbalète, sons `sfx_arrow_hit`,
  `sfx_rock_hit` et `sfx_metalbars_hit`. Rien n'est extrait ni livré avec le mod.

## Bibliothèques

- [BepInEx](https://github.com/BepInEx/BepInEx) (LGPL-2.1) et [Jötunn](https://github.com/Valheim-Modding/Jotunn)
  (MIT) sont des dépendances installées séparément.
- [BepInEx.AssemblyPublicizer](https://github.com/BepInEx/BepInEx.AssemblyPublicizer) (LGPL-2.1) ne sert qu'à la
  compilation.
