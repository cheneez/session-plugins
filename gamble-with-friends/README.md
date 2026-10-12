# Gamble With Your Friends plugins

BepInEx 5 plugins for the Unity (Mono) build. Networking in this game is Mirror. Lobby changes are applied by the host, so other players do not need the same files installed to join.

## Larger lobbies

`MorePlayers.cs` — plugin id `com.local.moreplayers`.

Adds a config value, `MaxPlayers`, from 2 to 32. The default is 16. Only the host has to run it. On load, and again at start, the plugin writes that cap into the lobby.

## Solo bank

`SoloBank.cs` — plugin id `com.local.solobank`.

A host session layer on top of the normal tables:

- A ranking panel in the top-right.
- Host loans, opened with F9.
- Bet insurance, including how much of the cover was actually used.
- Duel pairing and a short immunity window, with a log line for the stake, multiplier, and balance before and after.

## Day debug

`DayDebug.cs` — plugin id `com.local.solodaydebug`.

A small movable window that shows the current day timer so the host can see whether the round clock matches the expected length.
