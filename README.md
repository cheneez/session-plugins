# Gameplay plugins

Unity plugins I built for three Steam games. Each one loads with BepInEx and patches the game with Harmony.

## Crime Simulator

A set of plugins for this co-op Unity game. They add a third-person camera, player cash, item drops, extra police, a carry bag, and a skill shop.

**Third-person view.** Press O to switch between first and third person. The camera sits behind the player, pulls in for walls, and stays on the same line as the crosshair so aiming and pickups still land on the white dot. The local body fades when the camera is close. Other players still see a normal character. Third person turns off while dead or in a minigame such as lockpicking.

**Camera comfort.** Optional field of view, no walk bob, no damage shake, no sprint or jump field-of-view kick, scaled hand sway, and no motion blur or lens distortion. Aim zoom is left alone.

**Cash and item drops.** Cash picked up stays on that player. Press X to drop one bill, no larger than the current balance, onto a surface in front of the player. Job maps also scatter street bills for the day. Defeated police drop first aid, stun darts, an armor vest, or a cash bill.

**More police.** A chase rolls each officer into one of four types, and higher wanted stars bring more of the special types:

- Standard
- Fast, at 1.5 times walk and run speed
- Tank, with 5 times health
- Tracker, who keeps sight of the player and is outlined for the group

If several officers arrive without a tracker, the next one is forced to be a tracker. A bounty total sits on the HUD. Police who catch you take the bounty and add a fine.

**Carry bag.** An extra bag holds sellable loot first, from 3 slots up to 10. Scroll or 0 selects it. G drops the newest item. Contents save with the world.

**Skill shop.** A computer page sells the game’s skill books, gear (vest, ammo, bat, crowbar, axe, stun gun, silenced pistol), and experience in packs of 1, 10, and 100. Locked containers pay different bills depending on the lock: basic, advanced, master, dial safe, or electronic safe.

Upgrades bought with cash:

- Run speed, jump height, health, and stamina, up to 10
- Bag capacity, up to 7

**Abilities I, II, and III.** Five skills are bought in the same shop. Each one ranks I, then II, then III. Only one is equipped at a time. Press Z to use it. A higher rank shortens the cooldown, and rank III adds an extra effect.

- **Field repair.** Hold Z for 3 seconds to fix the item in hand, paid in cash. Cooldown is 30 seconds at I, 15 at II, and 5 at III. Rank III also cuts that repair cost in half.
- **Highlight.** Outlines people through walls within 30 meters. Rank I lasts 5 seconds with a 60 second cooldown. Rank II lasts 10 seconds with a 45 second cooldown. Rank III lasts 10 seconds, cooldown 30, and also outlines loot and safes.
- **Damage immunity.** Health cannot drop while it is on. Rank I lasts 10 seconds (180 second cooldown). Rank II lasts 15 seconds (120). Rank III lasts 20 seconds (60) and also adds 20% move speed, 50% jump height, and no stamina drain.
- **Heal.** Clears the player’s damage at once. Cooldown is 60 seconds at I, 45 at II, and 30 at III. Rank III can also revive a downed teammate by holding the interact key, with no first aid kit.
- **Stealth.** The body fades, and NPCs lose sight and search the last place they saw the player. Rank I lasts 15 seconds (180 second cooldown). Rank II lasts 20 seconds (120). Rank III lasts 30 seconds (60) and lets the player walk through doors while NPCs stay blocked.

**Revive.** A downed player presses N to come back for $500. Heal III is the other way to bring a teammate up. If everyone is down, the group has 10 seconds before the night ends.

Dying drops run speed, jump, health, and stamina by one level. Home items can be sold into the shared balance.

**Store.** Computer-store stock refills when the day advances, and the furniture purchase cap is raised to 10.

**Host session.** Only the host can toggle double jump, 1.5 times move speed, a one-hour rewind of the clock, a $100 cash add, and an outline on NPCs within 30 meters.

## Lost Castle 2

IL2CPP plugins for single-player.

- **Spawn menu.** F9 searches a catalog and spawns the selected item in camp or in a dungeon. Game-master flags are on only while that menu is open.
- **RPG layer.** Level and experience, with unspent points for attack (+5% each), health (+20), defense (+3%), steal (+1%), attack speed (+5%), and move speed (+2.5%). The menu does not tint the whole screen.
- **Treasure stack.** Matching treasure powers stack instead of replacing the one already held.

## Gamble With Your Friends

Plugins for this Unity party game. The host can set the lobby size from 2 to 32, and guests do not need the plugin to join. A ranking panel, host loans, bet insurance, and a day-timer window sit on top of the normal tables.

At the start of a session each player picks one of four classes. Thieves are capped at two people. Press 1, 2, 3, or 4 to choose.

**Casino boss.** Starts with $10,000 and 20 tickets and has no quota. Gold-ticket prices in the shop are cut in half and do not rise. Other players using a machine the boss bought pay the boss a cut, and a share of other players’ losses goes to the boss. The boss can tip players or loan them money, which has to be paid back. The goal is to stake a set amount and earn it back at a multiplier.

**Gambler.** Starts with the normal bank and a quota. Upgrades never reset and can stack. A daily basketball shot can double the bonus or the quota. Once a day the gambler can mark another player for a duel, and the winner takes the loser’s cash. The goal is to win a set amount.

**Thief.** Can steal cash from other players or from NPCs, disguise as another player or an NPC, and move faster. Stealing has a cooldown. At most two thieves can be in the room. The goal is to steal a rising percent of other people’s cash.

**Security.** Starts with $500 and 5 tickets and works for the boss. The bat and the pistol are free, and picking up the bat is 15% faster. Holding a thief for 2 seconds catches them: stolen NPC cash is seized, and catching a player-thief returns the money and raises the guard’s pay. If the boss’s money is stolen, the guard is not paid the next day. The goal is to protect the boss for three days, then five, then ten in a row.
