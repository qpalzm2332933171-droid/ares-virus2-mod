# 阿瑞斯病毒2 (Ares Virus 2) — Vector / LSPosed 全功能内置修改器

针对《阿瑞斯病毒2》（目标版本：`v2.2.13` 热更新，**全渠道/官服通用**：兼容官服 `com.zzonegame.aresvirus2.android`、OPPO 渠道服 `.nearme.gamecenter`、vivo `.vivo`、小米 `.mi`、九游/UC `.aligames`/`.uc`、B站 `.bilibili`、华为 `.huawei`、4399 `.m4399`、TapTap、好游快爆、应用宝等全部包名）打造的 **Vector / LSPosed 模块 (`com.android.support`)**。

全部功能均直接通过 **HybridCLR 热更原位 IL 补丁 (`arsmod.dll`) + 独立托管桥接 (`ModBridge.dll`) + Native 共享内存 IPC (`libmodmenu.so`)** 调用游戏原生方法与数据结构实现，不修改本地加密存档文件。

---

## 核心特性一览

### 1. 🎒 全物品动态选择器（背包 / 仓库）
- **4–5 列紧凑图标网格**：默认仅展示图标、名称与 `#ID`，彻底告别翻页疲劳；**长按任意物品卡片**弹出游戏富文本详情弹窗（完整渲染 `<color=#...>` / `<R>` / `<CB>` / `<CG>` 等原版着色说明，支持一键 `发 x1` / `发 x999` / `选中`）。
- **游戏原生分类与动态热更适配**：按 `UIBelongType` 5 大类 / `UIBelongSubType`（含 `42 饮品小吃`、`43 海鱼`、`44 家园装饰` 等）及 `Priority` 降序归类；每次进入游戏自动扫描运行期 `CMaterialConfigLoader` 与图集，新版本热更新增物品与图标自动导出至宿主 `files/dyn_items.json` 与 `files/dyn_icons/` 并实时显示。
- **高效交互**：支持 `1` / `999` 数量预设、自定义数值、当前过滤列表一键 **`全选`** / **`清空`**、搜索名称或 ID、背包与家园储物箱目标切换。

### 2. 📊 全人物 50 项属性修改 & 锁定 & 自动恢复
- 完整读取/修改/冻结 `DPropertyKey` 全部 **50 项属性**（分为 7 组：`生命(2)` / `战斗输出(12)` / `移动速度(8)` / `防御护盾(5)` / `体/饥/精(12)` / `声音/辅助(3)` / `塔防(8)`）。
- 自动联动解除 `DGameConfig.nMaxMoveSpeed` 移速上限钳制并同步血量/护盾/格挡 UI；输入框默认显示当前实时值。
- **属性配置持久化**：支持在「其他功能」点击 **`保存配置`** 将已修改数值与冻结状态持久化，**下次重进游戏进入存档时自动分批恢复加载**。

### 3. ✨ 全 Buff 多选施加与精确控时
- 支持游戏底层全部 **472+ 种 Buff**（动态扫描热更增量，修正 `BuffJudge == -1` 负面分类），支持名称/ID/效果搜索及 `全部 / 增益+ / 负面- / 中性·` 筛选。
- 通过托管帧心跳重置 `CBuff.m_nCurCycle` 配合真实到期时间销毁，支持自定义持续秒数与 `永久` 模式及一键清除 Mod Buff。

### 4. 🛠️ 其他核心功能面板（3×3 等高卡片 + 机关枪专栏）
- **🔒 全链路反作弊保护（默认开启）**：
  - **IL 层 (`arsmod.dll`)**：拦截 `CAntiCheatSystem` 全部 11 个作弊检测/黑名单扫描/异常日志上报入口，并在 `CNetworkMgr.SendRequest` 拦截作弊与封号协议号 `30, 44, 45, 214`。
  - **Java / Native 层 (`XposedEntry.java`)**：拦截 `HTProtect`（`init` / `getToken` / `getTokenAsync` / `ioctl` / `safeComm*`）、`HTProjectSerRoleInfo`、`fetchToken_Login`、`RequestGetAntiCheatToken`、`UnitySendMessage` 及作弊检测调用栈自杀。
- **🔫 子弹不减反增**：改写 `CEquipmentSystem.ReduceAmmo`，远程武器开火消耗弹药反而 `+1`（可随时开关恢复）。
- **🛡️ 锁定装备耐久 + 一键补满**：改写 `CMaterial.SetEndurance` 冻结全部武器、防具、载具及限时道具耐久；另提供「补满耐久」按钮一键回满。
- **⚡ 关闭交互时间**：改写 `CUI_Game_Input_MainCharacter.OnOperatePointDown` / `Car.OnOperatePointDown` 并清零 `fInputBackhomeTime`，拾取/采集/砍伐/回家 **0 秒瞬时完成**（关闭立即恢复原耗时）。
- **📦 无限堆叠（仓库 / 我的物品）**：同时在 `CContainer.RefreshContainer`、`CContainerSystem.AddMaterialToContainer` 与 `CHomeContainer.FindMergeTarget` 绕过 `bCannotCombine` 与 `nEndurance != 0` 限制，并在开启瞬间自动合并现有拆分物品（如 `战术背心` 等装备/道具无上限单格堆叠）。
- **👻 穿墙模式**：动态将主角所有非触发器 `Collider2D.isTrigger` 设为 `true`，自由穿过障碍物与墙体（关闭即还原原始碰撞体状态）。
- **📜 一键解锁全部蓝图**：静默解锁全部 `65+` 工作台/装备/建筑蓝图（无连环弹窗卡顿）。
- **💎 解锁全部付费道具**：本地层完整接管 `CPaySystem`（商城礼包/钻石包/限时礼包）、`CDLCSystem.GetDLCInstallInfoByID`、`CHolidayImportantExchangeConfigLoader`、付费天赋 (`1400/1500`) 及 `Pay_*` 购买事件，本地直接视为已购买（猎人小屋、哈士奇、猪王坐骑、付费 DLC 等全场景可用，支持切换还原）。
- **🔥 机关枪模式**：在 `CMainCharacter.OnInputCallback` 针对全部 **12 类蓄力远程武器行为**（掩码 `0x7e1cc210`：`4,9,14,15,18,19,20,25..30`，**近战刀棍不受影响**）将松手释放改写为 `Cancel` 并由 `ModBridge` 轮询连发；支持 **`1.0 ~ 60.0 发/秒`** 无级滑块与 `5/s ~ 60/s` 快捷预设，**按住右攻击轮盘持续连射，松手即停**。

### 5. 🎨 现代化流畅控制台与双端兼容优化
- **零卡顿低端机优化**：全列表采用 ViewHolder 复用 `ListView` + 双线程异步 `LruCache(384)` 图标加载；启动无多余欢迎弹窗或 Toast 刷屏。
- **真隐身与防裁剪悬浮球**：基于 `FrameLayout.LayoutParams` 自适应边距定位并禁用重叠裁剪，支持在 **ARM64 真机与 MuMu 等 x86_64 (`libhoudini`) 模拟器** 下流畅拖拽不缺角、不遮挡游戏触控；点击顶栏 **`隐藏`** 可完全隐形至 `0.0f`，点击原位置随时唤回。
- **双端统一稳定挂载**：将原生探测迁移至 `UnityPlayer.nativeRender`（`UnityMain` 线程），加入 `libhoudini` 下 `r--p` 代码页识别、延迟 `Class::Init` 与 `ModBridge` 自动重试，避免模拟器假死及长时间停留「等待热更与场景...」。
- **无桌面图标**：移除 Android 桌面 `LAUNCHER` 入口（仅保留 `MODULE_SETTINGS`），不在桌面生成多余图标。
- **5 套主题风格一键切换并记忆**：`极客赛博蓝`、`暗夜绯红`、`翡翠森境`、`琥珀黑金`、`晨曦极简白`（同步重绘悬浮球外框与配色）。

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
3. 打开 **LSPosed / Vector 管理器** → 模块列表启用 `AresModMenu`（`com.android.support`）→ 勾选你安装的《阿瑞斯病毒2》（模块已内置 `@array/xposed_scope` 自动匹配官服 `com.zzonegame.aresvirus2.android`、OPPO、vivo、小米、九游、B站、华为、4399、TapTap、好游快爆、应用宝等主流渠道服，亦支持手动勾选任意其他渠道服包名）。
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
