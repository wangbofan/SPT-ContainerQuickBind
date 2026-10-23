using Diz.LanguageExtensions;
using EFT.InventoryLogic;
using HarmonyLib;
using System;
using System.Collections.Generic;

namespace ContainerQuickBind
{
    /// <summary>
    /// 原始方法：BindResult.Run(InventoryController, Item, EBoundItem, bool)
    ///           -> OperationResult&lt;BindResult&gt;
    ///
    /// 原版 IL（已逐条反汇编确认）：
    ///   if (!controller.IsAtBindablePlace(item)) return "Cannot bind item";
    ///   List&lt;UnbindResult&gt; list = null;
    ///   if (FastAccess.BoundItems.Any(kv => kv.Value == item)) {
    ///       var r1 = controller.UnbindItemDirect(item, simulate);
    ///       if (r1.Succeeded) list = new List&lt;UnbindResult&gt; { r1.Value };   // 失败就静默跳过
    ///   }
    ///   if (FastAccess.BoundItems.TryGetValue(index, out var old)) {
    ///       var r2 = controller.UnbindItemDirect(old, simulate);
    ///       if (r2.Succeeded) { list = list ?? new List&lt;UnbindResult&gt;(); list.Add(r2.Value); }
    ///   }
    ///   if (!simulate) FastAccess.BoundItems[index] = item;   // ← 无条件写！
    ///   return new BindResult(controller, item, index, list);
    ///
    /// 缺陷在第 3~4 段：UnbindItemDirect 失败时只记一笔日志，循环照旧往下走，
    /// 最后那句 set_Item 照样把新槽位写进去 —— 于是在联机环境下（主机侧
    /// FastAccess.BoundItems 是观察者镜像，绑定关系不在 Fika 同步范围内），
    /// 「把药从 Item4 拖到 Item5」就会变成 Item4 和 Item5 同时指向同一个药：
    ///   客户端本地：UnbindItemDirect(Item4) 成功，本该只剩 Item5；
    ///   主机镜像：  UnbindItemDirect 找不到 Item4，list 为 null，但仍写入 Item5，
    ///              于是主机侧 Item4、Item5 双绑；下一次 resync 把这份脏数据
    ///              通过 InventoryDescriptor.FastAccess 灌回客户端，两边都双绑。
    ///
    /// 修法：在原方法成功后补一条不变式 —— 一个 Item 只允许占用一个快捷栏槽位。
    /// 只删「指向同一个 Item 实例的其它槽位」，不碰别人的绑定；simulate 阶段
    /// 一律不动，保证预演（CanAccept / ToInventoryOperation）不会产生副作用。
    /// </summary>
    [HarmonyPatch(typeof(BindResult), "Run",
        new Type[] { typeof(InventoryController), typeof(Item), typeof(EBoundItem), typeof(bool) })]
    internal static class BindResultPatch
    {
        private static readonly HashSet<string> Warned = new HashSet<string>();

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(InventoryController controller, Item item, EBoundItem index, bool simulate,
                                    ref OperationResult<BindResult> __result)
        {
            if (__result.Failed) return;
            if (simulate) return;                       // 预演不能改状态
            if (!Plugin.Enabled.Value) return;
            if (controller == null || item == null) return;

            try
            {
                Inventory inventory = controller.Inventory;
                if (inventory == null || inventory.FastAccess == null) return;

                Dictionary<EBoundItem, Item> boundItems = inventory.FastAccess.BoundItems;
                List<EBoundItem> stale = null;

                foreach (KeyValuePair<EBoundItem, Item> kv in boundItems)
                {
                    if (kv.Key == index) continue;
                    if (!ReferenceEquals(kv.Value, item)) continue;
                    if (stale == null) stale = new List<EBoundItem>();
                    stale.Add(kv.Key);
                }

                if (stale == null) return;

                for (int i = 0; i < stale.Count; i++) boundItems.Remove(stale[i]);

                if (Plugin.VerboseLogging.Value)
                {
                    string key = item.Id + "@" + index;
                    if (Warned.Add(key))
                    {
                        Plugin.Log.LogWarning("[bind] 清理重复绑定 " + item.Id + " 保留 " + index
                                              + "，移除 " + stale.Count + " 个残留槽位");
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[bind] 不变式修复异常: " + ex);
            }
        }
    }

    /// <summary>
    /// 原始方法：InventoryController.ReplaceInventory(Inventory)
    ///
    /// 战局里每次收到 Fika 的 ResyncInventoryIdPacket，客户端都会用主机下发的
    /// InventoryDescriptor 重建一份 Inventory —— 而 InventoryDescriptor 里
    /// FastAccess 字段的类型是 Dictionary&lt;EBoundItem, MongoID&gt;，也就是说
    /// 快捷栏绑定完全由主机那份镜像决定。主机镜像一旦是空的（观察者控制器
    /// 重建、玩家刚进战局、或者前面那种双绑脏数据被清掉），客户端的绑定就
    /// 会整个消失：表现就是「把背包关掉再打开，快捷栏 4 和 5 全空了」。
    ///
    /// 修法：换 Inventory 之前把本地绑定按 Item.Id 记下来，换完以后凡是
    /// 新 Inventory 里还没有对应 key 的，就在新 Inventory 里按 Id 找回
    /// Item 实例补回去。已经有 key 的以新 Inventory 为准 —— 主机的数据优先，
    /// 本模组只在主机「什么都没说」的时候兜底，不会覆盖主机的判断。
    /// </summary>
    [HarmonyPatch(typeof(InventoryController), "ReplaceInventory", new Type[] { typeof(Inventory) })]
    internal static class PreserveBindingsPatch
    {
        [HarmonyPrefix]
        private static void Prefix(InventoryController __instance, ref Dictionary<EBoundItem, string> __state)
        {
            __state = null;
            if (!Plugin.Enabled.Value) return;

            try
            {
                Inventory old = __instance.Inventory;
                if (old == null || old.FastAccess == null) return;

                Dictionary<EBoundItem, string> map = null;
                foreach (KeyValuePair<EBoundItem, Item> kv in old.FastAccess.BoundItems)
                {
                    if (kv.Value == null) continue;
                    if (map == null) map = new Dictionary<EBoundItem, string>();
                    map[kv.Key] = kv.Value.Id;
                }

                __state = map;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[preserve] 记录绑定异常: " + ex);
            }
        }

        [HarmonyPostfix]
        private static void Postfix(InventoryController __instance, Dictionary<EBoundItem, string> __state)
        {
            if (__state == null) return;

            try
            {
                Inventory inventory = __instance.Inventory;
                if (inventory == null || inventory.FastAccess == null) return;

                int restored = 0;
                foreach (KeyValuePair<EBoundItem, string> kv in __state)
                {
                    // 新 Inventory 已经有这个槽位的绑定 —— 以它为准
                    if (inventory.FastAccess.BoundItems.ContainsKey(kv.Key)) continue;

                    Item found;
                    if (!__instance.TryFindItem(kv.Value, out found) || found == null) continue;

                    inventory.FastAccess.BoundItems[kv.Key] = found;
                    restored++;

                    Plugin.Log.LogWarning("[preserve] 重新同步后补回快捷栏绑定 " + kv.Key + " -> " + found.Name);
                }

                if (restored > 0 && Plugin.VerboseLogging.Value)
                {
                    Plugin.Log.LogInfo("[preserve] 本次共补回 " + restored + " 个快捷栏绑定");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[preserve] 恢复绑定异常: " + ex);
            }
        }
    }
}
