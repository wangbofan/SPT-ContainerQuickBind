# Container Quick Bind

逃生塔科夫（SPT 4.1.x）客户端模组：**让安全箱 / 弹挂 / 口袋 / 背包里的消耗品可以直接拖到原生快捷栏绑定，绑定后按原生 4~0 快捷键使用。**

不需要记忆新按键，不新增界面，不改动原版绑定流程——只是把原版对「物品从哪来」的限制放开。

---

## 1. 原版到底限制了什么

游戏里拖物品到下方快捷栏时，会走 `EFT.InventoryLogic.BindResult.Run()`，第一行就是：

```csharp
if (!controller.IsAtBindablePlace(item)) return "Cannot bind item";
```

`InventoryController.IsAtBindablePlace` 的实现有两道闸：

| 限制 | 具体行为 | 后果 |
|---|---|---|
| 只认两个槽位 | `Inventory.FastAccessSlots` 的实际内容是 `{ Pockets, TacticalVest }` | 安全箱、背包里的东西一律绑不上 |
| 只看一层父级 | 取 `item.Parent.Container.ParentItem.CurrentAddress.Container as Slot` | 「背包 → 内部网格 → 药品」这种两层结构拿到 `null`，同样出局 |

即使绕过第一道，还有第二道：按快捷键时 `Player.TryProceed` 会查 `IsAtReachablePlace`，而它依赖 `Inventory.GetItemsInSlots(BindAvailableSlotsExtended)`——只返回**槽位顶层**的物品，容器内部的东西永远不在列表里。判定失败会 `SetFirstAvailableItem` 直接把东西收回去，表现为「按了没反应」。

本模组就干两件事：把这两道闸按配置放开。

---

## 2. 安装

1. 确认是 SPT 4.1.x（游戏本体 0.16.x），已装 BepInEx 5.4.x。
2. 二选一：

   **下载预编译版**——从 [Releases](https://github.com/wangbofan/SPT-ContainerQuickBind/releases/latest) 取最新的 `SPT-ContainerQuickBind-*.zip`，直接解压到游戏根目录。

   **自己编译**——见[第 8 节](#8-构建)，产物是 `dist/ContainerQuickBind.dll`。

   无论哪种，最终都是这个位置：

   ```
   <游戏目录>\BepInEx\plugins\ContainerQuickBind.dll
   ```

   > **用 Fika 联机的话，主机端（含 headless）也要放同一份 DLL**，否则主机校验会拒绝绑定并回滚。详见[第 6 节](#6-fika-联机必须双端安装)。

3. 启动一次游戏，生成配置文件：

   ```
   <游戏目录>\BepInEx\config\com.yg.containerquickbind.cfg
   ```

依赖：`com.SPT.core`（SPT 本体）。**不依赖 UI FIX**，装不装都能用。

---

## 3. 用法

1. 进战局，按 `Tab` 打开背包。
2. 把安全箱或背包里的药品 / 饮料 / 手雷**拖到屏幕下方的快捷栏格子上**。
   - 以前拖上去会被弹回来（格子不接收），现在格子会亮起并接受。
3. 松手，绑定完成。快捷栏格子显示该物品的图标。
4. 关掉背包，按对应的 `4`~`0` 键，人物直接把药拿出来用。

解绑：把快捷栏格子里的图标拖走，或拖一个新物品覆盖，跟原版一样。

> 注意：拖动的是**绑定关系**，不是移动物品。物品本身还留在安全箱/背包里，不会占快捷栏位置，也不会被移动。

---

## 4. 配置

配置文件 `BepInEx\config\com.yg.containerquickbind.cfg`，游戏内也可用 Configuration Manager 改。

### 1. General

| 键 | 默认 | 说明 |
|---|---|---|
| Enabled | `true` | 总开关。关掉后两个 Postfix 立即空转，回到原版行为 |

### 2. Containers —— 允许从哪些容器绑定

| 键 | 默认 | 说明 |
|---|---|---|
| Allow Secured Container | `true` | 安全箱。原版不支持 |
| Allow Tactical Vest | `true` | 弹挂。原版已支持，留开关便于对照 |
| Allow Pockets | `true` | 口袋。原版已支持，留开关便于对照 |
| Allow Backpack | `true` | 背包。原版不支持，**这是本模组主要扩展项** |

### 3. Item Types —— 允许绑定哪些类型

| 键 | 默认 | 说明 |
|---|---|---|
| Allow Meds | `true` | 药品、医疗包、注射器 |
| Allow Food and Drink | `true` | 食物、饮料 |
| Allow Throwables | `true` | 手雷、投掷物（拿到手上，不会直接扔） |
| Allow Weapons | `false` | 武器与刀。默认关：容器里的枪绑到快捷栏会和切枪逻辑叠加，容易误触 |
| Allow Tactical Devices | `false` | 指南针、测距仪、电台 |

### 4. Behaviour

| 键 | 默认 | 说明 |
|---|---|---|
| Require Examined | `true` | 只允许已鉴定物品，与原版一致。关掉后刚从别人背包里捡到的没鉴定过的药也能绑 |

### 5. Compatibility

| 键 | 默认 | 说明 |
|---|---|---|
| Log UI FIX Status | `true` | 启动时检测 UI FIX，把协同结论写进 `BepInEx\LogOutput.log` |

### 6. Debug

| 键 | 默认 | 说明 |
|---|---|---|
| Verbose Logging | `false` | 每次判定都打一行 `[bindable] 放行 [背包] 布洛芬 (Meds)`，排查用 |

---

## 5. 和 UI FIX 的冲突处理（重点）

UI FIX = `Tyfon.UIFixes`（GUID `com.tyfon.uifixes`）。经反编译核对 6.0.4 版本，它的 `UIFixes.TacticalBindsPatches` 一共挂 6 个 patch，其中 5 个与本模组相关：

| UI FIX 的 patch | 目标方法 | 类型 | 与本模组的关系 |
|---|---|---|---|
| `BindableTacticalPatch` | `InventoryController.IsAtBindablePlace` | Postfix | **同一方法**。见下 |
| `ReachableTacticalPatch` | `InventoryController.IsAtReachablePlace` | Postfix | **同一方法**。见下 |
| `UseTacticalPatch` | `Player.SetQuickSlotItem` | Prefix | 只在物品带 `LightComponent` / `NightVisionComponent` 时接管，其余 `return true` 放行 |
| `BindTacticalPatch` | `BindResult.Run` | Postfix | 只更新「这个格子是手电还是夜视」的标记 |
| `UnbindTacticalPatch` | `UnbindResult.Run` | Postfix | 同上 |
| `InitQuickBindsPatch` | `MainMenuShowOperation.Init` | Postfix | 回主菜单时重算格子标记 |

### 处理方式

UI FIX 那两个 Postfix 的字节码是：

```cil
ldarg.2          ; __result
ldind.u1
brfalse.s SKIP   ; 已经是 true 就直接返回
ret
SKIP:
ldarg.2
... IsEquippedTacticalDevice(...)
stind.i1         ; __result = 计算结果
ret
```

也就是标准的 **`if (__result) return;` 式 OR 合并**——它只在自己能处理时把 `false` 抬成 `true`，从不把 `true` 压回 `false`。

本模组用完全相同的写法，并加上 `[HarmonyPriority(Priority.Last)]` 排在它之后：

```csharp
if (__result) return;      // 原版或 UI FIX 已放行，不碰
if (!Plugin.Enabled.Value) return;
if (ContainerSlotPolicy.CanBindFromContainer(__instance, item)) __result = true;
```

于是：
- 手电 / 夜视 → UI FIX 先判定 `true`，本模组直接返回，**手电夜视功能完全不受影响**；
- 容器里的药品 → UI FIX 判定 `false`，本模组接手抬成 `true`，绑定成功；
- 两者都判 `false` → 保持 `false`，与原版一致。

### 绑定之后 UI FIX 会不会把绑定清掉

不会。`BindResult.Run` 之后 UI FIX 会调 `UpdateQuickbindType(item, index)`，其逻辑是：

```
item == null                                  -> Quickbind.SetType(index, ItemType.Other)
item 有 LightComponent 且根物品是 Weapon       -> ItemType.Headlight
item 有 LightComponent 且根物品是 Headwear     -> ItemType.Tactical
item 有 NightVisionComponent                  -> ItemType.NightVision
其余                                          -> ItemType.Other
```

药品走最后一条，被标成 `ItemType.Other`。而 `UIFixes.Quickbind` 内部只是一个

```csharp
public static Dictionary<EBoundItem, ItemType> TacticalQuickbinds;
public static void SetType(EBoundItem index, ItemType type)
    => TacticalQuickbinds[index] = type;
```

纯标记字典，`Other` 就是「普通物品」的默认值，**既不清空 `FastAccess.BoundItems`，也不改键位配置**。

### UI FIX 的「用完自动重绑」会跟着一起生效（正向协同）

UI FIX 6.0.4 有一个配置项 `Quickbind Matching Consumable After Use`（默认 `true`），由 `UIFixes.RebindConsumablesPatches` 实现：一盒药用完被丢弃后，它会从容器里找一盒**相同 `TemplateId`** 的物品，自动绑到刚空出来的那个格子上。

它挑候选物品用的是：

```csharp
var slots = new[] { EquipmentSlot.Pockets, EquipmentSlot.TacticalVest,
                    EquipmentSlot.Backpack, EquipmentSlot.SecuredContainer,
                    EquipmentSlot.ArmBand };
controller.GetAcceptableItemsNonAlloc<Item>(slots, list,
    g => g.TemplateId == unbindOperation.Item.TemplateId, null);
var replacement = list.FirstOrDefault(g => controller.IsAtBindablePlace(g));
```

两个细节：

1. `GetAcceptableItemsNonAlloc` 只遍历一层（`foreach container in cc.Containers` → `foreach item in container.Items`），不递归。口袋 / 弹挂 / 背包 / 安全箱的直接内容正好都在这一层，所以**背包里的备用药品本来就在候选列表里**。
2. 最后一步用 `IsAtBindablePlace` 过滤——**这正是原版把它们全部判掉的地方**。

也就是说：这个功能在纯原版下永远是「候选列表非空，但一个都过不了滤」，自动重绑从不发生。装上本模组后，`IsAtBindablePlace` 对容器内的同类药品返回 `true`，它就真的接上了。

行为上的直接结果：**快捷栏上的药用完之后，会自动换上一盒从背包 / 安全箱里取出的同类药**，中途不用开背包。这是纯增益，不需要额外配置；不想要就和本模组一起关掉（把 UI FIX 的 `Quickbind Matching Consumable After Use` 设为 `false`）。

### 物品交换时的解绑

`UIFixes.SwapPatches/SwapOperationRaiseEventsPatch` 挂在 `EFT.InventoryLogic.SwapResult.RaiseEvents` 上，两个物品交换成功后做一次检查：

```
对 Item 和 Item2 各执行：
  if (!controller.IsAtBindablePlace(x))
      controller.UnbindItemDirect(x, simulate: false)
        -> RaiseEvents(controller, 0) + RaiseEvents(controller, 1)
```

也就是：交换完，如果某件物品「不在可绑定位置」，UI FIX 会主动把它解绑。

这条逻辑的方向和原版一致（物品离开可绑定位置就该解绑），本模组只是**放宽了「可绑定位置」的判定范围**，所以它只会让这里的解绑**变少**，不会变多：

- 从背包拖药到快捷栏格子 → 绑定照常建立；
- 把快捷栏上的药和背包里的东西交换 → 按原版会被解绑，装上本模组后**保持绑定**（它仍处于「背包」这个可绑定来源中）。

如果你希望「东西一旦离开快捷栏就立刻解绑」（更接近原版严格语义），把配置 `2. Containers` 里对应的容器关掉即可。

### 键位方面

本模组**不注册任何新按键**，也不读写键位设置，走的是游戏原生的 `4`~`0`。所以不存在和 UI FIX 的 `[F. Item Keybinds]`（`I` / `O` / `T` / `R` / `U` / `F` / `A+Ctrl` 等）撞键的问题——那些是库存界面内鼠标悬停时的快捷键，和原生快捷栏不是一条路径。

### 和 BiC 同时装

`BiC - Bind in Container`（`com.fums.bindincontainer`）用的是同一套思路，也是 `if (__result) return;` 式 OR 合并，可以直接共存，效果取并集：

| | BiC | 本模组 |
|---|---|---|
| 安全箱 | ✅ | ✅ |
| 弹挂 | ✅ | ✅ |
| 口袋 | ✅ | ✅ |
| **背包** | ❌ | ✅ |
| 武器 | 默认允许 | 默认关闭（可开） |

如果两个都装，把本模组的 `Allow Weapons` 关着也没用——BiC 会放行武器。想让行为完全等于本模组，就把 BiC 移除。

---

## 6. Fika 联机（必须双端安装）

**先说结论：主机端（含 headless）必须安装同一份 `ContainerQuickBind.dll`，只装在客户端上没有用。**

### 为什么

Fika 里所有库存操作都是「客户端发描述符 → 主机校验 → 主机执行 → 广播结果」。绑定走的是这条链：

| 步骤 | 位置 | 发生了什么 |
|---|---|---|
| 1 | 客户端 | 拖拽到格子 → `BoundItemView.CanAccept` → `InventoryController.IsAtBindablePlace`，本模组在这里放行，所以拖得进去 |
| 2 | 客户端 | 打包成 `BindItemOperationDescriptor` 发给主机 |
| 3 | 主机 | `FikaServer.OnInventoryPacketReceived` → `IOperationHandler.CreateOperationFromDescriptor` |
| 4 | 主机 | `BindItemOperationDescriptor.ToInventoryOperation` → `BindResult.Run(controller, item, index, simulate: true)` |
| 5 | 主机 | `BindResult.Run` 第一行就是 `if (!controller.IsAtBindablePlace(item)) return "Cannot bind item";` ← **闸门在主机** |
| 6 | 主机 | 判定失败 → 回 `OperationCallbackPacket(EOperationStatus = 3)` + `ResyncInventoryIdPacket` |
| 7 | 客户端 | 日志出现 `Client operation rejected by server: N - OperationType: BindItemOperation` / `Reason: Cannot bind item`，绑定被回滚 |

第 5 步用的是原版判定：`FastAccessSlots = { Pockets, TacticalVest }` 且只向上看一层父级，所以背包 / 安全箱里的东西必然被判死。**主机上没装本模组，这一步就永远是原版结果，客户端怎么放宽都没用。**

### 怎么办

1. 把 `ContainerQuickBind.dll` 放到**主机**的 `BepInEx\plugins\` 下（headless 是同一套目录结构，也放在它的 `BepInEx\plugins\`）。
2. 重启主机与客户端。
3. 主机日志里必须能看到这两行，才算主机端真的生效了：

```
[Info :Container Quick Bind] [patch] BindablePlacePatch 已安装
[Info :Container Quick Bind] [patch] ReachablePlacePatch 已安装
```

单人模式（不经过 Fika 主机）本机既是主机，装一份就够，本节限制不适用。

### 相关日志对照

客户端看到：

```
[Error :Fika.Core] [ReceiveStatusFromServer]: <profileId> - Client operation rejected by server: 12 - OperationType: BindItemOperation, Owner: <profileId>
Reason: Cannot bind item
```

主机端看到：

```
[Error :Fika.Server] ItemControllerExecutePacket: Operation conversion failed: Cannot bind item
```

两条指向同一个原因：**主机端没有本模组**，或者主机端 `Require Examined` 为 `true` 而该物品在主机侧未被标记为已检视。

### 关于 Require Examined

`4. Behaviour / Require Examined` 的默认值已改为 `false`。联机时主机端的检视状态未必与客户端一致，开着会让主机把「客户端看着已检视」的物品判成未检视。单人想恢复原版语义可以再打开。

---

## 7. 实现说明

只有两个 Harmony Postfix，都在 `InventoryController` 上。

### `BindablePlacePatch`

目标：`bool InventoryController.IsAtBindablePlace(Item item)`

这个方法一处判定，三个地方生效：

| 调用点 | 作用 |
|---|---|
| `EFT.UI.DragAndDrop.BoundItemView.CanAccept` | 拖到快捷栏格子时，格子是否接收 |
| `EFT.InventoryLogic.BindResult.Run` | 真正执行绑定（含 `simulate` 预演） |
| `EFT.InventoryLogic.ItemManipulator.TryUnbindOneItem` | 解绑 |

所以打一处，拖拽、绑定、解绑全部打通。

放行条件（`ContainerSlotPolicy.CanBindFromContainer`）：

1. 类型在白名单内（按配置）；
2. 不是缺零件的复合物品（`CompoundItem.MissingVitalParts`）；
3. 已鉴定（按配置）；
4. **逐层向上遍历地址**，能走到「挂在 `Equipment` 上的某个槽位」，且该槽位是 `SecuredContainer` / `TacticalVest` / `Pockets` / `Backpack` 之一，且该槽位开关为开。

第 4 步替换掉原版的单层判定：

```csharp
ItemAddress address = item.CurrentAddress;
while (address != null) {
    IContainer container = address.Container;
    Item owner = container.ParentItem;
    if (container is Slot && ReferenceEquals(owner, equipment)) {
        // 到这里说明走到的就是 Equipment 上的槽位，比对四个候选槽
    }
    address = owner.CurrentAddress;
}
```

### `ReachablePlacePatch`

目标：`bool InventoryController.IsAtReachablePlace(Item item)`

两个调用点：

| 调用点 | 作用 |
|---|---|
| `EFT.Player.TryProceed` | 按快捷栏键时判断物品够不够得着；`false` 会 `SetFirstAvailableItem` 收手 |
| `EFT.Player.IsItemCanBeEquipped` | 判断能否装备 |

放行条件（`ContainerSlotPolicy.CanUseBoundItem`）**比绑定侧更严**，必须同时满足：

1. 类型在白名单内；
2. 不是缺零件的复合物品；
3. 已鉴定（按配置）；
4. **已被绑到某个 `EBoundItem` 格子**（遍历 `FastAccess.GetBoundItem(0..11)` 做引用比对）；
5. 位于允许的容器里，且该容器开关为开。

第 4 条是关键：不做「已绑定」的前置检查，等于把所有物品变成随手可得，仓库、尸体、地面的东西都能隔空使用——这是必须避免的。而原版另外还有一条 `Inventory.Stash != null && container == Inventory.Stash.Grid → false`，仓库物品本来就被挡掉，本模组不碰这条。

### 一个容易被忽略的点

`Player.SetQuickSlotItem`（按快捷栏键的入口）**内部并没有** `IsAtReachablePlace` 检查，可达性检查发生在它调用的 `Player.TryProceed` 里。所以只补 `ReachablePlacePatch` 这一个点就够了，不需要再去 patch `SetQuickSlotItem`。

---

## 8. 构建

本机没有 dotnet SDK，所以用 .NET Framework 自带的 csc（C# 5 语法上限，源码已按此编写）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -GameDir "E:\GAME"
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -GameDir "E:\GAME" -Deploy
```

产物 `dist/ContainerQuickBind.dll`。`-Deploy` 会直接复制到 `<GameDir>\BepInEx\plugins\`。

如果你有 dotnet SDK，也可以直接用 `ContainerQuickBind.csproj`（netstandard2.1）：

```powershell
dotnet build -c Release -p:GameDir="E:\GAME"
```

引用清单（`build.ps1` 内置，全部来自游戏目录）：

```
EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll
EscapeFromTarkov_Data\Managed\ItemComponent.Types.dll
EscapeFromTarkov_Data\Managed\ItemTemplate.Types.dll
EscapeFromTarkov_Data\Managed\Comfort.dll
EscapeFromTarkov_Data\Managed\Comfort.Unity.dll
EscapeFromTarkov_Data\Managed\DissonanceVoip.dll
EscapeFromTarkov_Data\Managed\UnityEngine.dll
EscapeFromTarkov_Data\Managed\UnityEngine.CoreModule.dll
EscapeFromTarkov_Data\Managed\UnityEngine.UI.dll
BepInEx\core\BepInEx.dll
BepInEx\core\0Harmony.dll
<ReferenceAssemblies>\v4.x\Facades\netstandard.dll
```

---

## 9. 已知限制

- **投掷物**：绑定后按快捷键是把它拿到手上（进入投掷准备），不会直接扔出去。这是原版 `TryProceed` 的分支行为，本模组不干预。
- **`Require Examined`**：安全箱里的药如果是没鉴定过的（捡来的），默认绑不上。把这一项关掉即可。
- **容器被卸下**：背包/弹挂被扔到地上或放进仓库后，里面的物品立即恢复为不可达，按快捷键无效——这是刻意的，避免隔空用药。
- **鼠标悬停不显示提示**：原版快捷栏格子的 tooltip 是按 `FastAccess` 里的内容生成的，容器物品绑定后仍能正常显示图标与名称，但没有「容器内」的标注。纯视觉细节，不影响功能。
- **不适用于仓库界面**：主菜单/仓库里的快捷栏属于 `OfflineInventoryController`，本模组的判定只依赖地址遍历，理论上也生效，但「使用」在非战局环境本来就没有意义。
- **和 UI FIX「用完自动重绑」串联后的表现**：快捷栏上的一盒药消耗殆尽后，UI FIX 会在下一帧自动从背包 / 安全箱取一盒**同 `TemplateId`** 的药填进同一个格子（见第 5 节）。所以连续治疗不用手动补绑；但也意味着**绑定目标会自动换成新物品**。不想串联就把 UI FIX 的 `Quickbind Matching Consumable After Use` 设为 `false`。
- **`ArmBand` 槽**：UI FIX 的重绑候选里含 `ArmBand`，本模组默认不把它算作可绑定来源（臂章槽本来就装不下药品，实际不会触发）。需要的话在 `CandidateSlots` 里加一项并重编译。

---

## 10. 排查

打开 `Verbose Logging`，然后看 `BepInEx\LogOutput.log`：

```
[bindable] 放行 [背包] 布洛芬 (Meds)
[bindable] 拒绝 [背包] 未知物品 (Item)
[reachable] 放行 [安全箱] 止血带 (Meds)
```

判定全为 `拒绝` 时，按顺序查：

1. `Allow Backpack` / `Allow Meds` 是否为 `true`；
2. `Require Examined` 是否拦住了没鉴定过的物品；
3. 物品类型是不是不在白名单（例如武器默认关）；
4. 有没有别的模组在同样两个方法上把 `__result` 直接赋值成 `false`（覆盖式写法），那种会和本模组打架。用 `tools/harmonyscan.ps1` 扫一下插件目录即可确认。

启动日志里还会有这一段，确认 patch 都装上了：

```
[patch] BindablePlacePatch 已安装
[patch] ReachablePlacePatch 已安装
```
---

## 11. 许可

MIT，见 [LICENSE](LICENSE)。

设计上参考了 [BiC - Bind in Container](https://github.com/MrFums/BindInContainer)（MIT）的 `if (__result) return;` OR 合并思路，代码为独立实现，额外支持背包、逐层地址遍历与 UI FIX 协同判定。
