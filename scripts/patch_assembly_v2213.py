import argparse, os, struct, subprocess, sys, hashlib

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, ".."))

parser = argparse.ArgumentParser(description="Patch Ares Virus 2 v2.2.13 Assembly-CSharp.dll in-place with zero C-block conflicts")
parser.add_argument("--dec-in", required=True, help="Decrypted original Assembly-CSharp.dll (4,134,400 B)")
parser.add_argument("--enc-orig", required=True, help="Original NHTP-encrypted Assembly-CSharp.dll (4,134,400 B)")
parser.add_argument("--out-pub", default=os.path.join(os.environ.get("TEMP", "."), "ares_modbridge_refs", "Assembly-CSharp.pub.dll"))
parser.add_argument("--out-dec", default=os.path.join(os.environ.get("TEMP", "."), "ares_modbridge_refs", "arsmod_2213.dec.dll"))
parser.add_argument("--out-nhtp", default=os.path.join(REPO_ROOT, "AresModMenu", "app", "src", "main", "assets", "arsmod.dll"))
args = parser.parse_args()

DEC_IN = args.dec_in
ENC_ORIG = args.enc_orig
PUB_REF = args.out_pub
DEC_OUT = args.out_dec
NHTP_OUT = args.out_nhtp
os.makedirs(os.path.dirname(os.path.abspath(PUB_REF)), exist_ok=True)
os.makedirs(os.path.dirname(os.path.abspath(DEC_OUT)), exist_ok=True)

C_BLOCKS = set([22, 941, 1860, 2779, 3698, 4617, 5536, 6455, 7374, 8293, 9212, 10131, 11050, 11969, 12888, 13807, 14726, 15645])

buf = bytearray(open(DEC_IN, "rb").read())
assert len(buf) == 4134400

def write_il(file_off, il_bytes, max_csz, clear_more_sects=False):
    b0 = buf[file_off]
    if (b0 & 3) == 2:
        hsz = 1
        csz = b0 >> 2
    else:
        assert (b0 & 3) == 3
        flags = struct.unpack_from("<H", buf, file_off)[0]
        hsz = (flags >> 12) * 4
        csz = struct.unpack_from("<I", buf, file_off + 4)[0]
        if clear_more_sects and (flags & 0x08):
            assert (file_off // 256) not in C_BLOCKS
            struct.pack_into("<H", buf, file_off, flags & ~0x08)
    assert csz == max_csz, f"unexpected csz {csz} != {max_csz}"
    assert len(il_bytes) <= csz, f"patch {len(il_bytes)} > {csz}"
    il_start = file_off + hsz
    padded = il_bytes + bytes([0x00] * (csz - len(il_bytes)))
    for off in range(il_start, il_start + csz):
        blk = off // 256
        assert blk not in C_BLOCKS, f"hit C-block {blk} at {off:#x}"
    buf[il_start:il_start + csz] = padded
    print(f"  patched off={file_off:#x} (il={il_start:#x}) {len(il_bytes)}/{csz} B")

# 1) Make fields & selected methods Public in metadata (0x0016 = Public|Static, 0x0006 = Public)
for name, off, old_v, new_v in [
    ("EditorPrefsX.endianDiff1",                         0x274a20, 0x0011, 0x0016),
    ("EditorPrefsX.endianDiff2",                         0x274a2a, 0x0011, 0x0016),
    ("EditorPrefsX.idx",                                 0x274a34, 0x0011, 0x0016),
    ("CBuff.m_nCurCycle",                                0x25775e, 0x0001, 0x0006),
    ("CHomeContainerSystem.m_dicContainer",              0x258028, 0x0001, 0x0006),
    ("CHomeItemSystem.m_dicData",                        0x25808c, 0x0001, 0x0006),
    ("CContainerSystem.m_dicData",                       0x257cfe, 0x0001, 0x0006),
    ("CPaySystem.m_listPackage",                         0x2583a2, 0x0001, 0x0006),
    ("CPaySystem.m_listDiamondPackage",                  0x2583ac, 0x0001, 0x0006),
    ("CPaySystem.m_listLimitGift",                       0x2583b6, 0x0001, 0x0006),
    ("CDLCSystem.m_dicDLCInstallDictionary",             0x257d08, 0x0001, 0x0006),
    ("CHolidayImportantExchangeConfigLoader.m_dicConfig",0x26f3f4, 0x0001, 0x0006),
    ("CCharacterBase.m_pCurState",                       0x26cdc0, 0x0004, 0x0006),
    ("CCharacterStateBase.m_pBehavior",                  0x26cfa0, 0x0004, 0x0006),
    ("CMainCharacter.m_bInputAttack",                    0x26d590, 0x0001, 0x0006),
    ("CUIJoystick.m_bEnterCancel",                       0x2503be, 0x0001, 0x0006),
    ("CUIJoystick.m_bPointerDown",                       0x2503c8, 0x0001, 0x0006),
    ("DuJiaoShou.m_bAttackDo",                           0x26cfe6, 0x0001, 0x0006),
    ("DuJiaoShou.fChargePercent",                        0x26cffa, 0x0001, 0x0006),
    ("DuanChangDi.m_bAttackDo",                          0x26d004, 0x0001, 0x0006),
    ("DuanChangDi.fChargePercent",                       0x26d018, 0x0001, 0x0006),
    ("FeiBiao.m_bAttackDo",                              0x26d022, 0x0001, 0x0006),
    ("FeiBiao.fChargePercent",                           0x26d036, 0x0001, 0x0006),
    ("GuLingJingGuai.m_bAttackDo",                       0x26d040, 0x0001, 0x0006),
    ("GuLingJingGuai.fChargePercent",                    0x26d054, 0x0001, 0x0006),
    ("JiHan.m_bAttackDo",                                0x26d072, 0x0001, 0x0006),
    ("JiHan.fChargePercent",                             0x26d086, 0x0001, 0x0006),
    ("YuMao.m_bAttackDo",                                0x26d19e, 0x0001, 0x0006),
    ("YuMao.fChargePercent",                             0x26d1b2, 0x0001, 0x0006),
    ("Yuancheng.m_bAttackDo",                            0x26d1bc, 0x0001, 0x0006),
    ("Yuancheng.fChargePercent",                         0x26d1d0, 0x0001, 0x0006),
    ("YuanchengMulti.m_bAttackDo",                       0x26d1e4, 0x0001, 0x0006),
    ("YuanchengMulti.fChargePercent",                    0x26d1f8, 0x0001, 0x0006),
    ("YuanchengMulti_Free.m_bAttackDo",                  0x26d202, 0x0001, 0x0006),
    ("YuanchengMulti_Free.fChargePercent",               0x26d216, 0x0001, 0x0006),
    ("Yuancheng_Free.m_bAttackDo",                       0x26d220, 0x0001, 0x0006),
    ("Yuancheng_Free.fChargePercent",                    0x26d234, 0x0001, 0x0006),
    ("Yuancheng_LongYan.m_bAttackDo",                    0x26d23e, 0x0001, 0x0006),
    ("Yuancheng_LongYan.fChargePercent",                 0x26d252, 0x0001, 0x0006),
    ("DuJiaoShou.Attack()",                              0x2c644a, 0x0081, 0x0086),
    ("DuanChangDi.Attack()",                             0x2c6558, 0x0081, 0x0086),
    ("FeiBiao.Attack()",                                 0x2c6666, 0x0081, 0x0086),
    ("GuLingJingGuai.Attack()",                          0x2c6750, 0x0081, 0x0086),
    ("JiHan.Attack()",                                   0x2c685e, 0x0081, 0x0086),
    ("YuMao.Attack()",                                   0x2c715e, 0x0081, 0x0086),
    ("Yuancheng.Attack()",                               0x2c726c, 0x0081, 0x0086),
    ("YuanchengLianFa.Attack()",                         0x2c738c, 0x0081, 0x0086),
    ("YuanchengMulti.Attack()",                          0x2c7476, 0x0081, 0x0086),
    ("YuanchengMulti_Free.Attack()",                     0x2c75de, 0x0081, 0x0086),
    ("Yuancheng_Free.Attack()",                          0x2c76da, 0x0081, 0x0086),
    ("Yuancheng_LongYan.Attack()",                       0x2c77e8, 0x0081, 0x0086),
]:
    cur = struct.unpack_from("<H", buf, off)[0]
    assert cur in (old_v, new_v), f"{name}: got {cur:#x}, want {old_v:#x}"
    assert (off // 256) not in C_BLOCKS
    struct.pack_into("<H", buf, off, new_v)
    print(f"  meta {name}: {old_v:#x} -> {new_v:#x} @ {off:#x}")

# Save public reference DLL for csc.exe
open(PUB_REF, "wb").write(buf)

TOK_FLAGS = b"\xeb\x44\x00\x04"  # 0x040044EB: EditorPrefsX.endianDiff1

# 2) CAntiCheatSystem (All cheat checks, blacklists, exception IDs, log uploads, paid checks blocked!)
# Use clean returns + clear MoreSects on Fat headers with EH so Mono/HybridCLR never sees stale EH ranges
for nm, off, csz, il in [
    ("BlackListCheck",           0x8f758, 44,  b"\x2a"),
    ("CheckPayItem",             0x8f788, 343, b"\x16\x2a"),
    ("CheckRaffleItem",          0x8f908, 189, b"\x16\x2a"),
    ("CheckPointItem",           0x8f9e4, 189, b"\x16\x2a"),
    ("CheckOtherItem",           0x8fac0, 138, b"\x16\x2a"),
    ("CheckImportantItem",       0x8fb68, 138, b"\x16\x2a"),
    ("RequestExceptionUserID",   0x8fc10, 160, b"\x2a"),
    ("UploadExceptionUserLog",   0x8fcbc, 310, b"\x2a"),
    ("RequestExceptionUserIDV1", 0x8fe00, 265, b"\x2a"),
    ("OnHttpRequestSucc",        0x8ff18, 168, b"\x2a"),
    ("CheckPaidMaterialById",    0x8ffd8, 54,  b"\x17\x2a"),
]:
    write_il(off, il, csz, clear_more_sects=True)

# 3) CEquipmentSystem.ReduceAmmo (off=0x99b04, il=0x99b10, csz=81, maxStack=6)
# locals in original sig: 0=CMaterial, 1=CEquipment -> wait! Check LocalVarSig of ReduceAmmo before reusing loc.0!
# loc.0 is CMaterial! We must NOT store int32 into CMaterial loc.0!
# Better: push containerSystem + cid + Bag(0) FIRST? Wait: we only need nAmmo ONCE if we compute Add vs Remove AFTER pushing args,
# wait: how did ReduceAmmo fit without loc.0? Push everything on the evaluation stack? No, `eq` stays in loc.1 (`stloc.1`), and stack can hold args!
# Look: nAmmo is fetched once before check: `ldloc.1; callvirt get_pEquipmentConfig; ldfld nAmmo` -> `dup`!
# Wait: `dup` leaves 2 copies on stack -> check `brtrue.s +2` consumes 1 copy, leaves 1 copy on stack?
# No, `if (!nAmmo) { pop; ret; }`? Even simpler! Before pushing `containerSystem`, keep `nAmmo` on stack? No, `containerSystem, cid, Bag` go BEFORE `nAmmo`!
#Wait: check if `ReduceAmmo` saves bytes on BOTH:
# Look at line 2..17 of ReduceAmmo:
# `call get_Instance(5) + callvirt get_materialSystem(5) + ldarg.1(1) + callvirt GetMaterialByUID(5) + isinst CEquipment(5) + stloc.1(1) + ldloc.1(1) + brtrue.s +1(2) + ret(1)` = 26 B!
# Then: `ldloc.1(1) + callvirt get_pEquipmentConfig(5) + ldfld nAmmo(5)` -> instead of storing nAmmo, keep `pEquipmentConfig`?
# No! Look: `ldloc.1; callvirt get_pEquipmentConfig; ldfld nAmmo; brtrue.s +1; ret` = 14 B (sum 40 B).
# Wait: if we leave `containerSystem` -> `cid` -> `0`: we ONLY called `get_pEquipmentConfig; ldfld nAmmo` TWICE (11 B each). Wait: how many bytes total if we check `(flags & 2)` BEFORE branching to 2 small blocks? Wait earlier it was 86 B (ONLY 5 B over 81!).
# HOW TO SAVE 6 BYTES IN 86 B WITH ZERO EXTRA LOCALS:
# Look at lines 1 & 2 vs line 5:
# Before we had TWO calls to `CGameSystem.get_Instance()` (`5` bytes x2 = 10 B)!
# Wait: store `CGameSystem`? Still 1 byte? No, keep `CGameSystem` in `loc.0` (Wait: `loc.0` has type `CMaterial` in metadata, IL verifier checks local type unless untyped!).
# Wait: we don't even NEED `loc.0`! Look: we can keep `CGameSystem.Instance` on the STACK!
# `call get_Instance (5)` + `dup (1)` -> `[gs, gs]`!
# `callvirt get_materialSystem (5)` + `ldarg.1 (1)` + `callvirt GetMaterialByUID (5)` + `isinst CEquipment (5)` + `stloc.1 (1)` -> stack now has `[gs]`!! (Wait: if `loc.1 == null` -> `pop; ret` = 2 bytes `26 2a`!)
# If `nAmmo == 0` -> `pop; ret` = 2 bytes (`26 2a`)!
# Then `[gs]` is ALREADY ON THE STACK!! So `callvirt get_containerSystem` doesn't need `call CGameSystem.get_Instance()` (`5` bytes saved right there -> 86 - 5 + 1 = 82)!
# STILL 1 BYTE OVER? Check `stloc.1; ldloc.1` -> wait: `dup; stloc.1; brtrue.s +2; pop; ret` is same 5 B vs `stloc.1; ldloc.1; brtrue.s +1; ret` (4 B).
# Wait: look how else to save 5 MORE bytes cleanly:
# Keep `sEquipmentConfig` on stack?!
# YES!! `[gs, eq]` -> check `[gs]` only at `get_containerSystem`, AND check `[eq]`? No:
# Look: `ldloc.1` (1) `callvirt get_pEquipmentConfig` (5) `dup` (1) `ldfld nAmmo` (5) `brtrue.s +2` (2) `pop; pop; ret` (3)! Wait that saves calling `get_pEquipmentConfig` the second time (saves 6 B!!)!
# Trace stack carefully!!
# 1. `call get_Instance`           [gs] (5B)
# 2. `dup`                         [gs, gs] (1B)
# 3. `callvirt get_materialSystem` [gs, ms] (5B)
# 4. `ldarg.1`                     [gs, ms, uid] (1B)
# 5. `callvirt GetMaterialByUID`   [gs, mat] (5B)
# 6. `isinst CEquipment`           [gs, eq] (5B)
# 7. `dup; stloc.1`                [gs, eq] (2B) -> or no `loc.1` at all! Keep `eq` on stack or `cfg` on stack! Wait maxStack=6!
# 8. `brtrue.s +2; pop; ret`       [gs] (4B)  (if null: stack had [gs, null], brtrue skips pop+ret, leaves [gs]; wait `dup; brtrue.s +2` checks `eq`! If false: [gs] -> `pop; ret`!)
# 9. `ldloc.1; callvirt get_pEquipmentConfig; ldfld nAmmo` (11B) -> `[gs, nAmmo]`
#Wait: check `dup; brtrue.s`!
# After `ldloc.1; callvirt get_pEquipmentConfig; ldfld nAmmo`: stack = `[gs, nAmmo]`!
# If `nAmmo == 0`, need `pop; pop; ret` -> `dup; brtrue.s +3; pop; pop; ret` (5 B).Wait then stack STILL has `[gs, nAmmo]`!!
#Wait: we still need `containerSystem, cid, Bag(0)` BEFORE `nAmmo`! So `ldloc.1` after `Bag(0)`?
# Compare byte counts:
# What if step 1 DOESN'T keep `[gs]` on stack, and we just keep `nAmmo` NOT in loc, OR look at step 9:
# Wait: step 1 (`call get_Instance; dup` + `pop` on 2 error paths = `5+1+2 = 8 B`) saved ONLY 1 B over calling `get_Instance` twice (`5+5 = 10 B`, no pops)!
# HOW CAN WE SAVE 10+ BYTES IN `ReduceAmmo` cleanly?
# LOOK AT LINE 1..15 OF ORIGINAL `ReduceAmmo`:
# `call get_Instance (5) + callvirt get_materialSystem (5) + ldarg.1 (1) + callvirt GetMaterialByUID (5) + isinst CEquipment (5)` = 21 B.
# Do we NEED `brtrue.s +1; ret` on BOTH `eq == null` AND `nAmmo == 0`? YES, otherwise NullReferenceException when swinging fists (`nAmmo == 0` or `GetMaterialByUID == null`)!
# So wait: total bytes WITH BOTH NULL CHECKS:
# `21` (get eq) + `5` (`stloc.1; ldloc.1; brtrue +1; ret` -> wait: `stloc.1(1) + ldloc.1(1) + brtrue.s +1(2) + ret(1)` = 5B)
# + `11` (`ldloc.1(1) + get_pEquipmentConfig(5) + nAmmo(5)`) + `3` (`brtrue.s +1(2) + ret(1)`) = **40 B**!
# Next block (push 6 args):
# `call get_Instance (5)` + `callvirt get_containerSystem (5)` + `ldarg.2 (1)` + `ldc.i4.0 (1)` + `ldloc.1 (1) + get_pEquipmentConfig (5) + nAmmo (5)` + `ldc.i4.1 (1)` + `ldc.i4.1 (1)` = **25 B**!
# Subtotal = **40 + 25 = 65 B**!! Leaves **16 BYTES** inside 81 B!!
#Wait WHY did my earlier sum say 86 B?! Let's check my arithmetic:
# Earlier I had 2 redundant `stloc.1`/`ldloc.1`? No, branch section at end is:
# `ldsfld (5) + ldc.i4.2 (1) + and (1) + brfalse.s +6 (2) + callvirt Add (5) + ret (1) + callvirt Remove (5) + ret (1)` = **21 B**!
# `65 + 21 = 86 B`! We only need **5 bytes**!
# Wait: look at the branch section (`21 B`):
# Put **`nAmmo` check + fetch `sEquipmentConfig`** using **the local? NO!** Keep **`pEquipmentConfig` isn't needed if**:
# Look at line: `call get_Instance` + `callvirt get_containerSystem` BEFORE checking `eq`? No!
# Look at **`stloc.1` and `ldloc.1`?**
# Wait: why fetch `get_pEquipmentConfig; ldfld nAmmo` AFTER `nAmmo != 0` AGAIN if `nAmmo` was already checked, OR why not:
# Push `call get_Instance; callvirt get_containerSystem; ldarg.2; ldc.i4.0` FIRST? Wait no, you don't want to call `get_containerSystem` if `eq == null`.
# LOOK AT `CContainerSystem` (`0x020004F2`) — does `CEquipment` check `nAmmo` using `get_pEquipmentConfig` (`5` B virtual call) vs a direct field? Wait: `sEquipmentConfig` IS `m_pConfig` downcast, `get_pEquipmentConfig` returns `(sEquipmentConfig)m_pConfig`. Calling `ldloc.1` + `ldfld CMaterial.m_pConfig (5B)`? That's still `5` B + `5` B = `10` B (same).
# WAIT — look at the LAST 21 B (`brfalse +6 + callvirt Add + ret + callvirt Remove + ret`):
# That has `2` `ret`s (`2a`) + `brfalse` = OK.
# Look at `0x99b04` (Fat header 12 B) -> total size BEFORE next method in DLL!
# Let's check what is right AFTER `0x99b04 + 12 + 81` (`0x99b61`)! Is there padding, OR can we make `ReduceAmmo` check **`endianDiff1`** WITHOUT a duplicate `ret`?
# Even simpler: check if `CEquipmentSystem` has ANOTHER caller or padding!Wait: why not `brfalse.s +1` (1 B saved if we omit extra ret? Can't skip ret between Add and Remove — actually **`br.s` over Remove is 2 B, `ret` is 1 B**, so `ret` is already 1 B shorter!).
# WAIT A SECOND!! Look at lines:
# `28 4f210006` (get_Instance) -> ALREADY called at offset 0 AND at offset 40!
# Suppose we put `CGameSystem.Instance` onto stack AT OFFSET 0:
# Stack: `[gs]` (5 B)
# `dup` -> `[gs, gs]` (1 B)
# `callvirt get_materialSystem` -> `[gs, ms]` (5 B)
# `ldarg.1; callvirt GetMaterialByUID; isinst CEquipment; dup; stloc.1` -> `[gs, eq]` (13 B)
# Wait: `stloc.1; ldloc.1; brtrue +2` is 4 B (`0b 07 2d 02`), vs `dup; stloc.1; brtrue +2` is ALSO 4 B (`25 0b 2d 02`), followed by `pop; ret` (`26 2a`, 2 B) = 6 B.
#Wait if `eq != null`: stack has `[gs]`!
# Next: push `get_containerSystem; ldarg.2; ldc.i4.0` -> `[gs.cs, cid, 0]`!! Wait: we STILL need `if (nAmmo == 0) return;` BEFORE calling `Add/Remove`! If we push `[cs, cid, 0]` AND THEN push `nAmmo`, if `nAmmo == 0` we have to pop 3 values (`pop; pop; pop; ret` = 4 B), PLUS we STILL save the second `ldloc.1; get_pEquipmentConfig; ldfld nAmmo` (**11 B saved!!**) AND `get_Instance` (**5 B saved!!**)!!
# WAIT A MINUTE!! COUNT THAT STACK MATH RIGHT NOW!!
# Trace stack carefully when `eq != null`:
# Stack currently `[gs]`!
# `6f 99210006` callvirt get_containerSystem -> `[cs]` (5 B)
# `04`          ldarg.2 (cid)               -> `[cs, cid]` (1 B)
# `16`          ldc.i4.0 (Bag)              -> `[cs, cid, 0]` (1 B)
# `07`          ldloc.1 (eq)                -> `[cs, cid, 0, eq]` (1 B)
# `6f 381e0006` callvirt get_pEquipmentConfig -> `[cs, cid, 0, cfg]` (5 B)
# `7b 883b0004` ldfld nAmmo                 -> `[cs, cid, 0, nAmmo]` (5 B)
# `25`          dup                         -> `[cs, cid, 0, nAmmo, nAmmo]` (1 B)
# `2d 04`       brtrue.s +4                 -> if != 0, skips `pop;pop;pop;pop;ret` (2 B)
# `26 26 26 26 2a` pop x4; ret              -> cleans `[cs, cid, 0, nAmmo]` and returns!! (5 B)
# If != 0, stack is `[cs, cid, 0, nAmmo]` (maxStack = 5 <= 6!!):
# `17 17`       ldc.i4.1; ldc.i4.1          -> `[cs, cid, 0, nAmmo, 1, 1]` (2 B, maxStack = 6 == 6!!)
# `7e`+TOK_FLAGS                            -> `[..., flags]` (5 B -> maxStack = 7!! WAIT!)
# CAREFUL!! At that moment stack would have `[cs, cid, 0, nAmmo, 1, 1, flags]` = **7 items**, while Fat header `maxStack` is **`6`**!!
# Can we increment `maxStack` in the Fat header (`buf[0x99b04 + 2]`) from `6` to `8`?!
# YES!! Fat header offset `+2` (`uint16_t maxStack`) at file offset `0x99b06` (block `0x99b06 // 256 = 2459`, NOT C-block!) is literally a plain uint16!! Setting `maxStack = 8` allows depth 8!
# COUNT THE TOTAL IL LENGTH OF THAT SOLUTION:
# 5 + 1 + 5 + 1 + 5 + 5 + 4 + 2 (stloc.1 / dup / brtrue / pop;ret? check!) = 28 B
# + 5 (get_cs) + 1 + 1 + 11 (nAmmo) + 1 (dup) + 2 (brtrue +5) + 5 (4 pops + ret) = 26 B -> total so far 54 B
# + 2 (1, 1) + 21 (flag check + Add/Remove + ret) = 23 B
# GRAND TOTAL = **77 BYTES <= 81 BYTES**!!

il_red = bytearray()
# Keep [gs] on stack while checking eq != null
il_red += b"\x28\x4f\x21\x00\x06"         # call CGameSystem.get_Instance -> [gs] (5)
il_red += b"\x25"                         # dup -> [gs, gs] (1)
il_red += b"\x6f\x98\x21\x00\x06"         # callvirt get_materialSystem -> [gs, ms] (5)
il_red += b"\x03"                         # ldarg.1 (uid) -> [gs, ms, uid] (1)
il_red += b"\x6f\x54\x1e\x00\x06"         # callvirt GetMaterialByUID -> [gs, mat] (5)
il_red += b"\x75\x37\x05\x00\x02"         # isinst CEquipment -> [gs, eq] (5)
il_red += b"\x25\x0b"                     # dup; stloc.1 -> [gs, eq] (2)
il_red += b"\x2d\x02"                     # brtrue.s +2 (skip pop;ret) (2) -> on branch popped eq -> [gs]
il_red += b"\x26\x2a"                     # pop [gs]; ret (2)
# Check flags/push cs, cid, 0, nAmmo ONCE!
il_red += b"\x6f\x99\x21\x00\x06"         # callvirt get_containerSystem -> [cs] (5)
il_red += b"\x04\x16"                     # ldarg.2 (cid); ldc.i4.0 (Bag) -> [cs, cid, 0] (2)
il_red += b"\x07\x6f\x38\x1e\x00\x06\x7b\x88\x3b\x00\x04" # ldloc.1.pEquipmentConfig.nAmmo -> [cs, cid, 0, nAmmo] (11)
il_red += b"\x25\x2d\x05"                 # dup; brtrue.s +5 -> pops copy -> if !=0 skips 4 pops + ret (3)
il_red += b"\x26\x26\x26\x26\x2a"         # 4x pop ([cs,cid,0,nAmmo]); ret (5)
il_red += b"\x17\x17"                     # ldc.i4.1 (1); ldc.i4.1 (true) -> [cs, cid, 0, nAmmo, 1, 1] (2)
il_red += b"\x7e" + TOK_FLAGS             # ldsfld endianDiff1 (5)
il_red += b"\x18\x5f"                     # ldc.i4.2; and (2)
il_red += b"\x2c\x06"                     # brfalse.s +6 -> skip Add+ret to Remove (2)
il_red += b"\x6f\x2a\x1c\x00\x06\x2a"     # callvirt AddMaterialToContainer/5; ret (6)
il_red += b"\x6f\x2c\x1c\x00\x06\x2a"     # callvirt RemoveMaterialFromContainer/5; ret (6)
assert len(il_red) == 77 <= 81, f"len={len(il_red)}"
# Bump Fat maxStack at 0x99b04+2 from 6 to 8
assert (0x99b06 // 256) not in C_BLOCKS
struct.pack_into("<H", buf, 0x99b06, 8)
write_il(0x99b04, bytes(il_red), 81)

# 4) CMaterial.SetEndurance (off=0x9fc18, il=0x9fc24, csz=125, maxStack=4)
orig_setend = bytes(buf[0x9fc24:0x9fc24+125])
tail_idx = orig_setend.find(b"\x04\x2c")
assert tail_idx == 84, f"unexpected tail_idx {tail_idx}"
tail_event = orig_setend[tail_idx:] # 41 bytes, ends with ret (0x2a)

il_setend = bytearray()
il_setend += b"\x7e" + TOK_FLAGS             # ldsfld endianDiff1 (5)
il_setend += b"\x1a\x5f"                     # ldc.i4.4; and (2)
il_setend += b"\x2c\x0d"                     # brfalse.s +13 (2)  <-- exact 13 bytes below!
il_setend += b"\x02\x7b\x4d\x17\x00\x04\x7b\x26\x3d\x00\x04\x10\x01" # nEndurance = m_pConfig.nEndurance (13)
il_setend += b"\x02"                         # ldarg.0 (1)
il_setend += b"\x03\x16\x28\x67\x00\x00\x0a" # Mathf.Max(nEndurance, 0) (7)
il_setend += b"\x02\x7b\x4d\x17\x00\x04\x7b\x26\x3d\x00\x04\x28\x88\x02\x00\x0a" # Mathf.Min(..., pConfig.nEndurance) (16)
il_setend += b"\x28\x12\x00\x00\x06\x7d\x4f\x17\x00\x04" # stfld m_nEndurance (10)
il_setend += b"\x02\x28\x91\x21\x00\x06"     # ldarg.0; AddSaveTag() (6)
il_setend += tail_event                      # 41 B -> total 103 <= 125
assert len(il_setend) <= 125
write_il(0x9fc18, bytes(il_setend), 125)

# 5) CNetworkMgr.SendRequest (off=0x44568, il=0x44574, csz=183, maxStack=7)
# Gate commands 30 (ReportCheatAccount), 44 (ExceptionUserID), 45 (UploadExceptionUserLog), 214 (ExceptionUserIDV1)
# whenever (EditorPrefsX.endianDiff1 & 1) == 0 (default ON).
def build_send_request_il():
    code = bytearray()
    patches = []  # (branch_op_end_offset, label_name)
    labels = {}
    def emit_br_s(opcode, lbl):
        code.append(opcode)
        patches.append((len(code) + 1, lbl))
        code.append(0)

    # Gate: if ((endianDiff1 & 1) == 0 && (nCommand in {30, 44, 45, 214})) return;
    code += b"\x7e" + TOK_FLAGS            # ldsfld endianDiff1
    code += b"\x17\x5f"                    # ldc.i4.1; and
    emit_br_s(0x2d, "normal")              # brtrue.s normal
    for cmd_val in (30, 44, 45):
        code += b"\x05\x1f" + bytes([cmd_val]) # ldarg.3; ldc.i4.s cmd
        emit_br_s(0x2e, "ret_lbl")             # beq.s ret_lbl
    code += b"\x05\x20" + struct.pack("<I", 214) # ldarg.3; ldc.i4 214
    emit_br_s(0x33, "normal")              # bne.un.s normal
    labels["ret_lbl"] = len(code)
    code += b"\x2a"                        # ret

    labels["normal"] = len(code)
    code += b"\x7e\x11\x00\x00\x0a\x0a"    # value = string.Empty
    code += b"\x02\x7b\x2a\x0f\x00\x04\x04\x12\x00\x6f\x49\x06\x00\x0a\x26" # m_dicUrl.TryGetValue(eURL, out value)
    code += b"\x06\x28\x9a\x00\x00\x0a"    # IsNullOrEmpty(value)
    emit_br_s(0x2c, "send")                # brfalse.s send

    # case eURL == 1 ("GameServer")
    code += b"\x04\x17"                    # ldarg.2; ldc.i4.1
    emit_br_s(0x33, "chk2")                # bne.un.s chk2
    code += b"\x28\xd0\x0c\x00\x06\x04\x72\x29\x35\x00\x70\x14\x28\x4a\x06\x00\x0a\x6f\xd3\x0c\x00\x06"
    code += b"\x72\x29\x35\x00\x70\x7e\x11\x00\x00\x0a\x28\x4b\x06\x00\x0a"
    emit_br_s(0x2b, "shared_try")          # br.s shared_try

    labels["chk2"] = len(code)
    code += b"\x04\x18"                    # ldarg.2; ldc.i4.2
    emit_br_s(0x33, "send")                # bne.un.s send
    code += b"\x28\xd0\x0c\x00\x06\x04\x72\x8b\x31\x00\x70\x14\x28\x4a\x06\x00\x0a\x6f\xd3\x0c\x00\x06"
    code += b"\x72\x8b\x31\x00\x70\x7e\x11\x00\x00\x0a\x28\x4b\x06\x00\x0a"

    labels["shared_try"] = len(code)
    code += b"\x02\x7b\x2a\x0f\x00\x04\x04\x12\x00\x6f\x49\x06\x00\x0a\x26"

    labels["send"] = len(code)
    code += b"\x02\x02\x03\x04\x06\x05\x0e\x04\x28\xd5\x0c\x00\x06\x28\x7c\x00\x00\x0a\x26\x2a"

    for end_off, lbl in patches:
        delta = labels[lbl] - end_off
        assert -128 <= delta <= 127, f"short branch {lbl} out of range: {delta}"
        code[end_off - 1] = delta & 0xFF
    return bytes(code)

il_send = build_send_request_il()
print(f"SendRequest IL size: {len(il_send)}/183 B")
assert len(il_send) <= 183
write_il(0x44568, il_send, 183)

# Helper: miniature label-aware IL emitter & instruction boundary validator
class ILAsm:
    def __init__(self):
        self.code = bytearray()
        self.labels = {}
        self.fixups = []  # (op_end_off, kind, lbl) where kind in ('br8', 'br32', 'sw')

    def lbl(self, name):
        self.labels[name] = len(self.code)

    def raw(self, b):
        self.code += b

    def op(self, *ops):
        self.code.extend(ops)

    def u32(self, v):
        self.code += struct.pack("<I", v & 0xFFFFFFFF)

    def i32(self, v):
        self.code += struct.pack("<i", v)

    def f32(self, v):
        self.code += struct.pack("<f", v)

    def tok(self, op, t):
        self.code.append(op)
        self.code += struct.pack("<I", t)

    def br_s(self, op, lbl):
        self.code.append(op)
        self.fixups.append((len(self.code) + 1, "br8", lbl))
        self.code.append(0)

    def br(self, op, lbl):
        self.code.append(op)
        self.fixups.append((len(self.code) + 4, "br32", lbl))
        self.code += b"\x00\x00\x00\x00"

    def finish(self):
        for end_off, kind, lbl in self.fixups:
            delta = self.labels[lbl] - end_off
            if kind == "br8":
                assert -128 <= delta <= 127, f"br8 {lbl} overflow: {delta}"
                self.code[end_off - 1] = delta & 0xFF
            elif kind == "br32":
                struct.pack_into("<i", self.code, end_off - 4, delta)
        return bytes(self.code)

def verify_il(name, il):
    # Decode every instruction & assert every branch target lands on an instruction start
    SINGLE = {
        0x0e:1,0x0f:1,0x10:1,0x11:1,0x12:1,0x13:1,0x1f:1,0x20:4,0x21:8,0x22:4,0x23:8,
        0x28:4,0x29:4,0x2b:-1,0x2c:-1,0x2d:-1,0x2e:-1,0x2f:-1,0x30:-1,0x31:-1,0x32:-1,0x33:-1,0x34:-1,0x35:-1,0x36:-1,0x37:-1,
        0x38:-4,0x39:-4,0x3a:-4,0x3b:-4,0x3c:-4,0x3d:-4,0x3e:-4,0x3f:-4,0x40:-4,0x41:-4,0x42:-4,0x43:-4,0x44:-4,
        0x6f:4,0x70:4,0x71:4,0x72:4,0x73:4,0x74:4,0x75:4,0x79:4,0x7a:4,0x7b:4,0x7c:4,0x7d:4,0x7e:4,0x7f:4,0x80:4,0x81:4,0x8c:4,0x8d:4,0x8f:4,0xa3:4,0xa4:4,0xa5:4,0xc2:4,0xc6:4,0xd0:4,
    }
    FE = {0x06:4,0x07:4,0x09:2,0x0a:2,0x0b:2,0x0c:2,0x0d:2,0x0e:2,0x0f:2,0x10:2,0x11:2,0x15:4,0x16:4,0x1c:4}
    starts = set()
    targets = []
    i = 0
    while i < len(il):
        starts.add(i)
        op = il[i]; i += 1
        if op == 0xFE:
            op2 = il[i]; i += 1
            i += FE.get(op2, 0)
        elif op == 0x45:
            cnt = struct.unpack_from("<I", il, i)[0]; i += 4
            targs = struct.unpack_from(f"<{cnt}i", il, i); i += 4 * cnt
            for t in targs: targets.append(i + t)
        else:
            sz = SINGLE.get(op, 0)
            if sz == -1:
                d = struct.unpack_from("<b", il, i)[0]; i += 1; targets.append(i + d)
            elif sz == -4:
                d = struct.unpack_from("<i", il, i)[0]; i += 4; targets.append(i + d)
            else:
                i += sz
    assert i == len(il), f"{name}: IL decode overran {i} != {len(il)}"
    for t in targets:
        assert t in starts, f"{name}: bad branch target {t:#x}"
    print(f"  verify_il OK: {name} ({len(il)} B, {len(starts)} insns, {len(targets)} branch targets)")

# 6) CDLCSystem.GetDLCInstallInfoByID (off=0x98f74, Tiny hsz=1, csz=42)
# Gate: if ((endianDiff1 & 8) != 0) return true;
a = ILAsm()
a.raw(b"\x7e" + TOK_FLAGS); a.op(0x1e, 0x5f); a.br_s(0x2c, "orig"); a.op(0x17, 0x2a)
a.lbl("orig")
a.op(0x02); a.tok(0x7b, 0x040016cf); a.op(0x25, 0x03); a.tok(0x6f, 0x0a0002e2); a.br_s(0x2d, "has")
a.op(0x26, 0x16, 0x2a)
a.lbl("has")
a.op(0x03); a.tok(0x6f, 0x0a0002e4); a.op(0x16, 0xfe, 0x02, 0x2a)
il_dlc = a.finish()
verify_il("GetDLCInstallInfoByID", il_dlc)
write_il(0x98f74, il_dlc, 42)

# 7) CHomeContainer.FindMergeTarget (off=0x9c818, Fat hsz=12, csz=135)
# Bypass bCannotCombine and nEndurance!=0 checks when (endianDiff1 & 32) != 0,
# AND strictly match cand.nEndurance == pMaterial.nEndurance (token 0x06001e3f) so modified weapons never merge with unmodified!
a = ILAsm()
a.op(0x03); a.br_s(0x2c, "ret_null") # ldarg.1; brfalse.s ret_null (3)
a.op(0x03); a.tok(0x6f, 0x06001e3d); a.op(0x25); a.br_s(0x2c, "ret_null_pop") # ldarg.1; get_pConfig; dup; brfalse ret_null_pop (9)
a.raw(b"\x7e" + TOK_FLAGS); a.op(0x1f, 32, 0x5f); a.br_s(0x2d, "pop_loop") # ldsfld; ldc.i4.32; and; brtrue.s pop_loop (10)
a.op(0x25); a.tok(0x7b, 0x04003d25); a.br_s(0x2d, "ret_null_pop") # dup; bCannotCombine; brtrue ret_null_pop (9)
a.tok(0x7b, 0x04003d26); a.br_s(0x2d, "ret_null") # nEndurance; brtrue ret_null (7)
a.br_s(0x2b, "loop_init") # br.s loop_init (2)
a.lbl("ret_null_pop")
a.op(0x26) # pop [cfg] (1)
a.lbl("ret_null")
a.op(0x14, 0x2a) # ldnull; ret (2)
a.lbl("pop_loop")
a.op(0x26) # pop [cfg] (1)
a.lbl("loop_init")
a.tok(0x28, 0x0600214f); a.tok(0x6f, 0x06002198); a.op(0x0a, 0x16, 0x0b); a.br_s(0x2b, "cond") # (15)
a.lbl("loop")
a.op(0x06, 0x02); a.tok(0x7b, 0x04001711); a.op(0x07); a.tok(0x6f, 0x0a00027d); a.tok(0x6f, 0x06001e54); a.op(0x0c) # (21)
a.op(0x08); a.br_s(0x2c, "next") # (3)
a.op(0x08); a.tok(0x6f, 0x06001e3c); a.op(0x03); a.tok(0x6f, 0x06001e3c); a.br_s(0x33, "next") # (14)
# Match endurance (cand.nEndurance == pMaterial.nEndurance)
a.op(0x08); a.tok(0x6f, 0x06001e3f); a.op(0x03); a.tok(0x6f, 0x06001e3f); a.br_s(0x33, "next") # (14)
a.op(0x08, 0x2a) # (2)
a.lbl("next")
a.op(0x07, 0x17, 0x58, 0x0b) # (4)
a.lbl("cond")
a.op(0x07, 0x02); a.tok(0x7b, 0x04001711); a.tok(0x6f, 0x0a00027c); a.br_s(0x32, "loop") # (13)
a.op(0x14, 0x2a) # (2)
il_merge = a.finish()
assert len(il_merge) <= 135, f"FindMergeTarget {len(il_merge)} > 135"
verify_il("FindMergeTarget", il_merge)
write_il(0x9c818, il_merge, 135)

# 8) CUI_Game_Input_MainCharacter.OnOperatePointDown (0xdd4f4) & Car.OnOperatePointDown (0xb7cd4)
# When (endianDiff1 & 16) != 0:
#  - OperatePointEnum == 0 -> immediate ReqOnClickOperatePoint() without timed bar
#  - OperatePointEnum != 0 (mining/chopping/fishing) -> arg.fOperateTime = 0.0f
def build_op_down_il(orig_il, fld_cur_op, fld_cb_op):
    a = ILAsm()
    # Keep 0x0000..0x001d intact (30 B: sound + null check)
    a.raw(orig_il[0x0000:0x001e])
    # 0x001e..0x0036 compacted via dup (-5 B)
    a.tok(0x28, 0x06000c8e) # CGameSceneMgr.get_Instance
    a.op(0x25, 0x14); a.tok(0x28, 0x0a000060); a.br_s(0x2c, "scene_ok"); a.op(0x26, 0x2a)
    a.lbl("scene_ok")
    a.tok(0x6f, 0x06000cab); a.op(0x0a) # stloc.0
    # Copy 0x0037..0x00b9 keeping branches mapped!
    # Wait: check internal branches in 0x0037..0x00b9:
    # 0x003e brfalse.s 0x0041 (+1), 0x0051 brtrue.s 0x0054 (+1), 0x005f brfalse.s 0x008e, 0x006c brfalse.s 0x008e, 0x0099 brfalse 0x012b!
    # Let's emit 0x0037..0x00b9 up to 0x0099, rewire 0x0099 & 0x00b8 to L_012b!
    a.raw(orig_il[0x0037:0x0099])
    a.br(0x39, "L_012b")          # 0x0099: brfalse L_012b (was 39 8d000000)
    a.raw(orig_il[0x009e:0x00b8]) # 0x009e..0x00b7 (PaySystem.IsPackageOwn)
    a.br_s(0x2d, "L_012b")        # 0x00b8: brtrue.s L_012b
    # 0x00ba..0x012a JumpShop compacted without loc.4 / loc.5 (-11 B!)
    a.tok(0x28, 0x0600230d); a.op(0x20); a.i32(0x91); a.tok(0x6f, 0x06002315) # GetWnd(145)
    a.op(0x25); a.br_s(0x2c, "no_wnd")
    a.tok(0x73, 0x0600285f) # newobj sUI_Game_JumpShop
    a.op(0x25, 0x02); a.tok(0x7b, fld_cur_op); a.tok(0x7b, 0x04000588); a.tok(0x7d, 0x04001be3)
    a.op(0x25); a.tok(0x28, 0x0600494a); a.op(0x02); a.tok(0x7b, fld_cur_op); a.tok(0x7b, 0x0400058a); a.tok(0x6f, 0x0600494f); a.tok(0x7d, 0x04001be2)
    a.op(0x25); a.tok(0x28, 0x0600494a); a.op(0x02); a.tok(0x7b, fld_cur_op); a.tok(0x7b, 0x04000589); a.tok(0x6f, 0x0600494f); a.tok(0x7d, 0x04001be1)
    a.tok(0x6f, 0x060022fd); a.op(0x26, 0x2a)
    a.lbl("no_wnd")
    a.op(0x26)
    a.lbl("L_012b")
    # 0x012b..0x0169 (JudgeGroup): internal rel branches stay exact until L_016a! (check 0x0136 brfalse.s L_016a, 0x015c brtrue.s L_016a)
    a.raw(orig_il[0x012b:0x0136])
    a.br_s(0x2c, "L_016a")
    a.raw(orig_il[0x0138:0x015c])
    a.br_s(0x2d, "L_016a")
    a.raw(orig_il[0x015e:0x016a])
    # 0x016a..0x0194: Instant check when OperatePointEnum == 0
    a.lbl("L_016a")
    a.op(0x02); a.tok(0x7b, fld_cur_op)       # [op]
    a.op(0x25); a.tok(0x7b, 0x0400057a); a.br_s(0x2d, "pop_0195") # OperatePointEnum != 0 -> go timed path
    a.raw(b"\x7e" + TOK_FLAGS); a.op(0x1f, 16, 0x5f); a.br_s(0x2d, "do_req") # (endianDiff1 & 16) != 0 -> instant!
    a.op(0x25); a.tok(0x7b, 0x04000579); a.op(0x22); a.f32(0.0); a.br_s(0x33, "pop_0195") # OperateTime != 0
    a.lbl("do_req")
    a.tok(0x6f, 0x06000405); a.op(0x2a)       # ReqOnClickOperatePoint(); ret
    a.lbl("pop_0195")
    a.op(0x26)                                # pop [op]
    # 0x0195..0x01af: callback null check + eOperate
    a.raw(orig_il[0x0195:0x01b0])
    # 0x01b0..0x01ef: fOperateTime = (flags & 16) != 0 ? 0f : OperateTime * Max(1 - val/100, 0.1)
    a.op(0x12, 0x01)                          # ldloca.s 1
    a.raw(b"\x7e" + TOK_FLAGS); a.op(0x1f, 16, 0x5f); a.br_s(0x2c, "calc_time")
    a.op(0x22); a.f32(0.0); a.br_s(0x2b, "st_time")
    a.lbl("calc_time")
    a.op(0x02); a.tok(0x7b, fld_cur_op); a.tok(0x7b, 0x04000579) # OperateTime
    a.op(0x22); a.f32(1.0); a.op(0x06); a.tok(0x7b, 0x04001228); a.op(0x19, 0x20); a.i32(600); a.op(0x16); a.tok(0x6f, 0x0600193d)
    a.op(0x6b, 0x22); a.f32(100.0); a.op(0x5b, 0x59)
    a.op(0x22); a.f32(0.10000000149011612); a.tok(0x28, 0x0a0000c9); a.op(0x5a) # Mathf.Max(..., 0.1f) * OperateTime
    a.lbl("st_time")
    a.tok(0x7d, 0x04003872)                   # stfld fOperateTime
    # 0x01f0..0x02a4 tail (straight-line, zero branches)
    a.raw(orig_il[0x01f0:])
    return a.finish()

orig_op_mc = bytes(buf[0xdd4f4 + 12 : 0xdd4f4 + 12 + 677])
orig_op_car = bytes(buf[0xb7cd4 + 12 : 0xb7cd4 + 12 + 677])
# Assert Car differs from MC ONLY on those exact 4 tokens so build_op_down_il preserves Car 100%!
norm_car = bytearray(orig_op_car)
for a_tok, b_tok in [(0x0400199e, 0x04001bce), (0x040019a5, 0x04001bdb), (0x0600238e, 0x06002818), (0x0600238f, 0x06002819)]:
    norm_car = norm_car.replace(struct.pack("<I", a_tok), struct.pack("<I", b_tok))
assert bytes(norm_car) == orig_op_mc, "Car.OnOperatePointDown unexpected IL diff!"

il_op_mc = build_op_down_il(orig_op_mc, 0x04001bce, 0x04001bdb)
il_op_car = build_op_down_il(orig_op_car, 0x0400199e, 0x040019a5)
verify_il("MC.OnOperatePointDown", il_op_mc)
verify_il("Car.OnOperatePointDown", il_op_car)
for h_off in (0xdd4f4, 0xb7cd4):
    assert ((h_off + 2) // 256) not in C_BLOCKS
    struct.pack_into("<H", buf, h_off + 2, 8)  # maxStack 5 -> 8
write_il(0xdd4f4, il_op_mc, 677)
write_il(0xb7cd4, il_op_car, 677)

# 9) CMainCharacter.OnInputCallback (off=0x198354, Fat hsz=12, csz=974)
# Replace 0x0060..0x00ff (exact 160 B slice before 0x0100!) + patch switch[1] offset.
# Note: everything >= 0x0100 stays at exact original offsets!
orig_cb = bytearray(buf[0x198354 + 12 : 0x198354 + 12 + 974])
RANGED_MASK = sum(1 << b for b in (4, 9, 14, 15, 18, 19, 20, 25, 26, 27, 28, 29, 30)) # 0x7e1cc210
a = ILAsm()
# case 0 (0x0060): if (eInput <= 5u) m_bInputMove = (11 >> eInput) & 1; br L_0252
a.op(0x03); a.tok(0x7b, 0x04003866); a.op(0x1b); a.br(0x42, "L_0252")   # bgt.un L_0252 (note: L_0252 resolved wrt 0x0060 base!)
a.op(0x02, 0x1f, 11, 0x03); a.tok(0x7b, 0x04003866); a.op(0x64, 0x17, 0x5f); a.tok(0x7d, 0x04003942)
a.br(0x38, "L_0252")

# case 1: Machine Gun Rewrite (PointerUp=2 / PointerUpLongPress=4 -> Cancel=5) + m_bInputAttack
a.lbl("case1")
a.op(0x03); a.tok(0x7b, 0x04003866); a.op(0x1b); a.br(0x42, "L_0252")   # (uint)eInput > 5 -> skip
a.raw(b"\x7e" + TOK_FLAGS); a.op(0x20); a.u32(128); a.op(0x5f); a.br_s(0x2c, "set_atk")
a.op(0x03); a.tok(0x7b, 0x04003866); a.op(0x18); a.br_s(0x2e, "chk_state")
a.op(0x03); a.tok(0x7b, 0x04003866); a.op(0x1a); a.br_s(0x33, "set_atk")
a.lbl("chk_state")
a.op(0x02); a.tok(0x7b, 0x0400387b); a.br_s(0x2c, "set_atk")            # m_pCurState != null
a.op(0x02); a.tok(0x7b, 0x0400387b); a.tok(0x6f, 0x06004136); a.op(0x19); a.br_s(0x33, "set_atk") # GetStateEnum() == 3
a.op(0x20); a.u32(RANGED_MASK)
a.op(0x02); a.tok(0x7b, 0x0400387b); a.tok(0x6f, 0x06004134)            # GetBehaviorID()
a.op(0x25, 0x1f, 30); a.br_s(0x35, "pop2")
a.op(0x64, 0x17, 0x5f); a.br_s(0x2c, "set_atk")
a.op(0x0f, 0x01, 0x1b); a.tok(0x7d, 0x04003866); a.br_s(0x2b, "set_atk") # pInputData.eInput = 5 (Cancel)
a.lbl("pop2")
a.op(0x26, 0x26)
a.lbl("set_atk")
a.op(0x02, 0x1f, 11, 0x03); a.tok(0x7b, 0x04003866); a.op(0x64, 0x17, 0x5f); a.tok(0x7d, 0x04003943)
a.br(0x38, "L_0252")
# Map L_0252 relative to 0x0060 so all br32 offsets hit exact orig offset 0x0252!
a.labels["L_0252"] = 0x0252 - 0x0060
blk_60 = a.finish()
assert len(blk_60) <= 160, f"OnInputCallback 0x60 block {len(blk_60)} > 160"
case1_rel = a.labels["case1"] + 0x0060 - 0x005b
struct.pack_into("<i", orig_cb, 0x0033, case1_rel) # update switch[1]
orig_cb[0x0060:0x0100] = blk_60 + bytes([0x00] * (160 - len(blk_60)))
verify_il("CMainCharacter.OnInputCallback", bytes(orig_cb))
write_il(0x198354, bytes(orig_cb), 974)

# Patches 10 and 11 removed: CContainer.RefreshContainer and CContainerSystem.AddMaterialToContainer
# left at vanilla logic so infinite stacking NEVER occurs in player bag, drop, or sale containers!


open(DEC_OUT, "wb").write(buf)
os.makedirs(os.path.dirname(NHTP_OUT), exist_ok=True)
r = subprocess.run([sys.executable, os.path.join(SCRIPT_DIR, "nhtp_encrypt.py"), DEC_OUT, ENC_ORIG, "-o", NHTP_OUT], capture_output=True, text=True)
print("nhtp_encrypt rc:", r.returncode)
print(r.stdout)
print(r.stderr)
assert r.returncode == 0 and os.path.getsize(NHTP_OUT) == 4134400
print("WROTE:", NHTP_OUT, "md5=", hashlib.md5(open(NHTP_OUT,"rb").read()).hexdigest())
