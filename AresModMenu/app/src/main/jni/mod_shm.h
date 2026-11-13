#pragma once
#include <stdint.h>

#define MOD_SHM_MAGIC 0x4D4F4432u /* "2DOM" */

/* EditorPrefsX.endianDiff1 & mod_shm->flags bitmask:
 *   bit 0 (0x01): 0 = Anti-Cheat Interception ENABLED (DEFAULT!), 1 = disabled
 *   bit 1 (0x02): Ammo non-decrease -> increase (+1)
 *   bit 2 (0x04): Lock weapon/equipment durability (full)
 *   bit 3 (0x08): Unlock all paid items / DLC / packages / gifts locally
 *   bit 4 (0x10): Instant interaction (0s operate time)
 *   bit 5 (0x20): Unlimited warehouse container stack
 *   bit 6 (0x40): Noclip (disable player solid colliders)
 *   bit 7 (0x80): Machine Gun (ranged rapid fire while holding right wheel)
 */
enum : uint32_t {
    MOD_FLAG_AC_DISABLE          = 1u << 0,
    MOD_FLAG_AMMO_INC            = 1u << 1,
    MOD_FLAG_LOCK_ENDURANCE      = 1u << 2,
    MOD_FLAG_UNLOCK_PAID         = 1u << 3,
    MOD_FLAG_INSTANT_INTERACT    = 1u << 4,
    MOD_FLAG_INF_STACK_WAREHOUSE = 1u << 5,
    MOD_FLAG_NOCLIP              = 1u << 6,
    MOD_FLAG_MACHINE_GUN         = 1u << 7,
    MOD_FLAG_SEWER_HIGHLIGHT     = 1u << 8,
    MOD_FLAG_ALWAYS_EVENT        = 1u << 9,
    MOD_FLAG_PHONOGRAPH_LOCAL    = 1u << 10,
};

enum : int32_t {
    MOD_CMD_NONE               = 0,
    MOD_CMD_ADD_ITEM           = 1,  /* arg1=itemId, arg2=num, arg3=target(0=Bag,1=Box), argL=boxInstanceId */
    MOD_CMD_SET_PROP           = 2,  /* arg1=propKey, arg2=rawVal, arg3=lock(-1=unchanged,0=unlock,1=lock) */
    MOD_CMD_UNLOCK_ALL_BP      = 3,  /* adds all Blueprint items + depends */
    MOD_CMD_APPLY_BUFF         = 4,  /* arg1=buffId, arg2=durationSec (<=0 = forever/default) */
    MOD_CMD_REMOVE_BUFF        = 5,  /* arg1=buffId (0 = remove all mod-managed buffs) */
    MOD_CMD_RESTORE_ENDURANCE  = 6,  /* restore all equipped + bag equipment to full endurance */
    MOD_CMD_REFRESH_SNAP       = 7,  /* force immediate property + box snapshot */
    MOD_CMD_CLEAR_PROP_LOCKS   = 8,  /* clear all locked properties */
    MOD_CMD_UNLOCK_ALL_PAID    = 9,  /* sync/apply or restore all local paid unlocks */
    MOD_CMD_SYNC_DYN_ASSETS    = 10, /* export runtime dyn_items.json, dyn_buffs.json & missing icons */
    MOD_CMD_DEL_WAREHOUSE_ITEM = 11, /* arg1=count, arg2=boxInstanceId, argL=materialUID */
    MOD_CMD_SET_CURRENCY       = 12, /* arg1=type(0=Diamond,1=GardenCoin,2=GardenPoint,3=CumulativePay), arg2=val */
};

struct ModCmd {
    int32_t cmdType;
    int32_t arg1;
    int32_t arg2;
    int32_t arg3;
    int64_t argL;
    int32_t status;
    int32_t extra;
};

struct ModPropSnap {
    int32_t key;
    int32_t curRaw;
    int32_t origRaw;
    int32_t lockRaw;
    uint8_t locked;
    uint8_t pad[3];
};

struct ModHomeBoxSnap {
    int64_t instanceId;
    int32_t capacity;
    int32_t usedSlots;
};

struct ModBuffSnap {
    int32_t buffId;
    int32_t remainSec;
};

struct ModShm {
    uint32_t magic;          /* 0x00 */
    uint32_t version;        /* 0x04 */
    uint32_t bridgeReady;    /* 0x08 */
    uint32_t tickCount;      /* 0x0C */
    int32_t  sceneReady;     /* 0x10 */
    int32_t  currentCid;     /* 0x14 */
    uint32_t flags;          /* 0x18 */
    uint32_t mgRateX10;      /* 0x1C: Machine gun shots/s * 10 (0 => 100 = 10.0/s) */
    uint32_t cmdWriteIdx;    /* 0x20 */
    uint32_t cmdReadIdx;     /* 0x24 */
    uint32_t dynSeq;         /* 0x28: bumped by ModBridge when dyn JSON/icons finish writing */
    uint32_t cmdOkCount;     /* 0x2C: cumulative executed command successes */
    uint32_t cmdFailCount;   /* 0x30: cumulative executed command failures */
    int32_t  lastFailArg1;   /* 0x34: arg1 (e.g. itemId/buffId) of last failed command */
    uint8_t  pad2[8];        /* 0x38..0x3F */
    ModCmd   cmds[32];       /* 0x40..0x43F (32 * 32 = 1024 B) */
    uint32_t propSeq;        /* 0x440 */
    int32_t  propCount;      /* 0x444 */
    ModPropSnap props[64];   /* 0x448..0x947 (64 * 20 = 1280 B) */
    int32_t  homeBoxCount;   /* 0x948 */
    int32_t  pad3;           /* 0x94C */
    ModHomeBoxSnap boxes[16];/* 0x950..0xA4F (16 * 16 = 256 B) */
    int32_t  activeBuffCount;/* 0xA50 */
    int32_t  lastCmdResult;  /* 0xA54 */
    ModBuffSnap buffs[64];   /* 0xA58..0xC57 (64 * 8 = 512 B) */
    char     lastStatus[256];/* 0xC58..0xD57 */
    char     dynDir[256];    /* 0xD58..0xE57: app files dir for dyn_items.json/dyn_buffs.json/dyn_icons */
};
