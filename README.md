# ProgressionSkills

Valheim mod (BepInEx) that caps combat skills based on which bosses have been killed in the world.

- Combat skills level normally up to the cap, then stop until the next boss raises it
- Other skills aren't affected
- No skill loss on death
- If your skill is above the cap you play at the cap in that world, but your real level stays in your character save, so you get it back elsewhere
- Gear and food bonuses still apply on top of the cap
- Off by default, turned on per world

| Boss killed | Cap |
|---|---|
| none | 10 |
| Eikthyr | 20 |
| The Elder | 35 |
| Bonemass | 50 |
| Moder | 65 |
| Yagluth | 80 |
| The Queen | 90 |
| Fader | 100 |

## Install

1. Install [BepInExPack_Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/). On Linux set the launch options to `./start_game_bepinex.sh %command%`.
2. Put `ProgressionSkills.dll` in `Valheim/BepInEx/plugins`.

Everyone on a server needs the mod (same version) to join.

## Turning it on

Host or admin, in the F5 console (needs the `-console` launch option):

```
progressionskills on
progressionskills off
progressionskills        # shows if it's on and the current cap
```

It's stored as a global key (`progressionskills`) in the world, so it stays with the world. It's not a cheat command and doesn't affect achievements.

## Config

`BepInEx/config/aj.progressionskills.cfg`, synced from the server:

- `CombatSkills` - which skills get capped (weapons, magic and blocking by default)
- `BaseLevel` and one entry per boss key - the caps
- `ShowCapMarker` - orange line on the skill bar at the cap (client only)

## Achievements

BepInExPack_Valheim sets `Game.isModded`, which turns achievements off whether or not you use this mod. [IsModded](https://thunderstore.io/c/valheim/p/djcdevelopment/IsModded/) fixes that, or use the game's own `yesiuseddevcommandsbutiwantmyachievementsanyway 1` (after `devcommands`) once per character.

## How it works

The stored skill level is never changed. The cap is applied in `SEMan.ModifySkillLevel` (before status effect bonuses) and on the skills screen, and `RaiseSkill` is blocked at the cap. Because of that the normal character save always has the real levels and there's no extra save data.

## Building

Needs the .NET SDK and Valheim installed.

```
dotnet build -c Release -p:GamePath=/path/to/Valheim
```

The game assemblies are referenced from `GamePath` (default is the Steam path on Linux). The dll gets copied to `BepInEx/plugins` if BepInEx is installed there. The version is set in `ProgressionSkills.csproj`.

[ServerSync](https://github.com/blaxxun-boop/ServerSync) is included in `lib/` and merged into the dll, see `lib/ServerSync.LICENSE.txt`.

## License

MIT
