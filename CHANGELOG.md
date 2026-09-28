# Changelog

## 0.1.0 - unreleased

The first version is the whole mod. It is written down here before it is built, because the
design went public for opinions first and it can still change on the strength of them.

### Planned

- **Starred creatures get likelier in a biome whose boss you have killed.** Five percentage
  points a kill on vanilla's ten, capped at twenty five extra, so a biome whose boss you have
  killed five times spawns starred creatures a third of the time. The cap on stars themselves
  does not move.
- **A boss you have killed before comes back harder.** One star stronger per repeat kill by the
  same player, capped at two.
- **All of it is per player.** Your own kills decide what you meet, so two people standing in
  the same forest can be having different evenings.
- **Credit goes to whoever made the offering**, even if they are dead, offline or far away when
  the boss falls. Helping with somebody else's boss earns nothing, which is what keeps the
  record personal.

### For other mods

- **Other mods can read your kill count.** `VandiApi` answers how many times a player has
  killed a boss, and whether Vandi counts that boss at all. It only reads. Malmr uses it to
  open a metal's vein mining once that biome's boss has been beaten at one star.

### Console

- `vandi` prints the boss kills this world has credited to you, one line per boss Vandi
  counts. It only reads, so it is not a cheat and needs no devcommands.
