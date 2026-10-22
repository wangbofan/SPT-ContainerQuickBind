using EFT.InventoryLogic;
using System;
using System.Linq;
using System.Text;

namespace ContainerQuickBind
{
    /// <summary>
    /// 判定一个物品是否「位于我们允许的容器里、且类型是允许绑定的消耗品」。
    ///
    /// 原版 InventoryController.IsAtBindablePlace 的硬限制有两层，都要绕开：
    ///   1) 它只接受父级槽位属于 Inventory.FastAccessSlots = { Pockets, TacticalVest }，所以安全箱和背包直接出局；
    ///   2) 它只向上看一层父级（item.Parent.Container.ParentItem.CurrentAddress），
    ///      所以「背包 -> 内部网格 -> 药品」这种两层结构拿到的父级槽位是 null。
    /// 这里换成逐层向上遍历，一直走到挂在 Equipment 上的那个槽位为止。
    /// </summary>
    internal static class ContainerSlotPolicy
    {
        /// <summary>允许作为绑定来源的装备槽，顺序即检查顺序。</summary>
        private static readonly EquipmentSlot[] CandidateSlots = new EquipmentSlot[]
        {
            EquipmentSlot.SecuredContainer,
            EquipmentSlot.TacticalVest,
            EquipmentSlot.Pockets,
            EquipmentSlot.Backpack
        };

        public static bool IsSlotEnabled(EquipmentSlot slot)
        {
            switch (slot)
            {
                case EquipmentSlot.SecuredContainer: return Plugin.AllowSecuredContainer.Value;
                case EquipmentSlot.TacticalVest: return Plugin.AllowTacticalVest.Value;
                case EquipmentSlot.Pockets: return Plugin.AllowPockets.Value;
                case EquipmentSlot.Backpack: return Plugin.AllowBackpack.Value;
                default: return false;
            }
        }

        /// <summary>
        /// 物品类型白名单。原版允许：Weapon / ThrowWeap / 带 KnifeComponent / Meds / FoodDrink /
        /// PortableRangeFinder / Compass / RadioTransmitter。这里逐项挂到配置开关上。
        /// </summary>
        public static bool IsBindableKind(Item item)
        {
            if (item == null) return false;

            if (item is Meds) return Plugin.AllowMeds.Value;
            if (item is FoodDrink) return Plugin.AllowFoodDrink.Value;
            if (item is ThrowWeap) return Plugin.AllowThrowables.Value;

            if (Plugin.AllowWeapons.Value)
            {
                if (item is Weapon) return true;
                if (item.GetItemComponent<KnifeComponent>() != null) return true;
            }

            if (Plugin.AllowTacticalDevices.Value)
            {
                if (item is PortableRangeFinder) return true;
                if (item is Compass) return true;
                if (item is RadioTransmitter) return true;
            }

            return false;
        }

        /// <summary>缺零件或缺弹匣的复合物品不允许绑定，与原版一致。</summary>
        public static bool HasMissingVitalParts(Item item)
        {
            CompoundItem compound = item as CompoundItem;
            if (compound == null) return false;
            return compound.MissingVitalParts.Any();
        }

        /// <summary>
        /// 从物品地址逐层向上遍历，找出它最终挂在 Equipment 的哪个槽位上。
        /// 「背包里的药」「安全箱里的注射器」都能正确解析到 Backpack / SecuredContainer。
        /// </summary>
        public static bool TryResolveEquipmentSlot(Item item, InventoryEquipment equipment, out EquipmentSlot slot)
        {
            slot = EquipmentSlot.Backpack;
            if (item == null || equipment == null) return false;

            ItemAddress address = item.CurrentAddress;
            int guard = 0;

            while (address != null && guard++ < 64)
            {
                IContainer container = address.Container;
                if (container == null) return false;

                Item owner = container.ParentItem;
                Slot slotContainer = container as Slot;

                // 走到「容器本身是一个装备槽，且它的宿主就是 Equipment」这一层，就到了顶。
                if (slotContainer != null && ReferenceEquals(owner, equipment))
                {
                    for (int i = 0; i < CandidateSlots.Length; i++)
                    {
                        Slot candidate = equipment.GetSlot(CandidateSlots[i]);
                        if (ReferenceEquals(candidate, slotContainer))
                        {
                            slot = CandidateSlots[i];
                            return true;
                        }
                    }

                    // 挂在 Equipment 的其它槽位（枪套、刀鞘、头戴等），不归本模组管。
                    return false;
                }

                if (owner == null) return false;
                address = owner.CurrentAddress;
            }

            return false;
        }

        /// <summary>物品当前是否已被绑到某个快捷栏格子上。</summary>
        public static bool IsBoundToQuickbar(FastAccess fastAccess, Item item)
        {
            if (fastAccess == null || item == null) return false;

            for (int i = 0; i <= (int)EBoundItem.Item10; i++)
            {
                Item bound = fastAccess.GetBoundItem((EBoundItem)i);
                if (ReferenceEquals(bound, item)) return true;
            }

            return false;
        }

        /// <summary>
        /// IsAtBindablePlace 的放行判定：拖到快捷栏格子时走这里。
        /// </summary>
        public static bool CanBindFromContainer(InventoryController controller, Item item)
        {
            string reason;
            return Evaluate(controller, item, false, out reason);
        }

        /// <summary>
        /// 带诊断的版本：reason 会写清是哪一条导致拒绝，方便看日志定位。
        /// </summary>
        public static bool CanBindFromContainer(InventoryController controller, Item item, out string reason)
        {
            return Evaluate(controller, item, false, out reason);
        }

        /// <summary>
        /// IsAtReachablePlace 的放行判定：按快捷键使用时走这里。
        /// 必须同时满足「已绑定到快捷栏」和「位于允许的容器里」，
        /// 否则等于把所有物品都变成随手可得，会破坏原版的距离与可达性规则。
        /// </summary>
        public static bool CanUseBoundItem(InventoryController controller, Item item)
        {
            string reason;
            return Evaluate(controller, item, true, out reason);
        }

        public static bool CanUseBoundItem(InventoryController controller, Item item, out string reason)
        {
            return Evaluate(controller, item, true, out reason);
        }

        private static bool Evaluate(InventoryController controller, Item item, bool requireBound, out string reason)
        {
            reason = "ok";

            if (controller == null) { reason = "controller is null"; return false; }
            if (item == null) { reason = "item is null"; return false; }

            Inventory inventory;
            try
            {
                inventory = controller.Inventory;
            }
            catch (Exception ex)
            {
                reason = "inventory threw: " + ex.GetType().Name;
                return false;
            }

            if (inventory == null) { reason = "inventory is null"; return false; }
            if (inventory.Equipment == null) { reason = "equipment is null"; return false; }

            if (!IsBindableKind(item))
            {
                reason = "type not allowed (" + item.GetType().Name + ")";
                return false;
            }

            if (HasMissingVitalParts(item))
            {
                reason = "missing vital parts";
                return false;
            }

            if (Plugin.RequireExamined.Value && !controller.Examined(item))
            {
                reason = "not examined";
                return false;
            }

            if (requireBound && !IsBoundToQuickbar(inventory.FastAccess, item))
            {
                reason = "not bound to quickbar";
                return false;
            }

            EquipmentSlot slot;
            if (!TryResolveEquipmentSlot(item, inventory.Equipment, out slot))
            {
                reason = "not inside an allowed equipment container";
                return false;
            }

            if (!IsSlotEnabled(slot))
            {
                reason = "container disabled in config (" + slot + ")";
                return false;
            }

            reason = "ok (" + slot + ")";
            return true;
        }

        /// <summary>日志用的一行描述，形如 "[背包] 布洛芬 (Meds)"。</summary>
        public static string Describe(InventoryController controller, Item item)
        {
            if (item == null) return "<null>";

            StringBuilder sb = new StringBuilder();
            string slotName = "?";

            if (controller != null && controller.Inventory != null && controller.Inventory.Equipment != null)
            {
                EquipmentSlot slot;
                if (TryResolveEquipmentSlot(item, controller.Inventory.Equipment, out slot))
                {
                    slotName = SlotDisplayName(slot);
                }
                else
                {
                    slotName = "未知槽位";
                }
            }

            sb.Append('[');
            sb.Append(slotName);
            sb.Append("] ");
            sb.Append(item.Name);

            string typeName = item.GetType().Name;
            if (!string.Equals(typeName, "Item", StringComparison.Ordinal))
            {
                sb.Append(" (");
                sb.Append(typeName);
                sb.Append(')');
            }

            return sb.ToString();
        }

        private static string SlotDisplayName(EquipmentSlot slot)
        {
            switch (slot)
            {
                case EquipmentSlot.SecuredContainer: return "安全箱";
                case EquipmentSlot.TacticalVest: return "弹挂";
                case EquipmentSlot.Pockets: return "口袋";
                case EquipmentSlot.Backpack: return "背包";
                default: return slot.ToString();
            }
        }
    }
}
