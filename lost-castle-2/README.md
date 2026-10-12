# Lost Castle 2 plugins

BepInEx 6 plugins for the IL2CPP build. Types that need to run as Unity components are registered with Il2CppInterop. Use these in single-player only.

## Spawn menu

`spawn-menu` — plugin id `lc2.spawnmenu`, version 1.3.2.

F9 opens a searchable list of items and spawns the selected row into the current camp or dungeon. While the menu is closed, the game-master flags it uses are turned back off. Item rows come from `items.json`, with classification in `ItemClassify` and the actual spawn call in `Spawner`.

## RPG layer

`rpg` — plugin id `lc2.rpg`, version 1.1.7.

Adds a level, experience, and an unspent-point menu for the local hero:

| Stat | Effect per point |
| --- | --- |
| Attack | +5% |
| Defense | +3% |
| Health | +20 |
| Steal | +1% |
| Attack speed | +5% |
| Move speed | +2.5% |

The GUI keeps its own color state so a tint does not wash the whole frame. Hooks apply the bonuses to the local hero and remember health that was already granted.

## Treasure stack

`treasure-stack` — plugin id `lc2.treasurestack`, version 1.3.9.

Patches treasure powers so matching pickups stack instead of overwriting the one already held. The stack rules live with the spawn-menu assembly (`StackPowers`) and this plugin is the loader that applies them on their own.
