# 反作弊逐方法清单（C# 层）

源文件：`src_real/Assembly-CSharp/CAntiCheatSystem.cs`（261 行）

| # | 方法 | 行 | 触发条件 | 上报 |
|---|---:|---|---|---|
| 1 | `BlackListCheck` | 20 | 进场景加载完成后调用一次（`CGameSceneMgr.cs:213`） | 汇总入口 |
| 2 | `CheckPayItem` | 28 | 背包内 9 个付费 ID 数量 > 0 且 `paySystem.IsPackageOwn` 为假 | `RequestExceptionUserID` cmd 44 |
| 3 | `CheckRaffleItem` | 80 | 抽奖物品数量 > 0 且 `userLog` 无来源记录 | cmd 44 |
| 4 | `CheckPointItem` | 94 | 积分物品数量 > 0 且无来源记录 | cmd 44 |
| 5 | `CheckOtherItem` | 108 | 数量 > `nNumMin` | cmd 44 |
| 6 | `CheckImportantItem` | 122 | 数量 > `nNumMin` | `RequestExceptionUserIDV1` cmd 214 |
| 7 | `RequestExceptionUserID` | 136 | 内部上报（旧协议） | `DNetwork` cmd 44 |
| 8 | `UploadExceptionUserLog` | 149 | 服务端回包 `nCommand == 1` 后触发 | cmd 45，上传 DBZip |
| 9 | `RequestExceptionUserIDV1` | 187 | 内部上报（新协议，带 MD5 盐） | cmd 214 |
| 10 | `OnHttpRequestSucc` | 208 | 44/214 回包处理 | 触发 45 |
| 11 | `CheckPaidMaterialById` | 251 | 单个物品付费校验 | 返回 bool，不直接上报 |
| 12 | `CheckOffendingMaterialById` | 261 | 违禁物品校验 | 当前恒 `true` |

固定盐：`3j5k2b1n7m8s4f6d5e3r2t1z`（出现在 44/214 两条链）。

旁路观察点：

- `CUI_Game_Report`：玩家举报 UI，不是自动反作弊。
- `CFSMFunction_JudgeCheat`：空实现（`CheckState()` 无方法体），无实际作用。
- `CUnityMethodReceiver` / `CGameDataCenter` 中的 `Cheat` 命中为配置枚举，不是检测逻辑。

拦截建议（按稳定性）：

1. `BlackListCheck` 整体 `ret`（一次性覆盖 5 个检查）。
2. 若只想拦上报：`RequestExceptionUserID` / `RequestExceptionUserIDV1` / `UploadExceptionUserLog` 三个方法体改 `ret`。
3. `CheckOffendingMaterialById` 已恒 `true`，无需处理。
