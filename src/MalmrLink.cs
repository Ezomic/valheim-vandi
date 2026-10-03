using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using UnityEngine;

namespace Vandi
{
    /// <summary>
    /// What Malmr has hung off Vandi's kill counts, read for the compendium page.
    ///
    /// Malmr depends on Vandi, softly, and not the other way round: Vandi has no reference to it
    /// and must run exactly as before on a machine that does not have it. So this file names no
    /// Malmr type. It finds Malmr's assembly through BepInEx's list of loaded plugins, which holds
    /// only plugins that really loaded, and calls into it by reflection. Every binding is made
    /// lazily and inside a try, so a Malmr that renames a method costs the page its Malmr lines and
    /// nothing else, and says so once in the log.
    ///
    /// It reads Malmr's own table and Malmr's own gate rather than working the rule out again. The
    /// unlock levels, which boss an entry waits for, how many kills it needs and the Pickaxes level
    /// the character has all come from the same code the vein swing asks, so the page cannot say a
    /// metal is open while the pickaxe says it is not. The one number taken from Vandi instead is
    /// the kill count, which is Vandi's to begin with and is what Malmr reads too.
    ///
    /// The members it reaches are internal to Malmr (UnlockTable, Gate.For, Vein.EarnedLevel,
    /// Deposits.Noun and DisplayName, MalmrConfig.NameFor). That is a coupling, and it is written
    /// down here and in Malmr's changelog rather than hidden. A small public API on Malmr would be
    /// tidier and is the thing to do if a third mod ever wants this.
    /// </summary>
    internal static class MalmrLink
    {
        private const string MalmrGuid = "ezomic.valheim.malmr";

        /// <summary>The "any metal" entry Malmr's tables may hold. It names no metal and is skipped.</summary>
        private const string AnyMetal = "*";

        /// <summary>One metal that waits for a boss, as Malmr has it set right now.</summary>
        internal sealed class Unlock
        {
            /// <summary>Malmr's own name for it, the Unlocks entry: "Copper".</summary>
            internal string Entry;

            /// <summary>One deposit as the screen calls it: "Copper vein", "Giant brain".</summary>
            internal string Noun;

            /// <summary>What a list calls it: "Silver", "Giant brains".</summary>
            internal string Plain;

            /// <summary>The Pickaxes level it needs.</summary>
            internal int Pickaxes;

            /// <summary>The Pickaxes level the character has, floored as Malmr floors it.</summary>
            internal int PickaxesNow;

            /// <summary>Kills of the boss it needs, which through Vandi is BossKills.</summary>
            internal int KillsNeeded;

            /// <summary>The defeat key it waits for, lowercase.</summary>
            internal string Boss;
        }

        private static bool _bound;
        private static bool _usable;
        private static bool _warned;

        private static Type _config, _gate, _vein, _deposits;
        private static MethodInfo _table, _gateFor, _earned, _noun, _display, _nameFor;
        private static FieldInfo _gUnlock, _gBoss, _gKillsNeeded;
        private static PropertyInfo _gOff;

        /// <summary>
        /// Every metal Malmr ties to a boss, in its Unlocks order. Empty when Malmr is not loaded,
        /// when its BossKills is 0 (the boss half switched off), and when anything about it could
        /// not be read, which is the page's cue to show no Malmr line at all.
        /// </summary>
        internal static List<Unlock> Unlocks()
        {
            List<Unlock> found = new List<Unlock>();

            try
            {
                if (!Bind()) return found;

                Player player = Player.m_localPlayer;
                float level = player == null ? 0f : (float)_earned.Invoke(null, new object[] { player });

                IDictionary table = (IDictionary)_table.Invoke(null, null);

                foreach (object key in table.Keys)
                {
                    string entry = (string)key;
                    if (entry == AnyMetal) continue;

                    object gate = _gateFor.Invoke(null, new object[] { entry, level });
                    if (gate == null || (bool)_gOff.GetValue(gate, null)) continue;

                    string boss = (string)_gBoss.GetValue(gate);
                    if (string.IsNullOrEmpty(boss)) continue;

                    string noun = Clean((string)_noun.Invoke(null, new object[] { entry }));
                    string named = Clean((string)_nameFor.Invoke(null, new object[] { entry }));

                    found.Add(new Unlock
                    {
                        Entry = entry,
                        Noun = noun,
                        Plain = named.Length > 0
                            ? named + "s"
                            : Clean((string)_display.Invoke(null, new object[] { entry })),
                        Pickaxes = (int)_gUnlock.GetValue(gate),
                        PickaxesNow = (int)level,
                        KillsNeeded = (int)_gKillsNeeded.GetValue(gate),
                        Boss = boss.ToLowerInvariant(),
                    });
                }
            }
            catch (Exception error)
            {
                Unusable("could not read its unlocks (" + error.GetType().Name + ": " + error.Message + ")");
                found.Clear();
            }

            return found;
        }

        /// <summary>
        /// Malmr's words go into a rich text label, so an angle bracket in a name somebody typed
        /// into its config would open a tag. They are dropped; no real name has one.
        /// </summary>
        private static string Clean(string text)
        {
            return string.IsNullOrEmpty(text) ? "" : text.Replace("<", "").Replace(">", "");
        }

        private static bool Bind()
        {
            if (_bound) return _usable;

            PluginInfoOf(out BepInEx.PluginInfo info);
            if (info == null || info.Instance == null) return false;

            // Not remembered as a failure when Malmr is simply absent: a plugin list that has not
            // finished loading must not stick the page without Malmr for the whole session.
            _bound = true;
            _usable = false;

            Assembly assembly = info.Instance.GetType().Assembly;

            _config = assembly.GetType("Malmr.MalmrConfig");
            _gate = assembly.GetType("Malmr.Gate");
            _vein = assembly.GetType("Malmr.Vein");
            _deposits = assembly.GetType("Malmr.Deposits");
            if (_config == null || _gate == null || _vein == null || _deposits == null)
            {
                Unusable("has no type the page expected");
                return false;
            }

            const BindingFlags any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public
                | BindingFlags.NonPublic;

            _table = _config.GetMethod("UnlockTable", any, null, Type.EmptyTypes, null);
            _nameFor = _config.GetMethod("NameFor", any, null, new[] { typeof(string) }, null);
            _gateFor = _gate.GetMethod("For", any, null, new[] { typeof(string), typeof(float) }, null);
            _earned = _vein.GetMethod("EarnedLevel", any, null, new[] { typeof(Player) }, null);
            _noun = _deposits.GetMethod("Noun", any, null, new[] { typeof(string) }, null);
            _display = _deposits.GetMethod("DisplayName", any, null, new[] { typeof(string) }, null);

            _gUnlock = _gate.GetField("Unlock", any);
            _gBoss = _gate.GetField("Boss", any);
            _gKillsNeeded = _gate.GetField("KillsNeeded", any);
            _gOff = _gate.GetProperty("Off", any);

            if (_table == null || _nameFor == null || _gateFor == null || _earned == null || _noun == null
                || _display == null || _gUnlock == null || _gBoss == null || _gKillsNeeded == null
                || _gOff == null)
            {
                Unusable("is missing a member the page expected");
                return false;
            }

            _usable = true;
            return true;
        }

        private static void PluginInfoOf(out BepInEx.PluginInfo info)
        {
            Chainloader.PluginInfos.TryGetValue(MalmrGuid, out info);
        }

        /// <summary>Said once per session: a page that quietly lost a section is a bug nobody can find.</summary>
        private static void Unusable(string why)
        {
            _usable = false;

            if (_warned) return;
            _warned = true;

            VandiPlugin.Log.LogWarning("The compendium page shows no Malmr lines: Malmr is loaded but "
                + why + ". Malmr and Vandi are probably from different builds.");
        }
    }
}
