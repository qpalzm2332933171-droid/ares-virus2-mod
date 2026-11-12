// il2cpp_bridge.cpp —— 在 libil2cpp.so 进程内按名字定位 C# 类/方法并调用
//
// 全部偏移来自 D:\arsbd\devtmp\libil2cpp.dump.so 的反汇编确证，逐条见 _tier2_bridge.md。
// 设计要点：
//   1) 不硬编码任何 RVA 当 hook 点：hook 目标一律运行时按「类名+方法名」查出来再 hook。
//   2) params 一律「指向实参槽的指针」（反汇编 0xaa6c40 段确证），引用类型槽里放对象指针。
//   3) il2cpp_runtime_invoke 的返回值：值类型按原始值返回（x0 原样），不是 boxed —— 由 0xaa6e88
//      之后 convert_args 自己 bl il2cpp_value_box 装箱反证；ret_i32 仍兼容 boxed 变体。
//   4) 扫堆回调声明 3 个形参：真 ABI 是 2 参时第 3 个只是寄存器垃圾，不 deref 就无害；
//      若真 ABI 是 3 参，则第三个寄存器里才是对象 —— 两种 ABI 都能捞到候选，且永不崩。

#include "il2cpp_bridge.h"
#include "mod_shm.h"

#include <android/log.h>
#include <dlfcn.h>
#include <unistd.h>
#include <sys/mman.h>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <cstdint>
#include <atomic>
#include <ctime>
#include <mutex>
#include <string>
#include <vector>
#include <unordered_map>
#include <unordered_set>

#include "dobby.h"

#define TAG "AresBridge"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO,  TAG, __VA_ARGS__)
#define LOGW(...) __android_log_print(ANDROID_LOG_WARN,  TAG, __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, TAG, __VA_ARGS__)

namespace ares {
namespace il2cpp {

static void* find_method_raw(void* klass, const char* name, int pc_want);   // fwd

// ==================================================================== 偏移常量
static const size_t OFF_KLASS_NAME        = 0x10;   // const char*
static const size_t OFF_KLASS_NS          = 0x18;   // const char*
static const size_t OFF_KLASS_PARENT      = 0x58;
static const size_t OFF_KLASS_FIELDS      = 0x80;   // FieldInfo**（stride 0x20）
static const size_t OFF_KLASS_METHODS     = 0x98;   // MethodInfo**（stride 8）
static const size_t OFF_KLASS_STATIC      = 0xB8;   // void* static_fields
static const size_t OFF_KLASS_IFACE_SIZE  = 0xF8;   // instance_size
static const size_t OFF_KLASS_MCOUNT      = 0x11C;  // uint16
static const size_t OFF_KLASS_FCOUNT      = 0x120;  // uint16
static const size_t OFF_KLASS_FLAGS       = 0x132;  // bit1 = value type
static const size_t FIELD_STRIDE          = 0x20;
static const size_t OFF_FIELD_NAME        = 0x00;
static const size_t OFF_FIELD_TYPE        = 0x08;
static const size_t OFF_FIELD_OFFSET      = 0x18;   // int32
static const size_t OFF_MI_POINTER        = 0x00;
static const size_t OFF_MI_INVOKER        = 0x08;
static const size_t OFF_MI_NAME           = 0x10;
static const size_t OFF_MI_KLASS          = 0x18;
static const size_t OFF_MI_RETTYPE        = 0x20;
static const size_t OFF_MI_PARAMS         = 0x28;   // ParameterInfo*（不是 Il2CppType**）
static const size_t PARAM_STRIDE          = 0x18;   // ParameterInfo 步长（0xaa6e30 add x24,#0x18）
static const size_t OFF_PARAM_TYPE        = 0x10;   // ParameterInfo->parameter_type（0xaa6d84 起点 0x10）
static const size_t OFF_MI_PCOUNT         = 0x4A;   // uint8
static const size_t OFF_TYPE_ATTRS        = 0x08;
static const size_t OFF_TYPE_CODE         = 0x0A;   // uint8 Il2CppTypeEnum
static const size_t OBJ_HEADER            = 0x10;

// Il2CppTypeEnum
enum { T_BOOLEAN = 0x02, T_I4 = 0x08, T_U4 = 0x09, T_I8 = 0x0A, T_U8 = 0x0B, T_R4 = 0x0C, T_R8 = 0x0D };

// ==================================================================== 导出符号
typedef void*  (*Fn_domain_get)();
typedef void*  (*Fn_thread_attach)(void*);
typedef void*  (*Fn_invoke)(void*, void*, void**, void**);
typedef void   (*Fn_heap_section)(const void*, void*);
typedef void   (*Fn_foreach_heap)(Fn_heap_section /*callback 在前*/, void* /*userData*/);
typedef void   (*Fn_class_init)(void*);
typedef void*  (*Fn_value_box)(void*, void*);
typedef size_t (*Fn_header_size)();
typedef char*  (*Fn_format_exc)(void*, char*, int);
typedef char*  (*Fn_format_trace)(void*, char*, int);
typedef void** (*Fn_get_assemblies)(void*, size_t*);
typedef void*  (*Fn_class_from_name)(void*, const char*, const char*);
typedef void*  (*Fn_thread_current)();
typedef void*  (*Fn_string_new)(const char*);

static Fn_domain_get    p_domain_get     = nullptr;
static Fn_thread_attach p_thread_attach  = nullptr;
static Fn_invoke        p_invoke         = nullptr;
static Fn_foreach_heap  p_foreach_heap   = nullptr;
static Fn_class_init    p_class_init     = nullptr;
static Fn_value_box     p_value_box      = nullptr;
static Fn_header_size   p_header_size    = nullptr;
static Fn_format_exc    p_format_exc     = nullptr;
static Fn_format_trace  p_format_trace   = nullptr;
static Fn_get_assemblies  p_get_assemblies  = nullptr;
static Fn_class_from_name p_class_from_name = nullptr;
static Fn_thread_current  p_thread_current  = nullptr;
static Fn_string_new      p_string_new      = nullptr;

static uintptr_t g_base   = 0;
static void*     g_domain = nullptr;
static bool      g_ready  = false;
static bool      g_inited = false;
static thread_local bool t_attached = false;

// 运行时按指令指纹动态解析出的 IL2CPP / HybridCLR RVA（兼容官服与全部渠道服构建）
static uintptr_t g_rva_domain_ptr        = 0;
static uintptr_t g_rva_runtime_invoke    = 0;
static uintptr_t g_rva_class_init        = 0;
static uintptr_t g_rva_foreach_heap      = 0;
static uintptr_t g_rva_domain_stub       = 0;
static uintptr_t g_rva_get_assemblies    = 0;
static uintptr_t g_rva_class_from_name   = 0;
static uintptr_t g_rva_thread_attach     = 0;
static uintptr_t g_rva_thread_current    = 0;
static uintptr_t g_rva_string_new        = 0;
static uintptr_t g_rva_typeinfo_table    = 0;
static uintptr_t g_rva_interp_exec       = 0;
static uintptr_t g_rva_interp_image_load = 0;

// 诊断开关（只在 probe 阶段打开，生产路径默认关，避免日志洪水）
static volatile bool g_traceInvoke = false;   // invoke 逐行打日志，定位卡在哪一步
static volatile bool g_skipAttach  = false;   // 跳过 ensure_thread()→il2cpp_thread_attach

void set_trace_invoke(bool on) { g_traceInvoke = on; }
void set_skip_attach(bool on)  { g_skipAttach  = on; }

static std::mutex g_mtx;
static std::string g_last_err;

// ==================================================================== /proc/self/maps
namespace {

struct Range { uintptr_t a, b; bool r, w, x; char name[96]; };

static std::vector<Range> g_ranges;
static std::once_flag     g_maps_once;

static uint64_t now_ms() {
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (uint64_t) ts.tv_sec * 1000ull + (uint64_t) (ts.tv_nsec / 1000000);
}

static void reload_maps() {
    g_ranges.clear();
    FILE* f = fopen("/proc/self/maps", "r");
    if (!f) return;
    char line[512];
    while (fgets(line, sizeof(line), f)) {
        unsigned long long s = 0, e = 0;
        char perms[8] = {0};
        if (sscanf(line, "%llx-%llx %7s", &s, &e, perms) != 3) continue;
        Range r;
        r.a = (uintptr_t) s; r.b = (uintptr_t) e;
        r.r = perms[0] == 'r'; r.w = perms[1] == 'w'; r.x = perms[2] == 'x';
        r.name[0] = 0;
        // 抓第 6 列（路径 / [anon:xxx] / [stack] ……），用于区分「malloc 堆」与「so 数据段」
        int col = 0; char* p = line;
        while (*p && col < 5) {                       // 跳过前 5 列
            while (*p && *p != ' ') ++p;
            while (*p == ' ') ++p;
            ++col;
        }
        if (*p) {
            size_t n = strlen(p);
            while (n && (p[n - 1] == '\n' || p[n - 1] == '\r' || p[n - 1] == ' ')) --n;
            if (n >= sizeof(r.name)) n = sizeof(r.name) - 1;
            memcpy(r.name, p, n); r.name[n] = 0;
        }
        g_ranges.push_back(r);
    }
    fclose(f);
}

// want: 1=可读 2=可执行 3=可读写
static thread_local size_t t_last_range = (size_t) -1;   // 缓存上次命中的区间（对象/klass 局部性极强）

// ★★ Android 14+ 的堆指针带 top-byte tag（实测 il2cpp_domain_get_assemblies 返回
//    0xb4000074ae9e9ba0，真实地址是 0x0074ae9e9ba0）。带 tag 的地址拿去查 /proc/self/maps
//    永远查不到 → 之前所有 ptr_at() 都返回 0，assemblies 数组看起来"全是空指针"。
//    这里统一在查表入口剥掉 tag；不带 tag 的地址（如 base+RVA）原样返回。
static inline uintptr_t untag(uintptr_t p) {
    if (p >= 0x0100000000000000ull) return p & 0x00FFFFFFFFFFFFFFull;
    return p;
}

static std::mutex         g_maps_mtx;

static bool mapped(uintptr_t addr, size_t len, int want) {
    addr = untag(addr);
    if (addr < 0x10000ull || addr >= 0x0001000000000000ull || !len) return false;
    uintptr_t end = addr + len;
    if (end < addr) return false;

    std::lock_guard<std::mutex> lk(g_maps_mtx);
    std::call_once(g_maps_once, reload_maps);

    if (t_last_range < g_ranges.size()) {
        const Range& r = g_ranges[t_last_range];
        if (addr >= r.a && end <= r.b && (!(want & 1) || r.r) && (!(want & 2) || r.x))
            return true;
    }
    for (size_t i = 0; i < g_ranges.size(); ++i) {
        const Range& r = g_ranges[i];
        if (addr >= r.a && end <= r.b) {
            if ((want & 1) && !r.r) continue;
            if ((want & 2) && !r.x) continue;
            t_last_range = i;
            return true;
        }
    }
    // 堆/大数组随时 mmap：没命中时最多每 200ms 重扫一次 maps
    static uint64_t last_reload = 0;
    uint64_t t = now_ms();
    if (t - last_reload > 200) {
        last_reload = t;
        reload_maps();
        t_last_range = (size_t) -1;
        for (size_t i = 0; i < g_ranges.size(); ++i) {
            const Range& r = g_ranges[i];
            if (addr >= r.a && end <= r.b) {
                if ((want & 1) && !r.r) continue;
                if ((want & 2) && !r.x) continue;
                t_last_range = i;
                return true;
            }
        }
    }
    return false;
}

static inline uint64_t u64_at(uintptr_t a) { uint64_t v = 0; if (mapped(a, 8, 1)) memcpy(&v, (const void*) a, 8); return v; }
static inline uint32_t u32_at(uintptr_t a) { uint32_t v = 0; if (mapped(a, 4, 1)) memcpy(&v, (const void*) a, 4); return v; }
static inline uint16_t u16_at(uintptr_t a) { uint16_t v = 0; if (mapped(a, 2, 1)) memcpy(&v, (const void*) a, 2); return v; }
static inline uint8_t  u8_at (uintptr_t a) { uint8_t  v = 0; if (mapped(a, 1, 1)) memcpy(&v, (const void*) a, 1); return v; }
static inline void*    ptr_at(uintptr_t a) { return (void*) (uintptr_t) u64_at(a); }

// 只认「可打印 ASCII + NUL 结尾、长度 1..maxlen」，用来把 const char* 与 Il2CppString* 区分开
static bool is_cstr(const char* s, size_t maxlen = 160) {
    if (!s) return false;
    uintptr_t p = (uintptr_t) s;
    if (!mapped(p, 1, 1)) return false;
    for (size_t i = 0; i < maxlen; ++i) {
        if (!mapped(p + i, 1, 1)) return false;
        unsigned char c = *(const unsigned char*) (p + i);
        if (!c) return i > 0;
        if (c < 0x20 || c > 0x7e) return false;
    }
    return false;   // 160 字节还没遇到 NUL：不是名字
}

static const char* kstr(const char* s) { return is_cstr(s) ? s : "(?)"; }

} // anonymous namespace

uintptr_t base()   { return g_base; }
void*     domain() { return g_domain; }
const char* last_error() { return g_last_err.c_str(); }
bool is_readable(uintptr_t addr, size_t len) { return mapped(addr, len, 1); }

static bool is_readable_fresh(uintptr_t addr, size_t len) {
    if (mapped(addr, len, 1)) return true;
    {
        std::lock_guard<std::mutex> lk(g_maps_mtx);
        reload_maps();
        t_last_range = (size_t) -1;
    }
    return mapped(addr, len, 1);
}

// ★ libil2cpp.so 由易盾自己 mmap 进来，不在 linker 的 soinfo 链表里 —— dl_iterate_phdr 枚举不到它，
//   所以任何「用 phdr 的 p_flags&PF_X 判断是否可 hook」的代码对 libil2cpp 恒为 false。
//   （mod_hooks.cpp 第一版就是这么写的，等于所有目标都判成 no-aot。此处统一改走 /proc/self/maps。）
static bool is_houdini_guest() {
    // Under libhoudini (x86_64 emulators like MuMu), libil2cpp.so guest code pages are mapped r--p (no x bit).
    // On native ARM64 devices, IL2CPP .text code pages are mapped r-xp.
    uintptr_t probe = g_rva_thread_current ? (g_base + g_rva_thread_current)
                    : (g_rva_runtime_invoke ? (g_base + g_rva_runtime_invoke) : 0);
    return g_base && probe && !mapped(probe, 4, 3) && mapped(probe, 4, 1);
}

bool is_executable(uintptr_t addr) {
    if (mapped(addr, 4, 3)) return true;
    return is_houdini_guest()
        && addr >= g_base + 0x00400000ull
        && addr <  g_base + 0x04000000ull
        && mapped(addr, 4, 1);
}

// 热更 image（Assembly-CSharp.dll 的 Il2CppImage*）。热更加载完成前恒为 nullptr。
static void* g_hotImage = nullptr;

static void set_error(const char* fmt, ...) __attribute__((format(printf, 1, 2)));
static void set_error(const char* fmt, ...) {
    char buf[256];
    va_list ap;
    va_start(ap, fmt);
    vsnprintf(buf, sizeof(buf), fmt, ap);
    va_end(ap);
    g_last_err = buf;
    LOGE("%s", buf);
}

// ==================================================================== 初始化
//
// ★★ 2026-10-29 关键设计变更：彻底不 dlopen、不 dlsym。
//
// 触发面证据（PROGRESS.md 6.38 的真机日志）：
//     AresBridge: dlopen via maps path: .../libil2cpp.so -> 0x8d5e10dfa6e8a089
//     Zygote:     Process 4980 exited due to signal 9 (Killed)
// dlopen 之前一切正常，dlopen 返回之后进程立刻 SIGKILL。
//
// 原因（推断，但与全部观测一致）：libil2cpp.so 是易盾自己的加载链 mmap 进来的，
// 不在 Android linker 的 soinfo 表里 —— 「按名 dlopen 失败、只有按绝对路径才成功」
// 本身就是证据。因此第二次按绝对路径 dlopen 会在 linker 里新建一份 soinfo、把整个
// so 重新映射一遍、再跑一遍 .init_array，等于在同一进程里造出第二份 IL2CPP 全局状态。
// 这不需要任何 watchdog 也会炸；而壳里实测确实有 fork+pipe+kill 三件套。
//
// 因此改成「零调用取址」——全部从 maps 拿基址，再用 dump 里反汇编确证的 RVA 硬算：
//    base          /proc/self/maps 里文件偏移最小的那一段起点（= ELF 头段）
//    domain        纯读 *(void**)(base + 0x36b8ef0)
//                  依据 0xa60ca4: b 0xa92f68 → 0xa92f68:
//                      adrp x19,#0x36b8000 ; ldr x0,[x19,#0xef0] ; cbnz ; mov w0,#0x30 ; bl malloc ; str
//                  dump 实测该全局 = 0x7ad4a0dfc0（非零、合法堆指针），与 malloc(0x30) 完全吻合
//    invoke        base + 0xaa0e90   Runtime::Invoke(method,obj,params,exc)
//    class_init    base + 0xa8e574   Class::Init(klass)
//    foreach_heap  base + 0xa60f08   il2cpp_gc_foreach_heap 桩体（直接可调，签名 (cb,ud)）
//
// ==================================================================== 动态 RVA 解析（零硬编码）
// 官服(2.2.1)与各渠道服(2.2.0等)的 libil2cpp.so 基址及内部偏移存在数 KB~数 MB 漂移，
// 且部分符号名会被 HTProtect 在 .dynsym 中重命名；统一通过可读 .text 段 ARM64 指令指纹
// 一次性动态定位全部入口与全局变量槽，兼容任意渠道服与热更新母包构建。

static inline uintptr_t decode_b_target(uint32_t w, uintptr_t rva) {
    if ((w & 0xfc000000u) != 0x14000000u) return 0;
    int32_t imm = (int32_t) (w & 0x03ffffffu);
    if (imm & 0x02000000) imm -= 0x04000000;
    return (uintptr_t) ((intptr_t) rva + (intptr_t) imm * 4);
}

static uintptr_t find_adrp_ldr64_rva(uintptr_t start_rva, size_t max_insns) {
    uint64_t page[31] = {};
    bool has_page[31] = {};
    for (size_t k = 0; k < max_insns; ++k) {
        uintptr_t pc = start_rva + k * 4;
        if (!mapped(g_base + pc, 4, 1)) break;
        uint32_t insn = *(const uint32_t*) (g_base + pc);
        if ((insn & 0x9f000000u) == 0x90000000u) {
            uint32_t rd = insn & 31u;
            if (rd < 31u) {
                int64_t imm = (int64_t) ((((insn >> 5) & 0x7ffffu) << 2) | ((insn >> 29) & 3u));
                if (imm & 0x100000) imm -= 0x200000;
                page[rd] = (uint64_t) ((int64_t) (pc & ~0xfffULL) + (imm << 12));
                has_page[rd] = true;
            }
        } else if ((insn & 0xffc00000u) == 0xf9400000u) {
            uint32_t rt = insn & 31u;
            uint32_t rn = (insn >> 5) & 31u;
            uint32_t imm12 = (insn >> 10) & 0xfffu;
            if (rn < 31u && has_page[rn]) {
                uintptr_t target = (uintptr_t) (page[rn] + (uint64_t) imm12 * 8ULL);
                if (target >= 0x01000000ull && target < 0x06000000ull) return target;
            }
            if (rt < 31u) has_page[rt] = false;
        }
    }
    return 0;
}

static bool resolve_il2cpp_rvas() {
    if (!g_base) return false;
    if (g_rva_runtime_invoke && g_rva_domain_ptr && g_rva_domain_stub && g_rva_class_from_name
        && g_rva_thread_attach && g_rva_string_new && g_rva_interp_exec && g_rva_interp_image_load) {
        return true;
    }

    const uintptr_t scanStart = 0x00200000ull;
    const uintptr_t scanEnd   = 0x02200000ull;
    bool pageOk = false;

    for (uintptr_t rva = scanStart; rva < scanEnd; rva += 4) {
        if ((rva & 0xfffu) == 0) {
            pageOk = mapped(g_base + rva, 0x1000, 1);
            if (!pageOk) { rva += 0xffc; continue; }
        }
        if ((rva & 0xfffu) > 0xfb0u && !mapped(g_base + rva, 80, 1)) continue;

        const uint32_t* w = (const uint32_t*) (g_base + rva);

        if (!g_rva_domain_stub
            && (w[0] & 0xfc000000u) == 0x14000000u
            && w[1] == 0xaa0103e0u
            && (w[2] & 0xfc000000u) == 0x14000000u
            && w[3] == 0xf81e0ff3u && w[4] == 0xa9017bfdu
            && w[5] == 0x910043fdu && w[6] == 0xaa0103f3u) {
            uintptr_t body = decode_b_target(w[0], rva);
            uintptr_t dp = body ? find_adrp_ldr64_rva(body, 16) : 0;
            if (dp) {
                g_rva_domain_stub    = rva;
                g_rva_get_assemblies = rva + 0x0C;
                g_rva_domain_ptr     = dp;
            }
        } else if (!g_rva_foreach_heap
            && w[0] == 0xd10083ffu && w[1] == 0xa9017bfdu && w[2] == 0x910043fdu
            && w[3] == 0xa90007e0u
            && (w[4] & 0x9f00001fu) == 0x90000001u
            && (w[5] & 0xffc003ffu) == 0xf9400021u) {
            g_rva_foreach_heap = rva;
        } else if (!g_rva_class_init
            && w[0] == 0xd10103ffu && w[1] == 0xa90157f6u && w[2] == 0xa9024ff4u
            && w[3] == 0xa9037bfdu && w[4] == 0x9100c3fdu && w[5] == 0x79423808u
            && w[6] == 0xaa0003f3u && w[7] == 0x35000068u && w[8] == 0x3944ba68u
            && w[9] == 0x34000588u) {
            g_rva_class_init = rva;
        } else if (!g_rva_runtime_invoke
            && w[0] == 0xa9bd57f6u && w[1] == 0xa9014ff4u && w[2] == 0xa9027bfdu
            && w[3] == 0x910083fdu && w[4] == 0xaa0303f3u && w[5] == 0xaa0203f4u
            && w[6] == 0xaa0103f5u && w[7] == 0xaa0003f6u && w[8] == 0xb4000083u
            && w[9] == 0xaa1303e0u && w[10] == 0xaa1f03e1u
            && (w[11] & 0xfc000000u) == 0x94000000u
            && w[12] == 0xaa1603e0u
            && (w[13] & 0xfc000000u) == 0x94000000u
            && w[14] == 0x394112c8u && w[15] == 0x36200108u) {
            g_rva_runtime_invoke = rva;
        } else if (!g_rva_class_from_name
            && w[0] == 0x14000001u && w[1] == 0xd10243ffu && w[2] == 0xa90657f6u
            && w[3] == 0xa9074ff4u && w[4] == 0xa9087bfdu && w[5] == 0x910203fdu
            && w[6] == 0xaa0003f3u && w[7] == 0xf9401800u && w[8] == 0xaa0203f4u
            && w[9] == 0xaa0103f5u && (w[12] & 0xfc000000u) == 0x94000000u
            && w[13] == 0xf9401a60u) {
            g_rva_class_from_name = rva;
        } else if (!g_rva_thread_attach
            && w[0] == 0xd10103ffu && w[1] == 0xa90157f6u && w[2] == 0xa9024ff4u
            && w[3] == 0xa9037bfdu && w[4] == 0x9100c3fdu
            && (w[5] & 0x9f00001fu) == 0x90000008u
            && (w[6] & 0xffc003ffu) == 0xf9400108u
            && w[7] == 0xaa0003f3u && w[8] == 0xb9400108u && w[9] == 0x2a0803e0u
            && (w[10] & 0xfc000000u) == 0x94000000u
            && w[11] == 0xaa0003f4u && w[12] == 0xb5000660u
            && w[13] == 0x910033e0u && w[14] == 0xb9000fffu) {
            g_rva_thread_attach = rva;
        } else if (!g_rva_thread_current
            && (w[0] & 0x9f00001fu) == 0x90000008u
            && (w[1] & 0xffc003ffu) == 0xf9400108u
            && w[2] == 0xb9400100u
            && (w[3] & 0xfc000000u) == 0x14000000u
            && w[4] == 0xa9bf7bfdu && w[5] == 0x910003fdu) {
            g_rva_thread_current = rva;
        } else if (!g_rva_string_new
            && w[0] == 0xf81e0ff3u && w[1] == 0xa9017bfdu && w[2] == 0x910043fdu
            && w[3] == 0xaa0003f3u && (w[4] & 0xfc000000u) == 0x94000000u
            && w[5] == 0xa9417bfdu && w[6] == 0xaa0003e1u && w[7] == 0xaa1303e0u
            && w[8] == 0xf84207f3u && w[9] == 0x17ffffcdu && w[10] == 0xa9bd57f6u
            && w[11] == 0xa9014ff4u) {
            g_rva_string_new = rva;
        } else if (!g_rva_typeinfo_table
            && w[0] == 0xd10143ffu && w[1] == 0xa9015ff8u && w[2] == 0xa90257f6u
            && w[3] == 0xa9034ff4u && w[4] == 0xa9047bfdu && w[5] == 0x910103fdu
            && w[6] == 0x2a0003f3u && w[7] == 0x3100041fu && w[8] == 0x54000140u
            && w[9] == 0x531c7e68u && w[10] == 0x34000108u && w[11] == 0x2a1303e0u) {
            g_rva_typeinfo_table = find_adrp_ldr64_rva(rva, 32);
        } else if (!g_rva_interp_exec
            && w[0] == 0xa9ba6ffcu && w[1] == 0xa90167fau && w[2] == 0xa9025ff8u
            && w[3] == 0xa90357f6u && w[4] == 0xa9044ff4u && w[5] == 0xa9057bfdu
            && w[6] == 0x910143fdu && w[7] == 0xd14007ffu && w[8] == 0xd11043ffu
            && w[9] == 0xaa0203f3u && w[10] == 0xaa0103f4u && w[11] == 0xaa0003f5u) {
            g_rva_interp_exec = rva;
        } else if (!g_rva_interp_image_load
            && (w[0] & 0x9f00001fu) == 0x90000011u
            && (w[1] & 0xffc003ffu) == 0x91000231u
            && w[2] == 0xd61f0220u && w[3] == 0x320003e9u && w[4] == 0x39058009u
            && w[5] == 0xaa0803e0u && (w[6] & 0xfc000000u) == 0x14000000u
            && w[7] == 0xa9bf7bfdu && w[8] == 0x910003fdu) {
            g_rva_interp_image_load = rva;
        }

        if (g_rva_domain_ptr && g_rva_runtime_invoke && g_rva_class_init
            && g_rva_foreach_heap && g_rva_domain_stub && g_rva_class_from_name
            && g_rva_thread_attach && g_rva_thread_current && g_rva_string_new
            && g_rva_typeinfo_table && g_rva_interp_exec && g_rva_interp_image_load) {
            break;
        }
    }

    LOGI("resolve_rvas: base=%p domain=%#lx invoke=%#lx init=%#lx heap=%#lx stub=%#lx cfn=%#lx ta=%#lx tc=%#lx sn=%#lx ti=%#lx iexec=%#lx iload=%#lx",
         (void*) g_base, (unsigned long) g_rva_domain_ptr, (unsigned long) g_rva_runtime_invoke,
         (unsigned long) g_rva_class_init, (unsigned long) g_rva_foreach_heap,
         (unsigned long) g_rva_domain_stub, (unsigned long) g_rva_class_from_name,
         (unsigned long) g_rva_thread_attach, (unsigned long) g_rva_thread_current,
         (unsigned long) g_rva_string_new, (unsigned long) g_rva_typeinfo_table,
         (unsigned long) g_rva_interp_exec, (unsigned long) g_rva_interp_image_load);

    return g_rva_runtime_invoke != 0 && g_rva_domain_ptr != 0;
}

// 取 so 加载基址：maps 里文件偏移最小那一段的起点。不经过 linker，零 dlopen/dlsym。
static uintptr_t find_module_base(const char* soname) {
    FILE* f = fopen("/proc/self/maps", "r");
    if (!f) return 0;
    char line[1024];
    uintptr_t best = 0;
    unsigned long long best_off = ~0ull;
    const size_t sl = strlen(soname);
    while (fgets(line, sizeof(line), f)) {
        char* p = strstr(line, soname);
        if (!p) continue;
        char* e = p + sl;                                  // 必须是行内最后一个 token
        while (*e == ' ' || *e == '\t' || *e == '\r' || *e == '\n') ++e;
        if (*e) continue;                                  // 排除 xxx.so.bak / xxx.so.1
        unsigned long long s = 0, en = 0, off = 0;
        char perms[8] = {0};
        if (sscanf(line, "%llx-%llx %7s %llx", &s, &en, perms, &off) != 4) continue;
        if (off < best_off) { best_off = off; best = (uintptr_t) s; }
    }
    fclose(f);
    return best;
}

static void ensure_thread() {
    // 所有 invoke 都从 UnityMain 线程（IL2CPP 初始化时已 attach）发起；
    // 先用动态解析的 il2cpp_thread_current 判当前线程是否已 attach，已 attach 则零调用直接返回。
    if (t_attached || !p_thread_attach || !g_domain) {
        if (g_traceInvoke)
            LOGI("ensure_thread: skip (t_attached=%d fn=%p domain=%p)",
                 (int) t_attached, (void*) p_thread_attach, g_domain);
        return;
    }
    if (p_thread_current && is_executable((uintptr_t) p_thread_current)) {
        void* cur = p_thread_current();
        if (cur) {
            t_attached = true;
            LOGI("ensure_thread: already attached on UnityMain (%p)", cur);
            return;
        }
    }
    LOGI("ensure_thread: >>> il2cpp_thread_attach(%p)  (桩 %p)", g_domain, (void*) p_thread_attach);
    p_thread_attach(g_domain);
    t_attached = true;
    LOGI("ensure_thread: <<< il2cpp_thread_attach 返回");
}

bool init() {
    std::lock_guard<std::mutex> lk(g_mtx);
    if (g_inited) return g_ready;
    // 失败时不置 g_inited，允许 il2cpp 解密并起来后重试

    if (!g_base) g_base = find_module_base("/libil2cpp.so");
    if (!g_base) { LOGI("init: libil2cpp.so 还没映射，稍后重试"); return false; }

    if (!resolve_il2cpp_rvas()) {
        static int s_failCnt = 0;
        if (s_failCnt++ < 5) {
            LOGI("init: 动态指纹解析尚未就绪（易盾解密或初始化进行中），稍后重试");
        }
        set_error("动态解析 IL2CPP 入口尚未就绪");
        return false;
    }

    bool have_stub  = g_rva_domain_stub != 0;
    bool have_init  = g_rva_class_init  != 0;
    bool have_heap  = g_rva_foreach_heap != 0;

    // ---- domain：纯读全局，不调用任何函数
    uintptr_t addr_domain = g_base + g_rva_domain_ptr;
    if (!mapped(addr_domain, 8, 1)) {
        set_error("domain 全局 @+0x%lx 不可读", (unsigned long) g_rva_domain_ptr);
        return false;
    }
    void* d = *(void**) addr_domain;
    if (!d) { LOGI("init: domain 尚未建立（IL2CPP 还没初始化），稍后重试"); return false; }
    g_domain = d;

    // ---- 入口一律 base + 动态解析出的 RVA
    p_invoke          = (Fn_invoke)         (g_base + g_rva_runtime_invoke);
    p_foreach_heap    = have_heap ? (Fn_foreach_heap) (g_base + g_rva_foreach_heap) : nullptr;
    p_class_init      = have_init ? (Fn_class_init)   (g_base + g_rva_class_init)   : nullptr;
    p_domain_get      = have_stub ? (Fn_domain_get)   (g_base + g_rva_domain_stub)  : nullptr;
    p_get_assemblies  = g_rva_get_assemblies  ? (Fn_get_assemblies) (g_base + g_rva_get_assemblies)  : nullptr;
    p_class_from_name = g_rva_class_from_name ? (Fn_class_from_name)(g_base + g_rva_class_from_name) : nullptr;
    p_thread_attach   = g_rva_thread_attach   ? (Fn_thread_attach)  (g_base + g_rva_thread_attach)   : nullptr;
    p_thread_current  = g_rva_thread_current  ? (Fn_thread_current) (g_base + g_rva_thread_current)  : nullptr;
    p_string_new      = g_rva_string_new      ? (Fn_string_new)     (g_base + g_rva_string_new)      : nullptr;
    p_value_box       = nullptr;
    p_header_size     = nullptr;
    p_format_exc      = nullptr;
    p_format_trace    = nullptr;

    g_inited = true;
    t_attached = false;
    ensure_thread();
    g_ready = true;

    LOGI("  have: stub=%d init=%d heap=%d cfn=%d sn=%d tc=%d  (class_init 为 NULL 时所有 Class::Init 都会被跳过)",
         (int) have_stub, (int) have_init, (int) have_heap,
         (int) (p_class_from_name != nullptr), (int) (p_string_new != nullptr), (int) (p_thread_current != nullptr));
    LOGI("init ok (DYNAMIC ZERO-HARDCODE): base=%p domain=%p (read @+0x%lx) invoke=%p class_init=%p foreach_heap=%p",
         (void*) g_base, g_domain, (unsigned long) g_rva_domain_ptr,
         (void*) p_invoke, (void*) p_class_init, (void*) p_foreach_heap);
    LOGI("  ptr=%p maps_first=%p  domain[0..3]=%016llx %016llx %016llx",
         (void*) p_invoke, (void*) find_module_base("/libil2cpp.so"),
         (unsigned long long) u64_at((uintptr_t) g_domain + 0),
         (unsigned long long) u64_at((uintptr_t) g_domain + 8),
         (unsigned long long) u64_at((uintptr_t) g_domain + 16));
    return true;
}

// ==================================================================== 扫堆 → klass
namespace {

static const size_t SECTION_MAX   = 512;                       // 最多记 512 个堆段
static const size_t SCAN_BYTE_CAP = 160u * 1024 * 1024;        // 最多扫 160 MB
static const size_t CAND_CAP      = 300000;

struct HeapSection { uintptr_t start, end; };
static HeapSection g_sections[SECTION_MAX];
static size_t      g_section_n = 0;

static std::vector<void*>            g_klasses;             // 去重后的 klass 全表
static std::unordered_map<std::string, void*> g_by_name;    // 简单名
static std::unordered_map<std::string, void*> g_by_full;    // ns.name
static bool     g_scanned = false;
static uint64_t g_scan_ms = 0;
static size_t   g_scan_bytes = 0;

// userData 哨兵：用来在回调里区分「第一参是 section 指针」还是「{start,size} 按值展开」
static void* const kScanUser = (void*) &g_section_n;

// 已复核的 ABI（_tier2_verify_offsets.md，寄存器级）：
//   typedef struct { void* start; size_t size; } Il2CppGCHeapSection;
//   typedef void (*Il2CppGCHeapSectionCallback)(const Il2CppGCHeapSection*, void* userData);
//   void il2cpp_gc_foreach_heap(Il2CppGCHeapSectionCallback callback, void* userData);
// 要点：① 第 1 参是回调、第 2 参是 userData（不是反过来）；② 回调拿到的是「堆段」而不是对象。
//       依据：0xa60f08 的 stp x0,x1,[sp] 打包 → 内部 bfe57c 把 pair 给 x19=0xa9b7a4，
//       0xa9b7b8 ldp x8,x1,[x0] 从 pair 取 {回调, userData} 再 blr，段描述 {start,size}
//       由 0xa9b7b0 sub / 0xa9b7b4 stp 现场构建。
// 所以段内必须自己按 8 字节步长找 klass（对象首字 = klass*）。
static void heap_section_cb(const void* sec, void* user) {
    uintptr_t start = 0, size = 0;
    if (user == kScanUser) {
        if (!sec || !mapped((uintptr_t) sec, 16, 1)) return;
        uint64_t s64 = 0, n64 = 0;                 // 段字段是 size_t（64 位 ABI 下 8 字节）
        memcpy(&s64, sec, 8);
        memcpy(&n64, (const char*) sec + 8, 8);
        start = (uintptr_t) s64;
        size  = (uintptr_t) n64;
    } else {
        uintptr_t b = (uintptr_t) user;
        if (b > 0x10000000ull) return;      // 第二参像指针 → 不是「按值展开」形态
        start = (uintptr_t) sec;
        size = b;
    }
    if (!start || !size || size > ((uintptr_t) 1 << 31)) return;
    if (g_section_n < SECTION_MAX) {
        g_sections[g_section_n].start = start;
        g_sections[g_section_n].end   = start + size;
        ++g_section_n;
    }
}

// klass 指纹：结构自洽优先（便宜），名称最后查（贵）
static bool is_klass(void* k) {
    if (!k) return false;
    uintptr_t a = (uintptr_t) k;
    if (a & 7) return false;
    if (!mapped(a, 0x140, 1)) return false;                 // 需要读到 flags(0x132) 之后
    uint16_t mc = u16_at(a + OFF_KLASS_MCOUNT);
    if (mc > 8192) return false;
    void* methods = ptr_at(a + OFF_KLASS_METHODS);
    if (mc && !methods) return false;
    if (mc) {
        for (unsigned i = 0; i < mc && i < 8; ++i) {
            uintptr_t mi = (uintptr_t) ptr_at((uintptr_t) methods + i * 8);
            if (!mi) continue;
            if (ptr_at(mi + OFF_MI_KLASS) == k) {           // method->klass 指回自己 → 铁证
                const char* nm = (const char*) ptr_at(a + OFF_KLASS_NAME);
                return is_cstr(nm, 128);
            }
        }
    }
    const char* nm = (const char*) ptr_at(a + OFF_KLASS_NAME);
    return is_cstr(nm, 128);
}

static void add_klass_locked(void* k) {
    const char* nm = (const char*) ptr_at((uintptr_t) k + OFF_KLASS_NAME);
    const char* ns = (const char*) ptr_at((uintptr_t) k + OFF_KLASS_NS);
    if (!is_cstr(nm, 128)) return;
    g_klasses.push_back(k);
    std::string s(nm);
    if (!g_by_name.count(s)) g_by_name[s] = k;
    if (is_cstr(ns, 128) && ns[0]) {
        std::string full = std::string(ns) + "." + s;
        if (!g_by_full.count(full)) g_by_full[full] = k;
    }
}

static void scan_heap_locked() {
    if (!p_foreach_heap) { set_error("il2cpp_gc_foreach_heap 不可用"); return; }
    g_section_n = 0;
    reload_maps();
    ensure_thread();
    p_foreach_heap(heap_section_cb, kScanUser);
    if (g_section_n == 0) {
        set_error("扫堆没拿到 heap section（ABI 或时机问题，首次应在进主场景后调用）");
        return;
    }

    std::unordered_set<uintptr_t> seen;
    seen.reserve(8192);
    g_scan_bytes = 0;
    size_t sec_ok = 0;
    for (size_t i = 0; i < g_section_n && g_scan_bytes < SCAN_BYTE_CAP; ++i) {
        uintptr_t s = g_sections[i].start, e = g_sections[i].end;
        size_t len = (size_t) (e - s);
        if (len < 0x1000) continue;
        if (!mapped(s, len, 1)) continue;          // 整段验一次，之后直接读
        ++sec_ok;
        g_scan_bytes += len;
        for (uintptr_t p = (s + 7) & ~(uintptr_t) 7; p + 8 <= e; p += 8) {
            uint64_t w;
            memcpy(&w, (const void*) p, 8);
            uintptr_t W = (uintptr_t) w;
            if (!W || (W & 7) || W < 0x10000) continue;
            if (!mapped(W, 0x140, 1)) continue;    // klass 必须能被读到 flags 之后
            uint16_t mc;
            memcpy(&mc, (const void*) (W + OFF_KLASS_MCOUNT), 2);
            if (mc > 4096) continue;               // 便宜预筛：方法数不合理就不是 klass
            if (seen.size() >= CAND_CAP) break;
            if (!seen.insert(W).second) continue;
            if (is_klass((void*) W)) add_klass_locked((void*) W);
        }
    }
    g_scanned = true;
    g_scan_ms = now_ms();
    LOGI("scan_heap: sections=%zu(ok=%zu) scanned=%zuMB klasses=%zu names=%zu",
         g_section_n, sec_ok, g_scan_bytes >> 20, g_klasses.size(), g_by_name.size());
}

// ---- native heap 直接扫 Il2CppClass ---------------------------------------------
//
// 为什么必须有这一条：游戏的玩法程序集 Assembly-CSharp 是 HybridCLR 热更程序集
// （Assembly.Load 进来的，见 src_out/AotDll/HotFixMgr.cs:300-304），它的 Il2CppClass
// 是运行时由 HybridCLR 现造的，落在 native malloc 区；而 il2cpp_gc_foreach_heap 只遍历
// Boehm GC 的托管堆，能看到的只是「堆上已有实例的对象头 klass」。
// 于是开局阶段（业务单例还没 new 出来）扫 GC 堆必然大面积 not-found —— 实测就是这样：
// scanned=59MB / klasses=433 / names=34，四个目标全 not-found。
//
// 这一条改成扫 /proc/self/maps 里所有 rw 的匿名段（libc_malloc / heap），
// 用 is_klass() 的「method->klass 指回自己」铁证做判定，与实例是否存在无关。
static bool   g_native_scanned = false;
static size_t g_native_bytes   = 0;

// 注意：这条路径实测会在扫描的几秒里撞上游戏侧 munmap（15:24 / 15:26 / 15:29 三次都崩在
// libmodmenu+0x846cc 附近），已从 find_class_by_name 摘掉，只保留代码备查。
__attribute__((unused))
static size_t scan_native_heap_locked() {
    reload_maps();
    const size_t CAP = 1200ull * 1024 * 1024;      // 上限 1.2 GB，够大又不会把游戏卡死太久
    size_t scanned = 0, found = 0;
    size_t c_cand = 0, c_name = 0, c_char = 0, c_mc = 0, c_map = 0;
    const size_t before = g_by_name.size();
    for (size_t i = 0; i < g_ranges.size() && scanned < CAP; ++i) {
        const Range& r = g_ranges[i];
        // 只扫可读写段。扫只读段时游戏会在扫描过程中 munmap/mmap（GC 堆、栈、
        // 资源映射都在动），maps 快照一旦过期就会 SIGSEGV —— 15:24 实测崩在
        // libmodmenu+0x846cc。实测 rw-only 版本稳定，就不再冒这个险。
        if (!(r.r && r.w)) continue;
        const char* nm = r.name;
        // 跳过明显无用的段；/data/ 不跳（metadata / hybridclr 补充元数据可能落在那里）
        if (strstr(nm, "[stack")) continue;
        if (strstr(nm, "/dev/"))  continue;
        if (strstr(nm, "/system/")) continue;
        if (strstr(nm, "/apex/"))  continue;
        if (strstr(nm, "/memfd"))  continue;
        if (strstr(nm, ".so"))     continue;
        if (strstr(nm, ".dex"))    continue;
        if (strstr(nm, ".oat"))    continue;
        if (strstr(nm, ".art"))    continue;
        size_t len = (size_t) (r.b - r.a);
        if (len < 0x1000) continue;
        if (len > 512ull * 1024 * 1024) continue;   // 单个超大映射跳过
        scanned += len;
        uintptr_t p = (r.a + 7) & ~(uintptr_t) 7;
        for (; p + 0x140 <= r.b; p += 8) {
            // ★ 逐页 mincore 探活：maps 快照只是扫描开始那一刻的样子，GC 堆 / 线程栈 /
            //   资源映射在扫描的几秒里会不断 munmap/mmap。15:24 与 15:26 两次实测都崩在
            //   libmodmenu+0x846cc，就是读到了已失效的页。mincore 是唯一不用改信号处理
            //   就能在解引用前知道「这一页还在不在」的办法。
            if ((p & 0xFFF) == 0) {
                unsigned char vec = 0;
                if (mincore((void*) p, 0x1000, &vec) != 0) break;   // 本段剩余已失效，放弃
            }
            ++c_cand;
            // ---- 便宜预筛：name 指针必须可读、首字符是标识符字符、方法数合理
            uintptr_t np;
            memcpy(&np, (const void*) (p + OFF_KLASS_NAME), 8);
            if (np < 0x10000) continue;
            if (!mapped(np, 2, 1)) continue;
            ++c_name;
            unsigned char c0 = *(const unsigned char*) np;
            if (!((c0 >= 'A' && c0 <= 'Z') || (c0 >= 'a' && c0 <= 'z') || c0 == '_')) continue;
            ++c_char;
            // 命中目标名但不是合法 klass 的候选也报出来，用来区分「没扫到」和「判定过严」
            {
                const char* s = (const char*) np;
                if (mapped(np, 16, 1)) {
                    char head[17]; memcpy(head, s, 16); head[16] = 0;
                    if (!strncmp(head, "CGameSystem", 11) || !strncmp(head, "CContainerS", 11) ||
                        !strncmp(head, "CEquipmentS", 11) || !strncmp(head, "CAntiCheatS", 11) ||
                        !strncmp(head, "CMaterial", 9)) {
                        LOGI("scan_native: 命中目标类名 '%s' @%p（候选 klass=%p）", head, (void*) np, (void*) p);
                    }
                }
            }
            uint16_t mc;
            memcpy(&mc, (const void*) (p + OFF_KLASS_MCOUNT), 2);
            if (!mc || mc > 4096) continue;
            ++c_mc;
            if (!mapped(p, 0x140, 1)) continue;
            ++c_map;
            void* k = (void*) p;
            if (is_klass(k)) { add_klass_locked(k); ++found; }
        }
    }
    g_native_scanned = true;
    g_native_bytes = scanned;
    LOGI("scan_native: scanned=%zuMB cand=%zu name_ok=%zu char_ok=%zu mc_ok=%zu map_ok=%zu found=%zu klasses=%zu names=%zu(+%zu)",
         scanned >> 20, c_cand, c_name, c_char, c_mc, c_map, found,
         g_klasses.size(), g_by_name.size(), g_by_name.size() - before);
    return found;
}

} // anonymous namespace

static bool ensure_scanned_locked(bool force) {
    if (!force && g_scanned) return true;
    scan_heap_locked();
    return g_scanned && !g_klasses.empty();
}

// ---- assemblies → image → class_from_name ---------------------------------------
//
// 这条是「按名字找类」的正路，完全不需要扫内存：
//   assemblies = il2cpp_domain_get_assemblies(domain,&n)   @0xa60cb0（返回 Il2CppAssembly**）
//   image      = *(void**)assembly                          （Il2CppAssembly+0x00 = Il2CppImage*）
//   image name = *(const char**)image                       （Il2CppImage+0x00 = name）
//   klass      = il2cpp_class_from_name(image, "", name)    @0xa606b0
// 依据来自 _sess/il2cpp_rva_report.md（逐条指令原文）。
// 局限：HybridCLR 热更程序集不一定进 domain 的 assembly 数组；找不到时退回扫 GC 堆。
static bool g_images_logged = false;

static void log_images_locked() {
    if (g_images_logged || !p_get_assemblies || !g_domain) return;
    g_images_logged = true;
    size_t n = 0;
    void** asms = p_get_assemblies(g_domain, &n);
    LOGI("assemblies: n=%zu base=%p", n, (void*) asms);
    if (!asms || n > 4096) return;
    for (size_t i = 0; i < n && i < 96; ++i) {
        void* a = (void*) ptr_at((uintptr_t) asms + i * 8);
        if (!a) continue;
        void* img = (void*) ptr_at((uintptr_t) a + 0);
        const char* nm = img ? (const char*) ptr_at((uintptr_t) img + 0) : nullptr;
        LOGI("  asm[%zu] image=%p name=%s", i, img, is_cstr(nm, 160) ? nm : "(?)");
    }
}

static void* find_class_via_images_ns(const char* name, const char* ns) {
    if (!p_get_assemblies || !p_class_from_name || !g_domain || !name) return nullptr;
    size_t n = 0;
    void** asms = p_get_assemblies(g_domain, &n);
    if (!asms || !n || n > 4096) return nullptr;
    for (size_t i = 0; i < n; ++i) {
        void* a = (void*) ptr_at((uintptr_t) asms + i * 8);
        if (!a) continue;
        void* img = (void*) ptr_at((uintptr_t) a + 0);
        if (!img) continue;
        void* k = p_class_from_name(img, ns ? ns : "", name);
        if (!k) continue;
        // 用 klass+0x10 复核名字，避免撞上同名不同命名空间
        const char* kn = (const char*) ptr_at((uintptr_t) k + OFF_KLASS_NAME);
        if (is_cstr(kn, 128) && strcmp(kn, name) == 0) return k;
    }
    return nullptr;
}

// 实测教训：只传空命名空间会漏掉 mscorlib/UnityEngine 里的类（Object/String/Debug/Assembly
// 全部 NOT FOUND），而 `HotFixMgr`（AotDll，无命名空间）能命中。所以按常见命名空间轮询。
static void* find_class_via_images(const char* name) {
    static const char* kNss[] = {
        "", "System", "System.Reflection", "UnityEngine", "System.IO",
        "YooAsset", "HybridCLR", "Mirror", "ET", "UnityEngine.UI"
    };
    for (const char* ns : kNss) {
        if (void* k = find_class_via_images_ns(name, ns)) return k;
    }
    return nullptr;
}

void* find_class_by_name(const char* name) {
    if (!name) return nullptr;
    if (!init()) return nullptr;
    std::lock_guard<std::mutex> lk(g_mtx);
    {
        auto it = g_by_name.find(name);
        if (it != g_by_name.end()) return it->second;
        auto it2 = g_by_full.find(name);
        if (it2 != g_by_full.end()) return it2->second;
    }
    // ① ★ 先查热更 image（Assembly-CSharp.dll）。7 个功能的目标类（CMaterial / CContainerSystem /
    //    CGameSystem / CEquipmentSystem / CAntiCheatSystem / CGameSceneMgr / CPropertyPart / CBuffPart）
    //    全部在这一个 image 里，而它**不在** il2cpp_domain_get_assemblies 的 66 项里
    //    （HybridCLR 的 Assembly.Load 只往 AppDomain 反射表里加，不加 domain 静态数组）。
    //    所以原来只走 find_class_via_images 时，热更类恒 NOT FOUND —— mod_hooks 的 12 个 hook
    //    目标全都装不上。这是 hooks 层真正的地基 bug。
    if (g_hotImage) {
        void* k = p_class_from_name(g_hotImage, "", name);
        if (k) {
            const char* kn = (const char*) ptr_at((uintptr_t) k + OFF_KLASS_NAME);
            if (is_cstr(kn, 128) && strcmp(kn, name) == 0) {
                if (p_class_init && !(u8_at((uintptr_t) k + 0x133) & 4)) p_class_init(k);
                g_by_name[name] = k;
                return k;
            }
        }
    }
    // ② 再走 AOT assemblies → image → class_from_name（零内存扫描、零风险）
    if (void* k = find_class_via_images(name)) {
        g_by_name[name] = k;
        return k;
    }
    // ③ 不再扫内存。实测结论（PROGRESS 6.40）：
    //    · il2cpp_gc_foreach_heap 只覆盖 Boehm GC 托管堆，热更（HybridCLR）类拿不到；
    //    · 扫的几秒里游戏不断 munmap/mmap，maps 快照过期就 SIGSEGV —— 15:24 / 15:26 /
    //      15:29 / 15:43 四次都崩在 libmodmenu+0x846cc~0x84940 这一段扫描代码里。
    // 热更加载前 g_hotImage 为空是**正常状态**，调用方应轮询重试而不是当成错误刷屏。
    if (!g_hotImage) {
        g_last_err = std::string("class not found: ") + name + " (热更 image 还没就绪)";
        return nullptr;
    }
    log_images_locked();
    set_error("class not found: %s (热更 image 与 66 个 AOT assembly 里都没有)", name);
    return nullptr;
}

static void log_klass_info(void* k);   // fwd

// ==================================================================== 裸调反射链
//
// 决定性事实（16:15 真机实测）：
//   · il2cpp_thread_attach 正常工作（不卡），attach 后 il2cpp_thread_current 从 0x0 变成 0x75fd6144e0；
//   · **裸调 MethodInfo+0x00 的 methodPointer 成功**：`((int(*)(void*))mp)(string)` 返回 5；
//   · 调 MethodInfo+0x08 的「invoker」则卡死。
// ⇒ methodPointer 是可信的裸函数指针（AOT 方法就是普通 AArch64 函数），
//   invoker 的调用约定与本项目不符，**彻底放弃 invoke 路径，全部改用裸调**。
//   AOT 裸调 ABI：静态无参 x0=返回值；静态 N 参 x0..=实参；实例无参 x0=this。
// 可重复调用：每轮只打一行摘要；只有当程序集数量变化时才打完整清单。
// 用途：Java 侧每秒 post 一次，跨越「热更加载」这个时间点（实测热更在 16:21:49 才开始，
// 而模块 16:21:34 就注入了 —— 中间 15 秒，所以首次探针必然看不到 Assembly-CSharp）。
static int g_lastAsmCount = -1;

void probe_reflection_raw() {
    if (!init()) { LOGE("probe_rr: init 失败"); return; }
    ensure_thread();

    typedef void* (*Fn_static0)();
    typedef void* (*Fn_inst0)(void*);

    std::lock_guard<std::mutex> lk(g_mtx);
    void* kAD = find_class_via_images_ns("AppDomain", "System");
    if (!kAD) { LOGE("probe_rr: System.AppDomain 未找到"); return; }
    void* mCur = find_method_raw(kAD, "get_CurrentDomain", 0);
    if (!mCur) { LOGE("probe_rr: get_CurrentDomain 未找到"); return; }
    void* mpCur = ptr_at((uintptr_t) mCur + OFF_MI_POINTER);
    if (g_lastAsmCount < 0) LOGI("probe_rr: get_CurrentDomain mp=%p，裸调 ...", mpCur);
    void* dom = ((Fn_static0) mpCur)();
    if (g_lastAsmCount < 0) LOGI("probe_rr: AppDomain = %p", dom);
    if (!dom) return;

    void* mAsm = find_method_raw(kAD, "GetAssemblies", 0);
    if (!mAsm) { LOGE("probe_rr: GetAssemblies 未找到"); return; }
    void* mpAsm = ptr_at((uintptr_t) mAsm + OFF_MI_POINTER);
    void* arr = ((Fn_inst0) mpAsm)(dom);
    if (!arr) { LOGE("probe_rr: GetAssemblies 返回 NULL"); return; }

    uint32_t len = u32_at((uintptr_t) arr + 0x18);
    if (len > 512) { LOGE("probe_rr: 长度异常 %u，放弃", len); return; }

    const bool changed = ((int) len != g_lastAsmCount);
    if (changed) {
        LOGI("probe_rr: 程序集数量 %d -> %u（domain 静态表是 66；多出来的就是热更加载进来的）",
             g_lastAsmCount, len);
        g_lastAsmCount = (int) len;
    }
    if (!changed) return;      // 数量没变就静默，避免每秒一条日志淹掉 logcat

    const char* want[] = {"Assembly-CSharp", "Code", "MonoHotUpdate", "AotDll"};
    for (uint32_t i = 0; i < len && i < 200; ++i) {
        void* asmObj = (void*) ptr_at((uintptr_t) arr + 0x20 + (uintptr_t) i * 8);
        if (!asmObj) continue;
        void* ia = (void*) ptr_at((uintptr_t) asmObj + 0x10);            // Il2CppReflectionAssembly+0x10
        void* img = ia ? (void*) ptr_at((uintptr_t) ia + 0) : nullptr;   // Il2CppAssembly+0x00 = image
        const char* nm = img ? (const char*) ptr_at((uintptr_t) img + 0) : nullptr;
        bool hit = false;
        if (is_cstr(nm, 96)) {
            if (!g_hotImage && strcmp(nm, "Assembly-CSharp.dll") == 0) {
                g_hotImage = img;
                LOGI("probe_rr: ★ cached Assembly-CSharp.dll image = %p", img);
            }
            for (const char* w : want) if (strstr(nm, w)) { hit = true; break; }
        }
        if (hit || i < 3 || i >= 66)
            LOGI("probe_rr: [%u] asmObj=%p il2Asm=%p img=%p name=%s%s", i, asmObj, ia, img,
                 is_cstr(nm, 96) ? nm : "(?)", hit ? "   *** 命中热更程序集 ***" : "");
    }
}

// ==================================================================== 热更类定位（真解）
//
// ★ 16:23 真机实测推翻了之前所有「热更程序集不在 domain 列表」的结论 —— 真正原因只是
//   **探针跑得太早**。模块在 Activity.onCreate 就注入（16:21:34），而 HotFixMgr.StartLoad
//   到 16:21:49 / 16:23:35 才执行 Assembly.Load。轮询跨过那个点之后：
//       16:23:35.375  Unity: 加载热更程序集成功
//       16:23:36.003  probe_rr: 程序集数量 66 -> 68
//       16:23:36.005  probe_rr: [67] img=0xb40000741e8a04d0 name=Assembly-CSharp.dll   ★
//
//   所以 il2cpp_class_from_name(image, ns, name) 对热更 image 也应该成立 —— 只要等热更加载完。
//   本函数就是拿那个 image 去 class_from_name，并把目标类的全部方法（含 methodPointer/invoker/
//   是否在 r-x 段）打出来，这是判定「热更方法能不能裸调」的最终依据。
static bool g_hotProbed = false;

void probe_hotclass() {
    if (g_hotProbed) return;
    if (!init()) return;
    ensure_thread();

    typedef void* (*Fn_static0)();
    typedef void* (*Fn_inst0)(void*);
    typedef void* (*Fn_class_from_name)(void*, const char*, const char*);
    Fn_class_from_name cfn = p_class_from_name;
    if (!cfn) { LOGE("probe_rr: p_class_from_name 为空"); return; }

    std::lock_guard<std::mutex> lk(g_mtx);
    void* kAD = find_class_via_images_ns("AppDomain", "System");
    if (!kAD) return;
    void* mCur = find_method_raw(kAD, "get_CurrentDomain", 0);
    void* mAsm = find_method_raw(kAD, "GetAssemblies", 0);
    if (!mCur || !mAsm) return;
    void* dom = ((Fn_static0) ptr_at((uintptr_t) mCur + OFF_MI_POINTER))();
    if (!dom) return;
    void* arr = ((Fn_inst0) ptr_at((uintptr_t) mAsm + OFF_MI_POINTER))(dom);
    if (!arr) return;
    uint32_t len = u32_at((uintptr_t) arr + 0x18);
    if (len < 67) return;                       // 热更还没加载完，下次再来

    void* img = nullptr;
    for (uint32_t i = 0; i < len && i < 200; ++i) {
        void* asmObj = (void*) ptr_at((uintptr_t) arr + 0x20 + (uintptr_t) i * 8);
        if (!asmObj) continue;
        void* ia = (void*) ptr_at((uintptr_t) asmObj + 0x10);
        void* im = ia ? (void*) ptr_at((uintptr_t) ia + 0) : nullptr;
        const char* nm = im ? (const char*) ptr_at((uintptr_t) im + 0) : nullptr;
        if (is_cstr(nm, 96) && strcmp(nm, "Assembly-CSharp.dll") == 0) { img = im; break; }
    }
    if (!img) return;
    g_hotProbed = true;
    LOGI("probe_hc: ★ Assembly-CSharp image = %p（程序集总数 %u）", img, len);

    // class_from_name 的第 2/3 参是 **C 字符串**（不是 Il2CppString*），可以直接传字面量
    struct Want { const char* cls; const char* meth; int pc; };
    const Want wants[] = {
        {"CMaterial",         "SetEndurance",        2},
        {"CContainerSystem",  "IsOwnBlueprint",      2},
        {"CContainerSystem",  "AddMaterialToContainer", 5},
        {"CGameSceneMgr",     "GetCurrentChararcterCustomID", 0},
        {"CGameSystem",       nullptr,               0},
        {"CEquipmentSystem",  "ReduceAmmo",          2},
        {"CAntiCheatSystem",  "BlackListCheck",      0},
    };

    for (const Want& w : wants) {
        void* k = cfn(img, "", w.cls);
        if (!k) { LOGI("probe_hc: %s -> NOT FOUND", w.cls); continue; }
        uintptr_t kk = (uintptr_t) k;
        const char* kn = (const char*) ptr_at(kk + OFF_KLASS_NAME);
        bool inited = (u8_at(kk + 0x133) & 4) != 0;
        void* mtb = ptr_at(kk + OFF_KLASS_METHODS);
        LOGI("probe_hc: ★ %s -> klass=%p name=%s mcount=%u methods=%p initialized=%d",
             w.cls, k, is_cstr(kn, 96) ? kn : "(?)",
             (unsigned) u16_at(kk + OFF_KLASS_MCOUNT), mtb, inited ? 1 : 0);
        if (!mtb && p_class_init) {
            // HybridCLR 的类在 Class::Init 之前 klass->methods 是空的（方法表由 Init 链接）。
            // Class::Init 快路径：klass+0x133 bit2 已置位就直接返回，所以对 CGameSceneMgr /
            // CGameSystem 这类 initialized=1 的类是零成本；对 CMaterial 这类才真正执行一次。
            LOGI("probe_hc:   methods 为空，调 Class::Init(%p) ...", k);
            p_class_init(k);
            mtb = ptr_at(kk + OFF_KLASS_METHODS);
            LOGI("probe_hc:   Init 返回，methods=%p mcount=%u initialized=%d",
                 mtb, (unsigned) u16_at(kk + OFF_KLASS_MCOUNT),
                 (int) ((u8_at(kk + 0x133) >> 2) & 1));
        }
        if (!w.meth) continue;
        void* m = find_method_raw(k, w.meth, w.pc);
        if (!m) { LOGI("probe_hc:   %s/%d NOT FOUND", w.meth, w.pc); continue; }
        uintptr_t mi = (uintptr_t) m;
        void* mp  = ptr_at(mi + OFF_MI_POINTER);
        void* inv = ptr_at(mi + OFF_MI_INVOKER);
        bool rx   = mp && mapped((uintptr_t) mp, 4, 3);
        LOGI("probe_hc:   %s/%d mi=%p mp=%p in_rxp=%d invoker=%p",
             w.meth, w.pc, m, mp, rx ? 1 : 0, inv);
    }
}

// ==================================================================== 首个真实功能调用
//
// 16:28 真机实测的决定性结果：热更类全部拿到，而且**目标方法的 methodPointer 全在 r-x 段**：
//   SetEndurance/2                 mp=0x72840217b0 in_rxp=1
//   IsOwnBlueprint/2               mp=0x728400d0c0 in_rxp=1
//   AddMaterialToContainer/5       mp=0x7284038808 in_rxp=1   ← 与 ReduceAmmo 同一个 mp（共享桩）
//   GetCurrentChararcterCustomID/0 mp=0x7283ffefe8 in_rxp=1
//   ReduceAmmo/2                   mp=0x7284038808 in_rxp=1   ← 同上
//   BlackListCheck/0               mp=0x728401ff6c in_rxp=1
// 也就是说 Assembly-CSharp 的这批方法是 **AOT 编译**的（不是解释执行），可以像 String.get_Length
// 那样直接裸调 —— 之前「热更方法只能解释执行」的判断需要按方法逐个看 mp，不能一概而论。
//
// 本函数做最小闭环：CGameSceneMgr.Instance（静态属性）→ GetCurrentChararcterCustomID()（实例 0 参）
// 拿到当前角色的 nCustomID。这是发物品/属性/Buff 三个功能的前置。
// ==================================================================== 热更 image
//
// 为什么必须单独拿：`il2cpp_domain_get_assemblies()` 返回 66 项（全 AOT），热更程序集
// Assembly-CSharp / MonoHotUpdate 只出现在 `AppDomain.CurrentDomain.GetAssemblies()` 的
// 反射数组里（66 → 68）。两者是**不同的表**，这一点在 6.41–6.43 被反复误判成
// 「热更类结构上拿不到」，实际只是探针跑得比 HotFixMgr.StartLoad 早（6.44 已纠正）。
//
// 本函数把那条链固化成一次可重复调用：AppDomain → GetAssemblies → 逐项取 image name。
void* refresh_hot_image() {
    if (g_hotImage) return g_hotImage;
    if (!init()) return nullptr;
    ensure_thread();                   // attach 要在拿锁之前做（它内部会取运行时锁）
    std::lock_guard<std::mutex> lk(g_mtx);
    if (g_hotImage) return g_hotImage;

    void* kAD = find_class_via_images_ns("AppDomain", "System");
    if (!kAD) return nullptr;
    void* mCur = find_method_raw(kAD, "get_CurrentDomain", 0);
    void* mAsm = find_method_raw(kAD, "GetAssemblies", 0);
    if (!mCur || !mAsm) return nullptr;

    typedef void* (*Fn_static0)();
    typedef void* (*Fn_inst0)(void*);
    void* dom = ((Fn_static0) ptr_at((uintptr_t) mCur + OFF_MI_POINTER))();
    if (!dom) return nullptr;
    void* arr = ((Fn_inst0) ptr_at((uintptr_t) mAsm + OFF_MI_POINTER))(dom);
    if (!arr) return nullptr;
    uint32_t len = u32_at((uintptr_t) arr + 0x18);
    if (len < 60 || len > 512) return nullptr;

    for (uint32_t i = 0; i < len; ++i) {
        void* asmObj = (void*) ptr_at((uintptr_t) arr + 0x20 + (uintptr_t) i * 8);
        if (!asmObj) continue;
        void* ia = (void*) ptr_at((uintptr_t) asmObj + 0x10);            // Il2CppReflectionAssembly+0x10
        void* im = ia ? (void*) ptr_at((uintptr_t) ia + 0) : nullptr;    // Il2CppAssembly+0x00 = image
        const char* nm = im ? (const char*) ptr_at((uintptr_t) im + 0) : nullptr;
        if (is_cstr(nm, 96) && strcmp(nm, "Assembly-CSharp.dll") == 0) {
            g_hotImage = im;
            LOGI("hot_image: ★ Assembly-CSharp image = %p（第 %u 项 / 共 %u）", im, i, len);
            return g_hotImage;
        }
    }
    return nullptr;
}

void* hot_image() { return g_hotImage ? g_hotImage : refresh_hot_image(); }

static void* hot_class(const char* name) {
    void* img = hot_image();
    if (!img) return nullptr;
    typedef void* (*Fn_cfn)(void*, const char*, const char*);
    Fn_cfn cfn = (Fn_cfn) p_class_from_name;
    if (!cfn) return nullptr;
    void* k = cfn(img, "", name);
    if (k && p_class_init && !(u8_at((uintptr_t) k + 0x133) & 4)) p_class_init(k);
    return k;
}

void probe_call_first() {
    if (!init()) { LOGE("probe_c: init 失败"); return; }
    ensure_thread();

    typedef void* (*Fn_static0)();
    typedef void* (*Fn_inst0)(void*);
    typedef void* (*Fn_static1)(void*);
    typedef int32_t (*Fn_inst0_i32)(void*);

    std::lock_guard<std::mutex> lk(g_mtx);

    // --- 0) 拿热更 image（沿用 probe_hotclass 的定位方式，这里独立做一遍）
    if (!g_hotImage) {
        void* kAD = find_class_via_images_ns("AppDomain", "System");
        void* mCur = kAD ? find_method_raw(kAD, "get_CurrentDomain", 0) : nullptr;
        void* mAsm = kAD ? find_method_raw(kAD, "GetAssemblies", 0) : nullptr;
        if (!mCur || !mAsm) { LOGE("probe_c: AppDomain 方法未找到"); return; }
        void* dom = ((Fn_static0) ptr_at((uintptr_t) mCur + OFF_MI_POINTER))();
        if (!dom) return;
        void* arr = ((Fn_inst0) ptr_at((uintptr_t) mAsm + OFF_MI_POINTER))(dom);
        if (!arr) return;
        uint32_t len = u32_at((uintptr_t) arr + 0x18);
        for (uint32_t i = 0; i < len && i < 200; ++i) {
            void* asmObj = (void*) ptr_at((uintptr_t) arr + 0x20 + (uintptr_t) i * 8);
            if (!asmObj) continue;
            void* ia = (void*) ptr_at((uintptr_t) asmObj + 0x10);
            void* im = ia ? (void*) ptr_at((uintptr_t) ia + 0) : nullptr;
            const char* nm = im ? (const char*) ptr_at((uintptr_t) im + 0) : nullptr;
            if (is_cstr(nm, 96) && strcmp(nm, "Assembly-CSharp.dll") == 0) { g_hotImage = im; break; }
        }
    }
    if (!g_hotImage) { LOGI("probe_c: 热更 image 还没就绪，下次再试"); return; }

    // --- 1) CGameSceneMgr.Instance（静态属性 get_Instance）
    void* kMgr = hot_class("CGameSceneMgr");
    if (!kMgr) { LOGE("probe_c: CGameSceneMgr 未找到"); return; }
    void* mInst = find_method_raw(kMgr, "get_Instance", 0);
    if (!mInst) { LOGE("probe_c: get_Instance 未找到"); return; }
    uintptr_t mip = (uintptr_t) mInst;
    void* mpInst = ptr_at(mip + OFF_MI_POINTER);
    bool rxInst = mpInst && mapped((uintptr_t) mpInst, 4, 3);
    LOGI("probe_c: get_Instance mi=%p mp=%p in_rxp=%d", mInst, mpInst, rxInst ? 1 : 0);
    if (!rxInst) { LOGE("probe_c: get_Instance 不可裸调，放弃"); return; }

    // ★ 静态方法的裸调 ABI：末尾多一个 const MethodInfo* 槽（IL2CPP 的 /methodinfo 约定）。
    //   16:30 实测：按纯无参裸调 `((void*(*)())mp)()` 会 SIGSEGV 在 libil2cpp+0xb89f40
    //   （pc=0x7289020f40 / lr=0x7289020f28 同一函数内），因为 x0 本该是 MethodInfo* 却成了垃圾。
    //   对照：String.get_Length 是**实例**方法，裸调 `(this)` 成功返回 5 —— 实例方法不带这个槽。
    //   所以：实例 → x0=this；静态 → x0..x(n-1)=实参，x(n)=MethodInfo*（0 参静态即 x0=mi）。
    void* mgr = ((Fn_static1) mpInst)(mInst);
    LOGI("probe_c: CGameSceneMgr.Instance = %p", mgr);
    if (!mgr) { LOGE("probe_c: Instance 为 NULL（场景未进），下次再试"); return; }

    const char* snm = (const char*) ptr_at((uintptr_t) mgr + 0x00);
    LOGI("probe_c: Instance 对象的 klass = %p", (void*) snm);

    // --- 2) GetCurrentChararcterCustomID()（实例 0 参，返回 int）
    void* mCid = find_method_raw(kMgr, "GetCurrentChararcterCustomID", 0);
    if (!mCid) { LOGE("probe_c: GetCurrentChararcterCustomID 未找到"); return; }
    void* mpCid = ptr_at((uintptr_t) mCid + OFF_MI_POINTER);
    LOGI("probe_c: GetCurrentChararcterCustomID mi=%p mp=%p in_rxp=%d",
         mCid, mpCid, (mpCid && mapped((uintptr_t) mpCid, 4, 3)) ? 1 : 0);
    if (!mpCid) return;
    // ★ 16:33 实测：实例方法的末尾槽**是否传 mi 取决于方法体**。
    //   String.get_Length 是 trivial getter（`ldr w0,[x0,#0x10]; ret`），x0=this 就够；
    //   而 CGameSceneMgr.GetCurrentChararcterCustomID 会走 il2cpp_codegen_initialize 之类的
    //   运行时调用，需要一个 MethodInfo*：按 1 参裸调会在 libil2cpp+0xb980e0 因 x8=0 而 SIGSEGV
    //   （lr=libil2cpp+0xb89f50 即该方法体内，pc=+0xb980e0 的被调函数）。
    //   所以统一按 IL2CPP 的 /methodinfo 约定：**末尾永远补一个 MethodInfo* 槽**。
    typedef int32_t (*Fn_inst1_i32)(void*, void*);
    int32_t cid = ((Fn_inst1_i32) mpCid)(mgr, mCid);
    LOGI("probe_c: ★ GetCurrentChararcterCustomID() = %d", cid);
}

// ==================================================================== 功能1：发物品
//
// ABI 定论（16:33/16:36 真机实测）：
//   实例方法：x0=this，x1..xN=实参，**x(N+1)=MethodInfo***
//   静态方法：x0..x(N-1)=实参，**x(N)=MethodInfo***
//   （end 尾补 mi 是安全的上界：AppDomain.get_CurrentDomain 实测只需 x0，多传一个槽无害；
//     而 CGameSceneMgr.GetCurrentChararcterCustomID 不传 mi 会在 libil2cpp+0xb980e0 因 x8=0 崩。
//     结论：一律补 mi。）
//
// 调用链（src_real/Assembly-CSharp/ 核对）：
//   CGameSystem.Instance                      静态属性 get_Instance / 0 参
//   CGameSystem.containerSystem               属性 get_containerSystem / 0 参 → IContainerSystem
//   CContainerSystem.AddMaterialToContainer(int nCustomID, eContainerTypeEnum eType,
//                                           int nMaterialConfigID, int nNum, bool bFireEvent)
//                                             CContainerSystem.cs:144，5 参，void
//   eContainerTypeEnum.Bag = 0
//
// 共享桩风险：日志实测 CContainerSystem.AddMaterialToContainer/5 与 CEquipmentSystem.ReduceAmmo/2
// 共用同一个 methodPointer（0x7284038808）—— 这类共享入口不能按签名裸调。所以本函数先统计
// 「同一个 mp 被多少个方法共用」，>=2 就直接拒绝，避免又一次 SIGSEGV。

static void* hot_class(const char* name);   // fwd（定义在 probe_call_first 上方）

// 统计 klass 内有多少方法的 methodPointer 等于 mp（本类范围，够用）
static int mp_shared_count(void* klass, void* mp) {
    uintptr_t k = (uintptr_t) klass;
    uint16_t n = u16_at(k + OFF_KLASS_MCOUNT);
    void* methods = ptr_at(k + OFF_KLASS_METHODS);
    if (!methods || !n) return -1;
    int hits = 0;
    for (uint16_t i = 0; i < n; ++i) {
        uintptr_t mi = (uintptr_t) ptr_at((uintptr_t) methods + i * 8);
        if (!mi) continue;
        if (ptr_at(mi + OFF_MI_POINTER) == mp) ++hits;
    }
    return hits;
}

// 打印 klass 的全部实例字段（名字 + 偏移），用于认字段
__attribute__((unused)) static void dump_klass_fields(void* klass) {
    uintptr_t k = (uintptr_t) klass;
    uint16_t n = u16_at(k + OFF_KLASS_FCOUNT);
    void* fields = ptr_at(k + OFF_KLASS_FIELDS);
    LOGI("dump_fields: klass=%p count=%u fields=%p", klass, (unsigned) n, fields);
    if (!fields || n > 512) return;
    for (uint16_t i = 0; i < n; ++i) {
        uintptr_t f = (uintptr_t) ptr_at((uintptr_t) fields + (uintptr_t) i * FIELD_STRIDE);
        if (!f) continue;
        const char* nm = (const char*) ptr_at(f + OFF_FIELD_NAME);
        int32_t off = (int32_t) u32_at(f + OFF_FIELD_OFFSET);
        uintptr_t ty = (uintptr_t) ptr_at(f + OFF_FIELD_TYPE);
        uint8_t tc = ty ? u8_at(ty + OFF_TYPE_CODE) : 0;
        LOGI("  field[%u] %-28s off=0x%x type=0x%02x", (unsigned) i,
             is_cstr(nm, 96) ? nm : "(?)", (unsigned) off, (unsigned) tc);
    }
}

// 扫实例字段里所有「看起来像对象指针」的值，打印前若干个
__attribute__((unused)) static bool dump_instance_fields(void* klass, void* obj) {
    uintptr_t k = (uintptr_t) klass;
    uint16_t n = u16_at(k + OFF_KLASS_FCOUNT);
    void* fields = ptr_at(k + OFF_KLASS_FIELDS);
    if (!fields || !n || n > 512 || !obj) return false;
    int printed = 0;
    for (uint16_t i = 0; i < n && printed < 24; ++i) {
        uintptr_t f = (uintptr_t) ptr_at((uintptr_t) fields + (uintptr_t) i * FIELD_STRIDE);
        if (!f) continue;
        const char* nm = (const char*) ptr_at(f + OFF_FIELD_NAME);
        int32_t off = (int32_t) u32_at(f + OFF_FIELD_OFFSET);
        uintptr_t ty = (uintptr_t) ptr_at(f + OFF_FIELD_TYPE);
        uint8_t tc = ty ? u8_at(ty + OFF_TYPE_CODE) : 0;
        if (tc != 0x12 && tc != 0x07) continue;      // 只看引用类型（CLASS / OBJECT）
        void* v1 = (void*) ptr_at((uintptr_t) obj + (uintptr_t) off);
        void* v2 = (void*) ptr_at((uintptr_t) obj + 0x10 + (uintptr_t) off);
        if (!v1 && !v2) continue;
        LOGI("  inst %-28s off=0x%-6x [+off]=%p [+0x10+off]=%p", is_cstr(nm, 96) ? nm : "(?)",
             (unsigned) off, v1, v2);
        ++printed;
    }
    return printed > 0;
}

typedef void* (*Fn_s0)(void*);
typedef void* (*Fn_i0)(void*, void*);
typedef void  (*Fn_i5v)(void*, int32_t, int32_t, int32_t, int32_t, int32_t, void*);

bool feat_add_item(int configId, int num) {
    if (!init()) { LOGE("feat1: init 失败"); return false; }
    ensure_thread();
    std::lock_guard<std::mutex> lk(g_mtx);

    if (!g_hotImage) { LOGE("feat1: 热更 image 未就绪"); return false; }

    // --- 1) CGameSystem.Instance
    void* kGS = hot_class("CGameSystem");
    if (!kGS) { LOGE("feat1: CGameSystem 未找到"); return false; }
    void* mInst = find_method_raw(kGS, "get_Instance", 0);
    if (!mInst) { LOGE("feat1: CGameSystem.get_Instance 未找到"); return false; }
    void* mpInst = ptr_at((uintptr_t) mInst + OFF_MI_POINTER);
    if (!mpInst || !mapped((uintptr_t) mpInst, 4, 3)) { LOGE("feat1: get_Instance 不可裸调"); return false; }
    void* gs = ((Fn_s0) mpInst)(mInst);
    LOGI("feat1: CGameSystem.Instance = %p", gs);
    if (!gs) { LOGE("feat1: CGameSystem.Instance 为 NULL"); return false; }

    // --- 2) ★ 按「对象实际 klass」找方法。CGameSystem.Instance 的声明类型是接口 IGameSystem
    //   （CGameSystem.cs:116 `static IGameSystem Instance`），实际对象可能是子类/实现类，
    //   在 CGameSystem 的 klass 上找 get_containerSystem 会拿到错误的方法表。
    void* kGSactual = (void*) ptr_at((uintptr_t) gs + 0x00);
    if (kGSactual) {
        const char* gsn = (const char*) ptr_at((uintptr_t) kGSactual + OFF_KLASS_NAME);
        LOGI("feat1: Instance 实际 klass=%p name=%s（声明 klass=%p）",
             kGSactual, is_cstr(gsn, 96) ? gsn : "(?)", kGS);
        if (p_class_init && !(u8_at((uintptr_t) kGSactual + 0x133) & 4)) p_class_init(kGSactual);
    }
    void* mCS = kGSactual ? find_method_raw(kGSactual, "get_containerSystem", 0) : nullptr;
    if (!mCS) mCS = find_method_raw(kGS, "get_containerSystem", 0);
    if (!mCS) mCS = find_method_raw(kGS, "get_ContainerSystem", 0);
    if (!mCS) { LOGE("feat1: get_containerSystem 未找到"); return false; }
    void* mpCS = ptr_at((uintptr_t) mCS + OFF_MI_POINTER);
    LOGI("feat1: get_containerSystem mi=%p mp=%p", mCS, mpCS);
    if (!mpCS) return false;
    void* cs = ((Fn_i0) mpCS)(gs, mCS);
    LOGI("feat1: containerSystem(getter) = %p", cs);

    if (!cs) {
        // 不在这里 dump 字段：Il2CppClass 的 fields 是 FieldInfo**（stride 8），本工程头注释
        // 里写的 stride 0x20 是错的，dump 出来全是垃圾（实测 off=0x79725400、"name=(?)"）。
        // 判据只看时序：热更换到位 ≠ CGameSystem.Init 跑过。源码 CGameSystem.cs:256
        // `m_pContainerSystem = new CContainerSystem()` 在 Init 里，角色进场景后才有值。
        // （另外 CContainerSystem 根本没有 get_Instance，那条"退路"是死路。）
        LOGE("feat1: m_pContainerSystem 仍为 null（CGameSystem 尚未 Init），稍后重试");
        return false;
    }

    // --- 3) 拿实际 klass（IContainerSystem 是接口，实例是 CContainerSystem）
    void* kCS = (void*) ptr_at((uintptr_t) cs + 0x00);
    if (kCS) {
        const char* kn = (const char*) ptr_at((uintptr_t) kCS + OFF_KLASS_NAME);
        LOGI("feat1: containerSystem 实际 klass=%p name=%s", kCS, is_cstr(kn, 96) ? kn : "(?)");
    }

    // --- 4) AddMaterialToContainer/5
    void* mAdd = kCS ? find_method_raw(kCS, "AddMaterialToContainer", 5) : nullptr;
    if (!mAdd) {
        // 退回按名字在 CContainerSystem 上再试一次（klass_of 可能拿到接口的 klass）
        void* kC = hot_class("CContainerSystem");
        if (kC) mAdd = find_method_raw(kC, "AddMaterialToContainer", 5);
        if (mAdd) kCS = kC;
    }
    if (!mAdd) { LOGE("feat1: AddMaterialToContainer/5 未找到"); return false; }

    void* mpAdd = ptr_at((uintptr_t) mAdd + OFF_MI_POINTER);
    void* invAdd = ptr_at((uintptr_t) mAdd + OFF_MI_INVOKER);
    int shared = mp_shared_count(kCS, mpAdd);
    unsigned long rvaAdd = mpAdd ? (unsigned long) ((uintptr_t) mpAdd - g_base) : 0;
    LOGI("feat1: AddMaterialToContainer/5 mi=%p mp=%p rva=%#lx invoker=%p shared=%d rx=%d",
         mAdd, mpAdd, rvaAdd, invAdd, shared, is_executable((uintptr_t) mpAdd) ? 1 : 0);
    if (!mpAdd || !invAdd) { LOGE("feat1: mp/invoker 为空，放弃"); return false; }
    // 注：shared>=2 只说明本类里有 5 个方法共用同一个 methodPointer，它不改变调用约定 ——
    //     HybridCLR 的 AOT 化会让同一批「薄封装」落到同一段共享代码上。调用一律走 invoker。

    // --- 5) 调用：AddMaterialToContainer(cid, Bag=0, configId, num, bFireEvent=true)
    void* mgr = nullptr;
    {
        void* kMgr = hot_class("CGameSceneMgr");
        void* mCid = kMgr ? find_method_raw(kMgr, "GetCurrentChararcterCustomID", 0) : nullptr;
        void* mMgr = kMgr ? find_method_raw(kMgr, "get_Instance", 0) : nullptr;
        if (mCid && mMgr) {
            mgr = ((Fn_s0) ptr_at((uintptr_t) mMgr + OFF_MI_POINTER))(mMgr);
            if (mgr) {
                typedef int32_t (*Fn_i1_i32)(void*, void*);
                int32_t cid = ((Fn_i1_i32) ptr_at((uintptr_t) mCid + OFF_MI_POINTER))(mgr, mCid);
                LOGI("feat1: 当前 nCustomID = %d", cid);
                // ★ nCustomID == 0 时绝不能调：CContainerSystem 内部会拿它去 m_dicData 查容器，
                //   0 号是「假容器」，实测会 SIGSEGV SEGV_ACCERR 在 libil2cpp+0xc7c44c。
                //   0 的真正含义是「当前角色实体还没注册进 CGameSceneMgr」（角色未进场景/正在加载）。
                if (cid == 0) { LOGE("feat1: nCustomID=0（角色尚未进场景），本轮跳过"); return false; }

                // ★★ 根因修正（2026-10-29 17:xx）：反汇编 dump 镜像，0xc7c44c 落在
                //   `_Unwind_Backtrace`(0xc7bc78, size 0xd0) 之后、`__register_frame_info_bases`
                //   (0xc7d108) 之前 —— 那是**静态链接 libgcc 的栈展开代码**，不是任何 IL2CPP 方法。
                //   所以「两次不同 ABI 都崩在同一个 RVA」说明方法其实已经进去了，内部抛了托管异常，
                //   而异常沿我们手工构造的 native 栈帧展开时把 unwinder 弄崩（二次崩溃）。参数本身没错。
                //   正确入口是 invoker：invoker(methodPointer, method, obj, params, exc) 把托管异常
                //   写进 exc，不走 native 栈展开。AOT 与 HybridCLR 解释方法都适用。
                // ★★ 走 HybridCLR 的解释执行入口（0xb89ef4），不再碰 invoker 也不再裸调桩：
                //   args[0]=this，随后 5 个实参，每项都是「指向实参值」的指针。
                void* p_self = cs;
                int32_t aCid = cid;         // nCustomID
                int32_t aType = 0;          // eContainerTypeEnum.Bag
                int32_t aCfg = configId;    // nMaterialConfigID
                int32_t aNum = num;         // nNum
                uint8_t aFire = 1;          // bFireEvent = true
                void* iargs[6] = { &p_self, &aCid, &aType, &aCfg, &aNum, &aFire };
                bool exc = false;
                bool okCall = interp_invoke(mAdd, iargs, nullptr, &exc);
                if (exc) {
                    LOGE("feat1: AddMaterialToContainer 抛了托管异常（已捕获，进程安全）");
                    return false;
                }
                if (!okCall) { LOGE("feat1: interp_invoke 返回失败"); return false; }
                LOGI("feat1: ★ AddMaterialToContainer(%d, Bag, %d, %d, true) 返回（shared=%d rva=%#lx）",
                     cid, configId, num, shared, rvaAdd);
                return true;
            }
        }
    }
    LOGE("feat1: 拿不到当前角色，放弃");
    return false;
}

// ==================================================================== MethodInfo 尾部字段诊断
//
// HybridCLR 在标准 MethodInfo 之后追加了自己的字段。反汇编 0xb89ef4 的入口可见
//   `ldr x27,[x21,#0x50]`（x21 = MethodInfo*）—— 有值就直接拿去执行，为空才 bl 0xb980c0 现场生成。
// 因此 `[mi+0x50]` 是否非空，就是「这个方法已经准备好走解释器」的判据。
// 上次用 0xb89ef4 调 get_Instance 卡死，最可能就是它属于 AOT、+0x50 不是 interpData 而是别的字段。
// 这个 probe 把 4 个代表方法的 +0x38..+0x68 全打出来，一次看清。
void probe_mi_layout() {
    if (!init()) return;
    ensure_thread();
    std::lock_guard<std::mutex> lk(g_mtx);
    struct W { const char* cls; const char* m; int pc; };
    const W ws[] = {
        {"CGameSceneMgr", "get_Instance", 0},
        {"CGameSceneMgr", "GetCurrentChararcterCustomID", 0},
        {"CContainerSystem", "AddMaterialToContainer", 5},
        {"CContainerSystem", "IsOwnBlueprint", 2},
        {"CPropertyPart", "GetValue", 3},
        {"CBuffPart", "AddBuff", 2},
    };
    for (const W& w : ws) {
        void* k = hot_class(w.cls);
        void* mi = k ? find_method_raw(k, w.m, w.pc) : nullptr;
        if (!mi) { LOGI("mi_layout: %s.%s/%d NOT FOUND", w.cls, w.m, w.pc); continue; }
        uintptr_t p = (uintptr_t) mi;
        LOGI("mi_layout: %s.%s/%d mi=%p mp=%p",
             w.cls, w.m, w.pc, mi, ptr_at(p));
        LOGI("   +0x08=%p +0x30=%p +0x38=%p +0x40=%p +0x48=%p +0x50=%p +0x58=%p +0x60=%p +0x68=%p",
             ptr_at(p + 0x08), ptr_at(p + 0x30), ptr_at(p + 0x38), ptr_at(p + 0x40),
             ptr_at(p + 0x48), ptr_at(p + 0x50), ptr_at(p + 0x58), ptr_at(p + 0x60),
             ptr_at(p + 0x68));
    }
}

// ==================================================================== 解释执行入口自测
void probe_interp() {
    if (!init()) { LOGE("probe_interp: init 失败"); return; }
    ensure_thread();
    std::lock_guard<std::mutex> lk(g_mtx);

    void* kMgr = hot_class("CGameSceneMgr");
    if (!kMgr) { LOGE("probe_interp: CGameSceneMgr 未找到"); return; }
    void* mInst = find_method_raw(kMgr, "get_Instance", 0);
    void* mCid  = find_method_raw(kMgr, "GetCurrentChararcterCustomID", 0);
    if (!mInst || !mCid) { LOGE("probe_interp: 方法未找到"); return; }
    LOGI("probe_interp: mInst=%p mi=%p  mCid=%p mi=%p",
         mInst, ptr_at((uintptr_t) mInst), mCid, ptr_at((uintptr_t) mCid));

    // ★ 17:24 真机 tombstone 把 0xb89ef4 的真面目钉死了：
    //   入口 `ldr x27,[x21,#0x50]`，x21 是**第 0 个参数**，也就是 MethodInfo*（读它的 method body）。
    //   那次崩溃里 x21 == 1 —— 说明「把 0xb89ef4 当方法体裸调」时实际传进去的 x0 是垃圾。
    //   也就是说：**get_Instance 这类实例/静态访问器的 methodPointer 本身可能就是解释器入口**，
    //   裸调它必然用垃圾 x0 去解引用 → SIGSEGV at 0x51。
    //   所以这里全部改走 interp_invoke（显式给 mi + args 数组），一个裸调都不留。
    uint64_t ret[4] = {0, 0, 0, 0};
    bool exc = false;

    // ① 静态 0 参：get_Instance()
    void* args0[1] = { nullptr };
    if (!interp_invoke(mInst, args0, ret, &exc)) {
        LOGE("probe_interp: get_Instance 调失败 exc=%d", exc ? 1 : 0);
        return;
    }
    void* mgr = (void*) ret[0];
    LOGI("probe_interp: CGameSceneMgr.Instance = %p (via interp, exc=%d)", mgr, exc ? 1 : 0);
    if (!mgr) return;

    // ② 实例 0 参：GetCurrentChararcterCustomID()，args[0] = this
    uint64_t ret2[4] = {0, 0, 0, 0};
    void* self = mgr;
    void* args1[1] = { &self };
    exc = false;
    if (!interp_invoke(mCid, args1, ret2, &exc)) {
        LOGE("probe_interp: GetCurrentChararcterCustomID 调失败 exc=%d", exc ? 1 : 0);
        return;
    }
    LOGI("probe_interp: ★ GetCurrentChararcterCustomID = %d (via interp, exc=%d)",
         (int32_t) ret2[0], exc ? 1 : 0);
}

// ==================================================================== 方法表诊断
// 「同一个 methodPointer 被 5 个方法共用」到底是共享桩、还是方法表偏移读错？判据就在这里：
//   · 22 个方法的 mp 全不同  → 表读对了，那 5 个共用就是真的共享代码（HybridCLR AOT 化的薄封装）；
//   · 大量 mp 相同且 RVA 落在奇怪位置 → OFF_KLASS_METHODS / OFF_KLASS_MCOUNT 读错。
// 同时把 invoker 打出来：invoker 全相同而 mp 不同 ⇒ 这批方法是同一个通用 invoker 驱动的。
void dump_klass_methods(const char* klassName) {
    if (!init() || !klassName) return;
    ensure_thread();
    std::lock_guard<std::mutex> lk(g_mtx);
    void* k = hot_class(klassName);
    if (!k) k = find_class_via_images(klassName);
    if (!k) { LOGE("dump_m: %s NOT FOUND", klassName); return; }
    uintptr_t kk = (uintptr_t) k;
    uint16_t n = u16_at(kk + OFF_KLASS_MCOUNT);
    void* tb = ptr_at(kk + OFF_KLASS_METHODS);
    LOGI("dump_m: %s klass=%p mcount=%u table(@+0x%x)=%p",
         klassName, k, (unsigned) n, (unsigned) OFF_KLASS_METHODS, tb);
    if (!tb || !n || n > 2048) { LOGE("dump_m: 方法表为空/异常"); return; }
    for (uint16_t i = 0; i < n; ++i) {
        uintptr_t mi = (uintptr_t) ptr_at((uintptr_t) tb + (uintptr_t) i * 8);
        if (!mi) { LOGI("  [%u] (null)", (unsigned) i); continue; }
        const char* nm = (const char*) ptr_at(mi + OFF_MI_NAME);
        void* mp  = ptr_at(mi + OFF_MI_POINTER);
        void* inv = ptr_at(mi + OFF_MI_INVOKER);
        unsigned long rva = mp ? (unsigned long) ((uintptr_t) mp - g_base) : 0;
        LOGI("  [%u] %-34s pc=%u mi=%p mp=%p rva=%#lx inv=%p rx=%d",
             (unsigned) i, is_cstr(nm, 96) ? nm : "(?)",
             (unsigned) u8_at(mi + OFF_MI_PCOUNT), (void*) mi, mp, rva, inv,
             is_executable((uintptr_t) mp) ? 1 : 0);
    }
}

// ==================================================================== 热更 DLL 字节替换
//
// 为什么必须走到这一步：三种 native 介入方式全部被真机否掉 ——
//   ① MethodInfo->methodPointer（+0x00/+0x58/+0x60 三处同改）：游戏侧 h 恒 0；
//   ② 统一 invoker（rva 0xb97cf0）：inv 每秒涨 1 万，但名字清一色是 .ctor/Awake/Update/
//      Start/LateUpdate/OnTriggerEnter2D —— 只服务 Unity 生命周期，业务方法一次没有；
//   ③ 按签名桥接桩（rva 0xb50364）：stub 恒 0。
// 剩下的唯一入口就是**热更 DLL 的字节本身**。
static uint8_t* g_patchData = nullptr;      // 最近一次载入的补丁（兼容旧调用）
static size_t   g_patchSize = 0;
static uint8_t* g_patchCipher = nullptr;    // nhtp 密文形态（Assembly.Load 收到的就是它）
static size_t   g_patchCipherSize = 0;
static uint8_t* g_patchPlain = nullptr;     // 明文 PE 形态（InterpreterImage::Load 可能收到解密后的）
static size_t   g_patchPlainSize = 0;

const uint8_t* patch_bytes(size_t* outSize) {
    if (outSize) *outSize = g_patchSize;
    return g_patchData;
}

bool load_patch_dll(const char* path) {
    if (!path) return false;
    FILE* fp = fopen(path, "rb");
    if (!fp) { LOGE("patch: 打不开 %s", path); return false; }
    fseek(fp, 0, SEEK_END);
    long n = ftell(fp);
    fseek(fp, 0, SEEK_SET);
    if (n <= 0 || n > (32L << 20)) { fclose(fp); LOGE("patch: 大小异常 %ld", n); return false; }
    uint8_t* buf = (uint8_t*) malloc((size_t) n);
    if (!buf) { fclose(fp); return false; }
    size_t got = fread(buf, 1, (size_t) n, fp);
    fclose(fp);
    if (got != (size_t) n) { free(buf); return false; }
    // 补丁可以是两种形态：
    //   MZ   —— 明文 PE（仅用于本地比对）
    //   nhtp —— 已用 tools/nhtp_encrypt.py 加密成容器格式，**与游戏 byte[] 内容同构**，
    //           这样才能原位覆盖（游戏收到的就是 nhtp 密文，Assembly.Load 内部自己解密）
    if (!((buf[0] == 'M' && buf[1] == 'Z') || (buf[0] == 'n' && buf[1] == 'h'))) {
        free(buf);
        LOGE("patch: 头既不是 MZ 也不是 nhtp（%02x %02x）", buf[0], buf[1]);
        return false;
    }
    LOGI("patch: 形态=%s", (buf[0] == 'M') ? "明文PE" : "nhtp密文");
    g_patchData = buf;
    g_patchSize = (size_t) n;
    if (buf[0] == 'M') { g_patchPlain = buf; g_patchPlainSize = (size_t) n; }
    else               { g_patchCipher = buf; g_patchCipherSize = (size_t) n; }
    LOGI("patch: 已载入 %s（%zu B，MZ ok）", path, g_patchSize);
    return true;
}

// ------------------------------------------------------------------ HybridCLR 真正入口
//
// ★ 20:21 真机实测：Assembly.Load 的 8 个托管重载全部 DobbyHook 成功（rc=0、计数=8），
//   但游戏加载热更 DLL 时替换体一次都没被调用 —— AOT 侧的托管方法调用压根不经过函数头。
//   改为 hook HybridCLR 的 C++ 入口（.dynsym 里有现成符号）：
//     _ZN9hybridclr8metadata16InterpreterImage4LoadEPKvm
//     hybridclr::metadata::InterpreterImage::Load(const void* data, unsigned long size)  @RVA 0xba1bd4
//   这是纯 native 函数，Dobby 一定命中；而且它在「马上要解析 IL」的那一刻拿到原始字节，
//   比在托管层抢更靠近终点。
static std::atomic<int> g_ilHits{0};
typedef void* (*Fn_interp_load)(void* self, const void* data, size_t size);
static Fn_interp_load g_origInterpLoad = nullptr;

static void* hk_interp_load(void* self, const void* data, size_t size) {
    int hit = g_ilHits.fetch_add(1, std::memory_order_relaxed) + 1;
    const uint8_t* p = (const uint8_t*) data;
    uint32_t h0 = (p && size >= 4) ? (uint32_t) p[0] | ((uint32_t) p[1] << 8) | ((uint32_t) p[2] << 16) | ((uint32_t) p[3] << 24) : 0;
    if (hit <= 12) {
        LOGI("interp_load: #%d self=%p data=%p size=%zu head=%08x", hit, self, data, size, h0);
    }
    if (data && size >= 4) {
        bool isCipher = (p[0] == 'n' && p[1] == 'h' && p[2] == 't' && p[3] == 'p');
        bool isPlain  = (p[0] == 'M' && p[1] == 'Z');
        const uint8_t* src = nullptr;
        size_t srcLen = 0;
        const char* kind = "?";
        if (isCipher && size == g_patchCipherSize) { src = g_patchCipher; srcLen = g_patchCipherSize; kind = "nhtp"; }
        else if (isPlain && size == g_patchPlainSize) { src = g_patchPlain; srcLen = g_patchPlainSize; kind = "MZ"; }
        if (src && srcLen == size) {
            void* dst = (void*) data;
            bool ok = false;
            if (DobbyCodePatch(dst, (uint8_t*) src, (uint32_t) srcLen) == 0) ok = true;
            else ok = false;
            LOGI("interp_load: ★ 覆盖 %s 补丁 %zu B -> %p ok=%d", kind, srcLen, dst, (int) ok);
        } else if (isCipher || isPlain) {
            LOGI("interp_load: 形态=%s 但长度 %zu 与补丁(%zu/%zu) 不匹配，跳过",
                 isCipher ? "nhtp" : "MZ", size, g_patchCipherSize, g_patchPlainSize);
        }
    }
    if (!g_origInterpLoad) { LOGE("interp_load: 没有 orig trampoline"); return nullptr; }
    return g_origInterpLoad(self, data, size);
}

void install_interp_load_hook() {
    if (g_origInterpLoad) return;
    if (!init()) return;
    void* addr = g_rva_interp_image_load ? (void*) (g_base + g_rva_interp_image_load) : nullptr;
    if (!addr || !is_executable((uintptr_t) addr)) {
        LOGE("interp_load: RVA 0x%lx 不可执行", (unsigned long) g_rva_interp_image_load);
        return;
    }
    void* orig = nullptr;
    int rc = DobbyHook(addr, (void*) hk_interp_load, &orig);
    LOGI("interp_load: DobbyHook @%p rc=%d orig=%p", addr, rc, orig);
    if (rc == 0) g_origInterpLoad = (Fn_interp_load) orig;
}

// Assembly* Load(...) —— 全部是静态 AOT 方法。静态方法的 native ABI 是
//     (arg0, arg1, ..., MethodInfo*)        ← MethodInfo 在**最后一个实参之后**那一位
// 所以 2 参重载的 mi 落在 x2，1 参落在 x1。之前把 hook 写成 (a0,a1,a2,mi) 并统一用
// 「第一个 hook 到的重载的 trampoline」转发 —— 那就是 17:53 那次事故的根因：
// 实际调用的是 Load/2，却拿 Load/1 的 trampoline 去执行，参数错位 → 抛异常 →
// 日志停在「开始读取Assembly-CSharp.pdb.bytes」而不见「加载热更程序集成功」。
struct LoadHook { void* mi; void* orig; uint8_t pcount; uint8_t p0code; };
static LoadHook g_loadHooks[16];
static int      g_loadHookN = 0;
static void*    g_load1Mi   = nullptr;
static void*    g_load1Orig = nullptr;
static std::atomic<int> g_loadHits{0};
static std::atomic<int> g_loadPatch{0};

// 在所有截获的 byte[] 里挑出「长度等于补丁且头是 MZ/nhtp」的那个，原位覆盖。
static bool try_patch_array(void* a0, const char* tag) {
    if (!a0) return false;
    uintptr_t arr = (uintptr_t) a0;
    uint32_t len = is_readable(arr + 0x18, 4) ? *(uint32_t*) (arr + 0x18) : 0;
    if (len == 0 && g_patchSize > 0 && is_readable_fresh(arr + 0x18, 4)) {
        len = *(uint32_t*) (arr + 0x18);
    }
    void* vec = (void*) (arr + 0x20);
    if (len >= 50000 && is_readable_fresh((uintptr_t) vec, 4)) {
        static std::atomic<int> s_bigSeen{0};
        int seen = s_bigSeen.fetch_add(1, std::memory_order_relaxed);
        if (seen < 16) {
            LOGE("%s: 大数组 len=%u want=%zu head=%#x", tag, len, g_patchSize, *(uint32_t*) vec);
        }
    }
    if (!g_patchData || len != g_patchSize || !is_readable_fresh((uintptr_t) vec, 4)) return false;
    {
        static std::atomic<int> s_patchOnce{0};
        uint32_t cur = *(uint32_t*) vec;
        if (s_patchOnce.fetch_add(1) < 6)
            LOGE("%s: 命中候选数组 len=%u 当前头=%#x，准备覆盖", tag, len, cur);
    }
    memcpy(vec, g_patchData, g_patchSize);
    g_loadPatch.fetch_add(1, std::memory_order_relaxed);
    LOGE("%s: ★ 已把 %zu B 补丁字节写进 byte[]（进程内生效）", tag, g_patchSize);
    return true;
}

typedef void* (*Fn4)(void*, void*, void*, void*);
static void* g_origLoadFn[8] = {nullptr};
static bool try_patch_array(void* a0, const char* tag);   // fwd
#define ARES_DEF_LOAD_HOOK(N)                                                       static void* hk_load_##N(void* a0, void* a1, void* a2, void* a3) {                   try_patch_array(a0, "load_hook");                                                if (g_origLoadFn[N]) return ((Fn4) g_origLoadFn[N])(a0, a1, a2, a3);             return nullptr;                                                              }
ARES_DEF_LOAD_HOOK(0) ARES_DEF_LOAD_HOOK(1) ARES_DEF_LOAD_HOOK(2) ARES_DEF_LOAD_HOOK(3)
ARES_DEF_LOAD_HOOK(4) ARES_DEF_LOAD_HOOK(5) ARES_DEF_LOAD_HOOK(6) ARES_DEF_LOAD_HOOK(7)
static void* g_loadRepl[8] = {
    (void*) hk_load_0, (void*) hk_load_1, (void*) hk_load_2, (void*) hk_load_3,
    (void*) hk_load_4, (void*) hk_load_5, (void*) hk_load_6, (void*) hk_load_7,
};

static void* hk_asm_load(void* a0, void* a1, void* a2, void* a3) {
    int hit = g_loadHits.fetch_add(1, std::memory_order_relaxed) + 1;
    // mi 的位置随重载变化：1 参在 a1，2 参在 a2，3 参在 a3。三个都试一下找表。
    void* cand[3] = { a1, a2, a3 };
    void* orig = nullptr;
    for (int i = 0; i < 3 && !orig; ++i) {
        for (int j = 0; j < g_loadHookN; ++j)
            if (g_loadHooks[j].mi == cand[i]) { orig = g_loadHooks[j].orig; break; }
    }
    if (hit <= 20) {
        uintptr_t arr = (uintptr_t) a0;
        uint32_t len = (arr && is_readable(arr + 0x18, 4)) ? *(uint32_t*) (arr + 0x18) : 0;
        void* pv = (arr && is_readable(arr + 0x20, 8)) ? *(void**) (arr + 0x20) : nullptr;
        uint32_t hdr = (pv && is_readable((uintptr_t) pv, 4)) ? *(uint32_t*) pv : 0;
        LOGE("load_hook: Assembly.Load #%d a0=%p len=%u hdr=%#x orig=%p a1=%p a2=%p a3=%p n=%d",
             hit, a0, len, hdr, orig, a1, a2, a3, g_loadHookN);
        // 数组对象内部布局诊断：0x10 起每 8 字节一个，看哪个才是指向数据的指针
        for (int off = 0x08; off <= 0x38; off += 8) {
            uintptr_t ad = (uintptr_t) a0 + (uintptr_t) off;
            if (!is_readable(ad, 8)) { LOGE("   +%#x = <unreadable>", off); continue; }
            uint64_t v = *(uint64_t*) ad;
            uint32_t head = 0;
            if (v && is_readable((uintptr_t) v, 4)) head = *(uint32_t*) (uintptr_t) v;
            LOGE("   +%#x = %#llx  (head=%#x)", off, (unsigned long long) v, head);
        }
    }
    try_patch_array(a0, "load_hook");
    typedef void* (*Fn4)(void*, void*, void*, void*);
    if (!orig) { LOGE("load_hook: 找不到对应重载的 trampoline，放弃转发"); return nullptr; }
    return ((Fn4) orig)(a0, a1, a2, a3);
}

// TextAsset.get_bytes 兜底路径：HotFixMgr 里 (… as TextAsset).bytes 就是它。
static void* g_origGetBytes = nullptr;
static std::atomic<int> g_bytesHits{0};

static void* hk_get_bytes(void* self, void* mi) {
    int hit = g_bytesHits.fetch_add(1, std::memory_order_relaxed) + 1;
    typedef void* (*Fn2)(void*, void*);
    void* r = ((Fn2) g_origGetBytes)(self, mi);
    if (hit <= 20) {
        uintptr_t arr = (uintptr_t) r;
        uint32_t len = (arr && is_readable(arr + 0x18, 4)) ? *(uint32_t*) (arr + 0x18) : 0;
        LOGE("get_bytes: #%d self=%p ret=%p len=%u", hit, self, r, len);
    }
    try_patch_array(r, "get_bytes");
    return r;
}

void install_load_hook() {
    if (g_loadHookN > 0) return;
    if (!init()) return;
    std::lock_guard<std::mutex> lk(g_mtx);
    void* kAsm = find_class_via_images_ns("Assembly", "System.Reflection");
    if (!kAsm) { LOGE("load_hook: System.Reflection.Assembly 未找到"); return; }
    uintptr_t kk = (uintptr_t) kAsm;
    if (!ptr_at(kk + OFF_KLASS_METHODS) && p_class_init) {
        p_class_init(kAsm);
    }
    uint16_t n = u16_at(kk + OFF_KLASS_MCOUNT);
    void* tb = ptr_at(kk + OFF_KLASS_METHODS);
    if (!tb) { LOGE("load_hook: Assembly 方法表为空"); return; }
    int hooked = 0;
    for (uint16_t i = 0; i < n; ++i) {
        uintptr_t mi = (uintptr_t) ptr_at((uintptr_t) tb + (uintptr_t) i * 8);
        if (!mi) continue;
        const char* nm = (const char*) ptr_at(mi + OFF_MI_NAME);
        if (!is_cstr(nm, 32) || strcmp(nm, "Load") != 0) continue;
        void* mp = ptr_at(mi + OFF_MI_POINTER);
        if (!mp || !is_executable((uintptr_t) mp)) continue;
        // 每个重载都挂：hook 体用 a0 的长度+PE 头双重判定，非 byte[] 重载不会误伤。
        void* orig = nullptr;
        int idx = g_loadHookN;
        if (idx >= 8) break;
        int rc = DobbyHook(mp, g_loadRepl[idx], &orig);
        __android_log_print(ANDROID_LOG_INFO, "Mod_Menu",
            "load_hook: Load/%u mi=%p mp=%p rva=0x%lx rc=%d orig=%p slot=%d",
            (unsigned) u8_at(mi + OFF_MI_PCOUNT), (void*) mi, mp,
            (unsigned long) ((uintptr_t) mp - g_base), rc, orig, idx);
        if (rc == 0) {
            uint8_t pc = u8_at(mi + OFF_MI_PCOUNT);
            uint8_t p0 = 0;
            void* prms = ptr_at(mi + OFF_MI_PARAMS);
            if (pc >= 1 && prms) {
                uintptr_t ty = (uintptr_t) ptr_at((uintptr_t) prms + OFF_PARAM_TYPE);
                if (ty) p0 = u8_at(ty + OFF_TYPE_CODE);
            }
            g_origLoadFn[idx] = orig;
            g_loadHooks[g_loadHookN].mi = (void*) mi;
            g_loadHooks[g_loadHookN].orig = orig;
            g_loadHooks[g_loadHookN].pcount = pc;
            g_loadHooks[g_loadHookN].p0code = p0;
            if (pc == 1 && p0 == 0x1D /* SZARRAY byte[] */) {
                g_load1Mi = (void*) mi;
                g_load1Orig = orig;
            }
            ++g_loadHookN; ++hooked;
        }
    }
    __android_log_print(ANDROID_LOG_INFO, "Mod_Menu", "load_hook: 装了 %d 个 Load 重载 (类里共有 %u 个方法)",
                        hooked, (unsigned) n);
    // 再挂 TextAsset.get_bytes
    void* kTA = find_class_via_images_ns("TextAsset", "UnityEngine");
    if (kTA) {
        uintptr_t tk = (uintptr_t) kTA;
        if (!ptr_at(tk + OFF_KLASS_METHODS) && p_class_init) {
            p_class_init(kTA);
        }
        uint16_t tn = u16_at(tk + OFF_KLASS_MCOUNT);
        void* ttb = ptr_at(tk + OFF_KLASS_METHODS);
        for (uint16_t i = 0; ttb && i < tn; ++i) {
            uintptr_t mi = (uintptr_t) ptr_at((uintptr_t) ttb + (uintptr_t) i * 8);
            if (!mi) continue;
            const char* nm = (const char*) ptr_at(mi + OFF_MI_NAME);
            if (!is_cstr(nm, 32) || strcmp(nm, "get_bytes") != 0) continue;
            void* mp = ptr_at(mi + OFF_MI_POINTER);
            if (!mp || !is_executable((uintptr_t) mp)) continue;
            void* orig = nullptr;
            int rc = DobbyHook(mp, (void*) hk_get_bytes, &orig);
            LOGI("load_hook: TextAsset.get_bytes mi=%p mp=%p rc=%d", (void*) mi, mp, rc);
            if (rc == 0 && !g_origGetBytes) g_origGetBytes = orig;
        }
    } else {
        LOGE("load_hook: UnityEngine.TextAsset 未找到");
    }
}

int load_hook_count() { return g_loadHookN; }

// ==================================================================== Type.GetType 探测
//
// 目标：绕开「热更类的 Il2CppClass 拿不到」这个死结。
// 链：il2cpp_string_new(0xaa7a98) 造 "CMaterial, Assembly-CSharp"
//     → 裸调 System.Type.GetType(string)（AOT 静态 1 参，x0=string，返回 Il2CppReflectionType*）
//     → Il2CppReflectionType+0x10 = const Il2CppType*（已由 il2cpp_type_get_object 0xaa2320 的
//       `str x8,[x0,#0x10]` 确证）
//     → Il2CppType.data 低 32 位 = klassIndex（TYPE_CLASS 情形）
//     → s_TypeInfoTable[klassIndex]（槽 0x36b8ff0）→ Il2CppClass*
//     → 用 klass+0x10 的名字复核
// 全程只用裸调 + 纯读，不碰 invoker。
void probe_type_gettype() {
    if (!init()) { LOGE("probe_gt: init 失败"); return; }
    ensure_thread();

    typedef void* (*Fn_string_new)(const char*);
    typedef void* (*Fn_static1)(void*);
    Fn_string_new sn = p_string_new;
    if (!sn || !g_rva_typeinfo_table) return;

    void* table = *(void**) (g_base + g_rva_typeinfo_table);
    LOGI("probe_gt: s_TypeInfoTable = %p", table);

    const char* names[] = {
        "CMaterial, Assembly-CSharp",
        "CGameSystem, Assembly-CSharp",
        "CMaterial",
        "UnityEngine.Debug, UnityEngine.CoreModule",
    };

    std::lock_guard<std::mutex> lk(g_mtx);
    void* kType = find_class_via_images_ns("Type", "System");
    if (!kType) { LOGE("probe_gt: System.Type 未找到"); return; }
    {
        uintptr_t kk = (uintptr_t) kType;
        LOGI("probe_gt: Type klass=%p initialized=%d mcount=%u methods=%p",
             kType, (int) ((u8_at(kk + 0x133) >> 2) & 1),
             (unsigned) u16_at(kk + OFF_KLASS_MCOUNT), ptr_at(kk + OFF_KLASS_METHODS));
    }
    void* mGT = find_method_raw(kType, "GetType", 1);
    if (!mGT) { LOGE("probe_gt: Type.GetType/1 未找到"); return; }
    void* mpGT = ptr_at((uintptr_t) mGT + OFF_MI_POINTER);
    LOGI("probe_gt: Type.GetType/1 mi=%p mp=%p", mGT, mpGT);

    for (const char* nm : names) {
        void* str = sn(nm);
        if (!str) { LOGE("probe_gt: string_new(%s) 失败", nm); continue; }
        void* t = ((Fn_static1) mpGT)(str);
        LOGI("probe_gt: GetType(\"%s\") -> %p", nm, t);
        if (!t) continue;

        void* it = (void*) ptr_at((uintptr_t) t + 0x10);        // Il2CppType*
        if (!it) { LOGE("probe_gt:   +0x10 为空"); continue; }
        uint8_t code = u8_at((uintptr_t) it + OFF_TYPE_CODE);   // Il2CppType.type @ +0x0a
        int32_t idx  = (int32_t) u32_at((uintptr_t) it + 0x00); // data（union）低 32 位
        LOGI("probe_gt:   Il2CppType=%p type=0x%02x data32=%d", it, (unsigned) code, idx);

        if (!table) continue;
        void* k = (void*) ptr_at((uintptr_t) table + (uintptr_t) (uint32_t) idx * 8);
        const char* kn = k ? (const char*) ptr_at((uintptr_t) k + OFF_KLASS_NAME) : nullptr;
        LOGI("probe_gt:   s_TypeInfoTable[%d] = %p name=%s", idx, k,
             is_cstr(kn, 96) ? kn : "(?)");
    }
}

// ==================================================================== invoke 可行性验证
//
// invoke 是 7 个功能的唯一通道（热更类不能 hook），但它在 Runtime::Invoke 上会卡死。
// 这里用最小闭环验证「il2cpp_string_new + invoker 直调」这条路是否可行：
//   il2cpp_string_new 桩 0xa6110c → 实体 0xaa7a98（0xaa7aa8 bl strlen → 尾调 String::New）
//   Il2CppString = {klass@0, monitor@8, length@0x10, chars@0x14}
//   String.get_Length() 是 AOT 已编译的 trivial getter，invoker 必定有效
void probe_invoke_test() {
    if (!init()) { LOGE("probe_i: init 失败"); return; }

    // 0) 当前线程是否已被 IL2CPP attach？il2cpp_thread_current 桩 0xa61120 → 0xa93074，
    //    全 4 条：adrp x8,#0x36b9000 ; ldr x8,[x8,#0x998] ; ldr w0,[x8] ; b #0x839390（TLS 取值）。
    typedef void* (*Fn_thread_current)();
    void* cur = p_thread_current ? p_thread_current() : nullptr;
    LOGI("probe_i: il2cpp_thread_current() = %p  (非空=当前线程已 attach)", cur);

    // 1) 造字符串（已实测可用）
    void* s = p_string_new ? p_string_new("hello") : nullptr;
    int len = s ? (int) u32_at((uintptr_t) s + 0x10) : -1;
    LOGI("probe_i: il2cpp_string_new(\"hello\") = %p, length 字段 = %d", s, len);
    if (!s) { LOGE("probe_i: string_new 返回 NULL，放弃"); return; }

    std::lock_guard<std::mutex> lk(g_mtx);
    void* kString = find_class_via_images_ns("String", "System");
    if (!kString) { LOGE("probe_i: System.String 未找到"); return; }
    void* m = find_method_raw(kString, "get_Length", 0);
    if (!m) { LOGE("probe_i: get_Length 未找到"); return; }

    uintptr_t mi = (uintptr_t) m;
    void* mp = ptr_at(mi + OFF_MI_POINTER);
    void* mpi = ptr_at(mi + OFF_MI_INVOKER);
    LOGI("probe_i: get_Length mi=%p mp=%p invoker=%p pcount=%u flags=0x%02x",
         m, mp, mpi,
         (unsigned) u8_at(mi + OFF_MI_PCOUNT), (unsigned) u8_at(mi + 0x44));

    // 1.5) 手动 attach，并在前后各测一次 thread_current —— 这一步直接判定 attach 是否真的生效/是否卡死
    {
        typedef void* (*Fn_tc)();
        Fn_tc tc = (Fn_tc) p_thread_current;
        LOGI("probe_i: [A] before manual attach, current=%p", tc ? tc() : nullptr);
        if (p_thread_attach && g_domain) {
            LOGI("probe_i: [B] calling il2cpp_thread_attach(%p) ...", g_domain);
            p_thread_attach(g_domain);
            LOGI("probe_i: [C] il2cpp_thread_attach returned");
            LOGI("probe_i: [D] after manual attach, current=%p", tc());
        } else {
            LOGE("probe_i: p_thread_attach=%p g_domain=%p，跳过 attach", (void*) p_thread_attach, g_domain);
        }
        t_attached = true;   // 无论上面结果如何，本次探针内不再重复 attach
    }

    // 2) ★ 先做「裸调 methodPointer」：String.get_Length 是 AOT 已编译的 trivial getter，
    //    它的 methodPointer 就是一个普通的 AArch64 函数 `int get_Length(String* this)`。
    //    如果这一条能返回 5，就证明 mp 取对了、AOT 方法可以完全绕开 IL2CPP 运行时直接调。
    //    （invoker 那条路已实测卡死，先把它放后面，别让卡死掩盖了这条信息。）
    {
        typedef int32_t (*Fn_getlen)(void*);
        LOGI("probe_i: [E] 裸调 methodPointer(%p)(%p) ...", (void*) mp, s);
        int32_t v = ((Fn_getlen) mp)(s);
        LOGI("probe_i: [F] 裸调返回 %d  (期望 5)", v);
    }

    // 3) 再试 invoker（这一步预期卡死；放在最后，用它确认「卡死 = invoker 而不是 methodPointer」）
    set_trace_invoke(true);
    set_skip_attach(true);
    Args a; bool exc = false;
    LOGI("probe_i: [G] >>> invoke(get_Length) via invoker enter（预期在这里卡死）");
    void* r = invoke(m, s, a, &exc);
    LOGI("probe_i: [H] <<< invoke exit r=%p exc=%d", r, exc ? 1 : 0);

    set_trace_invoke(false);
}
//
// 静态分析确证（devtmp/il2cpp_string_rva_report2.md）：
//   · .dynsym 没被擦除，DT_SYMTAB=0x5ec05e0 / DT_SYMENT=0x18 / DT_HASH nchain=2398，
//     2215 项 st_value 非零 → 桩↔名可直接对（不必猜顺序）。
//   · MetadataCache 的类表全局槽在 **RVA 0x36b8ff0**：
//       0xa96c7c  ldr x8,[x22,#0xff0]
//       0xa96c80  ldr x0,[x8,w19,sxtw #3]      ; s_TypeInfoTable[index]
//     访问器 0xa96c28 只判 index==-1、**无上界检查**。
//   · Il2CppString = {klass@0, monitor@8, length@0x10, chars@0x14}（不是 0x18/0x20）。
//
// 这条路径完全绕开 class_from_name 的 hash 表，直接按 type index 扫类表 —— 如果 HybridCLR
// 把热更类型也注册进了 s_TypeInfoTable，就能直接命中 CGameSystem 之类。
void probe_typeinfo_table() {
    if (!init()) { LOGE("probe_t: init 失败"); return; }
    uintptr_t slot = g_rva_typeinfo_table ? (g_base + g_rva_typeinfo_table) : 0;
    if (!slot || !mapped(slot, 8, 1)) { LOGE("probe_t: 槽不可读"); return; }
    void* table = *(void**) slot;
    LOGI("probe_t: s_TypeInfoTable @+0x%lx = %p", (unsigned long) g_rva_typeinfo_table, table);
    if (!table) return;

    const char* want[] = {"CGameSystem", "CContainerSystem", "CMaterial", "CEquipmentSystem",
                          "CAntiCheatSystem", "CGameSceneMgr", "CPropertyPart", "CBuffPart"};
    int found = 0, named = 0, slots = 0;
    for (int i = 0; i < 60000; ++i) {
        uintptr_t addr = (uintptr_t) table + (uintptr_t) i * 8;
        if (!mapped(addr, 8, 1)) break;
        void* k = (void*) ptr_at(addr);
        if (!k) continue;
        ++slots;
        const char* nm = (const char*) ptr_at((uintptr_t) k + OFF_KLASS_NAME);
        if (!is_cstr(nm, 96)) continue;
        ++named;
        for (const char* w : want) {
            if (strcmp(nm, w) == 0) {
                void* mi = ptr_at((uintptr_t) k + OFF_KLASS_METHODS);
                LOGI("probe_t: ★ FOUND %s idx=%d klass=%p methodCount=%u methods=%p", nm, i, k,
                     (unsigned) u16_at((uintptr_t) k + OFF_KLASS_MCOUNT), mi);
                ++found;
                break;
            }
        }
    }
    LOGI("probe_t: 扫描结束 slots=%d named=%d 命中=%d", slots, named, found);
}
//
// 思路：热更类拿不到 Il2CppClass，但**运行时的托管对象**拿得到 —— System.AppDomain 在
// mscorlib（AOT，class_from_name 可达），它的 GetAssemblies() 返回的 Assembly[] 里一定
// 包含通过 Assembly.Load 进来的热更程序集（Assembly-CSharp）。整条链路不需要构造字符串，
// 因此不依赖 il2cpp_string_new。
//
// Il2CppArray 布局（本项目 so 的常量 getter 反汇编确证）：
//   il2cpp_object_header_size = 0x10        (0xa60f98 → orr w0,wzr,#0x10)
//   array_object_header_size  = 0x20        (0xa60fa0 → #0x20)
//   offset_of_array_length    = 0x18        (0xa60fa8 → #0x18)
//   offset_of_array_bounds    = 0x10        (0xa60fb0 → #0x10)
// ⇒ obj@0 / bounds@0x10 / max_length@0x18 / vector@0x20
//
// Il2CppString：obj 头 0x10 字节，+0x10 = int32 length，+0x14 起 UTF-16 字符。
//
// 不调用 Class::Init 的裸查找。实测教训：在本模块的 hack_thread（非 Unity 主线程）上对
// 尚未初始化的 AOT 类调 Class::Init，会让整条线程卡死（15:55 与 15:56 两次，无崩溃、无后续日志）。
// AppDomain / Assembly / Type 这些 mscorlib 核心类游戏早已用过，方法表本来就是满的，
// 直接读即可；读不到就打诊断，绝不强行 Init。
static void* find_method_raw(void* klass, const char* name, int pc_want) {
    if (!klass || !name) return nullptr;
    uintptr_t k = (uintptr_t) klass;
    uint16_t n = u16_at(k + OFF_KLASS_MCOUNT);
    void* methods = ptr_at(k + OFF_KLASS_METHODS);
    if (!methods || !n) return nullptr;
    for (uint16_t i = 0; i < n; ++i) {
        uintptr_t mi = (uintptr_t) ptr_at((uintptr_t) methods + i * 8);
        if (!mi) continue;
        const char* nm = (const char*) ptr_at(mi + OFF_MI_NAME);
        if (!is_cstr(nm, 96) || strcmp(nm, name) != 0) continue;
        if (pc_want >= 0 && (int) u8_at(mi + OFF_MI_PCOUNT) != pc_want) continue;
        return (void*) mi;
    }
    return nullptr;
}

void probe_reflection() {
    if (!init()) { LOGE("probe_r: init 失败"); return; }
    std::lock_guard<std::mutex> lk(g_mtx);

    void* kAppDomain = find_class_via_images_ns("AppDomain", "System");
    if (!kAppDomain) { LOGE("probe_r: System.AppDomain 未找到"); return; }
    {
        uintptr_t kk = (uintptr_t) kAppDomain;
        LOGI("probe_r: AppDomain klass=%p initialized=%d methodCount=%u methods=%p",
             kAppDomain, (int) ((u8_at(kk + 0x133) >> 2) & 1),
             (unsigned) u16_at(kk + OFF_KLASS_MCOUNT), ptr_at(kk + OFF_KLASS_METHODS));
    }

    // 绝不在本线程对未初始化的 AOT 类调 Class::Init（会把 hack_thread 卡死）。
    void* mCur = find_method_raw(kAppDomain, "get_CurrentDomain", 0);
    if (!mCur) { LOGE("probe_r: get_CurrentDomain 未找到（方法表为空 = 该类尚未初始化）"); return; }
    bool exc = false;
    Args a0;
    void* dom = invoke(mCur, nullptr, a0, &exc);
    LOGI("probe_r: AppDomain=%p exc=%d", dom, exc ? 1 : 0);
    if (!dom) return;

    void* mAsm = find_method_raw(kAppDomain, "GetAssemblies", 0);
    if (!mAsm) { LOGE("probe_r: GetAssemblies 未找到"); return; }
    Args a1;
    void* arr = invoke(mAsm, dom, a1, &exc);
    LOGI("probe_r: Assembly[]=%p exc=%d", arr, exc ? 1 : 0);
    if (!arr) return;

    uint32_t len = u32_at((uintptr_t) arr + 0x18);
    LOGI("probe_r: GetAssemblies 长度=%u（domain 数组是 66，多出来的就是热更程序集）", len);
    if (len > 512) { LOGE("probe_r: 长度异常 %u，放弃", len); return; }
    for (uint32_t i = 0; i < len && i < 96; ++i) {
        void* asmObj = (void*) ptr_at((uintptr_t) arr + 0x20 + (uintptr_t) i * 8);
        if (!asmObj) continue;
        // Il2CppReflectionAssembly 的 +0x10 推测为 Il2CppAssembly*（与 ReflectionType/Method 同构），
        // 也可能是别的；两个候选都打出来，靠 Il2CppAssembly+0x00=Il2CppImage*、+0x00=name 交叉验证。
        void* cand1 = (void*) ptr_at((uintptr_t) asmObj + 0x10);
        void* cand2 = (void*) ptr_at((uintptr_t) asmObj + 0x08);
        const char* n1 = nullptr; const char* n2 = nullptr;
        if (cand1) { void* img = (void*) ptr_at((uintptr_t) cand1 + 0); if (img) n1 = (const char*) ptr_at((uintptr_t) img + 0); }
        if (cand2) { void* img = (void*) ptr_at((uintptr_t) cand2 + 0); if (img) n2 = (const char*) ptr_at((uintptr_t) img + 0); }
        LOGI("probe_r: [%u] obj=%p +0x10=%p name1=%s | +0x08=%p name2=%s", i, asmObj,
             cand1, is_cstr(n1, 96) ? n1 : "(?)", cand2, is_cstr(n2, 96) ? n2 : "(?)");
    }
}

// 探测 AOT 侧的 Assembly.Load —— 热更 DLL 的唯一入口，也是「换字节」的落点。
// 只在 mscorlib 里找（它在 66 个 assembly 里，class_from_name 可达），不碰热更类。
void probe_load_hook() {
    if (!init()) { LOGE("probe: init 失败"); return; }
    std::lock_guard<std::mutex> lk(g_mtx);
    // 先用几个「必然存在」的类验证 class_from_name 这条链本身是否可用：
    //   Object/String 在 mscorlib(asm[0])，Debug 在 UnityEngine.CoreModule(asm[7])，
    //   HotFixMgr 在 AotDll(asm[44])。任何一个命中就说明 RVA/偏移是对的。
    const char* probe_names[] = {"Object", "String", "Debug", "HotFixMgr", "Assembly",
                                 "CGameSystem", "CContainerSystem", "CMaterial",
                                 "CEquipmentSystem", "CAntiCheatSystem", "CGameSceneMgr"};
    for (const char* pn : probe_names) {
        void* k = find_class_via_images(pn);
        if (k) {
            const char* ns = (const char*) ptr_at((uintptr_t) k + OFF_KLASS_NS);
            LOGI("probe: %s -> klass=%p ns=%s", pn, k, is_cstr(ns, 96) ? ns : "(null)");
        } else {
            LOGI("probe: %s -> NOT FOUND", pn);
        }
    }
    void* kAsm = find_class_via_images("Assembly");
    if (!kAsm) { LOGE("probe: System.Reflection.Assembly 没找到"); return; }
    log_klass_info(kAsm);
    if (p_class_init && !(u8_at((uintptr_t) kAsm + 0x133) & 4)) p_class_init(kAsm);   // 只在未初始化时 Init
    uintptr_t k = (uintptr_t) kAsm;
    uint16_t n = u16_at(k + OFF_KLASS_MCOUNT);
    void* methods = ptr_at(k + OFF_KLASS_METHODS);
    LOGI("probe: Assembly methods=%p count=%u", methods, (unsigned) n);
    int hit = 0;
    for (uint16_t i = 0; i < n; ++i) {
        uintptr_t mi = (uintptr_t) ptr_at((uintptr_t) methods + i * 8);
        if (!mi) continue;
        const char* nm = (const char*) ptr_at(mi + OFF_MI_NAME);
        if (!is_cstr(nm, 96)) continue;
        if (strcmp(nm, "Load") != 0) continue;
        ++hit;
        uint8_t   pc  = u8_at(mi + OFF_MI_PCOUNT);
        uintptr_t mp  = (uintptr_t) ptr_at(mi + OFF_MI_POINTER);
        uintptr_t inv = (uintptr_t) ptr_at(mi + OFF_MI_INVOKER);
        bool rx = mp && mapped(mp, 4, 3);
        LOGI("probe: Assembly.Load/%u mi=%p mp=%p rx=%d invoker=%p", (unsigned) pc,
             (void*) mi, (void*) mp, rx ? 1 : 0, (void*) inv);
    }
    LOGI("probe: Assembly.Load 重载数=%d", hit);

    // ---- 第二段：HotFixMgr（AotDll，AOT 可 hook）全方法表 ——
    // 它是热更加载的总入口（HotFixMgr.StartLoad → Assembly.Load(Assembly-CSharp.dll.bytes)），
    // 只要能拿到可执行的 methodPointer，就可以在这儿一次性把整条热更加载链接管。
    void* kHF = find_class_via_images("HotFixMgr");
    if (!kHF) { LOGE("probe: HotFixMgr 没找到"); return; }
    log_klass_info(kHF);
    if (p_class_init && !(u8_at((uintptr_t) kHF + 0x133) & 4)) p_class_init(kHF);
    uintptr_t kh = (uintptr_t) kHF;
    uint16_t hn = u16_at(kh + OFF_KLASS_MCOUNT);
    void* hms = ptr_at(kh + OFF_KLASS_METHODS);
    LOGI("probe: HotFixMgr methods=%p count=%u", hms, (unsigned) hn);
    for (uint16_t i = 0; i < hn; ++i) {
        uintptr_t mi = (uintptr_t) ptr_at((uintptr_t) hms + i * 8);
        if (!mi) continue;
        const char* nm = (const char*) ptr_at(mi + OFF_MI_NAME);
        if (!is_cstr(nm, 96)) continue;
        uint8_t   pc = u8_at(mi + OFF_MI_PCOUNT);
        uintptr_t mp = (uintptr_t) ptr_at(mi + OFF_MI_POINTER);
        uintptr_t inv = (uintptr_t) ptr_at(mi + OFF_MI_INVOKER);
        bool rx = mp && mapped(mp, 4, 3);
        LOGI("probe: HotFixMgr.%s/%u mi=%p mp=%p rx=%d invoker=%p", nm, (unsigned) pc,
             (void*) mi, (void*) mp, rx ? 1 : 0, (void*) inv);
    }
}

void* find_class_by_name_ns(const char* namespaze, const char* name) {    if (!name) return nullptr;
    void* k = find_class_by_name(name);
    if (!k || !namespaze) return k;
    const char* ns = (const char*) ptr_at((uintptr_t) k + OFF_KLASS_NS);
    if (is_cstr(ns, 128) && strcmp(ns, namespaze) == 0) return k;
    std::lock_guard<std::mutex> lk(g_mtx);
    std::string full = std::string(namespaze) + "." + name;
    auto it = g_by_full.find(full);
    return it == g_by_full.end() ? nullptr : it->second;
}

void* klass_of(void* obj) {
    if (!obj) return nullptr;
    return ptr_at((uintptr_t) obj);
}

// ==================================================================== 方法 / 字段
void* find_method(void* klass, const char* name, int param_count) {
    if (!klass || !name) return nullptr;
    // 方法表由 Class::Init 填充：0xa8e500(class_get_methods) 在 iter==NULL 时先 bl 0xa8e574(InitClass)，
    // 直接读 +0x98 前必须自己补这一步。★ 但只对「尚未初始化」的类补：
    //   已初始化的类（如 mscorlib 的 System.AppDomain）再调一次 Class::Init 会重入
    //   mscorlib 的静态构造链，实测直接让 hack_thread 卡死（15:55:23 之后整条线程无输出、无崩溃）。
    //   判据：klass+0x133 bit2 = initialized（与 il2cpp_runtime_class_init 实体 0xa94b68 首两条
    //   `ldrb w8,[x0,#0x133]; tbz w8,#2` 完全一致）。
    uintptr_t k = (uintptr_t) klass;
    if (p_class_init && !(u8_at(k + 0x133) & 4)) p_class_init(klass);
    uint16_t n = u16_at(k + OFF_KLASS_MCOUNT);
    void* methods = ptr_at(k + OFF_KLASS_METHODS);
    if (!methods || !n) return nullptr;
    for (uint16_t i = 0; i < n; ++i) {
        uintptr_t mi = (uintptr_t) ptr_at((uintptr_t) methods + i * 8);
        if (!mi) continue;
        const char* nm = (const char*) ptr_at(mi + OFF_MI_NAME);
        if (!is_cstr(nm, 128)) continue;
        if (strcmp(nm, name) != 0) continue;
        if (param_count >= 0 && (int) u8_at(mi + OFF_MI_PCOUNT) != param_count) continue;
        return (void*) mi;
    }
    return nullptr;
}

// 沿 parent 链找（方法可能声明在基类上，例如 CEntityPartBase）
void* find_method_in_hierarchy(void* klass, const char* name, int param_count) {
    void* k = klass;
    for (int depth = 0; k && depth < 32; ++depth) {
        if (void* m = find_method(k, name, param_count)) return m;
        k = ptr_at((uintptr_t) k + OFF_KLASS_PARENT);
    }
    return nullptr;
}

// klass+0x132 bit1 = IL2CPP_CLASS_IS_VALUETYPE（0xa60d8 跳板与 value_box 0xa8c32c 同偏移）
static bool is_value_type(void* k) {
    return k && (u16_at((uintptr_t) k + OFF_KLASS_FLAGS) & 2) != 0;
}

// 打印 klass 概要：名称 / 命名空间 / 父类 / instance_size / 是否值类型
static void log_klass_info(void* k) {
    if (!k) return;
    uintptr_t a = (uintptr_t) k;
    const char* ns = (const char*) ptr_at(a + OFF_KLASS_NS);
    const char* nm = (const char*) ptr_at(a + OFF_KLASS_NAME);
    void* parent = ptr_at(a + OFF_KLASS_PARENT);
    const char* pnm = parent ? (const char*) ptr_at((uintptr_t) parent + OFF_KLASS_NAME) : nullptr;
    bool has_ns = is_cstr(ns, 96) && ns[0] != '\0';
    LOGI("klass=%p %s%s%s parent=%s instance_size=%d valuetype=%d",
         k,
         has_ns ? ns : "", has_ns ? "." : "", kstr(nm),
         is_cstr(pnm, 96) ? pnm : "-",
         (int) u32_at(a + OFF_KLASS_IFACE_SIZE),
         is_value_type(k) ? 1 : 0);
}


void* find_method_typed(void* klass, const char* name, const uint8_t* type_codes, int n) {
    if (!klass || !name || !type_codes || n <= 0) return nullptr;
    uintptr_t k = (uintptr_t) klass;
    uint16_t cnt = u16_at(k + OFF_KLASS_MCOUNT);
    void* methods = ptr_at(k + OFF_KLASS_METHODS);
    if (!methods || !cnt) return nullptr;
    for (uint16_t i = 0; i < cnt; ++i) {
        uintptr_t mi = (uintptr_t) ptr_at((uintptr_t) methods + i * 8);
        if (!mi) continue;
        const char* nm = (const char*) ptr_at(mi + OFF_MI_NAME);
        if (!is_cstr(nm, 128) || strcmp(nm, name) != 0) continue;
        if ((int) u8_at(mi + OFF_MI_PCOUNT) != n) continue;
        void* params = ptr_at(mi + OFF_MI_PARAMS);      // ParameterInfo*
        if (!params) continue;
        bool ok = true;
        for (int j = 0; j < n && ok; ++j) {
            uintptr_t ty = (uintptr_t) ptr_at((uintptr_t) params + (size_t) j * PARAM_STRIDE + OFF_PARAM_TYPE);
            if (!ty) { ok = false; break; }
            uint8_t code = u8_at(ty + OFF_TYPE_CODE);
            if (code != type_codes[j]) ok = false;
        }
        if (ok) return (void*) mi;
    }
    return nullptr;
}

void* find_field(void* klass, const char* name, bool* is_static, int32_t* offset) {
    if (!klass || !name) return nullptr;
    uintptr_t k = (uintptr_t) klass;
    uint16_t n = u16_at(k + OFF_KLASS_FCOUNT);
    void* fields = ptr_at(k + OFF_KLASS_FIELDS);
    if (!fields || !n) return nullptr;
    for (uint16_t i = 0; i < n; ++i) {
        uintptr_t fi = (uintptr_t) ptr_at((uintptr_t) fields + i * FIELD_STRIDE);
        if (!fi) continue;
        const char* nm = (const char*) ptr_at(fi + OFF_FIELD_NAME);
        if (!is_cstr(nm, 128) || strcmp(nm, name) != 0) continue;
        uintptr_t ty = (uintptr_t) ptr_at(fi + OFF_FIELD_TYPE);   // Il2CppType*
        bool st = ty && (u32_at(ty + OFF_TYPE_ATTRS) & 0x10) != 0; // FieldAttributes.Static
        if (is_static) *is_static = st;
        if (offset) *offset = (int32_t) u32_at(fi + OFF_FIELD_OFFSET);
        return (void*) fi;
    }
    return nullptr;
}

int32_t instance_field_offset(void* klass, const char* name) {
    bool st = false;
    int32_t off = -1;
    if (!find_field(klass, name, &st, &off)) return -1;
    return st ? -1 : off;
}

void* read_instance_object(void* obj, int32_t field_offset) {
    if (!obj || field_offset < 0) return nullptr;
    uintptr_t addr = (uintptr_t) obj + OBJ_HEADER + (uintptr_t) field_offset;
    if (!mapped(addr, 8, 1)) return nullptr;
    return ptr_at(addr);
}

void* find_static_field(void* klass, const char* name) {
    if (!klass || !name) return nullptr;
    bool st = false;
    int32_t off = 0;
    if (!find_field(klass, name, &st, &off)) return nullptr;
    if (!st) { LOGW("field %s 不是静态字段", name); return nullptr; }
    if (p_class_init && !(u8_at((uintptr_t) klass + 0x133) & 4)) p_class_init(klass);  // 静态构造：static_fields 由 Class::Init 分配
    void* sf = ptr_at((uintptr_t) klass + OFF_KLASS_STATIC);
    if (!sf) { LOGW("klass %s static_fields 仍为 NULL", kstr((const char*) ptr_at((uintptr_t) klass + OFF_KLASS_NAME))); return nullptr; }
    uintptr_t addr = (uintptr_t) sf + (uintptr_t) off;
    if (!mapped(addr, 8, 1)) return nullptr;
    return ptr_at(addr);
}

// ==================================================================== 菜单 → IL 参数槽
//
// 背景（PROGRESS 6.50.2）：菜单曾把参数写到 /data/data/<pkg>/files/Lv，IL 侧用
// `File.ReadAllText` + `Int32.Parse` 读，结果抛
//   MethodAccessException: IEqualityComparer<Int32>.GetHashCode on Dictionary<Int32,Int32>
// 根因是 Int32.Parse 的单参重载内部走 CultureInfo / globalization，那条 AOT 泛型路径在解释
// 器里不可访问。⇒ 结论：**任何托管 BCL 调用都不可靠**。
//
// 改成「读静态 int 字段」：IL 侧只有 ldsfld / stsfld 两条原语，不产生 MemberRef、
// 不碰 BCL、不碰泛型、不碰文件 IO；native 侧只做一次 4 字节内存写。
//
// 槽位选择依据（Cecil 全表统计，见 _ilplan/scan_static_int2.py）：
//   Assembly-CSharp 里共 104 个 `static System.Int32`，其中 81 个是 C# `const`
//   （literal=True，**没有存储槽**，ldsfld 读的是常量内联，不可用）。
//   剩下的非 literal 字段里，唯一一组属于「只在编辑器侧使用、运行时从不执行」的工具类：
//     Com.LuisPedroFonseca.ProCamera2D.EditorPrefsX.endianDiff1  (FieldDef 0x040043B5)
//     Com.LuisPedroFonseca.ProCamera2D.EditorPrefsX.endianDiff2  (FieldDef 0x040043B6)
//   写它们不会影响任何游戏行为（该类的 Initialize/ConvertTo4Bytes 只在编辑器存档时调用）。
static const char* SLOT_NS     = "Com.LuisPedroFonseca.ProCamera2D";
static const char* SLOT_CLASS  = "EditorPrefsX";
static const char* SLOT_CFG    = "endianDiff1";
static const char* SLOT_NUM    = "endianDiff2";

// 带命名空间、只查热更 image 的类定位（find_class_by_name 只试空命名空间，
// 对 Namespace.Class 形式的类永远 NOT FOUND）。
void* find_hot_class_ns(const char* ns, const char* name) {
    if (!name) return nullptr;
    if (!init()) return nullptr;
    if (!g_hotImage) { set_error("热更 image 未就绪，无法定位 %s", name); return nullptr; }
    void* k = p_class_from_name ? p_class_from_name(g_hotImage, ns ? ns : "", name) : nullptr;
    if (!k) { set_error("热更类不存在: %s.%s", ns ? ns : "", name); return nullptr; }
    const char* kn = (const char*) ptr_at((uintptr_t) k + OFF_KLASS_NAME);
    if (!is_cstr(kn, 128) || strcmp(kn, name) != 0) { set_error("类名复核失败: %s", name); return nullptr; }
    // static_fields 由 Class::Init 分配；未 init 时是 NULL，写不进去
    if (p_class_init && !(u8_at((uintptr_t) k + 0x133) & 4)) p_class_init(k);
    return k;
}

// 静态字段的**地址**（不是值）。addr = klass->static_fields + FieldInfo->offset
static uintptr_t static_field_addr(void* klass, const char* field, int32_t* out_off) {
    if (!klass || !field) return 0;
    bool st = false;
    int32_t off = 0;
    if (!find_field(klass, field, &st, &off)) { set_error("字段 %s 不存在", field); return 0; }
    if (!st) { set_error("字段 %s 不是 static", field); return 0; }
    void* sf = ptr_at((uintptr_t) klass + OFF_KLASS_STATIC);
    if (!sf) { set_error("klass static_fields 为 NULL（%s 未 init）", field); return 0; }
    uintptr_t addr = untag((uintptr_t) sf) + (uintptr_t) off;
    if (!mapped(addr, 4, 1)) { set_error("字段地址不可访问 %s @%p", field, (void*) addr); return 0; }
    if (out_off) *out_off = off;
    return addr;
}

bool read_static_i32(void* klass, const char* field, int32_t* out) {
    uintptr_t a = static_field_addr(klass, field, nullptr);
    if (!a) return false;
    if (out) *out = (int32_t) u32_at(a);
    return true;
}

bool write_static_i32(void* klass, const char* field, int32_t v, int32_t* prev) {
    uintptr_t a = static_field_addr(klass, field, nullptr);
    if (!a) return false;
    if (prev) *prev = (int32_t) u32_at(a);
    *(volatile int32_t*) a = v;
    __sync_synchronize();
    return true;
}

// 菜单 → IL：写入一次物品请求。
//
// ★ 通道演化（三次真机实测）：
//   ① 文件 + File.ReadAllText + Int32.Parse → MethodAccessException（CultureInfo 的 AOT 泛型不可访问）
//   ② 两个热更静态 int 字段（EditorPrefsX.endianDiff1/2）→ 20:16 报「endianDiff2 不存在」，
//      20:18 报「endianDiff1 不存在」—— HybridCLR 给这个纯静态工具类建的字段表不稳定，
//      同一个字段两次自检结果不同，不能作为通道。
//   ③ **本版：UnityEngine.PlayerPrefs**。它是 AOT icall（CoreModule），内部走 Unity 自己的
//      native map，既没有托管泛型、也不碰 CultureInfo、也不依赖热更类的字段表。
//      IL 侧只有 `call PlayerPrefs.GetInt(string,int)`（MemberRef 0x0A000100）与
//      `SetInt`（0x0A000101），两条都是 AOT→热更的桥接调用，HybridCLR 原生支持。
//      native 侧用裸调写（静态 AOT 方法：x0=Il2CppString*, x1=int, x2=MethodInfo*）。
static const char* PP_KEY = "Lv";
static const int   PP_SPLIT = 100000;

// 裸调 UnityEngine.PlayerPrefs.SetInt(string,int)
static bool playerprefs_set_int(int32_t v) {
    if (!init()) { set_error("init 未就绪"); return false; }
    void* k = find_class_via_images_ns("PlayerPrefs", "UnityEngine");
    if (!k) { set_error("UnityEngine.PlayerPrefs 未找到"); return false; }
    // ★ AOT 类的 klass->methods 要到 Class::Init 才链接（find_class_by_name 里有这一步、
    //   find_class_via_images_ns 没有）。20:21 实测 "PlayerPrefs.SetInt/2 未找到" 就是这个原因。
    if (p_class_init && !(u8_at((uintptr_t) k + 0x133) & 4)) p_class_init(k);
    void* mi = find_method_raw(k, "SetInt", 2);
    if (!mi) { set_error("PlayerPrefs.SetInt/2 未找到"); return false; }
    void* mp = ptr_at((uintptr_t) mi + OFF_MI_POINTER);
    if (!mp || !is_executable((uintptr_t) mp)) { set_error("SetInt methodPointer 不可执行"); return false; }
    Fn_string_new sn = p_string_new;
    if (!sn) { set_error("string_new 未解析"); return false; }
    void* str = sn(PP_KEY);
    if (!str) { set_error("string_new 失败"); return false; }
    typedef void (*Fn_set_int_2)(void*, int32_t, void*);
    ((Fn_set_int_2) mp)(str, v, mi);
    return true;
}

// 裸调 UnityEngine.PlayerPrefs.GetInt(string,int)
static bool playerprefs_get_int(int32_t* out) {
    if (!init()) return false;
    void* k = find_class_via_images_ns("PlayerPrefs", "UnityEngine");
    if (!k) return false;
    if (p_class_init && !(u8_at((uintptr_t) k + 0x133) & 4)) p_class_init(k);
    void* mi = find_method_raw(k, "GetInt", 2);
    if (!mi) return false;
    void* mp = ptr_at((uintptr_t) mi + OFF_MI_POINTER);
    if (!mp || !is_executable((uintptr_t) mp)) return false;
    Fn_string_new sn = p_string_new;
    if (!sn) return false;
    void* str = sn(PP_KEY);
    if (!str) return false;
    typedef int32_t (*Fn_get_int_2)(void*, int32_t, void*);
    int32_t r = ((Fn_get_int_2) mp)(str, 0, mi);
    if (out) *out = r;
    return true;
}

bool set_item_slot(int configId, int num) {
    if (configId <= 0 || num <= 0) return false;
    int32_t v = (int32_t) ((int64_t) configId * PP_SPLIT + (int64_t) num);
    int32_t prev = -1;
    bool hadPrev = playerprefs_get_int(&prev);
    if (!playerprefs_set_int(v)) {
        LOGE("slot: PlayerPrefs.SetInt 失败: %s", last_error());
        return false;
    }
    int32_t back = 0;
    bool okRead = playerprefs_get_int(&back);
    LOGI("slot: PlayerPrefs '%s' %d -> %d (= %d * %d + %d) 读回 %d ok=%d",
         PP_KEY, hadPrev ? prev : -999, v, configId, PP_SPLIT, num, back, (int) okRead);
    return okRead && back == v;
}

bool get_item_slot(int* configId, int* num) {
    int32_t v = 0;
    if (!playerprefs_get_int(&v)) return false;
    if (configId) *configId = v / PP_SPLIT;
    if (num)      *num      = v % PP_SPLIT;
    return true;
}

// 通道自检：写 → 读回 → 清零
bool selftest_item_slot() {
    int32_t prev = 0;
    bool had = playerprefs_get_int(&prev);
    if (!playerprefs_set_int(4321)) {
        LOGE("slot selftest: SetInt 失败: %s", last_error());
        return false;
    }
    int32_t back = 0;
    bool okRead = playerprefs_get_int(&back);
    playerprefs_set_int(0);
    LOGI("slot selftest: PlayerPrefs '%s' 原值=%d 写 4321 读回 %d ok=%d",
         PP_KEY, had ? prev : -999, back, (int) okRead);
    return okRead && back == 4321;
}

// ==================================================================== 调用
static void log_exception(void* ex) {
    if (!ex) return;
    char buf[1024];
    if (p_format_exc) { p_format_exc(ex, buf, (int) sizeof(buf)); LOGI("exc: %s", buf); }
    if (p_format_trace) { p_format_trace(ex, buf, (int) sizeof(buf)); LOGI("trace: %s", buf); }
}

void* invoke(void* method, void* obj, Args& a, bool* exc) {
    if (exc) *exc = false;
    if (!init() || !method) return nullptr;
    if (g_traceInvoke) LOGI("invoke: #1 enter method=%p obj=%p nargs=%d", method, obj, a.n);

    // 嫌疑一：主线程被 Unity 自己 attach 过，我们再 attach 会重入运行时锁。
    //   il2cpp_thread_attach 桩 0xa61124 → 0xaa4bc0；TLS 命中本该原样返回，
    //   但 t_attached 是 thread_local，主线程这里是 false → 每次都会真调一次。
    if (g_skipAttach) {
        if (g_traceInvoke) LOGI("invoke: #2b g_skipAttach=1，跳过 thread_attach");
    } else {
        if (g_traceInvoke) LOGI("invoke: #2 before ensure_thread (t_attached=%d)", (int) t_attached);
        ensure_thread();
        if (g_traceInvoke) LOGI("invoke: #3 after ensure_thread");
    }

    uintptr_t mi = (uintptr_t) method;
    uint8_t want = u8_at(mi + OFF_MI_PCOUNT);
    if ((int) want != a.n)
        LOGW("invoke: 形参 %u 个，实参 %d 个（%s）", (unsigned) want, a.n,
             kstr((const char*) ptr_at(mi + OFF_MI_NAME)));

    void* invoker = ptr_at(mi + OFF_MI_INVOKER);
    if (!invoker) { set_error("method %s 的 invoker_method 为 NULL（会跳零）",
                             kstr((const char*) ptr_at(mi + OFF_MI_NAME))); return nullptr; }

    // ★★ 不再经过 Runtime::Invoke(0xaa0e90)。
    // 实测：无论是 hack_thread 还是 Unity 主线程，只要走 Runtime::Invoke，它开头的
    //   `0xaa0ec4 bl 0xaa685c`（Method::Init）就会卡死 —— 15:55 卡 hack_thread、
    //   16:00 直接把 Unity 主线程卡成 ANR（`ANR in ... UnityPlayerActivity`）。
    // 改成直接调 MethodInfo+0x08 的 invoker，跳过 Method::Init：
    //   Runtime::Invoke 尾部 0xaa0eec:`ldp x0,x8,[x22]` / mov x1,x22 / mov x2,x21 /
    //   mov x3,x20 / `blr x8` ⇒ invoker 前四参 = (methodPointer, method, obj, params)，
    //   第五参 exc 它没赋值（x4 是垃圾）—— 我们显式传 &e，即使 invoker 用 x4 也安全。
    // 目标方法都是 AOT 已编译的（methodPointer 在 r-x 段），invoker 必定有效。
    typedef void* (*Fn_invoker5)(void*, void*, void*, void**, void**);
    void* mp = ptr_at(mi + OFF_MI_POINTER);
    void* e = nullptr;
    if (g_traceInvoke)
        LOGI("invoke: #4 mp=%p invoker=%p pcount=%u flags=0x%02x params0=%p",
             mp, invoker, (unsigned) u8_at(mi + OFF_MI_PCOUNT),
             (unsigned) u8_at(mi + 0x44), a.p[0]);
    void* r = ((Fn_invoker5) invoker)(mp, method, obj, a.p, &e);
    if (g_traceInvoke) LOGI("invoke: #5 returned r=%p e=%p", r, e);
    if (e) { log_exception(e); if (exc) *exc = true; return nullptr; }
    return r;
}

// ★★ HybridCLR 解释执行入口。
//
// 来源（反汇编 dump 镜像，见 PROGRESS「共享桩解码」）：
//   0xb70f6c（0 参 void 桩，被 BlackListCheck / UploadExceptionUserLog / .ctor 共用）：
//        mov x8,x1 ; str x0,[sp,#8] ; add x1,sp,#8 ; mov x0,x8 ; mov x2,xzr ; bl 0xb89ef4
//   0xb5e0c0（2 参 bool 桩，被 IsOwnBlueprint / IsOwnMap 共用）：
//        str x0,[sp,#8] ; str w1,[sp,#0x10] ; str w2,[sp,#0x18]
//        add x1,sp,#8 ; add x2,sp,#4 ; mov x0,x3 ; bl 0xb89ef4 ; ldrb w0,[sp,#4]
//   ⇒ 0xb89ef4(methodInfo, argsArray, retBuf, exc)，其中 argsArray[0]=this、随后是各实参，
//     每项都是「指向实参值」的指针（与 IL2CPP params 数组同构）。
//   这解释了为什么裸调桩会崩（那 5 个方法共用 0xb89808，而 0xb89808 是抛异常桩）、
//   也解释了为什么传 methodPointer 给 invoker 会卡死（invoker 要的是 InterpMethodInfo*）。
bool interp_invoke(void* mi, void** args, void* retBuf, bool* exc) {
    if (exc) *exc = false;
    if (!init() || !mi || !g_rva_interp_exec) return false;
    ensure_thread();
    void* e = nullptr;
    // 第三参不能给 NULL：实测传 nullptr 时 Interpreter::Execute 会在 +0x4c 处解引用 [x2+0x51]
    // 直接 SIGSEGV（17:24 真机 tombstone：fault addr 0x51，x2=0）。给一块 32 字节本地缓冲，
    // 有返回值的调用再从这里读。
    uint64_t localRet[4] = {0, 0, 0, 0};
    void* rb = retBuf ? retBuf : (void*) localRet;
    typedef void (*Fn_interp)(void*, void**, void*, void**);
    ((Fn_interp) (g_base + g_rva_interp_exec))(mi, args, rb, &e);
    if (e) { log_exception(e); if (exc) *exc = true; return false; }
    return true;
}

bool call_static_void(void* method, void** params) {
    if (!init() || !method) return false;
    ensure_thread();
    void* invoker = ptr_at((uintptr_t) method + OFF_MI_INVOKER);
    if (!invoker) { set_error("static method invoker == NULL"); return false; }
    void* e = nullptr;
    p_invoke(method, nullptr, params, &e);
    if (e) { log_exception(e); return false; }
    return true;
}

void* call_instance(void* method, void* obj, void** params, bool* exc) {
    if (exc) *exc = false;
    if (!init() || !method) return nullptr;
    ensure_thread();
    void* invoker = ptr_at((uintptr_t) method + OFF_MI_INVOKER);
    if (!invoker) { set_error("instance method invoker == NULL"); return nullptr; }
    void* e = nullptr;
    void* r = p_invoke(method, obj, params, &e);
    if (e) { log_exception(e); if (exc) *exc = true; return nullptr; }
    return r;
}

static uint8_t ret_type_code(void* method) {
    uintptr_t ty = (uintptr_t) ptr_at((uintptr_t) method + OFF_MI_RETTYPE);
    return ty ? u8_at(ty + OFF_TYPE_CODE) : 0;
}

int32_t ret_i32(void* method, void* r) {
    uintptr_t v = (uintptr_t) r;
    // 兼容「上游把值类型返回值装箱」的变体：真 64 位堆地址才按 boxed 解
    if (v > 0xFFFFFFFFull && method) {
        uint8_t tc = ret_type_code(method);
        if (tc == T_I4 || tc == T_U4 || tc == T_BOOLEAN || tc == 0x03) {
            size_t hdr = p_header_size ? p_header_size() : OBJ_HEADER;
            if (mapped(v + hdr, 4, 1)) return (int32_t) u32_at(v + hdr);
        }
    }
    return (int32_t) v;
}

bool ret_bool(void* method, void* r) { return ret_i32(method, r) != 0; }

// ==================================================================== 调试输出
void dump_classes(int n) {
    std::lock_guard<std::mutex> lk(g_mtx);
    if (!g_scanned) return;
    LOGI("---- 扫到 %zu 个 klass，前 %d 个 ----", g_klasses.size(), n);
    for (int i = 0; i < n && i < (int) g_klasses.size(); ++i) {
        uintptr_t k = (uintptr_t) g_klasses[i];
        const char* nm = (const char*) ptr_at(k + OFF_KLASS_NAME);
        const char* ns = (const char*) ptr_at(k + OFF_KLASS_NS);
        LOGI("  [%02d] %p %s%s%s", i, (void*) k,
             (is_cstr(ns, 96) && ns[0]) ? ns : "", (is_cstr(ns, 96) && ns[0]) ? "." : "",
             kstr(nm));
    }
}

void dump_methods(void* klass, int n) {
    if (!klass) return;
    uintptr_t k = (uintptr_t) klass;
    uint16_t cnt = u16_at(k + OFF_KLASS_MCOUNT);
    void* methods = ptr_at(k + OFF_KLASS_METHODS);
    LOGI("---- %s 有 %u 个方法，前 %d 个 ----",
         kstr((const char*) ptr_at(k + OFF_KLASS_NAME)), (unsigned) cnt, n);
    if (!methods) return;
    int shown = 0;
    for (uint16_t i = 0; i < cnt && shown < n; ++i) {
        uintptr_t mi = (uintptr_t) ptr_at((uintptr_t) methods + i * 8);
        if (!mi) continue;
        LOGI("  [%02d] %s/%u  mi=%p mp=%p inv=%p", shown,
             kstr((const char*) ptr_at(mi + OFF_MI_NAME)),
             (unsigned) u8_at(mi + OFF_MI_PCOUNT), (void*) mi,
             ptr_at(mi + OFF_MI_POINTER), ptr_at(mi + OFF_MI_INVOKER));
        ++shown;
    }
}

void dump_context(const char* klass_name) {
    dump_classes(20);
    void* k = nullptr;
    {
        std::lock_guard<std::mutex> lk(g_mtx);
        auto it = g_by_name.find(klass_name ? klass_name : "");
        if (it != g_by_name.end()) k = it->second;
    }
    if (k) { log_klass_info(k); dump_methods(k, 20); }
    else LOGI("context: 没有名为 %s 的 klass", klass_name ? klass_name : "(null)");
}

// ==================================================================== ModBridge.dll + ModShm
static ModShm g_shm = {};
static std::atomic<bool> g_bridgeLoaded{false};

const char* app_files_dir() {
    if (g_shm.dynDir[0] != '\0' && access(g_shm.dynDir, F_OK) == 0) {
        return g_shm.dynDir;
    }

    static char s_dir[256] = {0};
    if (s_dir[0] != '\0') return s_dir;

    // 1. 从当前加载的 libmodmenu.so 自身路径反推 <app_files_dir>（兼容任意官服/渠道服包名及多开分身目录）
    Dl_info dli = {};
    if (dladdr((const void*) &app_files_dir, &dli) != 0 && dli.dli_fname) {
        const char* slash = strrchr(dli.dli_fname, '/');
        if (slash && slash > dli.dli_fname) {
            size_t len = (size_t) (slash - dli.dli_fname);
            if (len < sizeof(s_dir) && strstr(dli.dli_fname, "/files/") != nullptr && strstr(dli.dli_fname, "/data/local/tmp") == nullptr) {
                memcpy(s_dir, dli.dli_fname, len);
                s_dir[len] = '\0';
                if (access(s_dir, F_OK) == 0) {
                    if (g_shm.dynDir[0] == '\0') {
                        strncpy(g_shm.dynDir, s_dir, sizeof(g_shm.dynDir) - 1);
                        g_shm.dynDir[sizeof(g_shm.dynDir) - 1] = '\0';
                    }
                    LOGI("app_files_dir (dladdr): %s", s_dir);
                    return s_dir;
                }
                s_dir[0] = '\0';
            }
        }
    }

    // 2. 兜底：读取 /proc/self/cmdline 获取当前包名
    char cmdline[192] = {0};
    if (FILE* f = fopen("/proc/self/cmdline", "rb")) {
        size_t n = fread(cmdline, 1, sizeof(cmdline) - 1, f);
        fclose(f);
        cmdline[n] = '\0';
        if (char* colon = strchr(cmdline, ':')) *colon = '\0';
    }
    if (cmdline[0] != '\0') {
        char cand[256];
        snprintf(cand, sizeof(cand), "/data/user/0/%s/files", cmdline);
        if (access(cand, F_OK) != 0) {
            snprintf(cand, sizeof(cand), "/data/data/%s/files", cmdline);
        }
        strncpy(s_dir, cand, sizeof(s_dir) - 1);
        s_dir[sizeof(s_dir) - 1] = '\0';
        if (g_shm.dynDir[0] == '\0') {
            strncpy(g_shm.dynDir, s_dir, sizeof(g_shm.dynDir) - 1);
            g_shm.dynDir[sizeof(g_shm.dynDir) - 1] = '\0';
        }
        LOGI("app_files_dir (cmdline): %s", s_dir);
        return s_dir;
    }

    return "/data/data/com.zzonegame.aresvirus2.android/files";
}

ModShm* mod_shm() {
    return &g_shm;
}

void mod_set_flag(uint32_t bit, bool on) {
    if (on) g_shm.flags |= bit;
    else    g_shm.flags &= ~bit;
}

bool mod_enqueue_cmd(int32_t type, int32_t a1, int32_t a2, int32_t a3, int64_t aL) {
    uint32_t w = g_shm.cmdWriteIdx;
    uint32_t r = g_shm.cmdReadIdx;
    if ((w - r) >= 31u) return false;
    ModCmd& c = g_shm.cmds[w & 31u];
    c.cmdType = type;
    c.arg1 = a1;
    c.arg2 = a2;
    c.arg3 = a3;
    c.argL = aL;
    c.status = 0;
    c.extra = 0;
    __atomic_store_n(&g_shm.cmdWriteIdx, w + 1u, __ATOMIC_RELEASE);
    return true;
}

bool load_mod_bridge(const char* path) {
    if (g_bridgeLoaded.load(std::memory_order_acquire)) return true;
    if (!g_hotImage) return false;
    if (!init()) return false;
    ensure_thread();

    if (!path || access(path, R_OK) != 0) {
        set_error("ModBridge.dll not readable");
        return false;
    }

    static int s_attempts = 0;
    static uint64_t s_lastAttemptMs = 0;
    if (s_attempts >= 5) return g_shm.bridgeReady != 0;
    uint64_t now = now_ms();
    if (s_attempts > 0 && (now - s_lastAttemptMs) < 1000) return false;

    void* kFile = find_class_via_images_ns("File", "System.IO");
    void* kAsm  = find_class_via_images_ns("Assembly", "System.Reflection");
    if (!kFile || !kAsm) {
        LOGE("bridge: File=%p Assembly=%p missing", kFile, kAsm);
        return false;
    }
    if (p_class_init && (!ptr_at((uintptr_t) kFile + OFF_KLASS_METHODS) || !(u8_at((uintptr_t) kFile + 0x133) & 4))) {
        p_class_init(kFile);
    }
    if (p_class_init && (!ptr_at((uintptr_t) kAsm + OFF_KLASS_METHODS) || !(u8_at((uintptr_t) kAsm + 0x133) & 4))) {
        p_class_init(kAsm);
    }

    uint8_t tcStr[1] = { 0x0E }; // ELEMENT_TYPE_STRING
    uint8_t tcArr[1] = { 0x1D }; // ELEMENT_TYPE_SZARRAY
    void* mRead = find_method_typed(kFile, "ReadAllBytes", tcStr, 1);
    void* mLoad = find_method_typed(kAsm, "Load", tcArr, 1);
    if (!mRead || !mLoad) {
        LOGE("bridge: mRead=%p mLoad=%p missing", mRead, mLoad);
        return false;
    }

    void* mpRead = ptr_at((uintptr_t) mRead + OFF_MI_POINTER);
    void* fnLoad = (g_load1Mi == mLoad && g_load1Orig) ? g_load1Orig : ptr_at((uintptr_t) mLoad + OFF_MI_POINTER);
    if (!mpRead || !fnLoad) {
        LOGE("bridge: mp null read=%p load=%p", mpRead, fnLoad);
        return false;
    }
    if (!is_readable_fresh((uintptr_t) mpRead, 4) || !is_readable_fresh((uintptr_t) fnLoad, 4)) {
        LOGE("bridge: mp not readable read=%p load=%p", mpRead, fnLoad);
        return false;
    }

    s_attempts++;
    s_lastAttemptMs = now;

    typedef void* (*Fn_static1)(void*, void*);
    Fn_string_new strNew = p_string_new;
    if (!strNew) { LOGE("bridge: p_string_new null"); return false; }
    void* pStr = strNew(path);
    if (!pStr) { LOGE("bridge: strNew failed"); return false; }

    void* byteArr = ((Fn_static1) mpRead)(pStr, mRead);
    uint32_t blen = byteArr ? u32_at((uintptr_t) byteArr + 0x18) : 0;
    LOGE("bridge: ReadAllBytes -> %p len=%u", byteArr, blen);
    if (!byteArr || blen < 512) return false;

    void* bridgeAsm = ((Fn_static1) fnLoad)(byteArr, mLoad);
    LOGE("bridge: Assembly.Load -> %p", bridgeAsm);
    if (!bridgeAsm) return false;

    void* ia = ptr_at((uintptr_t) bridgeAsm + 0x10);
    void* bImg = ia ? ptr_at((uintptr_t) ia) : nullptr;
    LOGE("bridge: bridgeImg=%p", bImg);
    if (!bImg || !p_class_from_name) return false;

    void* kEntry = p_class_from_name(bImg, "ModBridge", "ModEntry");
    LOGE("bridge: ModEntry=%p", kEntry);
    if (!kEntry) return false;

    if (p_class_init && (!ptr_at((uintptr_t) kEntry + OFF_KLASS_METHODS) || !(u8_at((uintptr_t) kEntry + 0x133) & 4))) {
        p_class_init(kEntry);
    }
    void* mInit = find_method_raw(kEntry, "Init", 1);
    LOGE("bridge: Init=%p (methods=%p)", mInit, ptr_at((uintptr_t) kEntry + OFF_KLASS_METHODS));
    if (!mInit) return false;

    if (g_shm.dynDir[0] == '\0') {
        app_files_dir();
    }
    uint32_t savedFlags = g_shm.flags;
    uint32_t savedMg    = g_shm.mgRateX10;
    char savedDir[256];
    memcpy(savedDir, g_shm.dynDir, sizeof(savedDir));
    memset(&g_shm, 0, sizeof(g_shm));
    g_shm.magic = MOD_SHM_MAGIC;
    g_shm.version = 1;
    g_shm.flags = savedFlags;
    g_shm.mgRateX10 = savedMg ? savedMg : 100u;
    memcpy(g_shm.dynDir, savedDir, sizeof(savedDir));
    // HybridCLR Interpreter::Execute takes direct values in args[] (not pointers-to-values)
    void* args[1] = { (void*) &g_shm };
    bool exc = false;
    bool ok = interp_invoke(mInit, args, nullptr, &exc);
    LOGE("bridge: ★ ModEntry.Init(&g_shm=%p) ok=%d exc=%d ready=%u magic=%#x",
         (void*) &g_shm, (int) ok, (int) exc, g_shm.bridgeReady, g_shm.magic);
    if (ok && !exc && g_shm.bridgeReady == 1) {
        g_bridgeLoaded.store(true, std::memory_order_release);
        return true;
    }
    return false;
}

} // namespace il2cpp
} // namespace ares

// ========================================================================
//                              7 个功能
// ========================================================================
using namespace ares;
using namespace ares::il2cpp;

namespace {

// 功能状态：0 未尝试 / 1 生效 / 2 失败
static int g_status[7] = {0, 0, 0, 0, 0, 0, 0};

static void* s_call(void* method, void* obj, Args& a, bool* exc) { return invoke(method, obj, a, exc); }

// ---- 单例链
static void* game_system() {
    static void* cached = nullptr;
    if (cached) return cached;
    void* k = find_class_by_name("CGameSystem");
    if (!k) return nullptr;
    void* inst = find_static_field(k, "_Instance");
    if (!inst) {
        void* m = find_method(k, "get_Instance", 0);
        if (m) {
            Args a; bool exc = false;
            inst = s_call(m, nullptr, a, &exc);      // 静态属性 getter，懒初始化
        }
    }
    if (inst) cached = inst;
    return cached;
}

static void* system_prop(const char* getter) {
    void* gs = game_system();
    if (!gs) return nullptr;
    void* k = klass_of(gs);
    if (!k) return nullptr;
    void* m = find_method(k, getter, 0);
    if (!m) { LOGW("CGameSystem 没有 %s", getter); return nullptr; }
    Args a; bool exc = false;
    return s_call(m, gs, a, &exc);
}

static void* scene_mgr() {
    static void* cached = nullptr;
    if (cached) return cached;
    void* k = find_class_by_name("CGameSceneMgr");
    if (!k) return nullptr;
    void* inst = find_static_field(k, "_Instance");
    if (!inst) {
        void* m = find_method(k, "get_Instance", 0);
        if (m) { Args a; bool exc = false; inst = s_call(m, nullptr, a, &exc); }
    }
    if (inst) cached = inst;
    return cached;
}

// 当前角色 CEntity：优先静态字段 m_pCharacterEntity，退化到 GetEntityByCustomID(GetCurrentChararcterCustomID())
static int current_custom_id() {
    void* sm = scene_mgr();
    if (!sm) return 0;
    void* k = klass_of(sm);
    void* m = find_method(k, "GetCurrentChararcterCustomID", 0);
    if (!m) return 0;
    Args a; bool exc = false;
    return ret_i32(m, s_call(m, sm, a, &exc));
}

static void* current_entity() {
    void* sm = scene_mgr();
    if (!sm) return nullptr;
    void* k = klass_of(sm);
    void* e = find_static_field(k, "m_pCharacterEntity");
    if (e) return e;
    int cid = current_custom_id();
    if (!cid) return nullptr;
    void* m = find_method(k, "GetEntityByCustomID", 1);
    if (!m) return nullptr;
    Args a; a.add_i32(cid); bool exc = false;
    return s_call(m, sm, a, &exc);
}

static void* entity_part(void* entity, const char* field) {
    if (!entity) return nullptr;
    void* ek = klass_of(entity);
    if (!ek) return nullptr;
    int32_t off = instance_field_offset(ek, field);
    if (off < 0) { LOGW("CEntity 没有字段 %s", field); return nullptr; }
    return read_instance_object(entity, off);
}

// customID <= 0 或就是当前角色 → 当前实体；否则走 CGameSceneMgr.GetEntityByCustomID
static void* entity_for(int customID) {
    void* cur = current_entity();
    if (customID <= 0) return cur;
    if (cur && klass_of(cur) && current_custom_id() == customID) return cur;
    void* sm = scene_mgr();
    if (!sm) return cur;
    void* k = klass_of(sm);
    void* m = find_method(k, "GetEntityByCustomID", 1);
    if (!m) return cur;
    Args a; a.add_i32(customID); bool exc = false;
    void* e = s_call(m, sm, a, &exc);
    return e ? e : cur;
}

} // anonymous namespace

namespace ares {
namespace mod {

// ---------------------------------------------------------------- 功能1 发物品
bool add_item(int customID, int e_type, int config_id, int num, bool fire_event) {
    void* container = system_prop("get_containerSystem");
    if (!container) { g_status[0] = 2; set_error("功能1: 拿不到 containerSystem"); return false; }
    void* k = klass_of(container);
    void* m = find_method(k, "AddMaterialToContainer", 5);
    if (!m) {
        g_status[0] = 2;
        set_error("功能1: 找不到 AddMaterialToContainer/5");
        il2cpp::dump_methods(k, 20);
        return false;
    }
    if (customID <= 0) customID = current_custom_id();
    Args a;
    a.add_i32(customID);        // int
    a.add_i32(e_type);          // enum eContainerTypeEnum (底层 int32)
    a.add_i32(config_id);       // int
    a.add_i32(num);             // int
    a.add_bool(fire_event);     // bool（槽低字节 0/1，1 字节读也正确）
    bool exc = false;
    s_call(m, container, a, &exc);
    g_status[0] = exc ? 2 : 1;
    if (!exc) LOGI("add_item: cid=%d type=%d cfg=%d num=%d ok", customID, e_type, config_id, num);
    return !exc;
}

// ---------------------------------------------------------------- 功能2 属性
int get_property(int customID, int e_type, int key, int default_value) {
    void* part = entity_part(entity_for(customID), "propertyPart");
    if (!part) { g_status[1] = 2; return default_value; }
    void* k = klass_of(part);
    void* m = find_method(k, "GetValue", 3);
    if (!m) {
        g_status[1] = 2;
        set_error("功能2: 找不到 CPropertyPart.GetValue/3");
        il2cpp::dump_methods(k, 20);
        return default_value;
    }
    Args a;
    a.add_i32(e_type);          // enum ePropValueType
    a.add_i32(key);
    a.add_i32(default_value);
    bool exc = false;
    void* r = s_call(m, part, a, &exc);
    g_status[1] = exc ? 2 : 1;
    return exc ? default_value : ret_i32(m, r);
}

bool set_property(int customID, int e_type, int key, int value) {
    void* part = entity_part(entity_for(customID), "propertyPart");
    if (!part) { g_status[1] = 2; return false; }
    void* k = klass_of(part);
    void* m = find_method(k, "SetValue", 3);
    if (!m) {
        g_status[1] = 2;
        set_error("功能2: 找不到 CPropertyPart.SetValue/3");
        il2cpp::dump_methods(k, 20);
        return false;
    }
    Args a;
    a.add_i32(e_type);
    a.add_i32(key);
    a.add_i32(value);
    bool exc = false;
    s_call(m, part, a, &exc);
    g_status[1] = exc ? 2 : 1;
    return !exc;
}

// ---------------------------------------------------------------- 功能3 Buff
bool add_buff(int customID, int buff_id, uint64_t trigger_uid) {
    void* part = entity_part(entity_for(customID), "buffPart");
    if (!part) { g_status[2] = 2; return false; }
    void* k = klass_of(part);
    static const uint8_t sig[2] = {T_I4, T_U8};          // AddBuff(int, ulong)，排除 AddBuff(int, CEntity)
    void* m = find_method_typed(k, "AddBuff", sig, 2);
    if (!m) m = find_method(k, "AddBuff", 2);
    if (!m) {
        g_status[2] = 2;
        set_error("功能3: 找不到 CBuffPart.AddBuff(int,ulong)");
        il2cpp::dump_methods(k, 20);
        return false;
    }
    Args a;
    a.add_i32(buff_id);
    a.add_u64(trigger_uid);
    bool exc = false;
    void* r = s_call(m, part, a, &exc);
    g_status[2] = exc ? 2 : 1;
    return !exc && ret_bool(m, r);
}

// ---------------------------------------------------------------- 功能6 兜底
bool grant_blueprint(int customID, int blueprint_id) {
    return add_item(customID, 6 /* Blueprint */, blueprint_id, 1, true);
}

// ---------------------------------------------------------------- 4~7 hook
typedef void (*Fn_void_u64_i32)(void*, uint64_t, int32_t);
typedef void (*Fn_void_i32_bool)(void*, int32_t, bool);
typedef bool (*Fn_bool_i32_i32)(void*, int32_t, int32_t);
typedef bool (*Fn_bool_0)(void*);
typedef void (*Fn_void_0)(void*);

static volatile bool g_on_ammo = false;
static volatile bool g_on_dura = false;
static volatile bool g_on_bp   = false;
static volatile bool g_on_ac   = false;

static Fn_void_u64_i32 o_ReduceAmmo    = nullptr;
static Fn_void_i32_bool o_SetEndurance = nullptr;
static Fn_bool_i32_i32 o_IsOwnBlueprint = nullptr;

static void hk_ReduceAmmo(void* self, uint64_t uid, int32_t cid) {
    if (g_on_ammo) return;                       // 唯一扣弹入口，直接吞掉
    if (o_ReduceAmmo) o_ReduceAmmo(self, uid, cid);
}

static void hk_SetEndurance(void* self, int32_t value, bool fire) {
    if (g_on_dura) return;                       // 唯一写耐久入口，直接吞掉
    if (o_SetEndurance) o_SetEndurance(self, value, fire);
}

static bool hk_IsOwnBlueprint(void* self, int32_t cid, int32_t bp) {
    if (g_on_bp) return true;
    return o_IsOwnBlueprint ? o_IsOwnBlueprint(self, cid, bp) : false;
}

#define AC_BOOL_HOOK(fname)                                                   \
    static Fn_bool_0 o_##fname = nullptr;                                     \
    static bool hk_##fname(void* self) {                                      \
        if (g_on_ac) return false;                                            \
        return o_##fname ? o_##fname(self) : false;                           \
    }
#define AC_VOID_HOOK(fname)                                                   \
    static Fn_void_0 o_##fname = nullptr;                                     \
    static void hk_##fname(void* self) {                                      \
        if (g_on_ac) return;                                                  \
        if (o_##fname) o_##fname(self);                                       \
    }

AC_BOOL_HOOK(CheckPayItem)
AC_BOOL_HOOK(CheckRaffleItem)
AC_BOOL_HOOK(CheckPointItem)
AC_BOOL_HOOK(CheckOtherItem)
AC_BOOL_HOOK(CheckImportantItem)
AC_VOID_HOOK(BlackListCheck)
AC_VOID_HOOK(UploadExceptionUserLog)
AC_VOID_HOOK(RequestExceptionUserID)
AC_VOID_HOOK(RequestExceptionUserIDV1)

namespace {

// 同一个 methodPointer 被多少个方法共用 → 判断它是不是共享 thunk（解释器桥）
static int shared_method_pointer(uintptr_t mp) {
    static std::unordered_map<uintptr_t, int> cache;
    auto it = cache.find(mp);
    if (it != cache.end()) return it->second;
    int hits = 0;
    std::lock_guard<std::mutex> lk(g_mtx);
    for (void* k : g_klasses) {
        uintptr_t kk = (uintptr_t) k;
        uint16_t n = u16_at(kk + OFF_KLASS_MCOUNT);
        void* methods = ptr_at(kk + OFF_KLASS_METHODS);
        if (!methods) continue;
        for (uint16_t i = 0; i < n; ++i) {
            uintptr_t mi = (uintptr_t) ptr_at((uintptr_t) methods + i * 8);
            if (!mi) continue;
            if ((uintptr_t) ptr_at(mi + OFF_MI_POINTER) == mp) { if (++hits >= 8) break; }
        }
        if (hits >= 8) break;
    }
    cache[mp] = hits;
    return hits;
}

struct HookRec { const char* label; const char* klass; const char* method; int nparams;
                 void* mi; void* mp; bool exec; int shared; bool ok; };

static std::vector<HookRec> g_hooks;

// hook 一个方法：运行时按名字查出来 → 判可执行 → 判共享 → DobbyHook
static bool install_hook(const char* klass_name, const char* method_name, int nparams,
                         void* repl, void** orig, const char* label) {
    HookRec rec{label, klass_name, method_name, nparams, nullptr, nullptr, false, -1, false};
    void* k = find_class_by_name(klass_name);
    if (!k) {
        LOGE("[%s] 类 %s 没找到", label, klass_name);
        il2cpp::dump_context(klass_name);
        g_hooks.push_back(rec);
        return false;
    }
    void* mi = find_method(k, method_name, nparams);
    if (!mi) mi = find_method_in_hierarchy(k, method_name, nparams);
    if (!mi) {
        LOGE("[%s] 方法 %s.%s/%d 没找到", label, klass_name, method_name, nparams);
        il2cpp::dump_methods(k, 20);
        g_hooks.push_back(rec);
        return false;
    }
    rec.mi = mi;
    uintptr_t mp = (uintptr_t) ptr_at((uintptr_t) mi + OFF_MI_POINTER);
    rec.mp = (void*) mp;
    rec.exec = mp && mapped(mp, 4, 2);
    rec.shared = mp ? shared_method_pointer(mp) : -1;

    if (!mp) {
        LOGE("[%s] methodPointer == NULL", label);
    } else if (!rec.exec) {
        LOGE("[%s] methodPointer %p 不在可执行段 → 解释执行（HybridCLR），不能 inline hook；走 invoke 通道",
             label, (void*) mp);
    } else if (rec.shared >= 4) {
        LOGE("[%s] methodPointer %p 被 %d 个方法共用（共享 thunk），拒绝 hook",
             label, (void*) mp, rec.shared);
    } else {
        int rc = DobbyHook((void*) mp, (dobby_dummy_func_t) repl, (dobby_dummy_func_t*) orig);
        if (rc == 0) {
            rec.ok = true;
            LOGI("[%s] hooked %s.%s/%d @ %p (shared=%d)", label, klass_name, method_name,
                 nparams, (void*) mp, rec.shared);
        } else {
            LOGE("[%s] DobbyHook rc=%d @ %p", label, rc, (void*) mp);
        }
    }
    g_hooks.push_back(rec);
    return rec.ok;
}

} // anonymous namespace

bool set_infinite_ammo(bool on) {
    if (on && !o_ReduceAmmo) {
        if (!install_hook("CEquipmentSystem", "ReduceAmmo", 2,
                          (void*) hk_ReduceAmmo, (void**) &o_ReduceAmmo, "F4/ammo")) {
            g_status[3] = 2;
            return false;
        }
    }
    g_on_ammo = on;
    g_status[3] = on ? 1 : 0;
    return true;
}

bool set_infinite_durability(bool on) {
    if (on && !o_SetEndurance) {
        if (!install_hook("CMaterial", "SetEndurance", 2,
                          (void*) hk_SetEndurance, (void**) &o_SetEndurance, "F5/dura")) {
            g_status[4] = 2;
            return false;
        }
    }
    g_on_dura = on;
    g_status[4] = on ? 1 : 0;
    return true;
}

bool set_own_blueprint(bool on) {
    if (on && !o_IsOwnBlueprint) {
        if (!install_hook("CContainerSystem", "IsOwnBlueprint", 2,
                          (void*) hk_IsOwnBlueprint, (void**) &o_IsOwnBlueprint, "F6/blueprint")) {
            g_status[5] = 2;
            return false;       // 兜底走 grant_blueprint()
        }
    }
    g_on_bp = on;
    g_status[5] = on ? 1 : 0;
    return true;
}

bool set_anticheat_off(bool on) {
    static bool installed = false;
    if (on && !installed) {
        installed = true;                 // 只装一次：重复 DobbyHook 同一地址会失败并刷日志
        bool any = false;
        any |= install_hook("CAntiCheatSystem", "BlackListCheck", 0,
                            (void*) hk_BlackListCheck, (void**) &o_BlackListCheck, "F7/BlackListCheck");
        any |= install_hook("CAntiCheatSystem", "CheckPayItem", 0,
                            (void*) hk_CheckPayItem, (void**) &o_CheckPayItem, "F7/CheckPayItem");
        any |= install_hook("CAntiCheatSystem", "CheckRaffleItem", 0,
                            (void*) hk_CheckRaffleItem, (void**) &o_CheckRaffleItem, "F7/CheckRaffleItem");
        any |= install_hook("CAntiCheatSystem", "CheckPointItem", 0,
                            (void*) hk_CheckPointItem, (void**) &o_CheckPointItem, "F7/CheckPointItem");
        any |= install_hook("CAntiCheatSystem", "CheckOtherItem", 0,
                            (void*) hk_CheckOtherItem, (void**) &o_CheckOtherItem, "F7/CheckOtherItem");
        any |= install_hook("CAntiCheatSystem", "CheckImportantItem", 0,
                            (void*) hk_CheckImportantItem, (void**) &o_CheckImportantItem, "F7/CheckImportantItem");
        // 下面三个是 private，可能被 strip；失败不算致命
        install_hook("CAntiCheatSystem", "RequestExceptionUserID", 4,
                     (void*) hk_RequestExceptionUserID, (void**) &o_RequestExceptionUserID, "F7/RequestExceptionUserID");
        install_hook("CAntiCheatSystem", "RequestExceptionUserIDV1", 4,
                     (void*) hk_RequestExceptionUserIDV1, (void**) &o_RequestExceptionUserIDV1, "F7/RequestExceptionUserIDV1");
        install_hook("CAntiCheatSystem", "UploadExceptionUserLog", 0,
                     (void*) hk_UploadExceptionUserLog, (void**) &o_UploadExceptionUserLog, "F7/UploadExceptionUserLog");
        if (!any) { g_status[6] = 2; return false; }
    }
    g_on_ac = on;
    g_status[6] = on ? 1 : 0;
    return true;
}

int status(int idx) { return (idx >= 0 && idx < 7) ? g_status[idx] : -1; }

void report() {
    LOGI("======== ares bridge report ========");
    LOGI("base=%p domain=%p klasses=%zu", (void*) g_base, g_domain,
         (g_scanned ? g_klasses.size() : 0));
    LOGI("F1 add_item=%d F2 property=%d F3 buff=%d", g_status[0], g_status[1], g_status[2]);
    LOGI("F4 ammo=%d F5 dura=%d F6 blueprint=%d F7 anticheat=%d",
         g_status[3], g_status[4], g_status[5], g_status[6]);
    for (const HookRec& h : g_hooks)
        LOGI("  %-26s mi=%p mp=%p exec=%d shared=%d hooked=%d",
             h.label, h.mi, h.mp, h.exec ? 1 : 0, h.shared, h.ok ? 1 : 0);
}

} // namespace mod
} // namespace ares
