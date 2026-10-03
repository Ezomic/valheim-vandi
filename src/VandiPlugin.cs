using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using Ezomic.Core;
using HarmonyLib;

namespace Vandi
{
    /// <summary>
    /// Vandi. One sentence saying what the mod does, then a paragraph saying why it is
    /// worth having - the design argument, not the feature list. That paragraph is the thing
    /// future-you reads first.
    ///
    /// Say here whether the mod is client-side, and say it in terms of where the work
    /// happens rather than by habit. "Client-side" means every effect is computed by the
    /// owning client off state it already has. The moment a decision reads another player's
    /// progress, writes a shared ZDO, or registers a prefab, it is not client-side any more
    /// and Requirement.Everyone below is load-bearing.
    ///
    /// There is deliberately no BepInProcess attribute. A dedicated server runs
    /// valheim_server.exe, and Core's gate only refuses on the server side of RPC_PeerInfo -
    /// so a mod that must be enforced has to be allowed to load there.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    // Soft, not hard. A hard dependency that is absent does not degrade - the plugin never
    // loads at all - and every mod here has to be installable on its own, because a stranger
    // should not need two installs to get one mod. Soft still buys the load-order guarantee
    // when Core is present, which is all that registering with the gate needs.
    [BepInDependency(CoreGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class VandiPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "ezomic.valheim.vandi";
        public const string PluginName = "Vandi";
        public const string PluginVersion = "1.0.0";
        public const string PluginAuthor = "Robbin Thijssen";

        /// <summary>Core's plugin GUID. Optional - see TryRegisterWithCore.</summary>
        private const string CoreGuid = "ezomic.valheim.core";

        internal static ManualLogSource Log;

        /// <summary>
        /// Says something once per message, however often it is reached.
        ///
        /// Config is parsed from an Update-driven path, so a misspelled biome in BossBiomes
        /// would otherwise write the same line sixty times a second for the rest of the
        /// session and bury whatever came before it.
        /// </summary>
        internal static void LogOnce(string message)
        {
            if (message == null || !_said.Add(message)) return;

            Log.LogWarning(message);
        }

        private static readonly System.Collections.Generic.HashSet<string> _said =
            new System.Collections.Generic.HashSet<string>();

        /// <summary>
        /// Whether Core answered at load. Worth keeping even when nothing reads it yet: the
        /// difference between gated and ungated is invisible to a player otherwise, and this
        /// is what a warning on spawn would be driven by.
        /// </summary>
        internal static bool CorePresent;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            // Config first. Registering absorbs every entry the mod has bound, so anything
            // bound after this line is carried only because Core re-absorbs at manifest
            // time - and depending on the order of two lines in an Awake is not a thing
            // worth relying on.
            VandiConfig.Bind(Config);

            TryRegisterWithCore();

            // PatchAll over a named type, never the whole assembly. A bare PatchAll() walks
            // every type in the DLL, so a half-written patch class in another file goes live
            // the moment it compiles.
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(Stars));
            _harmony.PatchAll(typeof(Summon));
            _harmony.PatchAll(typeof(Kills.Readout));
            Metal.Wire();

            // Fenced on its own: a drop method renamed by an update must cost the metal half and
            // never the star half, and PatchAll throws out of Awake on the first patch it cannot
            // resolve.
            try { _harmony.PatchAll(typeof(Metal)); }
            catch (System.Exception error)
            {
                Log.LogWarning("Metal doubling could not be fully patched and may be partly off, "
                    + "since PatchAll applies the patches it reaches before the one that failed. "
                    + "The rest of Vandi is unaffected: " + error);
            }

            // One seam at a time, each on its own, so a game update that renames one of the
            // compendium's methods costs the page and never the star roll or the kill credit.
            PatchPage(typeof(CompendiumPage.ListPatch));
            PatchPage(typeof(CompendiumPage.ShowPatch));
            PatchPage(typeof(CompendiumPage.HudPatch));

            // The startup line every mod in the suite writes. It is how a log answers "which
            // build of what is actually loaded" without anyone guessing.
            Log.LogInfo(PluginName + " " + PluginVersion + " by " + PluginAuthor + " - ready.");
        }

        private void PatchPage(System.Type patch)
        {
            try
            {
                _harmony.PatchAll(patch);
            }
            catch (System.Exception error)
            {
                Log.LogError("The compendium page could not be patched in (" + patch.Name + "), so it "
                    + "stays out this session. Nothing else is affected. " + error.Message);
            }
        }

        /// <summary>
        /// Joins Core's version gate when Core is installed, and does nothing when it is not.
        ///
        /// Name here exactly what standing alone costs, because it is usually not the mod.
        /// For most of these it is the *enforcement*: without Core nothing refuses a client
        /// that lacks the plugin, so the rule becomes an agreement between players rather
        /// than a property of the server. That is a real loss and a legitimate choice, and
        /// it is the server owner's to make - which is why this logs rather than refusing
        /// to run.
        /// </summary>
        private void TryRegisterWithCore()
        {
            CorePresent = Chainloader.PluginInfos.ContainsKey(CoreGuid);

            if (!CorePresent)
            {
                Log.LogInfo("Core not installed - running standalone, without the version gate.");
                return;
            }

            RegisterWithCore();
        }

        /// <summary>
        /// Kept separate and never inlined on purpose. The JIT resolves the assemblies a
        /// method needs when it first compiles that method, so a Suite call sitting directly
        /// in Awake would drag Ezomic.Core in before the check above could prevent it - and
        /// the missing-assembly exception would land during plugin load, which is the exact
        /// failure this arrangement exists to avoid. Isolating it means the type is only
        /// ever resolved on a machine that has Core.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RegisterWithCore()
        {
            // Requirement.Everyone or Requirement.HostOnly, and the choice is not a matter of
            // taste. Everyone for anything that registers a prefab or changes item data,
            // whether it looks networked or not: a client that cannot resolve a prefab hash
            // does not fail loudly, ZNetScene discards the ZDO as junk and the thing a player
            // built is simply gone. HostOnly only when a client without the mod is genuinely
            // unaffected.
            Suite.Register(PluginGuid, PluginName, PluginVersion, Config, Requirement.Everyone);

            // Registering already absorbs the whole config file, so this is a formality now.
            // It is still worth writing: naming an entry here is saying out loud that the
            // host decides it. Keybinds are excluded by Core itself - a host taking away
            // someone's keys for the evening is the kind of sync that gets a mod uninstalled.
            Suite.Sync(VandiConfig.Enabled);

            try
            {
                KeepPageLocal();
            }
            catch (System.Exception error)
            {
                // An older Core has no Suite.Local. The switch is then the host's like every other
                // entry, which is a worse default and not a broken mod.
                Log.LogInfo("Core cannot keep ShowCompendiumPage personal (" + error.Message + ").");
            }

            // If the mod reads a data file that decides what it does, hash it too. The gate
            // catches two ends on different builds; it cannot catch two ends running the
            // same build over different text unless it is told.
            //
            //     Suite.Data(File.ReadAllText(path));
        }

        private void Update()
        {
            Metal.Tick();
        }

        /// <summary>
        /// What the compendium page draws is one player's own screen. Core syncs a mod's whole config
        /// from the host unless told otherwise, so the switch is declared local. Its own method and
        /// never inlined, for RegisterWithCore's reason one level down: an old Core without
        /// Suite.Local then fails this call, which the caller catches, rather than the JIT of
        /// RegisterWithCore and the version gate with it.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void KeepPageLocal()
        {
            Suite.Local(VandiConfig.ShowCompendiumPage);
        }

        private void OnDestroy()
        {
            // UnpatchSelf, never UnpatchAll(). The argumentless one unpatches every mod in
            // the process, not just this one.
            if (_harmony != null) _harmony.UnpatchSelf();
        }
    }
}
