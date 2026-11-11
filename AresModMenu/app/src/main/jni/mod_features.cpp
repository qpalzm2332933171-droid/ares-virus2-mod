// mod_features.cpp —— 把菜单 Changes() 的 featNum 接到 il2cpp_bridge 的 mod:: API 上
#define FTAG "AresFeat"
#define FLOGI(...) __android_log_print(ANDROID_LOG_INFO, FTAG, __VA_ARGS__)
#define FLOGE(...) __android_log_print(ANDROID_LOG_ERROR, FTAG, __VA_ARGS__)

#include "mod_features.h"
#include "il2cpp_bridge.h"

#include <android/log.h>
#include <cstring>
#include <unistd.h>

// il2cpp 在 domain 就绪前调 il2cpp_domain_get 会 SIGSEGV，
// 用一次性信号保护把它变成"这次失败，下次再试"
// 不用 sigaction：易盾的 watchdog 对信号处理改动敏感，而且 SIGKILL 本来就捕不到。
static bool init_guarded() { return ares::il2cpp::init(); }

static int g_customID = 0;

static int resolve_custom_id() {
    using namespace ares::il2cpp;
    if (g_customID != 0) return g_customID;
    void* kMgr = find_class_by_name("CGameSceneMgr");
    if (!kMgr) { FLOGE("CGameSceneMgr not found"); return 0; }
    void* inst = find_static_field(kMgr, "Instance");
    if (!inst) inst = find_static_field(kMgr, "_Instance");
    if (!inst) inst = find_static_field(kMgr, "m_Instance");
    if (inst) {
        void* m = find_method(kMgr, "GetCurrentChararcterCustomID", 0);
        if (m) {
            Args a;
            bool exc = false;
            void* r = invoke(m, inst, a, &exc);
            if (!exc && r) {
                int v = ret_i32(m, r);
                if (v != 0) { g_customID = v; FLOGI("customID=%d (via method)", v); return v; }
            }
        }
    }
    void* ent = find_static_field(kMgr, "m_pCharacterEntity");
    if (ent) {
        void* kEnt = klass_of(ent);
        if (kEnt) {
            int32_t off = instance_field_offset(kEnt, "nCustomID");
            if (off >= 0) {
                int v = (int)*(int32_t*)((uint8_t*)ent + 0x10 + off);
                if (v != 0) { g_customID = v; FLOGI("customID=%d (via field)", v); return v; }
            }
        }
    }
    FLOGE("resolve_custom_id failed");
    return 0;
}

namespace modfeat {

void warmup() {
    // 2026-10-29：不再需要 30 秒「躲检测」延迟 —— 现在 init() 是零 dlopen 纯读，
    // 不产生任何会被壳盯上的调用。只等 libil2cpp.so 映射出来、domain 全局非零即可。
    bool ok = false;
    for (int i = 0; i < 120; i++) {
        if (i % 10 == 0) FLOGI("warmup: attempt %d ...", i);
        if (init_guarded()) { ok = true; FLOGI("warmup: init ok at attempt %d", i); break; }
        sleep(1);
    }
    FLOGI("bridge init=%d base=%p", (int) ok, (void*) ares::il2cpp::base());
    if (!ok) FLOGI("bridge 仍未就绪：last_error=%s", ares::il2cpp::last_error());
}

bool add_item(int configID, int num) {
    int cid = resolve_custom_id();
    if (!cid) return false;
    // eContainerTypeEnum::Bag = 0
    bool ok = ares::mod::add_item(cid, 0, configID, num, true);
    FLOGI("add_item(cid=%d, id=%d, n=%d) -> %d", cid, configID, num, (int)ok);
    return ok;
}

bool set_prop(int propKey, int uiValue) {
    int cid = resolve_custom_id();
    if (!cid) return false;
    // 内部值 = 显示值 × 1000（fPropertyScale）
    int raw = uiValue * 1000;
    // ePropValueType::Original = 1
    bool ok = ares::mod::set_property(cid, 1, propKey, raw);
    FLOGI("set_prop(cid=%d, key=%d, ui=%d raw=%d) -> %d", cid, propKey, uiValue, raw, (int)ok);
    return ok;
}

int get_prop(int propKey) {
    int cid = resolve_custom_id();
    if (!cid) return 0;
    int raw = ares::mod::get_property(cid, 1, propKey, 0);
    return raw / 1000;
}

// 当前角色实体自身的 UID（CEntity.UID, ulong, CEntity.cs:16）。
// 必要性：CBuffPart.AddBuff(int,ulong) 内部是 CGameSceneMgr.Instance.GetEntity(triggerUID)
// 再转调 AddBuff(int,CEntity)；triggerUID 查不到实体时 triggerEntity=null（CBuffPart.cs:129-130），
// 后续 Reset(entity)/事件里会解引用 → NRE。传「自己」的 UID 才能保证 triggerEntity 非空。
static uint64_t resolve_uid() {
    using namespace ares::il2cpp;
    void* kMgr = find_class_by_name("CGameSceneMgr");
    if (!kMgr) return 0;
    // 首选游戏自己的 API：CGameSceneMgr.GetCurrentCharacterUID()（CGameSceneMgr.cs:441-448）
    // 它直接返回 m_pCharacterEntity.UID，省掉手算字段偏移这一层（也避开 field offset 是否含对象头的争议）。
    if (void* m = find_method(kMgr, "GetCurrentCharacterUID", 0)) {
        void* mgr = find_static_field(kMgr, "Instance");
        if (!mgr) mgr = find_static_field(kMgr, "_Instance");
        if (!mgr) mgr = find_static_field(kMgr, "m_Instance");
        if (mgr) {
            Args a; bool exc = false;
            void* r = invoke(m, mgr, a, &exc);
            if (!exc && r) {
                uint64_t u = (uint64_t) (uintptr_t) r;
                if (u) { FLOGI("uid=%llu (via GetCurrentCharacterUID)", (unsigned long long) u); return u; }
            }
        }
    }
    // 兜底：手读 m_pCharacterEntity.UID
    void* ent = find_static_field(kMgr, "m_pCharacterEntity");
    if (!ent) return 0;
    void* kEnt = klass_of(ent);
    if (!kEnt) return 0;
    int32_t off = instance_field_offset(kEnt, "UID");
    if (off < 0) return 0;
    // OBJ_HEADER = 0x10，见 il2cpp_bridge.h 第 18 行；读 ulong 不能走 read_instance_object（那个按指针读）
    uint64_t v = 0;
    const void* addr = (const void*) ((uintptr_t) ent + 0x10u + (uintptr_t) off);
    __builtin_memcpy(&v, addr, sizeof(v));
    return v;
}

bool add_buff(int buffID) {
    int cid = resolve_custom_id();
    if (!cid) return false;
    uint64_t uid = resolve_uid();
    if (!uid) {
        FLOGE("add_buff: 拿不到当前角色 UID，拒绝调用（否则 triggerEntity=null）");
        return false;
    }
    bool ok = ares::mod::add_buff(cid, buffID, uid);
    FLOGI("add_buff(cid=%d, buff=%d, uid=%llu) -> %d", cid, buffID,
          (unsigned long long) uid, (int) ok);
    return ok;
}

void set_flag(int which, bool on) {
    switch (which) {
        case 4: ares::mod::set_infinite_ammo(on);      break;
        case 5: ares::mod::set_infinite_durability(on); break;
        case 6: ares::mod::set_own_blueprint(on);      break;
        case 7: ares::mod::set_anticheat_off(on);      break;
        default: break;
    }
}

void report() { ares::mod::report(); }

} // namespace modfeat

// ==================================================================== 功能 1/2/3 的 C 接口
// 分层归因：步骤1 = 参数，步骤2 = 类/字段/方法，步骤3 = 调用与校验。
// 失败日志一律带「步骤n/3」，看 logcat 的 AresFeat / AresModMenu 就能定位卡在哪一层。
extern "C" {

static const char* err_or_none() {
    const char* e = ares::il2cpp::last_error();
    return e ? e : "(bridge 未记录错误串)";
}

bool feat1_add_item(int configID, int num) {
    using namespace ares::il2cpp;

    if (configID <= 0) {
        FLOGE("feat1 步骤1/3 参数非法: configID=%d（物品配置 ID 必须 > 0）", configID);
        return false;
    }
    if (num <= 0) { FLOGI("feat1 步骤1/3 num=%d 非法，按 1 处理", num); num = 1; }

    const bool inited = init();
    if (!inited || !domain()) {
        FLOGE("feat1 步骤2/3 il2cpp 未就绪: init=%d domain=%p", (int) inited, (void*) domain());
        return false;
    }
    void* gs = find_class_by_name("CGameSystem");
    if (!gs) {
        FLOGE("feat1 步骤2/3 堆里找不到类 CGameSystem");
        dump_context("CGameSystem");
        return false;
    }
    // 不强制 _Instance 非空：CGameSystem.Instance 是懒加载（CGameSystem.cs:116-126），
    // 首次访问前 _Instance 本为 null，bridge 的 game_system() 有 get_Instance 兜底。
    void* inst = find_static_field(gs, "_Instance");
    if (!inst && !find_method(gs, "get_Instance", 0)) {
        FLOGE("feat1 步骤2/3 CGameSystem 既无 _Instance 也无 get_Instance（单例不可达）");
        return false;
    }

    const bool ok = modfeat::add_item(configID, num);
    if (!ok) FLOGE("feat1 步骤3/3 调用 AddMaterialToContainer 失败: %s", err_or_none());
    else     FLOGI("feat1 ok: configID=%d num=%d", configID, num);
    return ok;
}

bool feat2_set_prop(int propKey, int uiValue) {
    using namespace ares::il2cpp;

    if (propKey <= 0) {
        FLOGE("feat2 步骤1/3 propKey=%d：该 UI 项在 DPropertyKey 里没有对应 key，不写", propKey);
        return false;
    }
    if (uiValue < 0) {
        FLOGE("feat2 步骤1/3 uiValue=%d 非法（显示值不能为负）", uiValue);
        return false;
    }

    const bool inited = init();
    if (!inited || !domain()) {
        FLOGE("feat2 步骤2/3 il2cpp 未就绪: init=%d domain=%p", (int) inited, (void*) domain());
        return false;
    }
    void* ek = find_class_by_name("CEntity");
    if (!ek) {
        FLOGE("feat2 步骤2/3 堆里找不到类 CEntity");
        dump_context("CEntity");
        return false;
    }
    if (instance_field_offset(ek, "propertyPart") < 0) {
        FLOGE("feat2 步骤2/3 CEntity 上没有实例字段 propertyPart");
        return false;
    }

    if (!modfeat::set_prop(propKey, uiValue)) {
        FLOGE("feat2 步骤3/3 CPropertyPart.SetValue 失败 key=%d raw=%d: %s",
              propKey, uiValue * 1000, err_or_none());
        return false;
    }
    // SetValue 对不存在的 sPropertyNode 是「直接 return」，不抛异常（CPropertyPart.cs:60-64），
    // 所以必须读回校验，否则会静默成功。
    const int back = modfeat::get_prop(propKey);
    if (back != uiValue) {
        FLOGE("feat2 步骤3/3 写后校验失败 key=%d 期望=%d 实际=%d（该 key 的节点不存在或被夹取）",
              propKey, uiValue, back);
        return false;
    }
    FLOGI("feat2 ok: key=%d ui=%d raw=%d", propKey, uiValue, uiValue * 1000);
    return true;
}

bool feat3_add_buff(int buffID, int seconds) {
    using namespace ares::il2cpp;

    // AddBuff(int,ulong) / AddBuff(int,CEntity) 都没有时长参数，持续时间由 Buff 配置决定。
    (void) seconds;

    if (buffID <= 0) {
        FLOGE("feat3 步骤1/3 buffID=%d 非法", buffID);
        return false;
    }

    const bool inited = init();
    if (!inited || !domain()) {
        FLOGE("feat3 步骤2/3 il2cpp 未就绪: init=%d domain=%p", (int) inited, (void*) domain());
        return false;
    }
    void* ek = find_class_by_name("CEntity");
    if (!ek) {
        FLOGE("feat3 步骤2/3 堆里找不到类 CEntity");
        dump_context("CEntity");
        return false;
    }
    if (instance_field_offset(ek, "buffPart") < 0) {
        FLOGE("feat3 步骤2/3 CEntity 上没有实例字段 buffPart");
        return false;
    }

    const bool ok = modfeat::add_buff(buffID);
    if (!ok) {
        FLOGE("feat3 步骤3/3 CBuffPart.AddBuff 返回 false（配置无效/黑名单/前置未满足/被抵抗）: %s",
              err_or_none());
    } else {
        FLOGI("feat3 ok: buffID=%d（本地生效，不代表服务端确认）", buffID);
    }
    return ok;
}

} // extern "C"
