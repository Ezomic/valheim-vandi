using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace VandiProbe
{
    /// <summary>
    /// `vandiprobe` in the console: what the harder-summon scenarios need and Devkit has no step
    /// for. It calls VandiApi the way Hausi does, by reflection through BepInEx's plugin list and
    /// from a postfix on OfferingBowl.RPC_BossSpawnInitiated, so a pass means the shape another mod
    /// binds to at run time works, and on a Vandi without the two methods every ask fails out loud.
    ///
    /// <b>A DLL of its own and never in a pack.</b> The suite's other probes ship in the mod because
    /// they only read. This one asks for a harder boss, and a harder boss is what Hausi pays a relic
    /// for, so a console command in Vandi's own DLL would be a free relic. Nothing builds it but its
    /// own csproj, and no list in own-profile names it.
    /// </summary>
    [BepInPlugin("ezomic.valheim.vandiprobe", "VandiProbe", "1.0.0")]
    [BepInDependency("ezomic.valheim.vandi")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private const string VandiGuid = "ezomic.valheim.vandi";

        private static int _armedStars;
        private static BaseUnityPlugin _vandi;
        private static bool _registered;

        private void Awake()
        {
            new Harmony("ezomic.valheim.vandiprobe").PatchAll(typeof(Patches));
        }

        private static class Patches
        {
            [HarmonyPatch(typeof(Terminal), "InitTerminal")]
            [HarmonyPostfix]
            private static void Register()
            {
                if (_registered) return;
                _registered = true;

                new Terminal.ConsoleCommand("vandiprobe",
                    "- ask <boss> <stars> | arm <stars> | asked <boss> | boss <prefab> [radius] | isharder null|noview <prefab> "
                    + "| known | set <section> <key> <value> | altar | claimaltar",
                    OnCommand, isCheat: false);
            }

            // Where Hausi asks from: only the sender of an accepted offering gets this call.
            [HarmonyPatch(typeof(OfferingBowl), "RPC_BossSpawnInitiated")]
            [HarmonyPostfix]
            private static void Accepted(OfferingBowl __instance)
            {
                if (_armedStars == 0 || __instance == null || __instance.m_bossPrefab == null) return;

                Character boss = __instance.m_bossPrefab.GetComponent<Character>();
                if (boss == null) return;

                int stars = _armedStars;
                _armedStars = 0;

                string problem = Ask(boss.m_defeatSetGlobalKey, stars);
                UnityEngine.Debug.Log("vandiprobe: armed ask for " + boss.m_defeatSetGlobalKey + " at " + stars + ": " + (problem ?? "called"));
            }
        }

        private static void OnCommand(Terminal.ConsoleEventArgs args)
        {
            Terminal term = args.Context;
            if (term == null) return;

            string verb = args.Length > 1 ? args[1].ToLowerInvariant() : "";

            switch (verb)
            {
                case "ask": term.AddString("ask=" + (Ask(Token(args[2]), Int(args[3])) ?? "called")); return;
                case "arm": _armedStars = Int(args[2]); term.AddString("armed=" + _armedStars); return;
                case "asked": Asked(term, args[2]); return;
                case "boss": Boss(term, args[2], args.Length > 3 ? float.Parse(args[3], CultureInfo.InvariantCulture) : 80f); return;
                case "isharder": IsHarder(term, args); return;
                case "known": Known(term); return;
                case "set": Set(term, args[2], args[3], args[4]); return;
                case "altar": Altar(term, false); return;
                case "claimaltar": Altar(term, true); return;
            }

            term.AddString("vandiprobe: unknown verb " + verb);
        }

        private static int Int(string text)
        {
            return int.Parse(text, CultureInfo.InvariantCulture);
        }

        // A console word cannot be null, empty or padded, and those are exactly the inputs a caller
        // can hand over that a word cannot spell.
        private static string Token(string word)
        {
            switch (word)
            {
                case "null": return null;
                case "empty": return "";
                case "blank": return "   ";
                default: return word.StartsWith("padded:") ? "  " + word.Substring(7) + "  " : word;
            }
        }

        private static Type Api()
        {
            if (_vandi == null)
            {
                PluginInfo info;
                if (Chainloader.PluginInfos.TryGetValue(VandiGuid, out info)) _vandi = info.Instance;
            }

            return _vandi == null ? null : _vandi.GetType().Assembly.GetType("Vandi.VandiApi");
        }

        /// <summary>Null when the call was made, otherwise why it could not be.</summary>
        private static string Ask(string key, int stars)
        {
            Type api = Api();
            if (api == null) return "no-vandi-api";

            MethodInfo ask = api.GetMethod("AskHarder", new[] { typeof(string), typeof(int) });
            if (ask == null) return "no-askharder";

            ask.Invoke(null, new object[] { key, stars });
            return null;
        }

        private static string Harder(Character character)
        {
            Type api = Api();
            MethodInfo call = api == null ? null : api.GetMethod("IsHarderSummon", new[] { typeof(Character) });
            if (call == null) return "no-isharder";

            return (bool)call.Invoke(null, new object[] { character }) ? "yes" : "no";
        }

        private static void Asked(Terminal term, string boss)
        {
            string name = "vandi_harder_" + boss;
            string value;
            bool set = ZoneSystem.instance.GetGlobalKey(name, out value);

            List<string> all = ZoneSystem.instance.GetGlobalKeys();
            int harder = all.Count(k => k.StartsWith("vandi_harder_", StringComparison.Ordinal));

            term.AddString("asked=" + (set ? value : "none") + " harderkeys=" + harder + " globalkeys=" + all.Count);
        }

        private static void Boss(Terminal term, string prefab, float radius)
        {
            Character best = null;
            float nearest = radius;
            Vector3 here = Player.m_localPlayer.transform.position;

            foreach (Character character in Character.GetAllCharacters())
            {
                if (character == null || Utils.GetPrefabName(character.gameObject) != prefab) continue;

                float away = Vector3.Distance(character.transform.position, here);
                if (away > nearest) continue;

                nearest = away;
                best = character;
            }

            if (best == null) { term.AddString("boss=none"); return; }

            ZNetView nview = best.GetComponent<ZNetView>();
            ZDO zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;

            // The stamp read raw as well as through the API, so a pass on `harder=` cannot be the API
            // answering yes by some other route than the one written down.
            term.AddString("harder=" + Harder(best)
                + " stamp=" + (zdo == null ? -1 : zdo.GetInt("vandi_harder".GetStableHashCode()))
                + " level=" + best.GetLevel()
                + " summoned=" + (zdo != null && zdo.GetLong("vandi_summoner".GetStableHashCode(), 0L) != 0L ? "yes" : "no")
                + " mine=" + (zdo != null && zdo.GetLong("vandi_summoner".GetStableHashCode(), 0L) == Player.m_localPlayer.GetPlayerID() ? "yes" : "no")
                + " owner=" + (nview != null && nview.IsOwner() ? "me" : "other")
                + " maxhp=" + best.GetMaxHealth().ToString("0", CultureInfo.InvariantCulture));
        }

        private static void IsHarder(Terminal term, Terminal.ConsoleEventArgs args)
        {
            if (args[2] == "null")
            {
                term.AddString("isharder=" + Harder(null));
                return;
            }

            // A prefab's own component: a real Character whose ZNetView has never had a ZDO, which is
            // the state a boss's view is in once OnDeath has let go of it.
            GameObject prefab = ZNetScene.instance.GetPrefab(args[3]);
            Character character = prefab == null ? null : prefab.GetComponent<Character>();
            if (character == null) { term.AddString("isharder=noprefab"); return; }

            ZNetView nview = character.GetComponent<ZNetView>();
            term.AddString("isharder=" + Harder(character) + " valid=" + (nview != null && nview.IsValid() ? "yes" : "no"));
        }

        private static void Known(Terminal term)
        {
            PlayerProfile profile = Game.instance.GetPlayerProfile();

            int total = 0, harder = 0;
            foreach (string key in profile.m_playerStats[0].m_knownWorldKeys.Keys)
            {
                total++;
                if (key.StartsWith("vandi_harder_", StringComparison.Ordinal)) harder++;
            }

            term.AddString("known=" + harder + " knowntotal=" + total);
        }

        private static void Set(Terminal term, string section, string key, string value)
        {
            if (Api() == null) { term.AddString("set=no-vandi"); return; }

            ConfigEntryBase entry = _vandi.Config.Keys.Contains(new ConfigDefinition(section, key))
                ? _vandi.Config[new ConfigDefinition(section, key)]
                : null;

            if (entry == null) { term.AddString("set=no-such-entry"); return; }

            entry.SetSerializedValue(value);
            term.AddString("set=" + entry.GetSerializedValue());
        }

        private static void Altar(Terminal term, bool claim)
        {
            OfferingBowl altar = null;
            float nearest = float.MaxValue;
            Vector3 here = Player.m_localPlayer.transform.position;

            foreach (OfferingBowl bowl in UnityEngine.Object.FindObjectsOfType<OfferingBowl>())
            {
                float away = Vector3.Distance(bowl.transform.position, here);
                if (away >= nearest) continue;

                nearest = away;
                altar = bowl;
            }

            ZNetView nview = altar == null ? null : altar.GetComponentInParent<ZNetView>();
            if (nview == null || !nview.IsValid()) { term.AddString("altarowner=none"); return; }

            if (claim) nview.ClaimOwnership();

            term.AddString("altarowner=" + (nview.IsOwner() ? "me" : "other"));
        }
    }
}
