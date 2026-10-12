# Crime Simulator

BepInEx plugins for the Unity (Mono) build. They add third person, cash item drops, extra police types, a carry bag, and a skill shop.

## Third person

`comfort-camera` — `com.kaboo.crimesim.antisickness`.

O switches first and third person for the local player. The camera sits behind the eyes, from 1 to 6 meters, and stops at walls. Aiming still uses the eye position, so the crosshair matches the shot. The local body fades inside 2 meters. Other players see a solid character. The view returns to first person while dead or in a minigame.

The same plugin cuts motion sickness: field of view, no walk bob, no screen shake, no sprint or jump field-of-view kick, scaled hand sway, no motion blur, and no lens distortion.

## Cash and item drops

`skill-shop` — `com.kaboo.crimesim.skillcash`.

Cash stays with the player who picks it up. X drops one bill onto the ground, chosen from denominations the player can afford. Job maps spawn a set of street bills each day. Police drop first aid, stun darts, an armor vest, or a $20, $50, or $100 bill.

The same plugin is the shop: skill books, gear, experience packs, and the upgrade rows (run speed, jump, bag size, health, stamina, field repair, highlight, damage immunity, heal, stealth). Death removes one level from run speed, jump, health, and stamina. A paid revive, a 10-second group timer when everyone is down, home selling, and paid item repair are in this plugin too.

## More police

`bounty-board` — `com.kaboo.crimesim.bounty`.

Each officer is standard, fast (1.5 times speed), tank (5 times health), or tracker (keeps the player in sight). Higher wanted stars roll the special types more often. After four arrivals with no tracker, the next officer is a tracker. The bounty total is on the HUD, and police who catch the player take it plus a fine.

## Carry bag

`carry-bag` — `com.kaboo.crimesim.duffelbag`. Requires the skill shop.

Sellable loot goes into the bag first. Capacity starts at 3 and the shop can raise it to 10. G drops the newest item.

## Store

`store-restock` — `com.kaboo.crimesim.shop`. Requires the skill shop.

Shop stock refills when the day advances. The furniture purchase cap is 10.

## Host session

`host-session` — `com.kaboo.crimesim.hostbuffs`.

Host-only toggles: double jump, 1.5 times move speed, rewind the clock one hour, add $100, and outline NPCs within 30 meters.
