# Crime Simulator plugins

BepInEx 5 plugins for the Unity (Mono) build. Each folder is one plugin. They share the `com.kaboo.crimesim` id prefix and, where noted, load after the skill shop.

## Comfort camera

`comfort-camera` — plugin id `com.kaboo.crimesim.antisickness`.

Reduces camera sickness and adds an optional third-person view for the local player only.

- Field of view override, or leave the game’s own setting.
- Turns off walk bob, damage shake, sprint and jump field-of-view kicks, motion blur, and chromatic aberration. Aim zoom is left alone.
- Hand sway is a 0 to 1 scale.
- O toggles first and third person. Hold minus or equals to change camera distance from 1 to 6 meters. When the camera is close, the local body fades so it does not fill the lens. Other players still see a normal character.

## Carry bag

`carry-bag` — plugin id `com.kaboo.crimesim.duffelbag`. Requires the skill shop plugin.

Adds a duffel that holds loot outside the normal hands. Capacity starts at 3 and can grow to 10. Items keep an icon, the bag tracks the world save, and a quota label shows what is stored.

## Skill shop

`skill-shop` — plugin id `com.kaboo.crimesim.skillcash`.

Sells the game’s skill books and a short gear list for cash, using the game’s own item ids. Cash found in locked containers uses different bill weights for basic, advanced, and master locks, and for dial and electronic safes.

## Store restock

`store-restock` — plugin id `com.kaboo.crimesim.shop`. Requires the skill shop plugin.

Refills computer-store stock when the world advances a day, and raises the furniture site purchase limit to 10.

## Bounty board

`bounty-board` — plugin id `com.kaboo.crimesim.bounty`.

Tracks a bounty total for the session and shows it on the player HUD. Police encounters are tagged as standard, fast, tank, or tracker. Host and client stay aligned through short chat messages rather than a separate network object.

## Host session

`host-session` — plugin id `com.kaboo.crimesim.hostbuffs`.

Host-only switches for jump height, move speed, and the world time scale. The plugin looks up the game’s movement and time fields at startup and logs a clear error if a game update renamed them.
