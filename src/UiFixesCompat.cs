using BepInEx.Bootstrap;
using System;
using System.Collections.Generic;

namespace ContainerQuickBind
{
    /// <summary>
    /// 与 Tyfon.UIFixes（com.tyfon.uifixes）的协同层。
    ///
    /// 已经逐个把 UI FIX 6.0.4 的相关 patch 看完了，冲突面如下：
    ///
    /// 1) UIFixes.TacticalBindsPatches.BindableTacticalPatch
    ///      目标 InventoryController.IsAtBindablePlace，Postfix。
    ///      IL: if (__result) return; __result = IsEquippedTacticalDevice(...);
    ///    → 纯 OR 合并。我用 HarmonyPriority(Priority.Last) 排在它后面，双方互不覆盖。
    ///
    /// 2) UIFixes.TacticalBindsPatches.ReachableTacticalPatch
    ///      目标 InventoryController.IsAtReachablePlace，Postfix。IL 同上，也是 OR 合并。
    ///    → 同样安全。
    ///
    /// 3) UIFixes.TacticalBindsPatches.UseTacticalPatch
    ///      目标 Player.SetQuickSlotItem，Prefix。只在绑定物品带 LightComponent 或
    ///      NightVisionComponent 时接管（开关手电 / 夜视）并返回 false；
    ///      其余情况返回 true 放行原方法。
    ///    → 药品、食物、投掷物都不带这两个组件，直接落到原版逻辑，不受影响。
    ///
    /// 4) UIFixes.TacticalBindsPatches.BindTacticalPatch / UnbindTacticalPatch
    ///      目标 BindResult.Run / UnbindResult.Run，Postfix，会调
    ///      UIFixes.TacticalBindsPatches.UpdateQuickbindType(item, index)。
    ///    → UpdateQuickbindType 对非手电非夜视的物品执行
    ///      UIFixes.Quickbind.SetType(index, ItemType.Other)。
    ///      而 Quickbind 内部只是一个 Dictionary&lt;EBoundItem, ItemType&gt;，
    ///      Other 就是"普通物品"的默认值，不会清掉原生绑定，也不会改键位。
    ///      所以把容器里的药品绑上去之后，UI FIX 只会把它正确标成 Other。
    ///
    /// 5) UIFixes.TacticalBindsPatches.InitQuickBindsPatch
    ///      目标 EFT.MainMenuShowOperation.Init，Postfix。回到主菜单时遍历
    ///      FastAccess.BoundItems 重算每个格子的类型，同样只会得到 Other。
    ///
    /// 另外 UI FIX 的 [F. Item Keybinds] 节里的 Use Item / Use Item (All) 快捷键
    /// 是库存界面内的鼠标悬停使用，和本模组的原生快捷栏绑定不是同一条路径，没有键位冲突。
    /// 本模组不注册任何新按键，也不改键位配置，所以不需要做键位避让。
    /// </summary>
    internal static class UiFixesCompat
    {
        public static void Initialize()
        {
            if (!Plugin.LogUiFixesStatus.Value) return;

            bool present;
            try
            {
                present = Chainloader.PluginInfos.ContainsKey(PluginInfo.UiFixesGuid);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[compat] 无法读取插件列表: " + ex.Message);
                return;
            }

            if (!present)
            {
                Plugin.Log.LogInfo("[compat] 未检测到 UI FIX (com.tyfon.uifixes)，独立运行。");
                return;
            }

            Plugin.Log.LogInfo("[compat] 检测到 UI FIX，开始核对共享的 patch 目标：");
            Plugin.Log.LogInfo("[compat]   TacticalBindsPatches/BindableTacticalPatch  -> InventoryController.IsAtBindablePlace  [Postfix, OR 合并]");
            Plugin.Log.LogInfo("[compat]   TacticalBindsPatches/ReachableTacticalPatch -> InventoryController.IsAtReachablePlace [Postfix, OR 合并]");
            Plugin.Log.LogInfo("[compat]   TacticalBindsPatches/UseTacticalPatch        -> Player.SetQuickSlotItem               [Prefix, 仅手电/夜视]");
            Plugin.Log.LogInfo("[compat]   TacticalBindsPatches/BindTacticalPatch       -> BindResult.Run                        [Postfix]");
            Plugin.Log.LogInfo("[compat]   TacticalBindsPatches/UnbindTacticalPatch     -> UnbindResult.Run                      [Postfix]");
            Plugin.Log.LogInfo("[compat] 本模组两个 Postfix 均为 Priority.Last 的 OR 合并，不覆盖 UI FIX 的手电/夜视绑定。");
            Plugin.Log.LogInfo("[compat] UI FIX 的 UpdateQuickbindType 会把容器消耗品标为 ItemType.Other，不改动原生绑定。");
        }
    }
}
