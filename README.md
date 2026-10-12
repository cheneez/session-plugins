# Gameplay plugins

Private notes for three Unity plugin projects. The repository stays private so it is not indexed when someone searches a game title.

## Crime Simulator

Built a set of BepInEx plugins for this co-op Unity game, using Harmony to patch the player, inventory, store, and world systems.

- Comfort camera: optional field of view, no head bob, no screen shake, no motion blur or lens distortion, and a third-person camera with distance and body fade.
- Carry bag: an extra loot container with a 3 to 10 slot capacity, item icons, and saved contents.
- Skill shop: a cash catalog of the game’s skill books and gear, plus loot cash that scales with the lock or safe that was opened.
- Store restock: shop stock refills when the day advances, and the furniture purchase cap is raised.
- Bounty board: a session bounty with an on-screen total and four police encounter types (standard, fast, tank, and tracker), synced between host and clients.
- Host session: host-only toggles for jump, move speed, and time of day.

## Lost Castle 2

Built IL2CPP BepInEx plugins for this action game. They are meant for single-player.

- Spawn menu: press F9 to search a catalog and spawn an item. Game-master flags are on only while that menu is open.
- RPG layer: level and experience, with unspent points for attack, health, defense, steal, attack speed, and move speed. The overlay does not tint the whole screen.
- Treasure stack: stacking rules for treasure powers so pickups combine instead of replacing each other.

## Gamble With Your Friends

Built BepInEx plugins for this Unity party game (Mirror networking).

- Larger lobbies: the host can set the room size from 2 to 32. Guests do not need the plugin installed.
- Solo bank: a host session with a ranking panel, loans, bet insurance, and duel tracking.
- Day debug: a small on-screen window for inspecting the current day timer.
