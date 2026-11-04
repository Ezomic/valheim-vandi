# Changelog

## Unreleased

- **A refused offering no longer takes the boss.** Offering at an altar while someone else's boss was
  on its way used to name you as the summoner, although the altar turned you away and took
  nothing. Their boss then came at your record and its kill counted for you. The summoner
  is now written down only once the altar has accepted the offering.
- **Another mod can ask for a harder boss.** `VandiApi.AskHarder(bossKey, extraStars)`, called on the
  machine whose offering was accepted, adds that many stars to what the summoner's record gives the
  boss, and they may pass `BossStarCap`. `VandiApi.IsHarderSummon(boss)` says whether a boss got its
  stars that way, so a mod can reward the harder fight without knowing how it is written down. With
  `HarderBosses` off an ask buys nothing and leaves no stamp. An ask whose boss never came is removed
  by the next offering the altar accepts, so a plain summon stays plain. Nothing in the game calls
  this yet: Hausi will. Above the cap a boss keeps gaining health, damage and drops, but its speed
  holds at the cap and the game draws it no star marker. README has the details.
- **A starred boss is quicker on its feet and with its swing.** Five percent faster for each
  star, in both walking and attacking, so a two star boss is ten percent up on both. Movement is the
  boss's own speed fields; attacks play the swing animation faster, which also shortens the warning
  before a hit by the same fraction, so the telegraph is the thing to watch when tuning it. Turning
  speed, health and every other animation are untouched. Only the machine that owns the boss changes
  anything, and it works the stars out from the level stored on the boss each frame, so a reload or a
  change of owner carries it over. Moder is set to half the movement bonus (a flier's speed is large
  and she needs no help), Yagluth and the Queen to none (they barely walk). Settings `BossPace`,
  `BossMovePercentPerStar`, `BossAttackPercentPerStar` and `BossPaceOverrides`
  (`boss:moveMultiplier:attackMultiplier`). Checked by the `vandi-boss-pace` scenario against the
  Verbose log; not watched in game.
- **Every boss you have beaten gets a move of its own, and every one warns you first.** A boss
  summoned with one star or more, by someone who has killed it before, now uses the move below
  in the middle of its fight. Each is marked on the ground for seconds before it can hurt
  anyone, only one runs at a time (a boss does not start a move while the Bonemass fan is going,
  or the other way round), the first comes thirty seconds or more into the fight, and a boss at
  no stars never uses one. Every move is decided by the machine that owns the boss and drawn on
  every screen; nothing is played from a file, the warnings are shapes laid on the terrain. Every
  number is a setting, each move has an `Enabled` switch in its own section, `BossSpecials` still
  turns them all off, and `SpecialGap` and `SpecialDamageScale` apply to all. The damage numbers
  are first guesses and have not been balanced.
- **Bonemass: the bog ring.** The ground around him is marked out to fourteen metres for three
  seconds, then a ring of poison rolls out across it. Walk out of the mark, or be in the air when
  the ring passes under you.
- **The Elder: the root wave.** A strip of ground toward you is marked for two seconds, then roots
  erupt along it in a rolling front. Step sideways, or jump as the front passes.
- **Yagluth: the meteor volley.** Six spots are marked around you, the first one on you, a half
  second apart. Each is struck by a meteor when its own three seconds are up.
- **The Seeker Queen: the seeker swarm.** A spiral of spots is marked around her, and a seeker
  rises out of each in turn and hunts you. They are hers: they drop nothing and each death chips
  her, up to the same cap as the Bonemass adds. She is not summoned at an altar, so Vandi never
  gives her stars or a summoner and this move stays dormant until it does.
- **Moder: the ice ring.** A circle of ground where you stand is marked for three seconds, then
  freezes for six. The frost bites when it forms and every second after, and slows you.
- **Eikthyr: the charge.** A line follows you for a second and a half, then locks with a ring at
  its far end. After a short beat he runs the line on his own run animation and the ring goes
  off in lightning. His AI is switched off for the length of the move and back on after it.
- **Fader: the spokes.** His choice, left open. Six spokes of ground are marked around him like
  a wheel, one aimed at you, and fire erupts along all of them. The wedges between are safe, and so
  is the ground within four metres of him.

- **Bonemass has a move of his own once you have beaten him.** A Bonemass you summoned with stars
  rears up for a second and a half, then throws three skeletons in a fan over about five seconds,
  twenty seconds into the fight and every thirty after. Those skeletons are his: they drop nothing,
  and each one that dies takes three percent off his health, up to a third of the bar in all, so
  they shorten the fight and never replace it. To pay for that he arrives with half as much health
  again on top of what his stars already give. Settings `BossSpecials`, `BossHealthMultiplier`,
  `AddChipPercent`, `AddChipCapPercent` and the `Fan*` family. The rear-up is bones posed in code
  and has only been seen in a poison cloud, so look at it before trusting the angles.
- **His fight keeps sending him help, and the help speeds up as he weakens.** While you fight a
  starred Bonemass a skeleton arrives every 25 seconds from the edge of the fight and hunts you
  straight away, and the gap shrinks to 8 seconds as his health runs down, six alive at most. They
  are his, so they drop nothing and each death chips him. Settings `AddIntervalStart`,
  `AddIntervalEnd`, `AddMaxAlive`, `AddRingMin` and `AddRingMax`.
- **Starred Bonemass drags the swamp into the fight.** Wild Swamp creatures already within 60 metres of
  him are drawn in and hunt you, nearest first, a couple at a time on the same clock as the skeleton adds
  (slow at full health, quicker as he weakens), eight at most in a whole fight. They are the game's own
  creatures: they keep their loot and their deaths do not chip him. Tamed creatures, players, bosses,
  fish, anything already hunting and his own adds are left alone, and a creature is judged by the biome it
  stands in, so a Boar in the Meadows is not his to call. Settings `PullWild`, `PullRadius`, `PullPerWave`
  and `PullMaxPerFight`.
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
