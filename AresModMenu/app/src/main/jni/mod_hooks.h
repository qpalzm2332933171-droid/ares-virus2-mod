// mod_hooks.h —— 功能 4/5/6/7 的 native hook 层（自带探测 + 判定 + 安装）
//
// 与 il2cpp_bridge.{cpp,h} 完全解耦：只调用它公开的 init()/find_class_by_name()/find_method()，
// 不新增也不修改它的任何符号。本文件自己的状态全部私有在 mod_hooks.cpp 里。
//
// 设计前提（见 _tier3_hooks.md）：
//   * 目标方法全部来自游戏主程序集 Assembly-CSharp.dll；
//   * HybridCLR 会把热更程序集的方法编译成解释执行，这类方法的
//     MethodInfo.methodPointer 指向 InterpMethodInfo（数据段），不是可执行代码，
//     DobbyHook 无法 inline hook。hooks_install() 对每个目标打印
//     name / methodPointer / in r-xp? / param_count，据此判定走哪条路。

#pragma once

#ifdef __cplusplus
extern "C" {
#endif

// 启动时调一次（il2cpp 就绪之后）。内部按名字查 MethodInfo → 判定是否可 hook
// → 可 hook 就 DobbyHook（hook 体内部只查 enabled 开关，默认全关）→ 不可 hook 就记录原因。
// 全部目标处理完后自动打印一次 hooks_report()。
void hooks_install(void);

// feat: 4=子弹不减 5=耐久锁定 6=全部蓝图 7=拦截反作弊
// on=false 时 hook 已安装也将原样转发原函数；on=true 且当前不可 hook 时返回无效状态（见 hooks_report）。
void hooks_set_enabled(int feat, bool on);

// 功能 2：写一条属性锁定（key 即 DPropertyKey，value 是整数目标值）。
// 内部落在 CPropertyPart.GetValue 的 hook 上：命中就返回这里登记的值，未命中回落原桩。
void hooks_set_prop(int key, int value);

// 每个功能的 "ok" / "no-aot" / "not-found" / "busy" / "err=N"，一行一个功能，
// 形如 "4=ok 5=no-aot 6=not-found 7=ok"。返回进程生命周期内常驻的静态缓冲。
const char* hooks_report(void);

// 只重试当前 not-found 的目标，不改任何开关状态。
// 用途：启动那一刻游戏的 GC 堆还很小、业务类还没实例化，install_one 必然 not-found；
// 等玩家进主场景后堆里才有 CGameSystem/CEquipmentSystem/CMaterial 的实例，
// 这时候再扫一次才能按「对象头 klass」把它们找出来。返回本次重试的目标数。
int hooks_retry(void);

// 自测：经 MethodInfo->methodPointer 直接调一次所有「已开启」的替换体，
// 用来区分「没装上」与「装上了但游戏没调用」。会打一行 selftest 日志。
void hooks_selftest(void);

#ifdef __cplusplus
}
#endif
