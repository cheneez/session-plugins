# Gamble With Your Friends

BepInEx plugins for the Unity (Mono) build. Networking is Mirror. The host applies lobby and class rules, so other players can join without installing the files.

## Four classes

`SoloBank.cs` — `com.local.solobank`.

Each player picks one class at the start. Thieves are limited to two. The keys are 1, 2, 3, and 4.

**Casino boss.** Starts with $10,000 and 20 tickets and has no quota. Shop gold-ticket prices are permanently halved and do not rise. Machines the boss bought pay the boss a cut when someone else uses them, and a share of other players’ losses is paid to the boss. The boss can tip or loan cash that has to be repaid. Goal: stake a minimum and earn a set multiplier.

**Gambler.** Upgrades do not reset and can stack. A daily basketball shot can double the bonus or the quota. Once a day the gambler marks one player for a duel, and the winner takes the loser’s cash. Goal: win a set amount.

**Thief.** Steals cash from players or NPCs, can disguise as a player or an NPC, and moves faster. Stealing has a cooldown. Goal: steal a rising percent of other people’s cash.

**Security.** Starts with $500 and 5 tickets and is on the boss’s side. The bat and pistol cost nothing, and raising the bat is 15% faster. Holding a thief for 2 seconds catches them and raises the guard’s pay. If the boss is robbed, the guard is not paid the next day. Goal: protect the boss for three days, then five, then ten in a row.

The same plugin draws the ranking panel, host loans (F9), and bet insurance.

## Larger lobbies

`MorePlayers.cs` — `com.local.moreplayers`.

`MaxPlayers` is a config value from 2 to 32. The default is 16. Only the host needs it.

## Day timer

`DayDebug.cs` — `com.local.solodaydebug`.

A small window that shows the current day timer.
