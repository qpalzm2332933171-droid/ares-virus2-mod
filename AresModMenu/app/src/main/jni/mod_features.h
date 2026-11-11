#pragma once
#include <cstdint>

namespace modfeat {
void warmup();
bool add_item(int configID, int num);
bool set_prop(int propKey, int uiValue);
int  get_prop(int propKey);
bool add_buff(int buffID);
void set_flag(int which, bool on);   // 4=子弹 5=耐久 6=蓝图 7=反作弊
void report();
}

// ---------------------------------------------------------------- 功能 1/2/3 的窄接口
// Main.cpp 的 Changes() 直接调这三个；内部自己取 customID，失败时按步骤打日志。
// 语义与非 C 版 modfeat::add_item/set_prop/add_buff 一致，只是把参数收窄、加了分层归因。
extern "C" {

// 功能1 发物品：configID=物品配置 ID，num=数量；走向背包(Bag=0)。
bool feat1_add_item(int configID, int num);

// 功能2 写属性：propKey=DPropertyKey，uiValue=UI 显示值；内部 ×1000 后以 Original 写入并读回校验。
// propKey<=0 表示该 UI 项在 DPropertyKey 里没有对应 key，直接判失败。
bool feat2_set_prop(int propKey, int uiValue);

// 功能3 加 Buff：buffID=Buff 配置 ID。seconds 目前穿不进 AddBuff，保留但未消费。
bool feat3_add_buff(int buffID, int seconds);

} // extern "C"
