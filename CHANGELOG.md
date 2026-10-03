# Changelog

## Unreleased

- **A boss killed three times doubles the metal of its own biome, for you.** The third kill is
  the one summoned at two stars. Copper and tin for the Elder, iron for Bonemass, silver for
  Moder, black metal for Yagluth and flametal for Fader, worked out from what the furnace and
  the blast furnace take in rather than listed. It follows the metal and not the place, so a
  Mistlands copper deposit doubles for the Elder's player. Iron scrap, scrap piles and a fuling's
  black metal scrap count, bars never do. Eikthyr and the Queen give nothing, and bloodgold waits
  for the Deep North's boss. New settings `DoubleAtKills`, `DoubleBosses` and `NeverDouble`, and
  `vandi metal` in the console.
- **It happens when the drop is made, to the player whose blow made it.** Never on pickup, so a
  friend cannot mine for you. A vein Malmr breaks goes to the player whose swing filled the bar.
  The machine that owns the rock makes the decision from the world's global keys, so a client
  that does not own it is covered. Not run in game yet.
- **A creature's metal doubles from its drop list, made when it dies.** Most creatures drop their
  loot from a ragdoll a moment after death, and the first version only looked while the creature
  itself dropped, so a fuling's scrap almost never doubled. A blow that breaks a deposit inside
  another (a scrap pile that spawns a rock) no longer ends the outer blow's credit, a drop that
  fails to double is left as the game made it and logged once, and the metal list is built when
  the world loads instead of at the first drop.
- **A Vandi page in the compendium.** It is the third entry, after Active Effects and Logs, so
  opening the compendium still lands where it always did. The left side lists
  every boss Vandi counts, grouped by biome, with your own kill count on each. Pick one and the
  right side shows what each kill adds to the star chance in its biome, which kill the boss
  comes back with a star on, the stars the next summon will have, and where you stand on the
  ladder. Either side scrolls on its own, with the compendium's scrollbar, when it is taller than
  the window. Every figure comes from the same functions the star roll and the altar use, so the page
  cannot say something the world does not do. `ShowCompendiumPage` in the config turns it off,
  and it stays each player's own setting under Core.
- **Malmr's unlocks show on it.** With Malmr installed, a boss that gates one of its metals says
  what that metal is waiting for: how many more kills, the Pickaxes level, or that it is open. The
  other bosses that still gate a metal are listed under The road ahead. Without Malmr there is no
  Malmr line and nothing else changes. Vandi reads Malmr's own table and gate for this by
  reflection, so the two stay in step, and a Malmr it cannot read costs only those lines. It is
  read once each time the page is opened, a failed read is tried again after a few seconds, and
  only the third in a row, or a Malmr that lacks a member the page needs, ends it for the session.
- Two Devkit scenarios: `vandi-compendium-page` makes a real kill at an altar and checks the
  page's numbers move, and `vandi-compendium-malmr-line` checks the Malmr lines against seeded
  kills. Both include the `tall` checks that catch a label squeezed to no height.
- The page is built only from code, with no bundle asset, and is rebuilt for every new compendium.
  Its only borrowed look is a copy of the compendium's scrollbar. It was written without being run in game; the first look is the first test.

## 1.0.0 - 2026-09-30

First release. Both Devkit scenarios in `scenarios/` passed on 2026-09-29, and a two-player
run on a dedicated server credited each boss kill once, to the summoner.

- **Starred creatures get likelier in a biome whose boss you have killed.** Five percentage
  points a kill, up to twenty five extra, added to whatever the game already rolls where the
  creature spawns. That is ten in an ordinary stretch of a default world, so there a biome whose
  boss you have killed five times spawns starred creatures about a third of the time. The game
  rolls higher in parts of the world far from the centre and lower on a world set to fewer
  stars, and the points go on top either way. Creatures still stop at two stars, and a biome
  whose boss you have never killed is left as it was.
- **Each boss owns its own biome.** Eikthyr the Meadows, the Elder the Black Forest, Bonemass
  the Swamp and the Ocean, Moder the Mountains, Yagluth the Plains, the Queen the Mistlands, and
  Fader the Ashlands and the Deep North. `BossBiomes` in the config changes the pairs. Open-world
  spawns, camps and dungeon spawners all go through the same roll, and a dungeon counts as the
  biome above its entrance.
- **A boss you have killed before comes back harder.** One star for each earlier kill by whoever
  made the offering, up to two. `HarderBosses` turns this half off and leaves the rest alone.
- **Credit goes to whoever made the offering**, even if they are dead, offline or far away when
  the boss falls. Helping with somebody else's boss earns nothing. A boss that was not summoned
  at an altar with Vandi installed credits nobody.
- **The count is kept in the world, not on your character**, one global key per player per
  boss. The kill is recorded by whichever machine owned the boss when it died, which is often
  not the summoner's, so the world is the one place both can reach.
- **The star roll belongs to the zone owner.** Spawns roll on whichever client owns the zone,
  against that client's own record, so two people in the same forest both get the owner's odds.
  Alone in a biome you always get your own.

Everyone needs it, at the same build. With Core installed it registers at
`Requirement.Everyone`, because a player without Vandi spawns ordinary creatures for everyone
near them. The host's settings apply to everybody.

### For other mods

- **Other mods can read your kill count.** `VandiApi.BossKills` and `LocalBossKills` answer how
  many times a player has killed a boss, and `CountsKillsOf` whether Vandi counts that boss at
  all. It only reads, and answers the same whether Vandi is switched on or not. Malmr uses it to
  open a metal's vein mining once that biome's boss has been beaten at one star.

### Console

- `vandi` prints the boss kills this world has credited to you, one line per boss Vandi
  counts, and the star chance where you stand with Vandi's share of it. It only reads, so it
  is not a cheat and needs no devcommands.
