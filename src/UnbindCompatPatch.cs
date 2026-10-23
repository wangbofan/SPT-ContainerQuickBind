using Diz.LanguageExtensions;
using EFT.InventoryLogic;
using HarmonyLib;
using System;
using System.Collections.Generic;

namespace ContainerQuickBind
{
    /// <summary>
    /// 原始方法：UnbindResult.Run(InventoryController, Item, EBoundItem, bool)
    ///           -> OperationResult&lt;UnbindResult&gt;
    ///
    /// 原版 IL：
    ///   if (!inventory.FastAccess.BoundItems.TryGetValue(index, out cur)) -> " is not bound to "
    ///   if (cur != item)                                                 -> " is not bound to "
    ///   if (!simulate) BoundItems.Remove(index);
    ///   return new UnbindResult(controller, item, index);
    ///
    /// 为什么联机时这里会炸：
    ///   客户端把「药从 Item5 挪到 Item4」表示成 BindItem 加一条子操作 Unbind(Item5)，
    ///   而主机侧的 FastAccess.BoundItems 是网络同步出来的观察者镜像 —— 绑定关系
    ///   本身不在 Fika 的同步范围内。于是 UnbindItemOperationDescriptor.ToInventoryOperation
    ///   里那句 UnbindResult.Run(..., simulate:true) 在主机上找不到 Item5 -> item，
    ///   整个包在「转换」阶段就被拒：
    ///     ItemControllerExecutePacket::Operation conversion failed:
    ///       Item item_meds_xxx (id: ...) is not bound to Item5
    ///   客户端随之收到 OperationCallbackPacket(status=3) 和 ResyncInventoryIdPacket。
    ///
    /// 修法：把「解绑一个本来就没绑上的槽位」变成幂等成功。
    /// 只对本模组放行的容器消耗品生效，且槽位里确实没有别的物品时才兜底，
    /// 所以不会掩盖真正的绑定冲突（真冲突时 bound != item，直接放行给原版报错）。
    /// </summary>
    [HarmonyPatch(typeof(UnbindResult), "Run",
        new Type[] { typeof(InventoryController), typeof(Item), typeof(EBoundItem), typeof(bool) })]
    internal static class UnbindResultPatch
    {
        private static readonly HashSet<string> Warned = new HashSet<string>();

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(InventoryController controller, Item item, EBoundItem index, bool simulate,
                                    ref OperationResult<UnbindResult> __result)
        {
            if (__result.Succeeded) return;              // 原版已经解绑成功
            if (!Plugin.Enabled.Value) return;
            if (controller == null || item == null) return;

            try
            {
                // 只处理本模组放行的容器消耗品；武器、护甲、手电的解绑语义保持原样
                if (!ContainerSlotPolicy.CanBindFromContainer(controller, item)) return;

                Item bound = controller.Inventory.FastAccess.GetBoundItem(index);
                // 该槽位绑着另一个物品 —— 这是真的冲突，交回原版报错
                if (bound != null && !ReferenceEquals(bound, item)) return;

                __result = new UnbindResult(controller, item, index);

                // 主机侧的镜像可能残留「同一个物品绑在多个槽」的脏数据，
                // 真正执行解绑时顺手清掉，避免一次使用触发两次。
                if (!simulate) RemoveStaleBindings(controller, item, index);

                if (Plugin.VerboseLogging.Value)
                {
                    string key = item.Id + "@" + index;
                    if (Warned.Add("u:" + key))
                    {
                        Plugin.Log.LogInfo("[unbind] 幂等放行 " + ContainerSlotPolicy.Describe(controller, item)
                                           + " @" + index + " (simulate=" + simulate + ")");
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[unbind] 兜底异常: " + ex);
            }
        }

        private static void RemoveStaleBindings(InventoryController controller, Item item, EBoundItem keep)
        {
            Dictionary<EBoundItem, Item> boundItems = controller.Inventory.FastAccess.BoundItems;
            List<EBoundItem> stale = null;

            foreach (KeyValuePair<EBoundItem, Item> kv in boundItems)
            {
                if (kv.Key == keep) continue;
                if (!ReferenceEquals(kv.Value, item)) continue;
                if (stale == null) stale = new List<EBoundItem>();
                stale.Add(kv.Key);
            }

            if (stale == null) return;
            for (int i = 0; i < stale.Count; i++) boundItems.Remove(stale[i]);
        }
    }
}
