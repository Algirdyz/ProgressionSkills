# ProgressionSkills

Valheim mod. Combat skills are capped by the bosses you've defeated, and you don't lose skills on death.

| Boss | Cap |
|---|---|
| none | 10 |
| Eikthyr | 20 |
| The Elder | 35 |
| Bonemass | 50 |
| Moder | 65 |
| Yagluth | 80 |
| The Queen | 90 |
| Fader | 100 |

## Usage

Off by default. The host or an admin turns it on per world in the F5 console (launch with `-console`):

```
progressionskills on
progressionskills off
```

Everyone on the server needs the mod.

## How it works

Your saved skill levels are never changed, the cap only applies while playing in that world.

## Building

```
dotnet build -c Release -p:GamePath=/path/to/Valheim
```

ServerSync (`lib/`) is merged into the dll.

## License

MIT
