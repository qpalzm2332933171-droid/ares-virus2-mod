// Aresvirus2 直装 Mod 菜单 —— native 主体
// 目标: 阿瑞斯病毒2 官服及全部安卓渠道服 (com.zzonegame.aresvirus2.*, Unity 2020.3.49f1 / IL2CPP + HybridCLR)
//
// 设计要点（详见 PROGRESS.md 与 _tier2_hookpoints.md）：
//  * 游戏业务逻辑在 Code.dll（HybridCLR 解释执行），没有 native 函数指针，
//    所以不能对 ET 的 System 方法做 inline hook。可用通道：
//      1) hook hybridclr::metadata::Image::ReadMethodBody —— IL 方法体加载后改字节码
//      2) il2cpp_domain_get / il2cpp_domain_get_assemblies（有导出）遍历
//      3) 直接改字段（m_dicMaterial / m_dicProperty / m_nEnduranceHaveUsedTime / dicCompoundCount）

#include <list>
#include <vector>
#include <cstring>
#include <cstdio>
#include <cstdlib>
#include <string>
#include <pthread.h>
#include <thread>
#include <unistd.h>
#include <dlfcn.h>
#include <sys/stat.h>
#include <jni.h>
#include <android/log.h>

#include "Includes/Logger.h"
#include "Includes/obfuscate.h"
#include "Includes/Utils.hpp"
#include "Includes/Macros.h"
#include "Menu/Menu.hpp"
#include "Menu/Jni.hpp"
#include "dobby.h"
#include "il2cpp_bridge.h"
#include "mod_shm.h"
#include "mod_features.h"
#include "mod_hooks.h"

#define LOGTAG "AresModMenu"

// 进程内 .NET 程序集落盘（见 dumper.cpp）
int dump_dotnet_assemblies(const char* outdir);

// Menu.cpp 定义；头文件里没有声明，这里前置声明（用于把状态写到菜单副标题）
void menu_update_subtitle(JNIEnv* env, const char* html);

// ---------------------------------------------------------------- 全局状态
static bool g_MenuReady = false;
static uintptr_t g_il2cppBase = 0;

static bool g_antiCheat = true;       // 默认开
static bool g_noFireCooldown = false;
static bool g_lockEndurance = false;
static bool g_lockProperty = false;

static void write_item_request(int configId, int num);   // fwd
__attribute__((unused)) static int g_itemId = 1;
__attribute__((unused)) static int g_itemCount = 1;
static std::string g_itemList;      // InputText 清单原文（1210*5,1001*10）
static int g_buffId = 1;
static int g_buffSec = 60;

struct PropLock { int type; double value; };
static std::vector<PropLock> g_propLocks;
static pthread_mutex_t g_lock = PTHREAD_MUTEX_INITIALIZER;

// ---------------------------------------------------------------- il2cpp 桥
typedef void* (*t_domain_get)();
typedef void** (*t_get_assemblies)(void* domain, size_t* count);
typedef void* (*t_thread_attach)(void* domain);
typedef void* (*t_runtime_invoke)(void* method, void* obj, void** params, void** exc);

static t_domain_get p_domain_get = nullptr;
static t_get_assemblies p_get_assemblies = nullptr;
static t_thread_attach p_thread_attach = nullptr;
static t_runtime_invoke p_runtime_invoke = nullptr;

// 历史遗留：这里原来是 dlopen("libil2cpp.so") + dlsym 解析导出。
// 2026-10-29 实测证明这条路会 SIGKILL（见 il2cpp_bridge.cpp 的 init() 注释），
// 现在 il2cpp 取址全部走 il2cpp_bridge 的 base+RVA 纯读方案，本函数不再调用任何
// dlopen/dlsym，也不再被调用（保留是为了不破坏 Android.mk / 编译告警基线）。
__attribute__((unused)) static void ResolveIl2Cpp() {
    LOGI("ResolveIl2Cpp: 已废弃 —— il2cpp 取址改由 ares::il2cpp::init() 的 base+RVA 方案提供");
}

// ------------------------------------------------- HybridCLR ReadMethodBody hook
// hybridclr::metadata::Image::ReadMethodBody(const Il2CppMethodDefinition&, const TbMethod&, MethodBody&)
// RVA 来自 libil2cpp.dump.so 的 dynsym = 0xba3390
#define RVA_READ_METHOD_BODY 0xba3390

typedef int (*t_ReadMethodBody)(void* self, void* methodDef, void* tbMethod, void* outBody);
static t_ReadMethodBody orig_ReadMethodBody = nullptr;
static volatile int g_rmbCalls = 0;

__attribute__((unused)) static int hook_ReadMethodBody(void* self, void* methodDef, void* tbMethod, void* outBody) {
    int r = orig_ReadMethodBody(self, methodDef, tbMethod, outBody);
    int c = __sync_add_and_fetch(&g_rmbCalls, 1);
    if (r == 0 && (c % 500) == 0) LOGI("ReadMethodBody ok, calls=%d body=%p", c, outBody);
    return r;
}

// ---------------------------------------------------------------- 菜单描述
jobjectArray GetFeatureList(JNIEnv *env, jobject context) {
    jobjectArray ret;
    const char *features[] = {
            OBFUSCATE("1_RichTextView_status_状态: 等待热更与场景加载..."),

            OBFUSCATE("Category_① 物品发放（全 1,349 物品 / 背包与仓库）"),
            OBFUSCATE("10_Button_打开物品发放器（原分类 / 图标 / 背包·仓库）"),

            OBFUSCATE("Category_② 人物属性（全部 50 项实时默认值）"),
            OBFUSCATE("20_Button_打开全属性面板（50 项全部可改 / 可锁定）"),

            OBFUSCATE("Category_③ 状态效果 Buff（全 472 Buff）"),
            OBFUSCATE("30_Button_打开 Buff 选择器（多选 / 自定义时长）"),

            OBFUSCATE("Category_④ 战斗与蓝图"),
            OBFUSCATE("40_Toggle_子弹不减反增"),
            OBFUSCATE("41_Toggle_锁定武器/装备耐久"),
            OBFUSCATE("42_Button_立即补满当前武器及背包装备耐久"),
            OBFUSCATE("50_Button_一键解锁全部蓝图（含全 65 张）"),

            OBFUSCATE("Category_⑤ 全方位反作弊拦截（默认开）"),
            OBFUSCATE("60_Toggle_True_全代码拦截作弊检验/上报/封号"),
            OBFUSCATE("61_Button_立即刷新人物与仓库快照"),
    };

    int Total_Feature = (sizeof features / sizeof features[0]);
    jclass strCls = env->FindClass("java/lang/String");
    ret = (jobjectArray) env->NewObjectArray(Total_Feature, strCls, env->NewStringUTF(""));
    for (int i = 0; i < Total_Feature; i++)
        env->SetObjectArrayElement(ret, i, env->NewStringUTF(features[i]));
    return ret;
}

static std::string json_escape(const char* s) {
    std::string out;
    if (!s) return out;
    for (const char* p = s; *p; ++p) {
        unsigned char c = (unsigned char) *p;
        if (c == '"' || c == '\\') { out.push_back('\\'); out.push_back((char) c); }
        else if (c == '\n') out += "\\n";
        else if (c == '\r') out += "\\r";
        else if (c < 0x20) {
            char tmp[8]; snprintf(tmp, sizeof(tmp), "\\u%04x", c); out += tmp;
        } else {
            out.push_back((char) c);
        }
    }
    return out;
}

jstring NativeQuery(JNIEnv *env, jclass clazz, jint op, jint a1, jint a2, jlong aL, jstring str) {
    std::string sarg;
    if (str) {
        const char* p = env->GetStringUTFChars(str, nullptr);
        if (p) { sarg = p; env->ReleaseStringUTFChars(str, p); }
    }
    ModShm* shm = ares::il2cpp::mod_shm();
    if (!shm) return env->NewStringUTF("{}");

    if (op == 1) {
        size_t pending = ares::il2cpp::mod_pending_cmd_count();
        // Snapshot JSON
        std::string j;
        j.reserve(4096);
        char head[768];
        uint32_t fl = shm->flags;
        uint32_t mgR = shm->mgRateX10 ? shm->mgRateX10 : 100u;
        std::string stEsc = json_escape(shm->lastStatus);
        snprintf(head, sizeof(head),
                 "{\"ready\":%u,\"scene\":%d,\"cid\":%d,\"flags\":%u,"
                 "\"ac\":%d,\"ammo\":%d,\"end\":%d,\"paid\":%d,\"inst\":%d,\"infStk\":%d,\"noclip\":%d,\"mg\":%d,"
                 "\"mgRate\":%u,\"dynSeq\":%u,\"tick\":%u,\"cmdRes\":%d,"
                 "\"okCnt\":%u,\"failCnt\":%u,\"lastFailId\":%d,\"pending\":%zu,\"status\":\"%s\",",
                 shm->bridgeReady, shm->sceneReady, shm->currentCid, fl,
                 (fl & MOD_FLAG_AC_DISABLE) ? 0 : 1,
                 (fl & MOD_FLAG_AMMO_INC) ? 1 : 0,
                 (fl & MOD_FLAG_LOCK_ENDURANCE) ? 1 : 0,
                 (fl & MOD_FLAG_UNLOCK_PAID) ? 1 : 0,
                 (fl & MOD_FLAG_INSTANT_INTERACT) ? 1 : 0,
                 (fl & MOD_FLAG_INF_STACK_WAREHOUSE) ? 1 : 0,
                 (fl & MOD_FLAG_NOCLIP) ? 1 : 0,
                 (fl & MOD_FLAG_MACHINE_GUN) ? 1 : 0,
                 mgR, shm->dynSeq,
                 shm->tickCount, shm->lastCmdResult,
                 shm->cmdOkCount, shm->cmdFailCount, shm->lastFailArg1, pending, stEsc.c_str());
        j += head;

        // Home storage boxes
        j += "\"boxes\":[";
        int bc = shm->homeBoxCount;
        if (bc < 0) bc = 0;
        if (bc > 16) bc = 16;
        for (int i = 0; i < bc; ++i) {
            char buf[96];
            snprintf(buf, sizeof(buf), "%s{\"id\":%lld,\"cap\":%d,\"used\":%d}",
                     i ? "," : "", (long long) shm->boxes[i].instanceId, shm->boxes[i].capacity, shm->boxes[i].usedSlots);
            j += buf;
        }
        j += "],\"props\":[";

        // Live 50+ property values
        int pc = shm->propCount;
        if (pc < 0) pc = 0;
        if (pc > 64) pc = 64;
        for (int i = 0; i < pc; ++i) {
            char buf[96];
            snprintf(buf, sizeof(buf), "%s{\"k\":%d,\"cur\":%d,\"lk\":%u}",
                     i ? "," : "", shm->props[i].key, shm->props[i].curRaw, shm->props[i].locked);
            j += buf;
        }
        j += "],\"buffs\":[";

        // Active mod buffs
        int ac = shm->activeBuffCount;
        if (ac < 0) ac = 0;
        if (ac > 64) ac = 64;
        for (int i = 0; i < ac; ++i) {
            char buf[64];
            snprintf(buf, sizeof(buf), "%s{\"id\":%d,\"rem\":%d}",
                     i ? "," : "", shm->buffs[i].buffId, shm->buffs[i].remainSec);
            j += buf;
        }
        j += "]}";
        return env->NewStringUTF(j.c_str());
    }
    if (op == 2) {
        // Add item: a1=id, a2=num, aL=boxInstanceId, sarg="0"|"1"
        int target = (!sarg.empty() && sarg[0] == '1') ? 1 : 0;
        bool ok = ares::il2cpp::mod_enqueue_cmd(MOD_CMD_ADD_ITEM, a1, a2, target, (int64_t) aL);
        return env->NewStringUTF(ok ? "1" : "0");
    }
    if (op == 3) {
        // Set/Lock property: a1=key, a2=rawVal, aL=lockOp(-1/0/1)
        bool ok = ares::il2cpp::mod_enqueue_cmd(MOD_CMD_SET_PROP, a1, a2, (int32_t) aL, 0);
        return env->NewStringUTF(ok ? "1" : "0");
    }
    if (op == 4) {
        // Apply buffs: either single (a1=buffId, a2=durationSec, sarg="")
        // or batch (a1=durationSec, sarg="1001,1002,...")
        int queued = 0;
        if (sarg.empty()) {
            if (a1 > 0 && ares::il2cpp::mod_enqueue_cmd(MOD_CMD_APPLY_BUFF, a1, a2, 0, 0)) {
                ++queued;
            }
        } else {
            size_t pos = 0;
            while (pos < sarg.size()) {
                size_t comma = sarg.find(',', pos);
                std::string tok = sarg.substr(pos, (comma == std::string::npos) ? std::string::npos : comma - pos);
                pos = (comma == std::string::npos) ? sarg.size() : comma + 1;
                int bid = atoi(tok.c_str());
                if (bid > 0 && ares::il2cpp::mod_enqueue_cmd(MOD_CMD_APPLY_BUFF, bid, a1, 0, 0)) {
                    ++queued;
                }
            }
        }
        char out[32];
        snprintf(out, sizeof(out), "%d", queued);
        return env->NewStringUTF(out);
    }
    if (op == 5) {
        bool ok = ares::il2cpp::mod_enqueue_cmd(MOD_CMD_REMOVE_BUFF, a1, 0, 0, 0);
        return env->NewStringUTF(ok ? "1" : "0");
    }
    if (op == 6) {
        bool ok = ares::il2cpp::mod_enqueue_cmd(MOD_CMD_UNLOCK_ALL_BP, 0, 0, 0, 0);
        return env->NewStringUTF(ok ? "1" : "0");
    }
    if (op == 7) {
        bool ok = ares::il2cpp::mod_enqueue_cmd(MOD_CMD_RESTORE_ENDURANCE, 0, 0, 0, 0);
        return env->NewStringUTF(ok ? "1" : "0");
    }
    if (op == 8) {
        bool ok = ares::il2cpp::mod_enqueue_cmd(MOD_CMD_CLEAR_PROP_LOCKS, 0, 0, 0, 0);
        return env->NewStringUTF(ok ? "1" : "0");
    }
    if (op == 9) {
        bool ok = ares::il2cpp::mod_enqueue_cmd(MOD_CMD_REFRESH_SNAP, 0, 0, 0, 0);
        return env->NewStringUTF(ok ? "1" : "0");
    }
    if (op == 10) {
        bool on = (a2 != 0);
        if (a1 == 0) {
            g_antiCheat = on;
            ares::il2cpp::mod_set_flag(MOD_FLAG_AC_DISABLE, !on);
        } else if (a1 == 1) {
            g_noFireCooldown = on;
            ares::il2cpp::mod_set_flag(MOD_FLAG_AMMO_INC, on);
        } else if (a1 == 2) {
            g_lockEndurance = on;
            ares::il2cpp::mod_set_flag(MOD_FLAG_LOCK_ENDURANCE, on);
            if (on) ares::il2cpp::mod_enqueue_cmd(MOD_CMD_RESTORE_ENDURANCE, 0, 0, 0, 0);
        } else if (a1 == 3) {
            ares::il2cpp::mod_set_flag(MOD_FLAG_UNLOCK_PAID, on);
            ares::il2cpp::mod_enqueue_cmd(MOD_CMD_UNLOCK_ALL_PAID, on ? 1 : 0, 0, 0, 0);
        } else if (a1 == 4) {
            ares::il2cpp::mod_set_flag(MOD_FLAG_INSTANT_INTERACT, on);
        } else if (a1 == 5) {
            ares::il2cpp::mod_set_flag(MOD_FLAG_INF_STACK_WAREHOUSE, on);
        } else if (a1 == 6) {
            ares::il2cpp::mod_set_flag(MOD_FLAG_NOCLIP, on);
        } else if (a1 == 7) {
            ares::il2cpp::mod_set_flag(MOD_FLAG_MACHINE_GUN, on);
        }
        return env->NewStringUTF("1");
    }
    if (op == 11) {
        uint32_t r = (a1 < 10) ? 10u : ((a1 > 600) ? 600u : (uint32_t) a1);
        shm->mgRateX10 = r;
        return env->NewStringUTF("1");
    }
    if (op == 12) {
        if (!sarg.empty() && sarg.size() < sizeof(shm->dynDir)) {
            strncpy(shm->dynDir, sarg.c_str(), sizeof(shm->dynDir) - 1);
            shm->dynDir[sizeof(shm->dynDir) - 1] = '\0';
        }
        if (a1 != 0) ares::il2cpp::mod_enqueue_cmd(MOD_CMD_SYNC_DYN_ASSETS, 0, 0, 0, 0);
        return env->NewStringUTF("1");
    }
    if (op == 13) {
        bool ok = ares::il2cpp::mod_enqueue_cmd(MOD_CMD_SYNC_DYN_ASSETS, 0, 0, 0, 0);
        return env->NewStringUTF(ok ? "1" : "0");
    }
    if (op == 14) {
        size_t pending = ares::il2cpp::mod_pending_cmd_count();
        char out[128];
        snprintf(out, sizeof(out), "{\"pending\":%zu,\"okCnt\":%u,\"failCnt\":%u,\"lastFailId\":%d}",
                 pending, shm->cmdOkCount, shm->cmdFailCount, shm->lastFailArg1);
        return env->NewStringUTF(out);
    }
    return env->NewStringUTF("{}");
}

// ---------------------------------------------------------------- 回调
void Changes(JNIEnv *env, jclass clazz, jobject obj, jint featNum, jstring featName,
             jint value, jlong Lvalue, jboolean boolean, jstring text) {
    const char* name = env->GetStringUTFChars(featName, nullptr);
    LOGI("Changes feat=%d name=%s value=%d bool=%d", featNum, name ? name : "?", value, boolean);
    if (name) env->ReleaseStringUTFChars(featName, name);

    switch (featNum) {
        case 40:
            g_noFireCooldown = boolean;
            ares::il2cpp::mod_set_flag(MOD_FLAG_AMMO_INC, boolean);
            break;
        case 41:
            g_lockEndurance = boolean;
            ares::il2cpp::mod_set_flag(MOD_FLAG_LOCK_ENDURANCE, boolean);
            if (boolean) ares::il2cpp::mod_enqueue_cmd(MOD_CMD_RESTORE_ENDURANCE, 0, 0, 0, 0);
            break;
        case 42:
            ares::il2cpp::mod_enqueue_cmd(MOD_CMD_RESTORE_ENDURANCE, 0, 0, 0, 0);
            break;
        case 50:
            ares::il2cpp::mod_enqueue_cmd(MOD_CMD_UNLOCK_ALL_BP, 0, 0, 0, 0);
            break;
        case 60:
            g_antiCheat = boolean;
            ares::il2cpp::mod_set_flag(MOD_FLAG_AC_DISABLE, !boolean);
            break;
        case 61:
            ares::il2cpp::mod_enqueue_cmd(MOD_CMD_REFRESH_SNAP, 0, 0, 0, 0);
            break;
        default:
            break;
    }
}

// ---------------------------------------------------------------- 菜单 → IL 的参数通道
// 主通道：静态 int 字段槽（il2cpp_bridge 的 set_item_slot）。
//   IL 侧 payload4 每帧 ldsfld 读 EditorPrefsX::endianDiff1/2，非 0 就发物品并清零。
//   为什么不用文件：payload3 的 File.ReadAllText + Int32.Parse 会抛
//   MethodAccessException（Int32.Parse 内部 CultureInfo 的 AOT 泛型路径不可访问），实测已回退。
// 副通道：仍写 /data/data/<pkg>/files/Lv —— payload4 不再读它，只留作事后核对与降级诊断。
static void write_item_request(int configId, int num) {
    if (configId <= 0 || num <= 0) { LOGI("req: 参数非法 id=%d num=%d", configId, num); return; }
    if (num > 999) num = 999;                 // 不可堆叠物品会按数量循环造 UID，别一次给太多
    // ★★ 20:37:13 真机定论：feat_add_item()（native → interp_invoke(0xb89ef4) → 解释执行
    //   AddMaterialToContainer/5）会**把 Unity 主线程永久卡死** —— 日志停在
    //   `feat1: 当前 nCustomID = 20200202` 之后，ProbeOnMain 的每秒心跳同时消失，进程仍存活。
    //   这与 6.45.2 的「interp_invoke 喂 get_Instance(AOT) 卡死」是同一类现象：0xb89ef4 只对
    //   「已生成 interpData 的解释方法」安全，而 AddMaterialToContainer 的 mp 是 0xb89808
    //   抛异常桩（rva=0xb89808 shared=5），进去就会停在 0xb89398 的懒初始化里。
    //   ⇒ 不再从菜单调用它。唯一可用的落地方式是下面的参数槽 + 热更 DLL 补丁（payload6）。
    // bool okInterp = ares::il2cpp::feat_add_item(configId, num);   // ← 卡死主线程，禁用

    // 主路：写参数槽，让打过补丁的热更 DLL（payload6）在下一帧消费
    bool ok = ares::il2cpp::set_item_slot(configId, num);
    LOGI("req: slot=%s id=%d num=%d", ok ? "OK" : "FAIL", configId, num);
    if (!ok) LOGI("req: 失败原因 = %s", ares::il2cpp::last_error());

    const char* fdir = ares::il2cpp::app_files_dir();
    char path[320], dst[320];
    snprintf(path, sizeof path, "%s/Lv.tmp", fdir);
    snprintf(dst,  sizeof dst,  "%s/Lv",     fdir);
    if (FILE* f = fopen(path, "w")) {
        fprintf(f, "%lld", (long long) configId * 100000LL + num);
        fclose(f);
        rename(path, dst);
    }
}

// ---------------------------------------------------------------- 线程
void hack_thread() {
    while (!isLibraryLoaded(OBFUSCATE("libil2cpp.so"))) sleep(1);

    LOGI("hack_thread: libil2cpp mapped, warming bridge (waits for domain)");

    LOGI("hack_thread: libil2cpp loaded, base=%p", (void*) g_il2cppBase);

    // il2cpp 桥初始化（dumper 已完成使命，去掉了；否则每次 8MB dump 会淹掉日志）
    LOGI("hack_thread: calling warmup");
    modfeat::warmup();
    LOGI("hack_thread: warmup done");

    // ★ 所有 IL2CPP 反射/调用都挪到 Unity 主线程（ProbeOnMain，由 Java 侧 post 进来）。
    //   在这里（hack_thread）调 probe_* 会先拿到 g_mtx 并卡在 Method::Init 上，
    //   主线程随后去拿同一把锁 → 直接死锁（16:01 实测：ProbeOnMain 打 enter 后无 exit、ANR）。
    LOGI("hack_thread: skipping probes here — they run on Unity main via ProbeOnMain");

    // Wait until hot_image / domain are warm without touching HybridCLR MethodInfo pointers on bg thread
    LOGI("hack_thread: ready (waiting for Unity main thread ProbeOnMain + ModBridge)");
    g_MenuReady = true;
}

__attribute__((constructor))
void lib_main() {
    LOGI("lib_main: constructor fired, spawning hack_thread");
    std::thread(hack_thread).detach();
    LOGI("lib_main: thread spawned");
}
