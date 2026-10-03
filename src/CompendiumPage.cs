using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Vandi
{
    /// <summary>
    /// The Vandi page of the compendium (LHM-45): your boss kills, what each one has done to the
    /// star chance and to the boss itself, and what Malmr has hung off them.
    ///
    /// Robbin picked this from two mockups on 2026-10-03, "B", with no changes: a list of bosses
    /// grouped by biome on the left, one boss in detail on the right, a ladder of what each kill is
    /// worth, and below it the Malmr unlock tied to that boss and the road still ahead. The mockup
    /// is the spec. Colours, order, copy and the gaps between things are its, and a deviation is
    /// listed in the changelog rather than made quietly.
    ///
    /// <b>It draws inside the compendium's own text area, the way Utangard's page does.</b> The
    /// entry in the list is still a text page (Summary). When it is the one showing, this covers the
    /// text area and blanks the text, so the list, the selection, gamepad up and down, the close
    /// button and Escape all stay vanilla's. Rist's way, a tab of its own on the compendium bar with
    /// a window nothing else closes, would have been a second door to the same screen. The text page
    /// is also the fallback: if a seam is missing or the panel throws, the page says the same things
    /// in words and nothing is wrong that a player can see.
    ///
    /// <b>Every number is the world's number.</b> Kills.LocalCount is the count the altar and the
    /// star roll read, Stars.Earned is what the star roll adds for that count, and Summon.StarsFor is
    /// what the altar gives the boss. The page calls those, and never works a figure out again, so
    /// it cannot say something the spawns do not do. Malmr's side comes from Malmr's own gate through
    /// MalmrLink, and the page simply has no Malmr line when Malmr is not there.
    ///
    /// <b>Sizes are canvas units, and at 1080p they are the mockup's pixels.</b> GuiScaler sets the
    /// canvas scale, so at 1920x1080 and 100% one unit is one screen pixel and the smallest text
    /// here is 12. Fit measures what the compendium's own parents add and draws the page back up to
    /// full size when that is below 1, since 12 px is the floor Robbin reads at, not 12 units of
    /// somebody's transform.
    ///
    /// <b>Sits on the ScrollArea, never on Content.</b> Content is sized by the text inside it, so
    /// the moment the text is blanked it shrinks to nothing and takes the page with it. Utangard's
    /// panel did exactly that in Robbin's screenshot of 2026-09-29: 840 x 565 on the log line when it
    /// was built, 55 high a second later, and every label squeezed to no height. The page grows from
    /// its own content and is never squeezed, so no label can get less than its text needs, and
    /// Host names the rects it climbed past so the log says where it landed.
    ///
    /// <b>Built again for every compendium.</b> A world load destroys the inventory window and the
    /// compendium with it, and a page cloned from the old one's text holds fonts that are gone. It
    /// is forgotten on Hud.Awake, and built again the first time the page is shown in the new
    /// window, so nothing here outlives the UI it was cloned from. It borrows no bundle asset and
    /// every edge and face is a flat colour; the one thing copied is the compendium's scrollbar,
    /// for the two columns that scroll, which dies with the page.
    /// </summary>
    internal static class CompendiumPage
    {
        // ------------------------------------------------------------ mockup B, as data ---

        private static readonly Color PanelFace = Rgb(0x1f1a14);
        private static readonly Color RaisedFace = Rgb(0x2b2318);
        private static readonly Color PanelEdge = Rgb(0x5a4a36);
        private static readonly Color BodyText = Rgb(0xe6dac2);
        private static readonly Color Muted = Rgb(0xa3968a);
        private static readonly Color Orange = Rgb(0xffa500);
        private static readonly Color OpenText = Rgb(0x86c27e);
        private static readonly Color LockedText = Rgb(0xd27e6e);
        private static readonly Color BoxEdge = Rgb(0x4a3d2e);

        private const string OrangeTag = "#ffa500";
        private const string MutedTag = "#a3968a";
        private const string OpenTag = "#86c27e";

        private const float ListHeadSize = 13f;
        private const float RowBiomeSize = 14f;
        private const float RowBossSize = 13f;
        private const float RowCountSize = 18f;
        private const float TitleSize = 20f;
        private const float SubSize = 13f;
        private const float CellSize = 13f;
        private const float CellValueSize = 17f;
        private const float BoxSize = 13f;
        private const float BoxValueSize = 20f;
        private const float MalmrTitleSize = 15f;
        private const float RoadSize = 14f;

        /// <summary>
        /// The mockup's line-height. TextMeshPro sets its lines at the font's own spacing, which for
        /// this font is tighter, so the difference is made up twice: half above and half below every
        /// text (Lead), and a line-height tag for the gap between wrapped lines (Wrapped).
        /// </summary>
        private const float LineHeight = 1.4f;

        private const float ListWidth = 190f;
        private const float ColumnGap = 14f;
        private const float EdgeMargin = 14f;

        /// <summary>The most steps the ladder draws. Five at the defaults.</summary>
        private const int MostSteps = 8;

        private const float RefreshSeconds = 1f;

        /// <summary>Active Effects and Logs, which UpdateTextsList puts at the top of the list.</summary>
        private const int AfterVanilla = 2;

        /// <summary>
        /// What sits round the two columns, top to bottom: the frame's 1 px each side and the body's
        /// 10 above and 14 below. The columns get the page's height less this, and scroll inside it.
        /// </summary>
        private const float ColumnsPadding = 26f;

        /// <summary>Never shorter than this, whatever the host measures, so a bad measure is not a squeeze.</summary>
        private const float LeastColumns = 200f;

        // ------------------------------------------------------------------- state -------

        private static TextsDialog.TextInfo _page;

        private static TextsDialog _dialog;
        private static GameObject _root;
        private static RectTransform _host;
        private static LayoutElement _floor;
        private static LayoutElement _columns;
        private static ScrollRect _listScroll;
        private static ScrollRect _detailScroll;

        private static float _fitScale = -1f;
        private static Vector2 _fitSize;
        private static float _fitPixels = -1f;
        private static float _pixelsPerUnit = 1f;

        private static readonly List<RectTransform> Rims = new List<RectTransform>();

        private static TextsDialog _failedFor;

        private static string _selected;
        private static float _nextRefresh;
        private static string _built;

        private static TMP_Text _proto;
        private static float _naturalPerEm;

        private static readonly List<BossRow> Rows = new List<BossRow>();
        private static readonly List<Rung> Rungs = new List<Rung>();

        private static TMP_Text _title;
        private static TMP_Text _sub;
        private static TMP_Text _off;
        private static GameObject _ladder;
        private static GameObject _ladderGap;
        private static GameObject _pair;
        private static GameObject _pairGap;
        private static Pane _now;
        private static Pane _next;
        private static GameObject _malmrBox;
        private static GameObject _malmrGap;
        private static TMP_Text _malmrBody;
        private static GameObject _roadBox;
        private static GameObject _roadGap;
        private static TMP_Text _roadBody;

        private sealed class BossRow
        {
            public string Key;
            public GameObject Go;
            public Image Edge;
            public Image Face;
            public TMP_Text Biome;
            public TMP_Text Boss;
            public TMP_Text Count;
        }

        private sealed class Rung
        {
            public GameObject Go;
            public Image Edge;
            public Image Face;
            public TMP_Text Kill;
            public TMP_Text Value;
            public TMP_Text Stars;
        }

        private sealed class Pane
        {
            public TMP_Text Head;
            public TMP_Text Value;
            public TMP_Text Sub;
        }

        // ---------------------------------------------------------------- the seams ------

        /// <summary>
        /// The Vandi entry in the compendium's list, put there every time the list is built, beside
        /// Logs and Active Effects. Vanilla builds it in UpdateTextsList and makes a row per entry in
        /// FillTextList, so a postfix is an entry with the game's own skin, font, scrolling and
        /// gamepad handling, none of which this mod then owns.
        ///
        /// The page is the text version. The panel is drawn over it whenever it is showing, and the
        /// text is what stays if the panel cannot be.
        /// </summary>
        [HarmonyPatch(typeof(TextsDialog), "UpdateTextsList")]
        internal static class ListPatch
        {
            private static AccessTools.FieldRef<TextsDialog, List<TextsDialog.TextInfo>> _texts;
            private static bool _bound;

            [HarmonyPostfix]
            private static void Postfix(TextsDialog __instance)
            {
                // Forgotten first, so a page switched off since the last build is not still
                // recognised as ours.
                _page = null;

                if (!VandiConfig.ShowCompendiumPage.Value) return;

                try
                {
                    List<TextsDialog.TextInfo> texts = TextsOf(__instance);
                    if (texts == null) return;

                    // The text page is the whole page in words, and still complete when the panel
                    // is drawn over it. Third, after Active Effects and Logs, which vanilla puts
                    // first: the compendium opens on its first entry (Setup calls ShowText(0)), and
                    // opening on the Vandi page every time would take the screen over from the
                    // thing most people open it for. Another mod that inserts at 0 still goes above.
                    _page = new TextsDialog.TextInfo(VandiPlugin.PluginName, Summary());
                    texts.Insert(Math.Min(AfterVanilla, texts.Count), _page);
                }
                catch (Exception e)
                {
                    _page = null;
                    VandiPlugin.Log.LogError("Compendium page failed to build: " + e);
                }
            }

            private static List<TextsDialog.TextInfo> TextsOf(TextsDialog dialog)
            {
                if (!_bound)
                {
                    _bound = true;

                    try
                    {
                        _texts = AccessTools.FieldRefAccess<TextsDialog, List<TextsDialog.TextInfo>>("m_texts");
                    }
                    catch (Exception e)
                    {
                        VandiPlugin.Log.LogWarning("The compendium has no m_texts list to add the Vandi "
                            + "page to, so there is no page this session: " + e.Message);
                    }
                }

                return _texts == null ? null : _texts(dialog);
            }
        }

        /// <summary>
        /// TextsDialog.ShowText(TextInfo): every way a page gets shown ends here - the compendium
        /// opening on its first entry, a click in the list, gamepad up and down - so one postfix
        /// sees each switch to and away from this page. Private and overloaded with ShowText(int),
        /// so it is found by shape.
        /// </summary>
        [HarmonyPatch]
        internal static class ShowPatch
        {
            [HarmonyTargetMethod]
            private static MethodBase Target()
            {
                foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(TextsDialog)))
                {
                    if (method.Name != "ShowText") continue;

                    ParameterInfo[] args = method.GetParameters();
                    if (args.Length == 1 && args[0].ParameterType == typeof(TextsDialog.TextInfo))
                        return method;
                }

                VandiPlugin.Log.LogError("TextsDialog has no ShowText(TextInfo) any more, so the Vandi "
                    + "page in the compendium stays the plain text version.");

                return null;
            }

            /// <param name="__0">The page being shown. Positional, so a rename cannot unseat it.</param>
            [HarmonyPostfix]
            private static void Postfix(TextsDialog __instance, TextsDialog.TextInfo __0)
            {
                Shown(__instance, __0);
            }
        }

        /// <summary>
        /// A new world builds a new inventory window and compendium, and everything cloned from the
        /// old one's text is on its way out with it. Dropped here, on the Hud that carries them, so
        /// the first look at the page in the new world builds it again from the new text.
        /// </summary>
        [HarmonyPatch(typeof(Hud), "Awake")]
        internal static class HudPatch
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                Forget();
                _failedFor = null;
            }
        }

        private static void Shown(TextsDialog dialog, TextsDialog.TextInfo text)
        {
            bool ours = _page != null && ReferenceEquals(text, _page) && VandiConfig.ShowCompendiumPage.Value;
            if (!ours)
            {
                if (_root != null) _root.SetActive(false);
                return;
            }

            if (dialog == null || ReferenceEquals(dialog, _failedFor)) return;

            try
            {
                if (_root == null || !ReferenceEquals(_dialog, dialog) || _built != Signature())
                    Build(dialog);

                // Opens on the boss of the biome you are standing in, because "what does this biome
                // owe me" is the question the page is opened to ask. With none, the first boss.
                _selected = Default();

                MalmrLink.Opened();

                _root.transform.SetAsLastSibling();
                _root.SetActive(true);
                Fit();
                Draw();
                Settle(true);

                // Only once the page has drawn. Anything above that throws leaves the text page
                // showing, which is the fallback.
                dialog.m_textArea.text = "";
                _nextRefresh = Time.unscaledTime + RefreshSeconds;
            }
            catch (Exception e)
            {
                Abandon(dialog, e);
            }
        }

        /// <summary>Called every frame by the ticker on the page, so only while it is on screen.</summary>
        internal static void Tick()
        {
            if (_root == null || !_root.activeInHierarchy) return;

            try
            {
                // Vanilla's own compendium input is up and down for the list and the right stick for
                // scrolling, so left and right are free to walk the bosses.
                int step = 0;
                if (ZInput.IsExclusiveGamepadActive())
                {
                    if (ZInput.GetButtonDown("JoyDPadLeft")) step = -1;
                    else if (ZInput.GetButtonDown("JoyDPadRight")) step = 1;
                }

                // The right stick scrolls the boss the way it scrolls vanilla's text, which this
                // page has blanked and so left with nothing to scroll.
                if (ZInput.IsExclusiveGamepadActive() && _detailScroll != null)
                {
                    float stick = ZInput.GetJoyRightStickY();
                    if (Mathf.Abs(stick) > 0.1f)
                        _detailScroll.verticalNormalizedPosition = Mathf.Clamp01(
                            _detailScroll.verticalNormalizedPosition + stick * 2f * Time.unscaledDeltaTime);
                }

                if (step != 0) Walk(step);
                else if (Time.unscaledTime < _nextRefresh) return;

                _nextRefresh = Time.unscaledTime + RefreshSeconds;

                // BossBiomes is a string somebody can edit while the game runs, and a host's copy of
                // it lands after a world has loaded. A roster that changed is drawn again whole.
                if (_built != Signature())
                {
                    Build(_dialog);
                    _root.SetActive(true);
                }

                Fit();
                Draw();
                if (step != 0) Settle(true);
            }
            catch (Exception e)
            {
                Abandon(_dialog, e);
            }
        }

        private static void Select(string key)
        {
            if (_root == null) return;

            try
            {
                _selected = key;
                Draw();
                Settle(false);
            }
            catch (Exception e)
            {
                Abandon(_dialog, e);
            }
        }

        /// <summary>
        /// After a boss is chosen: the right column starts at its top, and the list is moved just
        /// far enough that the chosen row is in it. The gamepad walks to rows that may be below the
        /// fold, and the page opens on a boss that may be. Rows are placed by a layout pass, so a
        /// call from where none has run yet asks for one first.
        /// </summary>
        private static void Settle(bool layout)
        {
            if (_detailScroll != null) _detailScroll.verticalNormalizedPosition = 1f;
            if (_listScroll == null) return;

            if (layout) Canvas.ForceUpdateCanvases();

            foreach (BossRow row in Rows)
                if (row.Key == _selected) { Reveal(_listScroll, (RectTransform)row.Go.transform); break; }
        }

        private static void Reveal(ScrollRect scroll, RectTransform item)
        {
            RectTransform content = scroll.content;
            float view = scroll.viewport.rect.height;
            float room = content.rect.height - view;
            if (room <= 0f) return;

            Vector3[] corners = new Vector3[4];
            item.GetWorldCorners(corners);
            float top = content.InverseTransformPoint(corners[1]).y;
            float bottom = content.InverseTransformPoint(corners[0]).y;

            float offset = content.anchoredPosition.y;
            if (top > -offset) offset = -top;
            else if (bottom < -(offset + view)) offset = -bottom - view;

            content.anchoredPosition = new Vector2(content.anchoredPosition.x, Mathf.Clamp(offset, 0f, room));
        }

        private static void Walk(int step)
        {
            int at = -1;
            for (int i = 0; i < Rows.Count; i++)
                if (Rows[i].Key == _selected) at = i;

            at += step;
            if (at >= 0 && at < Rows.Count) _selected = Rows[at].Key;
        }

        private static string Default()
        {
            List<Bosses.Boss> roster = Bosses.Roster();
            if (roster.Count == 0) return null;

            Player player = Player.m_localPlayer;
            if (player != null)
            {
                Heightmap.Biome here = player.GetCurrentBiome();
                foreach (Bosses.Boss boss in roster)
                    if (boss.Biomes.Contains(here)) return boss.Key;
            }

            return roster[0].Key;
        }

        private static string Signature()
        {
            StringBuilder text = new StringBuilder();

            foreach (Bosses.Boss boss in Bosses.Roster())
            {
                text.Append(boss.Key);
                foreach (Heightmap.Biome biome in boss.Biomes) text.Append('/').Append((int)biome);
                text.Append(';');
            }

            return text.ToString();
        }

        /// <summary>
        /// Give up on the page for this compendium, put the text page back, and say why once. A page
        /// that threw once would throw again every second, and the text page is true.
        /// </summary>
        private static void Abandon(TextsDialog dialog, Exception e)
        {
            _failedFor = dialog;

            VandiPlugin.Log.LogError("The Vandi compendium page failed, so it is back to plain text "
                + "for this world. Nothing else is affected. " + e);

            try
            {
                if (_root != null) Object.Destroy(_root);
                _root = null;

                if (dialog != null && dialog.m_textArea != null && _page != null)
                    dialog.m_textArea.text = Localization.instance != null
                        ? Localization.instance.Localize(_page.m_text)
                        : _page.m_text;
            }
            catch (Exception again)
            {
                VandiPlugin.Log.LogError("Could not put the Vandi text page back either: " + again);
            }
        }

        // ------------------------------------------------------------------ the words ----

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static string Num(float value)
        {
            return value.ToString("0.##", Inv);
        }

        private static string Points(float value)
        {
            return "+" + Num(value) + "%";
        }

        private static string StarsText(int stars)
        {
            return stars <= 0 ? "no stars" : stars == 1 ? "1 star" : stars + " stars";
        }

        private static string TimesText(int kills)
        {
            return kills == 1 ? "1 time" : kills + " times";
        }

        private static string Ordinal(int n)
        {
            switch (n)
            {
                case 1: return "first";
                case 2: return "second";
                case 3: return "third";
                default: return n + "th";
            }
        }

        /// <summary>"a kill", "a one-star kill", "a 2-star kill": the kill that opens a metal, in stars.</summary>
        private static string KillPhrase(int needed)
        {
            int stars = needed - 1;
            return stars <= 0 ? "a kill" : stars == 1 ? "a one-star kill" : "a " + stars + "-star kill";
        }

        private static string BiomeName(Heightmap.Biome biome)
        {
            string token = "$biome_" + biome.ToString().ToLowerInvariant();

            Localization loc = Localization.instance;
            if (loc != null)
            {
                string name = loc.Localize(token);

                // A miss comes back as the token or as the word in brackets, and neither is a thing
                // a player should read.
                bool missed = string.IsNullOrEmpty(name) || name == token
                    || (name.StartsWith("[", StringComparison.Ordinal) && name.EndsWith("]", StringComparison.Ordinal));
                if (!missed) return name;
            }

            switch (biome)
            {
                case Heightmap.Biome.BlackForest: return "Black Forest";
                case Heightmap.Biome.AshLands: return "Ashlands";
                case Heightmap.Biome.DeepNorth: return "Deep North";
                default: return biome.ToString();
            }
        }

        /// <summary>"here" for a boss with one biome, "in the Swamp and the Ocean" for more.</summary>
        private static string Where(Bosses.Boss boss)
        {
            if (boss.Biomes.Count <= 1) return "here";

            List<string> names = new List<string>();
            foreach (Heightmap.Biome biome in boss.Biomes) names.Add("the " + BiomeName(biome));

            return "in " + Joined(names);
        }

        private static string Joined(List<string> items)
        {
            if (items.Count <= 1) return items.Count == 0 ? "" : items[0];

            return string.Join(", ", items.GetRange(0, items.Count - 1).ToArray()) + " and "
                + items[items.Count - 1];
        }

        private static string Intro(Bosses.Boss boss, int kills)
        {
            string name = Bosses.NameOf(boss.Key);
            float per = VandiConfig.StarChancePerKill.Value;
            float cap = VandiConfig.StarChanceCap.Value;

            string lead = kills <= 0
                ? "You have not killed " + name + " yet."
                : "You have killed " + name + " " + TimesText(kills) + ".";

            if (per <= 0f) return lead + " Kills add nothing to the star chance (StarChancePerKill is 0).";

            return lead + " Each kill adds " + Num(per) + "% starred creatures " + Where(boss)
                + (cap > 0f ? ", up to " + Num(cap) + "%." : ", with no cap.");
        }

        private static string NextKillLine(int kills)
        {
            float now = Stars.Earned(kills);
            float next = Stars.Earned(kills + 1);

            if (VandiConfig.StarChancePerKill.Value <= 0f) return "Kills add nothing here";
            if (next <= now) return "Already the most it gives";
            if (Stars.Earned(kills + 2) <= next) return "Next kill: " + Points(next) + ", the most it gives";

            return "Next kill: " + Points(next);
        }

        private static string NextSummonLine(int kills)
        {
            if (!VandiConfig.HarderBosses.Value) return "Bosses come back plain, HarderBosses is off";
            if (VandiConfig.BossStarCap.Value <= 0) return "Bosses come back plain, the star cap is 0";

            int now = Summon.StarsFor(kills);
            int next = Summon.StarsFor(kills + 1);

            return next <= now ? "The cap, so it stays " + now : "The next kill makes it " + StarsText(next);
        }

        /// <summary>
        /// The page in words: what shows in the list when the panel cannot be drawn, and what a
        /// player on a screen reader or a stripped-down UI still gets. One line per boss, built from
        /// the same functions as the panel.
        /// </summary>
        internal static string Summary()
        {
            StringBuilder text = new StringBuilder();
            text.Append("Your boss kills, as this world has recorded them. A kill counts when you made ")
                .Append("the offering at the altar.\n");

            foreach (Bosses.Boss boss in Bosses.Roster())
            {
                int kills = Kills.LocalCount(boss.Key);

                List<string> biomes = new List<string>();
                foreach (Heightmap.Biome biome in boss.Biomes) biomes.Add(BiomeName(biome));

                text.Append('\n').Append(Bosses.NameOf(boss.Key)).Append(" (").Append(string.Join(", ", biomes.ToArray()))
                    .Append("): killed ").Append(TimesText(kills)).Append(", starred creatures ")
                    .Append(Points(Stars.Earned(kills))).Append(", next summon at ")
                    .Append(StarsText(Summon.StarsFor(kills))).Append('.');
            }

            return text.ToString();
        }

        // ----------------------------------------------------------------- drawing -------

        private static void Draw()
        {
            List<Bosses.Boss> roster = Bosses.Roster();

            bool any = roster.Count > 0;
            Bosses.Boss chosen = null;
            foreach (Bosses.Boss boss in roster)
                if (boss.Key == _selected) chosen = boss;
            if (chosen == null && any) chosen = roster[0];
            if (chosen != null) _selected = chosen.Key;

            foreach (BossRow row in Rows)
            {
                int kills = Kills.LocalCount(row.Key);
                bool selected = row.Key == _selected;
                Bosses.Boss boss = Find(roster, row.Key);

                SetText(row.Biome, "<b>" + (boss == null ? "" : BiomeName(boss.Biomes[0])) + "</b>");
                SetColor(row.Biome, selected ? Orange : kills > 0 ? OpenText : LockedText);
                SetText(row.Boss, Bosses.NameOf(row.Key));
                SetText(row.Count, "<b>" + kills + "</b>");
                SetColor(row.Count, kills > 0 ? BodyText : Muted);
                SetColor(row.Edge, selected ? Orange : BoxEdge);
                SetColor(row.Face, selected ? RaisedFace : PanelFace);
            }

            bool off = !VandiConfig.Enabled.Value;
            SetActive(_off.gameObject, off && any);
            if (off)
                SetText(_off, Wrapped("Vandi is switched off in this game, so none of this is being applied."));

            if (!any)
            {
                SetText(_title, "<b>Vandi counts no bosses</b>");
                SetText(_sub, Wrapped("BossBiomes in the config is empty, so no kill is recorded and no "
                    + "biome is raised."));

                SetActive(_ladderGap, false);
                SetActive(_ladder, false);
                SetActive(_pairGap, false);
                SetActive(_pair, false);
                SetActive(_malmrGap, false);
                SetActive(_malmrBox, false);
                SetActive(_roadGap, false);
                SetActive(_roadBox, false);
                return;
            }

            string name = Bosses.NameOf(chosen.Key);
            int have = Kills.LocalCount(chosen.Key);

            SetText(_title, "<b>The " + BiomeName(chosen.Biomes[0]) + ", " + name + "</b>");
            SetText(_sub, Wrapped(Intro(chosen, have)));

            DrawLadder(have);

            SetActive(_pairGap, true);
            SetActive(_pair, true);

            SetText(_now.Head, "Starred creatures now");
            SetText(_now.Value, "<b>" + Points(Stars.Earned(have)) + "</b>");
            SetText(_now.Sub, Wrapped(NextKillLine(have)));

            SetText(_next.Head, "Next summon comes back at");
            SetText(_next.Value, "<b>" + StarsText(Summon.StarsFor(have)) + "</b>");
            SetText(_next.Sub, Wrapped(NextSummonLine(have)));

            List<MalmrLink.Unlock> unlocks = MalmrLink.Unlocks();
            DrawMalmr(chosen, name, have, unlocks);
            DrawRoad(roster, chosen, unlocks);
        }

        private static Bosses.Boss Find(List<Bosses.Boss> roster, string key)
        {
            foreach (Bosses.Boss boss in roster)
                if (boss.Key == key) return boss;

            return null;
        }

        private static void DrawLadder(int have)
        {
            float per = VandiConfig.StarChancePerKill.Value;
            float cap = VandiConfig.StarChanceCap.Value;

            int steps;
            if (per > 0f) steps = cap > 0f ? Mathf.CeilToInt(cap / per) : 5;
            else steps = VandiConfig.HarderBosses.Value ? VandiConfig.BossStarCap.Value : 0;
            steps = Mathf.Clamp(steps, 0, MostSteps);

            SetActive(_ladderGap, steps > 0);
            SetActive(_ladder, steps > 0);

            int current = Mathf.Min(have, steps);

            for (int i = 0; i < Rungs.Count; i++)
            {
                Rung step = Rungs[i];
                bool shown = i < steps;
                SetActive(step.Go, shown);
                if (!shown) continue;

                int n = i + 1;
                bool reached = n <= have;
                int stars = Summon.StarsFor(n);

                SetText(step.Kill, "Kill " + n);
                SetText(step.Value, "<b>" + Points(Stars.Earned(n)) + "</b>");
                SetText(step.Stars, stars <= 0 ? "boss plain" : "boss " + StarsText(stars));

                SetColor(step.Kill, reached ? BodyText : Muted);
                SetColor(step.Value, reached ? Orange : Muted);
                SetColor(step.Stars, Muted);
                SetColor(step.Edge, n == current ? Orange : BoxEdge);
                SetColor(step.Face, reached ? RaisedFace : PanelFace);
            }
        }

        private static void DrawMalmr(Bosses.Boss chosen, string name, int have, List<MalmrLink.Unlock> unlocks)
        {
            StringBuilder body = new StringBuilder();

            foreach (MalmrLink.Unlock unlock in unlocks)
            {
                if (unlock.Boss != chosen.Key) continue;

                bool killsMet = have >= unlock.KillsNeeded;
                bool levelMet = unlock.PickaxesNow >= unlock.Pickaxes;
                string kill = KillPhrase(unlock.KillsNeeded) + " of " + name + ", your "
                    + Ordinal(unlock.KillsNeeded);

                string title, sub, tag;
                if (killsMet && levelMet)
                {
                    tag = OpenTag;
                    title = unlock.Noun + " mining is open";
                    sub = "Needed Pickaxes " + unlock.Pickaxes + " and " + kill;
                }
                else if (killsMet)
                {
                    tag = OrangeTag;
                    title = unlock.Noun + " mining waits for Pickaxes " + unlock.Pickaxes;
                    sub = "You are at Pickaxes " + unlock.PickaxesNow + ", and " + kill + " is done";
                }
                else
                {
                    int left = unlock.KillsNeeded - have;
                    tag = OrangeTag;
                    title = left == 1
                        ? "The next kill opens " + unlock.Noun + " mining"
                        : left + " more kills open " + unlock.Noun + " mining";
                    sub = "Needs " + kill + (levelMet
                        ? ", and Pickaxes " + unlock.Pickaxes
                        : " and Pickaxes " + unlock.Pickaxes + ", you are at " + unlock.PickaxesNow);
                }

                if (body.Length > 0) body.Append('\n');
                body.Append("<size=").Append(Num(MalmrTitleSize)).Append("><b><color=").Append(tag).Append('>')
                    .Append(title).Append("</color></b></size>\n<color=").Append(MutedTag).Append('>')
                    .Append(sub).Append("</color>");
            }

            bool show = body.Length > 0;
            SetActive(_malmrGap, show);
            SetActive(_malmrBox, show);
            if (show) SetText(_malmrBody, Wrapped(body.ToString()));
        }

        private static void DrawRoad(List<Bosses.Boss> roster, Bosses.Boss chosen, List<MalmrLink.Unlock> unlocks)
        {
            StringBuilder body = new StringBuilder();

            foreach (Bosses.Boss other in roster)
            {
                if (other.Key == chosen.Key) continue;

                int have = Kills.LocalCount(other.Key);
                List<MalmrLink.Unlock> waiting = new List<MalmrLink.Unlock>();
                foreach (MalmrLink.Unlock unlock in unlocks)
                    if (unlock.Boss == other.Key && have < unlock.KillsNeeded) waiting.Add(unlock);

                if (waiting.Count == 0) continue;

                int needed = waiting[0].KillsNeeded;
                string name = Bosses.NameOf(other.Key);

                List<string> metals = new List<string>();
                foreach (MalmrLink.Unlock unlock in waiting)
                    metals.Add(have > 0 ? unlock.Plain + " (Pickaxes " + unlock.Pickaxes + ")" : unlock.Plain);

                string opens = "<color=" + OrangeTag + ">" + Joined(metals) + "</color>";

                string line;
                if (have > 0)
                {
                    int left = needed - have;
                    line = name + ": " + (left == 1 ? "one more kill opens " : left + " more kills open ") + opens;
                }
                else if (needed <= 1)
                {
                    line = name + ": never killed, the first kill opens " + opens;
                }
                else if (needed == 2)
                {
                    line = name + ": never killed, first kill opens nothing yet, the second opens " + opens;
                }
                else
                {
                    line = name + ": never killed, the first " + (needed - 1) + " kills open nothing yet, the "
                        + Ordinal(needed) + " opens " + opens;
                }

                if (body.Length > 0) body.Append('\n');
                body.Append(line);
            }

            bool show = body.Length > 0;
            SetActive(_roadGap, show);
            SetActive(_roadBox, show);
            if (show) SetText(_roadBody, Wrapped(body.ToString()));
        }

        /// <summary>
        /// A line that may wrap, with the mockup's line pitch and its text taken literally apart from
        /// the colour tags this page writes itself. Boss names come from the game's own language
        /// files and Malmr's words are cleaned of angle brackets, so none of it is markup it did not
        /// mean to be.
        /// </summary>
        private static string Wrapped(string text)
        {
            return "<line-height=" + LineHeight.ToString(Inv) + "em>" + text;
        }

        // Each of these writes only on a change. A TextMeshPro text or a RectTransform written with
        // its own value still dirties the layout, and this runs every second.

        private static void SetText(TMP_Text label, string text)
        {
            if (label.text != text) label.text = text;
        }

        private static void SetColor(Graphic graphic, Color color)
        {
            if (graphic.color != color) graphic.color = color;
        }

        private static void SetActive(GameObject go, bool active)
        {
            if (go.activeSelf != active) go.SetActive(active);
        }

        /// <summary>
        /// An edge the mockup draws one pixel wide, in page units: a whole number of screen pixels,
        /// never less than one. A 1-unit line at 0.934 screen pixels to a unit lands between two
        /// pixel centres and is not drawn at all, which took edges off Utangard's panel in Robbin's
        /// screenshot of 2026-09-29. N whole pixels covers exactly N centres wherever it lands.
        /// </summary>
        private static float Edge()
        {
            return Mathf.Max(1f, Mathf.Round(_pixelsPerUnit)) / _pixelsPerUnit;
        }

        /// <summary>A face inside a 1 px edge, which Fit redraws whenever the pixel size changes.</summary>
        private static void Rim(RectTransform face)
        {
            Rims.Add(face);
            Inset(face, Edge());
        }

        // ---------------------------------------------------------------- building -------

        private static void Build(TextsDialog dialog)
        {
            Forget();

            TMP_Text donor = dialog.m_textArea;
            if (donor == null) throw new InvalidOperationException("TextsDialog.m_textArea is not set.");

            string passed;
            RectTransform host = Host(dialog, donor, out passed);

            GameObject root = new GameObject("Vandi_Page", typeof(RectTransform));
            root.SetActive(false);

            RectTransform rootRect = (RectTransform)root.transform;
            rootRect.SetParent(host, false);

            // Laid out by nobody but itself. Its minimum height is the page's floor, which Fit keeps
            // at the host's: see Fit. ignoreLayout only keeps it out of a layout group above; the
            // root's own fitter still reads it.
            _floor = root.AddComponent<LayoutElement>();
            _floor.ignoreLayout = true;

            _root = root;
            _dialog = dialog;
            _host = host;

            // A 1 px edge in #5a4a36 around a #1f1a14 face. An Image with no sprite is a flat
            // rectangle, so the edge is the root's colour showing round a face inset by 1. Raycast
            // on, so a click on the page does not fall through to whatever is under it.
            Paint(rootRect, PanelEdge, true);
            RectTransform face = Child("Face", rootRect);
            face.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Rim(face);
            Paint(face, PanelFace, false);

            // As tall as what it holds, and never squeezed: see Fit.
            VerticalLayoutGroup frame = root.AddComponent<VerticalLayoutGroup>();
            frame.padding = new RectOffset(1, 1, 1, 1);
            Stack(frame, false);
            root.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // The label everything is cloned from, stripped and kept switched off.
            GameObject proto = Object.Instantiate(donor.gameObject, rootRect);
            proto.name = "Vandi_Label";
            Strip(proto);
            proto.SetActive(false);

            _proto = proto.GetComponent<TMP_Text>();
            if (_proto == null) throw new InvalidOperationException("The compendium's text carried no TextMeshPro component.");
            Prime(_proto);

            // Measured with the root switched on, since an inactive label has not loaded its font.
            root.SetActive(true);
            _naturalPerEm = Measure(rootRect);

            root.AddComponent<CompendiumPageTicker>();

            // The body: 14 in from the frame at the sides and 10 at the top, which is where the
            // mockup's list starts. It is exactly as tall as what it holds, with the floor's spare
            // height left under it.
            RectTransform body = Child("Body", rootRect);
            body.gameObject.AddComponent<LayoutElement>().flexibleHeight = 0f;
            VerticalLayoutGroup column = body.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(14, 14, 10, 14);
            Stack(column, false);

            _fitScale = -1f;
            Fit();

            RectTransform split = Child("Split", body);
            HorizontalLayoutGroup halves = split.gameObject.AddComponent<HorizontalLayoutGroup>();
            halves.spacing = ColumnGap;
            Across(halves, false);

            // Its height is Fit's: see there. Fixed, so each column's content is what scrolls, and
            // a content rect is sized by its own fitter and never squeezed to a viewport.
            _columns = split.gameObject.AddComponent<LayoutElement>();
            _columns.flexibleHeight = 0f;

            List<Bosses.Boss> roster = Bosses.Roster();
            int sounded = 0;

            // ---- the list on the left: a heading, then one row per boss, 5 apart.
            RectTransform list = Scroller("List", split, dialog, out _listScroll);
            LayoutElement listWidth = _listScroll.GetComponent<LayoutElement>();
            listWidth.minWidth = ListWidth;
            listWidth.preferredWidth = ListWidth;
            listWidth.flexibleWidth = 0f;
            VerticalLayoutGroup listColumn = list.gameObject.AddComponent<VerticalLayoutGroup>();
            Stack(listColumn, false);
            listColumn.spacing = 5f;

            TMP_Text head = Label(list, ListHeadSize, Muted, TextAlignmentOptions.TopLeft);
            head.text = "Boss, by biome (your kills)";

            foreach (Bosses.Boss boss in roster)
            {
                BossRow row = MakeRow(list, boss.Key);
                if (Sound(row.Go, dialog)) sounded++;
                Rows.Add(row);
            }

            // ---- the boss on the right.
            RectTransform detail = Scroller("Detail", split, dialog, out _detailScroll);
            LayoutElement share = _detailScroll.GetComponent<LayoutElement>();
            share.minWidth = 0f;
            share.preferredWidth = 0f;
            share.flexibleWidth = 1f;
            VerticalLayoutGroup right = detail.gameObject.AddComponent<VerticalLayoutGroup>();
            Stack(right, false);

            _title = Label(detail, TitleSize, Orange, TextAlignmentOptions.TopLeft);
            _sub = Label(detail, SubSize, Muted, TextAlignmentOptions.TopLeft);
            _off = Label(detail, SubSize, LockedText, TextAlignmentOptions.TopLeft);

            // The ladder, 10 under the line above it.
            _ladderGap = Gap(detail, 10f);
            RectTransform ladder = Child("Ladder", detail);
            HorizontalLayoutGroup rung = ladder.gameObject.AddComponent<HorizontalLayoutGroup>();
            rung.spacing = 6f;
            Across(rung, true);
            _ladder = ladder.gameObject;

            for (int i = 0; i < MostSteps; i++) Rungs.Add(MakeRung(ladder));

            // The two boxes, 10 below the ladder and 8 apart.
            _pairGap = Gap(detail, 10f);
            RectTransform pair = Child("Pair", detail);
            HorizontalLayoutGroup twin = pair.gameObject.AddComponent<HorizontalLayoutGroup>();
            twin.spacing = 8f;
            Across(twin, true);
            _pair = pair.gameObject;

            _now = MakePane(pair, "Now");
            _next = MakePane(pair, "Next");

            // Malmr, 8 below. Its heading is fixed, its body is what Draw writes.
            _malmrGap = Gap(detail, 8f);
            _malmrBox = MakeNote(detail, "Malmr", "Malmr, tied to this boss", Muted, SubSize, out _malmrBody);

            // The road ahead, 8 below that.
            _roadGap = Gap(detail, 8f);
            _roadBox = MakeNote(detail, "Road", "The road ahead", BodyText, RoadSize, out _roadBody);

            _built = Signature();

            _fitScale = -1f;
            Fit();

            Rect area = host.rect;
            VandiPlugin.Log.LogInfo("Compendium page built over '" + host.name + "'"
                + (passed.Length > 0 ? " (past " + passed + ", sized by what is in it)" : "") + ", "
                + Mathf.RoundToInt(area.width) + " x " + Mathf.RoundToInt(area.height)
                + " at scale " + _fitScale.ToString("0.###", Inv)
                + ", " + _pixelsPerUnit.ToString("0.###", Inv) + " screen pixels to a unit"
                + ", the compendium's own text at size " + donor.fontSize.ToString("0.#", Inv)
                + ", one line of text at " + _naturalPerEm.ToString("0.00", Inv) + " em, "
                + sounded + " of " + Rows.Count + " boss rows with vanilla's click sound.");
        }

        /// <summary>
        /// A column that scrolls: a clipping viewport inside a ScrollRect, a content rect hung from
        /// its top that is exactly as tall as what it holds, and the compendium's own scrollbar
        /// cloned beside it, shown only when there is something to scroll. The content rect is what
        /// is returned and what the caller fills.
        ///
        /// The content is sized by its own ContentSizeFitter and the viewport never touches its
        /// height, so this keeps the rule Fit is written round: no label is handed less height than
        /// its text needs, however short the viewport is.
        /// </summary>
        private static RectTransform Scroller(string name, RectTransform parent, TextsDialog dialog,
            out ScrollRect scroll)
        {
            RectTransform outer = Child(name + "_Scroll", parent);
            LayoutElement fill = outer.gameObject.AddComponent<LayoutElement>();
            fill.minHeight = 0f;
            fill.flexibleHeight = 1f;

            RectTransform viewport = Child("Viewport", outer);
            Inset(viewport, 0f);
            viewport.gameObject.AddComponent<RectMask2D>();

            // Invisible and catching the pointer, so the wheel over blank page still scrolls it.
            Paint(viewport, Color.clear, true);

            RectTransform content = Child(name, viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect rect = outer.gameObject.AddComponent<ScrollRect>();
            rect.horizontal = false;
            rect.vertical = true;
            rect.movementType = ScrollRect.MovementType.Clamped;
            rect.scrollSensitivity = dialog.m_leftScrollRect != null ? dialog.m_leftScrollRect.scrollSensitivity : 30f;
            rect.viewport = viewport;
            rect.content = content;

            Bar(rect, outer, dialog);

            scroll = rect;
            return content;
        }

        /// <summary>
        /// The compendium's right-hand scrollbar, copied, so the handle and track are vanilla's and
        /// follow its skin. Only its look is kept: every component that is not the bar or a graphic
        /// is removed, and the copy's own listeners with them, so it cannot drive the text it was
        /// cloned beside. Without a scrollbar to copy the column still scrolls by wheel and by drag.
        /// </summary>
        private static void Bar(ScrollRect scroll, RectTransform outer, TextsDialog dialog)
        {
            Scrollbar donor = dialog.m_rightScrollbar;
            if (donor == null) return;

            try
            {
                float width = Mathf.Clamp(((RectTransform)donor.transform).rect.width, 8f, 24f);

                GameObject go = Object.Instantiate(donor.gameObject, outer);
                go.name = "Scrollbar";

                foreach (MonoBehaviour part in go.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (part == null || part is Scrollbar || part is Graphic || part is Mask || part is RectMask2D)
                        continue;

                    Object.DestroyImmediate(part);
                }

                RectTransform rect = (RectTransform)go.transform;
                rect.anchorMin = new Vector2(1f, 0f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 0.5f);
                rect.sizeDelta = new Vector2(width, 0f);
                rect.anchoredPosition = Vector2.zero;
                rect.localScale = Vector3.one;

                Scrollbar bar = go.GetComponent<Scrollbar>();
                bar.onValueChanged = new Scrollbar.ScrollEvent();
                bar.direction = Scrollbar.Direction.BottomToTop;
                go.SetActive(true);

                scroll.verticalScrollbar = bar;
                scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
                scroll.verticalScrollbarSpacing = 4f;
            }
            catch (Exception e)
            {
                VandiPlugin.Log.LogWarning("Compendium page: could not copy the scrollbar, so the columns "
                    + "scroll without one. " + e.Message);
            }
        }

        /// <summary>
        /// One boss in the list: its biome and its name in a column, its count at the right, on a
        /// face with a 1 px edge that is orange while it is the one showing. 190 wide, and about 50
        /// tall with the text it holds.
        /// </summary>
        private static BossRow MakeRow(RectTransform list, string key)
        {
            RectTransform rect = Child("Boss_" + key, list);
            Image edge = Paint(rect, BoxEdge, true);

            // 5 inside the edge above and below, which with the two lines is the mockup's 50.
            HorizontalLayoutGroup line = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            line.padding = new RectOffset(11, 12, 5, 5);
            line.spacing = 8f;
            line.childAlignment = TextAnchor.MiddleLeft;
            line.childControlWidth = true;
            line.childControlHeight = true;
            line.childForceExpandWidth = false;
            line.childForceExpandHeight = false;

            RectTransform face = Child("Face", rect);
            face.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Rim(face);
            Image paint = Paint(face, PanelFace, false);

            RectTransform names = Child("Names", rect);
            LayoutElement grow = names.gameObject.AddComponent<LayoutElement>();
            grow.minWidth = 0f;
            grow.flexibleWidth = 1f;
            VerticalLayoutGroup stack = names.gameObject.AddComponent<VerticalLayoutGroup>();
            Stack(stack, false);

            BossRow row = new BossRow { Key = key, Go = rect.gameObject, Edge = edge, Face = paint };
            row.Biome = Label(names, RowBiomeSize, OpenText, TextAlignmentOptions.TopLeft);
            row.Boss = Label(names, RowBossSize, Muted, TextAlignmentOptions.TopLeft);

            row.Count = Label(rect, RowCountSize, BodyText, TextAlignmentOptions.MidlineRight);
            row.Count.textWrappingMode = TextWrappingModes.NoWrap;

            Button button = rect.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = edge;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            string chosen = key;
            button.onClick.AddListener(() => Select(chosen));

            return row;
        }

        /// <summary>One rung of the ladder: "Kill 3", what it adds, and what the boss then comes back at.</summary>
        private static Rung MakeRung(RectTransform ladder)
        {
            RectTransform rect = Child("Step", ladder);
            Image edge = Paint(rect, BoxEdge, false);

            LayoutElement share = rect.gameObject.AddComponent<LayoutElement>();
            share.minWidth = 0f;
            share.preferredWidth = 0f;
            share.flexibleWidth = 1f;

            VerticalLayoutGroup inner = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            inner.padding = new RectOffset(3, 3, 4, 4);
            Stack(inner, false);

            RectTransform face = Child("Face", rect);
            face.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Rim(face);
            Image paint = Paint(face, PanelFace, false);

            Rung step = new Rung { Go = rect.gameObject, Edge = edge, Face = paint };
            step.Kill = Label(rect, CellSize, BodyText, TextAlignmentOptions.Top);
            step.Value = Label(rect, CellValueSize, Orange, TextAlignmentOptions.Top);
            step.Stars = Label(rect, CellSize, Muted, TextAlignmentOptions.Top);

            step.Go.SetActive(false);
            return step;
        }

        /// <summary>A box of three lines: a heading, a big figure, and a line under it.</summary>
        private static Pane MakePane(RectTransform pair, string name)
        {
            RectTransform rect = Child(name, pair);
            Paint(rect, BoxEdge, false);

            LayoutElement share = rect.gameObject.AddComponent<LayoutElement>();
            share.minWidth = 0f;
            share.preferredWidth = 0f;
            share.flexibleWidth = 1f;

            VerticalLayoutGroup stack = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.padding = new RectOffset(12, 12, 4, 4);
            Stack(stack, false);

            RectTransform face = Child("Face", rect);
            face.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Rim(face);
            Paint(face, PanelFace, false);

            Pane pane = new Pane();
            pane.Head = Label(rect, BoxSize, Muted, TextAlignmentOptions.TopLeft);
            pane.Value = Label(rect, BoxValueSize, Orange, TextAlignmentOptions.TopLeft);
            pane.Sub = Label(rect, BoxSize, Muted, TextAlignmentOptions.TopLeft);
            return pane;
        }

        /// <summary>A full-width box with a fixed heading and a body Draw fills in.</summary>
        private static GameObject MakeNote(RectTransform detail, string name, string heading, Color bodyColor,
            float bodySize, out TMP_Text body)
        {
            RectTransform rect = Child(name, detail);
            Paint(rect, BoxEdge, false);

            VerticalLayoutGroup stack = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.padding = new RectOffset(12, 12, 4, 4);
            Stack(stack, false);

            RectTransform face = Child("Face", rect);
            face.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Rim(face);
            Paint(face, PanelFace, false);

            TMP_Text label = Label(rect, SubSize, Muted, TextAlignmentOptions.TopLeft);
            label.text = heading;

            body = Label(rect, bodySize, bodyColor, TextAlignmentOptions.TopLeft);

            rect.gameObject.SetActive(false);
            return rect.gameObject;
        }

        // ------------------------------------------------------------------ fitting ------

        /// <summary>
        /// Keeps the page over the area the compendium's text would fill: 14 in from the left and
        /// right, from the top, and at least as tall as it less the same 14.
        ///
        /// <b>The height is the page's own.</b> A ContentSizeFitter on the root makes it as tall as
        /// what it holds, and this only sets the floor under that, on the root's own LayoutElement,
        /// so the dark page still fills the text area when there is less to say. A column squeezed
        /// shorter than its content hands each child its MINIMUM height, which for a TextMeshPro
        /// label is 0, so a page stretched to a shrunken host would draw every text at no height on
        /// top of its neighbour. Grown from its content, no label can get less than its text needs,
        /// whatever it is drawn over.
        ///
        /// At a scale of 1 or more it is drawn as it is. Below 1 the compendium sits under a parent
        /// that shrinks it, and the page is drawn at 1/scale instead, with its width and floor shrunk
        /// by the same factor so it still covers the same area on screen. A compendium scaled up is
        /// left alone: its text is only bigger, and bigger is readable.
        /// </summary>
        private static void Fit()
        {
            if (_root == null || _host == null || _floor == null) return;

            float scale = HostScale(_host);
            Vector2 size = _host.rect.size;
            float pixels = PixelsPerUnit(_host);
            if (Mathf.Approximately(scale, _fitScale) && size == _fitSize
                && Mathf.Approximately(pixels, _fitPixels)) return;

            _fitScale = scale;
            _fitSize = size;
            _fitPixels = pixels;

            float drawn = scale >= 0.99f ? 1f : scale;
            float grow = 1f / drawn;

            RectTransform rect = (RectTransform)_root.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.localScale = new Vector3(grow, grow, 1f);
            rect.anchoredPosition = new Vector2(EdgeMargin, 0f);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(0f, size.x - 2f * EdgeMargin) * drawn);

            _floor.minHeight = Mathf.Max(0f, size.y - EdgeMargin) * drawn;

            // The two columns are as tall as the page less what is round them, and each scrolls
            // inside that. Not as tall as what they hold: a right column of 660 to 760 units does
            // not fit 617 with Malmr loaded and an early boss chosen, and a page taller than the
            // host is clipped with nothing to scroll it.
            if (_columns != null)
            {
                float height = Mathf.Max(LeastColumns, _floor.minHeight - ColumnsPadding);
                _columns.minHeight = height;
                _columns.preferredHeight = height;
            }

            _pixelsPerUnit = pixels * grow;
            foreach (RectTransform rim in Rims) Inset(rim, Edge());
        }

        private static float PixelsPerUnit(RectTransform rect)
        {
            Canvas canvas = rect.GetComponentInParent<Canvas>();
            if (canvas == null) return 1f;

            Canvas top = canvas.rootCanvas;
            float canvasScale = top.transform.lossyScale.x;
            if (canvasScale <= 0f || top.scaleFactor <= 0f) return 1f;

            float perUnit = top.scaleFactor * rect.lossyScale.x / canvasScale;
            return perUnit > 0.1f && perUnit < 20f ? perUnit : 1f;
        }

        private static float HostScale(RectTransform host)
        {
            Canvas canvas = host.GetComponentInParent<Canvas>();
            if (canvas == null) return 1f;

            float canvasScale = canvas.rootCanvas.transform.lossyScale.x;
            if (canvasScale <= 0f) return 1f;

            float scale = host.lossyScale.x / canvasScale;
            return scale > 0.2f && scale < 5f ? scale : 1f;
        }

        /// <summary>
        /// The click and hover sounds of the compendium's own list, copied onto a boss row off the
        /// list's row prefab rather than named, so they are whatever vanilla plays there. A method of
        /// its own inside a try, because ButtonSfx lives in assembly_guiutils: a rename must cost the
        /// sound and never the page.
        /// </summary>
        private static bool Sound(GameObject button, TextsDialog dialog)
        {
            try
            {
                return CopySound(button, dialog);
            }
            catch (Exception e)
            {
                VandiPlugin.Log.LogWarning("Compendium page: the boss rows stay silent. " + e.Message);
                return false;
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool CopySound(GameObject button, TextsDialog dialog)
        {
            if (dialog.m_elementPrefab == null) return false;

            ButtonSfx donor = dialog.m_elementPrefab.GetComponentInChildren<ButtonSfx>(true);
            if (donor == null) return false;

            // Added after the Button, because ButtonSfx looks for it once, in Awake, and the page is
            // switched on by now so Awake runs inside this AddComponent.
            ButtonSfx sfx = button.AddComponent<ButtonSfx>();
            sfx.m_sfxPrefab = donor.m_sfxPrefab;
            sfx.m_sfxPrefabVibrationOnly = donor.m_sfxPrefabVibrationOnly;
            sfx.m_selectSfxPrefab = donor.m_selectSfxPrefab;
            sfx.m_selectSfxPrefabVibrationOnly = donor.m_selectSfxPrefabVibrationOnly;
            sfx.m_enterSfxPrefab = donor.m_enterSfxPrefab;
            sfx.m_enterSfxPrefabVibrationOnly = donor.m_enterSfxPrefabVibrationOnly;
            return true;
        }

        /// <summary>
        /// Where the page goes: the compendium's ScrollArea, which is the nearest rect above the
        /// text whose height is its own rather than the text's.
        ///
        /// <b>The text's own parent is not it.</b> That is 'Content', sized by the text inside it, and
        /// a page built on it shrinks the moment the text is blanked. So this climbs past every rect
        /// whose height follows what is in it: one a ContentSizeFitter sizes, one a scroll view moves,
        /// one a layout group above it sizes. It stops at the first rect that clips what is drawn in
        /// it, since that is the area the text is seen in, and it never climbs to the dialog itself,
        /// nor to a rect that would bring the page's topic, the text's scrollbar or the list under
        /// the page. The hierarchy is asset data no decompile shows, so the rects it passed are named
        /// on the build log line. If the climb ends anywhere but a rect called ScrollArea, one by
        /// that name between the text and the dialog is taken instead, since 840 x 641 on ScrollArea
        /// is what the compendium was measured to be.
        /// </summary>
        private static RectTransform Host(TextsDialog dialog, TMP_Text donor, out string passed)
        {
            RectTransform host = donor.transform.parent as RectTransform;
            if (host == null) throw new InvalidOperationException("The compendium's text has no parent to draw over.");

            passed = "";
            while (!Clips(host) && FollowsContent(host))
            {
                RectTransform above = host.parent as RectTransform;
                if (above == null || above == dialog.transform || !above.IsChildOf(dialog.transform)) break;
                if (Brings(host, above, dialog.m_textAreaTopic) || Brings(host, above, dialog.m_rightScrollbar)
                    || Brings(host, above, dialog.m_listRoot)) break;

                passed += (passed.Length > 0 ? ", '" : "'") + host.name + "'";
                host = above;
            }

            if (host.name != "ScrollArea")
            {
                RectTransform from = host;
                for (Transform up = donor.transform.parent; up != null && up != dialog.transform; up = up.parent)
                {
                    RectTransform rect = up as RectTransform;
                    if (rect == null || rect.name != "ScrollArea") continue;
                    if (Brings(from, rect, dialog.m_textAreaTopic) || Brings(from, rect, dialog.m_rightScrollbar)
                        || Brings(from, rect, dialog.m_listRoot)) break;

                    passed += (passed.Length > 0 ? ", '" : "'") + host.name + "'";
                    host = rect;
                    break;
                }
            }

            return host;
        }

        private static bool FollowsContent(RectTransform rect)
        {
            foreach (ContentSizeFitter fitter in rect.GetComponents<ContentSizeFitter>())
                if (fitter.enabled && fitter.verticalFit != ContentSizeFitter.FitMode.Unconstrained) return true;

            foreach (ScrollRect scroll in rect.GetComponentsInParent<ScrollRect>(true))
                if (scroll.content == rect) return true;

            Transform parent = rect.parent;
            if (parent == null) return false;

            foreach (HorizontalOrVerticalLayoutGroup group in parent.GetComponents<HorizontalOrVerticalLayoutGroup>())
                if (group.enabled && group.childControlHeight) return true;

            return false;
        }

        private static bool Clips(RectTransform rect)
        {
            RectMask2D rectMask = rect.GetComponent<RectMask2D>();
            if (rectMask != null && rectMask.enabled) return true;

            Mask mask = rect.GetComponent<Mask>();
            return mask != null && mask.enabled;
        }

        private static bool Brings(Transform from, Transform to, Component part)
        {
            return part != null && part.transform.IsChildOf(to) && !part.transform.IsChildOf(from);
        }

        /// <summary>
        /// Everything off the clone except the text itself. A copy of the compendium's text would
        /// otherwise bring whatever sizes and localises the original, and those would go on doing it
        /// to our labels. DestroyImmediate, since the clone is used in the same frame, and twice over,
        /// because a component another one requires cannot go first.
        /// </summary>
        private static void Strip(GameObject go)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (Component component in go.GetComponents<Component>())
                {
                    if (component == null) continue;
                    if (component is RectTransform || component is CanvasRenderer || component is TMP_Text) continue;

                    Object.DestroyImmediate(component);
                }
            }

            for (int i = go.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
        }

        /// <summary>The settings every label shares, whatever the compendium's text used.</summary>
        private static void Prime(TMP_Text label)
        {
            label.text = "";
            label.enableAutoSizing = false;
            label.richText = true;
            label.raycastTarget = false;
            label.overrideColorTags = false;
            label.enableVertexGradient = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;
            label.margin = Vector4.zero;
            label.lineSpacing = 0f;
            label.paragraphSpacing = 0f;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.color = BodyText;
        }

        /// <summary>
        /// One line of the compendium's font at size 1, as TextMeshPro lays it out. Taken from a
        /// throwaway label, because asking for preferred values rewrites a label's working text. A
        /// result that cannot be a line height falls back to 1.2, which is a typical one.
        /// </summary>
        private static float Measure(RectTransform parent)
        {
            GameObject probe = Object.Instantiate(_proto.gameObject, parent);
            try
            {
                probe.SetActive(true);
                TMP_Text text = probe.GetComponent<TMP_Text>();
                text.fontSize = 100f;
                text.textWrappingMode = TextWrappingModes.NoWrap;

                float perEm = text.GetPreferredValues("Hg").y / 100f;
                if (perEm > 0.6f && perEm < 2.5f) return perEm;

                VandiPlugin.Log.LogWarning("Compendium page: measured a line of " + perEm
                    + " em, which cannot be right; spacing the text as if it were 1.2.");
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }

            return 1.2f;
        }

        /// <summary>Half the difference between the mockup's line and the font's own, at this size.</summary>
        private static float Lead(float size)
        {
            return Mathf.Max(0f, (LineHeight - _naturalPerEm) * size * 0.5f);
        }

        private static TMP_Text Label(Transform parent, float size, Color color, TextAlignmentOptions align)
        {
            GameObject go = Object.Instantiate(_proto.gameObject, parent);
            go.name = "Text";
            go.SetActive(true);

            TMP_Text label = go.GetComponent<TMP_Text>();
            label.fontSize = size;
            label.color = color;
            label.alignment = align;

            float lead = Lead(size);
            label.margin = new Vector4(0f, lead, 0f, lead);
            return label;
        }

        private static RectTransform Child(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static Image Paint(RectTransform rect, Color color, bool raycast)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        private static void Inset(RectTransform rect, float by)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(by, by);
            rect.offsetMax = new Vector2(-by, -by);
        }

        private static GameObject Gap(Transform parent, float height)
        {
            RectTransform rect = Child("Gap", parent);
            LayoutElement space = rect.gameObject.AddComponent<LayoutElement>();
            space.minHeight = height;
            space.preferredHeight = height;
            return rect.gameObject;
        }

        /// <summary>A column that gives its children the full width and their text's height, top down.</summary>
        private static void Stack(VerticalLayoutGroup group, bool fillHeight)
        {
            group.spacing = 0f;
            group.childAlignment = TextAnchor.UpperLeft;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = fillHeight;
        }

        /// <summary>
        /// A row. Even, its children share its width and all take the tallest one's height, which is
        /// the ladder and the pair of boxes. Not even, each child is as wide as it asks and the one
        /// marked flexible takes what is left, which is the list and the boss beside it.
        /// </summary>
        private static void Across(HorizontalLayoutGroup group, bool even)
        {
            group.childAlignment = TextAnchor.UpperLeft;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = even;
            group.childForceExpandHeight = even;
        }

        /// <summary>Drop every reference into a previous compendium, which a new world has destroyed.</summary>
        private static void Forget()
        {
            if (_root != null) Object.Destroy(_root);

            _root = null;
            _dialog = null;
            _host = null;
            _floor = null;
            _columns = null;
            _listScroll = null;
            _detailScroll = null;
            _fitScale = -1f;
            _fitPixels = -1f;
            _built = null;
            Rims.Clear();
            _proto = null;
            Rows.Clear();
            Rungs.Clear();
            _title = null;
            _sub = null;
            _off = null;
            _ladder = null;
            _ladderGap = null;
            _pair = null;
            _pairGap = null;
            _now = null;
            _next = null;
            _malmrBox = null;
            _malmrGap = null;
            _malmrBody = null;
            _roadBox = null;
            _roadGap = null;
            _roadBody = null;
        }

        private static Color Rgb(int hex)
        {
            return new Color(((hex >> 16) & 0xff) / 255f, ((hex >> 8) & 0xff) / 255f, (hex & 0xff) / 255f, 1f);
        }
    }

    /// <summary>
    /// Keeps the page's numbers fresh while it is on screen. A component on the page itself, so it
    /// runs exactly while the page is showing: Unity stops calling Update the moment the page, or the
    /// compendium around it, is switched off.
    /// </summary>
    internal sealed class CompendiumPageTicker : MonoBehaviour
    {
        private void Update()
        {
            CompendiumPage.Tick();
        }
    }
}
