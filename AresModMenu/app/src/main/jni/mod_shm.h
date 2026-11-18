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
    MOD_FLAG_CLOUD_ALLOW         = 1u << 11, /* 0 = Block Cloud Save Upload ENABLED (DEFAULT!), 1 = allow */
    MOD_FLAG_AIMBOT              = 1u << 12, /* Lock aim & attacks to nearest hostile unit */
    MOD_FLAG_ESP                 = 1u << 13, /* Draw ESP boxes + tracer lines over hostile units */
    MOD_FLAG_NOSHAKE             = 1u << 14, /* Disable firing Heavy & hit Normal camera shakes */
    MOD_FLAG_AIM_IGNORE_WALL     = 1u << 15, /* Aimbot ignores LoS obstacles when set */
    MOD_FLAG_ESP_BOX             = 1u << 16, /* Draw ESP bounding boxes */
    MOD_FLAG_ESP_LINE            = 1u << 17, /* Draw ESP tracer lines */
    MOD_FLAG_ESP_INFO            = 1u << 18, /* Draw ESP name/HP/distance */
    MOD_FLAG_ESP_NEUTRAL         = 1u << 19, /* Also include neutral huntable units in ESP/Aimbot */
    MOD_FLAG_LOGIN_7891          = 1u << 20, /* Force login with UserID=7891 + cross-UID save compat */
    MOD_FLAG_RANDOM_SPAWN        = 1u << 21, /* Scramble all hostile unit spawns uniformly across all maps */
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
    MOD_CMD_SYNC_DYN_ASSETS    = 10, /* export runtime dyn_*.json & missing icons */
    MOD_CMD_DEL_WAREHOUSE_ITEM = 11, /* arg1=count, arg2=boxInstanceId, argL=materialUID */
    MOD_CMD_SET_CURRENCY       = 12, /* arg1=type(0=Diamond,1=GardenCoin,2=GardenPoint,3=CumulativePay), arg2=val (<0=reset persist) */
    MOD_CMD_TALENT_OP          = 13, /* arg1=subOp, arg2=talentOrSkillId, arg3=targetCid(0=player), argL=level */
    MOD_CMD_HIRE_OP            = 14, /* arg1=hireOp, arg2=id, arg3=extra, argL=entityUid */
    MOD_CMD_TELEPORT_SCENE     = 15, /* arg1=sceneId, arg2=spawnPointId(<0=scene default), arg3=flags */
    MOD_CMD_TASK_OP            = 16, /* arg1=subOp, arg2=taskOrEventId, arg3=prereqFlag, argL=sceneId */
    MOD_CMD_LOGIN_7891_SYNC    = 17, /* arg1=1 sync current UID & save compatibility, arg2=forceImport */
    MOD_CMD_SPAWN_ENTITY       = 18, /* arg1=subOp(1=spawn,2=clear), arg2=unitId, arg3=factionOverride(0=native,1=enemy,2=neutral,3=friend) */
    MOD_CMD_CRICKET_DUEL       = 19, /* arg1=subOp(1=spawn,2=clear), arg2=unitIdA, arg3=unitIdB */
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

#define MOD_ESP_MAX 64

struct ModEspEntity {
    float   x1;          /* 0x00: normalized left [0..1] (top-left origin) */
    float   y1;          /* 0x04: normalized top [0..1] */
    float   x2;          /* 0x08: normalized right [0..1] */
    float   y2;          /* 0x0C: normalized bottom [0..1] */
    float   cx;          /* 0x10: normalized center X */
    float   cy;          /* 0x14: normalized center Y */
    float   distM;       /* 0x18: distance to player in world meters */
    int32_t hp;          /* 0x1C */
    int32_t maxHp;       /* 0x20 */
    int32_t unitId;      /* 0x24 */
    uint8_t hostileType; /* 0x28: 1=hostile, 2=elite/high-HP, 3=neutral */
    uint8_t onScreen;    /* 0x29: 1=in viewport, 0=offscreen */
    uint8_t pad[2];      /* 0x2A..0x2B */
    char    name[20];    /* 0x2C..0x3F: UTF-8 name (truncated cleanly) */
}; /* 64 bytes exact */

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
    uint64_t httpBridgePtr;  /* 0x38..0x3F: pointer to ModHttpBridge (127.0.0.1 loopback proxy) */
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
    uint32_t aimMaxRangeX10; /* 0xE58: Aimbot max range * 10m (0 = auto weapon range / 20m) */
    uint32_t espLineMode;    /* 0xE5C: 0=top, 1=screen center, 2=player screen pos */
    uint32_t espSeq;         /* 0xE60: bumped even/odd or monotonic per ESP frame */
    int32_t  espCount;       /* 0xE64: active entities written (0..64) */
    float    espPlayerNx;    /* 0xE68: player normalized screen X [0..1] */
    float    espPlayerNy;    /* 0xE6C: player normalized screen Y [0..1] */
    float    espScreenW;     /* 0xE70: Unity Screen.width */
    float    espScreenH;     /* 0xE74: Unity Screen.height */
    ModEspEntity espEntities[MOD_ESP_MAX]; /* 0xE78..0x1E77 (64 * 64 = 4096 B) */
};

#define MOD_HTTP_STATE_IDLE       0u
#define MOD_HTTP_STATE_REQ_READY  1u
#define MOD_HTTP_STATE_RESP_READY 2u
#define MOD_HTTP_MAX_REQ          (64u * 1024u)
#define MOD_HTTP_MAX_RESP         (4u * 1024u * 1024u)

struct ModHttpBridge {
    uint32_t port;                     /* 0x00: 127.0.0.1 TCP port (host byte order) */
    uint32_t state;                    /* 0x04: 0=IDLE, 1=REQ_READY, 2=RESP_READY */
    uint32_t seq;                      /* 0x08: monotonically incremented per request */
    int32_t  cmd;                      /* 0x0C: Header_Command int */
    int32_t  httpStatus;               /* 0x10: HTTP status written by C# */
    uint32_t reqLen;                   /* 0x14: request body bytes */
    uint32_t respLen;                  /* 0x18: response body bytes */
    uint32_t routeTag;                 /* 0x1C: 1=/gs, 2=/db */
    uint8_t  reqBuf[MOD_HTTP_MAX_REQ]; /* 0x20 */
    uint8_t  respBuf[MOD_HTTP_MAX_RESP]; /* 0x10020 */
};
