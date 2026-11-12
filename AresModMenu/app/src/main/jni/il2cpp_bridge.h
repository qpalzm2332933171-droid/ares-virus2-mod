// il2cpp_bridge.h —— 按名字定位 IL2CPP 类/方法并在进程内调用它们
//
// 目标进程：Aresvirus2（Unity 2020.3 / IL2CPP v27 / arm64 / HybridCLR）
// 本头文件只依赖 NDK，不依赖工程内其它模块；il2cpp_bridge.cpp 自带日志与 /proc 映射表。
//
// 所有偏移都有反汇编来源，见 _tier2_bridge.md「偏移来源」表：
//   Il2CppClass  name 0x10 (const char*) / namespaze 0x18 / parent 0x58
//                fields 0x80 (FieldInfo*, stride 0x20) / field_count 0x120
//                methods 0x98 (MethodInfo*, stride 8) / method_count 0x11c
//                static_fields 0xb8 / instance_size 0xf8 / flags 0x132(bit1=valuetype)
//   FieldInfo    name 0x00 / type 0x08 / parent 0x10 / offset 0x18 (int32, 静态时相对 static_fields)
//                static 判定 = (type->attrs & 0x10)
//   MethodInfo   methodPointer 0x00 / invoker_method 0x08 / name 0x10 (const char*)
//                klass 0x18 / return_type 0x20 / parameters 0x28 (ParameterInfo*, stride 0x18, type@+0x10)
//                flags 0x44 / parameters_count 0x4a (uint8)
//   Il2CppType   data 0x00 / attrs 0x08 / type 0x0a (uint8, Il2CppTypeEnum)
//   ParameterInfo name 0x00 / parameter_type 0x10 (stride 0x18)
//   Il2CppObject 对象头 0x10 字节，字段/装箱数据从 +0x10 起

#pragma once

#include <cstdint>
#include <cstddef>
#include "mod_shm.h"

namespace ares {
namespace il2cpp {

// ------------------------------------------------------------------ 基础
// 解析导出符号 + il2cpp_thread_attach + 首次扫堆。可重复调用，返回 false 表示 libil2cpp 还没就绪。
bool init();
// libil2cpp.so 的加载基址（0 表示未就绪）
uintptr_t base();
// il2cpp_domain_get() 的结果
void* domain();
// 地址是否可读（内部已处理 Android 14+ 的 top-byte tag；供 mod_hooks 等模块复用）
bool is_readable(uintptr_t addr, size_t len);
// 地址是否落在「可读且可执行」的映射里（走 /proc/self/maps，不是 dl_iterate_phdr）。
// ★ libil2cpp.so 是易盾用 mmap 装进来的，不在 linker 的 soinfo 表里，dl_iterate_phdr 看不到它，
//   所以 mod_hooks 里那套 phdr 判定对 libil2cpp 恒为 false —— 必须用这个。
bool is_executable(uintptr_t addr);

// 热更 image（Assembly-CSharp.dll）的 Il2CppImage*；未加载时返回 nullptr。
// 首次调用会主动去 AppDomain.GetAssemblies 里找一遍（主线程调用最安全）。
void* hot_image();
// 刷新热更 image（热更加载完成前返回 nullptr，调用方应轮询）。
void* refresh_hot_image();

// 诊断开关：为真时 invoke() 会逐步打日志（#1..#5），用来定位卡在哪一步。
void set_trace_invoke(bool on);
// 诊断开关：为真时 invoke() 跳过 il2cpp_thread_attach（验证「主线程重复 attach 是否死锁」）。
void set_skip_attach(bool on);

// ------------------------------------------------------------------ 定位
// 扫堆按类名（klass+0x10）找 klass。name 可写 "CGameSystem"，也可写带命名空间的全名。
void* find_class_by_name(const char* name);
// 先比 namespaze(klass+0x18) 再比 name
void* find_class_by_name_ns(const char* namespaze, const char* name);
// 任意堆对象的 klass：*(void**)obj
void* klass_of(void* obj);
// 在 klass 的方法表里按 名字 + 形参个数 找 MethodInfo
void* find_method(void* klass, const char* name, int param_count);
// 同上，但额外按形参类型码匹配（Il2CppTypeEnum）。用于消歧 AddBuff(int,ulong) vs AddBuff(int,CEntity)
void* find_method_typed(void* klass, const char* name, const uint8_t* type_codes, int n);
// 在 klass 的字段表里找字段；is_static/offset 可传 nullptr
void* find_field(void* klass, const char* name, bool* is_static, int32_t* offset);
// 静态字段的当前值（8 字节读，返回指针/整数）。内部会先 il2cpp_runtime_class_init(klass)。
void* find_static_field(void* klass, const char* name);
// 实例字段的偏移；找不到返回 -1
int32_t instance_field_offset(void* klass, const char* name);
// 读实例字段里的对象指针：obj + 0x10 + field_offset
void* read_instance_object(void* obj, int32_t field_offset);

// ------------------------------------------------------------------ 调用
// il2cpp_runtime_invoke 的实参槽。规则（反汇编确证，见报告）：
//   params[i] 一律是「指向存放该实参的栈槽」的指针：
//     值类型(int/bool/enum/float/double/int64) → 槽里放原始值
//     引用类型(string/object/类实例)          → 槽里放对象指针
struct Args {
    void*    p[8];   // 交给调用约定的 params 数组
    uint64_t s[8];   // 实参槽本体
    int      n;

    // ★ 必须清零：无参方法时调用方仍会把 a.p 作为 params 传进去，
    //   留下未初始化的栈垃圾会被某些通用 invoker 读（实测 get_Length 卡死时就带着这种垃圾）。
    Args() : n(0) {
        for (int i = 0; i < 8; ++i) { p[i] = nullptr; s[i] = 0; }
    }
    void add_i32(int32_t v)   { s[n] = (uint64_t)(uint32_t)v;   p[n] = &s[n]; ++n; }
    void add_u32(uint32_t v)  { s[n] = (uint64_t)v;             p[n] = &s[n]; ++n; }
    void add_i64(int64_t v)   { s[n] = (uint64_t)v;             p[n] = &s[n]; ++n; }
    void add_u64(uint64_t v)  { s[n] = v;                       p[n] = &s[n]; ++n; }
    void add_bool(bool v)     { s[n] = v ? 1u : 0u;             p[n] = &s[n]; ++n; } // 低字节=0/1，1 字节读也正确
    void add_f32(float v)     { uint32_t t; __builtin_memcpy(&t, &v, 4); s[n] = t; p[n] = &s[n]; ++n; }
    void add_f64(double v)    { __builtin_memcpy(&s[n], &v, 8); p[n] = &s[n]; ++n; }
    void add_obj(void* o)     { s[n] = (uint64_t)(uintptr_t)o;  p[n] = &s[n]; ++n; }
};

// ★★ HybridCLR 解释执行入口（RVA 0xb89ef4，从「按签名共享的桩」尾部 bl 目标反汇编得到）。
//   签名：void interp_exec(const MethodInfo* mi, void** args, void* retBuf, Il2CppException** exc)
//   与 Runtime::Invoke 的区别：它直接吃 MethodInfo，不需要先把 methodPointer 变成
//   InterpMethodInfo*；args 是 IL2CPP 的 params 数组（args[0] = this，接下来依次实参，
//   每项是「指向实参值」的指针）。这是目前唯一能可靠调起热更方法的入口。
bool interp_invoke(void* mi, void** args, void* retBuf, bool* exc);

// 用 interp_invoke 调一次 CGameSceneMgr.GetCurrentChararcterCustomID()，把结果打到日志。
// 这是「解释执行入口可用」的最小判据：返回 20200202（实测角色 ID）即成立。
void probe_interp();

// 打印若干代表方法的 MethodInfo 尾部字段（+0x38..+0x68），用于判定
// `[mi+0x50]`（HybridCLR 的 interpData）是否已就绪 —— 这是「该走解释器还是裸调」的判据。
void probe_mi_layout();

// ------------------------------------------------------------------ 热更 DLL 字节替换（真正的功能落点）
// 原生 hook 已经被真机排除干净：改 MethodInfo->methodPointer 三个偏移后游戏侧一次都不命中；
// 统一 invoker(rva 0xb97cf0) 只被 Unity 生命周期方法用到（.ctor/Awake/Update/Start…）；
// 按签名桩(rva 0xb50364) 计数恒 0。原因是业务代码在解释器内部互调（CallInterp_*），
// 不跨 native 边界。唯一还能改行为的地方是「热更程序集本身的字节」。
//
// 做法：把打过补丁的 Assembly-CSharp.dll 放 files/arsmod.dll，hook
// System.Reflection.Assembly.Load(byte[], byte[]) —— 它收到的 byte[] 长度恒等于原 DLL 大小，
// 且原位替换后**文件大小一分不差**（devtmp/min_patch.py 实测 4,054,528 → 4,054,528，delta +0），
// 所以可以直接 memcpy 覆盖数组内容，不需要新建 Il2CppArray，也不动 bundle / 清单 / CRC。
bool load_patch_dll(const char* path);
// 补丁字节的只读视图（供 mod_hooks 的 invoker 兜底路径复用）
const uint8_t* patch_bytes(size_t* outSize);      // 读文件进内存，返回是否成功
void install_load_hook();                   // 装 Assembly.Load hook（幂等）
// ★ 真正的注入入口：hook HybridCLR 的 C++ 侧
//   hybridclr::metadata::InterpreterImage::Load(const void*, unsigned long) @RVA 0xba1bd4
//   托管 Assembly.Load 的 8 个重载 hook 成功后游戏侧命中 0（AOT 调用不走函数头），
//   改挂这个纯 native 函数。它拿到字节后我们按头部（nhtp / MZ）选对应形态的补丁整段覆盖。
void install_interp_load_hook();
int  load_hook_count();                     // 已装成功的 Load 重载个数（0 = 还没装成）

// 无返回值调用；返回 true 表示没有托管异常
bool call_static_void(void* method, void** params);
// 实例调用；*exc 非空表示抛了托管异常（此时返回 nullptr）
void* call_instance(void* method, void* obj, void** params, bool* exc);
// 统一入口
void* invoke(void* method, void* obj, Args& a, bool* exc);
// 返回值按 int32/bool 解释（兼容“上游变体把值类型装箱”的情况，见报告）
int32_t ret_i32(void* method, void* r);
bool    ret_bool(void* method, void* r);

// ------------------------------------------------------------------ 调试
// 打印扫到的前 n 个类名
void dump_classes(int n);
// 打印某个 klass 的前 n 个方法名（找不到类时用）
void dump_methods(void* klass, int n);
// 把一个 klass 的全部方法逐条打出来：name / pcount / mi / mp / RVA / invoker / 是否 r-x。
// 用来判定「mp 相同」到底是共享桩、还是方法表偏移读错。klassName 走 find_class_by_name。
void dump_klass_methods(const char* klassName);
// 定位失败时调用：打印前 20 个类名 + 该类的 20 个方法名
void dump_context(const char* klass_name);
// 探测 AOT 侧 System.Reflection.Assembly.Load 的全部重载并打印 methodPointer / 是否在 r-x 段。
// 这是「hook Assembly.Load 换热更 DLL 字节」这条路的可行性判据。
void probe_load_hook();
// 走 System.AppDomain.GetAssemblies() 反射，列出运行时全部程序集（含 Assembly.Load 进来的
// 热更程序集）。整条链只用 AOT 类，不需要构造字符串。用于拿到热更程序集的 Il2CppAssembly*。
void probe_reflection();
void probe_typeinfo_table();
void probe_invoke_test();
// 裸调反射链：AppDomain.get_CurrentDomain → GetAssemblies → 找热更程序集
void probe_reflection_raw();
// Type.GetType(全名, 程序集) → Il2CppType → s_TypeInfoTable → Il2CppClass
void probe_type_gettype();
// 等热更加载完成后，用 Assembly-CSharp 的 image 走 class_from_name 拿热更类
void probe_hotclass();
// 首个真实功能调用：CGameSceneMgr.Instance → GetCurrentChararcterCustomID()
void probe_call_first();
// 功能1：向背包发放 configId × num（裸调 CContainerSystem.AddMaterialToContainer）
bool feat_add_item(int configId, int num);
// 最近一次失败的详情（可空）
const char* last_error();

// ------------------------------------------------------------------ 菜单 → IL 参数槽
// 唯一可用的菜单↔IL 通道：写一个**同程序集内的静态 int 字段**。
// IL 侧只读两条原语 ldsfld/stsfld，不产生 MemberRef、不碰 BCL、不碰泛型、
// 不碰被 Int32.Parse/CultureInfo 污染的 AOT 泛型路径（那是 payload3 失败的真因）。
//
// 槽位＝ Com.LuisPedroFonseca.ProCamera2D.EditorPrefsX.endianDiff1/2
//        （FieldDef 0x040043B5 / 0x040043B6，运行时从不执行的编辑器工具类）。
// 协议： set_item_slot(configId, num) 之后，游戏下一帧 CGameSceneMgr.Update
//        → MoreFarAmmo 负载读到非 0 的 cfg，就发物品并清零两个槽（天然节流）。
bool set_item_slot(int configId, int num);
bool get_item_slot(int* configId, int* num);   // 读回（自检用）
bool selftest_item_slot();                     // 写→读回→清零，全程打日志

// ModBridge.dll + Shared Memory IPC (v2.2.13)
const char* app_files_dir();
ModShm* mod_shm();
bool    load_mod_bridge(const char* path);
bool    mod_enqueue_cmd(int32_t type, int32_t a1, int32_t a2, int32_t a3, int64_t aL = 0);
void    mod_pump_overflow();
size_t  mod_pending_cmd_count();
void    mod_set_flag(uint32_t bit, bool on);

// 带命名空间的热更类定位（find_class_by_name 只试空命名空间）
void* find_hot_class_ns(const char* ns, const char* name);
bool  read_static_i32 (void* klass, const char* field, int32_t* out);
bool  write_static_i32(void* klass, const char* field, int32_t v, int32_t* prev);

} // namespace il2cpp

// ------------------------------------------------------------------ 7 个 mod 功能
namespace mod {

// 功能1：发物品。eType: Bag=0 Equipment=1 Drop=2 Wallet=3 Sale=4 Blueprint=6 Map=7 EXP=8 MakePart=9 Mount=10
bool add_item(int customID, int e_type, int config_id, int num, bool fire_event = true);

// 功能2：属性。e_type: Original=1 Gain=2 Percent=3 AfterCul=4（AfterCul 只读）
int  get_property(int customID, int e_type, int key, int default_value = 0);
bool set_property(int customID, int e_type, int key, int value);

// 功能3：Buff
bool add_buff(int customID, int buff_id, uint64_t trigger_uid);

// 功能4~7：hook 型。返回 false 表示该方法不是可执行代码（解释执行 / 被 strip），详见日志与 status()
bool set_infinite_ammo(bool on);        // CEquipmentSystem.ReduceAmmo(ulong,int)  → return
bool set_infinite_durability(bool on);  // CMaterial.SetEndurance(int,bool)        → return
bool set_own_blueprint(bool on);        // CContainerSystem.IsOwnBlueprint(int,int)→ true
bool set_anticheat_off(bool on);        // CAntiCheatSystem.* → false / return

// 功能6 的兜底：hook 不可用时直接把蓝图塞进容器（等价效果）
bool grant_blueprint(int customID, int blueprint_id);

// 每个功能的落地状态：0=未尝试 1=已生效 2=失败。idx: 0..6 对应功能1..7
int  status(int idx);
// 打印一次总览（含 4~7 每个方法的 methodPointer / 是否可执行 / 是否共享）
void report();

} // namespace mod
} // namespace ares
