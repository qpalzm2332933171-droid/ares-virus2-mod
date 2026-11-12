using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Com.LuisPedroFonseca.ProCamera2D;
using UnityEngine;

namespace ModBridge
{
    public static class ModEntry
    {
        public static long s_shmPtr = 0;
        private static bool s_inited = false;

        public static void Init(long shmPtr)
        {
            s_shmPtr = shmPtr;
            if (s_inited) return;
            s_inited = true;

            try
            {
                unsafe
                {
                    if (shmPtr != 0)
                    {
                        byte* b = (byte*)shmPtr;
                        *(uint*)(b + 0x00) = 0x4D4F4432u;
                        *(uint*)(b + 0x08) = 1u;
                    }
                }
                GameObject go = new GameObject("AresModBridgeGO");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<ModTicker>();
                Debug.Log("[ModBridge] Init OK shm=" + shmPtr.ToString());
            }
            catch (Exception ex)
            {
                Debug.LogError("[ModBridge] Init ex: " + ex.Message);
            }
        }
    }

    public class ModTicker : MonoBehaviour
    {
        // All 50 DPropertyKey enum values in v2.2.13
        private static readonly int[] ALL_PROP_KEYS = new int[] {
            1, 2,
            51, 52,
            101, 102, 103, 104, 105, 106, 107, 108,
            201, 202, 203, 204, 205, 206, 207, 210, 211, 212, 215,
            301, 302, 303, 304, 305,
            401, 402, 403, 404, 405, 406,
            411, 412, 413,
            421, 422, 423,
            500, 501, 502, 503, 504, 505, 506, 507,
            600, 601
        };

        private int[] _lockKeys = new int[64];
        private int[] _lockVals = new int[64];
        private int _lockCount = 0;

        // Custom-duration tracked buffs (parallel arrays to avoid Dictionary/AOT issues)
        private int[] _buffIds = new int[64];
        private float[] _buffExpireAt = new float[64]; // <= 0 means infinite
        private int _buffCount = 0;

        private uint _tick = 0;
        private uint _prevFlags = 0;
        private int _lastCid = 0;
        private int _lastInfStackCid = 0;

        // Paid unlock backup & state
        private bool _paidApplied = false;
        private int[] _origPkg = null;
        private int[] _origDiamond = null;
        private int[] _origLimit = null;
        private int[] _origDlcKeys = null;
        private int[] _origDlcVals = null;

        // Instant interact backup
        private bool _origBackhomeSaved = false;
        private float _origBackhomeTime = 3f;

        // Noclip state (tracked colliders that were originally non-trigger)
        private Collider2D[] _noclipCols = null;
        private CEntity _noclipPlayer = null;
        private bool _noclipActive = false;

        // Machine Gun timer
        private float _nextMgFireTime = 0f;

        // Dynamic config & missing icon exporter
        private bool _dynExportDone = false;
        private int _lastExportedMatCount = -1;
        private int _lastExportedBuffCount = -1;
        private string[] _iconQueue = null;
        private int _iconQueueLen = 0;
        private int _iconQueuePos = 0;

        // Deferred per-frame batch container refresh / UI notify state
        private uint _dirtyBagMask = 0u;
        private CContainerSystem _dirtyContainerSys = null;
        private bool _dirtyHomeBox = false;
        private int _dirtyLastItemId = 0;

        private unsafe byte* Shm()
        {
            return (byte*)ModEntry.s_shmPtr;
        }

        private unsafe void SetStatus(int code, string msg)
        {
            byte* b = Shm();
            if (b == null) return;
            *(int*)(b + 0xA54) = code;
            byte* dst = b + 0xC58;
            int maxLen = 250;
            int idx = 0;
            if (msg != null)
            {
                for (int i = 0; i < msg.Length && idx < maxLen; i++)
                {
                    char ch = msg[i];
                    if (ch < 0x80)
                    {
                        dst[idx++] = (byte)ch;
                    }
                    else if (ch < 0x800 && idx + 1 < maxLen)
                    {
                        dst[idx++] = (byte)(0xC0 | (ch >> 6));
                        dst[idx++] = (byte)(0x80 | (ch & 0x3F));
                    }
                    else if (idx + 2 < maxLen)
                    {
                        dst[idx++] = (byte)(0xE0 | (ch >> 12));
                        dst[idx++] = (byte)(0x80 | ((ch >> 6) & 0x3F));
                        dst[idx++] = (byte)(0x80 | (ch & 0x3F));
                    }
                }
            }
            dst[idx] = 0;
        }

        private void Awake()
        {
            Debug.Log("[ModBridge] ModTicker Awake");
        }

        private unsafe void Update()
        {
            byte* b = Shm();
            if (b == null) return;

            _tick++;
            *(uint*)(b + 0x0C) = _tick;

            // Sync flags & MachineGun fire rate -> EditorPrefsX continuously
            uint flags = *(uint*)(b + 0x18);
            EditorPrefsX.endianDiff1 = (int)flags;
            uint mgRateX10 = *(uint*)(b + 0x1C);
            if (mgRateX10 < 10u) mgRateX10 = 100u;
            else if (mgRateX10 > 600u) mgRateX10 = 600u;
            EditorPrefsX.endianDiff2 = (int)mgRateX10;

            CGameSceneMgr mgr = CGameSceneMgr.Instance;
            IGameSystem gs = CGameSystem.Instance;
            int cid = 0;
            CEntity player = null;

            if (mgr != null && gs != null && gs.containerSystem != null)
            {
                cid = mgr.GetCurrentChararcterCustomID();
                if (cid != 0)
                {
                    ulong uid = mgr.GetCurrentCharacterUID();
                    if (uid != 0UL)
                    {
                        player = mgr.GetEntity(uid);
                    }
                }
            }

            *(int*)(b + 0x10) = (player != null && cid != 0) ? 1 : 0;
            *(int*)(b + 0x14) = cid;

            if (cid != 0 && cid != _lastCid)
            {
                _lastCid = cid;
                Debug.Log("[ModBridge] Scene player ready cid=" + cid.ToString());
            }

            // Dynamic config auto-export once data loaders are ready
            if (!_dynExportDone && (_tick % 30u) == 5u)
            {
                TryExportDynConfigs(b, false);
            }
            if (_iconQueue != null && _iconQueuePos < _iconQueueLen)
            {
                ProcessIconQueue(b);
            }

            // Instant Interact DGameConfig sync (bit 4 = 16u)
            MaintainInstantInteract((flags & 16u) != 0);

            // Paid Unlock sync (bit 3 = 8u)
            if (gs != null)
            {
                bool wantPaid = (flags & 8u) != 0;
                if (wantPaid && (!_paidApplied || (_tick % 120u) == 0u || cid != _lastCid))
                {
                    ApplyPaidUnlock(gs, cid, player);
                }
                else if (!wantPaid && _paidApplied)
                {
                    RestorePaidUnlock(gs);
                }
            }

            // Process queued commands (up to 64 per frame so batch operations drain quickly without blocking UI)
            uint rIdx = *(uint*)(b + 0x24);
            uint wIdx = *(uint*)(b + 0x20);
            int processed = 0;
            while (rIdx != wIdx && processed < 64)
            {
                uint slot = rIdx & 31u;
                byte* cmd = b + 0x40 + slot * 32u;
                int ctype = *(int*)(cmd + 0x00);
                int arg1 = *(int*)(cmd + 0x04);
                int arg2 = *(int*)(cmd + 0x08);
                int arg3 = *(int*)(cmd + 0x0C);
                long argL = *(long*)(cmd + 0x10);
                int res = ExecuteCmd(ctype, arg1, arg2, arg3, argL, gs, cid, player);
                *(int*)(cmd + 0x18) = res;
                if (res > 0)
                {
                    *(uint*)(b + 0x2C) = *(uint*)(b + 0x2C) + 1u;
                }
                else
                {
                    *(uint*)(b + 0x30) = *(uint*)(b + 0x30) + 1u;
                    *(int*)(b + 0x34) = arg1;
                }
                rIdx++;
                *(uint*)(b + 0x24) = rIdx;
                processed++;
            }
            FlushDirtyContainers(cid);

            if (player != null && cid != 0)
            {
                // Maintain locked attributes
                EnforcePropLocks(player);
                // Maintain custom buff durations
                MaintainBuffs(player);
                // Maintain Noclip solid colliders (bit 6 = 64u)
                MaintainNoclip(player, (flags & 64u) != 0);
                // Maintain Machine Gun rapid fire (bit 7 = 128u)
                if ((flags & 128u) != 0)
                {
                    MaintainMachineGun(player, mgRateX10);
                }
                // Auto-restore durability when LockEndurance turns ON or periodically while ON
                if ((flags & 4u) != 0 && ((_prevFlags & 4u) == 0 || (_tick % 60u) == 0u))
                {
                    CmdRestoreEndurance(gs, cid);
                }
                // Merge existing split items when Infinite Stack turns ON or upon entering a save with it ON
                if ((flags & 32u) != 0)
                {
                    if ((_prevFlags & 32u) == 0 || _lastInfStackCid != cid)
                    {
                        _lastInfStackCid = cid;
                        MergeInfStackContainers(gs, cid);
                    }
                }
                else
                {
                    _lastInfStackCid = 0;
                }
                // Periodic snapshot (every 12 frames ~ 0.2-0.4s)
                if (processed > 0 || (_tick % 12u) == 1u)
                {
                    UpdateSnapshot(player);
                }
            }
            else if (_noclipActive)
            {
                MaintainNoclip(null, false);
            }
            _prevFlags = flags;

            if (_tick == 1u || (_tick % 300u) == 0u)
            {
                Debug.Log("[ModBridge] Heartbeat tick=" + _tick.ToString() + " cid=" + cid.ToString() + " flags=" + flags.ToString());
            }
        }

        private unsafe int ExecuteCmd(int ctype, int arg1, int arg2, int arg3, long argL, IGameSystem gs, int cid, CEntity player)
        {
            try
            {
                if (ctype == 10) // MOD_CMD_SYNC_DYN_ASSETS (runs even on login screen)
                {
                    return TryExportDynConfigs(Shm(), true) ? 1 : -1;
                }
                if (gs == null || cid == 0)
                {
                    SetStatus(-1, "Not in game scene yet");
                    return -1;
                }
                switch (ctype)
                {
                    case 1: // MOD_CMD_ADD_ITEM (arg1=id, arg2=num, arg3=0 Bag / 1 HomeBox, argL=boxInstanceId)
                        return CmdAddItem(arg1, arg2, arg3, argL, gs, cid);
                    case 2: // MOD_CMD_SET_PROP (arg1=key, arg2=rawVal, arg3=lock -1/0/1)
                        return CmdSetProp(player, arg1, arg2, arg3);
                    case 3: // MOD_CMD_UNLOCK_ALL_BP
                        return CmdUnlockAllBP(gs, cid);
                    case 4: // MOD_CMD_APPLY_BUFF (arg1=buffId, arg2=durationSec)
                        return CmdApplyBuff(player, arg1, arg2);
                    case 5: // MOD_CMD_REMOVE_BUFF (arg1=buffId, 0=all)
                        return CmdRemoveBuff(player, arg1);
                    case 6: // MOD_CMD_RESTORE_ENDURANCE
                        return CmdRestoreEndurance(gs, cid);
                    case 7: // MOD_CMD_REFRESH_SNAP
                        if (player != null) UpdateSnapshot(player);
                        SetStatus(1, "Snapshot refreshed");
                        return 1;
                    case 8: // MOD_CMD_CLEAR_PROP_LOCKS
                        _lockCount = 0;
                        if (player != null) UpdateSnapshot(player);
                        SetStatus(1, "Property locks cleared");
                        return 1;
                    case 9: // MOD_CMD_UNLOCK_ALL_PAID (arg1=1 apply, 0 restore)
                        if (arg1 != 0) return ApplyPaidUnlock(gs, cid, player);
                        return RestorePaidUnlock(gs);
                }
            }
            catch (Exception ex)
            {
                SetStatus(-99, "Ex: " + ex.Message);
                Debug.LogError("[ModBridge] Cmd ex: " + ex.ToString());
                return -99;
            }
            return 0;
        }

        private static bool IsStorageBoxHomeItem(int nHomeItemId)
        {
            return nHomeItemId == 120085 || nHomeItemId == 120100 || nHomeItemId == 120215;
        }

        private long ResolveDefaultBoxId(IGameSystem gs)
        {
            CHomeStorageBox[] boxes = UnityEngine.Object.FindObjectsOfType<CHomeStorageBox>();
            if (boxes != null)
            {
                for (int i = 0; i < boxes.Length; i++)
                {
                    if (boxes[i] != null && !boxes[i].RuntimeIsPreview && boxes[i].RuntimeInstanceId > 0)
                        return boxes[i].RuntimeInstanceId;
                }
            }
            CHomeItemSystem his = gs.homeItemSystem as CHomeItemSystem;
            if (his != null && his.m_dicData != null)
            {
                foreach (Dictionary<long, HomeItemPlacementData> homeMap in his.m_dicData.Values)
                {
                    if (homeMap == null) continue;
                    foreach (HomeItemPlacementData p in homeMap.Values)
                    {
                        if (p != null && p.lInstanceId > 0 && IsStorageBoxHomeItem(p.nHomeItemId))
                            return p.lInstanceId;
                    }
                }
            }
            CHomeContainerSystem hcs = gs.homeContainerSystem as CHomeContainerSystem;
            if (hcs != null && hcs.m_dicContainer != null)
            {
                foreach (long k in hcs.m_dicContainer.Keys)
                {
                    if (k > 0) return k;
                }
            }
            return 0;
        }

        private void MergeInfStackContainers(IGameSystem gs, int cid)
        {
            if (gs == null || cid == 0) return;
            try
            {
                if (gs.containerSystem != null)
                {
                    gs.containerSystem.RefreshContainer(cid, eContainerTypeEnum.Bag);
                    gs.containerSystem.RefreshContainer(cid, eContainerTypeEnum.Drop);
                    gs.containerSystem.RefreshContainer(cid, eContainerTypeEnum.Sale);
                }
                // Also merge duplicates already sitting inside any Home Storage Box container
                CHomeContainerSystem hcs = gs.homeContainerSystem as CHomeContainerSystem;
                if (hcs != null && hcs.m_dicContainer != null && gs.materialSystem != null)
                {
                    foreach (KeyValuePair<long, CHomeContainer> kv in hcs.m_dicContainer)
                    {
                        CHomeContainer hc = kv.Value;
                        if (hc == null) continue;
                        int cnt = hc.GetSlotCount();
                        if (cnt <= 1) continue;
                        int[] seenIds = new int[cnt];
                        CMaterial[] seenMats = new CMaterial[cnt];
                        int seenCount = 0;
                        ulong[] toRemove = new ulong[cnt];
                        int remCount = 0;
                        for (int i = 0; i < cnt; i++)
                        {
                            ulong uid = hc.GetMaterialUID(i);
                            CMaterial m = gs.materialSystem.GetMaterialByUID(uid);
                            if (m == null || m.nNumber <= 0) continue;
                            int match = -1;
                            for (int j = 0; j < seenCount; j++)
                            {
                                if (seenIds[j] == m.nConfigID) { match = j; break; }
                            }
                            if (match >= 0)
                            {
                                seenMats[match].SetNum(seenMats[match].nNumber + m.nNumber, false);
                                toRemove[remCount++] = uid;
                            }
                            else
                            {
                                seenIds[seenCount] = m.nConfigID;
                                seenMats[seenCount] = m;
                                seenCount++;
                            }
                        }
                        for (int i = 0; i < remCount; i++)
                        {
                            hc.RemoveMaterial(toRemove[i]);
                            gs.materialSystem.DestroyMaterial(toRemove[i], true);
                        }
                    }
                }
                if (CClientGlobal.Instance != null && CClientGlobal.Instance.GetEventSystem != null)
                {
                    eMSG.sContainer_Change ev = default(eMSG.sContainer_Change);
                    ev.nCustomID = cid;
                    ev.eContainer = eContainerTypeEnum.Bag;
                    ev.nMaterialConfigID = 0;
                    CClientGlobal.Instance.GetEventSystem.FireExecoute(eMSG.EVENT.Container_Change, 0, ev);
                    eMSG.sEquipment_Change eev = default(eMSG.sEquipment_Change);
                    eev.nCustomID = cid;
                    CClientGlobal.Instance.GetEventSystem.FireExecoute(eMSG.EVENT.Equipment_Change, 0, eev);
                }
                SetStatus(1, "Merged containers into infinite stacks");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModBridge] MergeInfStack ex: " + ex.Message);
            }
        }

        private static eContainerTypeEnum ResolveBagDestType(sMaterialConfig.eMaterialType mtype)
        {
            switch (mtype)
            {
                case sMaterialConfig.eMaterialType.Blueprint: return eContainerTypeEnum.Blueprint;
                case sMaterialConfig.eMaterialType.Map: return eContainerTypeEnum.Map;
                case sMaterialConfig.eMaterialType.EXP: return eContainerTypeEnum.EXP;
                case sMaterialConfig.eMaterialType.ZB: return eContainerTypeEnum.Wallet;
                default: return eContainerTypeEnum.Bag;
            }
        }

        private void FlushDirtyContainers(int cid)
        {
            if (cid == 0)
            {
                _dirtyBagMask = 0u;
                _dirtyHomeBox = false;
                return;
            }
            if (_dirtyBagMask != 0u && _dirtyContainerSys != null)
            {
                uint mask = _dirtyBagMask;
                CContainerSystem cs = _dirtyContainerSys;
                int lastId = _dirtyLastItemId;
                _dirtyBagMask = 0u;
                for (int bit = 0; bit < 16; bit++)
                {
                    if ((mask & (1u << bit)) == 0u) continue;
                    eContainerTypeEnum et = (eContainerTypeEnum)bit;
                    try { cs.RefreshContainer(cid, et); } catch { }
                    try
                    {
                        if (CClientGlobal.Instance != null && CClientGlobal.Instance.GetEventSystem != null)
                        {
                            eMSG.sContainer_Change ev = default(eMSG.sContainer_Change);
                            ev.nCustomID = cid;
                            ev.eContainer = et;
                            ev.nMaterialConfigID = lastId;
                            CClientGlobal.Instance.GetEventSystem.FireExecoute(eMSG.EVENT.Container_Change, 0, ev);
                        }
                    }
                    catch { }
                }
            }
            if (_dirtyHomeBox)
            {
                int lastId = _dirtyLastItemId;
                _dirtyHomeBox = false;
                try
                {
                    if (CClientGlobal.Instance != null && CClientGlobal.Instance.GetEventSystem != null)
                    {
                        eMSG.sContainer_Change ev = default(eMSG.sContainer_Change);
                        ev.nCustomID = cid;
                        ev.eContainer = eContainerTypeEnum.Bag;
                        ev.nMaterialConfigID = lastId;
                        CClientGlobal.Instance.GetEventSystem.FireExecoute(eMSG.EVENT.Container_Change, 0, ev);
                    }
                }
                catch { }
            }
        }

        private int CmdAddItem(int itemId, int num, int target, long boxInstanceId, IGameSystem gs, int cid)
        {
            if (num <= 0) num = 1;
            if (gs == null || gs.materialSystem == null || CClientGlobal.Instance == null || CClientGlobal.Instance.GetGameDataCenter == null)
            {
                SetStatus(-2, "GameSystem not ready");
                return -2;
            }
            CMaterialConfigLoader loader = CClientGlobal.Instance.GetGameDataCenter.GetConfigLoader<CMaterialConfigLoader>(eGameConfigType.Begin);
            sMaterialConfig cfg = (loader != null) ? loader.GetConfig(itemId) : null;
            if (cfg == null)
            {
                SetStatus(-5, "Unknown item id: " + itemId.ToString());
                return -5;
            }

            bool allowMerge = (!cfg.bCannotCombine && cfg.nEndurance == 0) || ((EditorPrefsX.endianDiff1 & 32) != 0);

            if (target == 1)
            {
                IHomeContainerSystem hcs = gs.homeContainerSystem;
                if (hcs == null)
                {
                    SetStatus(-2, "HomeContainerSystem null");
                    return -2;
                }
                long chosenId = (boxInstanceId > 0) ? boxInstanceId : ResolveDefaultBoxId(gs);
                if (chosenId <= 0)
                {
                    SetStatus(-3, "No placed Home StorageBox found");
                    return -3;
                }
                CHomeContainer hc = hcs.TryGetContainer(chosenId);
                if (hc == null)
                {
                    CHomeContainerSystem hcsImpl = hcs as CHomeContainerSystem;
                    if (hcsImpl != null && hcsImpl.m_dicContainer != null)
                    {
                        hc = new CHomeContainer(chosenId);
                        hcsImpl.m_dicContainer[chosenId] = hc;
                    }
                }
                if (hc == null)
                {
                    SetStatus(-4, "Failed to get/create HomeContainer");
                    return -4;
                }

                if (allowMerge)
                {
                    CMaterial merge = null;
                    int slotCnt = hc.GetSlotCount();
                    for (int i = 0; i < slotCnt; i++)
                    {
                        ulong uid = hc.GetMaterialUID(i);
                        CMaterial cand = gs.materialSystem.GetMaterialByUID(uid);
                        if (cand != null && cand.pConfig != null && cand.nConfigID == itemId)
                        {
                            merge = cand;
                            break;
                        }
                    }
                    if (merge != null)
                    {
                        long tot = (long)merge.nNumber + (long)num;
                        merge.SetNum((tot > int.MaxValue) ? int.MaxValue : (int)tot, false);
                    }
                    else
                    {
                        CMaterial mat = gs.materialSystem.CreateMaterial(itemId, num);
                        if (mat == null) { SetStatus(-5, "CreateMaterial failed: " + itemId.ToString()); return -5; }
                        hc.AddMaterial(mat.UID);
                    }
                }
                else
                {
                    int cnt = Mathf.Clamp(num, 1, 200);
                    int created = 0;
                    for (int i = 0; i < cnt; i++)
                    {
                        CMaterial mat = gs.materialSystem.CreateMaterial(itemId, 1);
                        if (mat != null)
                        {
                            hc.AddMaterial(mat.UID);
                            created++;
                        }
                    }
                    if (created == 0) { SetStatus(-5, "CreateMaterial failed: " + itemId.ToString()); return -5; }
                }

                _dirtyHomeBox = true;
                _dirtyLastItemId = itemId;
                SetStatus(1, "Added to Box #" + chosenId.ToString() + ": " + itemId.ToString() + " x" + num.ToString());
                return 1;
            }

            // Bag / Blueprint / Map / EXP / Wallet: insert directly to avoid blocking modal tips and per-item O(N^2) refreshes
            eContainerTypeEnum destType = ResolveBagDestType(cfg.eType);
            CContainerSystem cs = gs.containerSystem as CContainerSystem;
            if (cs != null && cs.m_dicData != null)
            {
                sContainerSystemData sdata = null;
                if (!cs.m_dicData.TryGetValue(cid, out sdata) || sdata == null)
                {
                    sdata = new sContainerSystemData();
                    sdata.nCustomID = cid;
                    cs.m_dicData[cid] = sdata;
                }
                CContainer cont = null;
                if (!sdata.dicContainer.TryGetValue(destType, out cont) || cont == null)
                {
                    cont = CContainerFactory.Create(destType, cid);
                    if (cont == null) { SetStatus(-4, "Failed to create container: " + destType.ToString()); return -4; }
                    sdata.dicContainer[destType] = cont;
                }

                if (allowMerge)
                {
                    CMaterial merge = null;
                    List<ulong> uids = cont.GetMaterialList();
                    if (uids != null)
                    {
                        for (int i = 0; i < uids.Count; i++)
                        {
                            CMaterial cand = gs.materialSystem.GetMaterialByUID(uids[i]);
                            if (cand != null && cand.nConfigID == itemId)
                            {
                                merge = cand;
                                break;
                            }
                        }
                    }
                    if (merge != null)
                    {
                        long tot = (long)merge.nNumber + (long)num;
                        merge.SetNum((tot > int.MaxValue) ? int.MaxValue : (int)tot, false);
                    }
                    else
                    {
                        CMaterial mat = gs.materialSystem.CreateMaterial(itemId, num);
                        if (mat == null) { SetStatus(-5, "CreateMaterial failed: " + itemId.ToString()); return -5; }
                        cont.AddMaterial(mat.UID, false);
                    }
                }
                else
                {
                    int cnt = Mathf.Clamp(num, 1, 200);
                    int created = 0;
                    for (int i = 0; i < cnt; i++)
                    {
                        CMaterial mat = gs.materialSystem.CreateMaterial(itemId, 1);
                        if (mat != null)
                        {
                            cont.AddMaterial(mat.UID, false);
                            created++;
                        }
                    }
                    if (created == 0) { SetStatus(-5, "CreateMaterial failed: " + itemId.ToString()); return -5; }
                }

                _dirtyContainerSys = cs;
                _dirtyBagMask |= (1u << ((int)destType & 15));
                _dirtyLastItemId = itemId;
                SetStatus(1, "Added to Bag: " + itemId.ToString() + " x" + num.ToString());
                return 1;
            }

            if (gs.containerSystem == null)
            {
                SetStatus(-2, "ContainerSystem null");
                return -2;
            }
            int safeNum = allowMerge ? num : Mathf.Clamp(num, 1, 200);
            gs.containerSystem.AddMaterialToContainer(cid, eContainerTypeEnum.Bag, itemId, safeNum, false);
            SetStatus(1, "Added to Bag: " + itemId.ToString() + " x" + safeNum.ToString());
            return 1;
        }

        private void SetPropLockState(int key, int val, int lockOp)
        {
            int idx = -1;
            for (int i = 0; i < _lockCount; i++)
            {
                if (_lockKeys[i] == key) { idx = i; break; }
            }
            if (lockOp == 0)
            {
                if (idx >= 0)
                {
                    _lockCount--;
                    _lockKeys[idx] = _lockKeys[_lockCount];
                    _lockVals[idx] = _lockVals[_lockCount];
                }
            }
            else if (lockOp == 1)
            {
                if (idx < 0 && _lockCount < 64)
                {
                    idx = _lockCount++;
                    _lockKeys[idx] = key;
                }
                if (idx >= 0) _lockVals[idx] = val;
            }
            else if (idx >= 0)
            {
                _lockVals[idx] = val;
            }
        }

        private void SetPropRaw(CPropertyPart pp, int key, int targetVal)
        {
            if (key == 600)
            {
                if (!pp.IsExistValue(key))
                    pp.InsertValue(ePropValueType.Percent, key, targetVal, false, false);
                else
                    pp.SetValue(ePropValueType.Percent, key, targetVal);
                return;
            }
            int gain = pp.GetValue(ePropValueType.Gain, key, 0);
            int pct = pp.GetValue(ePropValueType.Percent, key, 0);
            int desiredOrig;
            float scale = 1f + (float)pct / 100f;
            if (scale > 0.01f)
                desiredOrig = (int)Mathf.Round((float)targetVal / scale) - gain;
            else
                desiredOrig = targetVal - gain;

            if (!pp.IsExistValue(key))
                pp.InsertValue(ePropValueType.Original, key, desiredOrig, false, false);
            else
                pp.SetValue(ePropValueType.Original, key, desiredOrig);
        }

        private void EnsureMaxAtLeast(CPropertyPart pp, int maxKey, int needed)
        {
            if (pp.GetValue(ePropValueType.AfterCul, maxKey, 0) < needed)
            {
                SetPropRaw(pp, maxKey, needed);
            }
        }

        private void ApplySingleProp(CEntity player, int key, int targetVal)
        {
            if (player == null || player.propertyPart == null) return;
            CPropertyPart pp = player.propertyPart;

            // Lift global move speed clamp if editing any speed attribute (101..108)
            if (key >= 101 && key <= 108 && DGameConfig.Instance != null)
            {
                if (DGameConfig.Instance.nMaxMoveSpeed < targetVal)
                    DGameConfig.Instance.nMaxMoveSpeed = targetVal + 100000;
                if (DGameConfig.Instance.nMinMoveSpeed > targetVal && targetVal >= 0)
                    DGameConfig.Instance.nMinMoveSpeed = 0;
            }

            CFightObj fo = player.fightObj as CFightObj;
            if (key == 2 && fo != null)
            {
                EnsureMaxAtLeast(pp, 1, targetVal);
                SetPropRaw(pp, 2, targetVal);
                fo.SysncHP();
                return;
            }
            if (key == 303 && fo != null)
            {
                EnsureMaxAtLeast(pp, 302, targetVal);
                SetPropRaw(pp, 303, targetVal);
                fo.SetShield(targetVal);
                return;
            }
            if (key == 305 && fo != null)
            {
                EnsureMaxAtLeast(pp, 304, targetVal);
                fo.SetCurBlockCount(targetVal);
                return;
            }
            if (key == 402) EnsureMaxAtLeast(pp, 403, targetVal);
            else if (key == 411) EnsureMaxAtLeast(pp, 412, targetVal);
            else if (key == 421) EnsureMaxAtLeast(pp, 422, targetVal);

            SetPropRaw(pp, key, targetVal);
            if (key == 1 && fo != null) fo.SysncHP();
            else if (key == 302 && fo != null) fo.SysncShield();
            else if (key == 304 && fo != null) fo.SysncBlockCount();
        }

        private int CmdSetProp(CEntity player, int key, int rawVal, int lockOp)
        {
            if (player == null || player.propertyPart == null)
            {
                SetStatus(-2, "Player entity null");
                return -2;
            }
            ApplySingleProp(player, key, rawVal);
            SetPropLockState(key, rawVal, lockOp);
            UpdateSnapshot(player);
            SetStatus(1, "Prop " + key.ToString() + "=" + rawVal.ToString() + (lockOp == 1 ? " [LOCK]" : ""));
            return 1;
        }

        private static int ReadPropCurRaw(CPropertyPart pp, int key)
        {
            if (key == 600) return pp.GetValue(ePropValueType.Percent, key, 0);
            int def = (key == 601) ? 1000 : ((key == 1 || key == 52) ? 1 : 0);
            return pp.GetValue(ePropValueType.AfterCul, key, def);
        }

        private void EnforcePropLocks(CEntity player)
        {
            for (int i = 0; i < _lockCount; i++)
            {
                int k = _lockKeys[i];
                int v = _lockVals[i];
                if (player.propertyPart != null && ReadPropCurRaw(player.propertyPart, k) != v)
                {
                    ApplySingleProp(player, k, v);
                }
            }
        }

        private int CmdUnlockAllBP(IGameSystem gs, int cid)
        {
            CMaterialConfigLoader loader = CClientGlobal.Instance.GetGameDataCenter.GetConfigLoader<CMaterialConfigLoader>(eGameConfigType.Begin);
            if (loader == null)
            {
                SetStatus(-2, "MaterialConfigLoader null");
                return -2;
            }
            List<sMaterialConfig> all = loader.GetAllConfig();
            int added = 0;

            CContainerSystem cs = gs.containerSystem as CContainerSystem;
            CContainer bpCont = null;
            if (cs != null && cs.m_dicData != null)
            {
                sContainerSystemData sdata = null;
                if (!cs.m_dicData.TryGetValue(cid, out sdata) || sdata == null)
                {
                    sdata = new sContainerSystemData();
                    sdata.nCustomID = cid;
                    cs.m_dicData[cid] = sdata;
                }
                if (!sdata.dicContainer.TryGetValue(eContainerTypeEnum.Blueprint, out bpCont) || bpCont == null)
                {
                    bpCont = CContainerFactory.Create(eContainerTypeEnum.Blueprint, cid);
                    if (bpCont != null) sdata.dicContainer[eContainerTypeEnum.Blueprint] = bpCont;
                }
            }

            if (bpCont != null)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    sMaterialConfig cfg = all[i];
                    if (cfg != null && cfg.eType == sMaterialConfig.eMaterialType.Blueprint)
                    {
                        if (bpCont.GetMaterialNumber(cfg.nID) <= 0)
                        {
                            CMaterial mat = gs.materialSystem.CreateMaterial(cfg.nID, 1);
                            if (mat != null)
                            {
                                bpCont.AddMaterial(mat.UID, false);
                                added++;
                            }
                        }
                    }
                }
                if (added > 0)
                {
                    bpCont.RefreshContainer();
                    eMSG.sContainer_Change ev = default(eMSG.sContainer_Change);
                    ev.nCustomID = cid;
                    ev.eContainer = eContainerTypeEnum.Blueprint;
                    ev.nMaterialConfigID = 0;
                    CClientGlobal.Instance.GetEventSystem.FireExecoute(eMSG.EVENT.Container_Change, 0, ev);
                }
            }
            else
            {
                for (int i = 0; i < all.Count; i++)
                {
                    sMaterialConfig cfg = all[i];
                    if (cfg != null && cfg.eType == sMaterialConfig.eMaterialType.Blueprint)
                    {
                        if (!gs.containerSystem.IsOwnBlueprint(cid, cfg.nID))
                        {
                            gs.containerSystem.AddMaterialToContainer(cid, eContainerTypeEnum.Bag, cfg.nID, 1, false);
                            added++;
                        }
                    }
                }
            }

            if (gs.tipsSystem != null)
                gs.tipsSystem.ShowFlowTips(eTipsUrgentType.Normal, "已一键解锁全部蓝图 (新增 " + added.ToString() + ")");
            SetStatus(1, "Blueprints unlocked: +" + added.ToString());
            return 1;
        }

        private int CmdApplyBuff(CEntity player, int buffId, int durationSec)
        {
            if (player == null || player.buffPart == null)
            {
                SetStatus(-2, "Player buffPart null");
                return -2;
            }
            // Add buff via native CBuffPart.AddBuff(int, CEntity)
            bool ok = player.buffPart.AddBuff(buffId, player);
            CBuff cb = player.buffPart.GetBuff(buffId);
            if (cb != null)
            {
                cb.m_nCurCycle = -999999;
                float exp = (durationSec > 0) ? (Time.realtimeSinceStartup + (float)durationSec) : -1f;
                int idx = -1;
                for (int i = 0; i < _buffCount; i++)
                {
                    if (_buffIds[i] == buffId) { idx = i; break; }
                }
                if (idx < 0 && _buffCount < 64) idx = _buffCount++;
                if (idx >= 0)
                {
                    _buffIds[idx] = buffId;
                    _buffExpireAt[idx] = exp;
                }
            }
            SetStatus(ok ? 1 : 0, "Buff " + buffId.ToString() + (durationSec > 0 ? " (" + durationSec.ToString() + "s)" : " (Perm)"));
            return 1;
        }

        private int CmdRemoveBuff(CEntity player, int buffId)
        {
            if (player == null || player.buffPart == null) return -2;
            if (buffId == 0)
            {
                for (int i = 0; i < _buffCount; i++)
                {
                    player.buffPart.ReqRemoveBuff(_buffIds[i]);
                }
                _buffCount = 0;
                SetStatus(1, "Removed mod buffs");
                return 1;
            }
            player.buffPart.ReqRemoveBuff(buffId);
            for (int i = 0; i < _buffCount; i++)
            {
                if (_buffIds[i] == buffId)
                {
                    _buffCount--;
                    _buffIds[i] = _buffIds[_buffCount];
                    _buffExpireAt[i] = _buffExpireAt[_buffCount];
                    break;
                }
            }
            SetStatus(1, "Removed buff " + buffId.ToString());
            return 1;
        }

        private void MaintainBuffs(CEntity player)
        {
            if (player.buffPart == null || _buffCount == 0) return;
            float now = Time.realtimeSinceStartup;
            for (int i = _buffCount - 1; i >= 0; i--)
            {
                int bid = _buffIds[i];
                float exp = _buffExpireAt[i];
                CBuff cb = player.buffPart.GetBuff(bid);
                if (cb == null)
                {
                    _buffCount--;
                    _buffIds[i] = _buffIds[_buffCount];
                    _buffExpireAt[i] = _buffExpireAt[_buffCount];
                    continue;
                }
                if (exp > 0f && now >= exp)
                {
                    player.buffPart.ReqRemoveBuff(bid);
                    _buffCount--;
                    _buffIds[i] = _buffIds[_buffCount];
                    _buffExpireAt[i] = _buffExpireAt[_buffCount];
                }
                else
                {
                    cb.m_nCurCycle = -999999;
                }
            }
        }

        private int CmdRestoreEndurance(IGameSystem gs, int cid)
        {
            int n = 0;
            eContainerTypeEnum[] containers = new eContainerTypeEnum[] { eContainerTypeEnum.Equipment, eContainerTypeEnum.Bag };
            for (int c = 0; c < containers.Length; c++)
            {
                List<ulong> list = gs.containerSystem.GetContainerMaterialList(cid, containers[c]);
                if (list == null) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    CMaterial m = gs.materialSystem.GetMaterialByUID(list[i]);
                    if (m != null && m.pConfig != null && m.pConfig.nEndurance > 0 && m.nEndurance < m.pConfig.nEndurance)
                    {
                        m.SetEndurance(m.pConfig.nEndurance, true);
                        n++;
                    }
                }
            }
            SetStatus(1, "Restored endurance x" + n.ToString());
            return 1;
        }

        private unsafe void UpdateSnapshot(CEntity player)
        {
            byte* b = Shm();
            if (b == null || player == null || player.propertyPart == null) return;

            CPropertyPart pp = player.propertyPart;
            int cnt = ALL_PROP_KEYS.Length;
            if (cnt > 64) cnt = 64;
            *(int*)(b + 0x444) = cnt;
            byte* pbase = b + 0x448;
            for (int i = 0; i < cnt; i++)
            {
                int k = ALL_PROP_KEYS[i];
                int cur = ReadPropCurRaw(pp, k);
                int orig = (k == 600) ? cur : pp.GetValue(ePropValueType.Original, k, 0);
                int lval = 0;
                byte lkd = 0;
                for (int j = 0; j < _lockCount; j++)
                {
                    if (_lockKeys[j] == k)
                    {
                        lkd = 1;
                        lval = _lockVals[j];
                        break;
                    }
                }
                byte* row = pbase + i * 20;
                *(int*)(row + 0x00) = k;
                *(int*)(row + 0x04) = cur;
                *(int*)(row + 0x08) = orig;
                *(int*)(row + 0x0C) = lval;
                *(byte*)(row + 0x10) = lkd;
            }

            // Home storage boxes (scene + placed HomeItemSystem + active HomeContainerSystem)
            int boxCnt = 0;
            try
            {
                IGameSystem gs = CGameSystem.Instance;
                IHomeContainerSystem hcs = (gs != null) ? gs.homeContainerSystem : null;
                byte* bbase = b + 0x950;
                long[] seenIds = new long[16];

                CHomeStorageBox[] boxes = UnityEngine.Object.FindObjectsOfType<CHomeStorageBox>();
                if (boxes != null)
                {
                    for (int i = 0; i < boxes.Length && boxCnt < 16; i++)
                    {
                        if (boxes[i] == null || boxes[i].RuntimeIsPreview) continue;
                        long iid = boxes[i].RuntimeInstanceId;
                        if (iid <= 0) continue;
                        int used = 0;
                        if (hcs != null)
                        {
                            CHomeContainer hc = hcs.TryGetContainer(iid);
                            if (hc != null) used = hc.GetSlotCount();
                        }
                        byte* brow = bbase + boxCnt * 16;
                        *(long*)(brow + 0x00) = iid;
                        *(int*)(brow + 0x08) = boxes[i].Capacity;
                        *(int*)(brow + 0x0C) = used;
                        seenIds[boxCnt] = iid;
                        boxCnt++;
                    }
                }

                CHomeItemSystem his = (gs != null) ? (gs.homeItemSystem as CHomeItemSystem) : null;
                if (his != null && his.m_dicData != null && boxCnt < 16)
                {
                    foreach (Dictionary<long, HomeItemPlacementData> homeMap in his.m_dicData.Values)
                    {
                        if (homeMap == null) continue;
                        foreach (HomeItemPlacementData p in homeMap.Values)
                        {
                            if (boxCnt >= 16) break;
                            if (p == null || p.lInstanceId <= 0 || !IsStorageBoxHomeItem(p.nHomeItemId)) continue;
                            bool dup = false;
                            for (int j = 0; j < boxCnt; j++) if (seenIds[j] == p.lInstanceId) { dup = true; break; }
                            if (dup) continue;
                            int used = 0;
                            if (hcs != null)
                            {
                                CHomeContainer hc = hcs.TryGetContainer(p.lInstanceId);
                                if (hc != null) used = hc.GetSlotCount();
                            }
                            byte* brow = bbase + boxCnt * 16;
                            *(long*)(brow + 0x00) = p.lInstanceId;
                            *(int*)(brow + 0x08) = 12;
                            *(int*)(brow + 0x0C) = used;
                            seenIds[boxCnt] = p.lInstanceId;
                            boxCnt++;
                        }
                    }
                }

                CHomeContainerSystem hcsImpl = hcs as CHomeContainerSystem;
                if (hcsImpl != null && hcsImpl.m_dicContainer != null && boxCnt < 16)
                {
                    foreach (KeyValuePair<long, CHomeContainer> kv in hcsImpl.m_dicContainer)
                    {
                        if (boxCnt >= 16) break;
                        long iid = kv.Key;
                        if (iid <= 0) continue;
                        bool dup = false;
                        for (int j = 0; j < boxCnt; j++) if (seenIds[j] == iid) { dup = true; break; }
                        if (dup) continue;
                        int used = (kv.Value != null) ? kv.Value.GetSlotCount() : 0;
                        byte* brow = bbase + boxCnt * 16;
                        *(long*)(brow + 0x00) = iid;
                        *(int*)(brow + 0x08) = 12;
                        *(int*)(brow + 0x0C) = used;
                        seenIds[boxCnt] = iid;
                        boxCnt++;
                    }
                }
            }
            catch { }
            *(int*)(b + 0x948) = boxCnt;

            // Active custom buffs snapshot
            int bc = _buffCount;
            if (bc > 64) bc = 64;
            *(int*)(b + 0xA50) = bc;
            float now = Time.realtimeSinceStartup;
            for (int i = 0; i < bc; i++)
            {
                byte* brow = b + 0xA58 + i * 8;
                *(int*)(brow + 0x00) = _buffIds[i];
                int rem = (_buffExpireAt[i] > 0f) ? Mathf.Max(0, (int)(_buffExpireAt[i] - now)) : -1;
                *(int*)(brow + 0x04) = rem;
            }

            uint seq = *(uint*)(b + 0x440);
            *(uint*)(b + 0x440) = seq + 1u;
        }

        // ==================== #10 Instant Interact ====================
        private void MaintainInstantInteract(bool on)
        {
            DGameConfig cfg = DGameConfig.Instance;
            if (cfg == null) return;
            if (on)
            {
                if (!_origBackhomeSaved)
                {
                    _origBackhomeTime = cfg.fInputBackhomeTime;
                    _origBackhomeSaved = true;
                }
                if (cfg.fInputBackhomeTime != 0f) cfg.fInputBackhomeTime = 0f;
            }
            else if (_origBackhomeSaved)
            {
                cfg.fInputBackhomeTime = _origBackhomeTime;
                _origBackhomeSaved = false;
            }
        }

        // ==================== #6 Local Paid Unlock ====================
        private static void AddUnique(List<int> list, int id)
        {
            if (id != 0 && !list.Contains(id)) list.Add(id);
        }

        private int ApplyPaidUnlock(IGameSystem gs, int cid, CEntity player)
        {
            if (gs == null) return -1;
            CPaySystem ps = gs.paySystem as CPaySystem;
            CDLCSystem dlc = gs.dLCSystem as CDLCSystem;
            if (ps == null) return -2;

            if (!_paidApplied)
            {
                _origPkg = (ps.m_listPackage != null) ? ps.m_listPackage.ToArray() : new int[0];
                _origDiamond = (ps.m_listDiamondPackage != null) ? ps.m_listDiamondPackage.ToArray() : new int[0];
                _origLimit = (ps.m_listLimitGift != null) ? ps.m_listLimitGift.ToArray() : new int[0];
                if (dlc != null && dlc.m_dicDLCInstallDictionary != null)
                {
                    int c = dlc.m_dicDLCInstallDictionary.Count;
                    _origDlcKeys = new int[c];
                    _origDlcVals = new int[c];
                    int k = 0;
                    foreach (KeyValuePair<int, int> kv in dlc.m_dicDLCInstallDictionary)
                    {
                        _origDlcKeys[k] = kv.Key;
                        _origDlcVals[k] = kv.Value;
                        k++;
                    }
                }
                _paidApplied = true;
            }

            if (ps.m_listPackage == null) ps.m_listPackage = new List<int>();
            if (ps.m_listDiamondPackage == null) ps.m_listDiamondPackage = new List<int>();
            if (ps.m_listLimitGift == null) ps.m_listLimitGift = new List<int>();

            int beforeCount = ps.m_listPackage.Count + ps.m_listLimitGift.Count + ((dlc != null && dlc.m_dicDLCInstallDictionary != null) ? dlc.m_dicDLCInstallDictionary.Count : 0);

            // Baseline IDs: Shop 11..35, Bundles 101..115, Story DLCs 201..220, Holiday/Bundles 200..310
            for (int id = 1; id <= 40; id++)
            {
                AddUnique(ps.m_listPackage, id);
                AddUnique(ps.m_listDiamondPackage, id);
                AddUnique(ps.m_listLimitGift, id);
            }
            for (int id = 100; id <= 115; id++)
            {
                AddUnique(ps.m_listPackage, id);
                AddUnique(ps.m_listLimitGift, id);
            }
            for (int id = 200; id <= 310; id++)
            {
                AddUnique(ps.m_listPackage, id);
                AddUnique(ps.m_listLimitGift, id);
                if (dlc != null && dlc.m_dicDLCInstallDictionary != null) dlc.m_dicDLCInstallDictionary[id] = 1;
            }

            // Runtime config loaders for any future updates
            try
            {
                IGameDataCenter dc = (CClientGlobal.Instance != null) ? CClientGlobal.Instance.GetGameDataCenter : null;
                if (dc != null)
                {
                    CPayShopConfigLoader pShop = dc.GetConfigLoader(eGameConfigType.PayShop) as CPayShopConfigLoader;
                    if (pShop != null)
                    {
                        List<sPayShopConfig> l = pShop.GetAllConfig();
                        if (l != null)
                        {
                            for (int i = 0; i < l.Count; i++)
                            {
                                if (l[i] == null) continue;
                                AddUnique(ps.m_listPackage, l[i].nId);
                                if (l[i].listBindID != null)
                                    for (int j = 0; j < l[i].listBindID.Count; j++) AddUnique(ps.m_listPackage, l[i].listBindID[j]);
                            }
                        }
                    }
                    CPayLimitGiftConfigLoader pGift = dc.GetConfigLoader(eGameConfigType.PayLimitGift) as CPayLimitGiftConfigLoader;
                    if (pGift != null)
                    {
                        List<sPayLimitGiftConfig> l = pGift.GetAllConfig();
                        if (l != null)
                        {
                            for (int i = 0; i < l.Count; i++)
                            {
                                if (l[i] == null) continue;
                                AddUnique(ps.m_listLimitGift, l[i].nId);
                                AddUnique(ps.m_listPackage, l[i].nId);
                                if (l[i].listBindID != null)
                                    for (int j = 0; j < l[i].listBindID.Count; j++)
                                    {
                                        AddUnique(ps.m_listPackage, l[i].listBindID[j]);
                                        AddUnique(ps.m_listLimitGift, l[i].listBindID[j]);
                                    }
                            }
                        }
                    }
                    CPayDiamondConfigLoader pDia = dc.GetConfigLoader(eGameConfigType.PayDiamond) as CPayDiamondConfigLoader;
                    if (pDia != null)
                    {
                        List<sPayDiamondConfig> l = pDia.GetAllConfig();
                        if (l != null)
                            for (int i = 0; i < l.Count; i++)
                                if (l[i] != null) { AddUnique(ps.m_listDiamondPackage, l[i].nId); AddUnique(ps.m_listPackage, l[i].nId); }
                    }
                    CPayDLCConfigLoader pDlc = dc.GetConfigLoader(eGameConfigType.PayDLC) as CPayDLCConfigLoader;
                    if (pDlc != null)
                    {
                        List<sPayDLCConfig> l = pDlc.GetAllConfig();
                        if (l != null)
                            for (int i = 0; i < l.Count; i++)
                                if (l[i] != null)
                                {
                                    AddUnique(ps.m_listPackage, l[i].nId);
                                    if (dlc != null && dlc.m_dicDLCInstallDictionary != null) dlc.m_dicDLCInstallDictionary[l[i].nId] = 1;
                                }
                    }
                    CSceneConfigLoader pScene = dc.GetConfigLoader(eGameConfigType.Scene) as CSceneConfigLoader;
                    CSceneSystem ss = gs.sceneSystem as CSceneSystem;
                    if (pScene != null)
                    {
                        List<sSceneConfig> l = pScene.GetAllConfig();
                        if (l != null)
                        {
                            for (int i = 0; i < l.Count; i++)
                            {
                                if (l[i] != null && l[i].nDependentShop > 0)
                                {
                                    AddUnique(ps.m_listPackage, l[i].nDependentShop);
                                    if (ss != null)
                                    {
                                        CScene sc = ss.GetScene(l[i].nID);
                                        if (sc != null) sc.SetOwn(true);
                                    }
                                }
                            }
                        }
                    }
                    CHolidayImportantExchangeConfigLoader pHol = dc.GetConfigLoader(eGameConfigType.HolidayImportantExchange) as CHolidayImportantExchangeConfigLoader;
                    if (pHol != null && pHol.m_dicConfig != null)
                    {
                        foreach (int hk in pHol.m_dicConfig.Keys)
                        {
                            AddUnique(ps.m_listPackage, hk);
                        }
                    }
                }
            }
            catch { }

            // Immediately apply paid talent upgrades (14 = HP +30% -> 1400, 15 = Energy +30% -> 1500)
            try
            {
                if (cid != 0 && gs.talentSystem != null)
                {
                    gs.talentSystem.UpgradeToLevel(cid, 1400, 1);
                    gs.talentSystem.UpgradeToLevel(cid, 1500, 1);
                }
            }
            catch { }

            int afterCount = ps.m_listPackage.Count + ps.m_listLimitGift.Count + ((dlc != null && dlc.m_dicDLCInstallDictionary != null) ? dlc.m_dicDLCInstallDictionary.Count : 0);
            if (afterCount != beforeCount)
            {
                try
                {
                    if (CClientGlobal.Instance != null && CClientGlobal.Instance.GetEventSystem != null)
                    {
                        CClientGlobal.Instance.GetEventSystem.FireExecoute(eMSG.EVENT.Pay_Set_PackageList, 0, null);
                        CClientGlobal.Instance.GetEventSystem.FireExecoute(eMSG.EVENT.Pay_Set_DiamondPackageList, 0, null);
                        CClientGlobal.Instance.GetEventSystem.FireExecoute(eMSG.EVENT.pLayer_UpdateLimitGift, 0, null);
                        CClientGlobal.Instance.GetEventSystem.FireExecoute(eMSG.EVENT.Player_UpdateDLC, 0, null);
                    }
                }
                catch { }
            }

            SetStatus(1, "Paid unlock active (" + ps.m_listPackage.Count.ToString() + " pkgs)");
            return 1;
        }

        private int RestorePaidUnlock(IGameSystem gs)
        {
            if (!_paidApplied || gs == null) return 0;
            CPaySystem ps = gs.paySystem as CPaySystem;
            CDLCSystem dlc = gs.dLCSystem as CDLCSystem;
            if (ps != null)
            {
                if (ps.m_listPackage != null && _origPkg != null)
                {
                    ps.m_listPackage.Clear();
                    for (int i = 0; i < _origPkg.Length; i++) ps.m_listPackage.Add(_origPkg[i]);
                }
                if (ps.m_listDiamondPackage != null && _origDiamond != null)
                {
                    ps.m_listDiamondPackage.Clear();
                    for (int i = 0; i < _origDiamond.Length; i++) ps.m_listDiamondPackage.Add(_origDiamond[i]);
                }
                if (ps.m_listLimitGift != null && _origLimit != null)
                {
                    ps.m_listLimitGift.Clear();
                    for (int i = 0; i < _origLimit.Length; i++) ps.m_listLimitGift.Add(_origLimit[i]);
                }
            }
            if (dlc != null && dlc.m_dicDLCInstallDictionary != null && _origDlcKeys != null)
            {
                dlc.m_dicDLCInstallDictionary.Clear();
                for (int i = 0; i < _origDlcKeys.Length; i++) dlc.m_dicDLCInstallDictionary[_origDlcKeys[i]] = _origDlcVals[i];
            }
            _paidApplied = false;
            try
            {
                if (CClientGlobal.Instance != null && CClientGlobal.Instance.GetEventSystem != null)
                {
                    CClientGlobal.Instance.GetEventSystem.FireExecoute(eMSG.EVENT.Pay_Set_PackageList, 0, null);
                    CClientGlobal.Instance.GetEventSystem.FireExecoute(eMSG.EVENT.Pay_Set_DiamondPackageList, 0, null);
                    CClientGlobal.Instance.GetEventSystem.FireExecoute(eMSG.EVENT.pLayer_UpdateLimitGift, 0, null);
                    CClientGlobal.Instance.GetEventSystem.FireExecoute(eMSG.EVENT.Player_UpdateDLC, 0, null);
                }
            }
            catch { }
            SetStatus(1, "Paid unlock restored");
            return 1;
        }

        // ==================== #12 Noclip (Collider2D.isTrigger) ====================
        private void MaintainNoclip(CEntity player, bool on)
        {
            if (!on || player == null)
            {
                if (_noclipActive && _noclipCols != null)
                {
                    for (int i = 0; i < _noclipCols.Length; i++)
                    {
                        if (_noclipCols[i] != null) _noclipCols[i].isTrigger = false;
                    }
                    _noclipCols = null;
                    _noclipPlayer = null;
                    _noclipActive = false;
                }
                return;
            }

            if (_noclipPlayer != player || !_noclipActive || (_tick % 20u) == 0u)
            {
                if (_noclipPlayer != player && _noclipCols != null)
                {
                    for (int i = 0; i < _noclipCols.Length; i++)
                    {
                        if (_noclipCols[i] != null) _noclipCols[i].isTrigger = false;
                    }
                    _noclipCols = null;
                }

                Component[] raw = player.gameObject.GetComponentsInChildren(typeof(Collider2D), true);
                if (raw != null && raw.Length > 0)
                {
                    List<Collider2D> toggled = (_noclipCols != null) ? new List<Collider2D>(_noclipCols) : new List<Collider2D>();
                    for (int i = 0; i < raw.Length; i++)
                    {
                        Collider2D c = raw[i] as Collider2D;
                        if (c == null) continue;
                        if (!c.isTrigger)
                        {
                            c.isTrigger = true;
                            if (!toggled.Contains(c)) toggled.Add(c);
                        }
                    }
                    _noclipCols = toggled.ToArray();
                }
                _noclipPlayer = player;
                _noclipActive = true;
            }
        }

        // ==================== #13 Machine Gun (Ranged Weapons Only) ====================
        private static bool IsRangedBehaviorId(int bid)
        {
            switch (bid)
            {
                case 4: case 9: case 14: case 15: case 18: case 19: case 20:
                case 25: case 26: case 27: case 28: case 29: case 30:
                    return true;
                default:
                    return false;
            }
        }

        private void MaintainMachineGun(CEntity player, uint mgRateX10)
        {
            CMainCharacter mc = player.GetComponent(typeof(CMainCharacter)) as CMainCharacter;
            if (mc == null || !mc.m_bInputAttack) return;
            CCharacterStateBase st = mc.m_pCurState;
            if (st == null || st.GetStateEnum() != 3) return;
            int bid = st.GetBehaviorID();
            if (!IsRangedBehaviorId(bid)) return;

            // Pause firing if player dragged attack joystick into cancel area
            try
            {
                UnityEngine.Object[] sticks = UnityEngine.Object.FindObjectsOfType(typeof(CUIJoystick));
                if (sticks != null)
                {
                    for (int i = 0; i < sticks.Length; i++)
                    {
                        CUIJoystick j = sticks[i] as CUIJoystick;
                        if (j != null && j.m_bPointerDown && j.m_bEnterCancel) return;
                    }
                }
            }
            catch { }

            float now = Time.realtimeSinceStartup;
            if (now < _nextMgFireTime) return;
            float interval = 10f / (float)mgRateX10;
            _nextMgFireTime = now + interval;

            CCharacterBehaviorBase beh = st.m_pBehavior;
            if (beh == null) return;
            try
            {
                FireRangedBehavior(beh);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModBridge] MG fire ex: " + ex.Message);
            }
        }

        private static void FireRangedBehavior(CCharacterBehaviorBase beh)
        {
            CMainCharacterBehavior_Attack_Yuancheng b4 = beh as CMainCharacterBehavior_Attack_Yuancheng;
            if (b4 != null) { b4.m_bAttackDo = false; b4.fChargePercent = 1f; b4.Attack(); return; }

            CMainCharacterBehavior_Attack_YuanchengLianFa b9 = beh as CMainCharacterBehavior_Attack_YuanchengLianFa;
            if (b9 != null) { b9.Attack(); return; }

            CMainCharacterBehavior_Attack_YuanchengMulti b14 = beh as CMainCharacterBehavior_Attack_YuanchengMulti;
            if (b14 != null) { b14.m_bAttackDo = false; b14.fChargePercent = 1f; b14.Attack(); return; }

            CMainCharacterBehavior_Attack_Yuancheng_Free b19 = beh as CMainCharacterBehavior_Attack_Yuancheng_Free;
            if (b19 != null) { b19.m_bAttackDo = false; b19.fChargePercent = 1f; b19.Attack(); return; }

            CMainCharacterBehavior_Attack_YuanchengMulti_Free b20 = beh as CMainCharacterBehavior_Attack_YuanchengMulti_Free;
            if (b20 != null) { b20.m_bAttackDo = false; b20.fChargePercent = 1f; b20.Attack(); return; }

            CMainCharacterBehavior_Attack_DuJiaoShou b25 = beh as CMainCharacterBehavior_Attack_DuJiaoShou;
            if (b25 != null) { b25.m_bAttackDo = false; b25.fChargePercent = 1f; b25.Attack(); return; }

            CMainCharacterBehavior_Attack_Yuancheng_LongYan b26 = beh as CMainCharacterBehavior_Attack_Yuancheng_LongYan;
            if (b26 != null) { b26.m_bAttackDo = false; b26.fChargePercent = 1f; b26.Attack(); return; }

            CMainCharacterBehavior_Attack_GuLingJingGuai b27 = beh as CMainCharacterBehavior_Attack_GuLingJingGuai;
            if (b27 != null) { b27.m_bAttackDo = false; b27.fChargePercent = 1f; b27.Attack(); return; }

            CMainCharacterBehavior_Attack_YuMao b28 = beh as CMainCharacterBehavior_Attack_YuMao;
            if (b28 != null) { b28.m_bAttackDo = false; b28.fChargePercent = 1f; b28.Attack(); return; }

            CMainCharacterBehavior_Attack_DuanChangDi b29 = beh as CMainCharacterBehavior_Attack_DuanChangDi;
            if (b29 != null) { b29.m_bAttackDo = false; b29.fChargePercent = 1f; b29.Attack(); return; }

            CMainCharacterBehavior_Attack_JiHan b30 = beh as CMainCharacterBehavior_Attack_JiHan;
            if (b30 != null) { b30.m_bAttackDo = false; b30.fChargePercent = 1f; b30.Attack(); return; }

            CMainCharacterBehavior_Attack_FeiBiao b18 = beh as CMainCharacterBehavior_Attack_FeiBiao;
            if (b18 != null) { b18.m_bAttackDo = false; b18.fChargePercent = 1f; b18.Attack(); return; }
        }

        // ==================== #4 Dynamic Item / Buff / Icon Exporter ====================
        private unsafe string ReadShmDynDir(byte* b)
        {
            if (b != null && *(b + 0xD58) != 0)
            {
                byte[] buf = new byte[255];
                int n = 0;
                while (n < 255 && *(b + 0xD58 + n) != 0)
                {
                    buf[n] = *(b + 0xD58 + n);
                    n++;
                }
                if (n > 0) return Encoding.UTF8.GetString(buf, 0, n);
            }
            try
            {
                byte[] cmd = File.ReadAllBytes("/proc/self/cmdline");
                int len = 0;
                while (len < cmd.Length && cmd[len] != 0 && cmd[len] != (byte)':') len++;
                if (len > 0)
                {
                    string pkg = Encoding.UTF8.GetString(cmd, 0, len).Trim();
                    if (!string.IsNullOrEmpty(pkg)) return "/data/data/" + pkg + "/files";
                }
            }
            catch { }
            return "/data/data/com.zzonegame.aresvirus2.android/files";
        }

        private unsafe void BumpDynSeq(byte* b)
        {
            if (b == null) return;
            uint cur = *(uint*)(b + 0x28);
            *(uint*)(b + 0x28) = cur + 1u;
        }

        private static string JsonEsc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"' || c == '\\') { sb.Append('\\'); sb.Append(c); }
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < ' ') sb.Append(' ');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private unsafe bool TryExportDynConfigs(byte* b, bool force)
        {
            try
            {
                if (CClientGlobal.Instance == null || CClientGlobal.Instance.GetGameDataCenter == null) return false;
                IGameDataCenter dc = CClientGlobal.Instance.GetGameDataCenter;
                CMaterialConfigLoader matLoader = dc.GetConfigLoader(eGameConfigType.Materiel) as CMaterialConfigLoader;
                CBuffConfigLoader buffLoader = dc.GetConfigLoader(eGameConfigType.Buff) as CBuffConfigLoader;
                if (matLoader == null || buffLoader == null) return false;

                List<sMaterialConfig> mats = matLoader.GetAllConfig();
                List<sBuffConfig> buffs = buffLoader.GetAllConfig();
                if (mats == null || mats.Count < 100 || buffs == null || buffs.Count < 10) return false;

                if (!force && _dynExportDone && mats.Count == _lastExportedMatCount && buffs.Count == _lastExportedBuffCount)
                    return true;

                string dir = ReadShmDynDir(b);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string iconDir = dir + "/dyn_icons";
                if (!Directory.Exists(iconDir)) Directory.CreateDirectory(iconDir);

                CLanguageMgr lm = CLanguageMgr.Instance;

                // Load APK bundled icon set (written once by Java UI)
                Dictionary<string, int> apkIcons = new Dictionary<string, int>();
                string apkListPath = dir + "/apk_icons.txt";
                if (File.Exists(apkListPath))
                {
                    string[] lines = File.ReadAllLines(apkListPath);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string k = lines[i].Trim();
                        if (k.Length > 0) apkIcons[k] = 1;
                    }
                }
                List<string> missingIcons = new List<string>();
                Dictionary<string, int> queuedSet = new Dictionary<string, int>();
                List<int> seenSubtypes = new List<int>();

                // Write dyn_items.json
                StringBuilder isb = new StringBuilder(mats.Count * 160);
                isb.Append('[');
                bool first = true;
                for (int i = 0; i < mats.Count; i++)
                {
                    sMaterialConfig m = mats[i];
                    if (m == null || m.nID <= 0) continue;
                    string nm = !string.IsNullOrEmpty(m.szStringName) ? m.szStringName : (m.nStringName > 0 && lm != null ? lm.GetString(m.nStringName) : ("Item #" + m.nID.ToString()));
                    string ds = !string.IsNullOrEmpty(m.szStringDesc) ? m.szStringDesc : (m.nStringDesc > 0 && lm != null ? lm.GetString(m.nStringDesc) : "");
                    string dt = !string.IsNullOrEmpty(m.szStringDetailDesc) ? m.szStringDetailDesc : (m.nStringDetailDesc > 0 && lm != null ? lm.GetString(m.nStringDetailDesc) : "");
                    string ic = (m.szIcon != null) ? m.szIcon.Trim() : "";

                    if (!first) isb.Append(',');
                    first = false;
                    isb.Append("{\"id\":").Append(m.nID);
                    isb.Append(",\"name\":\"").Append(JsonEsc(nm)).Append('"');
                    isb.Append(",\"desc\":\"").Append(JsonEsc(ds)).Append('"');
                    isb.Append(",\"detail\":\"").Append(JsonEsc(dt)).Append('"');
                    isb.Append(",\"icon\":\"").Append(JsonEsc(ic)).Append('"');
                    isb.Append(",\"cls\":").Append((int)m.eClass);
                    isb.Append(",\"type\":").Append((int)m.eType);
                    isb.Append(",\"bt\":").Append(m.nUIBelongType);
                    isb.Append(",\"bst\":").Append(m.nUIBelongSubType);
                    isb.Append(",\"prio\":").Append(m.nPriority);
                    isb.Append(",\"end\":").Append(m.nEndurance);
                    isb.Append(",\"bp\":").Append(m.nDependBlueprint);
                    isb.Append(",\"cantCombine\":").Append(m.bCannotCombine ? 1 : 0).Append('}');

                    if (!seenSubtypes.Contains(m.nUIBelongSubType)) seenSubtypes.Add(m.nUIBelongSubType);
                    if (ic.Length > 0 && !apkIcons.ContainsKey(ic) && !queuedSet.ContainsKey(ic))
                    {
                        if (!File.Exists(iconDir + "/" + ic + ".png"))
                        {
                            missingIcons.Add(ic);
                            queuedSet[ic] = 1;
                        }
                    }
                }
                isb.Append(']');
                WriteAtomicText(dir + "/dyn_items.json", isb.ToString());

                // Write dyn_subtypes.json from live game GetStringByUISubType(bst)
                StringBuilder ssb = new StringBuilder(512);
                ssb.Append('{');
                for (int i = 0; i < seenSubtypes.Count; i++)
                {
                    int bst = seenSubtypes[i];
                    string sname = "";
                    try { sname = matLoader.GetStringByUISubType(bst); } catch { }
                    if (i > 0) ssb.Append(',');
                    ssb.Append('"').Append(bst).Append("\":\"").Append(JsonEsc(sname ?? "")).Append('"');
                }
                ssb.Append('}');
                WriteAtomicText(dir + "/dyn_subtypes.json", ssb.ToString());

                // Write dyn_buffs.json
                StringBuilder bsb = new StringBuilder(buffs.Count * 120);
                bsb.Append('[');
                first = true;
                for (int i = 0; i < buffs.Count; i++)
                {
                    sBuffConfig bf = buffs[i];
                    if (bf == null || bf.nID <= 0) continue;
                    string nm = !string.IsNullOrEmpty(bf.szName) ? bf.szName : (bf.nName > 0 && lm != null ? lm.GetString(bf.nName) : ("Buff #" + bf.nID.ToString()));
                    string ut = !string.IsNullOrEmpty(bf.szUITitle) ? bf.szUITitle : (bf.nUITitle > 0 && lm != null ? lm.GetString(bf.nUITitle) : "");
                    string ds = !string.IsNullOrEmpty(bf.szDesc) ? bf.szDesc : (bf.nDesc > 0 && lm != null ? lm.GetString(bf.nDesc) : "");
                    string ic = (bf.szIcon != null) ? bf.szIcon.Trim() : "";

                    if (!first) bsb.Append(',');
                    first = false;
                    bsb.Append("{\"id\":").Append(bf.nID);
                    bsb.Append(",\"name\":\"").Append(JsonEsc(nm)).Append('"');
                    bsb.Append(",\"uiTitle\":\"").Append(JsonEsc(ut)).Append('"');
                    bsb.Append(",\"desc\":\"").Append(JsonEsc(ds)).Append('"');
                    bsb.Append(",\"icon\":\"").Append(JsonEsc(ic)).Append('"');
                    bsb.Append(",\"judge\":").Append((int)bf.BuffJudge);
                    bsb.Append(",\"timeType\":").Append((int)bf.TimeType);
                    bsb.Append(",\"keepTime\":").Append(bf.nKeepCycle);
                    bsb.Append(",\"show\":").Append(bf.bShow ? 1 : 0).Append('}');

                    if (ic.Length > 0 && !apkIcons.ContainsKey(ic) && !queuedSet.ContainsKey(ic))
                    {
                        if (!File.Exists(iconDir + "/" + ic + ".png"))
                        {
                            missingIcons.Add(ic);
                            queuedSet[ic] = 1;
                        }
                    }
                }
                bsb.Append(']');
                WriteAtomicText(dir + "/dyn_buffs.json", bsb.ToString());

                _lastExportedMatCount = mats.Count;
                _lastExportedBuffCount = buffs.Count;
                _dynExportDone = true;

                if (missingIcons.Count > 0)
                {
                    _iconQueue = missingIcons.ToArray();
                    _iconQueueLen = missingIcons.Count;
                    _iconQueuePos = 0;
                }
                BumpDynSeq(b);
                Debug.Log("[ModBridge] Dyn exported items=" + mats.Count.ToString() + " buffs=" + buffs.Count.ToString() + " missingIcons=" + missingIcons.Count.ToString());
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModBridge] TryExportDynConfigs ex: " + ex.Message);
                return false;
            }
        }

        private static void WriteAtomicText(string dst, string text)
        {
            string tmp = dst + ".tmp";
            File.WriteAllText(tmp, text, Encoding.UTF8);
            if (File.Exists(dst)) File.Delete(dst);
            File.Move(tmp, dst);
        }

        private unsafe void ProcessIconQueue(byte* b)
        {
            string dir = ReadShmDynDir(b) + "/dyn_icons";
            int doneThisFrame = 0;
            int exported = 0;
            while (_iconQueuePos < _iconQueueLen && doneThisFrame < 2)
            {
                string key = _iconQueue[_iconQueuePos++];
                doneThisFrame++;
                if (string.IsNullOrEmpty(key)) continue;
                try
                {
                    string outPath = dir + "/" + key.Replace('/', '_') + ".png";
                    if (File.Exists(outPath)) continue;
                    CAsset asset = CAssetMgr.Instance != null ? CAssetMgr.Instance.Load<Sprite>(key) : null;
                    Sprite sp = (asset != null) ? (asset.asset as Sprite) : null;
                    if (sp != null && sp.texture != null && ExportSpriteToPng(sp, outPath))
                    {
                        exported++;
                    }
                }
                catch { }
            }
            if (_iconQueuePos >= _iconQueueLen)
            {
                _iconQueue = null;
                _iconQueueLen = 0;
                _iconQueuePos = 0;
                BumpDynSeq(b);
            }
            else if (exported > 0 && (_iconQueuePos % 16) == 0)
            {
                BumpDynSeq(b);
            }
        }

        private static bool ExportSpriteToPng(Sprite sp, string outPath)
        {
            Texture2D src = sp.texture;
            if (src == null || src.width <= 0 || src.height <= 0) return false;
            Rect r = sp.textureRect;
            int rx = Mathf.Max(0, Mathf.FloorToInt(r.x));
            int ry = Mathf.Max(0, Mathf.FloorToInt(r.y));
            int rw = Mathf.Clamp(Mathf.CeilToInt(r.width), 1, src.width - rx);
            int rh = Mathf.Clamp(Mathf.CeilToInt(r.height), 1, src.height - ry);
            if (rw <= 0 || rh <= 0 || rw > 512 || rh > 512) return false;

            RenderTexture prev = RenderTexture.active;
            RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Default);
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;

            Texture2D tex = new Texture2D(rw, rh, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(rx, ry, rw, rh), 0, 0, false);
            Color32[] px = tex.GetPixels32();

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.Destroy(tex);

            if (px == null || px.Length < rw * rh) return false;
            byte[] png = EncodeRgbaToPng(px, rw, rh);
            File.WriteAllBytes(outPath, png);
            return true;
        }

        private static uint Crc32(byte[] buf, int off, int len)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < len; i++)
            {
                crc ^= buf[off + i];
                for (int k = 0; k < 8; k++)
                {
                    uint m = (uint)-(int)(crc & 1u);
                    crc = (crc >> 1) ^ (0xEDB88320u & m);
                }
            }
            return ~crc;
        }

        private static void WriteBE32(byte[] dst, int pos, uint v)
        {
            dst[pos] = (byte)(v >> 24);
            dst[pos + 1] = (byte)(v >> 16);
            dst[pos + 2] = (byte)(v >> 8);
            dst[pos + 3] = (byte)v;
        }

        private static byte[] EncodeRgbaToPng(Color32[] px, int w, int h)
        {
            int rawRowLen = 1 + w * 4;
            int rawLen = h * rawRowLen;
            int maxBlock = 65535;
            int numBlocks = (rawLen + maxBlock - 1) / maxBlock;
            if (numBlocks == 0) numBlocks = 1;
            int zlibLen = 2 + rawLen + numBlocks * 5 + 4;

            // Total PNG = 8 (sig) + 25 (IHDR) + (12 + zlibLen) (IDAT) + 12 (IEND)
            byte[] outBuf = new byte[8 + 25 + 12 + zlibLen + 12];
            outBuf[0] = 0x89; outBuf[1] = 0x50; outBuf[2] = 0x4E; outBuf[3] = 0x47;
            outBuf[4] = 0x0D; outBuf[5] = 0x0A; outBuf[6] = 0x1A; outBuf[7] = 0x0A;

            // IHDR
            WriteBE32(outBuf, 8, 13u);
            outBuf[12] = (byte)'I'; outBuf[13] = (byte)'H'; outBuf[14] = (byte)'D'; outBuf[15] = (byte)'R';
            WriteBE32(outBuf, 16, (uint)w);
            WriteBE32(outBuf, 20, (uint)h);
            outBuf[24] = 8; outBuf[25] = 6; outBuf[26] = 0; outBuf[27] = 0; outBuf[28] = 0;
            WriteBE32(outBuf, 29, Crc32(outBuf, 12, 17));

            // IDAT header
            int idatOff = 33;
            WriteBE32(outBuf, idatOff, (uint)zlibLen);
            outBuf[idatOff + 4] = (byte)'I'; outBuf[idatOff + 5] = (byte)'D';
            outBuf[idatOff + 6] = (byte)'A'; outBuf[idatOff + 7] = (byte)'T';

            int zp = idatOff + 8;
            outBuf[zp++] = 0x78; outBuf[zp++] = 0x01; // zlib header (deflate, fast/none)

            uint s1 = 1u, s2 = 0u;
            int y = h - 1; // Flip Unity bottom-up pixels to PNG top-down
            int xbyte = -1;
            int rem = rawLen;
            while (rem > 0)
            {
                int blk = (rem > maxBlock) ? maxBlock : rem;
                rem -= blk;
                outBuf[zp++] = (rem == 0) ? (byte)1 : (byte)0;
                outBuf[zp++] = (byte)(blk & 0xFF);
                outBuf[zp++] = (byte)((blk >> 8) & 0xFF);
                int nlen = (~blk) & 0xFFFF;
                outBuf[zp++] = (byte)(nlen & 0xFF);
                outBuf[zp++] = (byte)((nlen >> 8) & 0xFF);

                for (int k = 0; k < blk; k++)
                {
                    byte val;
                    if (xbyte < 0)
                    {
                        val = 0; // filter None
                        xbyte = 0;
                    }
                    else
                    {
                        int pxIdx = y * w + (xbyte >> 2);
                        Color32 c = px[pxIdx];
                        int comp = xbyte & 3;
                        val = (comp == 0) ? c.r : (comp == 1 ? c.g : (comp == 2 ? c.b : c.a));
                        xbyte++;
                        if (xbyte >= w * 4) { xbyte = -1; y--; }
                    }
                    outBuf[zp++] = val;
                    s1 = (s1 + val) % 65521u;
                    s2 = (s2 + s1) % 65521u;
                }
            }
            WriteBE32(outBuf, zp, (s2 << 16) | s1); zp += 4;
            WriteBE32(outBuf, zp, Crc32(outBuf, idatOff + 4, 4 + zlibLen)); zp += 4;

            // IEND
            WriteBE32(outBuf, zp, 0u);
            outBuf[zp + 4] = (byte)'I'; outBuf[zp + 5] = (byte)'E'; outBuf[zp + 6] = (byte)'N'; outBuf[zp + 7] = (byte)'D';
            WriteBE32(outBuf, zp + 8, 0xAE426082u);
            return outBuf;
        }
    }
}
