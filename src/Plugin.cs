using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;

namespace ContainerQuickBind
{
    /// <summary>
    /// 让安全箱 / 弹挂 / 口袋 / 背包里的消耗品可以拖到原生快捷栏绑定，
    /// 绑定后按原生 4~0 快捷键直接使用。
    ///
    /// 实现只有两个 Harmony Postfix，都作用在 InventoryController 上：
    ///   IsAtBindablePlace  —— 放开「这个物品能不能绑」，决定拖拽目标是否接受、绑定是否成功
    ///   IsAtReachablePlace —— 放开「这个物品现在够不够得着」，决定按快捷键时会不会被丢弃
    /// </summary>
    [BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
    [BepInDependency(PluginInfo.SptCoreGuid, BepInDependency.DependencyFlags.HardDependency)]
    public class Plugin : BaseUnityPlugin
    {
        internal static Plugin Instance;
        internal static ManualLogSource Log;

        // 1. General
        internal static ConfigEntry<bool> Enabled;

        // 2. Containers
        internal static ConfigEntry<bool> AllowSecuredContainer;
        internal static ConfigEntry<bool> AllowTacticalVest;
        internal static ConfigEntry<bool> AllowPockets;
        internal static ConfigEntry<bool> AllowBackpack;

        // 3. Item Types
        internal static ConfigEntry<bool> AllowMeds;
        internal static ConfigEntry<bool> AllowFoodDrink;
        internal static ConfigEntry<bool> AllowThrowables;
        internal static ConfigEntry<bool> AllowWeapons;
        internal static ConfigEntry<bool> AllowTacticalDevices;

        // 4. Behaviour
        internal static ConfigEntry<bool> RequireExamined;

        // 5. Compatibility
        internal static ConfigEntry<bool> LogUiFixesStatus;

        // 6. Debug
        internal static ConfigEntry<bool> VerboseLogging;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            BindConfig();

            _harmony = new Harmony(PluginInfo.Guid);
            int applied = PatchInstaller.ApplyAll(_harmony);

            Log.LogInfo(string.Format("Container Quick Bind {0} loaded. {1} patch class(es) applied.", PluginInfo.Version, applied));

            UiFixesCompat.Initialize();
            FikaCompat.Initialize();
        }

        private void BindConfig()
        {
            Enabled = Config.Bind("1. General", "Enabled", true,
                "总开关。关闭后两个 Postfix 立即变为纯空转，游戏回到原生行为。");

            AllowSecuredContainer = Config.Bind("2. Containers", "Allow Secured Container", true,
                "允许把安全箱里的物品绑定到快捷栏。原生不支持。");

            AllowTacticalVest = Config.Bind("2. Containers", "Allow Tactical Vest", true,
                "允许把弹挂内的物品绑定到快捷栏。原生已支持，保留开关便于对照排查。");

            AllowPockets = Config.Bind("2. Containers", "Allow Pockets", true,
                "允许把口袋里的物品绑定到快捷栏。原生已支持，保留开关便于对照排查。");

            AllowBackpack = Config.Bind("2. Containers", "Allow Backpack", true,
                "允许把背包里的物品绑定到快捷栏。原生不支持，这是本模组主要扩展的一项。");

            AllowMeds = Config.Bind("3. Item Types", "Allow Meds", true,
                "允许药品、医疗包、注射器。");

            AllowFoodDrink = Config.Bind("3. Item Types", "Allow Food and Drink", true,
                "允许食物与饮料。");

            AllowThrowables = Config.Bind("3. Item Types", "Allow Throwables", true,
                "允许手雷与投掷物。注意：投掷是独立流程，绑定后按快捷键是把它拿到手上，不会直接扔出去。");

            AllowWeapons = Config.Bind("3. Item Types", "Allow Weapons", false,
                "允许武器与刀。默认关闭：容器内的武器绑到快捷栏会和切枪逻辑叠加，容易误触。");

            AllowTacticalDevices = Config.Bind("3. Item Types", "Allow Tactical Devices", false,
                "允许指南针、测距仪、电台。");

            RequireExamined = Config.Bind("4. Behaviour", "Require Examined", false,
                "只有鉴定过的物品才能绑定，与原版一致。默认关闭：联机（Fika）时主机端的检视状态未必同步，开着会导致主机拒绝绑定。");

            LogUiFixesStatus = Config.Bind("5. Compatibility", "Log UI FIX Status", true,
                "启动时检测 UI FIX 并把协同说明输出到 BepInEx 日志。");

            VerboseLogging = Config.Bind("6. Debug", "Verbose Logging", false,
                "把每次判定结果的详细日志打到控制台。排查拖不上去、按了没反应之类问题时打开。");
        }

        private void OnDestroy()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
            }
        }
    }

    internal static class PluginInfo
    {
        public const string Guid = "com.yg.containerquickbind";
        public const string Name = "Container Quick Bind";
        public const string Version = "1.0.1";
        public const string SptCoreGuid = "com.SPT.core";
        public const string UiFixesGuid = "com.tyfon.uifixes";
        public const string FikaGuid = "com.fika.core";
    }
}
