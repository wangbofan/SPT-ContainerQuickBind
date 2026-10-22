using BepInEx.Bootstrap;
using System;

namespace ContainerQuickBind
{
    /// <summary>
    /// Fika 联机的额外说明。
    ///
    /// 这不是一个补丁类，只负责把「为什么联机时必须双端安装」讲清楚，并在检测到
    /// Fika 但本机没装到主机端时，把排查方向直接写进日志。
    ///
    /// 原理（已在 Assembly-CSharp 里逐条核对）：
    ///   客户端按下拖拽 → BoundItemView.CanAccept → 本模组放行 → 生成 BindItemOperationDescriptor 发给主机；
    ///   主机 FikaServer.OnInventoryPacketReceived → IOperationHandler.CreateOperationFromDescriptor
    ///     → BindItemOperationDescriptor.ToInventoryOperation
    ///     → BindResult.Run(controller, item, index, simulate: true)
    ///     → InventoryController.IsAtBindablePlace        ← 主机的判定，本模组若未安装在主机上就是原版逻辑
    ///     → 失败即回 OperationCallbackPacket(status = 3) + ResyncInventoryIdPacket，
    ///       客户端收到「Client operation rejected by server」并把绑定回滚。
    ///
    /// 也就是说：判定发生在主机，客户端放宽没有意义。
    /// 官方单人（无 Fika）时本机既是主机，所以一份 DLL 就够。
    /// </summary>
    internal static class FikaCompat
    {
        public static void Initialize()
        {
            bool hasFika = Chainloader.PluginInfos != null
                           && Chainloader.PluginInfos.ContainsKey(PluginInfo.FikaGuid);

            if (!hasFika) return;

            Plugin.Log.LogWarning(
                "[fika] 检测到 Fika（" + PluginInfo.FikaGuid + "）。" +
                "绑定操作的最终判定在主机端执行，主机（包括 headless）必须安装同一份 ContainerQuickBind.dll，" +
                "否则拖到快捷栏会被主机以「Cannot bind item」拒绝并回滚。");

            Plugin.Log.LogWarning(
                "[fika] 单人模式（不经过 Fika 主机）不受此限制。");
        }
    }
}
