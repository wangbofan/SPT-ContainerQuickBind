using EFT.InventoryLogic;
using HarmonyLib;
using System;
using System.Collections.Generic;

namespace ContainerQuickBind
{
    /// <summary>
    /// 原始方法：InventoryController.IsAtBindablePlace(Item item) -> bool
    ///
    /// 三个调用点，打这一处三处同时生效：
    ///   EFT.UI.DragAndDrop.BoundItemView.CanAccept      —— 把物品拖到快捷栏格子时，目标格是否接受
    ///   EFT.InventoryLogic.BindResult.Run               —— 真正执行绑定（含 simulate 预演）
    ///   EFT.InventoryLogic.ItemManipulator.TryUnbindOneItem —— 解绑
    ///
    /// 只做 OR 合并（__result 为 true 时直接返回），不覆盖原返回值，
    /// 因此与 UI FIX 的 BindableTacticalPatch（同样 Postfix + OR 合并）可以共存。
    /// </summary>
    [HarmonyPatch(typeof(InventoryController), "IsAtBindablePlace", new Type[] { typeof(Item) })]
    internal static class BindablePlacePatch
    {
        private static readonly HashSet<string> Warned = new HashSet<string>();

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(InventoryController __instance, Item item, ref bool __result)
        {
            if (__result) return;                      // 原版或别的模组已经放行，不干预
            if (!Plugin.Enabled.Value) return;

            bool allowed = false;
            string reason = "unknown";
            try
            {
                allowed = ContainerSlotPolicy.CanBindFromContainer(__instance, item, out reason);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[bindable] 判定异常: " + ex);
                return;
            }

            if (allowed)
            {
                __result = true;
                if (Plugin.VerboseLogging.Value)
                {
                    Plugin.Log.LogInfo("[bindable] 放行 " + ContainerSlotPolicy.Describe(__instance, item));
                }
                return;
            }

            // 拒绝时只在「该物品本来是本模组管的类型」时提示一次，
            // 避免原版每次拖枪、拖护甲都刷屏。
            if (!Plugin.VerboseLogging.Value && !ContainerSlotPolicy.IsBindableKind(item)) return;

            string key = "b:" + ReasonKey(__instance, item) + ":" + reason;
            if (!Warned.Add(key)) return;

            Plugin.Log.LogWarning("[bindable] 拒绝 [" + reason + "] " + ContainerSlotPolicy.Describe(__instance, item));
        }

        internal static string ReasonKey(InventoryController controller, Item item)
        {
            if (item == null) return "<null>";
            return item.Id + "@" + (controller == null ? "?" : controller.GetType().Name);
        }
    }

    /// <summary>
    /// 原始方法：InventoryController.IsAtReachablePlace(Item item) -> bool
    ///
    /// 两个调用点：
    ///   EFT.Player.TryProceed        —— 按快捷栏键时判断物品够不够得着；false 会 SetFirstAvailableItem 收手
    ///   EFT.Player.IsItemCanBeEquipped
    ///
    /// 原版实现只认 Inventory.GetItemsInSlots(Inventory.BindAvailableSlotsExtended) 返回的顶层物品，
    /// 容器里的东西必然不在其中，于是绑上去也用不了。这里补的就是这一步。
    ///
    /// 放行条件收紧为「已绑定到快捷栏 + 位于允许的容器里」，不是无差别放行，
    /// 避免把仓库、尸体、地面物品也变成随手可得。
    /// </summary>
    [HarmonyPatch(typeof(InventoryController), "IsAtReachablePlace", new Type[] { typeof(Item) })]
    internal static class ReachablePlacePatch
    {
        private static readonly HashSet<string> Warned = new HashSet<string>();

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(InventoryController __instance, Item item, ref bool __result)
        {
            if (__result) return;
            if (!Plugin.Enabled.Value) return;

            bool allowed = false;
            string reason = "unknown";
            try
            {
                allowed = ContainerSlotPolicy.CanUseBoundItem(__instance, item, out reason);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[reachable] 判定异常: " + ex);
                return;
            }

            if (allowed)
            {
                __result = true;
                if (Plugin.VerboseLogging.Value)
                {
                    Plugin.Log.LogInfo("[reachable] 放行 " + ContainerSlotPolicy.Describe(__instance, item));
                }
                return;
            }

            if (!Plugin.VerboseLogging.Value && !ContainerSlotPolicy.IsBindableKind(item)) return;

            string key = "r:" + BindablePlacePatch.ReasonKey(__instance, item) + ":" + reason;
            if (!Warned.Add(key)) return;

            Plugin.Log.LogWarning("[reachable] 拒绝 [" + reason + "] " + ContainerSlotPolicy.Describe(__instance, item));
        }
    }

    internal static class PatchInstaller
    {
        /// <summary>
        /// 逐个类型打补丁。任何一个失败都不影响另一个继续装，
        /// 失败信息直接进日志，便于判断是不是 SPT 版本变了导致签名不匹配。
        /// </summary>
        public static int ApplyAll(Harmony harmony)
        {
            int ok = 0;
            if (SafePatch(harmony, typeof(BindablePlacePatch))) ok++;
            if (SafePatch(harmony, typeof(ReachablePlacePatch))) ok++;
            if (SafePatch(harmony, typeof(UnbindResultPatch))) ok++;
            if (SafePatch(harmony, typeof(BindResultPatch))) ok++;
            if (SafePatch(harmony, typeof(PreserveBindingsPatch))) ok++;
            return ok;
        }

        private static bool SafePatch(Harmony harmony, Type patchType)
        {
            try
            {
                harmony.CreateClassProcessor(patchType).Patch();
                Plugin.Log.LogInfo("[patch] " + patchType.Name + " 已安装");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[patch] " + patchType.Name + " 安装失败: " + ex);
                return false;
            }
        }
    }
}
