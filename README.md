# 阿瑞斯病毒2 (Ares Virus 2) — Vector / LSPosed 全功能内置修改器

针对《阿瑞斯病毒2》（目标版本：`v2.2.13` 热更新，**全渠道/官服通用**：兼容官服 `com.zzonegame.aresvirus2.android`、OPPO 渠道服 `.nearme.gamecenter`、vivo `.vivo`、小米 `.mi`、九游/UC `.aligames`/`.uc`、B站 `.bilibili`、华为 `.huawei`、4399 `.m4399`、TapTap、好游快爆、应用宝等全部包名）打造的 **Vector / LSPosed 模块 (`com.android.support`)**。

全部功能均直接通过 **HybridCLR 热更原位 IL 补丁 (`arsmod.dll`) + 独立托管桥接 (`ModBridge.dll`) + Native 共享内存 IPC (`libmodmenu.so`)** 调用游戏原生方法与数据结构实现，不修改本地加密存档文件。

---

## 核心特性一览

### 1. 🎒 全物品动态选择器（背包 / 仓库）
- **4–5 列紧凑图标网格**：默认仅展示图标、名称与 `#ID`，彻底告别翻页疲劳；**长按任意物品卡片**弹出游戏富文本详情弹窗（完整渲染 `<color=#...>` / `<R>` / `<CB>` / `<CG>` 等原版着色说明，支持一键 `发 x1` / `发 x999` / `选中`）。
- **游戏原生分类与动态热更适配**：按 `UIBelongType` 5 大类 / `UIBelongSubType`（含 `42 饮品小吃`、`43 海鱼`、`44 家园装饰` 等）及 `Priority` 降序归类；每次进入游戏自动扫描运行期 `CMaterialConfigLoader` 与图集，新版本热更新增物品与图标自动导出至宿主 `files/dyn_items.json` 与 `files/dyn_icons/` 并实时显示。
- **高效交互**：支持 `1` / `999` 数量预设、自定义数值、当前过滤列表一键 **`全选`** / **`清空`**、搜索名称或 ID、背包与家园储物箱目标切换；内置无上限本地待发暂存队列（自动高帧率泵送至 `ModShm` 共享内存环）、逐项异常隔离与合批延迟刷新（避免蓝图/地图连环弹窗卡顿及 `RefreshContainer` 阻塞），全选「`3 物资材料`（523 项）」甚至全物品库（`1,349` 项）亦可一次性完整生效、不丢件、不因单项异常中断后续队列。

### 2. 🗑️ 仓库物品删除专栏（原生 CHomeContainer）
- **实时扫描仓库物品**：自动解析并导出全部家园储物箱内物品到 `dyn_warehouse.json`，展示物品图标、名称、耐久度、所属箱子 ID 及当前堆叠数量。
- **箱子筛选与实时搜索**：支持一键按单箱（`全部箱子` / `箱子 #1` / `箱子 #2` ...）快速定位物品，支持名称与 ID 模糊检索。
- **精细化步进删除**：点击任意物品呼出删除弹窗，提供 `-10`、`-1`、`+1`、`+10`、`全部` 快捷步进按钮或自由数字输入，调用原生 `CHomeContainer` 与 `CMaterialSystem` 精准移除指定数量物品并同步界面。

### 3. 💎 货币修改独立专栏（原生 CPaySystem）
- **四大核心货币/额度实时同步修改**：
  - 💎 **钻石 (Diamond)** (`SetDiamondNum`)
  - 🪙 **游园币 (Garden Coin)** (`SetGardenCoinNum`)
  - 🎟️ **游园积分 (Garden Point)** (`SetGardenPointNum`)
  - 💰 **累计充值额度 (Cumulative Payment)** (`SetCumulativePayment`)
- **快捷与自定义设定**：每项货币均提供 `+1,000`、`+10,000`、`+100,000`、`设为 999,999`（充值额度为 `+648`、`+6,480`、`+64,800`、`99,999`）快捷修改与自定义数值设定/增量按钮，存档与游戏商城实时生效。

### 4. 📊 全人物 50 项属性修改 & 锁定 & 自动恢复 & 配置删除
- 完整读取/修改/冻结 `DPropertyKey` 全部 **50 项属性**（分为 7 组：`生命(2)` / `战斗输出(12)` / `移动速度(8)` / `防御护盾(5)` / `体/饥/精(12)` / `声音/辅助(3)` / `塔防(8)`）。
- 自动联动解除 `DGameConfig.nMaxMoveSpeed` 移速上限钳制并同步血量/护盾/格挡 UI；输入框默认显示当前实时值。
- **配置持久化与删除**：顶栏直观新增 **`保存配置`** 与 **`删除已存配置`** 按钮，既可一键将已修改属性持久化以便重进存档自动恢复，也可随时一键彻底清空已存配置。

### 5. ✨ 全 Buff 多选施加与永久保持加固
- 支持游戏底层全部 **472+ 种 Buff**，支持名称/ID/效果搜索及 `全部 / 增益+ / 负面- / 中性·` 筛选。
- **永久 Buff 加固防掉机制**：针对用户选择「永久」的 Buff，通过底层循环心跳将其周期强制设为 `-999999`，即使受击、过图、跨天、时间流逝或游戏机制触发移除，ModBridge 均会实时自动补加，彻底解决永久 Buff 过一阵消失的问题。

### 6. 🛠️ 其他核心功能（UI 全面轻量化为开关行 + 仅机关枪保留卡片调速）
- **全新扁平化选项行设计**：除机关枪模式保留卡片与调速滑条外，其余全部功能均转换为简洁的原生水平开关行（`Switch`），布局紧凑清爽。
- **🔒 全链路反作弊保护（默认开启）**：IL + Java 全套拦截作弊检测/黑名单扫描/上报与封号协议号。
- **🔫 子弹不减反增**：远程武器开火消耗弹药反而 `+1`。
- **🛡️ 锁定装备耐久 + 一键补满**：冻结全部武器/防具耐久，附带「补满耐久」按钮。
- **⚡ 关闭交互时间（深度覆盖全进度条操作）**：不仅包括拾取/采集/开箱/回家，还深入覆盖 **砍树、采矿、砍伐家具** 等需要斧头、镐子、电锯、凿岩机、扳手、螺丝刀的逐步进度条操作，全部 **0 秒瞬间完成**！
- **📦 仓库无限堆叠（精细化控制）**：**严格限制仅在仓库储物箱生效**，背包装备/掉落物/售卖容器均恢复原版限制；在仓库内**严格按相同 NBT / 耐久度**堆叠，未经改造与改造过的武器装备绝不混叠。
- **👻 穿墙模式**：关闭主角实体碰撞体，自由穿行任何障碍与地图边界。
- **💡 下水道/洞穴全图高亮**：接管昼夜与暗光系统（`CDayAndNightSceneDisplay`），下水道、矿洞等全黑区域进入即全图持续高亮，无需头灯与照明棒。
- **🎪 限时活动全开放**：接管 `CActivator_Weekend` 与活动总览配置，周末、节假日及限时专属活动与小游戏商人全天候常驻开放。
- **📻 留声机播放本地音乐**：家园留声机（`CPhonograph`）自动扫描 `/sdcard/Music` 与 `/sdcard/AresMod/Music` 目录下的 `.mp3` 音乐并注入曲库，支持原生播放本地自选歌曲。
- **💎 一键解锁全部付费道具**：本地解锁猎人小屋、哈士奇、猪王坐骑、商城礼包及 DLC。
- **📜 一键解锁全部蓝图**：一键静默解锁全部 65+ 工作台/武器/防具/建筑蓝图。
- **💾 属性配置持久化**：开关进存档自动加载，配套「保存配置」与「删除已存配置」快捷操作。
- **🔥 机关枪模式**：独家保留微调卡片，支持 `1.0 ~ 60.0 发/秒` 无级调速滑条与 `5/s ~ 60/s` 快捷档位，按住右攻击轮盘持续连射，松手即停。

### 7. ⚔️ 副本 / 局域网联机 / 单人挑战全面双引擎兼容
- 针对游戏副本、挑战及局域网联机采用 `LAN.CLanMainCharacter.localPlayer` / `CLanEntity` 独立架构的特性，实现 **双引擎无缝派发**：
- 副本内 **全部 50 项属性修改与锁定、Buff 施加与永久保持、物品发放、瞬时交互、穿墙模式、机关枪连发** 完全正常生效，彻底解决副本功能失灵问题。

### 8. 🎨 5 套主题风格与诊断设置
- 支持 `极客赛博蓝`、`暗夜绯红`、`翡翠森境`、`琥珀黑金`、`晨曦极简白` 实时换肤与悬浮球自适应重绘。
- 诊断设置内置动态配表/图标重扫、快照刷新与悬浮球一键复位。

---

## 工程目录结构

```text
.
├── AresModMenu/                        # Vector / LSPosed 模块主工程
│   ├── app/src/main/
│   │   ├── AndroidManifest.xml
│   │   ├── assets/
│   │   │   ├── arsmod.dll              # 经 NHTP 保护重加密的 v2.2.13 补丁程序集
│   │   │   ├── ModBridge.dll           # 独立托管逻辑桥接程序集
│   │   │   ├── xposed_init             # Xposed 入口声明 (com.android.support.XposedEntry)
│   │   │   └── items/                  # 内置基础物品/Buff配表与图标缓存
│   │   ├── java/com/android/support/
│   │   │   ├── XposedEntry.java        # Vector Hook 入口：资源同步、Assembly.Load 替换、Java 反作弊
│   │   │   ├── Menu.java               # 悬浮球 / 真隐身 / WindowManager 管理与 JNI 通信
│   │   │   ├── ModConsole.java         # 6 栏控制台 UI、主题引擎、持久化属性自动恢复与异步图标缓存
│   │   │   └── ...                     # Preferences / OverlayAttach / ZOrderFix 等辅助类
│   │   ├── jni/
│   │   │   ├── mod_shm.h               # Native ↔ C# 双向共享内存结构布局 (ModShm)
│   │   │   ├── il2cpp_bridge.cpp       # IL2CPP / HybridCLR 解析、Hook、Bridge 注入与加载
│   │   │   ├── Main.cpp                # JNI NativeQuery(op=1..13) 网关与状态快照序列化
│   │   │   ├── Menu/Menu.cpp           # 控制台挂载与生命周期控制
│   │   │   └── Dobby / KittyMemory / xDL
│   │   └── libs/arm64-v8a/libmodmenu.so
│   ├── bridge/
│   │   ├── ModBridge.cs                # 运行期 MonoBehaviour 心跳与全游戏原生调用实现
│   │   └── stubs/                      # ModBridge 编译所需的最小精简 Unity 引用桩源码
│   ├── stubs/                          # Xposed API 编译桩
│   ├── build_native.cmd                # Windows 一键编译 libmodmenu.so (NDK r27d)
│   └── build_apk.sh                    # 无 Gradle 轻量打包与签名生成 AresModMenu.apk
├── scripts/                            # 逆向解析与离线补丁脚本
│   ├── nhtp_decrypt.py                 # NHTP 热更 DLL 块类型分析与解密
│   ├── nhtp_encrypt.py                 # NHTP 零 C-block 冲突保块重加密工具
│   ├── patch_assembly_v2213.py         # v2.2.13 IL 补丁、Metadata 提权与 verify_il 校验器
│   ├── build_modbridge.ps1             # C# .NET Framework csc.exe 编译 ModBridge.dll 脚本
│   ├── extract_dll.py / extract_cfg.py / parse_*.py / export_icons*.py
├── docs/                               # 逆向分析文档与协议/属性映射表
│   ├── ANTICHEAT_MAP.md
│   ├── CODEBASE_MAP.md
│   ├── GAME_SOURCE_ANALYSIS.md
│   ├── PROPERTY_KEYS.md
│   └── source_index.csv
├── LICENSE                             # GNU General Public License v3.0 (GPL-3.0)
└── README.md
```

---

## 安装与使用（真机 / 模拟器通用）

1. 前往 **[Releases](../../releases)** 下载最新的 `AresModMenu.apk`。
2. 手机或模拟器（支持 MuMu 12 等 x86_64 + `libhoudini` 转译环境及 ARM64 真机）需已配置 **Vector / LSPosed**（推荐 KernelSU + Vector），安装 `AresModMenu.apk`：
   ```bash
   adb install --no-incremental -r AresModMenu.apk
   ```
   > 注意：Android 16 等高版本请勿省略 `--no-incremental`，避免增量安装引起作用域失效。模块已隐藏桌面启动图标，安装后不会在桌面生成多余入口。
3. 打开 **LSPosed / Vector 管理器** → 模块列表启用 `AresModMenu`（`com.android.support`）→ 勾选你安装的《阿瑞斯病毒2》（模块已内置 `@array/xposed_scope` 自动匹配官服 `com.zzonegame.aresvirus2.android`、OPPO、vivo、小米、九游、B站、华为、4399、TapTap、好游快爆、应用宝等主流渠道服，亦支持手动勾选任意其他渠道服包名）。若设备上使用了 **隐藏应用列表（HideMyApplist）**，请记得同时为对应渠道包名启用白名单/作用域隔离。
4. 强行停止并启动游戏，点击屏幕左上角 `"MOD"` 悬浮球即可呼出控制台（支持自由拖拽位置；如点击「隐藏」，点击原悬浮球位置即可重新呼出）。

---

## 从源码构建（开发者）

### 环境依赖
- **Android SDK Build-Tools**（`aapt2`、`d8`、`zipalign`、`apksigner`，脚本默认 `SDK=/d/Android/AndroidSDK`、`build-tools/36.0.0`）
- **JDK 11+**、`bash`（Git Bash / MSYS2）、`zip`
- **Android NDK r27d**（可选，仅修改 `jni/` C++ 代码后重新编译 `.so` 时需要，默认路径 `tools/ndk_extract/android-ndk-r27d`）
- **Python 3 + `dnfile`** + **.NET `csc.exe`**（可选，仅重新修补热更 DLL / 重编 `ModBridge.dll` 时需要）

### 构建步骤

```powershell
# 1. （可选）重新生成 arsmod.dll 与 Publicized Assembly-CSharp 引用
python scripts/patch_assembly_v2213.py --dec-in <path\Assembly-CSharp.dec.dll> --enc-orig <path\Assembly-CSharp.dll>

# 2. （可选）重新编译托管桥接 ModBridge.dll
powershell -ExecutionPolicy Bypass -File scripts/build_modbridge.ps1 -GameManagedDir <path\managed_2213>

# 3. 修改 C++ (jni/) 后必须先重新编译原生库 libmodmenu.so (arm64-v8a)
cmd /c AresModMenu\build_native.cmd
```

```bash
# 4. 打包并签名最终模块 APK（输出 AresModMenu/build/AresModMenu.apk，直接复用上述 .so，不会自动运行 NDK）
bash AresModMenu/build_apk.sh
```

---

## 免责声明

本项目仅供 **Android 逆向工程、HybridCLR / IL2CPP 虚拟化机制及客户端安全防护技术研究与学习** 使用。请勿用于任何破坏多人联机公平性、商业破解或侵犯原开发者合法权益的用途；因使用本项目产生的任何账号或法律风险由使用者自行承担。

---

## 开源协议 (License)

本项目基于 **[GNU General Public License v3.0 (GPL-3.0)](LICENSE)** 协议开源。
