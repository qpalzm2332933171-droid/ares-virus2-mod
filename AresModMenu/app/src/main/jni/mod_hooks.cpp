// mod_hooks.cpp —— 功能 4/5/6/7 的实现层
//
// ============================ 设计变更（2026-10-29 17:10，实测驱动） ============================
//
// 原实现用 DobbyHook(methodPointer)。两个致命前提都被真机推翻：
//
// ① mod_hooks 自己的 is_exec_ptr 用 dl_iterate_phdr 判 PF_X —— libil2cpp 是易盾 mmap 进来的，
//    不在 linker 的 soinfo 链表里，dl_iterate_phdr 枚举不到它，于是 12 个目标全判成 no-aot。
//    （已改为走 il2cpp_bridge::is_executable()，即 /proc/self/maps。）
//
// ② 更根本：**methodPointer 是「按签名共享」的桩，不是方法体**。
//    17:07 真机 dump CContainerSystem 的 22 个方法（dump_m）实测：
//        [2] LoadFromDB/1            mp rva=0xb77d3c   \
//        [3] SaveToDB/1              mp rva=0xb77d3c   /
//        [4] AddMaterialToContainer/4 rva=0xb89808  \
//        [5] AddMaterialToContainer/5 rva=0xb89808   |
//        [6] RemoveMaterialFromContainer/4 0xb89808  |  5 个方法共用一个地址
//        [7] RemoveMaterialFromContainer/5 0xb89808  |
//        [13] IsContainerHaveMaterial/3    0xb89808  /
//        [11][12][14][15] 共用 0xb71350；[16][19] 共用 0xb502f4；[17][18] 共用 0xb5e0c0
//    而且**全部 22 个方法的 invoker 完全相同**（0x…ecf0）——这是 HybridCLR 的统一解释器入口。
//    ⇒ 对这类方法：裸调 methodPointer 会崩（异常沿手工栈帧展开时炸在 libgcc 的 _Unwind_* 里，
//      也就是历史上反复出现的 libil2cpp+0xc7c44c）；走 invoker 会卡死（它把 mp 当
//      InterpMethodInfo* 解析）。两条路都不通，17:07 实测都复现了。
//
// ⇒ 改用「换 MethodInfo->methodPointer」：热更方法的 MethodInfo 由 HybridCLR 分配在托管堆
//    （0xb40000752f…），可写；改它**只影响这一个方法**，不受「按签名共享桩」连坐。
//    功能 4/5/6/7 全是短路型 —— 不需要调原函数，所以不碰任何 ABI。
//    开关 = 换指针 / 还原指针（关闭时我们的函数根本不会被调用，无需 orig thunk）。
//
// 参数约定（6.44 真机定论）：托管方法的 native 形参 = (this, 实参…, MethodInfo*)。
// 末尾那个 MethodInfo* 我们不读，但必须在签名里留出来，否则多余的实参会落到我们栈上。

#include "mod_hooks.h"

#include <cstdint>
#include <cstddef>
#include <cstring>
#include <cstdio>
#include <unistd.h>
#include <sys/mman.h>
#include <android/log.h>
#include <atomic>

#include "dobby.h"
#include "il2cpp_bridge.h"

#define HLOG_TAG "AresHooks"
#define HLOGI(...) ((void) __android_log_print(ANDROID_LOG_INFO,  HLOG_TAG, __VA_ARGS__))
#define HLOGE(...) ((void) __android_log_print(ANDROID_LOG_ERROR, HLOG_TAG, __VA_ARGS__))

namespace {

constexpr uintptr_t OFF_MI_METHOD_POINTER = 0x00;
constexpr uintptr_t OFF_MI_NAME           = 0x10;
constexpr uintptr_t OFF_KLASS_METHODS     = 0x98;
constexpr uintptr_t OFF_KLASS_METHOD_CNT  = 0x11c;

// ------------------------------------------------------------------ 替换体
// 全部不调原函数：功能关闭时指针已还原，本函数不会被调用；
// 万一因为竞态被调到，也只是「保持原行为一次」——对 4/5 无害，对 6 会多放行一次，可接受。

// 命中计数：用来证明「游戏真的走到我们的替换体」，比任何间接推断都硬。
std::atomic<int> g_hits[8];

// ---------------------------------------------------------------- 功能 2：属性锁定表
// CPropertyPart.GetValue(ePropValueType eType, int nKey, int nDefault) 是**转发桩**（rva 0xb50364），
// 所以「查不到锁定值」时可以安全地回落去调原桩 —— 这是功能 1/3 做不到的（那两个落在 0xb89808
// 抛异常桩上）。锁定表只由菜单线程写，读侧无锁读 n 与数组，够用。
constexpr int kMaxLock = 32;
std::atomic<int> g_lockN{0};
int32_t g_lockKey[kMaxLock];
int32_t g_lockVal[kMaxLock];
void*   o_GetValue = nullptr;      // 原 GetValue 的 methodPointer（转发桩，可裸调）

int32_t hk_GetValue(void* self, int32_t eType, int32_t nKey, int32_t nDefault, void* mi) {
    g_hits[2].fetch_add(1, std::memory_order_relaxed);
    int n = g_lockN.load(std::memory_order_relaxed);
    for (int i = 0; i < n && i < kMaxLock; ++i)
        if (g_lockKey[i] == nKey) return g_lockVal[i];
    if (o_GetValue) {
        typedef int32_t (*Fn)(void*, int32_t, int32_t, int32_t, void*);
        return ((Fn) o_GetValue)(self, eType, nKey, nDefault, mi);
    }
    return nDefault;
}

// 4 子弹不减：CEquipmentSystem.ReduceAmmo(ulong equipmentUID, int nCustomID_Character) void
void hk_ReduceAmmo(void* self, uint64_t uid, int32_t cid, void* mi) {
    (void) self; (void) uid; (void) cid; (void) mi;
    g_hits[4].fetch_add(1, std::memory_order_relaxed);
}

// 5 耐久锁定：CMaterial.SetEndurance(int nEndurance, bool bFireEvent) void
void hk_SetEndurance(void* self, int32_t v, bool fire, void* mi) {
    (void) self; (void) v; (void) fire; (void) mi;
    g_hits[5].fetch_add(1, std::memory_order_relaxed);
}

// 6 全部蓝图：CContainerSystem.IsOwnBlueprint(int nCustomID, int nBlueprintID) bool
bool hk_IsOwnBlueprint(void* self, int32_t cid, int32_t bp, void* mi) {
    (void) self; (void) cid; (void) bp; (void) mi;
    g_hits[6].fetch_add(1, std::memory_order_relaxed);
    return true;
}

// 7 反作弊：CAntiCheatSystem.BlackListCheck() void
void hk_BlackListCheck(void* self, void* mi) {
    (void) self; (void) mi;
    g_hits[7].fetch_add(1, std::memory_order_relaxed);
}

// 7 五个 Check*：语义是「命中黑名单返回 true」，放行必须 false
bool hk_CheckFalse(void* self, void* mi) {
    (void) self; (void) mi;
    g_hits[7].fetch_add(1, std::memory_order_relaxed);
    return false;
}

// 7 两个上报入口 + 上传日志：直接吞掉
void hk_Void0(void* self, void* mi) {
    (void) self; (void) mi;
    g_hits[7].fetch_add(1, std::memory_order_relaxed);
}

// ------------------------------------------------------------------ 目标表
enum State { ST_NONE = 0, ST_OK, ST_NOTFOUND, ST_SHARED, ST_WRITE, ST_ERR };

struct Target {
    int         feat;
    const char* klass;
    const char* method;
    int         nparams;
    void*       repl;
    const char* label;
    // 运行时
    void*   mi;        // MethodInfo*
    void*   savedMp;   // 原始 methodPointer
    bool    on;        // 当前是否已把指针换成 repl
    State   state;
};

Target g_targets[] = {
    { 2, "CPropertyPart",    "GetValue",          3, (void*) hk_GetValue,
      "F2/GetValue",          nullptr, nullptr, false, ST_NONE },
    { 4, "CEquipmentSystem", "ReduceAmmo",        2, (void*) hk_ReduceAmmo,
      "F4/ReduceAmmo",        nullptr, nullptr, false, ST_NONE },
    { 5, "CMaterial",        "SetEndurance",      2, (void*) hk_SetEndurance,
      "F5/SetEndurance",      nullptr, nullptr, false, ST_NONE },
    { 6, "CContainerSystem", "IsOwnBlueprint",    2, (void*) hk_IsOwnBlueprint,
      "F6/IsOwnBlueprint",    nullptr, nullptr, false, ST_NONE },
    { 7, "CAntiCheatSystem", "BlackListCheck",    0, (void*) hk_BlackListCheck,
      "F7/BlackListCheck",    nullptr, nullptr, false, ST_NONE },
    { 7, "CAntiCheatSystem", "CheckPayItem",      0, (void*) hk_CheckFalse,
      "F7/CheckPayItem",      nullptr, nullptr, false, ST_NONE },
    { 7, "CAntiCheatSystem", "CheckRaffleItem",   0, (void*) hk_CheckFalse,
      "F7/CheckRaffleItem",   nullptr, nullptr, false, ST_NONE },
    { 7, "CAntiCheatSystem", "CheckPointItem",    0, (void*) hk_CheckFalse,
      "F7/CheckPointItem",    nullptr, nullptr, false, ST_NONE },
    { 7, "CAntiCheatSystem", "CheckOtherItem",    0, (void*) hk_CheckFalse,
      "F7/CheckOtherItem",    nullptr, nullptr, false, ST_NONE },
    { 7, "CAntiCheatSystem", "CheckImportantItem",0, (void*) hk_CheckFalse,
      "F7/CheckImportantItem",nullptr, nullptr, false, ST_NONE },
    { 7, "CAntiCheatSystem", "RequestExceptionUserID",   4, (void*) hk_Void0,
      "F7/RequestExceptionUserID",   nullptr, nullptr, false, ST_NONE },
    { 7, "CAntiCheatSystem", "RequestExceptionUserIDV1", 4, (void*) hk_Void0,
      "F7/RequestExceptionUserIDV1", nullptr, nullptr, false, ST_NONE },
    { 7, "CAntiCheatSystem", "UploadExceptionUserLog",   0, (void*) hk_Void0,
      "F7/UploadExceptionUserLog",   nullptr, nullptr, false, ST_NONE },
};

constexpr int kTargetN = (int) (sizeof g_targets / sizeof g_targets[0]);

std::atomic<bool> g_on[8];      // 按 feat 索引（2..7）

const char* state_name(State s) {
    switch (s) {
        case ST_OK:       return "ok";
        case ST_NOTFOUND: return "not-found";
        case ST_SHARED:   return "shared";
        case ST_WRITE:    return "ro";
        case ST_ERR:      return "err";
        default:          return "none";
    }
}

// ------------------------------------------------------------------ 指针替换
// MethodInfo 通常在 HybridCLR 的托管堆上（可写）。为防落进只读页，写前 mprotect 一次。
bool write_ptr(uintptr_t addr, void* v) {
    if (!ares::il2cpp::is_readable(addr, 8)) return false;
    void* page = (void*) (addr & ~(uintptr_t) 0xFFF);
    // 先试写：可写页（堆）直接成功，避免无谓的 mprotect 抖动
    void* old = *(void**) addr;
    *(void**) addr = v;
    if (*(void**) addr == v) return true;
    *(void**) addr = old;
    if (mprotect(page, 0x2000, PROT_READ | PROT_WRITE) != 0) return false;
    *(void**) addr = v;
    if (*(void**) addr == v) return true;
    *(void**) addr = old;
    return false;
}

State resolve_one(Target& t) {
    void* k = ares::il2cpp::find_class_by_name(t.klass);
    if (!k) { HLOGE("[%s] klass %s NOT FOUND", t.label, t.klass); return ST_NOTFOUND; }
    void* mi = ares::il2cpp::find_method(k, t.method, t.nparams);
    if (!mi) { HLOGE("[%s] %s.%s/%d NOT FOUND", t.label, t.klass, t.method, t.nparams); return ST_NOTFOUND; }
    uintptr_t mip = (uintptr_t) mi;
    void* mp  = *(void**) mip;
    const char* nm = *(const char**) (mip + OFF_MI_NAME);
    if (!mp) { HLOGE("[%s] methodPointer == NULL（HybridCLR 尚未为其生成入口）", t.label); return ST_SHARED; }
    t.mi = mi;
    t.savedMp = mp;
    unsigned long rva = (unsigned long) ((uintptr_t) mp - ares::il2cpp::base());
    HLOGI("[%s] mi=%p mp=%p rva=%#lx name=%s", t.label, mi, mp, rva, nm ? nm : "(?)");
    return ST_OK;
}

// HybridCLR 在标准 MethodInfo 之后还留了 methodPointer 的副本。17:36 实测（probe_mi_layout）：
//   get_Instance               +0x00=mp  +0x58=mp  +0x60=mp  +0x68=0x101
//   AddMaterialToContainer/5   +0x00=mp  +0x58=mp  +0x60=mp
//   被 hook 过的 IsOwnBlueprint +0x00=我们函数  +0x58=原桩  +0x60=原桩   ← 副本没跟着变
// 如果调用方读的是副本，只改 +0x00 就等于没改（IsOwnBlueprint 的 h1 全部来自 selftest，
// 游戏侧一次都没命中，这是最可疑的解释）。所以三个位置一起改，off 时一起还原。
static const uintptr_t kMpCopies[2] = { 0x58, 0x60 };

void apply_one(Target& t, bool on) {
    if (t.state != ST_OK || !t.mi) return;
    if (on == t.on) return;
    uintptr_t mip = (uintptr_t) t.mi;
    if (on) {
        bool ok = write_ptr(mip + OFF_MI_METHOD_POINTER, t.repl);
        int copied = 0;
        for (uintptr_t off : kMpCopies)
            if (write_ptr(mip + off, t.repl)) ++copied;
        if (ok) {
            t.on = true;
            HLOGI("[%s] ON  -> +0x00/+0x58/+0x60 = %p（原 %p，副本命中 %d/2）",
                  t.label, t.repl, t.savedMp, copied);
        } else {
            t.state = ST_WRITE;
            HLOGE("[%s] 写 methodPointer 失败（只读页？）", t.label);
        }
    } else {
        bool ok = write_ptr(mip + OFF_MI_METHOD_POINTER, t.savedMp);
        for (uintptr_t off : kMpCopies) write_ptr(mip + off, t.savedMp);
        if (ok) {
            t.on = false;
            HLOGI("[%s] OFF -> 三个位置还原 %p", t.label, t.savedMp);
        } else {
            HLOGE("[%s] 还原 methodPointer 失败", t.label);
        }
    }
}

State feat_state(int feat) {
    State best = ST_NONE;
    for (int i = 0; i < kTargetN; ++i) {
        if (g_targets[i].feat != feat) continue;
        if (g_targets[i].state == ST_OK) return ST_OK;
        if (g_targets[i].state == ST_NOTFOUND) best = ST_NOTFOUND;
        else if (best == ST_NONE) best = g_targets[i].state;
    }
    return best;
}

// ---------------------------------------------------------------- 统一 invoker hook
// HybridCLR 把**所有**热更方法的 invoker_method(+0x08) 指到同一个地址（本机实测 rva 0xb97cf0）。
// 游戏内部调用热更方法时会走 invoker(methodPointer, methodInfo, obj, params, exc) 这一跳，
// 而不是查 MethodInfo->methodPointer —— 这就是「改了 MethodInfo 却一次都没命中」的原因
// （17:40 实测：CPropertyPart.GetValue 有 911 个调用点，改完 +0x00/+0x58/+0x60 后 h 仍然是 0）。
// hook 这个统一入口，才能把「每一次热更方法调用」都过一遍手。
static const uintptr_t RVA_INVOKER = 0xb97cf0;      // 运行时用实际 invoker 值反算，见 resolve_invoker()
static void*  g_origInvoker = nullptr;
std::atomic<int> g_invHits{0};
std::atomic<int> g_invRepl{0};

bool g_resolved = false;
char g_report[320];

} // namespace

// ------------------------------------------------------------------ 公开接口
// 统一 invoker hook：先把参数原样转发，只计数 —— 先用它证明「游戏真的走这条路」，
// 确认后再往里加拦截逻辑（拦截逻辑必须按 MethodInfo 精确区分，不能按签名乱切）。
// ★★ 这里是整个 mod 的真正开关点。
//
// 17:45 实测：inv 计数每秒涨 1 万次（inv=215938 → 237582），而桩 hook 的 stub=0。
// 也就是说 —— AOT 侧调用热更方法走的是 **invoker_method(InterpreterInvoke)**，
// 不是 methodPointer 那个桥接桩。这解释了为什么「改 MethodInfo->methodPointer 三个偏移」
// 后游戏侧的 h 仍然恒为 0（改到的只是反射/委托会用的那份指针）。
//
// InvokerMethod 的 ABI（IL2CPP 标准 + HybridCLR 的 InterpreterInvoke 实现）：
//     void* invoker(Il2CppMethodPointer methodPointer, const MethodInfo* method,
//                   void* obj, void** args, Il2CppException** exc)
// 返回值直接放 x0（Runtime::Invoke 就是 `(Il2CppObject*)method->invoker_method(...)`）。
// 所以在这里按 MethodInfo 精确匹配、直接返回我们想要的值，就能改变方法的**全部**调用者。
// 诊断：invoker 到底被哪些方法用过。只记前 32 个不同名字，够判断「目标方法是否走这条路」。
static char g_seenName[32][48];
static int  g_seenN = 0;

static void note_invoker_name(void* mi) {
    if (g_seenN >= 32 || !mi) return;
    const char* nm = *(const char**) ((uintptr_t) mi + 0x10);   // MethodInfo.name
    if (!nm || !ares::il2cpp::is_readable((uintptr_t) nm, 8)) return;
    for (int i = 0; i < g_seenN; ++i) if (strcmp(g_seenName[i], nm) == 0) return;
    strncpy(g_seenName[g_seenN], nm, 47);
    g_seenName[g_seenN][47] = 0;
    ++g_seenN;
    HLOGE("invoker name[%d] = %s", g_seenN - 1, g_seenName[g_seenN - 1]);
}

// Assembly.Load 的另一条可能路径：走 invoker（热更代码 reflective 调用时）。
// 这里在 invoker 里再兜一次「把补丁字节写进 byte[]」，两种 args 布局都试。
static void try_patch_arg(void** args, const char* tag) {
    if (!args) return;
    size_t pn = 0;
    const uint8_t* pd = ares::il2cpp::patch_bytes(&pn);
    if (!pd || !pn) return;
    for (int k = 0; k < 2; ++k) {
        void* cand = (k == 0) ? args[0] : (args[0] ? *(void**) args[0] : nullptr);
        if (!cand) continue;
        uintptr_t arr = (uintptr_t) cand;
        if (!ares::il2cpp::is_readable(arr + 0x20, 8)) continue;
        uint32_t len = ares::il2cpp::is_readable(arr + 0x18, 4) ? *(uint32_t*) (arr + 0x18) : 0;
        void* vec = *(void**) (arr + 0x20);
        if (vec && len == (uint32_t) pn && ares::il2cpp::is_readable((uintptr_t) vec, 4)
            && *(uint32_t*) vec == 0x00905a4d) {
            memcpy(vec, pd, pn);
            HLOGE("%s: ★ 已把补丁字节写进 byte[]（len=%u，布局%d）", tag, len, k);
            return;
        }
    }
}

static void* hk_invoker(void* mp, void* mi, void* obj, void** args, void** exc) {
    g_invHits.fetch_add(1, std::memory_order_relaxed);
    note_invoker_name(mi);
    if (mi) {
        const char* nm = *(const char**) ((uintptr_t) mi + 0x10);
        if (nm && ares::il2cpp::is_readable((uintptr_t) nm, 8) && strcmp(nm, "Load") == 0) {
            HLOGE("invoker: Load 走 invoker！mp=%p args0=%p", mp, args ? args[0] : nullptr);
            try_patch_arg(args, "invoker.Load");
        }
    }
    if (mi) {
        for (int i = 0; i < kTargetN; ++i) {
            const Target& t = g_targets[i];
            if (!t.on || t.mi != mi) continue;
            g_hits[t.feat].fetch_add(1, std::memory_order_relaxed);
            if (exc) *exc = nullptr;
            switch (t.feat) {
                case 6:                                  // bool IsOwnBlueprint(...) → true
                    return (void*) (intptr_t) 1;
                case 4: case 5: case 7:                  // void：子弹/耐久/反作弊 → 直接吞掉
                    return nullptr;
                case 2: {                                // int GetValue(eType, nKey, nDefault)
                    // args 是 IL2CPP 的 params 数组：args[0]=this，args[1..]=各实参。
                    // invoker 收到的 args 若为 StackObject* 连续槽，这里的解引用仍然成立
                    // （两种布局下 args[i] 都指向「第 i 个实参值」的位置）。
                    int32_t nKey = 0;
                    if (args && args[2]) nKey = *(int32_t*) args[2];
                    int n = g_lockN.load(std::memory_order_relaxed);
                    for (int k = 0; k < n && k < kMaxLock; ++k)
                        if (g_lockKey[k] == nKey) return (void*) (intptr_t) g_lockVal[k];
                    return (void*) (intptr_t) ((args && args[3]) ? *(int32_t*) args[3] : 0);
                }
                default:
                    return nullptr;
            }
        }
    }
    return ((void* (*)(void*, void*, void*, void**, void**)) g_origInvoker)(mp, mi, obj, args, exc);
}

// 装 unified invoker hook（幂等）。返回 1 成功。
static bool install_invoker_hook() {
    if (g_origInvoker) return true;
    uintptr_t base = ares::il2cpp::base();
    if (!base) return false;
    // 用「任一已知方法的 invoker」反算真实地址，避免 RVA 写死。
    void* k = ares::il2cpp::find_class_by_name("CGameSceneMgr");
    void* mi = k ? ares::il2cpp::find_method(k, "get_Instance", 0) : nullptr;
    if (!mi) return false;
    void* inv = *(void**) ((uintptr_t) mi + 0x08);
    if (!inv || !ares::il2cpp::is_executable((uintptr_t) inv)) return false;
    unsigned long rva = (unsigned long) ((uintptr_t) inv - base);
    int rc = DobbyHook((void*) inv, (void*) hk_invoker, (void**) &g_origInvoker);
    HLOGI("invoker: addr=%p rva=%#lx DobbyHook rc=%d orig=%p", inv, rva, rc, g_origInvoker);
    return rc == 0;
}

// ---------------------------------------------------------------- 桩 hook 实验
// invoker(0xb97cf0) hook 装上后 inv 计数恒为 0（17:43 实测）⇒ 游戏内部调用热更方法
// **不经过 invoker_method**，而是直接 blr 到 methodPointer（那个「按签名分组的桩」）。
// 所以真正能拦住调用的位置是桩本身。这里拿 (int,int,int)->int 那个桩（rva 0xb50364，
// CPropertyPart.GetValue 就落在它上面）做实验：只计数并原样转发，先证明「能拦到」。
static void* g_origStub3i = nullptr;
std::atomic<int> g_stub3iHits{0};
static const uintptr_t RVA_STUB_3I = 0xb50364;     // (this,int,int,int,mi)->int

static int hk_stub3i(void* self, int32_t a, int32_t b, int32_t c, void* mi) {
    g_stub3iHits.fetch_add(1, std::memory_order_relaxed);
    return ((int32_t (*)(void*, int32_t, int32_t, int32_t, void*)) g_origStub3i)(self, a, b, c, mi);
}

static bool install_stub_hook(uintptr_t rva, void* repl, void** orig, const char* tag) {
    uintptr_t base = ares::il2cpp::base();
    if (!base) return false;
    void* addr = (void*) (base + rva);
    if (!ares::il2cpp::is_executable((uintptr_t) addr)) {
        HLOGE("stub[%s]: %p 不在可执行段", tag, addr);
        return false;
    }
    int rc = DobbyHook(addr, repl, orig);
    HLOGI("stub[%s]: addr=%p rva=%#lx rc=%d orig=%p", tag, addr, (unsigned long) rva, rc, *orig);
    return rc == 0;
}

extern "C" void hooks_install(void) {
    if (!ares::il2cpp::init()) {
        HLOGE("hooks_install: il2cpp 未就绪");
        return;
    }
    if (!ares::il2cpp::hot_image()) {
        HLOGE("hooks_install: 热更 image 还没就绪（HotFixMgr.StartLoad 尚未跑完），稍后重试");
        return;
    }
    // 默认开启：7 拦截反作弊（用户要求「默认打开」）、6 解锁全部蓝图（一键型功能，装上即生效）。
    // 菜单里仍可关：g_on 是唯一真相源，hooks_set_enabled 会覆写。
    // 功能 2 暂时不默认开：它的拦截要读 args 里的 nKey，args 布局未证实前贸然拦截
    // 会把所有属性读成 0。等 6/7 的拦截被证实之后再逐项打开。
    // g_on[2].store(true, std::memory_order_relaxed);
    g_on[6].store(true, std::memory_order_relaxed);
    g_on[7].store(true, std::memory_order_relaxed);
    HLOGI("======== hooks_install: resolve %d targets ========", kTargetN);
    for (int i = 0; i < kTargetN; ++i) g_targets[i].state = resolve_one(g_targets[i]);
    g_resolved = true;
    // ★ 解析完成后立刻按当前开关状态应用一次。漏了这一步，功能 7（默认开）即便解析成功
    //   也永远不会真的替换指针 —— 17:09 实测就是卡在这里：12 个目标全 ok，但一条 ON 都没有。
    for (int i = 0; i < kTargetN; ++i)
        if (g_on[g_targets[i].feat].load(std::memory_order_relaxed))
            apply_one(g_targets[i], true);
    // invoker hook 保留（成本为零，能顺带观察到反射路径），但已证实游戏主路径不经过它。
    install_invoker_hook();
    // ★ 真正能拦到调用的位置：按签名共享的桩。
    if (!g_origStub3i) install_stub_hook(RVA_STUB_3I, (void*) hk_stub3i, (void**) &g_origStub3i, "3i");
    HLOGI("hooks_report: %s", hooks_report());
}

extern "C" int hooks_retry(void) {
    if (!ares::il2cpp::init()) return 0;
    if (!ares::il2cpp::hot_image()) return 0;
    if (!g_resolved) { hooks_install(); return kTargetN; }
    int retried = 0, fixed = 0;
    for (int i = 0; i < kTargetN; ++i) {
        if (g_targets[i].state != ST_NOTFOUND && g_targets[i].state != ST_SHARED) continue;
        ++retried;
        g_targets[i].state = resolve_one(g_targets[i]);
        if (g_targets[i].state == ST_OK) ++fixed;
        // 解析成功后若该功能处于开启状态，立刻补装
        if (g_targets[i].state == ST_OK && g_on[g_targets[i].feat].load())
            apply_one(g_targets[i], true);
    }
    if (retried) HLOGI("hooks_retry: retried=%d 修好=%d -> %s", retried, fixed, hooks_report());
    return retried;
}

extern "C" void hooks_set_prop(int key, int value) {
    int n = g_lockN.load(std::memory_order_relaxed);
    for (int i = 0; i < n && i < kMaxLock; ++i)
        if (g_lockKey[i] == key) { g_lockVal[i] = value; return; }
    if (n < kMaxLock) {
        g_lockKey[n] = key;
        g_lockVal[n] = value;
        g_lockN.store(n + 1, std::memory_order_relaxed);
    }
    HLOGI("hooks_set_prop: key=%d value=%d（表内 %d 项）", key, value, g_lockN.load());
}

extern "C" void hooks_set_enabled(int feat, bool on) {
    if (feat < 2 || feat > 7) return;
    g_on[feat].store(on, std::memory_order_relaxed);
    if (!g_resolved) hooks_install();
    for (int i = 0; i < kTargetN; ++i) {
        if (g_targets[i].feat != feat) continue;
        if (g_targets[i].state != ST_OK && on) {
            g_targets[i].state = resolve_one(g_targets[i]);
        }
        apply_one(g_targets[i], on);
    }
    HLOGI("hooks_set_enabled feat=%d on=%d -> %s", feat, on ? 1 : 0, hooks_report());
}

extern "C" void hooks_selftest(void) {
    // 自测：经 MethodInfo->methodPointer 直接调一次已被替换的方法。
    // 证明的是「MethodInfo → 我们的实现」这条链完整（不是 inline hook 被绕过、也不是指针被游戏改回）。
    // self 传 nullptr —— 替换体不读 self。
    int n = 0;
    for (int i = 0; i < kTargetN; ++i) {
        Target& t = g_targets[i];
        if (t.state != ST_OK || !t.on || !t.mi) continue;
        void* cur = *(void**) t.mi;
        if (cur != t.repl) { HLOGE("selftest: %s 指针已漂移（当前 %p 期望 %p）", t.label, cur, t.repl); continue; }
        ++n;
        if (strcmp(t.method, "GetValue") == 0) {
            // 不在这里调：hk_GetValue 的 fallback 会拿 self 去读 CPropertyPart，self=nullptr 必崩。
            HLOGI("selftest: %s 跳过（需要真实 self）", t.label);
        } else if (strcmp(t.method, "IsOwnBlueprint") == 0) {
            bool r = ((bool (*)(void*, int32_t, int32_t, void*)) cur)(nullptr, 0, 0, t.mi);
            HLOGI("selftest: %s -> %d（期望 1）", t.label, r ? 1 : 0);
        } else if (strcmp(t.method, "SetEndurance") == 0) {
            ((void (*)(void*, int32_t, bool, void*)) cur)(nullptr, 0, false, t.mi);
            HLOGI("selftest: %s ok", t.label);
        } else if (strcmp(t.method, "ReduceAmmo") == 0) {
            ((void (*)(void*, uint64_t, int32_t, void*)) cur)(nullptr, 0, 0, t.mi);
            HLOGI("selftest: %s ok", t.label);
        } else {
            ((void (*)(void*, void*)) cur)(nullptr, t.mi);
            HLOGI("selftest: %s ok", t.label);
        }
    }
    HLOGI("selftest: 调用了 %d 个替换体 -> %s", n, hooks_report());
}

extern "C" const char* hooks_report(void) {
    int n = 0;
    g_report[0] = 0;
    for (int f = 2; f <= 7; ++f) {
        const char* s = state_name(feat_state(f));
        // 漂移检测：MethodInfo->methodPointer 现在是否还等于我们的替换体。
        // 如果被游戏/HybridCLR 改回去（例如方法首次被调用时重新 Init），这里会显示 !drift，
        // 那就必须改成「每次调用前重装」或换别的手段 —— 不能拿 h0 当"没被调用"的证据。
        int live = 0, total = 0;
        for (int i = 0; i < kTargetN; ++i) {
            Target& t = g_targets[i];
            if (t.feat != f || t.state != ST_OK || !t.mi || !t.on) continue;   // 未启用的不参与漂移统计
            ++total;
            if (*(void**) t.mi == t.repl && *(void**) ((uintptr_t) t.mi + 0x58) == t.repl) ++live;
        }
        int w = snprintf(g_report + n, sizeof (g_report) - (size_t) n, "%s%d=%s%s/h%d/%d-%d%s",
                         (f == 2) ? "" : " ", f, s, g_on[f].load() ? "(on)" : "",
                         g_hits[f].load(), live, total, (live < total) ? "!drift" : "");
        if (w < 0) break;
        n += w;
        if ((size_t) n >= sizeof (g_report)) { g_report[sizeof (g_report) - 1] = 0; break; }
    }
    {
        int w = snprintf(g_report + n, sizeof (g_report) - (size_t) n, " inv=%d stub=%d",
                         g_invHits.load(), g_stub3iHits.load());
        if (w > 0) n += w;
    }
    return g_report;
}
