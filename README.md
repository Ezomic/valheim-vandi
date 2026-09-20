# Vandi

Creatures wear more stars in a biome whose boss you keep killing, and that boss comes back
harder the next time you summon it. All of it follows your own kills, not the server's.

**Nothing is built yet.** This repository holds the design and an empty scaffold. The idea is
on the site so people can argue with it before it becomes code:
[Stars worth earning, and bosses that remember you](https://longhouse.thijssensoftware.nl/devlog/stars-worth-earning).

Vandi is Old Norse for trouble.

## The problem

Once a biome's boss is dead, that biome is mostly walking. Its creatures cannot hurt you any
more, and the starred ones actually worth meeting, for the drops or for taming, are rare enough
that finding one is a chore rather than an event. The usual answer is a difficulty setting,
which raises the whole world for everybody at once and lands hardest on the player who has not
got there yet.

The idea came from CRVD in the Discord: let the biomes you have beaten be the ones that get
harder.

## What it would do

- Killing a boss raises the chance of starred creatures **in that boss's own biome**. The Elder
  changes the Black Forest, Moder changes the Mountains. A biome whose boss you have never
  beaten stays exactly as it was.
- Every repeat kill of that boss adds more. Five percentage points a kill on vanilla's ten,
  stopping at twenty five extra, so a biome whose boss you have killed five times spawns starred
  creatures a third of the time. Creatures still cap at two stars: only the odds move, never the
  ceiling.
- Each repeat kill also makes that boss **one star harder** the next time you summon it, capped
  at two.
- All of it is yours. A player who joined last week walks into an ordinary Black Forest while
  the veteran standing beside them does not.

Starred creatures drop more and a harder boss drops more, so the mod pays for the trouble it
makes. It is a reason to go back to a biome rather than a reason to avoid one.

## The part that will start arguments

Credit for a boss kill goes to the player who **made the offering**, and to nobody else. They
are credited even if they are dead, offline or across the map when it falls, because they paid
for it. Help a friend kill their Bonemass and you get nothing from it; summon your own and it
counts.

That is deliberate. Credit shared by everyone standing nearby turns a personal record into a
server score, and the one promise here is that your world reflects what you have done yourself.
The cost is real, and worth saying out loud: a group that always fights on one person's
offerings only moves one person's world, so everyone summons their own.

## Why it is not part of Utangard

Utangard already knows who has beaten what, so the boss half could have lived there. It does
not, because the work is a patch on the roll that **every creature spawn in the game** goes
through, and that is a much wider seam than Utangard's promise. Two different jobs, two mods,
and either one can be uninstalled without the other noticing.

## Multiplayer

Everyone will need it, at the same build. The star roll happens on whichever client owns the
zone a creature spawns in, so one player without Vandi spawns ordinary creatures for everybody
standing around them.

## Licence

MIT. See `LICENSE`.
