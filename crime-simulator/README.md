# Crime Simulator

BepInEx plugins for the Unity (Mono) build. They add third person, cash item drops, extra police types, a carry bag, and a skill shop.

## Third person

`comfort-camera` — `com.kaboo.crimesim.antisickness`.

O switches first and third person for the local player. The camera sits behind the eyes, from 1 to 6 meters, and stops at walls. Aiming still uses the eye position, so the crosshair matches the shot. The local body fades inside 2 meters. Other players see a solid character. The view returns to first person while dead or in a minigame.

The same plugin cuts motion sickness: field of view, no walk bob, no screen shake, no sprint or jump field-of-view kick, scaled hand sway, no motion blur, and no lens distortion.

## Cash and item drops

`skill-shop` — `com.kaboo.crimesim.skillcash`.

Cash stays with the player who picks it up. X drops one bill onto the ground, chosen from denominations the player can afford. Job maps spawn a set of street bills each day. Police drop first aid, stun darts, an armor vest, or a $20, $50, or $100 bill.

The same plugin is the shop: skill books, gear, experience packs, and passive upgrades for run speed, jump, bag size, health, and stamina. Death removes one level from run speed, jump, health, and stamina. Home items can be sold into the shared balance.

Five active skills sit in that shop, each ranked I, II, and III. Only one is equipped. Z uses it. Higher ranks shorten the cooldown, and III adds an extra effect.

- Field repair. Hold Z for 3 seconds and pay cash to fix the held item. Cooldown 30 / 15 / 5 seconds. Rank III halves the cost.
- Highlight. Outlines people through walls within 30 meters. Rank I is 5 seconds (60 second cooldown). Rank II is 10 seconds (45). Rank III is 10 seconds (30) and also outlines loot and safes.
- Damage immunity. Health is locked. Rank I is 10 seconds (180 second cooldown). Rank II is 15 seconds (120). Rank III is 20 seconds (60), plus 20% move speed, 50% jump, and no stamina drain.
- Heal. Clears damage. Cooldown 60 / 45 / 30 seconds. Rank III revives a downed teammate on the interact key, with no first aid kit.
- Stealth. The body fades and NPCs search the last known spot. Rank I is 15 seconds (180 second cooldown). Rank II is 20 seconds (120). Rank III is 30 seconds (60) and walks through doors.

Revive is separate from the skill ranks: N spends $500 to get back up. If the whole group is down, they have 10 seconds before the night ends.

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
