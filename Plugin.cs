using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;
using UnityEngine;
using UnityEngine.UI;

namespace ProgressionSkills
{
    // Stored skill levels are never lowered, the cap is only applied where levels are read.
    // That way the character file always keeps the real levels.
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModGuid = "aj.progressionskills";
        public const string ModName = "ProgressionSkills";
        public const string ModVersion = PluginVersion.Value;

        private const string DefaultCombatSkills =
            "Swords,Knives,Clubs,Polearms,Spears,Axes,Bows,Crossbows,Unarmed,Blocking,ElementalMagic,BloodMagic";

        // queen and fader aren't in the GlobalKeys enum, so keys are checked by name
        private static readonly (string Key, int Level)[] DefaultTiers =
        {
            ("defeated_eikthyr", 20),
            ("defeated_gdking", 35),
            ("defeated_bonemass", 50),
            ("defeated_dragon", 65),
            ("defeated_goblinking", 80),
            ("defeated_queen", 90),
            ("defeated_fader", 100),
        };

        private static readonly ConfigSync configSync = new ConfigSync(ModGuid)
        {
            DisplayName = ModName,
            CurrentVersion = ModVersion,
            MinimumRequiredVersion = ModVersion,
            ModRequired = true,
        };

        // not PatchAll(assembly): ServerSync is merged in and would get patched twice
        private static readonly Type[] Patches =
        {
            typeof(ModifySkillLevelPatch),
            typeof(RaiseSkillPatch),
            typeof(OnDeathPatch),
            typeof(SkillsDialogPatch),
            typeof(InitTerminalPatch),
            typeof(WorldKeyGuardPatch),
        };

        private static ConfigEntry<bool> lockConfig = null!;
        private static ConfigEntry<string> combatSkillList = null!;
        private static ConfigEntry<int> baseLevel = null!;
        private static readonly List<(string Key, ConfigEntry<int> Level)> tiers = new List<(string, ConfigEntry<int>)>();
        private static ConfigEntry<bool> showCapMarker = null!;

        private static HashSet<Skills.SkillType> combatSkills = new HashSet<Skills.SkillType>();

        private static bool cachedActive;
        private static int cachedCap;
        private static int cachedFrame = -1;

        private void Awake()
        {
            lockConfig = config("1 - General", "Lock Configuration", true,
                "If on, the configuration is locked and can be changed by server admins only.");
            configSync.AddLockingConfigEntry(lockConfig);

            combatSkillList = config("2 - Skills", "CombatSkills", DefaultCombatSkills,
                "Comma-separated Skills.SkillType names whose level is capped by defeated bosses. Other skills are not capped.");

            baseLevel = config("3 - Levels", "BaseLevel", 10,
                new ConfigDescription("Level cap for combat skills before any boss is defeated.", new AcceptableValueRange<int>(0, 100)));
            foreach ((string key, int level) in DefaultTiers)
            {
                tiers.Add((key, config("3 - Levels", key, level,
                    new ConfigDescription($"Level cap for combat skills once the '{key}' global key is set.", new AcceptableValueRange<int>(0, 100)))));
            }

            showCapMarker = config("4 - Interface", "ShowCapMarker", true,
                "If on, the skills screen marks the current cap on each capped skill's bar.", synchronizedSetting: false);

            ParseCombatSkills();
            combatSkillList.SettingChanged += (_, _) => ParseCombatSkills();

            Harmony harmony = new Harmony(ModGuid);
            foreach (Type patch in Patches)
            {
                harmony.PatchAll(patch);
            }
        }

        private void ParseCombatSkills()
        {
            HashSet<Skills.SkillType> parsed = new HashSet<Skills.SkillType>();
            foreach (string name in combatSkillList.Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0))
            {
                // TryParse also accepts plain numbers
                if (Enum.TryParse(name, true, out Skills.SkillType type) && Enum.IsDefined(typeof(Skills.SkillType), type) &&
                    type != Skills.SkillType.None && type != Skills.SkillType.All)
                {
                    parsed.Add(type);
                }
                else
                {
                    Logger.LogWarning($"Unknown skill '{name}' in CombatSkills; ignoring.");
                }
            }
            combatSkills = parsed;
        }

        internal static bool ShowCapMarker => showCapMarker.Value;

        internal static bool Active
        {
            get
            {
                Refresh();
                return cachedActive;
            }
        }

        internal static int Cap
        {
            get
            {
                Refresh();
                return cachedCap;
            }
        }

        // set lookup first, run/swim/sneak hit this every frame
        internal static bool IsCapped(Skills.SkillType type) => combatSkills.Contains(type) && Active;

        // levels get read a lot, recompute once per frame
        private static void Refresh()
        {
            if (cachedFrame == Time.frameCount)
            {
                return;
            }
            cachedFrame = Time.frameCount;
            ZoneSystem zoneSystem = ZoneSystem.instance;
            cachedActive = zoneSystem != null && zoneSystem.m_globalKeysValues.ContainsKey(WorldToggle.WorldKey);
            cachedCap = ComputeCap(zoneSystem);
        }

        private static int ComputeCap(ZoneSystem zoneSystem)
        {
            int best = 0;
            if (zoneSystem != null)
            {
                foreach ((string key, ConfigEntry<int> level) in tiers)
                {
                    // GetGlobalKey(string) does a ToLower on every call
                    if (zoneSystem.m_globalKeysValues.ContainsKey(key))
                    {
                        best = Math.Max(best, level.Value);
                    }
                }
            }
            return Mathf.Clamp(Math.Max(baseLevel.Value, best), 0, 100);
        }

        internal static float GetStoredLevel(Skills skills, Skills.SkillType type) =>
            skills.m_skillData.TryGetValue(type, out Skills.Skill skill) ? skill.m_level : 0f;

        // only used around SkillsDialog.Setup, always paired with Restore
        internal static List<(Skills.Skill Skill, float Level, float Accumulator)> LowerToCap(Skills skills)
        {
            List<(Skills.Skill, float, float)> lowered = new List<(Skills.Skill, float, float)>();
            int cap = Cap;
            foreach (Skills.Skill skill in skills.m_skillData.Values)
            {
                if (skill.m_info != null && IsCapped(skill.m_info.m_skill) && skill.m_level > cap)
                {
                    lowered.Add((skill, skill.m_level, skill.m_accumulator));
                    skill.m_level = cap;
                    skill.m_accumulator = 0f;
                }
            }
            return lowered;
        }

        internal static void Restore(List<(Skills.Skill Skill, float Level, float Accumulator)> lowered)
        {
            foreach ((Skills.Skill skill, float level, float accumulator) in lowered)
            {
                skill.m_level = level;
                skill.m_accumulator = accumulator;
            }
        }

        private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
        {
            ConfigEntry<T> entry = Config.Bind(group, name, value, description);
            SyncedConfigEntry<T> syncedEntry = configSync.AddConfigEntry(entry);
            syncedEntry.SynchronizedConfig = synchronizedSetting;
            return entry;
        }

        private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true) =>
            config(group, name, value, new ConfigDescription(description), synchronizedSetting);
    }

    // Called from Skills.GetSkillLevel with the base level, before status effects add their bonuses.
    // Capping here lets gear/set bonuses go over the cap.
    [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifySkillLevel))]
    internal static class ModifySkillLevelPatch
    {
        private static void Prefix(SEMan __instance, Skills.SkillType skill, ref float level)
        {
            if (__instance.m_character is Player && Plugin.IsCapped(skill))
            {
                level = Mathf.Min(level, Plugin.Cap);
            }
        }
    }

    [HarmonyPatch(typeof(Skills), nameof(Skills.RaiseSkill))]
    internal static class RaiseSkillPatch
    {
        private static bool Prefix(Skills __instance, Skills.SkillType skillType, out bool __state)
        {
            if (!Plugin.IsCapped(skillType))
            {
                __state = false;
                return true;
            }
            __state = Plugin.GetStoredLevel(__instance, skillType) < Plugin.Cap;
            return __state;
        }

        // fractional levels (death drain from other worlds) can level up past the cap
        private static void Postfix(Skills __instance, Skills.SkillType skillType, bool __state)
        {
            if (__state && __instance.m_skillData.TryGetValue(skillType, out Skills.Skill skill) && skill.m_level > Plugin.Cap)
            {
                skill.m_level = Plugin.Cap;
            }
        }
    }

    [HarmonyPatch(typeof(Skills), nameof(Skills.OnDeath))]
    internal static class OnDeathPatch
    {
        private static bool Prefix() => !Plugin.Active;
    }

    // On/off is a global key, so it's saved with the world and synced like the boss keys.
    // It's not a world modifier, so it doesn't count as a cheat for achievements.
    internal static class WorldToggle
    {
        internal const string WorldKey = "progressionskills";

        internal static void RegisterCommand() =>
            new Terminal.ConsoleCommand(WorldKey, "[on|off] - turn ProgressionSkills on or off for this world (host or admin)", Run,
                isNetwork: true, optionsFetcher: () => new List<string> { "on", "off" });

        private static void Run(Terminal.ConsoleEventArgs args)
        {
            ZoneSystem zoneSystem = ZoneSystem.instance;
            if (zoneSystem == null || ZNet.instance == null)
            {
                args.Context.AddString("Load a world first.");
                return;
            }
            string action = args.Length >= 2 ? args[1].ToLower() : "";
            if (action != "on" && action != "off")
            {
                args.Context.AddString(Plugin.Active
                    ? $"ProgressionSkills is on in this world. Combat skill cap: {Plugin.Cap}."
                    : "ProgressionSkills is off in this world.");
                return;
            }
            if (!ZNet.instance.LocalPlayerIsAdminOrHost())
            {
                args.Context.AddString("Only the host or an admin can change this.");
                return;
            }
            if (action == "on")
            {
                zoneSystem.SetGlobalKey(WorldKey);
            }
            else
            {
                zoneSystem.RemoveGlobalKey(WorldKey);
            }
            args.Context.AddString($"Turning ProgressionSkills {action} for this world.");
        }

        internal static bool IsWorldKey(string name) => ZoneSystem.GetKeyValue(name, out _, out _) == WorldKey;

        // same check as vanilla admin RPCs, the host has no peer
        internal static bool IsAdminOrHost(long sender)
        {
            if (sender == ZNet.GetUID())
            {
                return true;
            }
            ZNetPeer peer = ZNet.instance.GetPeer(sender);
            if (peer != null && ZNet.instance.IsAdmin(peer.m_socket.GetHostName()))
            {
                return true;
            }
            if (peer != null)
            {
                ZNet.instance.RemotePrint(peer.m_rpc, "You are not admin");
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
    internal static class InitTerminalPatch
    {
        private static void Postfix() => WorldToggle.RegisterCommand();
    }

    // vanilla lets any client set global keys, so check admin on the server too
    [HarmonyPatch]
    internal static class WorldKeyGuardPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ZoneSystem), nameof(ZoneSystem.RPC_SetGlobalKey));
            yield return AccessTools.Method(typeof(ZoneSystem), nameof(ZoneSystem.RPC_RemoveGlobalKey));
        }

        private static bool Prefix(long sender, string name) => !WorldToggle.IsWorldKey(name) || WorldToggle.IsAdminOrHost(sender);
    }

    // Setup reads m_level directly, so show capped values just for this call.
    // Finalizer so the real levels are restored even if Setup throws.
    [HarmonyPatch(typeof(SkillsDialog), nameof(SkillsDialog.Setup))]
    internal static class SkillsDialogPatch
    {
        private static void Prefix(Player player, out List<(Skills.Skill Skill, float Level, float Accumulator)> __state) =>
            __state = Plugin.LowerToCap(player.GetSkills());

        private static void Postfix(SkillsDialog __instance, Player player) => CapMarker.Update(__instance, player);

        private static void Finalizer(List<(Skills.Skill Skill, float Level, float Accumulator)> __state)
        {
            if (__state != null)
            {
                Plugin.Restore(__state);
            }
        }
    }

    internal static class CapMarker
    {
        private const string MarkerName = "ProgressionSkills_CapMarker";
        private const float MarkerWidth = 2f;
        private static readonly Color MarkerColor = new Color(1f, 0.6f, 0.1f);

        internal static void Update(SkillsDialog dialog, Player player)
        {
            // same order Setup uses for m_elements
            List<Skills.Skill> skills = player.GetSkills().GetSkillList();
            int cap = Plugin.Cap;
            for (int i = 0; i < skills.Count && i < dialog.m_elements.Count; i++)
            {
                Transform levelBar = Utils.FindChild(dialog.m_elements[i].transform, "levelbar");
                GuiBar bar = levelBar != null ? levelBar.GetComponent<GuiBar>() : null;
                if (bar == null || bar.m_bar == null)
                {
                    continue;
                }
                bool show = Plugin.ShowCapMarker && skills[i].m_info != null && Plugin.IsCapped(skills[i].m_info.m_skill) && cap < 100;
                Transform existing = bar.m_bar.parent.Find(MarkerName);
                if (existing == null && !show)
                {
                    continue;
                }
                RectTransform marker = existing != null ? (RectTransform)existing : Create(bar.m_bar.parent);
                marker.gameObject.SetActive(show);
                if (show)
                {
                    Place(marker, bar, cap);
                }
            }
        }

        private static RectTransform Create(Transform parent)
        {
            GameObject go = new GameObject(MarkerName, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            Image image = go.GetComponent<Image>();
            image.color = MarkerColor;
            image.raycastTarget = false;
            RectTransform marker = (RectTransform)go.transform;
            marker.SetParent(parent, false);
            marker.anchorMin = marker.anchorMax = marker.pivot = new Vector2(0.5f, 0.5f);
            return marker;
        }

        private static void Place(RectTransform marker, GuiBar bar, int cap)
        {
            RectTransform fill = bar.m_bar;
            float left = fill.localPosition.x - fill.pivot.x * bar.m_width;
            float centerY = fill.localPosition.y + fill.rect.center.y;
            marker.sizeDelta = new Vector2(MarkerWidth, fill.rect.height);
            marker.localPosition = new Vector3(left + bar.m_width * cap / 100f, centerY, 0f);
            marker.SetAsLastSibling();
        }
    }
}
