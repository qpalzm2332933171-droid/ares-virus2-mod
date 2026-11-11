# 全游戏源码级分析（Assembly-CSharp 层）

> 数据源：`D:\arsbd\src_real\Assembly-CSharp\`，3,714 个 .cs，333,442 行，4,664 个类型，17,414 个方法（统计见 `docs/source_index.csv`）。
> 性质：从热更 DLL 反编译得到的完整托管层源码，符号未被混淆；Unity 引擎与系统库（AOT）不在本目录。
> 结论只覆盖 C# 托管层；libil2cpp、易盾壳、native 校验需配合 IDA，见第七节边界。

## 一、运行时骨架

| 层 | 入口 | 说明 |
|---|---|---|
| 全局单例 | `CGameSystem.Instance` | 53 个子系统属性（`CGameSystem.cs`）：entity / property / material / container / clock / buff / tips / exchange / scene / fight / task / character / equipment / antiCheat / pay 等 |
| 全局客户端 | `CClientGlobal.Instance` | 配置中心 `GetGameDataCenter`、事件系统 `GetEventSystem` |
| 场景与实体 | `CGameSceneMgr` | 实体表 `AddEntity/GetEntity`、当前角色 `GetCurrentCharacterEntity()`（L458）、`GetCurrentChararcterCustomID()`（L449，原拼写如此） |
| 角色组件 | `CEntity` + `*Part` | `CPropertyPart` / `CLifePart` / `CBuffPart` / `CFightObj` / `CContainerSystem` 等 |
| 配置 | `*ConfigLoader` | `CMaterialConfigLoader`、`CBuffConfigLoader`、`CEquipmentConfigLoader` 等，按 `eGameConfigType` 分域 |
| 存档 | `sDBGameWorld` / `DDBContent` | `CContainerSystem.LoadFromDB/SaveToDB`、`CMaterialSystem.LoadFromDB/SaveToDB` |

`CGameSystem` 关键子系统（全部为公开属性）：`entitySystem`、`propertySystem`、`materialSystem`、`containerSystem`、`clockSystem`、`buffSystem`、`equipmentSystem`、`characterSystem`、`antiCheatSystem`、`paySystem`、`userLogSystem`。

## 二、功能一：物品发放（源码级）

调用链（全部为公开方法，可直接反射调用）：

```
CContainerSystem.AddMaterialToContainer(int nCustomID, eContainerTypeEnum eType,
                                        int nMaterialConfigID, int nNum, bool bFireEvent = true)
  └─ CMaterialConfigLoader.GetConfig(nMaterialConfigID)        // 无效 ID 直接 return
  └─ ConvertContainerType(eType, config.eType)                 // Bag 自动改道 Blueprint/Map/EXP/Wallet
  └─ CContainerFactory.Create(eType, nCustomID)                // 容器不存在则建
  └─ 可堆叠分支：CMaterialSystem.CreateMaterial(id, nNum) → CContainer.AddMaterial(uid)
  └─ 不可堆叠/有耐久分支：循环 CreateMaterial(id, 1) → AddMaterial → RefreshContainer
  └─ 事件 eMSG.EVENT.Container_Change
```

关键文件与行号：

| 成员 | 位置 |
|---|---|
| `AddMaterialToContainer/5`（按配置 ID 发） | `CContainerSystem.cs:144` |
| `AddMaterialToContainer/4`（按 UID 发） | `CContainerSystem.cs:94` |
| `RemoveMaterialFromContainer/5` | `CContainerSystem.cs:253` |
| `GetMaterialNumInContainer` | `CContainerSystem.cs:349` |
| `CMaterialSystem.CreateMaterial` | `CMaterialSystem.cs:19` |
| `CMaterial.SetNum` | `CMaterial.cs:48` |
| 容器枚举 | `eContainerTypeEnum.cs`：Bag=0 / Equipment=1 / Drop=2 / Wallet=3 / Sale=4 / Blueprint=6 / Map=7 / EXP=8 / MakePart=9 / Mount=10 |

源码级要点：

1. `CMaterial.SetNum` 里的 `inte` 是异或加密的整数（`m_nNumber = nNum` 走隐式转换），直接调方法即可，不要手改字段。
2. 可堆叠判定是 `!config.bCannotCombine && config.nEndurance == 0`；装备/工具走逐件创建分支，发 100 件就是 100 次 `CreateMaterial`，菜单需要限流。
3. 发蓝图/地图/经验/货币时传 `Bag` 会被 `ConvertContainerType` 自动改到对应容器，菜单不用自己判断。
4. 9 个付费黑名单 ID（8411/1450/7005/8403/1650/8400/7130/8170/7403）会触发功能七的上报，菜单应默认隐藏或标红。

## 三、功能二：属性修改与锁定

`CPropertyPart` 是属性唯一入口（每个实体挂一个）：

| 方法 | 位置 | 语义 |
|---|---|---|
| `GetValue(ePropValueType eType, int nKey, int nDefault = 0)` | `CPropertyPart.cs:20` | 读值；`nCustomID != 0` 时优先读存档节点 |
| `SetValue(ePropValueType eValue, int nKey, int nValue)` | `CPropertyPart.cs:51` | 写值；`bSaveToDB` 的节点同步写 `propertySystem` |
| `ChangeValue` / `ChangeValueWithInsert` | `CPropertyPart.cs:96/111` | 增量修改 |

`ePropValueType`：`Original=1`、`Gain=2`、`Percent=3`、`AfterCul=4`。最终值由四层计算，菜单默认应写 `Original`，显示 `AfterCul`。

全量属性键：50 个，见 `docs/PROPERTY_KEYS.md`（源 `DPropertyKey.cs`）。分组：

| 组 | key | 数量 |
|---|---|---:|
| 生命 | 1 上限 / 2 当前 | 2 |
| 特殊 | 51 声音半径 / 52 队友上限 | 2 |
| 移速 | 101–108 | 8 |
| 攻击 | 201–207、210、211、212、215 | 11 |
| 防御 | 301–305 | 5 |
| 体力 | 401–406 | 6 |
| 饥饿 | 411–413 | 3 |
| 精力 | 421–423 | 3 |
| 塔防 | 500–507 | 8 |
| 钓鱼/攻速 | 600、601 | 2 |

锁定做法：每帧（或 0.2 s 节流）对勾选的 key 重新 `SetValue(Original, key, 目标值)`；读当前值用 `GetValue(AfterCul, key)`，输入框默认值由此填充。

## 四、功能三：Buff 施加与持续时间

`CBuffPart` 公开接口：

| 方法 | 位置 |
|---|---|
| `AddBuff(int nBuffID, ulong triggerUID)` | `CBuffPart.cs:127` |
| `AddBuff(int nBuffID, CEntity triggerEntity)` | `CBuffPart.cs:133`（可传 `null`） |
| `CanAddBuff(int nBuffID, out bool bFlowTip)` | `CBuffPart.cs:101`（检查 `m_listBanBuffID`、前置 buff、`nMaxNum`） |
| `HaveBuff(int nBuffID)` | `CBuffPart.cs:301` |
| `RemoveBuff(int nBuffID)` | `CBuffPart.cs:242` |

持续时间模型（`sBuffConfig` + `CBuff`）：

- `sBuffConfig.TimeType`：`Infinite=0 / Once=1 / Cycle=2 / RealTime=3`。
- `sBuffConfig.nKeepCycle`：持续周期数。
- `CBuff.CheckEnd()`（`CBuff.cs:283`）在 `m_nCurCycle >= nKeepCycle` 时 `ReqRemoveBuff`。
- Cycle 走 `clockSystem.SubClockGap`，RealTime 走 `SubClockChange`（每 30 秒折算 1 cycle）。

自定义时长的可行做法（按侵入性排序）：

1. 施加前临时改 `sBuffConfig.nKeepCycle`（共享配置对象，会影响同 ID 的所有 buff，需在菜单里恢复或接受全局生效）。
2. 施加后通过 `CBuff.SetCurCycle`（private，`CBuff.cs:319`）重置计数，只能延长不能精确设定。
3. hook `CBuff.CheckEnd`，用菜单维护的「buffID → 到期时间」表替代原判定，最精确但需要 IL 补丁。

## 五、功能四/五/六：弹药、耐久、蓝图

| 功能 | 源码事实 | 实现 |
|---|---|---|
| 子弹不减 | `CEquipmentSystem.ReduceAmmo(ulong equipmentUID, int nCustomID_Character)`（L198）从背包扣 `pEquipmentConfig.nAmmo` 1 个 | 方法体改 `ret` = 不减；「反增」需在同一方法里改调 `AddMaterialToContainer(customID, Bag, nAmmo, +1)`，或 hook `RemoveMaterialFromContainer` 按 `nMaterialConfigID` 识别弹药 |
| 耐久锁定 | `CMaterial.SetEndurance(int, bool)`（L63）会 `Clamp(0, pConfig.nEndurance)` | 方法体改 `ret` 即锁定；注意 `CMaterial.Use` 与 `CEquipment` 子类共用该字段 |
| 全部蓝图 | `CContainerSystem.IsOwnBlueprint(int nCustomID, int nBlueprintID)`（L439）只查 `Blueprint` 容器内数量 > 0 | 恒 `true` 即解锁全部；65 条蓝图 `eType==7`，散落在 8 个 UI 子组，必须按 `eType` 筛，不能按 UI 组取；`IsOwnMap`（L453）同理可一并处理 |

## 六、功能七：反作弊源码清单

`CAntiCheatSystem`（`CAntiCheatSystem.cs`，261 行）是客户端唯一黑名单检查系统，调用点在 `CGameSceneMgr.cs:213`（每次进场景加载完成后）：

```
BlackListCheck()                        // L20
  ├─ CheckPayItem()                     // L28  付费物品，nMaterielId 9 个，nPackageOwn 判定
  ├─ CheckRaffleItem()                  // L80  抽奖物品，userLog 无来源即上报
  ├─ CheckPointItem()                   // L94  积分物品
  ├─ CheckOtherItem()                   // L108 其他物品，num > nNumMin
  └─ CheckImportantItem()               // L122 重要物品
       └─ RequestExceptionUserIDV1      // L187 cmd=214（新版）
       └─ RequestExceptionUserID        // L136 cmd=44（旧版）
            └─ UploadExceptionUserLog() // L149 cmd=45，打包本地 DB 上传
```

上报字段：`lUserID`、`MD5(userID + 固定盐 "3j5k2b1n7m8s4f6d5e3r2t1z")`、`eBlackItemType + materialId + name + num`。本地 DB 路径由 `CFilePathUtil.GetDBPath(userID)` 决定。

源码级结论：这 5 个函数 + 2 个上报入口 + 1 个上传入口就是 C# 层全部黑名单逻辑；全部替换为 `return false` / `ret` 即可阻断。服务端另有校验（见第七节），C# 层拦截不能替代服务端风控。

## 七、边界与风险

1. **服务端校验不可绕**：物品数量、付费包、抽奖来源都会在服务端二次核对；客户端拦截只保证「不上报」，不保证「不封号」。
2. **native 层**：易盾壳、libil2cpp 完整性校验、调试检测不在 C# 源码内，需 IDA 分析；模块自身要避免被 `ptrace`/maps 扫描命中。
3. **热更 vs AOT**：`CContainerSystem`/`CBuffPart` 等方法在热更 DLL 内，`methodPointer` 指向解释器桩；`CMaterialSystem` 等是否 AOT 需按 `dump_klass_methods` 实测确认。
4. **线程**：`AddMaterialToContainer`/`AddBuff` 必须在 Unity 主线程调用，native 侧需 `ProbeOnMain` 排队。
5. **存档一致性**：发放后触发 `Container_Change`，自动保存协程会写盘；大批量发放要分批，避免单帧卡死。
6. **配置共享**：改 `sBuffConfig.nKeepCycle` 是全局生效，多 buff 同时改会互相污染。

## 八、产物

| 文件 | 内容 |
|---|---|
| `docs/source_index.csv` | 3,714 文件 × 行数/方法数/类型 |
| `docs/PROPERTY_KEYS.md` | 50 个属性键全表 |
| `docs/GAME_SOURCE_ANALYSIS.md` | 本文件 |
| `docs/ANTICHEAT_MAP.md` | 反作弊逐方法清单 |
