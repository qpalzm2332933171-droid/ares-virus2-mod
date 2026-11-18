using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Com.LuisPedroFonseca.ProCamera2D;
using NetCore.Define;
using NetCore.Util;
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

    internal class ModContainerSystem : CContainerSystem, IContainerSystem
    {
        private readonly CContainerSystem _orig;
        private static FieldInfo s_fiDicData;
        private static FieldInfo s_fiContUids;

        public ModContainerSystem(CContainerSystem orig)
        {
            _orig = orig;
            if (orig != null)
            {
                try
                {
                    FieldInfo[] fields = typeof(CContainerSystem).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    for (int i = 0; i < fields.Length; i++)
                    {
                        fields[i].SetValue(this, fields[i].GetValue(orig));
                    }
                }
                catch { }
            }
        }

        private Dictionary<int, sContainerSystemData> GetDataDict()
        {
            if (s_fiDicData == null)
                s_fiDicData = typeof(CContainerSystem).GetField("m_dicData", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return (s_fiDicData != null) ? (s_fiDicData.GetValue(this) as Dictionary<int, sContainerSystemData>) : null;
        }

        private static List<ulong> GetUids(CContainer c)
        {
            if (c == null) return null;
            if (s_fiContUids == null)
                s_fiContUids = typeof(CContainer).GetField("m_listUIDs", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return (s_fiContUids != null) ? (s_fiContUids.GetValue(c) as List<ulong>) : null;
        }

        private static MethodInfo s_miAddSaveTag;

        internal static void MarkSaveTag(object sysData)
        {
            if (sysData == null) return;
            try
            {
                if (s_miAddSaveTag == null)
                    s_miAddSaveTag = typeof(CGameSystemDataBase).GetMethod("AddSaveTag", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (s_miAddSaveTag != null) s_miAddSaveTag.Invoke(sysData, null);
            }
            catch { }
        }

        private static eContainerTypeEnum ConvertBagType(eContainerTypeEnum eContainerType, sMaterialConfig.eMaterialType eMaterialType)
        {
            if (eContainerType == eContainerTypeEnum.Bag)
            {
                switch (eMaterialType)
                {
                    case sMaterialConfig.eMaterialType.Blueprint: return eContainerTypeEnum.Blueprint;
                    case sMaterialConfig.eMaterialType.Map: return eContainerTypeEnum.Map;
                    case sMaterialConfig.eMaterialType.EXP: return eContainerTypeEnum.EXP;
                    case sMaterialConfig.eMaterialType.ZB: return eContainerTypeEnum.Wallet;
                }
            }
            return eContainerType;
        }

        private static void FireContainerChange(int nCustomID, eContainerTypeEnum eType, int cfgId)
        {
            try
            {
                if (CClientGlobal.Instance != null && CClientGlobal.Instance.GetEventSystem != null)
                {
                    eMSG.sContainer_Change ev = default(eMSG.sContainer_Change);
                    ev.nCustomID = nCustomID;
                    ev.eContainer = eType;
                    ev.nMaterialConfigID = cfgId;
                    CClientGlobal.Instance.GetEventSystem.FireExecoute(eMSG.EVENT.Container_Change, 0, ev);
                }
            }
            catch { }
        }

        private static void SafePruneContainer(CContainer cont)
        {
            if (cont == null) return;
            IGameSystem gs = CGameSystem.Instance;
            if (gs == null || gs.materialSystem == null) return;
            List<ulong> uids = GetUids(cont);
            if (uids == null || uids.Count == 0) return;
            bool changed = false;
            int write = 0;
            for (int i = 0; i < uids.Count; i++)
            {
                ulong uid = uids[i];
                CMaterial m = gs.materialSystem.GetMaterialByUID(uid);
                if (m == null)
                {
                    changed = true;
                    continue;
                }
                if (m.nNumber <= 0 || (m.pConfig != null && m.pConfig.nEndurance != 0 && m.nEndurance == 0 && m.pConfig.bDestoryNoEndurance))
                {
                    gs.materialSystem.DestroyMaterial(m.UID, true);
                    changed = true;
                    continue;
                }
                uids[write++] = uid;
            }
            if (write < uids.Count)
            {
                uids.RemoveRange(write, uids.Count - write);
            }
            if (changed) MarkSaveTag(cont);
        }

        private CContainer GetOrCreateContainer(int nCustomID, eContainerTypeEnum eType)
        {
            var dic = GetDataDict();
            if (dic == null) return null;
            sContainerSystemData sdata = null;
            if (!dic.TryGetValue(nCustomID, out sdata) || sdata == null)
            {
                sdata = new sContainerSystemData();
                sdata.nCustomID = nCustomID;
                dic[nCustomID] = sdata;
            }
            CContainer cont = null;
            if (!sdata.dicContainer.TryGetValue(eType, out cont) || cont == null)
            {
                cont = CContainerFactory.Create(eType, nCustomID);
                if (cont != null) sdata.dicContainer[eType] = cont;
            }
            return cont;
        }

        public new void AddMaterialToContainer(int nCustomID, eContainerTypeEnum eType, ulong UID, bool bFireEvent = true)
        {
            IGameSystem gs = CGameSystem.Instance;
            if ((EditorPrefsX.endianDiff1 & 32) == 0 || gs == null || gs.materialSystem == null)
            {
                base.AddMaterialToContainer(nCustomID, eType, UID, bFireEvent);
                ModTicker.MarkWarehouseDirty();
                return;
            }

            CMaterial mat = gs.materialSystem.GetMaterialByUID(UID);
            if (mat == null || mat.pConfig == null) return;
            sMaterialConfig cfg = mat.pConfig;
            eType = ConvertBagType(eType, cfg.eType);
            if (eType != eContainerTypeEnum.Bag)
            {
                base.AddMaterialToContainer(nCustomID, eType, UID, bFireEvent);
                return;
            }

            CContainer cont = GetOrCreateContainer(nCustomID, eType);
            if (cont == null) return;
            bool isFirst = (cont.GetMaterialNumber(mat.nConfigID) == 0);
            bool inShelter = ModTicker.IsInShelterNow(gs);
            IRefitSystem refit = gs.refitSystem;
            bool isRefitNew = (refit != null && refit.IsRefit(UID));
            bool canMerge = inShelter ? !isRefitNew : (!cfg.bCannotCombine && cfg.nEndurance == 0);

            bool merged = false;
            if (canMerge)
            {
                List<ulong> uids = GetUids(cont);
                if (uids != null)
                {
                    for (int i = 0; i < uids.Count; i++)
                    {
                        ulong exUid = uids[i];
                        if (exUid == UID) { merged = true; break; }
                        if (refit != null && refit.IsRefit(exUid)) continue;
                        CMaterial ex = gs.materialSystem.GetMaterialByUID(exUid);
                        if (ex != null && ex.nConfigID == mat.nConfigID && ex.nEndurance == mat.nEndurance)
                        {
                            long tot = (long)ex.nNumber + (long)mat.nNumber;
                            if (tot <= (long)int.MaxValue)
                            {
                                ex.SetNum((int)tot, false);
                                gs.materialSystem.DestroyMaterial(UID, true);
                                MarkSaveTag(cont);
                                merged = true;
                                break;
                            }
                        }
                    }
                }
            }
            if (!merged)
            {
                cont.AddMaterial(UID, false);
                SafePruneContainer(cont);
            }

            if (CGameSceneMgr.Instance != null && CGameSceneMgr.Instance.GetCurrentChararcterCustomID() == nCustomID)
            {
                if (isFirst && cfg.bIsImportent && gs.tipsSystem != null)
                    gs.tipsSystem.ShowImportantTips_Material(cfg.szIcon, cfg.szStringName);
            }
            if (bFireEvent) FireContainerChange(nCustomID, eType, cfg.nID);
            ModTicker.MarkWarehouseDirty();
        }

        public new void AddMaterialToContainer(int nCustomID, eContainerTypeEnum eType, int nMaterialConfigID, int nNum, bool bFireEvent = true)
        {
            IGameSystem gs = CGameSystem.Instance;
            if ((EditorPrefsX.endianDiff1 & 32) == 0 || gs == null || gs.materialSystem == null || CClientGlobal.Instance == null || CClientGlobal.Instance.GetGameDataCenter == null)
            {
                base.AddMaterialToContainer(nCustomID, eType, nMaterialConfigID, nNum, bFireEvent);
                ModTicker.MarkWarehouseDirty();
                return;
            }

            CMaterialConfigLoader loader = CClientGlobal.Instance.GetGameDataCenter.GetConfigLoader<CMaterialConfigLoader>(eGameConfigType.Begin);
            sMaterialConfig cfg = (loader != null) ? loader.GetConfig(nMaterialConfigID) : null;
            if (cfg == null) return;
            eType = ConvertBagType(eType, cfg.eType);
            if (eType != eContainerTypeEnum.Bag)
            {
                base.AddMaterialToContainer(nCustomID, eType, nMaterialConfigID, nNum, bFireEvent);
                return;
            }

            CContainer cont = GetOrCreateContainer(nCustomID, eType);
            if (cont == null) return;
            bool isFirst = (cont.GetMaterialNumber(nMaterialConfigID) == 0);
            bool inShelter = ModTicker.IsInShelterNow(gs);
            bool canMerge = inShelter || (!cfg.bCannotCombine && cfg.nEndurance == 0);

            if (canMerge)
            {
                IRefitSystem refit = gs.refitSystem;
                CMaterial found = null;
                List<ulong> uids = GetUids(cont);
                if (uids != null)
                {
                    for (int i = 0; i < uids.Count; i++)
                    {
                        if (refit != null && refit.IsRefit(uids[i])) continue;
                        CMaterial cand = gs.materialSystem.GetMaterialByUID(uids[i]);
                        if (cand != null && cand.nConfigID == nMaterialConfigID && cand.nEndurance == cfg.nEndurance)
                        {
                            found = cand;
                            break;
                        }
                    }
                }
                if (found != null)
                {
                    long tot = (long)found.nNumber + (long)nNum;
                    found.SetNum((tot > int.MaxValue) ? int.MaxValue : (int)tot, false);
                    MarkSaveTag(cont);
                }
                else
                {
                    CMaterial m = gs.materialSystem.CreateMaterial(nMaterialConfigID, nNum);
                    if (m != null) cont.AddMaterial(m.UID, false);
                }
            }
            else
            {
                for (int i = 0; i < nNum; i++)
                {
                    CMaterial m = gs.materialSystem.CreateMaterial(nMaterialConfigID, 1);
                    if (m != null) cont.AddMaterial(m.UID, false);
                }
                SafePruneContainer(cont);
            }

            if (CGameSceneMgr.Instance != null && CGameSceneMgr.Instance.GetCurrentChararcterCustomID() == nCustomID)
            {
                if (isFirst && cfg.bIsImportent && gs.tipsSystem != null)
                    gs.tipsSystem.ShowImportantTips_Material(cfg.szIcon, cfg.szStringName);
            }
            if (bFireEvent) FireContainerChange(nCustomID, eType, nMaterialConfigID);
            ModTicker.MarkWarehouseDirty();
        }

        public new void RefreshContainer(int nCustomID, eContainerTypeEnum eType)
        {
            if ((EditorPrefsX.endianDiff1 & 32) != 0 && eType == eContainerTypeEnum.Bag)
            {
                var dic = GetDataDict();
                sContainerSystemData sdata = null;
                CContainer cont = null;
                if (dic != null && dic.TryGetValue(nCustomID, out sdata) && sdata != null && sdata.dicContainer.TryGetValue(eType, out cont))
                {
                    SafePruneContainer(cont);
                }
                return;
            }
            base.RefreshContainer(nCustomID, eType);
        }

        public new void RemoveMaterialFromContainer(int nCustomID, eContainerTypeEnum eType, ulong UID, bool bFireEvent = true)
        {
            base.RemoveMaterialFromContainer(nCustomID, eType, UID, bFireEvent);
            if (eType == eContainerTypeEnum.Bag) ModTicker.MarkWarehouseDirty();
        }

        public new void RemoveMaterialFromContainer(int nCustomID, eContainerTypeEnum eType, int nMaterialConfigID, int nNum, bool bFireEvent = true)
        {
            base.RemoveMaterialFromContainer(nCustomID, eType, nMaterialConfigID, nNum, bFireEvent);
            if (eType == eContainerTypeEnum.Bag) ModTicker.MarkWarehouseDirty();
        }
    }

    internal class ModPaySystem : CPaySystem, IPaySystem
    {
        public static bool s_modDiamond = false;
        public static bool s_modCoin = false;
        public static bool s_modPoint = false;
        public static bool s_modCumul = false;
        public static int s_valDiamond = 0;
        public static int s_valCoin = 0;
        public static int s_valPoint = 0;
        public static int s_valCumul = 0;

        private static FieldInfo s_fiDia, s_fiCoin, s_fiPoint, s_fiCumul;

        public ModPaySystem(CPaySystem orig)
        {
            if (orig != null)
            {
                try
                {
                    FieldInfo[] fields = typeof(CPaySystem).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    for (int i = 0; i < fields.Length; i++)
                    {
                        fields[i].SetValue(this, fields[i].GetValue(orig));
                    }
                }
                catch { }
            }
            EnsureFields();
            if (s_modDiamond) SetRaw(0, s_valDiamond, false);
            if (s_modCoin) SetRaw(1, s_valCoin, false);
            if (s_modPoint) SetRaw(2, s_valPoint, false);
            if (s_modCumul) SetRaw(3, s_valCumul, false);
        }

        private static void EnsureFields()
        {
            BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            if (s_fiDia == null) s_fiDia = typeof(CPaySystem).GetField("m_nDiamondNum", bf);
            if (s_fiCoin == null) s_fiCoin = typeof(CPaySystem).GetField("m_nGardenCoinNum", bf);
            if (s_fiPoint == null) s_fiPoint = typeof(CPaySystem).GetField("m_nGardenPointNum", bf);
            if (s_fiCumul == null) s_fiCumul = typeof(CPaySystem).GetField("m_nCumulativePayment", bf);
        }

        public static bool s_persistDirty = false;

        public static bool IsAnyCurrencyModded()
        {
            return s_modDiamond || s_modCoin || s_modPoint || s_modCumul;
        }

        public static void ForceSetStatic(int type, int val, bool markDirty)
        {
            if (val < 0)
            {
                ForceClearStatic(type, markDirty);
                return;
            }
            if (type == 0) { s_modDiamond = true; s_valDiamond = val; }
            else if (type == 1) { s_modCoin = true; s_valCoin = val; }
            else if (type == 2) { s_modPoint = true; s_valPoint = val; }
            else if (type == 3) { s_modCumul = true; s_valCumul = val; }
            if (markDirty) s_persistDirty = true;
            SyncServerInit();
        }

        public static void ForceClearStatic(int type, bool markDirty)
        {
            if (type < 0 || type == 0) s_modDiamond = false;
            if (type < 0 || type == 1) s_modCoin = false;
            if (type < 0 || type == 2) s_modPoint = false;
            if (type < 0 || type == 3) s_modCumul = false;
            if (markDirty) s_persistDirty = true;
        }

        public void ForceSet(int type, int val)
        {
            if (val < 0)
            {
                ForceClear(type);
                return;
            }
            ForceSetStatic(type, val, true);
            SetRaw(type, val, true);
        }

        public void ForceClear(int type)
        {
            ForceClearStatic(type, true);
        }

        public static string SerializePersistJson()
        {
            return "{\"md\":" + (s_modDiamond ? 1 : 0) + ",\"d\":" + s_valDiamond +
                   ",\"mgc\":" + (s_modCoin ? 1 : 0) + ",\"gc\":" + s_valCoin +
                   ",\"mgp\":" + (s_modPoint ? 1 : 0) + ",\"gp\":" + s_valPoint +
                   ",\"mcp\":" + (s_modCumul ? 1 : 0) + ",\"cp\":" + s_valCumul + "}";
        }

        public static void SyncServerInit()
        {
            try
            {
                if (s_modDiamond) CPaySystem.ServerInitDiamond = (inte)s_valDiamond;
                if (s_modCoin) CPaySystem.ServerInitGardenCoin = (inte)s_valCoin;
                if (s_modPoint) CPaySystem.ServerInitGardenPoint = (inte)s_valPoint;
                if (s_modCumul) CPaySystem.ServermInitCumulativePayment.SetNum(s_valCumul);
            }
            catch { }
        }

        public void EnsurePersistedApplied()
        {
            SyncServerInit();
            if (s_modDiamond && (base.GetDiamondNum() != s_valDiamond || (int)CPaySystem.ServerInitDiamond != s_valDiamond))
                SetRaw(0, s_valDiamond, true);
            if (s_modCoin && (base.GetGardenCoinNum() != s_valCoin || (int)CPaySystem.ServerInitGardenCoin != s_valCoin))
                SetRaw(1, s_valCoin, true);
            if (s_modPoint && (base.GetGardenPointNum() != s_valPoint || (int)CPaySystem.ServerInitGardenPoint != s_valPoint))
                SetRaw(2, s_valPoint, true);
            if (s_modCumul && (GetRawCumulative() != s_valCumul || CPaySystem.ServermInitCumulativePayment.GetNum() != s_valCumul))
                SetRaw(3, s_valCumul, true);
        }

        private int GetRawCumulative()
        {
            EnsureFields();
            if (s_fiCumul != null)
            {
                try { return ((inte)s_fiCumul.GetValue(this)).GetNum(); } catch { }
            }
            return 0;
        }

        private void SetRaw(int type, int val, bool fire)
        {
            if (type == 0)
            {
                if (fire) base.SetDiamondNum(val);
                else if (s_fiDia != null) s_fiDia.SetValue(this, (inte)val);
            }
            else if (type == 1)
            {
                if (fire) base.SetGardenCoinNum(val);
                else if (s_fiCoin != null) s_fiCoin.SetValue(this, (inte)val);
            }
            else if (type == 2)
            {
                if (fire) base.SetGardenPointNum(val);
                else if (s_fiPoint != null) s_fiPoint.SetValue(this, (inte)val);
            }
            else if (type == 3)
            {
                if (fire) base.SetCumulativePayment(val);
                else if (s_fiCumul != null) s_fiCumul.SetValue(this, (inte)val);
            }
        }

        public new int GetDiamondNum()
        {
            return s_modDiamond ? s_valDiamond : base.GetDiamondNum();
        }

        public new void SetDiamondNum(int nNum)
        {
            if (s_modDiamond)
            {
                SyncServerInit();
                base.SetDiamondNum(s_valDiamond);
                return;
            }
            base.SetDiamondNum(nNum);
        }

        public new int GetGardenCoinNum()
        {
            return s_modCoin ? s_valCoin : base.GetGardenCoinNum();
        }

        public new void SetGardenCoinNum(int nNum)
        {
            if (s_modCoin)
            {
                SyncServerInit();
                base.SetGardenCoinNum(s_valCoin);
                return;
            }
            base.SetGardenCoinNum(nNum);
        }

        public new int GetGardenPointNum()
        {
            return s_modPoint ? s_valPoint : base.GetGardenPointNum();
        }

        public new void SetGardenPointNum(int nNum)
        {
            if (s_modPoint)
            {
                SyncServerInit();
                base.SetGardenPointNum(s_valPoint);
                return;
            }
            base.SetGardenPointNum(nNum);
        }

        public new int GetCumulativePayment()
        {
            if (s_modCumul) return s_valCumul;
            EnsureFields();
            if (s_fiCumul != null)
            {
                try { return ((inte)s_fiCumul.GetValue(this)).GetNum(); } catch { }
            }
            return 0;
        }

        public new void SetCumulativePayment(int nNum)
        {
            if (s_modCumul)
            {
                SyncServerInit();
                base.SetCumulativePayment(s_valCumul);
                return;
            }
            base.SetCumulativePayment(nNum);
        }
    }

    internal class ModEquipmentSystem : CEquipmentSystem, IEquipmentSystem
    {
        private static ulong s_lastEqUid = 0UL;
        private static int s_lastEqAmmoId = 0;

        private static int s_ammoCid = 0;
        private static int s_ammoCfgId = 0;
        private static int s_bagCountSnap = -1;
        private static int s_otherAmmoSum = 0;
        private static ulong s_primaryUid = 0UL;
        private static CMaterial s_primaryMat = null;
        private static CContainer s_bagCont = null;
        private static uint s_lastRebuildTick = 0u;

        private static FieldInfo s_fiDicData = null;
        private static FieldInfo s_fiContUids = null;
        private static CUIInputAmmo s_ammoUi = null;
        private static FieldInfo s_fiUiAmmoLbNum = null;
        private static object s_ammoLbObj = null;
        private static Action<string> s_setAmmoText = null;
        private static PropertyInfo s_piGraphicColor = null;
        private static uint s_lastUiFindTick = 0u;

        private static bool ResolveAmmoBagCache(IGameSystem gs, int cid, int ammoId)
        {
            CContainerSystem cs = gs.containerSystem as CContainerSystem;
            if (cs == null) return false;
            BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            if (s_fiDicData == null) s_fiDicData = typeof(CContainerSystem).GetField("m_dicData", bf);
            if (s_fiContUids == null) s_fiContUids = typeof(CContainer).GetField("m_listUIDs", bf);
            var dic = (s_fiDicData != null) ? (s_fiDicData.GetValue(cs) as Dictionary<int, sContainerSystemData>) : null;
            sContainerSystemData sdata = null;
            CContainer bag = null;
            if (dic == null || !dic.TryGetValue(cid, out sdata) || sdata == null ||
                !sdata.dicContainer.TryGetValue(eContainerTypeEnum.Bag, out bag) || bag == null)
                return false;

            List<ulong> uids = (s_fiContUids != null) ? (s_fiContUids.GetValue(bag) as List<ulong>) : null;
            if (uids == null) return false;

            uint tick = ModTicker.CurrentTick;
            if (s_primaryMat != null && s_bagCont == bag && s_ammoCid == cid && s_ammoCfgId == ammoId &&
                uids.Count == s_bagCountSnap && (tick - s_lastRebuildTick) < 120u &&
                s_primaryMat.nNumber > 0 && gs.materialSystem.GetMaterialByUID(s_primaryUid) == s_primaryMat)
            {
                return true;
            }

            CMaterial primary = null;
            int otherSum = 0;
            for (int i = 0; i < uids.Count; i++)
            {
                CMaterial m = gs.materialSystem.GetMaterialByUID(uids[i]);
                if (m != null && m.nConfigID == ammoId && m.nNumber > 0)
                {
                    if (primary == null) primary = m;
                    else otherSum += m.nNumber;
                }
            }
            if (primary == null) return false;

            s_ammoCid = cid;
            s_ammoCfgId = ammoId;
            s_bagCont = bag;
            s_bagCountSnap = uids.Count;
            s_primaryMat = primary;
            s_primaryUid = primary.UID;
            s_otherAmmoSum = otherSum;
            s_lastRebuildTick = tick;
            return true;
        }

        private static void UpdateAmmoHudFast(int total)
        {
            try
            {
                if (s_ammoUi == null || !s_ammoUi.gameObject.activeInHierarchy)
                {
                    uint t = ModTicker.CurrentTick;
                    if ((t - s_lastUiFindTick) < 30u && s_ammoUi == null) return;
                    s_lastUiFindTick = t;
                    s_ammoUi = UnityEngine.Object.FindObjectOfType(typeof(CUIInputAmmo)) as CUIInputAmmo;
                    s_setAmmoText = null;
                    s_ammoLbObj = null;
                }
                if (s_ammoUi == null || !s_ammoUi.gameObject.activeInHierarchy) return;
                if (s_setAmmoText == null)
                {
                    BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                    if (s_fiUiAmmoLbNum == null) s_fiUiAmmoLbNum = typeof(CUIInputAmmo).GetField("m_pLb_Num", bf);
                    if (s_fiUiAmmoLbNum == null) return;
                    s_ammoLbObj = s_fiUiAmmoLbNum.GetValue(s_ammoUi);
                    if (s_ammoLbObj == null) return;
                    Type lt = s_ammoLbObj.GetType();
                    PropertyInfo pt = lt.GetProperty("text", bf);
                    MethodInfo setter = (pt != null) ? pt.GetSetMethod(true) : null;
                    if (setter != null)
                    {
                        s_setAmmoText = (Action<string>)Delegate.CreateDelegate(typeof(Action<string>), s_ammoLbObj, setter);
                    }
                    s_piGraphicColor = lt.GetProperty("color", bf);
                    if (s_piGraphicColor != null) s_piGraphicColor.SetValue(s_ammoLbObj, Color.white, null);
                }
                if (s_setAmmoText != null) s_setAmmoText(total.ToString());
            }
            catch { s_setAmmoText = null; s_ammoUi = null; }
        }

        private void HandleReduceAmmo(ulong equipmentUID, int nCustomID_Character)
        {
            if ((EditorPrefsX.endianDiff1 & 2) != 0)
            {
                IGameSystem gs = CGameSystem.Instance;
                if (gs != null && gs.materialSystem != null && gs.containerSystem != null)
                {
                    int ammoId = 0;
                    if (equipmentUID == s_lastEqUid && s_lastEqAmmoId > 0)
                    {
                        ammoId = s_lastEqAmmoId;
                    }
                    else
                    {
                        CEquipment eq = gs.materialSystem.GetMaterialByUID(equipmentUID) as CEquipment;
                        if (eq != null && eq.pEquipmentConfig != null) ammoId = eq.pEquipmentConfig.nAmmo;
                        s_lastEqUid = equipmentUID;
                        s_lastEqAmmoId = ammoId;
                    }
                    if (ammoId != 0)
                    {
                        if (ResolveAmmoBagCache(gs, nCustomID_Character, ammoId))
                        {
                            int cur = s_primaryMat.nNumber;
                            int next = (cur < 999999999) ? (cur + 1) : cur;
                            s_primaryMat.SetNum(next, false);
                            UpdateAmmoHudFast(next + s_otherAmmoSum);
                            return;
                        }
                        // Stack does not exist yet: create initial bullet once
                        gs.containerSystem.AddMaterialToContainer(nCustomID_Character, eContainerTypeEnum.Bag, ammoId, 1, true);
                        s_lastRebuildTick = 0u;
                        return;
                    }
                }
            }
            base.ReduceAmmo(equipmentUID, nCustomID_Character);
        }

        void IEquipmentSystem.ReduceAmmo(ulong equipmentUID, int nCustomID_Character)
        {
            HandleReduceAmmo(equipmentUID, nCustomID_Character);
        }

        public new void ReduceAmmo(ulong equipmentUID, int nCustomID_Character)
        {
            HandleReduceAmmo(equipmentUID, nCustomID_Character);
        }
    }

    internal class ModHomeContainerSystem : CHomeContainerSystem, IHomeContainerSystem
    {
        private static readonly string[] s_copyFields = new string[] {
            "m_dicContainer",
            "m_listQuarantineRecord",
            "m_dicSkippedDBID"
        };

        public ModHomeContainerSystem(CHomeContainerSystem orig)
        {
            Create();
            if (orig != null)
            {
                BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                for (int i = 0; i < s_copyFields.Length; i++)
                {
                    FieldInfo fi = typeof(CHomeContainerSystem).GetField(s_copyFields[i], bf);
                    if (fi != null)
                    {
                        object val = fi.GetValue(orig);
                        if (val != null) fi.SetValue(this, val);
                    }
                }
            }
        }

        private EHomeContainerTransferResult HandleTryStoreMaterial(int nOwnerCustomID, long lInstanceId, int nCapacity, ulong nMaterialUID)
        {
            if ((EditorPrefsX.endianDiff1 & 32) == 0)
            {
                return base.TryStoreMaterial(nOwnerCustomID, lInstanceId, nCapacity, nMaterialUID);
            }

            IGameSystem gs = CGameSystem.Instance;
            EHomeContainerTransferResult res = base.TryStoreMaterial(nOwnerCustomID, lInstanceId, nCapacity, nMaterialUID);
            if (res == EHomeContainerTransferResult.Success)
            {
                CHomeContainer hc = TryGetContainer(lInstanceId);
                if (hc != null) ModTicker.MergeSingleHomeContainer(gs, hc);
                ModTicker.NotifyWarehouseChanged(gs);
                return EHomeContainerTransferResult.Success;
            }

            if (res == EHomeContainerTransferResult.FailedBoxFull && gs != null && gs.materialSystem != null && gs.containerSystem != null)
            {
                CHomeContainer hc = TryGetContainer(lInstanceId);
                CMaterial mat = gs.materialSystem.GetMaterialByUID(nMaterialUID);
                IRefitSystem refit = gs.refitSystem;
                if (hc != null && mat != null && mat.nNumber > 0 && (refit == null || !refit.IsRefit(nMaterialUID)))
                {
                    int slotCnt = hc.GetSlotCount();
                    CMaterial target = null;
                    for (int i = 0; i < slotCnt; i++)
                    {
                        ulong candUid = hc.GetMaterialUID(i);
                        CMaterial cand = gs.materialSystem.GetMaterialByUID(candUid);
                        if (cand != null && cand.nNumber > 0 && cand.nConfigID == mat.nConfigID && cand.nEndurance == mat.nEndurance && (refit == null || !refit.IsRefit(candUid)))
                        {
                            if ((long)cand.nNumber + (long)mat.nNumber <= (long)int.MaxValue)
                            {
                                target = cand;
                                break;
                            }
                        }
                    }
                    if (target != null)
                    {
                        ulong dbid = mat.DBID;
                        gs.containerSystem.RemoveMaterialFromContainer(nOwnerCustomID, eContainerTypeEnum.Bag, nMaterialUID);
                        target.SetNum(target.nNumber + mat.nNumber, false);
                        gs.materialSystem.DestroyMaterial(nMaterialUID, true);
                        if (gs.characterSystem != null)
                        {
                            CCharacterSystemData csd = gs.characterSystem.GetCharacterSystemData(nOwnerCustomID);
                            if (csd != null) csd.RemoveQuicklyShortcut(dbid);
                        }
                        ModTicker.NotifyWarehouseChanged(gs);
                        return EHomeContainerTransferResult.Success;
                    }
                }
            }
            return res;
        }

        EHomeContainerTransferResult IHomeContainerSystem.TryStoreMaterial(int nOwnerCustomID, long lInstanceId, int nCapacity, ulong nMaterialUID)
        {
            return HandleTryStoreMaterial(nOwnerCustomID, lInstanceId, nCapacity, nMaterialUID);
        }

        public new EHomeContainerTransferResult TryStoreMaterial(int nOwnerCustomID, long lInstanceId, int nCapacity, ulong nMaterialUID)
        {
            return HandleTryStoreMaterial(nOwnerCustomID, lInstanceId, nCapacity, nMaterialUID);
        }
    }

    public class ModTicker : MonoBehaviour
    {
        private sealed class RangedBehMeta
        {
            public FieldInfo fCharge;
            public FieldInfo fAttackDo;
            public MethodInfo miAttack;
            public MethodInfo miEnd;
            public FieldInfo fBehavior;
        }

        // Safe Reflection Helpers (Immune to Hot Updates and Access Modifiers)
        private static FieldInfo s_fiHomeContainers = null;
        private static FieldInfo s_fiHomeItemData = null;
        private static FieldInfo s_fiContainerData = null;
        private static FieldInfo s_fiPayPkg = null;
        private static FieldInfo s_fiPayDiamondPkg = null;
        private static FieldInfo s_fiPayLimitGift = null;
        private static FieldInfo s_fiDlcInstall = null;
        private static FieldInfo s_fiHolidayConfig = null;
        private static FieldInfo s_fiCurState = null;
        private static FieldInfo s_fiBehavior = null;
        private static FieldInfo s_fiInputAttack = null;
        private static FieldInfo s_fiJoyPointerDown = null;
        private static FieldInfo s_fiJoyEnterCancel = null;
        private static FieldInfo s_fiBuffCurCycle = null;
        private static FieldInfo s_fiGsEquipSys = null;
        private static FieldInfo s_fiGsHomeContSys = null;
        private static FieldInfo s_fiDndImageBg = null;
        private static FieldInfo s_fiActSorted = null;
        private static PropertyInfo s_piActOpen = null;
        private static PropertyInfo s_piActEnd = null;
        private static MethodInfo s_miSpBehEnd = null;
        private static FieldInfo s_fiLanCurState = null;
        private static MethodInfo s_miStorageBoxRefresh = null;
        private static Type s_cachedStorageBoxType = null;
        private static readonly Dictionary<Type, RangedBehMeta> s_typeCache = new Dictionary<Type, RangedBehMeta>();
        private static ModTicker s_instance = null;

        private static RangedBehMeta GetTypeMeta(Type t)
        {
            RangedBehMeta meta;
            if (s_typeCache.TryGetValue(t, out meta)) return meta;
            meta = new RangedBehMeta();
            BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            meta.fCharge = t.GetField("fChargePercent", bf);
            meta.fAttackDo = t.GetField("m_bAttackDo", bf);
            meta.miAttack = t.GetMethod("Attack", bf);
            meta.miEnd = t.GetMethod("End", bf);
            meta.fBehavior = t.GetField("m_pBehavior", bf);
            s_typeCache[t] = meta;
            return meta;
        }

        private static Dictionary<long, CHomeContainer> GetHomeContainers(CHomeContainerSystem hcs)
        {
            if (hcs == null) return null;
            if (s_fiHomeContainers == null)
                s_fiHomeContainers = typeof(CHomeContainerSystem).GetField("m_dicContainer", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return s_fiHomeContainers != null ? (Dictionary<long, CHomeContainer>)s_fiHomeContainers.GetValue(hcs) : null;
        }

        private static Dictionary<int, Dictionary<long, HomeItemPlacementData>> GetHomeItemData(CHomeItemSystem his)
        {
            if (his == null) return null;
            if (s_fiHomeItemData == null)
                s_fiHomeItemData = typeof(CHomeItemSystem).GetField("m_dicData", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return s_fiHomeItemData != null ? (Dictionary<int, Dictionary<long, HomeItemPlacementData>>)s_fiHomeItemData.GetValue(his) : null;
        }

        private static Dictionary<int, sContainerSystemData> GetContainerData(CContainerSystem cs)
        {
            if (cs == null) return null;
            if (s_fiContainerData == null)
                s_fiContainerData = typeof(CContainerSystem).GetField("m_dicData", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return s_fiContainerData != null ? (Dictionary<int, sContainerSystemData>)s_fiContainerData.GetValue(cs) : null;
        }

        private static List<int> GetPayListPackage(CPaySystem ps)
        {
            if (ps == null) return null;
            if (s_fiPayPkg == null)
                s_fiPayPkg = typeof(CPaySystem).GetField("m_listPackage", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            List<int> l = s_fiPayPkg != null ? (List<int>)s_fiPayPkg.GetValue(ps) : null;
            if (l == null && s_fiPayPkg != null) { l = new List<int>(); s_fiPayPkg.SetValue(ps, l); }
            return l;
        }

        private static List<int> GetPayDiamondPackage(CPaySystem ps)
        {
            if (ps == null) return null;
            if (s_fiPayDiamondPkg == null)
                s_fiPayDiamondPkg = typeof(CPaySystem).GetField("m_listDiamondPackage", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            List<int> l = s_fiPayDiamondPkg != null ? (List<int>)s_fiPayDiamondPkg.GetValue(ps) : null;
            if (l == null && s_fiPayDiamondPkg != null) { l = new List<int>(); s_fiPayDiamondPkg.SetValue(ps, l); }
            return l;
        }

        private static List<int> GetPayLimitGift(CPaySystem ps)
        {
            if (ps == null) return null;
            if (s_fiPayLimitGift == null)
                s_fiPayLimitGift = typeof(CPaySystem).GetField("m_listLimitGift", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            List<int> l = s_fiPayLimitGift != null ? (List<int>)s_fiPayLimitGift.GetValue(ps) : null;
            if (l == null && s_fiPayLimitGift != null) { l = new List<int>(); s_fiPayLimitGift.SetValue(ps, l); }
            return l;
        }

        private static Dictionary<int, int> GetDlcInstall(CDLCSystem dlc)
        {
            if (dlc == null) return null;
            if (s_fiDlcInstall == null)
                s_fiDlcInstall = typeof(CDLCSystem).GetField("m_dicDLCInstallDictionary", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Dictionary<int, int> d = s_fiDlcInstall != null ? (Dictionary<int, int>)s_fiDlcInstall.GetValue(dlc) : null;
            if (d == null && s_fiDlcInstall != null) { d = new Dictionary<int, int>(); s_fiDlcInstall.SetValue(dlc, d); }
            return d;
        }

        private static Dictionary<int, sHolidayImportantExchangeConfig> GetHolidayConfig(CHolidayImportantExchangeConfigLoader hol)
        {
            if (hol == null) return null;
            if (s_fiHolidayConfig == null)
                s_fiHolidayConfig = typeof(CHolidayImportantExchangeConfigLoader).GetField("m_dicConfig", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return s_fiHolidayConfig != null ? (Dictionary<int, sHolidayImportantExchangeConfig>)s_fiHolidayConfig.GetValue(hol) : null;
        }

        private static CCharacterStateBase GetCurState(CCharacterBase cb)
        {
            if (cb == null) return null;
            if (s_fiCurState == null)
                s_fiCurState = typeof(CCharacterBase).GetField("m_pCurState", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return s_fiCurState != null ? (CCharacterStateBase)s_fiCurState.GetValue(cb) : null;
        }

        private static CCharacterBehaviorBase GetBehavior(CCharacterStateBase cs)
        {
            if (cs == null) return null;
            if (s_fiBehavior == null)
                s_fiBehavior = typeof(CCharacterStateBase).GetField("m_pBehavior", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return s_fiBehavior != null ? (CCharacterBehaviorBase)s_fiBehavior.GetValue(cs) : null;
        }

        private static bool GetInputAttack(CMainCharacter mc)
        {
            if (mc == null) return false;
            if (s_fiInputAttack == null)
                s_fiInputAttack = typeof(CMainCharacter).GetField("m_bInputAttack", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return s_fiInputAttack != null ? (bool)s_fiInputAttack.GetValue(mc) : false;
        }

        private static bool IsJoystickCancelling(CUIJoystick j)
        {
            if (j == null) return false;
            if (s_fiJoyPointerDown == null)
                s_fiJoyPointerDown = typeof(CUIJoystick).GetField("m_bPointerDown", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (s_fiJoyEnterCancel == null)
                s_fiJoyEnterCancel = typeof(CUIJoystick).GetField("m_bEnterCancel", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            bool down = s_fiJoyPointerDown != null && (bool)s_fiJoyPointerDown.GetValue(j);
            bool cancel = s_fiJoyEnterCancel != null && (bool)s_fiJoyEnterCancel.GetValue(j);
            return down && cancel;
        }

        private static void SetBuffCurCycle(CBuff cb, int cycle)
        {
            if (cb == null) return;
            if (s_fiBuffCurCycle == null)
                s_fiBuffCurCycle = typeof(CBuff).GetField("m_nCurCycle", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (s_fiBuffCurCycle != null) s_fiBuffCurCycle.SetValue(cb, cycle);
        }

        private void TryNeutralizeAntiCheat()
        {
            try
            {
                if (CClientGlobal.Instance == null || CClientGlobal.Instance.GetGameDataCenter == null) return;
                IGameDataCenter dc = CClientGlobal.Instance.GetGameDataCenter;
                int[] blTypes = new int[] { 82, 83, 84, 85, 86 };
                for (int i = 0; i < blTypes.Length; i++)
                {
                    object loader = dc.GetConfigLoader((eGameConfigType)blTypes[i]);
                    if (loader != null)
                    {
                        FieldInfo fi = loader.GetType().GetField("m_dicConfig", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        if (fi != null)
                        {
                            System.Collections.IDictionary dict = fi.GetValue(loader) as System.Collections.IDictionary;
                            if (dict != null && dict.Count > 0) dict.Clear();
                        }
                    }
                }
            }
            catch { }
        }

        private bool _systemsHookAttempted = false;
        private static FieldInfo s_fiGsContSys;
        private static FieldInfo s_fiGsPaySys;
        private static FieldInfo s_fiTickerContUids;
        private static FieldInfo s_fiNetInstance;
        private static FieldInfo s_fiNetUrls;
        private static FieldInfo s_fiRafflePoolDic;
        private static FieldInfo s_fiRaffleTraderDic;
        private static uint s_curTickStatic = 0u;
        private static bool s_whForceDirty = true;

        internal static uint CurrentTick { get { return s_curTickStatic; } }
        internal static void MarkWarehouseDirty() { s_whForceDirty = true; }

        internal static bool IsInShelterNow(IGameSystem gs)
        {
            if (gs == null || gs.sceneSystem == null || CGameSceneMgr.Instance == null) return false;
            try
            {
                return gs.sceneSystem.GetCurrentShelterSceneID() == CGameSceneMgr.Instance.nSceneID;
            }
            catch { return false; }
        }

        private void EnsureModSystems(IGameSystem gs)
        {
            if (gs == null) return;
            try
            {
                if (gs.equipmentSystem != null && !(gs.equipmentSystem is ModEquipmentSystem))
                {
                    if (s_fiGsEquipSys == null)
                        s_fiGsEquipSys = gs.GetType().GetField("m_pEquipmentSystem", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (s_fiGsEquipSys != null)
                    {
                        s_fiGsEquipSys.SetValue(gs, new ModEquipmentSystem());
                        Debug.Log("[ModBridge] Installed ModEquipmentSystem O(1) ammo hook");
                    }
                }
                if (gs.containerSystem != null && !(gs.containerSystem is ModContainerSystem))
                {
                    if (s_fiGsContSys == null)
                        s_fiGsContSys = gs.GetType().GetField("m_pContainerSystem", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (s_fiGsContSys != null)
                    {
                        CContainerSystem origCs = gs.containerSystem as CContainerSystem;
                        if (origCs != null)
                        {
                            s_fiGsContSys.SetValue(gs, new ModContainerSystem(origCs));
                            Debug.Log("[ModBridge] Installed ModContainerSystem Bag infinite-stack hook");
                        }
                    }
                }
                if (gs.homeContainerSystem != null && !(gs.homeContainerSystem is ModHomeContainerSystem))
                {
                    if (s_fiGsHomeContSys == null)
                        s_fiGsHomeContSys = gs.GetType().GetField("m_pHomeContainerSystem", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (s_fiGsHomeContSys != null)
                    {
                        CHomeContainerSystem orig = gs.homeContainerSystem as CHomeContainerSystem;
                        if (orig != null)
                        {
                            s_fiGsHomeContSys.SetValue(gs, new ModHomeContainerSystem(orig));
                            Debug.Log("[ModBridge] Installed ModHomeContainerSystem infinite-stack hook");
                        }
                    }
                }
                if (gs.paySystem != null && !(gs.paySystem is ModPaySystem))
                {
                    if (s_fiGsPaySys == null)
                        s_fiGsPaySys = gs.GetType().GetField("m_pPaySystem", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (s_fiGsPaySys != null)
                    {
                        CPaySystem origPay = gs.paySystem as CPaySystem;
                        if (origPay != null)
                        {
                            s_fiGsPaySys.SetValue(gs, new ModPaySystem(origPay));
                            Debug.Log("[ModBridge] Installed ModPaySystem currency guard hook");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_systemsHookAttempted)
                {
                    _systemsHookAttempted = true;
                    Debug.LogWarning("[ModBridge] EnsureModSystems ex: " + ex.Message);
                }
            }
        }

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
        private GameObject _noclipGo = null;
        private bool _noclipActive = false;

        // Tool instant interact reflection field
        private static FieldInfo s_toolDataFi = null;

        // Local MP3 player state & caches
        private AndroidJavaObject _mediaPlayer = null;
        private string _playingMp3Path = null;
        private CPhonograph _cachedPh = null;
        private Component _cachedPhAnim = null;
        private uint _lastPhScanTick = 0u;
        private bool _phInjected = false;

        // Cached scene / runtime flags
        private bool _sewerHighlightApplied = false;
        private int _lastEventsCid = 0;

        // Machine Gun timer & joystick cache
        private float _nextMgFireTime = 0f;
        private CUIJoystick[] _cachedJoysticks = null;
        private uint _lastJoystickScanTick = 0u;

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

        // Persistent currency, Aimbot, ESP, NoShake, Talents & Force-Hire state
        private bool _currencyLoaded = false;

        // Login UID=7891 + Universal Save Compatibility state
        private bool _prevLogin7891Flag = false;
        private long _origLoginUid = 0L;
        private string _origLoginToken = string.Empty;
        private long _lastSyncedSaveUid = 0L;
        private uint _preHttpBusySeq = 0u;
        private uint _preHttpDoneSeq = 0u;
        private string _realLaunchUrl3 = null;
        private uint _lastLaunchSyncTick = 0u;
        private CLoginScene _cachedLoginScene = null;
        private object _boundOnlineBtn = null;
        private bool _boundOnlineState = false;
        private float _suppressBanUntil = 0f;
        private string _loginStateMsg = string.Empty;
        private string _lastLoginStateJson = null;

        // Map Teleport & Task Manager state
        private bool _mapTaskForceDirty = false;
        private string _lastMapStateJson = null;
        private string _lastTaskStateJson = null;
        private int _pendingTeleportScene = 0;
        private int _pendingTeleportSpawn = 0;
        private int _lanTeleportSceneId = 0;
        private int _lanTeleportSpawnId = 0;
        private int _lanTeleportStage = 0;
        private int _lanSpDelayFrames = 0;
        private int _lanSpNextSceneId = 0;
        private int _lanSpNextSpawnId = 0;
        private float _lanTeleportStart = 0f;
        private float _lanLocalReadyAt = 0f;
        private float _spTeleportStart = 0f;
        private bool _lanLobbyWaitHostInit = false;
        private Dictionary<int, List<int>> _taskPrereqCache = null;
        private Dictionary<int, int> _taskParentCache = null;
        private Dictionary<int, int> _taskDepthCache = null;
        private Dictionary<int, sSceneEventConfig> _canonEventsCache = null;
        private Dictionary<int, List<int>> _canonMirrorsCache = null;
        private uint _espSeq = 0u;
        private readonly float[] _espSortDist = new float[256];
        private readonly int[] _espSortIdx = new int[256];
        private readonly List<CEntity> _hostileCache = new List<CEntity>(64);
        private readonly List<int> _hostileTypeCache = new List<int>(64);
        private readonly List<float> _hostileDistCache = new List<float>(64);
        private readonly Dictionary<int, string> _unitNameCache = new Dictionary<int, string>(128);
        private readonly Dictionary<int, Vector4> _unitBoundsCache = new Dictionary<int, Vector4>(128);
        private uint _lastEspNeuDiagTick = 0u;
        private bool _prevEspNeuState = false;

        // Force-Hire, Story-Hire & Live-Tame state
        private sealed class TamedBackup
        {
            public CEntity ent;
            public int origCampId;
            public CTerritory origTerritory;
            public float origSearchR;
            public float origSearchRFight;
            public Dictionary<int, CFightObj.sCampNode> origDicCamp;
            public bool hadAiData;
            public eAIPartrolType origPartrolType;
            public Vector3 origQuitPos;
            public bool origPauseByGrid;
            public bool origActive;
            public List<MonoBehaviour> disabledActivators;
        }

        private readonly Dictionary<int, CEntity> _forcedSpawned = new Dictionary<int, CEntity>();
        private readonly Dictionary<int, object> _retainedUnitHandles = new Dictionary<int, object>();
        private readonly List<int> _forcedHireIds = new List<int>();
        private readonly Dictionary<int, CEntity> _modHireSpawned = new Dictionary<int, CEntity>();
        private readonly Dictionary<int, object> _retainedHireHandles = new Dictionary<int, object>();
        private readonly Dictionary<int, bool> _loggedStoryHireIds = new Dictionary<int, bool>();
        private object _hirePoolHandle = null;
        private bool _hirePoolLoaded = false;
        private readonly Dictionary<string, GameObject> _hirePoolTemplates = new Dictionary<string, GameObject>(64);
        private readonly Dictionary<ulong, TamedBackup> _tamedEntities = new Dictionary<ulong, TamedBackup>();
        private float _tameScanRadiusM = 25f;
        private readonly Dictionary<int, bool> _loggedUnitSpawnComps = new Dictionary<int, bool>();
        private bool _forceHireLoaded = false;
        private int _lastForceSceneId = -1;
        private bool _sceneTransitioning = false;
        private GameObject _allyTerritoryObj = null;
        private CTerritory _allyTerritory = null;
        private object _registeredEventSys = null;
        private SceneEventSink _sceneEventSink = null;
        [ThreadStatic] private static bool _inLogHook = false;
        private readonly Dictionary<int, string> _unitPrefabPathCache = new Dictionary<int, string>(512);
        private readonly Dictionary<string, string> _unitIconStemToPath = new Dictionary<string, string>(512);
        private readonly Dictionary<string, string> _unitNameToPath = new Dictionary<string, string>(512);
        private readonly Dictionary<string, string> _allPrefabStemToPath = new Dictionary<string, string>(4096);
        private readonly Dictionary<string, int> _allPrefabStemScore = new Dictionary<string, int>(4096);
        private static FieldInfo s_fiHireSpawnerNodes = null;
        private static FieldInfo s_fiHireNodeGo = null;
        private bool _unitPrefabCacheLoaded = false;
        private bool _yooManifestInit = false;
        private object _yooDefaultPkg = null;
        private readonly List<string> _yooUnitPaths = new List<string>(2048);
        private readonly Dictionary<string, object> _yooPathToPkgAsset = new Dictionary<string, object>(2048);
        private int _yooScanCursor = 0;
        private bool _yooScanDone = false;
        private int _unitPrefabDirtyAdds = 0;
        private static MethodInfo s_miYooLoadAssetInfo = null;
        private static MethodInfo s_miYooLoadAssetStr = null;
        private static MethodInfo s_miYooHandleRelease = null;
        private static PropertyInfo s_piYooHandleAsset = null;
        private static ConstructorInfo s_ciYooAssetInfo = null;

        // Entity Spawner, Cricket Duel & Global Random Scrambler state
        private const int CRICKET_CAMP_A = 99901;
        private const int CRICKET_CAMP_B = 99902;
        private readonly Dictionary<int, int> _unitCampCache = new Dictionary<int, int>(512);
        private bool _unitCampScanDone = false;
        private readonly List<int> _unitCampPendingIds = new List<int>(256);
        private int _unitCampPendingCursor = 0;
        private bool _dynUnitsExportedWithCamps = false;
        private readonly Dictionary<int, List<CEntity>> _spawnedCustomEntities = new Dictionary<int, List<CEntity>>();
        private readonly List<object> _sceneTempYooHandles = new List<object>(64);
        private CEntity _cricketFighterA = null;
        private CEntity _cricketFighterB = null;
        private string _cricketNameA = "";
        private string _cricketNameB = "";
        private bool _cricketAnnouncedEnd = false;
        private readonly Dictionary<ulong, bool> _scrambledProcessedUids = new Dictionary<ulong, bool>(256);
        private readonly Dictionary<int, bool> _scrambledReplacedGoIds = new Dictionary<int, bool>(256);
        private List<int> _randomHostilePool = null;
        private bool _prevRandomSpawnFlag = false;
        private uint _lastScrambleTick = 0u;
        private int _scrambleLogCount = 0;

        private uint _lastTalentSyncTick = 0u;
        private uint _lastHireSyncTick = 0u;
        private string _lastTalentStateJson = "";
        private string _lastHireStateJson = "";

        // Input / Aimbot & Rotation reflection caches (with recursion guards)
        private CUI_Game_Input_MainCharacter _hookedInputWnd = null;
        private Action<sUIInputData> _origInputCb = null;
        private Delegate[] _origInputList = null;
        private Action<sUIInputData> _wrappedInputCb = null;
        [ThreadStatic] private static bool _inInputHook = false;
        private bool _aimAttackHeld = false;
        private bool _aimAngleApplied = false;
        private int _aimSavedOrig206 = 0;
        private int _aimSavedOrig207 = 0;
        private CEntity _aimAnglePlayer = null;
        private bool _lanAimAngleApplied = false;
        private int _lanAimSavedOrig206 = 0;
        private int _lanAimSavedOrig207 = 0;
        private LAN.CLanEntity _lanAimAnglePlayer = null;
        private static MethodInfo s_miRotateImm = null;

        // NoShake reflection caches
        private object _hookedGameCamMgr = null;
        private FieldInfo _fiGameCamOnShake = null;
        private Action<int> _origGameCamShake = null;
        private Delegate[] _origShakeList = null;
        private Action<int> _wrappedGameCamShake = null;
        [ThreadStatic] private static bool _inShakeHook = false;
        private static FieldInfo s_fiCamMgrShaker = null;
        private static FieldInfo s_fiShakerPro = null;
        private static FieldInfo s_fiShakerLock = null;
        private static FieldInfo s_fiShakerPreType = null;
        private static FieldInfo s_fiProParent = null;
        private static FieldInfo s_fiProInfluences = null;
        private static FieldInfo s_fiProInfSum = null;
        private static FieldInfo s_fiProPositions = null;
        private static FieldInfo s_fiProCoros = null;
        private static FieldInfo s_fiProShakeCoro = null;
        private static FieldInfo s_fiProIsConst = null;
        private static FieldInfo s_fiProOrigRot = null;
        private bool _beforeRenderHooked = false;

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

        private sealed class SceneEventSink : BaseCore.IEventExecuteSink
        {
            private readonly ModTicker _owner;
            public SceneEventSink(ModTicker owner) { _owner = owner; }
            public void OnExecute(eMSG.EVENT eEvent, int dwSrcID, object sData)
            {
                if (_owner != null) _owner.OnGameSceneEvent(eEvent);
            }
        }

        private unsafe void OnUnityLogMessage(string condition, string stackTrace, LogType type)
        {
            if (_inLogHook || string.IsNullOrEmpty(condition)) return;
            if (condition.StartsWith("[Mod", StringComparison.Ordinal)) return;
            bool isErr = (type == LogType.Exception || type == LogType.Error)
                || condition.IndexOf("DOTWEEN", StringComparison.OrdinalIgnoreCase) >= 0
                || condition.IndexOf("NullReference", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isErr) return;

            _inLogHook = true;
            try
            {
                Debug.Log("[ModTrace] " + type + ": " + condition);
                if (!string.IsNullOrEmpty(stackTrace))
                {
                    string[] lines = stackTrace.Split('\n');
                    int lim = (lines.Length < 24) ? lines.Length : 24;
                    for (int i = 0; i < lim; i++)
                    {
                        string ln = lines[i].Trim();
                        if (ln.Length > 0) Debug.Log("[ModTrace]   " + ln);
                    }
                }
                string dir = ReadShmDynDir(Shm());
                if (!string.IsNullOrEmpty(dir))
                {
                    string p = dir + "/mod_unity_errors.txt";
                    FileInfo fi = File.Exists(p) ? new FileInfo(p) : null;
                    if (fi == null || fi.Length < 131072L)
                    {
                        string entry = DateTime.Now.ToString("HH:mm:ss.fff") + " [" + type + "] " + condition + "\n" + (stackTrace ?? "") + "\n---\n";
                        File.AppendAllText(p, entry, s_utf8NoBom);
                    }
                }
            }
            catch { }
            finally { _inLogHook = false; }
        }

        private void EnsureSceneEventSink()
        {
            try
            {
                IClientGlobal cg = CClientGlobal.Instance;
                var ev = (cg != null) ? cg.GetEventSystem : null;
                if (ev == null || object.ReferenceEquals(ev, _registeredEventSys)) return;
                if (_registeredEventSys != null && _sceneEventSink != null)
                {
                    try
                    {
                        var oldEv = _registeredEventSys as BaseCore.IEventEngine;
                        if (oldEv != null)
                        {
                            oldEv.UnSubExecoute(_sceneEventSink, eMSG.EVENT.Scene_Destroy_Start, 0);
                            oldEv.UnSubExecoute(_sceneEventSink, eMSG.EVENT.Scene_Destroy_Finish, 0);
                            oldEv.UnSubExecoute(_sceneEventSink, eMSG.EVENT.Scene_Init_Finish, 0);
                        }
                    }
                    catch { }
                }
                if (_sceneEventSink == null) _sceneEventSink = new SceneEventSink(this);
                ev.SubExecoute(_sceneEventSink, eMSG.EVENT.Scene_Destroy_Start, 0, "");
                ev.SubExecoute(_sceneEventSink, eMSG.EVENT.Scene_Destroy_Finish, 0, "");
                ev.SubExecoute(_sceneEventSink, eMSG.EVENT.Scene_Init_Finish, 0, "");
                _registeredEventSys = ev;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModBridge] EnsureSceneEventSink ex: " + ex.Message);
            }
        }

        private static void PruneBrokenLaunchNodes()
        {
            try
            {
                BindingFlags bfAny = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                FieldInfo fiList = typeof(CGameSceneMgr).GetField("m_listLaunchNode", bfAny);
                object listObj = (fiList != null) ? fiList.GetValue(null) : null;
                System.Collections.IEnumerable seq = listObj as System.Collections.IEnumerable;
                if (seq == null) return;

                Type listT = listObj.GetType();
                PropertyInfo piFirst = listT.GetProperty("First", bfAny);
                MethodInfo miRemove = null;
                MethodInfo[] ms = listT.GetMethods(bfAny);
                for (int i = 0; i < ms.Length; i++)
                {
                    if (ms[i].Name == "Remove")
                    {
                        ParameterInfo[] ps = ms[i].GetParameters();
                        if (ps.Length == 1 && ps[0].ParameterType.Name.StartsWith("LinkedListNode", StringComparison.Ordinal))
                        {
                            miRemove = ms[i];
                            break;
                        }
                    }
                }

                FieldInfo fiHandler = null;
                object curNode = (piFirst != null) ? piFirst.GetValue(listObj, null) : null;
                PropertyInfo piNext = null;
                PropertyInfo piVal = null;
                while (curNode != null)
                {
                    if (piNext == null) piNext = curNode.GetType().GetProperty("Next", bfAny);
                    if (piVal == null) piVal = curNode.GetType().GetProperty("Value", bfAny);
                    object nextNode = (piNext != null) ? piNext.GetValue(curNode, null) : null;
                    object val = (piVal != null) ? piVal.GetValue(curNode, null) : null;
                    if (val != null)
                    {
                        if (fiHandler == null) fiHandler = val.GetType().GetField("pHandler", bfAny);
                        IMonoLaunchHandler h = (fiHandler != null) ? (fiHandler.GetValue(val) as IMonoLaunchHandler) : null;
                        UnityEngine.Object uo = h as UnityEngine.Object;
                        bool isDead = (h == null) || (uo == null);
                        if (isDead && miRemove != null)
                        {
                            try { miRemove.Invoke(listObj, new object[] { curNode }); } catch { }
                        }
                        else if (h != null)
                        {
                            // Prevent native CHireSpwaner dogs from crashing DestroyScene when animationPart is null
                            if (h is CAIFSM_GuYong_Dog_Out || h is CAIFSM_GuYong_Dog_Home)
                            {
                                MonoBehaviour mb = h as MonoBehaviour;
                                if (mb != null && mb.gameObject != null)
                                    EnsureDogSafeComponents(mb.gameObject, mb.GetComponent<CEntity>());
                            }
                            else if (h is CFSMPart)
                            {
                                MonoBehaviour mb = h as MonoBehaviour;
                                if (mb != null && mb.GetComponent<ModForceAllyFollower>() != null)
                                {
                                    ClearFsmPartStates((CFSMPart)h);
                                    if (miRemove != null) { try { miRemove.Invoke(listObj, new object[] { curNode }); } catch { } }
                                }
                            }
                        }
                    }
                    curNode = nextNode;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModBridge] PruneBrokenLaunchNodes ex: " + ex.Message);
            }
        }

        private void OnGameSceneEvent(eMSG.EVENT eEvent)
        {
            try
            {
                if (eEvent == eMSG.EVENT.Scene_Destroy_Start)
                {
                    _sceneTransitioning = true;
                    int hCnt = -1;
                    try { if (CGameSystem.Instance != null && CGameSystem.Instance.hireSystem != null) hCnt = CGameSystem.Instance.hireSystem.GetAllHireData().Count; } catch { }
                    Debug.Log("[ModBridge] Scene_Destroy_Start forced=" + _forcedSpawned.Count + " modHires=" + _modHireSpawned.Count + " hires=" + hCnt);
                    DespawnAllForcedAllies(false);
                    DespawnAllModHires(false);
                    DespawnAllCustomEntities(false);
                    ClearCricketArena(false);
                    _tamedEntities.Clear();
                    _scrambledProcessedUids.Clear();
                    _scrambledReplacedGoIds.Clear();
                    _scrambleLogCount = 0;
                    PruneBrokenLaunchNodes();
                }
                else if (eEvent == eMSG.EVENT.Scene_Destroy_Finish)
                {
                    Debug.Log("[ModBridge] Scene_Destroy_Finish retained=" + _retainedUnitHandles.Count + " retainedHire=" + _retainedHireHandles.Count + " temp=" + _sceneTempYooHandles.Count);
                    foreach (KeyValuePair<int, object> kv in _retainedUnitHandles) ReleaseYooHandle(kv.Value);
                    _retainedUnitHandles.Clear();
                    foreach (KeyValuePair<int, object> kv in _retainedHireHandles) ReleaseYooHandle(kv.Value);
                    _retainedHireHandles.Clear();
                    ReleaseTempYooHandles();
                }
                else if (eEvent == eMSG.EVENT.Scene_Init_Finish)
                {
                    _sceneTransitioning = false;
                    _spTeleportStart = 0f;
                    int sid = (CGameSceneMgr.Instance != null) ? CGameSceneMgr.Instance.nSceneID : -1;
                    _lastForceSceneId = -1;
                    _lastHireStateJson = null;
                    _mapTaskForceDirty = true;
                    HandlePostTeleportSpawn(sid);
                    Debug.Log("[ModBridge] Scene_Init_Finish scene=" + sid);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModBridge] OnGameSceneEvent ex: " + ex.Message);
            }
        }

        private void Awake()
        {
            s_instance = this;
            Application.logMessageReceived += OnUnityLogMessage;
            if (!_beforeRenderHooked)
            {
                _beforeRenderHooked = true;
                Application.onBeforeRender += OnBeforeRenderMod;
            }
            Debug.Log("[ModBridge] ModTicker Awake");
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnUnityLogMessage;
            try
            {
                var oldEv = _registeredEventSys as BaseCore.IEventEngine;
                if (oldEv != null && _sceneEventSink != null)
                {
                    oldEv.UnSubExecoute(_sceneEventSink, eMSG.EVENT.Scene_Destroy_Start, 0);
                    oldEv.UnSubExecoute(_sceneEventSink, eMSG.EVENT.Scene_Destroy_Finish, 0);
                    oldEv.UnSubExecoute(_sceneEventSink, eMSG.EVENT.Scene_Init_Finish, 0);
                }
            }
            catch { }
            _registeredEventSys = null;
            DespawnAllForcedAllies(true);
            DespawnAllModHires(true);
            _hirePoolTemplates.Clear();
            if (_hirePoolHandle != null)
            {
                ReleaseYooHandle(_hirePoolHandle);
                _hirePoolHandle = null;
            }
            _hirePoolLoaded = false;
            _tamedEntities.Clear();
            if (_beforeRenderHooked)
            {
                _beforeRenderHooked = false;
                Application.onBeforeRender -= OnBeforeRenderMod;
            }
            RestoreInputHook();
            RestoreGameCamShakeHook();
            StopLocalMusic();
        }

        private unsafe void OnBeforeRenderMod()
        {
            byte* b = Shm();
            if (b == null) return;
            uint flags = *(uint*)(b + 0x18);
            if ((flags & (1u << 14)) != 0u)
            {
                EnforceStoryNoShake();
            }
        }

        private unsafe void LateUpdate()
        {
            byte* b = Shm();
            if (b == null) return;
            uint flags = *(uint*)(b + 0x18);
            if ((flags & (1u << 14)) != 0u)
            {
                EnforceStoryNoShake();
            }
            UpdateEspOverlay(b, flags);
        }

        private unsafe void Update()
        {
            byte* b = Shm();
            if (b == null) return;

            _tick++;
            s_curTickStatic = _tick;
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
            EnsureSceneEventSink();
            if (gs != null)
            {
                EnsureModSystems(gs);
            }
            TickPendingLanTeleport(gs);
            MaintainPersistedCurrency(b, gs);
            MaintainLogin7891AndSaveCompat(b, flags, gs, mgr != null && mgr.GetCurrentChararcterCustomID() != 0);
            if (!PreHandleLoginOrLaunchHttp(b, flags, gs))
            {
                PollHttpBridge(b, gs);
            }
            int cid = 0;
            CEntity player = null;
            LAN.CLanEntity lanPlayer = null;

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
            if (player == null)
            {
                try
                {
                    lanPlayer = LAN.CLanMainCharacter.localPlayer;
                    if (lanPlayer != null)
                    {
                        if (cid == 0) cid = LAN.CLanMainCharacter.nCustomID;
                        if (cid == 0) cid = lanPlayer.nCustomID;
                    }
                }
                catch { }
            }

            bool sceneReady = ((player != null || lanPlayer != null) && cid != 0);
            *(int*)(b + 0x10) = sceneReady ? 1 : 0;
            *(int*)(b + 0x14) = cid;

            if (cid != 0 && cid != _lastCid)
            {
                _lastCid = cid;
                Debug.Log("[ModBridge] Scene player ready cid=" + cid.ToString() + " lan=" + (lanPlayer != null).ToString());
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
            MaintainInstantInteract((flags & 16u) != 0, player, lanPlayer);
            MaintainSewerHighlight((flags & 256u) != 0);
            MaintainAlwaysEvents((flags & 512u) != 0);
            MaintainPhonograph((flags & 1024u) != 0);

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
                int res = ExecuteCmd(ctype, arg1, arg2, arg3, argL, gs, cid, player, lanPlayer);
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

            if ((flags & (1u << 14)) != 0u)
            {
                EnsureGameCamShakeHook();
                EnforceStoryNoShake();
            }
            else if (_hookedGameCamMgr != null)
            {
                RestoreGameCamShakeHook();
            }

            if (sceneReady)
            {
                if ((_tick % 120u) == 20u)
                {
                    TryNeutralizeAntiCheat();
                }

                if (player != null)
                {
                    EnforcePropLocks(player);
                    MaintainBuffs(player);
                    MaintainNoclip(player.gameObject, (flags & 64u) != 0);
                    try { StepUnitPrefabIndex(b, 16); } catch { }
                    try { MaintainForcedHires(b, gs, cid, player); } catch { }
                    try { MaintainStoryHires(gs, player, false); } catch { }
                    try { TickCricketDuelStatus(); } catch { }
                    try { StepRandomEnemyScrambler(gs, cid, player, (flags & (1u << 21)) != 0u); } catch { }
                    bool aimOn = (flags & (1u << 12)) != 0u;
                    try { MaintainAimbotWeaponAngles(player, aimOn); } catch { }
                    if (aimOn)
                    {
                        try
                        {
                            EnsureInputHook();
                            MaintainAimbot(b, player, flags);
                        }
                        catch { }
                    }
                    else
                    {
                        _aimAttackHeld = false;
                        RestoreInputHook();
                    }
                    if ((flags & 128u) != 0)
                    {
                        MaintainMachineGun(player, mgRateX10);
                    }
                }
                else if (lanPlayer != null)
                {
                    EnforcePropLocksLAN(lanPlayer);
                    MaintainBuffsLAN(lanPlayer);
                    MaintainNoclip(lanPlayer.gameObject, (flags & 64u) != 0);
                    bool aimOn = (flags & (1u << 12)) != 0u;
                    try { MaintainAimbotWeaponAnglesLAN(lanPlayer, aimOn); } catch { }
                    if (aimOn)
                    {
                        try
                        {
                            EnsureInputHook();
                            MaintainAimbotLAN(b, lanPlayer, flags);
                        }
                        catch { }
                    }
                    else
                    {
                        _aimAttackHeld = false;
                        RestoreInputHook();
                    }
                    if ((flags & 128u) != 0)
                    {
                        MaintainMachineGunLAN(lanPlayer, mgRateX10);
                    }
                }

                // Auto-restore durability: Equipment every 60 ticks, full Bag only on toggle-ON or every 600 ticks
                if ((flags & 4u) != 0)
                {
                    if ((_prevFlags & 4u) == 0 || (_tick % 600u) == 0u)
                    {
                        RestoreEndurance(gs, cid, true, false);
                    }
                    else if ((_tick % 60u) == 0u)
                    {
                        RestoreEndurance(gs, cid, false, false);
                    }
                }
                // Merge warehouse containers + shelter Bag when InfStack turns ON, enters save/shelter, or periodically
                if ((flags & 32u) != 0)
                {
                    if ((_prevFlags & 32u) == 0 || _lastInfStackCid != cid)
                    {
                        _lastInfStackCid = cid;
                        MergeInfStackAll(gs, cid, false);
                    }
                    else if ((_tick % 60u) == 30u)
                    {
                        MergeInfStackAll(gs, cid, true);
                    }
                }
                else
                {
                    _lastInfStackCid = 0;
                }
                // Periodic snapshot (every 12 frames ~ 0.2-0.4s)
                if (processed > 0 || (_tick % 12u) == 1u)
                {
                    if (player != null) UpdateSnapshot(player);
                    else if (lanPlayer != null) UpdateSnapshotLAN(lanPlayer);
                }

                // Periodic export of dynamic warehouse, currency, talents/skills, hire, maps & tasks state
                if (_mapTaskForceDirty || (_tick % 60u) == 12u)
                {
                    ExportDynWarehouseAll(gs, false);
                    ExportDynCurrencyFast(gs);
                    ExportDynTalentState(gs, cid, false);
                    ExportDynHireState(gs, cid, false);
                    ExportDynMapState(gs, _mapTaskForceDirty);
                    ExportDynTaskState(gs, cid, _mapTaskForceDirty);
                    ExportDynLoginState(b, _mapTaskForceDirty);
                    _mapTaskForceDirty = false;
                }
            }
            else if (_noclipActive)
            {
                MaintainNoclip(null, false);
            }
            if (!sceneReady && (_tick % 60u) == 15u)
            {
                ExportDynLoginState(b, false);
            }
            _prevFlags = flags;

            if (_tick == 1u || (_tick % 300u) == 0u)
            {
                Debug.Log("[ModBridge] Heartbeat tick=" + _tick.ToString() + " cid=" + cid.ToString() + " flags=" + flags.ToString());
            }
        }

        private unsafe int ExecuteCmd(int ctype, int arg1, int arg2, int arg3, long argL, IGameSystem gs, int cid, CEntity player, LAN.CLanEntity lanPlayer)
        {
            try
            {
                if (ctype == 10) // MOD_CMD_SYNC_DYN_ASSETS (runs even on login screen)
                {
                    return TryExportDynConfigs(Shm(), true) ? 1 : -1;
                }
                if (ctype == 17) // MOD_CMD_LOGIN_7891_SYNC (runs even on login screen)
                {
                    return CmdLogin7891Sync(Shm(), arg1, arg2);
                }
                if (ctype == 12 && gs != null && gs.paySystem != null) // MOD_CMD_SET_CURRENCY works at cid==0 too
                {
                    return CmdSetCurrencyV2(gs, arg1, arg2);
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
                        return CmdSetProp(player, lanPlayer, arg1, arg2, arg3);
                    case 3: // MOD_CMD_UNLOCK_ALL_BP
                        return CmdUnlockAllBP(gs, cid);
                    case 4: // MOD_CMD_APPLY_BUFF (arg1=buffId, arg2=durationSec)
                        return CmdApplyBuff(player, lanPlayer, arg1, arg2);
                    case 5: // MOD_CMD_REMOVE_BUFF (arg1=buffId, 0=all)
                        return CmdRemoveBuff(player, lanPlayer, arg1);
                    case 6: // MOD_CMD_RESTORE_ENDURANCE
                        return CmdRestoreEndurance(gs, cid);
                    case 7: // MOD_CMD_REFRESH_SNAP
                        if (player != null) UpdateSnapshot(player);
                        else if (lanPlayer != null) UpdateSnapshotLAN(lanPlayer);
                        ExportDynWarehouseAll(gs, true);
                        ExportDynCurrencyFast(gs);
                        ExportDynTalentState(gs, cid, true);
                        ExportDynHireState(gs, cid, true);
                        ExportDynMapState(gs, true);
                        ExportDynTaskState(gs, cid, true);
                        ExportDynLoginState(Shm(), true);
                        SetStatus(1, "Snapshot refreshed");
                        return 1;
                    case 8: // MOD_CMD_CLEAR_PROP_LOCKS
                        _lockCount = 0;
                        if (player != null) UpdateSnapshot(player);
                        else if (lanPlayer != null) UpdateSnapshotLAN(lanPlayer);
                        SetStatus(1, "Property locks cleared");
                        return 1;
                    case 9: // MOD_CMD_UNLOCK_ALL_PAID (arg1=1 apply, 0 restore)
                        if (arg1 != 0) return ApplyPaidUnlock(gs, cid, player);
                        return RestorePaidUnlock(gs);
                    case 11: // MOD_CMD_DEL_WAREHOUSE_ITEM (arg1=count, arg2=boxId, argL=matUID)
                        return CmdDeleteWarehouseItemV2(gs, cid, arg1, (long)arg2, (ulong)argL);
                    case 12: // MOD_CMD_SET_CURRENCY (arg1=type, arg2=val)
                        return CmdSetCurrencyV2(gs, arg1, arg2);
                    case 13: // MOD_CMD_TALENT_OP (arg1=id, arg2=subOp, arg3=level, argL=targetCustomId)
                        return CmdTalentOp(gs, cid, arg1, arg2, arg3, (int)argL);
                    case 14: // MOD_CMD_HIRE_OP (arg1=id, arg2=hireOp, arg3=subArg, argL=entityUid)
                        return CmdHireOp(gs, cid, player, arg1, arg2, arg3, (ulong)argL);
                    case 15: // MOD_CMD_TELEPORT_SCENE (arg1=sceneId, arg2=spawnPointId, arg3=flags)
                        return CmdTeleportScene(gs, player, arg1, arg2, arg3);
                    case 16: // MOD_CMD_TASK_OP (arg1=subOp, arg2=eventOrTaskId, arg3=prereqFlag, argL=sceneId)
                        return CmdTaskOp(gs, cid, arg1, arg2, arg3, (int)argL);
                    case 18: // MOD_CMD_SPAWN_ENTITY (arg1=subOp, arg2=unitId, arg3=factionOverride)
                        return CmdSpawnEntity(gs, player, arg1, arg2, arg3);
                    case 19: // MOD_CMD_CRICKET_DUEL (arg1=subOp, arg2=unitIdA, arg3=unitIdB)
                        return CmdCricketDuel(gs, player, arg1, arg2, arg3);
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
            return nHomeItemId == 120045 || nHomeItemId == 120085 || nHomeItemId == 120100 ||
                   nHomeItemId == 120135 || nHomeItemId == 120215 || nHomeItemId == 120300;
        }

        private static bool IsStorageBoxPlacement(HomeItemPlacementData p)
        {
            if (p == null || p.lInstanceId <= 0) return false;
            if (IsStorageBoxHomeItem(p.nHomeItemId)) return true;
            try
            {
                CHomeItemSceneRoot root = CHomeItemSceneRoot.CurrentRoot;
                if (root != null)
                {
                    CHomeItemView view = root.GetView(p.lInstanceId);
                    if (view != null && view.GetComponent(typeof(IHomeContainerFurniture)) != null)
                        return true;
                }
            }
            catch { }
            return false;
        }

        internal static void RefreshOpenStorageBoxUi()
        {
            try
            {
                CUIBase boxUI = (CUIMgr.Instance != null) ? CUIMgr.Instance.GetWnd(CUIDefine.eUIWnd.UI_Game_Home_StorageBox) : null;
                if (boxUI != null && boxUI.IsVisable())
                {
                    Type uiType = boxUI.GetType();
                    if (s_cachedStorageBoxType != uiType || s_miStorageBoxRefresh == null)
                    {
                        s_cachedStorageBoxType = uiType;
                        s_miStorageBoxRefresh = uiType.GetMethod("RefreshStorageBox", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    }
                    if (s_miStorageBoxRefresh != null) s_miStorageBoxRefresh.Invoke(boxUI, null);
                }
            }
            catch { }
        }

        internal static void NotifyWarehouseChanged(IGameSystem gs)
        {
            s_whForceDirty = true;
            if (s_instance != null && gs != null)
            {
                s_instance.ExportDynWarehouseAll(gs, true);
            }
        }

        // ==================== #6/Perf/Bag+Box V2 Implementations ====================
        private static int _lastMergeBagCid = 0;
        private static int _lastMergeBagUidsCount = -1;
        private static long _lastMergeBoxSig = -1L;

        private static long _lastWhCheckSig = -1L;
        private static string _lastWhJsonHash = "";

        private uint _activeHttpSeq = 0u;
        private uint _lastDoneHttpSeq = 0u;
        private string _realGameUrl1 = null;
        private string _realDbUrl2 = null;
        private string _localProxyUrl = null;
        private int _raffleCountAll = 0;
        private int _raffleTopSignCount = 0;

        private sealed class WhKey : IEquatable<WhKey>
        {
            public long Box;
            public int CfgId;
            public int End;
            public int Refit;

            public bool Equals(WhKey o)
            {
                if (ReferenceEquals(o, null)) return false;
                return Box == o.Box && CfgId == o.CfgId && End == o.End && Refit == o.Refit;
            }

            public override bool Equals(object o)
            {
                return Equals(o as WhKey);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = (int)(Box ^ (Box >> 32));
                    h = (h * 397) ^ CfgId;
                    h = (h * 397) ^ End;
                    h = (h * 397) ^ Refit;
                    return h;
                }
            }
        }

        private sealed class WhAgg
        {
            public long Box;
            public int CfgId;
            public int End;
            public int MaxEnd;
            public int Refit;
            public ulong PrimaryUid;
            public long TotalNum;
            public int Slots;
        }

        private static List<ulong> GetContainerUidsDirect(CContainer c)
        {
            if (c == null) return null;
            if (s_fiTickerContUids == null)
                s_fiTickerContUids = typeof(CContainer).GetField("m_listUIDs", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return (s_fiTickerContUids != null) ? (s_fiTickerContUids.GetValue(c) as List<ulong>) : null;
        }

        private static CContainer GetBagContainer(IGameSystem gs, int cid)
        {
            if (gs == null || gs.containerSystem == null || cid == 0) return null;
            CContainerSystem cs = gs.containerSystem as CContainerSystem;
            var dicData = GetContainerData(cs);
            sContainerSystemData sdata = null;
            CContainer cont = null;
            if (dicData != null && dicData.TryGetValue(cid, out sdata) && sdata != null)
            {
                sdata.dicContainer.TryGetValue(eContainerTypeEnum.Bag, out cont);
            }
            return cont;
        }

        private void MergeInfStackAll(IGameSystem gs, int cid, bool silentIfUnchanged)
        {
            if (gs == null || gs.materialSystem == null) return;

            bool anyBoxChanged = false;
            int totalBoxMerged = 0;
            long boxSig = 0L;
            CHomeContainerSystem hcs = gs.homeContainerSystem as CHomeContainerSystem;
            var containers = GetHomeContainers(hcs);
            if (containers != null && containers.Count > 0)
            {
                foreach (CHomeContainer hc in containers.Values)
                {
                    if (hc != null) boxSig = (boxSig * 31L) + hc.GetSlotCount();
                }
                if (!silentIfUnchanged || s_whForceDirty || boxSig != _lastMergeBoxSig)
                {
                    foreach (CHomeContainer hc in containers.Values)
                    {
                        if (hc == null) continue;
                        int before = hc.GetSlotCount();
                        if (MergeSingleHomeContainer(gs, hc))
                        {
                            anyBoxChanged = true;
                            int after = hc.GetSlotCount();
                            if (before > after) totalBoxMerged += (before - after);
                        }
                    }
                    long newBoxSig = 0L;
                    foreach (CHomeContainer hc in containers.Values)
                    {
                        if (hc != null) newBoxSig = (newBoxSig * 31L) + hc.GetSlotCount();
                    }
                    _lastMergeBoxSig = newBoxSig;
                }
            }
            else
            {
                _lastMergeBoxSig = 0L;
            }

            int bagMerged = 0;
            if (cid != 0 && IsInShelterNow(gs))
            {
                CContainer bag = GetBagContainer(gs, cid);
                List<ulong> uids = GetContainerUidsDirect(bag);
                if (bag != null && uids != null)
                {
                    if (!silentIfUnchanged || s_whForceDirty || _lastMergeBagCid != cid || uids.Count != _lastMergeBagUidsCount)
                    {
                        bagMerged = MergeBagContainerFast(gs, cid, bag, uids);
                        _lastMergeBagCid = cid;
                        _lastMergeBagUidsCount = uids.Count;
                    }
                }
            }

            if (anyBoxChanged || bagMerged > 0)
            {
                s_whForceDirty = true;
                if (anyBoxChanged) RefreshOpenStorageBoxUi();
                if (bagMerged > 0)
                {
                    try
                    {
                        if (CClientGlobal.Instance != null && CClientGlobal.Instance.GetEventSystem != null)
                        {
                            eMSG.sContainer_Change ev = default(eMSG.sContainer_Change);
                            ev.nCustomID = cid;
                            ev.eContainer = eContainerTypeEnum.Bag;
                            ev.nMaterialConfigID = 0;
                            CClientGlobal.Instance.GetEventSystem.FireExecoute(eMSG.EVENT.Container_Change, 0, ev);
                        }
                    }
                    catch { }
                }
                ExportDynWarehouseAll(gs, true);
                if (!silentIfUnchanged)
                {
                    SetStatus(1, "仓库无限堆叠已合并 " + (totalBoxMerged + bagMerged).ToString() + " 个重复槽位");
                }
            }
        }

        private int MergeBagContainerFast(IGameSystem gs, int cid, CContainer bag, List<ulong> uids)
        {
            if (uids == null || uids.Count <= 1) return 0;

            Dictionary<ulong, bool> protectedDbids = null;
            try
            {
                if (gs.characterSystem != null)
                {
                    CCharacterSystemData cdata = gs.characterSystem.GetCharacterSystemData(cid);
                    if (cdata != null)
                    {
                        protectedDbids = new Dictionary<ulong, bool>();
                        if (cdata.nMountDBID != 0UL) protectedDbids[cdata.nMountDBID] = true;
                        if (cdata.listQuicklyShortcutDBID != null)
                        {
                            for (int q = 0; q < cdata.listQuicklyShortcutDBID.Count; q++)
                            {
                                if (cdata.listQuicklyShortcutDBID[q] != 0UL)
                                    protectedDbids[cdata.listQuicklyShortcutDBID[q]] = true;
                            }
                        }
                    }
                }
            }
            catch { }

            IRefitSystem refit = gs.refitSystem;
            Dictionary<long, CMaterial> firstByKey = new Dictionary<long, CMaterial>(256);
            List<ulong> kept = new List<ulong>(uids.Count);
            List<ulong> toDestroy = new List<ulong>(256);

            for (int i = 0; i < uids.Count; i++)
            {
                ulong uid = uids[i];
                CMaterial m = gs.materialSystem.GetMaterialByUID(uid);
                if (m == null || m.nNumber <= 0)
                {
                    if (m != null) toDestroy.Add(uid);
                    continue;
                }
                if (refit != null && refit.IsRefit(uid))
                {
                    kept.Add(uid);
                    continue;
                }
                // Keep mounted / quick-shortcut items unmerged as their primary slot if needed
                long key = (((long)m.nConfigID) << 32) ^ (long)((uint)m.nEndurance);
                CMaterial existing = null;
                if (firstByKey.TryGetValue(key, out existing) && existing != null)
                {
                    bool curProtected = (protectedDbids != null && protectedDbids.ContainsKey(m.DBID));
                    bool exProtected = (protectedDbids != null && protectedDbids.ContainsKey(existing.DBID));
                    if (curProtected && !exProtected)
                    {
                        // Leave shortcut slot distinct rather than destroying its DBID
                        kept.Add(uid);
                        firstByKey[key] = m;
                        continue;
                    }
                    long tot = (long)existing.nNumber + (long)m.nNumber;
                    if (tot <= (long)int.MaxValue)
                    {
                        existing.SetNum((int)tot, false);
                        toDestroy.Add(uid);
                        continue;
                    }
                    firstByKey[key] = m;
                }
                else
                {
                    firstByKey[key] = m;
                }
                kept.Add(uid);
            }

            if (toDestroy.Count == 0 && kept.Count == uids.Count) return 0;

            for (int i = 0; i < toDestroy.Count; i++)
            {
                try { gs.materialSystem.DestroyMaterial(toDestroy[i], true); } catch { }
            }
            uids.Clear();
            uids.AddRange(kept);
            ModContainerSystem.MarkSaveTag(bag);
            return toDestroy.Count;
        }

        private unsafe void ExportDynWarehouseAll(IGameSystem gs, bool force)
        {
            if (gs == null || gs.materialSystem == null) return;
            try
            {
                byte* b = Shm();
                if (b == null) return;
                string dir = ReadShmDynDir(b);
                if (string.IsNullOrEmpty(dir)) return;

                int cid = *(int*)(b + 0x14);
                CContainer bag = GetBagContainer(gs, cid);
                List<ulong> bagUids = GetContainerUidsDirect(bag);
                int bagCnt = (bagUids != null) ? bagUids.Count : 0;
                ulong bFirst = (bagCnt > 0) ? bagUids[0] : 0UL;
                ulong bLast = (bagCnt > 0) ? bagUids[bagCnt - 1] : 0UL;

                CHomeContainerSystem hcs = gs.homeContainerSystem as CHomeContainerSystem;
                var containers = GetHomeContainers(hcs);
                int boxCount = (containers != null) ? containers.Count : 0;
                long boxSlots = 0L;
                if (containers != null)
                {
                    foreach (CHomeContainer hc in containers.Values)
                    {
                        if (hc != null) boxSlots += hc.GetSlotCount();
                    }
                }

                long sig = ((long)cid * 397L) ^ ((long)bagCnt << 16) ^ (long)(bFirst ^ bLast) ^ (boxSlots << 8) ^ boxCount;
                string outPath = dir + "/dyn_warehouse.json";
                if (!force && !s_whForceDirty && sig == _lastWhCheckSig && File.Exists(outPath))
                {
                    return;
                }
                s_whForceDirty = false;
                _lastWhCheckSig = sig;

                IRefitSystem refit = gs.refitSystem;
                Dictionary<WhKey, WhAgg> aggs = new Dictionary<WhKey, WhAgg>(512);
                List<WhAgg> ordered = new List<WhAgg>(512);

                if (bagUids != null)
                {
                    for (int i = 0; i < bagUids.Count; i++)
                    {
                        ulong uid = bagUids[i];
                        CMaterial m = gs.materialSystem.GetMaterialByUID(uid);
                        if (m == null || m.nNumber <= 0) continue;
                        int isRefit = (refit != null && refit.IsRefit(uid)) ? 1 : 0;
                        WhKey k = new WhKey { Box = 0L, CfgId = m.nConfigID, End = m.nEndurance, Refit = isRefit };
                        WhAgg a = null;
                        if (aggs.TryGetValue(k, out a) && isRefit == 0)
                        {
                            a.TotalNum += m.nNumber;
                            a.Slots++;
                        }
                        else if (isRefit != 0)
                        {
                            WhAgg rAgg = new WhAgg
                            {
                                Box = 0L, CfgId = m.nConfigID, End = m.nEndurance,
                                MaxEnd = (m.pConfig != null) ? m.pConfig.nEndurance : 0,
                                Refit = 1, PrimaryUid = uid, TotalNum = m.nNumber, Slots = 1
                            };
                            ordered.Add(rAgg);
                        }
                        else
                        {
                            a = new WhAgg
                            {
                                Box = 0L, CfgId = m.nConfigID, End = m.nEndurance,
                                MaxEnd = (m.pConfig != null) ? m.pConfig.nEndurance : 0,
                                Refit = 0, PrimaryUid = uid, TotalNum = m.nNumber, Slots = 1
                            };
                            aggs[k] = a;
                            ordered.Add(a);
                        }
                    }
                }

                // Update box snapshots in Shm (box 0 = Shelter Bag, boxes 1..15 = CHomeContainer boxes)
                int snapIdx = 0;
                if (bagCnt > 0 || cid != 0)
                {
                    byte* slot0 = b + 0x950;
                    *(long*)(slot0 + 0) = 0L;
                    *(int*)(slot0 + 8) = Math.Max(bagCnt, 99999);
                    *(int*)(slot0 + 12) = bagCnt;
                    snapIdx = 1;
                }

                if (containers != null)
                {
                    foreach (KeyValuePair<long, CHomeContainer> kv in containers)
                    {
                        long boxId = kv.Key;
                        CHomeContainer hc = kv.Value;
                        if (hc == null) continue;
                        int cnt = hc.GetSlotCount();
                        if (snapIdx < 16)
                        {
                            byte* slot = b + 0x950 + snapIdx * 16;
                            *(long*)(slot + 0) = boxId;
                            *(int*)(slot + 8) = 40;
                            *(int*)(slot + 12) = cnt;
                            snapIdx++;
                        }
                        for (int i = 0; i < cnt; i++)
                        {
                            ulong uid = hc.GetMaterialUID(i);
                            CMaterial m = gs.materialSystem.GetMaterialByUID(uid);
                            if (m == null || m.nNumber <= 0) continue;
                            int isRefit = (refit != null && refit.IsRefit(uid)) ? 1 : 0;
                            WhKey k = new WhKey { Box = boxId, CfgId = m.nConfigID, End = m.nEndurance, Refit = isRefit };
                            WhAgg a = null;
                            if (isRefit == 0 && aggs.TryGetValue(k, out a))
                            {
                                a.TotalNum += m.nNumber;
                                a.Slots++;
                            }
                            else if (isRefit != 0)
                            {
                                ordered.Add(new WhAgg
                                {
                                    Box = boxId, CfgId = m.nConfigID, End = m.nEndurance,
                                    MaxEnd = (m.pConfig != null) ? m.pConfig.nEndurance : 0,
                                    Refit = 1, PrimaryUid = uid, TotalNum = m.nNumber, Slots = 1
                                });
                            }
                            else
                            {
                                a = new WhAgg
                                {
                                    Box = boxId, CfgId = m.nConfigID, End = m.nEndurance,
                                    MaxEnd = (m.pConfig != null) ? m.pConfig.nEndurance : 0,
                                    Refit = 0, PrimaryUid = uid, TotalNum = m.nNumber, Slots = 1
                                };
                                aggs[k] = a;
                                ordered.Add(a);
                            }
                        }
                    }
                }
                *(int*)(b + 0x948) = snapIdx;

                ordered.Sort(delegate(WhAgg x, WhAgg y)
                {
                    int c = x.Box.CompareTo(y.Box);
                    if (c != 0) return c;
                    c = x.CfgId.CompareTo(y.CfgId);
                    if (c != 0) return c;
                    c = y.End.CompareTo(x.End);
                    if (c != 0) return c;
                    return x.PrimaryUid.CompareTo(y.PrimaryUid);
                });

                StringBuilder sb = new StringBuilder(ordered.Count * 76 + 16);
                sb.Append('[');
                for (int i = 0; i < ordered.Count; i++)
                {
                    WhAgg it = ordered[i];
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"uid\":\"").Append(it.PrimaryUid.ToString())
                      .Append("\",\"box\":").Append(it.Box)
                      .Append(",\"id\":").Append(it.CfgId)
                      .Append(",\"num\":").Append(it.TotalNum)
                      .Append(",\"end\":").Append(it.End)
                      .Append(",\"maxEnd\":").Append(it.MaxEnd)
                      .Append(",\"refit\":").Append(it.Refit)
                      .Append(",\"slots\":").Append(it.Slots).Append('}');
                }
                sb.Append(']');
                string json = sb.ToString();
                if (force || json != _lastWhJsonHash || !File.Exists(outPath))
                {
                    _lastWhJsonHash = json;
                    WriteAtomicText(outPath, json);
                    BumpDynSeq(b);
                }
            }
            catch { }
        }

        private int CmdDeleteWarehouseItemV2(IGameSystem gs, int cid, int delCount, long boxId, ulong matUID)
        {
            if (gs == null || gs.materialSystem == null || delCount <= 0)
            {
                SetStatus(-1, "Invalid delete parameters");
                return -1;
            }
            CMaterial target = gs.materialSystem.GetMaterialByUID(matUID);
            if (target == null)
            {
                SetStatus(-2, "Item UID not found");
                return -2;
            }
            int cfgId = target.nConfigID;
            int endVal = target.nEndurance;
            IRefitSystem refit = gs.refitSystem;
            bool targetRefit = (refit != null && refit.IsRefit(matUID));
            int remaining = delCount;
            int removedTotal = 0;

            if (boxId == 0L)
            {
                CContainer bag = GetBagContainer(gs, cid);
                List<ulong> uids = GetContainerUidsDirect(bag);
                if (bag == null || uids == null)
                {
                    SetStatus(-2, "Bag container not found");
                    return -2;
                }
                // Deduct primary UID first
                if (bag.Contain(matUID))
                {
                    int take = Mathf.Min(remaining, target.nNumber);
                    if (take > 0)
                    {
                        if (target.nNumber > take)
                        {
                            target.SetNum(target.nNumber - take, false);
                            remaining -= take;
                            removedTotal += take;
                        }
                        else
                        {
                            remaining -= target.nNumber;
                            removedTotal += target.nNumber;
                            uids.Remove(matUID);
                            gs.materialSystem.DestroyMaterial(matUID, true);
                        }
                    }
                }
                // If unstacked items were grouped in UI, deduct remaining from matching un-refitted slots in O(N)
                if (remaining > 0 && !targetRefit)
                {
                    List<ulong> kept = new List<ulong>(uids.Count);
                    for (int i = 0; i < uids.Count; i++)
                    {
                        ulong uid = uids[i];
                        if (remaining <= 0)
                        {
                            kept.Add(uid);
                            continue;
                        }
                        CMaterial m = gs.materialSystem.GetMaterialByUID(uid);
                        if (m != null && m.nConfigID == cfgId && m.nEndurance == endVal &&
                            (refit == null || !refit.IsRefit(uid)))
                        {
                            if (m.nNumber > remaining)
                            {
                                m.SetNum(m.nNumber - remaining, false);
                                removedTotal += remaining;
                                remaining = 0;
                                kept.Add(uid);
                            }
                            else
                            {
                                remaining -= m.nNumber;
                                removedTotal += m.nNumber;
                                gs.materialSystem.DestroyMaterial(uid, true);
                            }
                            continue;
                        }
                        kept.Add(uid);
                    }
                    if (kept.Count != uids.Count)
                    {
                        uids.Clear();
                        uids.AddRange(kept);
                    }
                }
                ModContainerSystem.MarkSaveTag(bag);
                try
                {
                    if (CClientGlobal.Instance != null && CClientGlobal.Instance.GetEventSystem != null)
                    {
                        eMSG.sContainer_Change ev = default(eMSG.sContainer_Change);
                        ev.nCustomID = cid;
                        ev.eContainer = eContainerTypeEnum.Bag;
                        ev.nMaterialConfigID = cfgId;
                        CClientGlobal.Instance.GetEventSystem.FireExecoute(eMSG.EVENT.Container_Change, 0, ev);
                    }
                }
                catch { }
            }
            else
            {
                CHomeContainer hc = (gs.homeContainerSystem != null) ? gs.homeContainerSystem.TryGetContainer(boxId) : null;
                if (hc == null)
                {
                    SetStatus(-2, "StorageBox not found");
                    return -2;
                }
                List<ulong> matchUids = new List<ulong>(16);
                matchUids.Add(matUID);
                if (!targetRefit)
                {
                    int slotCnt = hc.GetSlotCount();
                    for (int i = 0; i < slotCnt; i++)
                    {
                        ulong u = hc.GetMaterialUID(i);
                        if (u == matUID) continue;
                        CMaterial m = gs.materialSystem.GetMaterialByUID(u);
                        if (m != null && m.nConfigID == cfgId && m.nEndurance == endVal &&
                            (refit == null || !refit.IsRefit(u)))
                        {
                            matchUids.Add(u);
                        }
                    }
                }
                for (int i = 0; i < matchUids.Count && remaining > 0; i++)
                {
                    ulong u = matchUids[i];
                    CMaterial m = gs.materialSystem.GetMaterialByUID(u);
                    if (m == null) continue;
                    if (m.nNumber > remaining)
                    {
                        m.SetNum(m.nNumber - remaining, false);
                        removedTotal += remaining;
                        remaining = 0;
                    }
                    else
                    {
                        remaining -= m.nNumber;
                        removedTotal += m.nNumber;
                        hc.RemoveMaterial(u);
                        gs.materialSystem.DestroyMaterial(u, true);
                    }
                }
                RefreshOpenStorageBoxUi();
            }

            s_whForceDirty = true;
            ExportDynWarehouseAll(gs, true);
            SetStatus(1, "Deleted warehouse item x" + removedTotal.ToString());
            return (removedTotal > 0) ? 1 : -2;
        }

        private unsafe int CmdSetCurrencyV2(IGameSystem gs, int ctype, int val)
        {
            if (gs == null || gs.paySystem == null)
            {
                SetStatus(-2, "PaySystem null");
                return -2;
            }
            ModPaySystem mps = gs.paySystem as ModPaySystem;
            if (val < 0)
            {
                if (mps != null) mps.ForceClear(ctype);
                else ModPaySystem.ForceClearStatic(ctype, true);
                MaintainPersistedCurrency(Shm(), gs);
                ExportDynCurrencyFast(gs);
                SetStatus(1, "Currency mod cleared");
                return 1;
            }
            if (mps != null)
            {
                mps.ForceSet(ctype, val);
            }
            else
            {
                ModPaySystem.ForceSetStatic(ctype, val, true);
                IPaySystem ps = gs.paySystem;
                if (ctype == 0) ps.SetDiamondNum(val);
                else if (ctype == 1) ps.SetGardenCoinNum(val);
                else if (ctype == 2) ps.SetGardenPointNum(val);
                else if (ctype == 3) ps.SetCumulativePayment(val);
            }
            MaintainPersistedCurrency(Shm(), gs);
            ExportDynCurrencyFast(gs);
            SetStatus(1, "Currency updated & persisted");
            return 1;
        }

        private string _lastCurrencyJson = "";
        private unsafe void ExportDynCurrencyFast(IGameSystem gs)
        {
            if (gs == null || gs.paySystem == null) return;
            try
            {
                byte* b = Shm();
                if (b == null) return;
                string dir = ReadShmDynDir(b);
                if (string.IsNullOrEmpty(dir)) return;

                IPaySystem ps = gs.paySystem;
                int d = ps.GetDiamondNum();
                int gc = ps.GetGardenCoinNum();
                int gp = ps.GetGardenPointNum();
                int cp = 0;
                ModPaySystem mps = ps as ModPaySystem;
                if (mps != null)
                {
                    cp = mps.GetCumulativePayment();
                }
                else
                {
                    FieldInfo fi = typeof(CPaySystem).GetField("m_nCumulativePayment", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (fi != null)
                    {
                        try { cp = ((inte)fi.GetValue(ps)).GetNum(); } catch { }
                    }
                }
                string json = "{\"diamond\":" + d.ToString() + ",\"gardenCoin\":" + gc.ToString() +
                              ",\"gardenPoint\":" + gp.ToString() + ",\"cumulativePayment\":" + cp.ToString() + "}";
                if (json != _lastCurrencyJson)
                {
                    _lastCurrencyJson = json;
                    WriteAtomicText(dir + "/dyn_currency.json", json);
                }
            }
            catch { }
        }

        // ==================== #6 Network Currency Interceptor (ModHttpBridge) ====================
        private static unsafe void WriteHttpBridgeResponse(ulong bridgePtr, uint seq, int status, bool bSucc, byte[] resStream)
        {
            if (bridgePtr == 0UL) return;
            try
            {
                byte* hb = (byte*)bridgePtr;
                if (*(uint*)(hb + 0x08) != seq) return;
                byte[] outPayload = null;
                if (status == 200)
                {
                    byte[] body = resStream ?? new byte[0];
                    DNetwork.sNetwork_Head_Res hres = new DNetwork.sNetwork_Head_Res();
                    hres.bSucc = bSucc;
                    hres.szSignMD5 = (body.Length > 0) ? CMD5Util.MD5(body) : string.Empty;
                    byte[] hbytes = CStreamUtil.StructToBytes(hres);
                    outPayload = new byte[hbytes.Length + body.Length];
                    Array.Copy(hbytes, 0, outPayload, 0, hbytes.Length);
                    if (body.Length > 0) Array.Copy(body, 0, outPayload, hbytes.Length, body.Length);
                }
                int len = (outPayload != null) ? Mathf.Min(outPayload.Length, 4 * 1024 * 1024) : 0;
                for (int i = 0; i < len; i++) hb[0x10020 + i] = outPayload[i];
                *(int*)(hb + 0x10) = status;
                *(uint*)(hb + 0x18) = (uint)len;
                Thread.MemoryBarrier();
                *(uint*)(hb + 0x04) = 2u; // MOD_HTTP_STATE_RESP_READY
            }
            catch { }
        }

        private sealed class ModForwardHandler : INetworkHandler
        {
            private readonly ModTicker _owner;
            private readonly ulong _bridgePtr;
            private readonly uint _seq;

            public ModForwardHandler(ModTicker owner, ulong bridgePtr, uint seq)
            {
                _owner = owner;
                _bridgePtr = bridgePtr;
                _seq = seq;
            }

            public void OnHttpRequestSucc(int nCommand, byte[] resStream, byte[] reqStream)
            {
                WriteHttpBridgeResponse(_bridgePtr, _seq, 200, true, resStream);
                if (_owner != null) _owner._activeHttpSeq = 0u;
                IGameSystem gs = CGameSystem.Instance;
                if (gs != null && _owner != null) _owner.ExportDynCurrencyFast(gs);
            }

            public void OnHttpRequestFail(int nCommand, uint nErrorCode, string szMsg, byte[] reqStream, byte[] resStream)
            {
                try
                {
                    DNetwork.sNetwork_Error_Res err = new DNetwork.sNetwork_Error_Res();
                    err.nCode = nErrorCode;
                    err.szMsg = szMsg ?? string.Empty;
                    byte[] ebytes = CStreamUtil.StructToBytes(err);
                    WriteHttpBridgeResponse(_bridgePtr, _seq, 200, false, ebytes);
                }
                catch
                {
                    WriteHttpBridgeResponse(_bridgePtr, _seq, 502, false, null);
                }
                if (_owner != null) _owner._activeHttpSeq = 0u;
            }

            public void OnHttpRequestError(int nCommand, string szMsg, byte[] reqStream)
            {
                WriteHttpBridgeResponse(_bridgePtr, _seq, 502, false, null);
                if (_owner != null) _owner._activeHttpSeq = 0u;
            }
        }

        private void SyncNetworkMgrUrl()
        {
            try
            {
                if (string.IsNullOrEmpty(_localProxyUrl)) return;
                if (s_fiNetInstance == null)
                    s_fiNetInstance = typeof(CNetworkMgr).GetField("_Instance", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                CNetworkMgr nm = (s_fiNetInstance != null) ? (s_fiNetInstance.GetValue(null) as CNetworkMgr) : null;
                if (nm == null) return;

                if (s_fiNetUrls == null)
                    s_fiNetUrls = typeof(CNetworkMgr).GetField("m_dicUrl", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var dic = (s_fiNetUrls != null) ? (s_fiNetUrls.GetValue(nm) as Dictionary<DNetwork.eClientRequireUrl, string>) : null;
                if (dic == null) return;

                string localGsUrl = _localProxyUrl + "gs";
                string prefUrl = PlayerPrefs.GetString("GameServer", string.Empty);
                if (!string.IsNullOrEmpty(prefUrl) && !prefUrl.StartsWith("http://127.0.0.1:"))
                {
                    _realGameUrl1 = prefUrl;
                    PlayerPrefs.SetString("GameServer", string.Empty);
                    nm.SetUrl((DNetwork.eClientRequireUrl)99, _realGameUrl1);
                    nm.SetUrl((DNetwork.eClientRequireUrl)1, localGsUrl);
                }

                string curUrl = null;
                dic.TryGetValue((DNetwork.eClientRequireUrl)1, out curUrl);
                if (!string.IsNullOrEmpty(curUrl) && !curUrl.StartsWith("http://127.0.0.1:"))
                {
                    _realGameUrl1 = curUrl;
                    nm.SetUrl((DNetwork.eClientRequireUrl)99, _realGameUrl1);
                    nm.SetUrl((DNetwork.eClientRequireUrl)1, localGsUrl);
                }
                else if (curUrl != localGsUrl && !string.IsNullOrEmpty(_realGameUrl1))
                {
                    nm.SetUrl((DNetwork.eClientRequireUrl)99, _realGameUrl1);
                    nm.SetUrl((DNetwork.eClientRequireUrl)1, localGsUrl);
                }

                string localDbUrl = _localProxyUrl + "db";
                string prefDbUrl = PlayerPrefs.GetString("DBServer", string.Empty);
                if (!string.IsNullOrEmpty(prefDbUrl) && !prefDbUrl.StartsWith("http://127.0.0.1:"))
                {
                    _realDbUrl2 = prefDbUrl;
                    PlayerPrefs.SetString("DBServer", string.Empty);
                    nm.SetUrl((DNetwork.eClientRequireUrl)98, _realDbUrl2);
                    nm.SetUrl((DNetwork.eClientRequireUrl)2, localDbUrl);
                }

                string curDbUrl = null;
                dic.TryGetValue((DNetwork.eClientRequireUrl)2, out curDbUrl);
                if (!string.IsNullOrEmpty(curDbUrl) && !curDbUrl.StartsWith("http://127.0.0.1:"))
                {
                    _realDbUrl2 = curDbUrl;
                    nm.SetUrl((DNetwork.eClientRequireUrl)98, _realDbUrl2);
                    nm.SetUrl((DNetwork.eClientRequireUrl)2, localDbUrl);
                }
                else if (curDbUrl != localDbUrl)
                {
                    if (!string.IsNullOrEmpty(_realDbUrl2))
                        nm.SetUrl((DNetwork.eClientRequireUrl)98, _realDbUrl2);
                    nm.SetUrl((DNetwork.eClientRequireUrl)2, localDbUrl);
                }
            }
            catch { }
        }

        private unsafe void PollHttpBridge(byte* b, IGameSystem gs)
        {
            if (b == null) return;
            ulong hbPtr = *(ulong*)(b + 0x38);
            if (hbPtr == 0UL) return;

            byte* hb = (byte*)hbPtr;
            uint port = *(uint*)(hb + 0x00);
            if (port != 0u && string.IsNullOrEmpty(_localProxyUrl))
            {
                _localProxyUrl = "http://127.0.0.1:" + port.ToString() + "/";
                Debug.Log("[ModBridge] Local HTTP proxy URL=" + _localProxyUrl);
            }
            SyncNetworkMgrUrl();

            Thread.MemoryBarrier();
            uint st = *(uint*)(hb + 0x04);
            if (st != 1u) return;

            uint seq = *(uint*)(hb + 0x08);
            if (seq == _activeHttpSeq || seq == _lastDoneHttpSeq) return;

            int cmd = *(int*)(hb + 0x0C);
            uint reqLen = *(uint*)(hb + 0x14);
            uint routeTag = *(uint*)(hb + 0x1C);
            if (reqLen > 65536u)
            {
                _lastDoneHttpSeq = seq;
                WriteHttpBridgeResponse(hbPtr, seq, 502, false, null);
                return;
            }

            byte[] fullBody = new byte[reqLen];
            for (int i = 0; i < (int)reqLen; i++) fullBody[i] = hb[0x20 + i];

            uint flags = *(uint*)(b + 0x18);
            bool antiCheatOn = (flags & 1u) == 0u;
            bool blockCloudOn = (flags & 2048u) == 0u;

            if (blockCloudOn && (cmd == 9 || cmd == 10 || cmd == 115))
            {
                _lastDoneHttpSeq = seq;
                try
                {
                    if (CUIMgr.Instance != null)
                    {
                        var tipWnd = CUIMgr.Instance.GetWnd(CUIDefine.eUIWnd.UI_Game_SaveCloudTip);
                        if (tipWnd != null && tipWnd.IsVisable())
                        {
                            GameObject fx = tipWnd.GetNode("Widget_OpenEffect");
                            if (fx == null || !fx.activeSelf)
                                tipWnd.Hide();
                        }
                    }
                }
                catch { }
                Debug.Log("[ModBridge] Blocked Cloud Save upload cmd=" + cmd);
                byte[] errBytes = CStreamUtil.StructToBytes(new DNetwork.sNetwork_Error_Res
                {
                    nCode = 9001u,
                    szMsg = "反作弊保护：已100%拦截云存档上传"
                });
                WriteHttpBridgeResponse(hbPtr, seq, 200, false, errBytes);
                return;
            }

            if (antiCheatOn && (cmd == 44 || cmd == 45 || cmd == 214 || cmd == 19 || cmd == 20))
            {
                _lastDoneHttpSeq = seq;
                Debug.Log("[ModBridge] Blocked AntiCheat/Log upload cmd=" + cmd);
                byte[] errBytes = CStreamUtil.StructToBytes(new DNetwork.sNetwork_Error_Res
                {
                    nCode = 9002u,
                    szMsg = "Blocked by Mod"
                });
                WriteHttpBridgeResponse(hbPtr, seq, 200, false, errBytes);
                return;
            }

            byte[] innerReq = fullBody;
            try
            {
                CStreamReader sr = new CStreamReader(fullBody);
                sr.Pop<DNetwork.sNetwork_Head_Req>();
                int left = sr.GetLengthLeft();
                if (left >= 0 && left <= fullBody.Length)
                {
                    innerReq = new byte[left];
                    if (left > 0) Array.Copy(fullBody, fullBody.Length - left, innerReq, 0, left);
                }
            }
            catch { }

            if (routeTag != 2u)
            {
                bool handled = false;
                bool bSucc = false;
                byte[] resStream = null;
                TryHandleLocalCurrencyCmd(gs, cmd, innerReq, out handled, out bSucc, out resStream);
                if (handled)
                {
                    _lastDoneHttpSeq = seq;
                    if (bSucc)
                    {
                        WriteHttpBridgeResponse(hbPtr, seq, 200, true, resStream);
                        ExportDynCurrencyFast(gs);
                    }
                    else
                    {
                        byte[] errBytes = CStreamUtil.StructToBytes(new DNetwork.sNetwork_Error_Res { nCode = 1001u, szMsg = "本地货币余额不足" });
                        WriteHttpBridgeResponse(hbPtr, seq, 200, false, errBytes);
                    }
                    return;
                }

                if (string.IsNullOrEmpty(_realGameUrl1) || CNetworkMgr.Instance == null)
                {
                    _lastDoneHttpSeq = seq;
                    WriteHttpBridgeResponse(hbPtr, seq, 502, false, null);
                    return;
                }

                _activeHttpSeq = seq;
                _lastDoneHttpSeq = seq;
                CNetworkMgr.Instance.SendRequest(new ModForwardHandler(this, hbPtr, seq), (DNetwork.eClientRequireUrl)99, cmd, innerReq);
            }
            else
            {
                if (string.IsNullOrEmpty(_realDbUrl2) || CNetworkMgr.Instance == null)
                {
                    _lastDoneHttpSeq = seq;
                    WriteHttpBridgeResponse(hbPtr, seq, 502, false, null);
                    return;
                }

                _activeHttpSeq = seq;
                _lastDoneHttpSeq = seq;
                CNetworkMgr.Instance.SendRequest(new ModForwardHandler(this, hbPtr, seq), (DNetwork.eClientRequireUrl)98, cmd, innerReq);
            }
        }

        private static sMarket_RafflePoolConfig RollRaffleReward(IGameDataCenter dc, int poolId, int groupId, ref int topSignCount)
        {
            if (dc == null) return null;
            var loader = dc.GetConfigLoader<CMarket_RafflePoolConfigLoader>(eGameConfigType.Market_RafflePool);
            if (loader == null) return null;
            if (s_fiRafflePoolDic == null)
                s_fiRafflePoolDic = typeof(CMarket_RafflePoolConfigLoader).GetField("m_dicConfigs", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var dic = (s_fiRafflePoolDic != null) ? (s_fiRafflePoolDic.GetValue(loader) as Dictionary<int, sMarket_RafflePoolConfig>) : null;
            if (dic == null || dic.Count == 0) return null;

            List<sMarket_RafflePoolConfig> candidates = new List<sMarket_RafflePoolConfig>(16);
            int totalWeight = 0;
            sMarket_RafflePoolConfig topItem = null;
            foreach (sMarket_RafflePoolConfig cfg in dic.Values)
            {
                if (cfg != null && cfg.nPoolId == poolId && (cfg.nPoolGroupId == 0 || cfg.nPoolGroupId == groupId))
                {
                    candidates.Add(cfg);
                    totalWeight += Mathf.Max(1, cfg.nWeight);
                    if (cfg.nTopSign > 0) topItem = cfg;
                }
            }
            if (candidates.Count == 0)
            {
                foreach (sMarket_RafflePoolConfig cfg in dic.Values)
                {
                    if (cfg != null && cfg.nPoolId == poolId)
                    {
                        candidates.Add(cfg);
                        totalWeight += Mathf.Max(1, cfg.nWeight);
                        if (cfg.nTopSign > 0) topItem = cfg;
                    }
                }
            }
            if (candidates.Count == 0) return null;

            topSignCount++;
            if (topItem != null && topItem.nTopSign > 0 && topSignCount >= topItem.nTopSign)
            {
                topSignCount = 0;
                return topItem;
            }
            int roll = UnityEngine.Random.Range(0, totalWeight);
            int acc = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                acc += Mathf.Max(1, candidates[i].nWeight);
                if (roll < acc)
                {
                    if (candidates[i].nTopSign > 0) topSignCount = 0;
                    return candidates[i];
                }
            }
            return candidates[0];
        }

        private static int FindRaffleCostPerDraw(IGameDataCenter dc, int poolId)
        {
            if (dc == null) return 0;
            var loader = dc.GetConfigLoader<CMarket_RaffleTraderConfigLoader>(eGameConfigType.Market_RaffleTrader);
            if (loader == null) return 0;
            if (s_fiRaffleTraderDic == null)
                s_fiRaffleTraderDic = typeof(CMarket_RaffleTraderConfigLoader).GetField("m_dicConfigs", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var dic = (s_fiRaffleTraderDic != null) ? (s_fiRaffleTraderDic.GetValue(loader) as Dictionary<int, sMarket_RaffleTraderConfig>) : null;
            if (dic == null) return 0;
            foreach (sMarket_RaffleTraderConfig cfg in dic.Values)
            {
                if (cfg != null && cfg.nPoolId == poolId) return cfg.nGardenCoinNum;
            }
            return 0;
        }

        private unsafe void SetCurrencyChecked(IGameSystem gs, int ctype, int val)
        {
            if (gs == null || gs.paySystem == null) return;
            ModPaySystem mps = gs.paySystem as ModPaySystem;
            if (mps != null) mps.ForceSet(ctype, val);
            else if (ctype == 0) gs.paySystem.SetDiamondNum(val);
            else if (ctype == 1) gs.paySystem.SetGardenCoinNum(val);
            else if (ctype == 2) gs.paySystem.SetGardenPointNum(val);
            MaintainPersistedCurrency(Shm(), gs);
            ExportDynCurrencyFast(gs);
        }

        private void TryHandleLocalCurrencyCmd(IGameSystem gs, int cmd, byte[] innerReq, out bool handled, out bool bSucc, out byte[] resStream)
        {
            handled = false;
            bSucc = false;
            resStream = null;
            if (gs == null || gs.paySystem == null || CClientGlobal.Instance == null || CClientGlobal.Instance.GetGameDataCenter == null)
                return;
            if (!ModPaySystem.IsAnyCurrencyModded())
                return;

            IGameDataCenter dc = CClientGlobal.Instance.GetGameDataCenter;
            IPaySystem pay = gs.paySystem;
            const string SALT = "3j5k2b1n7m8s4f6d5e3r2t1z";

            try
            {
                if (cmd == 7) // RequestExchange (NPC Diamond Shop)
                {
                    handled = true;
                    DNetwork.sNetwork_RequestExchange_Req req = new CStreamReader(innerReq).Pop<DNetwork.sNetwork_RequestExchange_Req>();
                    var loader = dc.GetConfigLoader<CExchangeConfigLoader>(eGameConfigType.Exchange);
                    sExchangeConfig cfg = (loader != null) ? loader.GetConfig(req.nExchangeID) : null;
                    int cost = (cfg != null) ? (int)cfg.nDiamond : req.nPrice;
                    if (cost < 0) cost = 0;
                    int cur = pay.GetDiamondNum();
                    if (cur < cost) { bSucc = false; return; }
                    int next = cur - cost;
                    SetCurrencyChecked(gs, 0, next);
                    DNetwork.sNetwork_RequestExchange_Res res = new DNetwork.sNetwork_RequestExchange_Res();
                    res.nExchangeID = req.nExchangeID;
                    res.nDiamond = next;
                    res.szSignMD5 = CMD5Util.MD5(new List<string> { res.nExchangeID.ToString(), res.nDiamond.ToString(), SALT });
                    resStream = CStreamUtil.StructToBytes(res);
                    bSucc = true;
                    return;
                }
                if (cmd == 32) // RequestGardenTrade (Garden Diamond Trader)
                {
                    handled = true;
                    DNetwork.sNetwork_RequestGardenTrade_Req req = new CStreamReader(innerReq).Pop<DNetwork.sNetwork_RequestGardenTrade_Req>();
                    var loader = dc.GetConfigLoader<CMarket_ExchangeDepotConfigLoader>(eGameConfigType.Market_ExchangeDepot);
                    sMarket_ExchangeDepotConfig cfg = (loader != null) ? loader.GetConfig(req.nTrade_id) : null;
                    int cost = (cfg != null && cfg.nCostDiamond > 0) ? cfg.nCostDiamond : 0;
                    int cur = pay.GetDiamondNum();
                    if (cur < cost) { bSucc = false; return; }
                    int next = cur - cost;
                    SetCurrencyChecked(gs, 0, next);
                    DNetwork.sNetwork_RequestGardenTrade_Res res = new DNetwork.sNetwork_RequestGardenTrade_Res();
                    res.nTrade_id = req.nTrade_id;
                    res.nDiamond = next;
                    res.nGardenCoin = pay.GetGardenCoinNum();
                    res.nGardenPoint = pay.GetGardenPointNum();
                    res.szSignMD5 = string.Empty;
                    resStream = CStreamUtil.StructToBytes(res);
                    bSucc = true;
                    return;
                }
                if (cmd == 38) // RequestGardenPointExchange (Garden Point Shop)
                {
                    handled = true;
                    DNetwork.sNetwork_RequestGardenPointExchange_Req req = new CStreamReader(innerReq).Pop<DNetwork.sNetwork_RequestGardenPointExchange_Req>();
                    var loader = dc.GetConfigLoader<CMarket_GardenPointShopConfigLoader>(eGameConfigType.Market_GardenPointShop);
                    sMarket_GardenPointShopConfig cfg = (loader != null) ? loader.GetConfig(req.nExchangeId) : null;
                    int cost = 0;
                    if (cfg != null)
                        cost = (cfg.nCostOffPoint > 0 && cfg.nCostOffPoint < cfg.nCostGardenPoint) ? cfg.nCostOffPoint : cfg.nCostGardenPoint;
                    int cur = pay.GetGardenPointNum();
                    if (cur < cost) { bSucc = false; return; }
                    int next = cur - cost;
                    SetCurrencyChecked(gs, 2, next);
                    DNetwork.sNetwork_RequestGardenPointExchange_Res res = new DNetwork.sNetwork_RequestGardenPointExchange_Res();
                    res.nExchangeId = req.nExchangeId;
                    res.nDiamond = pay.GetDiamondNum();
                    res.nGardenCoin = pay.GetGardenCoinNum();
                    res.nGardenPoint = next;
                    res.szSignMD5 = string.Empty;
                    resStream = CStreamUtil.StructToBytes(res);
                    bSucc = true;
                    return;
                }
                if (cmd == 39) // RequestMarketPlay (Garden Mini-Game)
                {
                    handled = true;
                    DNetwork.sNetwork_RequestMarketPlay_Req req = new CStreamReader(innerReq).Pop<DNetwork.sNetwork_RequestMarketPlay_Req>();
                    var loader = dc.GetConfigLoader<CMarket_PlayConfigLoader>(eGameConfigType.Market_Play);
                    sMarket_PlayConfig cfg = (loader != null) ? loader.GetConfig(req.nGamerId) : null;
                    int cost = (cfg != null) ? Mathf.Max(0, cfg.nGardenCoinCost) : 0;
                    int cur = pay.GetGardenCoinNum();
                    if (cur < cost) { bSucc = false; return; }
                    int next = cur - cost;
                    SetCurrencyChecked(gs, 1, next);
                    DNetwork.sNetwork_RequestMarketPlay_Res res = new DNetwork.sNetwork_RequestMarketPlay_Res();
                    res.nGamerId = req.nGamerId;
                    res.nDiamond = pay.GetDiamondNum();
                    res.nGardenCoin = next;
                    res.nGardenPoint = pay.GetGardenPointNum();
                    resStream = CStreamUtil.StructToBytes(res);
                    bSucc = true;
                    return;
                }
                if (cmd == 40) // RequestGardenCoinExchange (Material -> GardenCoin)
                {
                    handled = true;
                    DNetwork.sNetwork_RequestGardenCoinExchange_Req req = new CStreamReader(innerReq).Pop<DNetwork.sNetwork_RequestGardenCoinExchange_Req>();
                    var loader = dc.GetConfigLoader<CMarket_GardenCoinShopConfigLoader>(eGameConfigType.Market_GardenCoinShop);
                    sMarket_GardenCoinShop cfg = (loader != null) ? loader.GetConfig(req.nTradeId) : null;
                    int gain = (cfg != null) ? Mathf.Max(0, cfg.nGardenCoinNum) : 0;
                    int next = pay.GetGardenCoinNum() + gain;
                    SetCurrencyChecked(gs, 1, next);
                    DNetwork.sNetwork_RequestGardenCoinExchange_Res res = new DNetwork.sNetwork_RequestGardenCoinExchange_Res();
                    res.nTradeId = req.nTradeId;
                    res.nDiamond = pay.GetDiamondNum();
                    res.nGardenCoin = next;
                    res.nGardenPoint = pay.GetGardenPointNum();
                    resStream = CStreamUtil.StructToBytes(res);
                    bSucc = true;
                    return;
                }
                if (cmd == 48) // RequestActivityPointExchange (Holiday Point Exchange)
                {
                    handled = true;
                    DNetwork.sNetwork_RequestActivityPointExchange_Req req = new CStreamReader(innerReq).Pop<DNetwork.sNetwork_RequestActivityPointExchange_Req>();
                    int cost = 0;
                    int matNum = 1;
                    var impLoader = dc.GetConfigLoader<CHolidayImportantExchangeConfigLoader>(eGameConfigType.HolidayImportantExchange);
                    sHolidayImportantExchangeConfig impCfg = (impLoader != null) ? impLoader.GetConfig(req.nActivityId) : null;
                    if (impCfg != null)
                    {
                        if (req.nMaterialId == impCfg.nMaterialId1) cost = impCfg.nMaterialPoint1;
                        else if (req.nMaterialId == impCfg.nMaterialId2) cost = impCfg.nMaterialPoint2;
                        else if (req.nMaterialId == impCfg.nMaterialId3) cost = impCfg.nMaterialPoint3;
                        else if (req.nMaterialId == impCfg.nMaterialId4) cost = impCfg.nMaterialPoint4;
                    }
                    else
                    {
                        var holLoader = dc.GetConfigLoader<CHolidayExchangeConfigLoader>(eGameConfigType.HolidayExchange);
                        sHolidayExchangeConfig holCfg = (holLoader != null) ? holLoader.GetConfig(req.nActivityId) : null;
                        if (holCfg != null)
                        {
                            if (req.nMaterialId == holCfg.nMaterialId1) { cost = holCfg.nMaterialPoint1; matNum = Mathf.Max(1, holCfg.nMaterialNum1); }
                            else if (req.nMaterialId == holCfg.nMaterialId2) { cost = holCfg.nMaterialPoint2; matNum = Mathf.Max(1, holCfg.nMaterialNum2); }
                            else if (req.nMaterialId == holCfg.nMaterialId3) { cost = holCfg.nMaterialPoint3; matNum = Mathf.Max(1, holCfg.nMaterialNum3); }
                        }
                    }
                    int cur = pay.GetGardenPointNum();
                    if (cur < cost) { bSucc = false; return; }
                    int next = cur - cost;
                    SetCurrencyChecked(gs, 2, next);
                    DNetwork.sNetwork_RequestActivityPointExchange_Res res = new DNetwork.sNetwork_RequestActivityPointExchange_Res();
                    res.nMaterialId = req.nMaterialId;
                    res.nMaterialNum = matNum;
                    res.nGardenPoint = next;
                    resStream = CStreamUtil.StructToBytes(res);
                    bSucc = true;
                    return;
                }
                if (cmd == 201 && innerReq != null && innerReq.Length >= 16) // RequestOneRaffleV1
                {
                    handled = true;
                    int nPoolid = BitConverter.ToInt32(innerReq, 8);
                    int nGroupid = BitConverter.ToInt32(innerReq, 12);
                    int cost = FindRaffleCostPerDraw(dc, nPoolid);
                    int curCoin = pay.GetGardenCoinNum();
                    if (curCoin < cost) { bSucc = false; return; }
                    sMarket_RafflePoolConfig picked = RollRaffleReward(dc, nPoolid, nGroupid, ref _raffleTopSignCount);
                    if (picked == null) { bSucc = false; return; }
                    _raffleCountAll++;
                    int nextCoin = curCoin - cost;
                    int nextPt = pay.GetGardenPointNum() + Mathf.Max(0, picked.nGardenPointNum);
                    SetCurrencyChecked(gs, 1, nextCoin);
                    if (picked.nGardenPointNum > 0) SetCurrencyChecked(gs, 2, nextPt);

                    HotfixDNetWork.sNetwork_RequestOneRaffleV1_Res res = default(HotfixDNetWork.sNetwork_RequestOneRaffleV1_Res);
                    res.nPoolid = nPoolid;
                    res.nGroupid = nGroupid;
                    res.nRewardId = picked.nRewardId;
                    res.nRaffle_Count_All = _raffleCountAll;
                    res.nTopSignRaffleCount = _raffleTopSignCount;
                    res.nGardenCoin = nextCoin;
                    res.nGardenPoint = nextPt;
                    resStream = CStreamUtil.StructToBytes((object)res);
                    bSucc = true;
                    return;
                }
                if (cmd == 205 && innerReq != null && innerReq.Length >= 16) // RequestTenRaffle
                {
                    handled = true;
                    int nPoolid = BitConverter.ToInt32(innerReq, 8);
                    int nGroupid = BitConverter.ToInt32(innerReq, 12);
                    int oneCost = FindRaffleCostPerDraw(dc, nPoolid);
                    int cost = oneCost * 10;
                    int curCoin = pay.GetGardenCoinNum();
                    if (curCoin < cost) { bSucc = false; return; }

                    int[] rewardIds = new int[10];
                    int bonusPt = 0;
                    for (int i = 0; i < 10; i++)
                    {
                        sMarket_RafflePoolConfig picked = RollRaffleReward(dc, nPoolid, nGroupid, ref _raffleTopSignCount);
                        if (picked == null) { bSucc = false; return; }
                        rewardIds[i] = picked.nRewardId;
                        bonusPt += Mathf.Max(0, picked.nGardenPointNum);
                    }
                    _raffleCountAll += 10;
                    int nextCoin = curCoin - cost;
                    int nextPt = pay.GetGardenPointNum() + bonusPt;
                    SetCurrencyChecked(gs, 1, nextCoin);
                    if (bonusPt > 0) SetCurrencyChecked(gs, 2, nextPt);

                    HotfixDNetWork.sNetwork_RequestTenRaffle_Res res = default(HotfixDNetWork.sNetwork_RequestTenRaffle_Res);
                    res.nPoolid = nPoolid;
                    res.nGroupid = nGroupid;
                    res.nRaffle_Count_All = _raffleCountAll;
                    res.nTopSignRaffleCount = _raffleTopSignCount;
                    res.nGardenCoin = nextCoin;
                    res.nGardenPoint = nextPt;
                    CStreamWritter w = new CStreamWritter(24 + 40);
                    w.Push((object)res);
                    for (int i = 0; i < 10; i++)
                    {
                        HotfixDNetWork.sNetwork_GardenTradeDataV1 item = default(HotfixDNetWork.sNetwork_GardenTradeDataV1);
                        item.nTrade_id = rewardIds[i];
                        w.Push((object)item);
                    }
                    resStream = w.GetBuffer();
                    bSucc = true;
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModBridge] TryHandleLocalCurrencyCmd ex: " + ex.Message);
            }
        }

        private long ResolveDefaultBoxId(IGameSystem gs)
        {
            if (gs == null) return 0;
            CHomeItemSystem his = gs.homeItemSystem as CHomeItemSystem;
            var homeData = GetHomeItemData(his);
            if (homeData != null)
            {
                foreach (Dictionary<long, HomeItemPlacementData> homeMap in homeData.Values)
                {
                    if (homeMap == null) continue;
                    foreach (HomeItemPlacementData p in homeMap.Values)
                    {
                        if (IsStorageBoxPlacement(p))
                            return p.lInstanceId;
                    }
                }
            }
            CHomeContainerSystem hcs = gs.homeContainerSystem as CHomeContainerSystem;
            var containers = GetHomeContainers(hcs);
            if (containers != null)
            {
                foreach (long k in containers.Keys)
                {
                    if (k > 0) return k;
                }
            }
            return 0;
        }

        internal static bool MergeSingleHomeContainer(IGameSystem gs, CHomeContainer hc)
        {
            if (gs == null || hc == null || gs.materialSystem == null) return false;
            int cnt = hc.GetSlotCount();
            if (cnt <= 1) return false;

            IRefitSystem refit = gs.refitSystem;
            int[] seenIds = new int[cnt];
            int[] seenEnds = new int[cnt];
            CMaterial[] seenMats = new CMaterial[cnt];
            int seenCount = 0;
            ulong[] toRemove = new ulong[cnt];
            int remCount = 0;

            for (int i = 0; i < cnt; i++)
            {
                ulong uid = hc.GetMaterialUID(i);
                CMaterial m = gs.materialSystem.GetMaterialByUID(uid);
                if (m == null || m.nNumber <= 0) continue;

                // Refitted weapons carry custom modifications - never stack with unmodified or other refitted items
                if (refit != null && refit.IsRefit(uid)) continue;

                int match = -1;
                for (int j = 0; j < seenCount; j++)
                {
                    if (seenIds[j] == m.nConfigID && seenEnds[j] == m.nEndurance &&
                        (long)seenMats[j].nNumber + (long)m.nNumber <= (long)int.MaxValue)
                    {
                        match = j;
                        break;
                    }
                }
                if (match >= 0)
                {
                    seenMats[match].SetNum(seenMats[match].nNumber + m.nNumber, false);
                    toRemove[remCount++] = uid;
                }
                else
                {
                    seenIds[seenCount] = m.nConfigID;
                    seenEnds[seenCount] = m.nEndurance;
                    seenMats[seenCount] = m;
                    seenCount++;
                }
            }

            for (int i = 0; i < remCount; i++)
            {
                hc.RemoveMaterial(toRemove[i]);
                gs.materialSystem.DestroyMaterial(toRemove[i], true);
            }
            return remCount > 0;
        }

        private void MergeInfStackContainers(IGameSystem gs, int cid, bool silentIfUnchanged)
        {
            if (gs == null || cid == 0) return;
            try
            {
                // Infinite stacking strictly applies ONLY to warehouse containers (CHomeContainer).
                // Player bag, drop, and sale containers retain vanilla rules (never auto-merged here).
                bool anyMerged = false;
                CHomeContainerSystem hcs = gs.homeContainerSystem as CHomeContainerSystem;
                var containers = GetHomeContainers(hcs);
                if (containers != null && gs.materialSystem != null)
                {
                    foreach (KeyValuePair<long, CHomeContainer> kv in containers)
                    {
                        if (MergeSingleHomeContainer(gs, kv.Value))
                            anyMerged = true;
                    }
                }
                if (anyMerged)
                {
                    RefreshOpenStorageBoxUi();
                    ExportDynWarehouse(gs);
                }
                if (!silentIfUnchanged || anyMerged)
                {
                    SetStatus(1, "Merged warehouse into infinite stacks");
                }
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
                    try
                    {
                        IGameSystem gsCur = CGameSystem.Instance;
                        if (gsCur != null && gsCur.containerSystem != null) gsCur.containerSystem.RefreshContainer(cid, et);
                        else cs.RefreshContainer(cid, et);
                    }
                    catch { }
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
                s_whForceDirty = true;
                RefreshOpenStorageBoxUi();
                ExportDynWarehouseAll(CGameSystem.Instance, true);
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

            eContainerTypeEnum destType = ResolveBagDestType(cfg.eType);
            bool infBagNow = (target == 0 && destType == eContainerTypeEnum.Bag &&
                              (EditorPrefsX.endianDiff1 & 32) != 0 && IsInShelterNow(gs));
            bool allowMerge = (!cfg.bCannotCombine && cfg.nEndurance == 0) ||
                              ((EditorPrefsX.endianDiff1 & 32) != 0 && (target == 1 || infBagNow));

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
                    var dicC = GetHomeContainers(hcsImpl);
                    if (dicC != null)
                    {
                        hc = new CHomeContainer(chosenId);
                        dicC[chosenId] = hc;
                    }
                }
                if (hc == null)
                {
                    SetStatus(-4, "Failed to get/create HomeContainer");
                    return -4;
                }

                if (allowMerge)
                {
                    IRefitSystem refit = gs.refitSystem;
                    CMaterial merge = null;
                    int slotCnt = hc.GetSlotCount();
                    for (int i = 0; i < slotCnt; i++)
                    {
                        ulong uid = hc.GetMaterialUID(i);
                        CMaterial cand = gs.materialSystem.GetMaterialByUID(uid);
                        if (cand != null && cand.pConfig != null && cand.nConfigID == itemId &&
                            cand.nEndurance == cfg.nEndurance && (refit == null || !refit.IsRefit(uid)))
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
            CContainerSystem cs = gs.containerSystem as CContainerSystem;
            var dicData = GetContainerData(cs);
            if (dicData != null)
            {
                sContainerSystemData sdata = null;
                if (!dicData.TryGetValue(cid, out sdata) || sdata == null)
                {
                    sdata = new sContainerSystemData();
                    sdata.nCustomID = cid;
                    dicData[cid] = sdata;
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
                    IRefitSystem refit = gs.refitSystem;
                    CMaterial merge = null;
                    List<ulong> uids = GetContainerUidsDirect(cont);
                    if (uids != null)
                    {
                        for (int i = 0; i < uids.Count; i++)
                        {
                            if (refit != null && refit.IsRefit(uids[i])) continue;
                            CMaterial cand = gs.materialSystem.GetMaterialByUID(uids[i]);
                            if (cand != null && cand.nConfigID == itemId && cand.nEndurance == cfg.nEndurance)
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
                s_whForceDirty = true;
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

        private void ApplySinglePropLAN(LAN.CLanEntity lanPlayer, int key, int targetVal)
        {
            if (lanPlayer == null || lanPlayer.propertyPart == null) return;
            LAN.CLanPropertyPart pp = lanPlayer.propertyPart;

            if (key >= 101 && key <= 108 && DGameConfig.Instance != null)
            {
                if (DGameConfig.Instance.nMaxMoveSpeed < targetVal)
                    DGameConfig.Instance.nMaxMoveSpeed = targetVal + 100000;
                if (DGameConfig.Instance.nMinMoveSpeed > targetVal && targetVal >= 0)
                    DGameConfig.Instance.nMinMoveSpeed = 0;
            }

            if (key == 2)
            {
                if (pp.GetValue(ePropValueType.AfterCul, 1, 0) < targetVal)
                    pp.SetValue(ePropValueType.Original, 1, targetVal, true);
                pp.SetValue(ePropValueType.Original, 2, targetVal, true);
                return;
            }
            if (key == 303)
            {
                if (pp.GetValue(ePropValueType.AfterCul, 302, 0) < targetVal)
                    pp.SetValue(ePropValueType.Original, 302, targetVal, true);
                pp.SetValue(ePropValueType.Original, 303, targetVal, true);
                return;
            }
            if (key == 305)
            {
                if (pp.GetValue(ePropValueType.AfterCul, 304, 0) < targetVal)
                    pp.SetValue(ePropValueType.Original, 304, targetVal, true);
                pp.SetValue(ePropValueType.Original, 305, targetVal, true);
                return;
            }
            if (key == 402 && pp.GetValue(ePropValueType.AfterCul, 403, 0) < targetVal)
                pp.SetValue(ePropValueType.Original, 403, targetVal, true);
            else if (key == 411 && pp.GetValue(ePropValueType.AfterCul, 412, 0) < targetVal)
                pp.SetValue(ePropValueType.Original, 412, targetVal, true);
            else if (key == 421 && pp.GetValue(ePropValueType.AfterCul, 422, 0) < targetVal)
                pp.SetValue(ePropValueType.Original, 422, targetVal, true);

            pp.SetValue(ePropValueType.Original, key, targetVal, true);
        }

        private void EnforcePropLocksLAN(LAN.CLanEntity lanPlayer)
        {
            if (lanPlayer == null || lanPlayer.propertyPart == null) return;
            for (int i = 0; i < _lockCount; i++)
            {
                int k = _lockKeys[i];
                int v = _lockVals[i];
                int cur = lanPlayer.propertyPart.GetValue(ePropValueType.AfterCul, k, 0);
                if (cur != v)
                {
                    ApplySinglePropLAN(lanPlayer, k, v);
                }
            }
        }

        private int CmdSetProp(CEntity player, LAN.CLanEntity lanPlayer, int key, int rawVal, int lockOp)
        {
            if (player == null && lanPlayer == null)
            {
                SetStatus(-2, "Player entity null");
                return -2;
            }
            if (player != null)
            {
                ApplySingleProp(player, key, rawVal);
                SetPropLockState(key, rawVal, lockOp);
                UpdateSnapshot(player);
            }
            else if (lanPlayer != null)
            {
                ApplySinglePropLAN(lanPlayer, key, rawVal);
                SetPropLockState(key, rawVal, lockOp);
                UpdateSnapshotLAN(lanPlayer);
            }
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
            var dicData = GetContainerData(cs);
            if (dicData != null)
            {
                sContainerSystemData sdata = null;
                if (!dicData.TryGetValue(cid, out sdata) || sdata == null)
                {
                    sdata = new sContainerSystemData();
                    sdata.nCustomID = cid;
                    dicData[cid] = sdata;
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

        private int CmdApplyBuff(CEntity player, LAN.CLanEntity lanPlayer, int buffId, int durationSec)
        {
            if (player == null && lanPlayer == null)
            {
                SetStatus(-2, "Player null");
                return -2;
            }
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

            bool ok = false;
            if (player != null && player.buffPart != null)
            {
                ok = player.buffPart.AddBuff(buffId, player);
                CBuff cb = player.buffPart.GetBuff(buffId);
                if (cb != null) SetBuffCurCycle(cb, -999999);
            }
            if (lanPlayer != null && lanPlayer.buffPart != null)
            {
                ok = lanPlayer.buffPart.AddBuff(buffId, lanPlayer);
                lanPlayer.buffPart.RecordBuffCycle(buffId, -999999);
            }
            SetStatus(ok ? 1 : 0, "Buff " + buffId.ToString() + (durationSec > 0 ? " (" + durationSec.ToString() + "s)" : " (Perm)"));
            return 1;
        }

        private int CmdRemoveBuff(CEntity player, LAN.CLanEntity lanPlayer, int buffId)
        {
            if (buffId == 0)
            {
                if (player != null && player.buffPart != null)
                {
                    for (int i = 0; i < _buffCount; i++) player.buffPart.ReqRemoveBuff(_buffIds[i]);
                }
                if (lanPlayer != null && lanPlayer.buffPart != null)
                {
                    for (int i = 0; i < _buffCount; i++) lanPlayer.buffPart.RemoveBuff(_buffIds[i]);
                }
                _buffCount = 0;
                SetStatus(1, "Removed mod buffs");
                return 1;
            }
            if (player != null && player.buffPart != null) player.buffPart.ReqRemoveBuff(buffId);
            if (lanPlayer != null && lanPlayer.buffPart != null) lanPlayer.buffPart.RemoveBuff(buffId);
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
            if (player == null || player.buffPart == null || _buffCount == 0) return;
            float now = Time.realtimeSinceStartup;
            for (int i = _buffCount - 1; i >= 0; i--)
            {
                int bid = _buffIds[i];
                float exp = _buffExpireAt[i];
                CBuff cb = player.buffPart.GetBuff(bid);
                if (exp <= 0f) // Permanent buff - never auto-drop, re-add if expired or cleared by scene/clock
                {
                    if (cb == null)
                    {
                        player.buffPart.AddBuff(bid, player);
                        cb = player.buffPart.GetBuff(bid);
                    }
                    if (cb != null)
                    {
                        SetBuffCurCycle(cb, -999999);
                    }
                }
                else // Timed buff
                {
                    if (cb == null)
                    {
                        _buffCount--;
                        _buffIds[i] = _buffIds[_buffCount];
                        _buffExpireAt[i] = _buffExpireAt[_buffCount];
                        continue;
                    }
                    if (now >= exp)
                    {
                        player.buffPart.ReqRemoveBuff(bid);
                        _buffCount--;
                        _buffIds[i] = _buffIds[_buffCount];
                        _buffExpireAt[i] = _buffExpireAt[_buffCount];
                    }
                    else
                    {
                        SetBuffCurCycle(cb, -999999);
                    }
                }
            }
        }

        private void MaintainBuffsLAN(LAN.CLanEntity lanPlayer)
        {
            if (lanPlayer == null || lanPlayer.buffPart == null || _buffCount == 0) return;
            float now = Time.realtimeSinceStartup;
            for (int i = _buffCount - 1; i >= 0; i--)
            {
                int bid = _buffIds[i];
                float exp = _buffExpireAt[i];
                LAN.CLanBuff cb = lanPlayer.buffPart.GetBuff(bid);
                if (exp <= 0f) // Permanent buff
                {
                    if (cb == null)
                    {
                        lanPlayer.buffPart.AddBuff(bid, lanPlayer);
                    }
                    lanPlayer.buffPart.RecordBuffCycle(bid, -999999);
                }
                else // Timed buff
                {
                    if (cb == null)
                    {
                        _buffCount--;
                        _buffIds[i] = _buffIds[_buffCount];
                        _buffExpireAt[i] = _buffExpireAt[_buffCount];
                        continue;
                    }
                    if (now >= exp)
                    {
                        lanPlayer.buffPart.RemoveBuff(bid);
                        _buffCount--;
                        _buffIds[i] = _buffIds[_buffCount];
                        _buffExpireAt[i] = _buffExpireAt[_buffCount];
                    }
                    else
                    {
                        lanPlayer.buffPart.RecordBuffCycle(bid, -999999);
                    }
                }
            }
        }

        private int CmdRestoreEndurance(IGameSystem gs, int cid)
        {
            return RestoreEndurance(gs, cid, true, true);
        }

        private int RestoreEndurance(IGameSystem gs, int cid, bool includeBag, bool updateStatus)
        {
            if (gs == null || gs.containerSystem == null || gs.materialSystem == null) return 0;
            int n = 0;
            eContainerTypeEnum[] containers = includeBag
                ? new eContainerTypeEnum[] { eContainerTypeEnum.Equipment, eContainerTypeEnum.Bag }
                : new eContainerTypeEnum[] { eContainerTypeEnum.Equipment };
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
            if (updateStatus)
            {
                SetStatus(1, "Restored endurance x" + n.ToString());
            }
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

            // Home storage boxes (placed HomeItemSystem + active HomeContainerSystem)
            int boxCnt = 0;
            try
            {
                IGameSystem gs = CGameSystem.Instance;
                IHomeContainerSystem hcs = (gs != null) ? gs.homeContainerSystem : null;
                byte* bbase = b + 0x950;
                long[] seenIds = new long[16];

                CHomeItemSystem his = (gs != null) ? (gs.homeItemSystem as CHomeItemSystem) : null;
                var homeData = GetHomeItemData(his);
                if (homeData != null)
                {
                    foreach (Dictionary<long, HomeItemPlacementData> homeMap in homeData.Values)
                    {
                        if (homeMap == null) continue;
                        foreach (HomeItemPlacementData p in homeMap.Values)
                        {
                            if (boxCnt >= 16) break;
                            if (!IsStorageBoxPlacement(p)) continue;
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
                            *(int*)(brow + 0x08) = 50;
                            *(int*)(brow + 0x0C) = used;
                            seenIds[boxCnt] = p.lInstanceId;
                            boxCnt++;
                        }
                    }
                }

                CHomeContainerSystem hcsImpl = hcs as CHomeContainerSystem;
                var containers = GetHomeContainers(hcsImpl);
                if (containers != null && boxCnt < 16)
                {
                    foreach (KeyValuePair<long, CHomeContainer> kv in containers)
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
                        *(int*)(brow + 0x08) = 50;
                        *(int*)(brow + 0x0C) = used;
                        seenIds[boxCnt] = iid;
                        boxCnt++;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModBridge] UpdateSnapshot boxes ex: " + ex.Message);
            }
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

        private unsafe void UpdateSnapshotLAN(LAN.CLanEntity lanPlayer)
        {
            byte* b = Shm();
            if (b == null || lanPlayer == null || lanPlayer.propertyPart == null) return;

            LAN.CLanPropertyPart pp = lanPlayer.propertyPart;
            int cnt = ALL_PROP_KEYS.Length;
            if (cnt > 64) cnt = 64;
            *(int*)(b + 0x444) = cnt;
            byte* pbase = b + 0x448;
            for (int i = 0; i < cnt; i++)
            {
                int k = ALL_PROP_KEYS[i];
                int def = (k == 601) ? 1000 : ((k == 1 || k == 52) ? 1 : 0);
                int cur = pp.GetValue(ePropValueType.AfterCul, k, def);
                int orig = pp.GetValue(ePropValueType.Original, k, def);
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

            // LAN storage boxes count = 0
            *(int*)(b + 0x948) = 0;

            // Active mod buffs snapshot
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
        private void MaintainInstantInteract(bool on, CEntity player, LAN.CLanEntity lanPlayer)
        {
            DGameConfig cfg = DGameConfig.Instance;
            if (cfg != null)
            {
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

            if (!on) return;

            // Single-player: Complete ALL Operate behaviors instantly (gathering, looting, chests, tools, trees, mining, sawing)
            if (player != null)
            {
                try
                {
                    CMainCharacter mc = player.GetComponent(typeof(CMainCharacter)) as CMainCharacter;
                    if (mc != null && mc.GetStateID() == 4)
                    {
                        CCharacterStateBase st = GetCurState(mc);
                        if (st != null)
                        {
                            CCharacterBehaviorBase beh = GetBehavior(st);
                            if (beh != null)
                            {
                                string bName = beh.GetType().Name;
                                if (bName.Contains("Operate"))
                                {
                                    if (beh is CMainCharacterBehavior_Operate_Tool)
                                    {
                                        if (s_toolDataFi == null)
                                        {
                                            s_toolDataFi = typeof(CMainCharacterBehavior_Operate_Tool).GetField("m_pData", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                                        }
                                        if (s_toolDataFi != null)
                                        {
                                            object dObj = s_toolDataFi.GetValue(beh);
                                            if (dObj is sUIInputOperateData)
                                            {
                                                sUIInputOperateData data = (sUIInputOperateData)dObj;
                                                if (data.pTargetOperatePoint != null)
                                                {
                                                    data.pTargetOperatePoint.ReduceHP(999999);
                                                    if (data.onFinishCallback != null)
                                                    {
                                                        data.onFinishCallback(data.pTargetOperatePoint, data.inputData);
                                                    }
                                                }
                                            }
                                        }
                                    }

                                    if (s_miSpBehEnd == null)
                                    {
                                        s_miSpBehEnd = typeof(CCharacterBehaviorBase).GetMethod("End", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                                    }
                                    if (s_miSpBehEnd != null) s_miSpBehEnd.Invoke(beh, null);
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            // LAN / Solo Challenge operate behaviors
            if (lanPlayer != null)
            {
                try
                {
                    LAN.CLanMainCharacter lmc = lanPlayer.GetComponent(typeof(LAN.CLanMainCharacter)) as LAN.CLanMainCharacter;
                    if (lmc != null && lmc.GetStateID() == 4) // State 4 is Operate
                    {
                        if (s_fiLanCurState == null)
                        {
                            s_fiLanCurState = typeof(LAN.CLanCharacterBase).GetField("m_pCurState", BindingFlags.Instance | BindingFlags.NonPublic);
                        }
                        if (s_fiLanCurState != null)
                        {
                            object curSt = s_fiLanCurState.GetValue(lmc);
                            if (curSt != null)
                            {
                                RangedBehMeta stMeta = GetTypeMeta(curSt.GetType());
                                if (stMeta.fBehavior != null)
                                {
                                    object lbeh = stMeta.fBehavior.GetValue(curSt);
                                    if (lbeh != null)
                                    {
                                        RangedBehMeta behMeta = GetTypeMeta(lbeh.GetType());
                                        if (behMeta.miEnd != null) behMeta.miEnd.Invoke(lbeh, null);
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
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

            List<int> listPkg = GetPayListPackage(ps);
            List<int> listDiamond = GetPayDiamondPackage(ps);
            List<int> listLimit = GetPayLimitGift(ps);
            Dictionary<int, int> dlcMap = GetDlcInstall(dlc);

            if (!_paidApplied)
            {
                _origPkg = (listPkg != null) ? listPkg.ToArray() : new int[0];
                _origDiamond = (listDiamond != null) ? listDiamond.ToArray() : new int[0];
                _origLimit = (listLimit != null) ? listLimit.ToArray() : new int[0];
                if (dlcMap != null)
                {
                    int c = dlcMap.Count;
                    _origDlcKeys = new int[c];
                    _origDlcVals = new int[c];
                    int k = 0;
                    foreach (KeyValuePair<int, int> kv in dlcMap)
                    {
                        _origDlcKeys[k] = kv.Key;
                        _origDlcVals[k] = kv.Value;
                        k++;
                    }
                }
                _paidApplied = true;
            }

            int beforeCount = (listPkg != null ? listPkg.Count : 0) + (listLimit != null ? listLimit.Count : 0) + (dlcMap != null ? dlcMap.Count : 0);

            // Baseline IDs: Shop 11..35, Bundles 101..115, Story DLCs 201..220, Holiday/Bundles 200..310
            for (int id = 1; id <= 40; id++)
            {
                AddUnique(listPkg, id);
                AddUnique(listDiamond, id);
                AddUnique(listLimit, id);
            }
            for (int id = 100; id <= 115; id++)
            {
                AddUnique(listPkg, id);
                AddUnique(listLimit, id);
            }
            for (int id = 200; id <= 310; id++)
            {
                AddUnique(listPkg, id);
                AddUnique(listLimit, id);
                if (dlcMap != null) dlcMap[id] = 1;
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
                                AddUnique(listPkg, l[i].nId);
                                if (l[i].listBindID != null)
                                    for (int j = 0; j < l[i].listBindID.Count; j++) AddUnique(listPkg, l[i].listBindID[j]);
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
                                AddUnique(listLimit, l[i].nId);
                                AddUnique(listPkg, l[i].nId);
                                if (l[i].listBindID != null)
                                    for (int j = 0; j < l[i].listBindID.Count; j++)
                                    {
                                        AddUnique(listPkg, l[i].listBindID[j]);
                                        AddUnique(listLimit, l[i].listBindID[j]);
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
                                if (l[i] != null) { AddUnique(listDiamond, l[i].nId); AddUnique(listPkg, l[i].nId); }
                    }
                    CPayDLCConfigLoader pDlc = dc.GetConfigLoader(eGameConfigType.PayDLC) as CPayDLCConfigLoader;
                    if (pDlc != null)
                    {
                        List<sPayDLCConfig> l = pDlc.GetAllConfig();
                        if (l != null)
                            for (int i = 0; i < l.Count; i++)
                                if (l[i] != null)
                                {
                                    AddUnique(listPkg, l[i].nId);
                                    if (dlcMap != null) dlcMap[l[i].nId] = 1;
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
                                    AddUnique(listPkg, l[i].nDependentShop);
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
                    var holConf = GetHolidayConfig(pHol); if (holConf != null)
                    {
                        foreach (int hk in holConf.Keys)
                        {
                            AddUnique(listPkg, hk);
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

            int afterCount = (listPkg != null ? listPkg.Count : 0) + (listLimit != null ? listLimit.Count : 0) + (dlcMap != null ? dlcMap.Count : 0);
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

            SetStatus(1, "Paid unlock active (" + (listPkg != null ? listPkg.Count : 0).ToString() + " pkgs)");
            return 1;
        }

        private int RestorePaidUnlock(IGameSystem gs)
        {
            if (!_paidApplied || gs == null) return 0;
            CPaySystem ps = gs.paySystem as CPaySystem;
            CDLCSystem dlc = gs.dLCSystem as CDLCSystem;
            if (ps != null)
            {
                List<int> listPkg = GetPayListPackage(ps);
                if (listPkg != null && _origPkg != null)
                {
                    listPkg.Clear();
                    for (int i = 0; i < _origPkg.Length; i++) listPkg.Add(_origPkg[i]);
                }
                List<int> listDiamond = GetPayDiamondPackage(ps);
                if (listDiamond != null && _origDiamond != null)
                {
                    listDiamond.Clear();
                    for (int i = 0; i < _origDiamond.Length; i++) listDiamond.Add(_origDiamond[i]);
                }
                List<int> listLimit = GetPayLimitGift(ps);
                if (listLimit != null && _origLimit != null)
                {
                    listLimit.Clear();
                    for (int i = 0; i < _origLimit.Length; i++) listLimit.Add(_origLimit[i]);
                }
            }
            Dictionary<int, int> dlcMap = GetDlcInstall(dlc);
            if (dlcMap != null && _origDlcKeys != null)
            {
                dlcMap.Clear();
                for (int i = 0; i < _origDlcKeys.Length; i++) dlcMap[_origDlcKeys[i]] = _origDlcVals[i];
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
        private void MaintainNoclip(GameObject go, bool on)
        {
            if (!on || go == null)
            {
                if (_noclipActive && _noclipCols != null)
                {
                    for (int i = 0; i < _noclipCols.Length; i++)
                    {
                        if (_noclipCols[i] != null) _noclipCols[i].isTrigger = false;
                    }
                    _noclipCols = null;
                    _noclipGo = null;
                    _noclipActive = false;
                }
                return;
            }

            if (_noclipGo != go || !_noclipActive || (_tick % 20u) == 0u)
            {
                if (_noclipGo != go && _noclipCols != null)
                {
                    for (int i = 0; i < _noclipCols.Length; i++)
                    {
                        if (_noclipCols[i] != null) _noclipCols[i].isTrigger = false;
                    }
                    _noclipCols = null;
                }

                Component[] raw = go.GetComponentsInChildren(typeof(Collider2D), true);
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
                _noclipGo = go;
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

        private bool IsAnyJoystickCancelling()
        {
            try
            {
                bool needRescan = (_cachedJoysticks == null) || ((_tick - _lastJoystickScanTick) >= 180u);
                if (!needRescan)
                {
                    for (int i = 0; i < _cachedJoysticks.Length; i++)
                    {
                        if (_cachedJoysticks[i] == null) { needRescan = true; break; }
                    }
                }
                if (needRescan)
                {
                    _lastJoystickScanTick = _tick;
                    UnityEngine.Object[] raw = UnityEngine.Object.FindObjectsOfType(typeof(CUIJoystick));
                    if (raw != null && raw.Length > 0)
                    {
                        _cachedJoysticks = new CUIJoystick[raw.Length];
                        for (int i = 0; i < raw.Length; i++) _cachedJoysticks[i] = raw[i] as CUIJoystick;
                    }
                    else
                    {
                        _cachedJoysticks = null;
                    }
                }
                if (_cachedJoysticks != null)
                {
                    for (int i = 0; i < _cachedJoysticks.Length; i++)
                    {
                        CUIJoystick j = _cachedJoysticks[i];
                        if (j != null && IsJoystickCancelling(j)) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private void MaintainMachineGun(CEntity player, uint mgRateX10)
        {
            CMainCharacter mc = player.GetComponent(typeof(CMainCharacter)) as CMainCharacter;
            if (mc == null || !GetInputAttack(mc)) return;
            CCharacterStateBase st = GetCurState(mc);
            if (st == null || st.GetStateEnum() != 3) return;
            int bid = st.GetBehaviorID();
            if (!IsRangedBehaviorId(bid)) return;

            // Pause firing if player dragged attack joystick into cancel area
            if (IsAnyJoystickCancelling()) return;

            float now = Time.realtimeSinceStartup;
            if (now < _nextMgFireTime) return;
            float interval = 10f / (float)mgRateX10;
            _nextMgFireTime = now + interval;

            CCharacterBehaviorBase beh = GetBehavior(st);
            if (beh == null) return;
            try
            {
                if (((uint)EditorPrefsX.endianDiff1 & (1u << 12)) != 0u)
                {
                    unsafe { MaintainAimbot(Shm(), player, (uint)EditorPrefsX.endianDiff1); }
                }
                FireRangedBehaviorGeneric(beh);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModBridge] MG fire ex: " + ex.Message);
            }
        }

        private static void FireRangedBehaviorGeneric(object beh)
        {
            if (beh == null) return;
            RangedBehMeta meta = GetTypeMeta(beh.GetType());
            if (meta.fCharge != null) meta.fCharge.SetValue(beh, 1f);
            if (meta.fAttackDo != null) meta.fAttackDo.SetValue(beh, false);
            if (meta.miAttack != null) meta.miAttack.Invoke(beh, null);
        }

        private static void FireRangedBehavior(CCharacterBehaviorBase beh)
        {
            FireRangedBehaviorGeneric(beh);
        }

        private void MaintainMachineGunLAN(LAN.CLanEntity lanPlayer, uint mgRateX10)
        {
            LAN.CLanMainCharacter mc = lanPlayer.GetComponent(typeof(LAN.CLanMainCharacter)) as LAN.CLanMainCharacter;
            if (mc == null) return;
            if (mc.GetStateID() != 3) return; // State 3 is Attack

            if (IsAnyJoystickCancelling()) return;

            float now = Time.realtimeSinceStartup;
            if (now < _nextMgFireTime) return;
            float interval = 10f / (float)mgRateX10;
            _nextMgFireTime = now + interval;

            if (s_fiLanCurState == null)
            {
                s_fiLanCurState = typeof(LAN.CLanCharacterBase).GetField("m_pCurState", BindingFlags.Instance | BindingFlags.NonPublic);
            }
            if (s_fiLanCurState == null) return;
            object st = s_fiLanCurState.GetValue(mc);
            if (st == null) return;
            RangedBehMeta stMeta = GetTypeMeta(st.GetType());
            if (stMeta.fBehavior == null) return;
            LAN.CLanCharacterBehaviorBase beh = stMeta.fBehavior.GetValue(st) as LAN.CLanCharacterBehaviorBase;
            if (beh == null) return;
            try
            {
                FireRangedBehaviorGeneric(beh);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModBridge] MG LAN fire ex: " + ex.Message);
            }
        }

        private static void FireRangedBehaviorLAN(LAN.CLanCharacterBehaviorBase beh)
        {
            FireRangedBehaviorGeneric(beh);
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

                ExportDynTalentsAndSkillsConfigs(dc, lm, dir, apkIcons, iconDir, missingIcons, queuedSet);
                ExportDynHireConfigs(dc, lm, dir);
                ExportDynUnits(dc, lm, dir);
                ExportDynMaps(dc, lm, dir);
                ExportDynTasks(dc, lm, dir);
                ExportDynMapState(CGameSystem.Instance, true);
                ExportDynTaskState(CGameSystem.Instance, _lastCid, true);
                ExportDynLoginState(b, true);

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

        private static readonly UTF8Encoding s_utf8NoBom = new UTF8Encoding(false);

        private static void WriteAtomicText(string dst, string text)
        {
            string tmp = dst + ".tmp";
            File.WriteAllText(tmp, text, s_utf8NoBom);
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

        // ==================== #14 Warehouse Deletion & Dynamic JSON ====================
        private int CmdDeleteWarehouseItem(IGameSystem gs, int delCount, long boxId, ulong matUID)
        {
            if (gs == null || gs.homeContainerSystem == null || gs.materialSystem == null)
            {
                SetStatus(-2, "Warehouse system not ready");
                return -2;
            }
            CHomeContainerSystem hcs = gs.homeContainerSystem as CHomeContainerSystem;
            if (hcs == null)
            {
                SetStatus(-2, "HomeContainerSystem null");
                return -2;
            }
            CHomeContainer hc = hcs.TryGetContainer(boxId);
            if (hc == null)
            {
                SetStatus(-3, "Box container not found: " + boxId.ToString());
                return -3;
            }
            CMaterial mat = gs.materialSystem.GetMaterialByUID(matUID);
            if (mat == null)
            {
                SetStatus(-4, "Material not found: " + matUID.ToString());
                return -4;
            }
            string matName = (mat.pConfig != null) ? mat.pConfig.szStringName : "Item";
            int actualDel = 0;
            if (delCount <= 0 || delCount >= mat.nNumber)
            {
                actualDel = mat.nNumber;
                hc.RemoveMaterial(matUID);
                gs.materialSystem.DestroyMaterial(matUID, true);
                if (hc.IsEmpty())
                {
                    hcs.RemoveContainerIfEmpty(boxId);
                }
            }
            else
            {
                actualDel = delCount;
                mat.SetNum(mat.nNumber - delCount, true);
            }

            RefreshOpenStorageBoxUi();

            ExportDynWarehouse(gs);
            SetStatus(1, "Deleted " + actualDel.ToString() + "x " + matName);
            return 1;
        }

        private unsafe void ExportDynWarehouse(IGameSystem gs)
        {
            if (gs == null || gs.homeContainerSystem == null || gs.materialSystem == null) return;
            CHomeContainerSystem hcs = gs.homeContainerSystem as CHomeContainerSystem;
            var containers = GetHomeContainers(hcs);
            if (containers == null) return;
            byte* b = Shm();
            if (b == null) return;
            string dir = ReadShmDynDir(b);
            if (string.IsNullOrEmpty(dir)) return;

            try
            {
                StringBuilder sb = new StringBuilder(4096);
                sb.Append('[');
                bool first = true;
                foreach (KeyValuePair<long, CHomeContainer> kv in containers)
                {
                    long boxId = kv.Key;
                    CHomeContainer hc = kv.Value;
                    if (hc == null) continue;
                    List<ulong> uids = hc.GetMaterialUIDList();
                    if (uids == null) continue;
                    for (int i = 0; i < uids.Count; i++)
                    {
                        ulong uid = uids[i];
                        CMaterial m = gs.materialSystem.GetMaterialByUID(uid);
                        if (m == null || m.pConfig == null || m.nNumber <= 0) continue;
                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append("{\"boxId\":").Append(boxId.ToString())
                          .Append(",\"uid\":").Append(uid.ToString())
                          .Append(",\"id\":").Append(m.nConfigID.ToString())
                          .Append(",\"name\":\"").Append(JsonEsc(m.pConfig.szStringName))
                          .Append("\",\"num\":").Append(m.nNumber.ToString())
                          .Append(",\"end\":").Append(m.nEndurance.ToString())
                          .Append(",\"icon\":\"").Append(JsonEsc(m.pConfig.szIcon ?? ""))
                          .Append("\"}");
                    }
                }
                sb.Append(']');
                WriteAtomicText(dir + "/dyn_warehouse.json", sb.ToString());
            }
            catch { }
        }

        // ==================== #15 Native Currency System ====================
        private int CmdSetCurrency(IGameSystem gs, int type, int val)
        {
            if (gs == null || gs.paySystem == null) return -2;
            CPaySystem ps = gs.paySystem as CPaySystem;
            if (ps == null) return -2;
            if (val < 0) val = 0;
            if (type == 0) ps.SetDiamondNum(val);
            else if (type == 1) ps.SetGardenCoinNum(val);
            else if (type == 2) ps.SetGardenPointNum(val);
            else if (type == 3) ps.SetCumulativePayment(val);
            ExportDynCurrency(gs);
            SetStatus(1, "Currency type " + type.ToString() + "=" + val.ToString());
            return 1;
        }

        private unsafe void ExportDynCurrency(IGameSystem gs)
        {
            if (gs == null || gs.paySystem == null) return;
            CPaySystem ps = gs.paySystem as CPaySystem;
            if (ps == null) return;
            byte* b = Shm();
            if (b == null) return;
            string dir = ReadShmDynDir(b);
            if (string.IsNullOrEmpty(dir)) return;

            try
            {
                string json = "{\"diamond\":" + ps.GetDiamondNum().ToString() +
                              ",\"gardenCoin\":" + ps.GetGardenCoinNum().ToString() +
                              ",\"gardenPoint\":" + ps.GetGardenPointNum().ToString() +
                              ",\"cumulativePayment\":" + ps.GetCumulativePayment().ToString() + "}";
                WriteAtomicText(dir + "/dyn_currency.json", json);
            }
            catch { }
        }

        // ==================== #16 Sewer / Cave Full Brightness ====================
        private void MaintainSewerHighlight(bool on)
        {
            if (!on && !_sewerHighlightApplied) return;
            try
            {
                CDayAndNightSceneDisplay dnd = CDayAndNightSceneDisplay.Instance;
                if (dnd == null) return;
                if (dnd.m_pWorkingRoot != null && dnd.m_pWorkingRoot.activeSelf == on)
                {
                    dnd.m_pWorkingRoot.SetActive(!on);
                }
                if (s_fiDndImageBg == null)
                {
                    s_fiDndImageBg = typeof(CDayAndNightSceneDisplay).GetField("m_pImageBG", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
                if (s_fiDndImageBg != null)
                {
                    Component comp = s_fiDndImageBg.GetValue(dnd) as Component;
                    if (comp != null && comp.gameObject.activeSelf == on)
                    {
                        comp.gameObject.SetActive(!on);
                    }
                }
                _sewerHighlightApplied = on;
            }
            catch { }
        }

        // ==================== #17 Always Open Limited-Time Events ====================
        private void MaintainAlwaysEvents(bool on)
        {
            if (!on)
            {
                _lastEventsCid = 0;
                return;
            }
            bool toggleOn = (_prevFlags & 512u) == 0u;
            bool cidChanged = (_lastCid != 0 && _lastEventsCid != _lastCid);
            if (!toggleOn && !cidChanged && (_tick % 300u) != 0u) return;
            _lastEventsCid = _lastCid;

            try
            {
                CAccountMgr acc = CAccountMgr.Instance;
                if (acc != null)
                {
                    long dayKey = (DateTime.Now - DateTime.MinValue).Days;
                    for (int i = 1; i <= 64; i++)
                    {
                        acc.AddOpenType(dayKey, (HotfixDNetWork.EOpenType_ID)i);
                    }
                }

                UnityEngine.Object[] rawWks = UnityEngine.Object.FindObjectsOfType(typeof(CActivator_Weekend));
                if (rawWks != null)
                {
                    for (int i = 0; i < rawWks.Length; i++)
                    {
                        CActivator_Weekend w = rawWks[i] as CActivator_Weekend;
                        if (w != null)
                        {
                            w.m_bOnlyShowInWeekend = false;
                            if (!w.gameObject.activeSelf) w.gameObject.SetActive(true);
                        }
                    }
                }

                if (CClientGlobal.Instance != null && CClientGlobal.Instance.GetGameDataCenter != null)
                {
                    CActivityOverviewConfigLoader actLoader = CClientGlobal.Instance.GetGameDataCenter.GetConfigLoader<CActivityOverviewConfigLoader>(eGameConfigType.ActivityOverview);
                    if (actLoader != null)
                    {
                        if (s_fiActSorted == null)
                            s_fiActSorted = typeof(CActivityOverviewConfigLoader).GetField("m_listSorted", BindingFlags.Instance | BindingFlags.NonPublic);
                        if (s_fiActSorted != null)
                        {
                            List<sActivityOverviewConfig> configs = s_fiActSorted.GetValue(actLoader) as List<sActivityOverviewConfig>;
                            if (configs != null)
                            {
                                if (s_piActOpen == null) s_piActOpen = typeof(sActivityOverviewConfig).GetProperty("OpenDay");
                                if (s_piActEnd == null) s_piActEnd = typeof(sActivityOverviewConfig).GetProperty("EndDay");
                                for (int i = 0; i < configs.Count; i++)
                                {
                                    if (s_piActOpen != null && s_piActOpen.CanWrite) s_piActOpen.SetValue(configs[i], DateTime.MinValue, null);
                                    if (s_piActEnd != null && s_piActEnd.CanWrite) s_piActEnd.SetValue(configs[i], DateTime.MaxValue, null);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        // ==================== #18 Phonograph Local MP3 Music ====================
        private void MaintainPhonograph(bool on)
        {
            if (!on && _playingMp3Path == null && !_phInjected && (_prevFlags & 1024u) == 0u)
                return;

            try
            {
                if (_cachedPh == null)
                {
                    bool toggleOn = (_prevFlags & 1024u) == 0u;
                    if (!on)
                    {
                        StopLocalMusic();
                        _phInjected = false;
                        return;
                    }
                    if (!toggleOn && (_tick - _lastPhScanTick) < 180u)
                        return;
                    _lastPhScanTick = _tick;
                    _cachedPh = UnityEngine.Object.FindObjectOfType(typeof(CPhonograph)) as CPhonograph;
                    _cachedPhAnim = null;
                }

                CPhonograph ph = _cachedPh;
                if (ph == null)
                {
                    StopLocalMusic();
                    _phInjected = false;
                    return;
                }

                if (on)
                {
                    if (!_phInjected || (_tick % 300u) == 0u)
                    {
                        bool hasCustom = false;
                        for (int i = 0; i < ph.m_listMusic.Count; i++)
                        {
                            if (ph.m_listMusic[i].nMusicID >= 88000) { hasCustom = true; break; }
                        }
                        if (!hasCustom || (_tick % 300u) == 0u)
                        {
                            InjectLocalMusic(ph);
                        }
                        _phInjected = true;
                    }

                    int curMusicId = PlayerPrefs.GetInt(DPlayerPrefs.PHONOGRAPH_MUSICID, 0);
                    if (curMusicId >= 88000)
                    {
                        CPhonograph.sMusicData match = null;
                        for (int i = 0; i < ph.m_listMusic.Count; i++)
                        {
                            if (ph.m_listMusic[i].nMusicID == curMusicId) { match = ph.m_listMusic[i]; break; }
                        }
                        if (match != null && !string.IsNullOrEmpty(match.szFileName))
                        {
                            bool trackChanged = (_playingMp3Path != match.szFileName);
                            if (trackChanged)
                            {
                                PlayLocalMusic(match.szFileName);
                            }
                            if (trackChanged || (_tick % 60u) == 0u)
                            {
                                if (_cachedPhAnim == null) _cachedPhAnim = ph.GetComponent("Animator");
                                if (_cachedPhAnim != null) _cachedPhAnim.SendMessage("Play", "liushengji_playing");
                            }
                        }
                        else
                        {
                            StopLocalMusic();
                        }
                    }
                    else
                    {
                        StopLocalMusic();
                    }
                }
                else
                {
                    StopLocalMusic();
                    ph.m_listMusic.RemoveAll(m => m.nMusicID >= 88000);
                    _phInjected = false;
                }
            }
            catch { }
        }

        private void InjectLocalMusic(CPhonograph ph)
        {
            try
            {
                string[] dirs = new string[] { "/sdcard/Music", "/sdcard/AresMod/Music" };
                List<string> mp3Files = new List<string>();
                for (int d = 0; d < dirs.Length; d++)
                {
                    if (!Directory.Exists(dirs[d]))
                    {
                        try { Directory.CreateDirectory(dirs[d]); } catch { }
                    }
                    if (Directory.Exists(dirs[d]))
                    {
                        string[] files = Directory.GetFiles(dirs[d], "*.mp3");
                        if (files != null)
                        {
                            for (int f = 0; f < files.Length; f++)
                            {
                                if (!mp3Files.Contains(files[f])) mp3Files.Add(files[f]);
                            }
                        }
                    }
                }

                ph.m_listMusic.RemoveAll(m => m.nMusicID >= 88000);
                for (int i = 0; i < mp3Files.Count; i++)
                {
                    CPhonograph.sMusicData m = new CPhonograph.sMusicData();
                    m.nMusicID = 88000 + i;
                    m.nMaterialID = 0;
                    m.szMusicName = "[本地] " + Path.GetFileNameWithoutExtension(mp3Files[i]);
                    m.szFileName = mp3Files[i];
                    ph.m_listMusic.Add(m);
                }
            }
            catch { }
        }

        private void PlayLocalMusic(string path)
        {
            StopLocalMusic();
            try
            {
                _mediaPlayer = new AndroidJavaObject("android.media.MediaPlayer");
                _mediaPlayer.Call("setDataSource", path);
                _mediaPlayer.Call("setLooping", true);
                _mediaPlayer.Call("prepare");
                _mediaPlayer.Call("start");
                _playingMp3Path = path;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModBridge] MediaPlayer failed: " + ex.Message);
                _playingMp3Path = null;
            }
        }

        private void StopLocalMusic()
        {
            if (_mediaPlayer != null)
            {
                try
                {
                    _mediaPlayer.Call("stop");
                    _mediaPlayer.Call("release");
                }
                catch { }
                _mediaPlayer = null;
            }
            _playingMp3Path = null;
        }

        // ==================== #5 Persistent Currency ====================
        private static int ParseJsonIntField(string json, string key, int defVal)
        {
            if (string.IsNullOrEmpty(json)) return defVal;
            string pat = "\"" + key + "\":";
            int p = json.IndexOf(pat, StringComparison.Ordinal);
            if (p < 0) return defVal;
            p += pat.Length;
            while (p < json.Length && (json[p] == ' ' || json[p] == '\t')) p++;
            bool neg = false;
            if (p < json.Length && json[p] == '-') { neg = true; p++; }
            long val = 0;
            bool any = false;
            while (p < json.Length && json[p] >= '0' && json[p] <= '9')
            {
                val = val * 10 + (json[p] - '0');
                any = true;
                p++;
            }
            if (!any) return defVal;
            if (neg) val = -val;
            if (val > int.MaxValue) return int.MaxValue;
            if (val < int.MinValue) return int.MinValue;
            return (int)val;
        }

        private unsafe void MaintainPersistedCurrency(byte* b, IGameSystem gs)
        {
            if (b == null) return;
            string dir = ReadShmDynDir(b);
            if (string.IsNullOrEmpty(dir)) return;
            string persistPath = dir + "/mod_currency_persist.json";

            if (!_currencyLoaded)
            {
                _currencyLoaded = true;
                try
                {
                    if (File.Exists(persistPath))
                    {
                        string raw = File.ReadAllText(persistPath, s_utf8NoBom);
                        if (ParseJsonIntField(raw, "md", 0) != 0)
                            ModPaySystem.ForceSetStatic(0, ParseJsonIntField(raw, "d", 0), false);
                        if (ParseJsonIntField(raw, "mgc", 0) != 0)
                            ModPaySystem.ForceSetStatic(1, ParseJsonIntField(raw, "gc", 0), false);
                        if (ParseJsonIntField(raw, "mgp", 0) != 0)
                            ModPaySystem.ForceSetStatic(2, ParseJsonIntField(raw, "gp", 0), false);
                        if (ParseJsonIntField(raw, "mcp", 0) != 0)
                            ModPaySystem.ForceSetStatic(3, ParseJsonIntField(raw, "cp", 0), false);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[ModBridge] Load currency persist ex: " + ex.Message);
                }
            }

            if (ModPaySystem.IsAnyCurrencyModded())
            {
                ModPaySystem.SyncServerInit();
                if (gs != null && gs.paySystem != null)
                {
                    ModPaySystem mps = gs.paySystem as ModPaySystem;
                    if (mps != null) mps.EnsurePersistedApplied();
                }
            }

            if (ModPaySystem.s_persistDirty)
            {
                ModPaySystem.s_persistDirty = false;
                try
                {
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    WriteAtomicText(persistPath, ModPaySystem.SerializePersistJson());
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[ModBridge] Save currency persist ex: " + ex.Message);
                }
            }
        }

        // ==================== #6 NoShake (Story Normal/Heavy + Online) ====================
        private static void ZeroProShake(ProCamera2DShake pro)
        {
            if (pro == null) return;
            try
            {
                BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                Type t = typeof(ProCamera2DShake);
                if (s_fiProCoros == null) s_fiProCoros = t.GetField("_applyInfluencesCoroutines", bf);
                if (s_fiProShakeCoro == null) s_fiProShakeCoro = t.GetField("_shakeCoroutine", bf);
                if (s_fiProPositions == null) s_fiProPositions = t.GetField("_shakePositions", bf);
                if (s_fiProInfluences == null) s_fiProInfluences = t.GetField("_influences", bf);
                if (s_fiProInfSum == null) s_fiProInfSum = t.GetField("_influencesSum", bf);
                if (s_fiProParent == null) s_fiProParent = t.GetField("_shakeParent", bf);
                if (s_fiProIsConst == null) s_fiProIsConst = t.GetField("_isConstantShaking", bf);

                pro.StopShaking();
                pro.StopAllCoroutines();

                var coros = (s_fiProCoros != null) ? (s_fiProCoros.GetValue(pro) as List<Coroutine>) : null;
                if (coros != null) coros.Clear();
                var positions = (s_fiProPositions != null) ? (s_fiProPositions.GetValue(pro) as List<Vector3>) : null;
                if (positions != null) positions.Clear();
                var inf = (s_fiProInfluences != null) ? (s_fiProInfluences.GetValue(pro) as List<Vector3>) : null;
                if (inf != null) inf.Clear();
                if (s_fiProInfSum != null) s_fiProInfSum.SetValue(pro, Vector3.zero);
                if (s_fiProShakeCoro != null) s_fiProShakeCoro.SetValue(pro, null);
                if (s_fiProIsConst != null) s_fiProIsConst.SetValue(pro, false);

                Transform parent = (s_fiProParent != null) ? (s_fiProParent.GetValue(pro) as Transform) : null;
                if (parent != null)
                {
                    Vector3 lp = parent.localPosition;
                    if (lp.x != 0f || lp.y != 0f)
                        parent.localPosition = new Vector3(0f, 0f, lp.z);
                }
            }
            catch { }
        }

        private void EnforceStoryNoShake()
        {
            try
            {
                CCameraMgr cm = CCameraMgr.Instance;
                if (cm == null) return;
                CCameraShaker shaker = null;
                BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                if (s_fiCamMgrShaker == null)
                    s_fiCamMgrShaker = typeof(CCameraMgr).GetField("m_pCameraShaker", bf);
                if (s_fiCamMgrShaker != null)
                    shaker = s_fiCamMgrShaker.GetValue(cm) as CCameraShaker;
                if (shaker != null)
                {
                    if (s_fiShakerPro == null) s_fiShakerPro = typeof(CCameraShaker).GetField("m_pProCamera2DShake", bf);
                    if (s_fiShakerLock == null) s_fiShakerLock = typeof(CCameraShaker).GetField("m_nLock", bf);
                    if (s_fiShakerPreType == null) s_fiShakerPreType = typeof(CCameraShaker).GetField("m_ePreType", bf);

                    int preType = (s_fiShakerPreType != null) ? Convert.ToInt32(s_fiShakerPreType.GetValue(shaker)) : 0;
                    int lk = (s_fiShakerLock != null) ? Convert.ToInt32(s_fiShakerLock.GetValue(shaker)) : 0;
                    if (preType >= 1) // eCameraShakeType.Normal (1) or Heavy (2)
                    {
                        ProCamera2DShake pro = (s_fiShakerPro != null) ? (s_fiShakerPro.GetValue(shaker) as ProCamera2DShake) : null;
                        if (pro != null) ZeroProShake(pro);
                        if (s_fiShakerLock != null && lk != 0) s_fiShakerLock.SetValue(shaker, 0);
                        if (s_fiShakerPreType != null) s_fiShakerPreType.SetValue(shaker, (eCameraShakeType)0);
                    }
                }
            }
            catch { }
        }

        private static Delegate[] FilterCleanDelegates(Delegate cur, Delegate wrapper, out Delegate cleanCombined)
        {
            cleanCombined = null;
            if (cur == null) return null;
            Delegate[] list = cur.GetInvocationList();
            if (list == null || list.Length == 0) return null;
            List<Delegate> kept = new List<Delegate>(list.Length);
            for (int i = 0; i < list.Length; i++)
            {
                Delegate d = list[i];
                if (d == null) continue;
                if (wrapper != null && (object)d == (object)wrapper) continue;
                if (wrapper != null && d.Method == wrapper.Method && object.ReferenceEquals(d.Target, wrapper.Target)) continue;
                UnityEngine.Object uo = d.Target as UnityEngine.Object;
                if (d.Target is UnityEngine.Object && uo == null) continue;
                kept.Add(d);
            }
            if (kept.Count == 0) return null;
            cleanCombined = Delegate.Combine(kept.ToArray());
            return kept.ToArray();
        }

        private void EnsureGameCamShakeHook()
        {
            try
            {
                if (_hookedGameCamMgr != null)
                {
                    UnityEngine.Object uo = _hookedGameCamMgr as UnityEngine.Object;
                    if (_hookedGameCamMgr is UnityEngine.Object && uo == null) _hookedGameCamMgr = null;
                }
                if (_hookedGameCamMgr == null)
                {
                    Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                    for (int i = 0; i < asms.Length; i++)
                    {
                        Type t = asms[i].GetType("GameCameraMgr");
                        if (t == null) continue;
                        PropertyInfo pi = t.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
                        if (pi == null) continue;
                        object inst = pi.GetValue(null, null);
                        if (inst == null) continue;
                        FieldInfo fi = t.GetField("onShake", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (fi == null) continue;
                        _hookedGameCamMgr = inst;
                        _fiGameCamOnShake = fi;
                        break;
                    }
                }
                if (_hookedGameCamMgr != null && _fiGameCamOnShake != null)
                {
                    Action<int> cur = _fiGameCamOnShake.GetValue(_hookedGameCamMgr) as Action<int>;
                    if (_wrappedGameCamShake == null)
                    {
                        _wrappedGameCamShake = delegate(int eType)
                        {
                            if (_inShakeHook) return;
                            _inShakeHook = true;
                            try
                            {
                                if (eType == 1 || eType == 2) return;
                                Delegate[] list = _origShakeList;
                                if (list != null)
                                {
                                    for (int i = 0; i < list.Length; i++)
                                    {
                                        Action<int> item = list[i] as Action<int>;
                                        if (item == null) continue;
                                        if (item.Target is UnityEngine.Object && ((UnityEngine.Object)item.Target) == null) continue;
                                        try { item(eType); } catch { }
                                    }
                                }
                            }
                            finally
                            {
                                _inShakeHook = false;
                            }
                        };
                    }
                    if ((object)cur != (object)_wrappedGameCamShake)
                    {
                        Delegate combined;
                        _origShakeList = FilterCleanDelegates(cur, _wrappedGameCamShake, out combined);
                        _origGameCamShake = combined as Action<int>;
                        _fiGameCamOnShake.SetValue(_hookedGameCamMgr, _wrappedGameCamShake);
                    }
                }
            }
            catch { }
        }

        private void RestoreGameCamShakeHook()
        {
            try
            {
                if (_hookedGameCamMgr != null && _fiGameCamOnShake != null)
                {
                    Action<int> cur = _fiGameCamOnShake.GetValue(_hookedGameCamMgr) as Action<int>;
                    Delegate combined;
                    FilterCleanDelegates(cur, _wrappedGameCamShake, out combined);
                    Action<int> cleanCur = combined as Action<int>;
                    Action<int> target = cleanCur != null ? cleanCur : _origGameCamShake;
                    _fiGameCamOnShake.SetValue(_hookedGameCamMgr, target);
                }
            }
            catch { }
            _hookedGameCamMgr = null;
            _origGameCamShake = null;
            _origShakeList = null;
        }

        // ==================== #7 Shared Hostile Collector, Aimbot & ESP ====================
        private static FieldInfo s_fiSceneEntities = null;
        private static PropertyInfo s_piColliderBounds = null;
        private static bool s_checkedColBounds = false;

        private static string StripRichTags(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.IndexOf('<') < 0) return s;
            System.Text.StringBuilder sb = new System.Text.StringBuilder(s.Length);
            bool inTag = false;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '<') { inTag = true; continue; }
                if (ch == '>') { inTag = false; continue; }
                if (!inTag) sb.Append(ch);
            }
            return sb.ToString().Trim();
        }

        private string ResolveEntityName(CEntity ent)
        {
            if (ent == null) return "Unknown";
            int id = ent.nConfigID;
            if (id > 0)
            {
                string cached;
                if (_unitNameCache.TryGetValue(id, out cached)) return cached;
                string nm = StripRichTags(ResolveIdName(id, ""));
                if (!string.IsNullOrEmpty(nm))
                {
                    _unitNameCache[id] = nm;
                    return nm;
                }
            }
            try
            {
                string gn = ent.gameObject != null ? ent.gameObject.name : "";
                if (!string.IsNullOrEmpty(gn))
                {
                    int cl = gn.IndexOf("(Clone)", StringComparison.Ordinal);
                    if (cl > 0) gn = gn.Substring(0, cl);
                    return StripRichTags(gn);
                }
            }
            catch { }
            return "#" + id.ToString();
        }

        private static string ResolveIdName(int id, string fallbackTpl)
        {
            try
            {
                IGameDataCenter dc = CClientGlobal.Instance != null ? CClientGlobal.Instance.GetGameDataCenter : null;
                CLanguageMgr lm = CLanguageMgr.Instance;
                if (dc != null)
                {
                    CUnitConfigLoader ul = dc.GetConfigLoader(eGameConfigType.Unit) as CUnitConfigLoader;
                    if (ul != null)
                    {
                        sUnitConfig u = ul.GetConfig(id);
                        if (u != null)
                        {
                            string s = !string.IsNullOrEmpty(u.szStringName) ? u.szStringName : (u.nStringName > 0 && lm != null ? lm.GetString(u.nStringName) : "");
                            if (!string.IsNullOrEmpty(s)) return s;
                        }
                    }
                    CWorkerConfigLoader wl = dc.GetConfigLoader(eGameConfigType.Worker) as CWorkerConfigLoader;
                    if (wl != null)
                    {
                        sWorkerConfig w = wl.GetConfig(id);
                        if (w != null)
                        {
                            string s = !string.IsNullOrEmpty(w.szStringName) ? w.szStringName : (w.nStringName > 0 && lm != null ? lm.GetString(w.nStringName) : "");
                            if (!string.IsNullOrEmpty(s)) return s;
                        }
                    }
                    CCharacterConfigLoader cl = dc.GetConfigLoader(eGameConfigType.Character) as CCharacterConfigLoader;
                    if (cl != null)
                    {
                        sCharacterConfig c = cl.GetConfig(id);
                        if (c != null)
                        {
                            string s = !string.IsNullOrEmpty(c.szStringName) ? c.szStringName : (c.nStringName > 0 && lm != null ? lm.GetString(c.nStringName) : "");
                            if (!string.IsNullOrEmpty(s)) return s;
                        }
                    }
                }
            }
            catch { }
            if (!string.IsNullOrEmpty(fallbackTpl))
            {
                string t = fallbackTpl.Replace(".prefab", "").Replace("Hire_", "").Replace("hire_", "").Replace("NPC_", "");
                if (!string.IsNullOrEmpty(t)) return t;
            }
            return "#" + id.ToString();
        }

        private static int ClassifyHostile(CEntity player, CFightObj pFo, int pCamp, CEntity ent, bool includeNeutral)
        {
            if (ent == null || ent == player || ent.gameObject == null || !ent.gameObject.activeInHierarchy)
                return 0;
            CFightObj fo = ent.fightObj as CFightObj;
            if (fo == null || fo.IsDead()) return 0;
            if (fo.GetMaxHP() <= 0 || fo.GetCurHP() <= 0) return 0;

            string foName = fo.GetType().Name;
            if (foName.IndexOf("PiaoLiuTong", StringComparison.OrdinalIgnoreCase) >= 0) return 0;

            int eCamp = fo.CampID;
            if (eCamp == pCamp) return 0;
            if (ent.GetComponent(typeof(ModForceAllyFollower)) != null) return 0;
            if (ent.GetComponent(typeof(CAIFSM_HireTeamate)) != null) return 0;

            bool explicitFriend = false;
            if (pFo != null && pFo.dicCamp != null)
            {
                CFightObj.sCampNode pNode;
                if (pFo.dicCamp.TryGetValue(eCamp, out pNode))
                {
                    if (pNode.eCurCamp == CFightObj.eCampEnum.Enemy) return 1;
                    if (pNode.eCurCamp == CFightObj.eCampEnum.Friend) explicitFriend = true;
                }
            }
            if (fo.dicCamp != null)
            {
                CFightObj.sCampNode eNode;
                if (fo.dicCamp.TryGetValue(pCamp, out eNode))
                {
                    if (eNode.eCurCamp == CFightObj.eCampEnum.Enemy) return 1;
                    if (eNode.eCurCamp == CFightObj.eCampEnum.Friend) explicitFriend = true;
                }
            }
            if (fo.GetCurTarget() == player) return 1;
            if (eCamp == 1916) return 1;

            if (!includeNeutral) return 0;
            if (explicitFriend && eCamp != 97 && eCamp != 98 && eCamp != 99) return 0;

            if (ent.movementPart != null || ent.GetComponent(typeof(CAIFSM)) != null || ent.nConfigID > 0)
            {
                return 2;
            }
            return 0;
        }

        private SortedDictionary<ulong, CEntity> GetSceneEntitiesDict()
        {
            CGameSceneMgr mgr = CGameSceneMgr.Instance;
            if (mgr == null) return null;
            if (s_fiSceneEntities == null)
            {
                s_fiSceneEntities = typeof(CGameSceneMgr).GetField("m_dicEntity", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            }
            return (s_fiSceneEntities != null) ? (s_fiSceneEntities.GetValue(mgr) as SortedDictionary<ulong, CEntity>) : null;
        }

        private void CollectHostiles(CEntity player, float maxRange, bool includeNeutral)
        {
            _hostileCache.Clear();
            _hostileTypeCache.Clear();
            _hostileDistCache.Clear();
            if (player == null) return;
            SortedDictionary<ulong, CEntity> dic = GetSceneEntitiesDict();
            if (dic == null || dic.Count == 0) return;

            CFightObj pFo = player.fightObj as CFightObj;
            int pCamp = (pFo != null) ? pFo.CampID : 0;
            Vector3 pPos = player.transform.position;
            float maxSq = (maxRange > 0.1f) ? (maxRange * maxRange) : 0f;

            foreach (KeyValuePair<ulong, CEntity> kv in dic)
            {
                CEntity ent = kv.Value;
                int htype = ClassifyHostile(player, pFo, pCamp, ent, includeNeutral);
                if (htype == 0) continue;
                Vector3 ePos = ent.transform.position;
                float dx = ePos.x - pPos.x;
                float dy = ePos.y - pPos.y;
                float dSq = dx * dx + dy * dy;
                if (maxSq > 0f && dSq > maxSq) continue;
                _hostileCache.Add(ent);
                _hostileTypeCache.Add(htype);
                _hostileDistCache.Add(Mathf.Sqrt(dSq));
            }
        }

        private CEntity FindNearestAimTarget(CEntity player, float maxRange, bool ignoreWall)
        {
            CollectHostiles(player, maxRange, false);
            if (_hostileCache.Count == 0) return null;

            Vector2 pPos = player.transform.position;
            CEntity best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < _hostileCache.Count; i++)
            {
                float d = _hostileDistCache[i];
                if (d >= bestDist) continue;
                CEntity cand = _hostileCache[i];
                if (cand == null || cand.transform == null) continue;
                if (!ignoreWall)
                {
                    try
                    {
                        Vector2 tPos = cand.transform.position;
                        if ((tPos - pPos).sqrMagnitude > 1e-4f && !CNavmeshUtil.CheckLOS(pPos, tPos)) continue;
                    }
                    catch { }
                }
                bestDist = d;
                best = cand;
            }
            return best;
        }

        private void ForceRotatePlayer(CEntity player, Vector2 dir)
        {
            if (player == null || dir.sqrMagnitude < 1e-6f) return;
            Vector2 n = dir.normalized;
            try
            {
                if (player.rotationPart != null)
                {
                    player.rotationPart.Rotate2Dir(new Vector3(n.x, n.y, 0f), true);
                    if (s_miRotateImm == null)
                    {
                        s_miRotateImm = typeof(CRotationPart).GetMethod("ApplyImmediateRotation", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    }
                    if (s_miRotateImm != null)
                    {
                        s_miRotateImm.Invoke(player.rotationPart, null);
                    }
                }
                else
                {
                    player.transform.right = new Vector3(n.x, n.y, 0f);
                }
            }
            catch { }
        }

        private bool IsPropLocked(int key)
        {
            for (int i = 0; i < _lockCount; i++)
            {
                if (_lockKeys[i] == key) return true;
            }
            return false;
        }

        private void MaintainAimbotWeaponAngles(CEntity player, bool aimOn)
        {
            if (player == null || player.propertyPart == null || player.gameObject == null)
            {
                if (!aimOn)
                {
                    _aimAngleApplied = false;
                    _aimAnglePlayer = null;
                }
                return;
            }
            if (_aimAnglePlayer != player)
            {
                _aimAngleApplied = false;
                _aimAnglePlayer = player;
            }
            CPropertyPart pp = player.propertyPart;
            float scale = (DGameConfig.Instance != null && DGameConfig.Instance.fPropertyScale > 1f) ? DGameConfig.Instance.fPropertyScale : 1000f;
            int targetRaw = Mathf.RoundToInt(0.5f * scale);

            if (aimOn)
            {
                if (!_aimAngleApplied)
                {
                    _aimSavedOrig206 = pp.IsExistValue(206) ? pp.GetValue(ePropValueType.Original, 206, 0) : 0;
                    _aimSavedOrig207 = pp.IsExistValue(207) ? pp.GetValue(ePropValueType.Original, 207, 0) : 0;
                    _aimAngleApplied = true;
                }
                if (!IsPropLocked(206) && ReadPropCurRaw(pp, 206) != targetRaw)
                {
                    SetPropRaw(pp, 206, targetRaw);
                }
                if (!IsPropLocked(207) && ReadPropCurRaw(pp, 207) != targetRaw)
                {
                    SetPropRaw(pp, 207, targetRaw);
                }
            }
            else if (_aimAngleApplied)
            {
                if (!IsPropLocked(206) && pp.IsExistValue(206))
                {
                    pp.SetValue(ePropValueType.Original, 206, _aimSavedOrig206);
                }
                if (!IsPropLocked(207) && pp.IsExistValue(207))
                {
                    pp.SetValue(ePropValueType.Original, 207, _aimSavedOrig207);
                }
                _aimAngleApplied = false;
            }
        }

        private void MaintainAimbotWeaponAnglesLAN(LAN.CLanEntity lanPlayer, bool aimOn)
        {
            if (lanPlayer == null || lanPlayer.propertyPart == null || lanPlayer.gameObject == null)
            {
                if (!aimOn)
                {
                    _lanAimAngleApplied = false;
                    _lanAimAnglePlayer = null;
                }
                return;
            }
            if (_lanAimAnglePlayer != lanPlayer)
            {
                _lanAimAngleApplied = false;
                _lanAimAnglePlayer = lanPlayer;
            }
            LAN.CLanPropertyPart pp = lanPlayer.propertyPart;
            float scale = (DGameConfig.Instance != null && DGameConfig.Instance.fPropertyScale > 1f) ? DGameConfig.Instance.fPropertyScale : 1000f;
            int targetRaw = Mathf.RoundToInt(0.5f * scale);

            if (aimOn)
            {
                if (!_lanAimAngleApplied)
                {
                    _lanAimSavedOrig206 = pp.GetValue(ePropValueType.Original, 206, 0);
                    _lanAimSavedOrig207 = pp.GetValue(ePropValueType.Original, 207, 0);
                    _lanAimAngleApplied = true;
                }
                for (int idx = 0; idx < 2; idx++)
                {
                    int k = (idx == 0) ? 206 : 207;
                    if (IsPropLocked(k)) continue;
                    if (pp.GetValue(ePropValueType.AfterCul, k, 0) != targetRaw)
                    {
                        int gain = pp.GetValue(ePropValueType.Gain, k, 0);
                        int pct = pp.GetValue(ePropValueType.Percent, k, 0);
                        float spct = 1f + (float)pct / 100f;
                        int orig = (spct > 0.01f) ? ((int)Mathf.Round((float)targetRaw / spct) - gain) : (targetRaw - gain);
                        pp.SetValue(ePropValueType.Original, k, orig, true);
                    }
                }
            }
            else if (_lanAimAngleApplied)
            {
                if (!IsPropLocked(206)) pp.SetValue(ePropValueType.Original, 206, _lanAimSavedOrig206, true);
                if (!IsPropLocked(207)) pp.SetValue(ePropValueType.Original, 207, _lanAimSavedOrig207, true);
                _lanAimAngleApplied = false;
            }
        }

        private void EnsureInputHook()
        {
            try
            {
                if (CUIMgr.Instance == null) return;
                CUI_Game_Input_MainCharacter wnd = CUIMgr.Instance.GetWnd(CUIDefine.eUIWnd.UI_Game_Input_MainCharacter) as CUI_Game_Input_MainCharacter;
                if (wnd == null)
                {
                    _hookedInputWnd = null;
                    _origInputCb = null;
                    _origInputList = null;
                    return;
                }
                if (_wrappedInputCb == null)
                {
                    _wrappedInputCb = OnWrappedUiInput;
                }
                if (_hookedInputWnd != null && _hookedInputWnd == null)
                {
                    _hookedInputWnd = null;
                    _origInputCb = null;
                    _origInputList = null;
                }
                Action<sUIInputData> cur = wnd.onInputCallback;
                if (wnd != _hookedInputWnd || (object)cur != (object)_wrappedInputCb)
                {
                    _hookedInputWnd = wnd;
                    Delegate combined;
                    _origInputList = FilterCleanDelegates(cur, _wrappedInputCb, out combined);
                    _origInputCb = combined as Action<sUIInputData>;
                    wnd.onInputCallback = _wrappedInputCb;
                }
            }
            catch { }
        }

        private void RestoreInputHook()
        {
            try
            {
                if (_hookedInputWnd != null)
                {
                    Action<sUIInputData> cur = _hookedInputWnd.onInputCallback;
                    Delegate combined;
                    FilterCleanDelegates(cur, _wrappedInputCb, out combined);
                    Action<sUIInputData> cleanCur = combined as Action<sUIInputData>;
                    _hookedInputWnd.onInputCallback = (cleanCur != null) ? cleanCur : _origInputCb;
                }
            }
            catch { }
            _hookedInputWnd = null;
            _origInputCb = null;
            _origInputList = null;
            _inInputHook = false;
        }

        private unsafe void OnWrappedUiInput(sUIInputData data)
        {
            if (_inInputHook) return;
            _inInputHook = true;
            try
            {
                try
                {
                    byte* b = Shm();
                    uint flags = (b != null) ? *(uint*)(b + 0x18) : (uint)EditorPrefsX.endianDiff1;
                    if ((flags & (1u << 12)) != 0u && (data.nJoystickID == 1 || data.nJoystickID == 5))
                    {
                        if (data.eInput == sUIInputData.eInputType.PointerDown ||
                            data.eInput == sUIInputData.eInputType.PointerUpdate ||
                            data.eInput == sUIInputData.eInputType.StartLongPress)
                        {
                            _aimAttackHeld = true;
                        }
                        else if (data.eInput == sUIInputData.eInputType.PointerUp ||
                                 data.eInput == sUIInputData.eInputType.PointerUpLongPress ||
                                 data.eInput == sUIInputData.eInputType.Cancel)
                        {
                            _aimAttackHeld = false;
                        }

                        if (data.eInput != sUIInputData.eInputType.Cancel)
                        {
                            CEntity player = (CGameSceneMgr.Instance != null) ? CGameSceneMgr.Instance.GetCurrentCharacterEntity() : null;
                            if (player != null)
                            {
                                float maxRange = (b != null && *(uint*)(b + 0xE58) > 0u) ? (*(uint*)(b + 0xE58) / 10f) : 0f;
                                bool ignoreWall = (flags & (1u << 15)) != 0u;
                                CEntity target = FindNearestAimTarget(player, maxRange, ignoreWall);
                                if (target != null && target.transform != null)
                                {
                                    Vector2 dir = ((Vector2)(target.transform.position - player.transform.position)).normalized;
                                    if (dir.sqrMagnitude > 1e-6f)
                                    {
                                        data.v2Dir = dir;
                                        data.v2Normalized = dir;
                                        ForceRotatePlayer(player, dir);
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }

                Delegate[] list = _origInputList;
                if (list != null)
                {
                    for (int i = 0; i < list.Length; i++)
                    {
                        Action<sUIInputData> item = list[i] as Action<sUIInputData>;
                        if (item == null) continue;
                        if (item.Target is UnityEngine.Object && ((UnityEngine.Object)item.Target) == null) continue;
                        try { item(data); } catch { }
                    }
                }
            }
            finally
            {
                _inInputHook = false;
            }
        }

        private unsafe void MaintainAimbot(byte* b, CEntity player, uint flags)
        {
            if (player == null) return;
            CMainCharacter mc = player.GetComponent(typeof(CMainCharacter)) as CMainCharacter;
            bool inAttack = _aimAttackHeld;
            if (!inAttack && mc != null)
            {
                if (GetInputAttack(mc)) inAttack = true;
                else
                {
                    CCharacterStateBase st = GetCurState(mc);
                    if (st != null && (st.GetStateEnum() == 3 || st.GetStateEnum() == 10))
                        inAttack = true;
                }
            }
            if (!inAttack) return;

            float maxRange = (b != null && *(uint*)(b + 0xE58) > 0u) ? (*(uint*)(b + 0xE58) / 10f) : 0f;
            bool ignoreWall = (flags & (1u << 15)) != 0u;
            CEntity target = FindNearestAimTarget(player, maxRange, ignoreWall);
            if (target != null)
            {
                Vector2 dir = ((Vector2)(target.transform.position - player.transform.position)).normalized;
                ForceRotatePlayer(player, dir);
            }
        }

        private unsafe void MaintainAimbotLAN(byte* b, LAN.CLanEntity lanPlayer, uint flags)
        {
            if (lanPlayer == null) return;
            LAN.CLanMainCharacter mc = lanPlayer.GetComponent(typeof(LAN.CLanMainCharacter)) as LAN.CLanMainCharacter;
            bool inAttack = _aimAttackHeld || (mc != null && (mc.GetStateID() == 3 || mc.GetStateID() == 10));
            if (!inAttack) return;

            LAN.CLanEntity[] all = UnityEngine.Object.FindObjectsOfType(typeof(LAN.CLanEntity)) as LAN.CLanEntity[];
            if (all == null || all.Length == 0) return;
            LAN.CLanFightObj pFo = lanPlayer.fightObj;
            int pCamp = (pFo != null) ? pFo.campID : 0;
            Vector2 pPos = lanPlayer.transform.position;
            float maxRange = (b != null && *(uint*)(b + 0xE58) > 0u) ? (*(uint*)(b + 0xE58) / 10f) : 0f;
            float maxSq = (maxRange > 0.1f) ? (maxRange * maxRange) : float.MaxValue;
            bool ignoreWall = (flags & (1u << 15)) != 0u;

            LAN.CLanEntity best = null;
            float bestDistSq = maxSq;
            for (int i = 0; i < all.Length; i++)
            {
                LAN.CLanEntity e = all[i];
                if (e == null || e == lanPlayer || !e.gameObject.activeInHierarchy) continue;
                LAN.CLanFightObj fo = e.fightObj;
                if (fo == null || fo.IsDead() || fo.GetCurHP() <= 0 || fo.campID == pCamp) continue;
                bool hostile = (fo.campID == 1916);
                if (!hostile && pFo != null && pFo.dicCamp != null)
                {
                    LAN.CLanFightObj.sCampNode cn;
                    if (pFo.dicCamp.TryGetValue(fo.campID, out cn) && cn.eCurCamp == LAN.CLanFightObj.eCampEnum.Enemy)
                        hostile = true;
                }
                if (!hostile) continue;
                Vector2 tPos = e.transform.position;
                float dSq = (tPos - pPos).sqrMagnitude;
                if (dSq >= bestDistSq) continue;
                if (!ignoreWall)
                {
                    try
                    {
                        if ((tPos - pPos).sqrMagnitude > 1e-4f && !CNavmeshUtil.CheckLOS(pPos, tPos)) continue;
                    }
                    catch { }
                }
                bestDistSq = dSq;
                best = e;
            }
            if (best != null && lanPlayer.rotationPart != null)
            {
                Vector2 dir = ((Vector2)(best.transform.position - lanPlayer.transform.position)).normalized;
                lanPlayer.rotationPart.Rotate2Dir(new Vector3(dir.x, dir.y, 0f), true);
            }
        }

        private static unsafe void WriteEspSeqlockClear(byte* b, ref uint espSeq)
        {
            if (b == null) return;
            if (*(int*)(b + 0xE64) == 0) return;
            uint seq = ++espSeq;
            if ((seq & 1u) == 0u) seq = ++espSeq;
            *(uint*)(b + 0xE60) = seq;
            Thread.MemoryBarrier();
            *(int*)(b + 0xE64) = 0;
            Thread.MemoryBarrier();
            espSeq = seq + 1u;
            *(uint*)(b + 0xE60) = espSeq;
        }

        private bool TryGetEntityWorldBounds(CEntity ent, out Vector3 bMin, out Vector3 bMax)
        {
            Vector3 p = ent.transform.position;
            bMin = new Vector3(p.x - 0.42f, p.y - 0.1f, p.z);
            bMax = new Vector3(p.x + 0.42f, p.y + 1.45f, p.z);

            int cfgId = ent.nConfigID;
            Vector4 cached;
            if (cfgId > 0 && _unitBoundsCache.TryGetValue(cfgId, out cached))
            {
                bMin = new Vector3(p.x + cached.x, p.y + cached.y, p.z);
                bMax = new Vector3(p.x + cached.z, p.y + cached.w, p.z);
                return true;
            }

            if (!s_checkedColBounds)
            {
                s_checkedColBounds = true;
                try { s_piColliderBounds = typeof(Collider2D).GetProperty("bounds", BindingFlags.Instance | BindingFlags.Public); } catch { }
            }
            bool got = false;
            float bestArea = 0f;
            try
            {
                Renderer[] rs = ent.GetComponentsInChildren<Renderer>(false);
                if (rs != null)
                {
                    for (int i = 0; i < rs.Length; i++)
                    {
                        Renderer r = rs[i];
                        if (r == null || !r.enabled) continue;
                        if (!(r is SpriteRenderer || r is MeshRenderer)) continue;
                        Bounds rb = r.bounds;
                        Vector3 sz = rb.size;
                        if (sz.x < 0.2f || sz.y < 0.2f || sz.x > 6f || sz.y > 6f) continue;
                        float area = sz.x * sz.y;
                        if (area > bestArea)
                        {
                            bestArea = area;
                            bMin = rb.min;
                            bMax = rb.max;
                            got = true;
                        }
                    }
                }
            }
            catch { }
            if (!got && s_piColliderBounds != null)
            {
                try
                {
                    Collider2D col = ent.GetComponent<Collider2D>();
                    if (col != null)
                    {
                        Bounds cb = (Bounds)s_piColliderBounds.GetValue(col, null);
                        Vector3 sz = cb.size;
                        if (sz.x >= 0.2f && sz.y >= 0.2f && sz.x <= 6f && sz.y <= 6f)
                        {
                            bMin = cb.min;
                            bMax = cb.max;
                            got = true;
                        }
                    }
                }
                catch { }
            }
            if (got && cfgId > 0 && _unitBoundsCache.Count < 256)
            {
                _unitBoundsCache[cfgId] = new Vector4(bMin.x - p.x, bMin.y - p.y, bMax.x - p.x, bMax.y - p.y);
            }
            return got;
        }

        private static unsafe void WriteUtf8Fixed20(byte* dst, string s)
        {
            int idx = 0;
            if (!string.IsNullOrEmpty(s))
            {
                for (int i = 0; i < s.Length && idx < 19; i++)
                {
                    char ch = s[i];
                    if (ch < 0x80) dst[idx++] = (byte)ch;
                    else if (ch < 0x800 && idx + 2 <= 19)
                    {
                        dst[idx++] = (byte)(0xC0 | (ch >> 6));
                        dst[idx++] = (byte)(0x80 | (ch & 0x3F));
                    }
                    else if (idx + 3 <= 19)
                    {
                        dst[idx++] = (byte)(0xE0 | (ch >> 12));
                        dst[idx++] = (byte)(0x80 | ((ch >> 6) & 0x3F));
                        dst[idx++] = (byte)(0x80 | (ch & 0x3F));
                    }
                }
            }
            while (idx < 20) dst[idx++] = 0;
        }

        private unsafe void UpdateEspOverlay(byte* b, uint flags)
        {
            if (b == null) return;
            if ((flags & (1u << 13)) == 0u || *(int*)(b + 0x10) == 0)
            {
                WriteEspSeqlockClear(b, ref _espSeq);
                return;
            }
            Camera cam = null;
            try
            {
                cam = Camera.main;
                if (cam == null)
                {
                    Camera[] cams = Camera.allCameras;
                    if (cams != null && cams.Length > 0) cam = cams[0];
                }
            }
            catch { }
            int sw = Screen.width;
            int sh = Screen.height;
            if (cam == null || sw <= 0 || sh <= 0)
            {
                WriteEspSeqlockClear(b, ref _espSeq);
                return;
            }

            CEntity player = (CGameSceneMgr.Instance != null) ? CGameSceneMgr.Instance.GetCurrentCharacterEntity() : null;
            if (player == null)
            {
                WriteEspSeqlockClear(b, ref _espSeq);
                return;
            }

            bool includeNeutral = (flags & (1u << 19)) != 0u;
            CollectHostiles(player, 0f, includeNeutral);

            int total = _hostileCache.Count;
            if (total > 256) total = 256;
            for (int i = 0; i < total; i++)
            {
                _espSortIdx[i] = i;
                _espSortDist[i] = _hostileDistCache[i];
            }
            // Insertion sort nearest first
            for (int i = 1; i < total; i++)
            {
                int keyIdx = _espSortIdx[i];
                float keyDist = _espSortDist[keyIdx];
                int j = i - 1;
                while (j >= 0 && _espSortDist[_espSortIdx[j]] > keyDist)
                {
                    _espSortIdx[j + 1] = _espSortIdx[j];
                    j--;
                }
                _espSortIdx[j + 1] = keyIdx;
            }
            int outCount = (total > 64) ? 64 : total;

            Vector3 pScr = cam.WorldToScreenPoint(player.transform.position + new Vector3(0f, 0.7f, 0f));
            float pNx = pScr.x / (float)sw;
            float pNy = 1f - (pScr.y / (float)sh);

            uint seq = ++_espSeq;
            if ((seq & 1u) == 0u) seq = ++_espSeq;
            *(uint*)(b + 0xE60) = seq;
            Thread.MemoryBarrier();
            try
            {
                *(float*)(b + 0xE68) = pNx;
                *(float*)(b + 0xE6C) = pNy;
                *(float*)(b + 0xE70) = (float)sw;
                *(float*)(b + 0xE74) = (float)sh;

                int written = 0;
                for (int i = 0; i < outCount; i++)
                {
                    int srcIdx = _espSortIdx[i];
                    CEntity ent = _hostileCache[srcIdx];
                    if (ent == null || ent.transform == null) continue;
                    byte* rec = b + 0xE78 + written * 64;

                    Vector3 wMin, wMax;
                    TryGetEntityWorldBounds(ent, out wMin, out wMax);
                    Vector3 sMin = cam.WorldToScreenPoint(new Vector3(wMin.x, wMin.y, ent.transform.position.z));
                    Vector3 sMax = cam.WorldToScreenPoint(new Vector3(wMax.x, wMax.y, ent.transform.position.z));
                    Vector3 sCtr = cam.WorldToScreenPoint(ent.transform.position + new Vector3(0f, 0.7f, 0f));

                    float nxMin = Mathf.Min(sMin.x, sMax.x) / (float)sw;
                    float nxMax = Mathf.Max(sMin.x, sMax.x) / (float)sw;
                    float nyTop = 1f - (Mathf.Max(sMin.y, sMax.y) / (float)sh);
                    float nyBot = 1f - (Mathf.Min(sMin.y, sMax.y) / (float)sh);
                    if (nxMax - nxMin < 0.024f)
                    {
                        float mid = 0.5f * (nxMin + nxMax);
                        nxMin = mid - 0.012f;
                        nxMax = mid + 0.012f;
                    }
                    if (nyBot - nyTop < 0.04f)
                    {
                        float mid = 0.5f * (nyTop + nyBot);
                        nyTop = mid - 0.02f;
                        nyBot = mid + 0.02f;
                    }
                    float cx = sCtr.x / (float)sw;
                    float cy = 1f - (sCtr.y / (float)sh);
                    bool onScr = (sCtr.z > 0f && nxMax >= -0.05f && nxMin <= 1.05f && nyBot >= -0.05f && nyTop <= 1.05f);

                    CFightObj fo = ent.fightObj as CFightObj;
                    *(float*)(rec + 0x00) = nxMin;
                    *(float*)(rec + 0x04) = nyTop;
                    *(float*)(rec + 0x08) = nxMax;
                    *(float*)(rec + 0x0C) = nyBot;
                    *(float*)(rec + 0x10) = cx;
                    *(float*)(rec + 0x14) = cy;
                    *(float*)(rec + 0x18) = _hostileDistCache[srcIdx];
                    *(int*)(rec + 0x1C) = (fo != null) ? fo.GetCurHP() : 0;
                    *(int*)(rec + 0x20) = (fo != null) ? fo.GetMaxHP() : 0;
                    *(int*)(rec + 0x24) = ent.nConfigID;
                    *(byte*)(rec + 0x28) = (byte)_hostileTypeCache[srcIdx];
                    *(byte*)(rec + 0x29) = (byte)(onScr ? 1 : 0);
                    *(byte*)(rec + 0x2A) = 0;
                    *(byte*)(rec + 0x2B) = 0;
                    WriteUtf8Fixed20(rec + 0x2C, ResolveEntityName(ent));
                    written++;
                }
                *(int*)(b + 0xE64) = written;
            }
            catch
            {
                *(int*)(b + 0xE64) = 0;
            }
            finally
            {
                Thread.MemoryBarrier();
                _espSeq = seq + 1u;
                *(uint*)(b + 0xE60) = _espSeq;
            }
        }

        // ==================== #8 Talents, Expertise Skills & Active Skills ====================
        private static void ClearAllCharacterTalentBlacklists()
        {
            try
            {
                IGameDataCenter dc = CClientGlobal.Instance != null ? CClientGlobal.Instance.GetGameDataCenter : null;
                if (dc == null) return;
                CCharacterConfigLoader cl = dc.GetConfigLoader(eGameConfigType.Character) as CCharacterConfigLoader;
                if (cl == null) return;
                FieldInfo fi = typeof(CCharacterConfigLoader).GetField("m_dicConfigs", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var dic = (fi != null) ? (fi.GetValue(cl) as Dictionary<int, sCharacterConfig>) : null;
                if (dic == null) return;
                foreach (KeyValuePair<int, sCharacterConfig> kv in dic)
                {
                    if (kv.Value != null && kv.Value.listTalent != null && kv.Value.listTalent.Count > 0)
                        kv.Value.listTalent.Clear();
                }
            }
            catch { }
        }

        private static int GetObjInt(object obj, string[] names, int defVal)
        {
            if (obj == null) return defVal;
            Type t = obj.GetType();
            BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    FieldInfo fi = t.GetField(names[i], bf);
                    if (fi != null) return Convert.ToInt32(fi.GetValue(obj));
                    PropertyInfo pi = t.GetProperty(names[i], bf);
                    if (pi != null) return Convert.ToInt32(pi.GetValue(obj, null));
                }
                catch { }
            }
            return defVal;
        }

        private static string GetObjStr(object obj, string[] names, string defVal)
        {
            if (obj == null) return defVal;
            Type t = obj.GetType();
            BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    FieldInfo fi = t.GetField(names[i], bf);
                    if (fi != null)
                    {
                        object v = fi.GetValue(obj);
                        if (v != null) return v.ToString();
                    }
                    PropertyInfo pi = t.GetProperty(names[i], bf);
                    if (pi != null)
                    {
                        object v = pi.GetValue(obj, null);
                        if (v != null) return v.ToString();
                    }
                }
                catch { }
            }
            return defVal;
        }

        private void ExportDynTalentsAndSkillsConfigs(IGameDataCenter dc, CLanguageMgr lm, string dir,
            Dictionary<string, int> apkIcons, string iconDir, List<string> missingIcons, Dictionary<string, int> queuedSet)
        {
            try
            {
                BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                StringBuilder sb = new StringBuilder(32768);
                sb.Append("{\"talents\":[");
                object tl = dc.GetConfigLoader(eGameConfigType.Talent);
                if (tl != null)
                {
                    FieldInfo fi = tl.GetType().GetField("m_dicConfigs", bf);
                    System.Collections.IDictionary dic = (fi != null) ? (fi.GetValue(tl) as System.Collections.IDictionary) : null;
                    if (dic != null)
                    {
                        bool first = true;
                        string[] fId = new string[] { "nId", "nID" };
                        string[] fType = new string[] { "eTalentType" };
                        string[] fProp = new string[] { "eOperateProperty" };
                        string[] fRate = new string[] { "nRate" };
                        string[] fSz = new string[] { "szStringName" };
                        string[] fNs = new string[] { "nStringName" };
                        string[] fLv = new string[] { "nLevel" };
                        string[] fPar = new string[] { "szOperateParam" };

                        foreach (System.Collections.DictionaryEntry kv in dic)
                        {
                            System.Collections.IList list = kv.Value as System.Collections.IList;
                            if (list == null || list.Count == 0) continue;
                            object rep = list[0];
                            if (rep == null) continue;
                            int tid = GetObjInt(rep, fId, 0);
                            if (tid <= 0) continue;
                            string sName = GetObjStr(rep, fSz, "");
                            int nStr = GetObjInt(rep, fNs, 0);
                            string nm = !string.IsNullOrEmpty(sName) ? sName :
                                (nStr > 0 && lm != null ? lm.GetString(nStr) : ("Talent #" + tid));
                            if (!first) sb.Append(',');
                            first = false;
                            sb.Append("{\"id\":").Append(tid);
                            sb.Append(",\"type\":").Append(GetObjInt(rep, fType, 1));
                            sb.Append(",\"prop\":").Append(GetObjInt(rep, fProp, 0));
                            sb.Append(",\"rate\":").Append(GetObjInt(rep, fRate, 0));
                            sb.Append(",\"name\":\"").Append(JsonEsc(nm)).Append("\",\"levels\":[");
                            for (int i = 0; i < list.Count; i++)
                            {
                                object cfg = list[i];
                                if (cfg == null) continue;
                                if (i > 0) sb.Append(',');
                                string ls = GetObjStr(cfg, fSz, "");
                                int lns = GetObjInt(cfg, fNs, 0);
                                string ln = !string.IsNullOrEmpty(ls) ? ls : (lns > 0 && lm != null ? lm.GetString(lns) : "");
                                sb.Append("{\"lv\":").Append(GetObjInt(cfg, fLv, 1));
                                sb.Append(",\"param\":\"").Append(JsonEsc(GetObjStr(cfg, fPar, ""))).Append('"');
                                sb.Append(",\"lname\":\"").Append(JsonEsc(ln)).Append("\"}");
                            }
                            sb.Append("]}");
                        }
                    }
                }
                sb.Append("],\"skills\":[]}");
                WriteAtomicText(dir + "/dyn_talents_skills.json", sb.ToString());
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModBridge] ExportDynTalentsAndSkillsConfigs ex: " + ex.Message);
            }
        }

        private unsafe void ExportDynTalentState(IGameSystem gs, int cid, bool force)
        {
            if (gs == null || cid == 0) return;
            try
            {
                byte* b = Shm();
                if (b == null) return;
                string dir = ReadShmDynDir(b);
                if (string.IsNullOrEmpty(dir)) return;

                List<int> targets = new List<int>();
                targets.Add(cid);
                if (gs.workerSystem != null)
                {
                    List<CWorker> ws = gs.workerSystem.GetAllWorker();
                    if (ws != null)
                    {
                        for (int i = 0; i < ws.Count; i++)
                        {
                            if (ws[i] != null && !targets.Contains(ws[i].configID)) targets.Add(ws[i].configID);
                        }
                    }
                }
                if (gs.hireSystem != null)
                {
                    List<CHireSystemData> hs = gs.hireSystem.GetAllHireData();
                    if (hs != null)
                    {
                        for (int i = 0; i < hs.Count; i++)
                        {
                            if (hs[i] != null && !targets.Contains(hs[i].nID)) targets.Add(hs[i].nID);
                        }
                    }
                }

                StringBuilder sb = new StringBuilder(2048);
                sb.Append("{\"cid\":").Append(cid).Append(",\"targets\":[");
                for (int i = 0; i < targets.Count; i++)
                {
                    int tc = targets[i];
                    string tn = (tc == cid) ? ("主角 (" + ResolveIdName(tc, "Nick") + ")") : ResolveIdName(tc, "");
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"cid\":").Append(tc).Append(",\"name\":\"").Append(JsonEsc(tn)).Append("\"}");
                }
                sb.Append("],\"owned\":[");
                bool first = true;
                if (gs.talentSystem != null)
                {
                    for (int i = 0; i < targets.Count; i++)
                    {
                        int tc = targets[i];
                        Dictionary<int, int> map = gs.talentSystem.GetAllTalentLevel(tc);
                        if (map == null) continue;
                        foreach (KeyValuePair<int, int> kv in map)
                        {
                            if (kv.Value <= 0) continue;
                            if (!first) sb.Append(',');
                            first = false;
                            sb.Append("{\"tc\":").Append(tc).Append(",\"id\":").Append(kv.Key).Append(",\"lv\":").Append(kv.Value).Append('}');
                        }
                    }
                }
                sb.Append("],\"skills\":[]}");
                string json = sb.ToString();
                if (!force && json == _lastTalentStateJson) return;
                _lastTalentStateJson = json;
                WriteAtomicText(dir + "/dyn_talent_state.json", json);
                BumpDynSeq(b);
            }
            catch { }
        }

        private static object InvokeNamedMethod(object target, string methodName, params object[] args)
        {
            if (target == null || string.IsNullOrEmpty(methodName)) return null;
            BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type t = target.GetType();
            MethodInfo[] ms = t.GetMethods(bf);
            int argc = (args != null) ? args.Length : 0;
            for (int i = 0; i < ms.Length; i++)
            {
                MethodInfo m = ms[i];
                if (m.Name != methodName) continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length != argc) continue;
                try { return m.Invoke(target, args); } catch { }
            }
            return null;
        }

        private int ResolveValidTalentLevel(int talentId, int wantLv)
        {
            if (wantLv <= 1) wantLv = 1;
            try
            {
                IGameDataCenter dc = CClientGlobal.Instance != null ? CClientGlobal.Instance.GetGameDataCenter : null;
                CTalentConfigLoader tl = (dc != null) ? (dc.GetConfigLoader(eGameConfigType.Talent) as CTalentConfigLoader) : null;
                if (tl == null) return wantLv;
                List<sTalentConfig> list = tl.GetConfigList(talentId);
                if (list == null || list.Count == 0) return wantLv;
                int best = list[0].nLevel;
                int bestDiff = int.MaxValue;
                for (int i = 0; i < list.Count; i++)
                {
                    sTalentConfig cfg = list[i];
                    if (cfg == null) continue;
                    if (cfg.nLevel == wantLv) return wantLv;
                    int diff = Math.Abs(cfg.nLevel - wantLv);
                    if (diff < bestDiff) { bestDiff = diff; best = cfg.nLevel; }
                }
                return best;
            }
            catch { return wantLv; }
        }

        private unsafe int CmdTalentOp(IGameSystem gs, int cid, int id, int subOp, int level, int targetCustomId)
        {
            int targetCid = (targetCustomId > 0) ? targetCustomId : cid;
            ClearAllCharacterTalentBlacklists();

            if (subOp == 0 || subOp == 1) // 0=Add/Set talent level, 1=Remove talent
            {
                if (gs.talentSystem == null || id <= 0)
                {
                    SetStatus(-2, "Invalid talent id");
                    return -2;
                }
                if (subOp == 1 || level <= 0)
                {
                    InvokeNamedMethod(gs.talentSystem, "DecreaseToLevel", targetCid, id, 0);
                    SetStatus(1, "Removed talent #" + id);
                }
                else
                {
                    int validLv = ResolveValidTalentLevel(id, level);
                    object curObj = InvokeNamedMethod(gs.talentSystem, "GetLevel", targetCid, id);
                    int curLv = (curObj != null) ? Convert.ToInt32(curObj) : 0;
                    if (curLv > validLv)
                    {
                        InvokeNamedMethod(gs.talentSystem, "DecreaseToLevel", targetCid, id, validLv);
                    }
                    else
                    {
                        InvokeNamedMethod(gs.talentSystem, "UpgradeToLevel", targetCid, id, validLv);
                    }
                    SetStatus(1, "Set talent #" + id + " Lv" + validLv);
                }
                ExportDynTalentState(gs, cid, true);
                return 1;
            }
            return -1;
        }

        // ==================== #9 Hire, Workers/Teamates, Force-Hire & Force-Tame ====================
        private void ExportDynHireConfigs(IGameDataCenter dc, CLanguageMgr lm, string dir)
        {
            try
            {
                BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                StringBuilder sb = new StringBuilder(32768);
                sb.Append("{\"hires\":[");
                object hl = dc.GetConfigLoader(eGameConfigType.Hire);
                if (hl != null)
                {
                    FieldInfo fi = hl.GetType().GetField("m_dicConfigs", bf);
                    System.Collections.IDictionary dic = (fi != null) ? (fi.GetValue(hl) as System.Collections.IDictionary) : null;
                    if (dic != null)
                    {
                        bool first = true;
                        string[] fId = new string[] { "nID", "nId" };
                        string[] fTpl = new string[] { "szPrefabTemplate" };
                        foreach (System.Collections.DictionaryEntry kv in dic)
                        {
                            object hc = kv.Value;
                            if (hc == null) continue;
                            int hid = GetObjInt(hc, fId, 0);
                            if (hid <= 0) continue;
                            string tpl = GetObjStr(hc, fTpl, "");
                            if (!first) sb.Append(',');
                            first = false;
                            string nm = ResolveIdName(hid, tpl);
                            sb.Append("{\"id\":").Append(hid);
                            sb.Append(",\"name\":\"").Append(JsonEsc(nm)).Append('"');
                            sb.Append(",\"tpl\":\"").Append(JsonEsc(tpl)).Append("\"}");
                        }
                    }
                }
                sb.Append("],\"workers\":[");
                object wl = dc.GetConfigLoader(eGameConfigType.Worker);
                if (wl != null)
                {
                    FieldInfo fi = wl.GetType().GetField("m_dicConfigs", bf);
                    System.Collections.IDictionary dic = (fi != null) ? (fi.GetValue(wl) as System.Collections.IDictionary) : null;
                    if (dic != null)
                    {
                        bool first = true;
                        string[] fId = new string[] { "nID", "nId" };
                        string[] fSz = new string[] { "szStringName" };
                        string[] fNs = new string[] { "nStringName" };
                        string[] fWt = new string[] { "eEnum" };
                        string[] fHp = new string[] { "nHP" };
                        string[] fPr = new string[] { "nPrice" };
                        string[] fSp = new string[] { "nProduceSpeed" };
                        foreach (System.Collections.DictionaryEntry kv in dic)
                        {
                            object wc = kv.Value;
                            if (wc == null) continue;
                            int wid = GetObjInt(wc, fId, 0);
                            if (wid <= 0) continue;
                            string sName = GetObjStr(wc, fSz, "");
                            int nStr = GetObjInt(wc, fNs, 0);
                            string nm = !string.IsNullOrEmpty(sName) ? sName : (nStr > 0 && lm != null ? lm.GetString(nStr) : ("Worker #" + wid));
                            if (!first) sb.Append(',');
                            first = false;
                            sb.Append("{\"id\":").Append(wid);
                            sb.Append(",\"name\":\"").Append(JsonEsc(nm)).Append('"');
                            sb.Append(",\"wtype\":").Append(GetObjInt(wc, fWt, 0));
                            sb.Append(",\"hp\":").Append(GetObjInt(wc, fHp, 0));
                            sb.Append(",\"price\":").Append(GetObjInt(wc, fPr, 0));
                            sb.Append(",\"spd\":").Append(GetObjInt(wc, fSp, 0)).Append('}');
                        }
                    }
                }
                sb.Append("],\"units\":[");
                object ul = dc.GetConfigLoader(eGameConfigType.Unit);
                if (ul != null)
                {
                    FieldInfo fi = ul.GetType().GetField("m_dicConfigs", bf);
                    System.Collections.IDictionary dic = (fi != null) ? (fi.GetValue(ul) as System.Collections.IDictionary) : null;
                    if (dic != null)
                    {
                        bool first = true;
                        string[] fId = new string[] { "nID", "nId" };
                        string[] fCls = new string[] { "eClass" };
                        string[] fSz = new string[] { "szStringName" };
                        string[] fNs = new string[] { "nStringName" };
                        foreach (System.Collections.DictionaryEntry kv in dic)
                        {
                            object uc = kv.Value;
                            if (uc == null) continue;
                            int uid = GetObjInt(uc, fId, 0);
                            int cls = GetObjInt(uc, fCls, 0);
                            if (uid <= 0 || cls == 0) continue;
                            string sName = GetObjStr(uc, fSz, "");
                            int nStr = GetObjInt(uc, fNs, 0);
                            string nm = !string.IsNullOrEmpty(sName) ? sName : (nStr > 0 && lm != null ? lm.GetString(nStr) : "");
                            if (string.IsNullOrEmpty(nm)) continue;
                            if (!first) sb.Append(',');
                            first = false;
                            sb.Append("{\"id\":").Append(uid);
                            sb.Append(",\"cls\":").Append(cls);
                            sb.Append(",\"name\":\"").Append(JsonEsc(nm)).Append("\"}");
                        }
                    }
                }
                sb.Append("]}");
                WriteAtomicText(dir + "/dyn_hires.json", sb.ToString());
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModBridge] ExportDynHireConfigs ex: " + ex.Message);
            }
        }

        private List<CEntity> CollectSceneEntities(bool includeInactive)
        {
            List<CEntity> list = new List<CEntity>(64);
            Dictionary<ulong, bool> seenUids = new Dictionary<ulong, bool>(64);
            Dictionary<int, bool> seenGoIds = new Dictionary<int, bool>(64);
            try
            {
                if (CGameSceneMgr.Instance != null)
                {
                    List<CEntity> activeList = CGameSceneMgr.Instance.GetAllEntity();
                    if (activeList != null)
                    {
                        for (int i = 0; i < activeList.Count; i++)
                        {
                            CEntity ce = activeList[i];
                            if (ce == null || ce.gameObject == null) continue;
                            if (seenUids.ContainsKey(ce.UID)) continue;
                            seenUids[ce.UID] = true;
                            seenGoIds[ce.gameObject.GetInstanceID()] = true;
                            list.Add(ce);
                        }
                    }
                }
            }
            catch { }
            try
            {
                SortedDictionary<ulong, CEntity> dic = GetSceneEntitiesDict();
                if (dic != null)
                {
                    foreach (KeyValuePair<ulong, CEntity> kv in dic)
                    {
                        CEntity ce = kv.Value;
                        if (ce == null || ce.gameObject == null) continue;
                        if (seenUids.ContainsKey(ce.UID)) continue;
                        seenUids[ce.UID] = true;
                        seenGoIds[ce.gameObject.GetInstanceID()] = true;
                        list.Add(ce);
                    }
                }
            }
            catch { }
            if (includeInactive)
            {
                try
                {
                    UnityEngine.SceneManagement.Scene sc = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                    if (sc.IsValid() && sc.isLoaded)
                    {
                        GameObject[] roots = sc.GetRootGameObjects();
                        if (roots != null)
                        {
                            for (int r = 0; r < roots.Length; r++)
                            {
                                GameObject root = roots[r];
                                if (root == null) continue;
                                CEntity[] ces = root.GetComponentsInChildren<CEntity>(true);
                                if (ces == null) continue;
                                for (int c = 0; c < ces.Length; c++)
                                {
                                    CEntity ce = ces[c];
                                    if (ce == null || ce.gameObject == null) continue;
                                    int gid = ce.gameObject.GetInstanceID();
                                    if (seenGoIds.ContainsKey(gid)) continue;
                                    seenGoIds[gid] = true;
                                    if (ce.UID != 0UL) seenUids[ce.UID] = true;
                                    list.Add(ce);
                                }
                            }
                        }
                    }
                }
                catch { }
            }
            if (_tamedEntities.Count > 0)
            {
                foreach (KeyValuePair<ulong, TamedBackup> kv in _tamedEntities)
                {
                    TamedBackup bk = kv.Value;
                    if (bk == null || bk.ent == null || bk.ent.gameObject == null) continue;
                    int gid = bk.ent.gameObject.GetInstanceID();
                    if (seenGoIds.ContainsKey(gid)) continue;
                    seenGoIds[gid] = true;
                    list.Add(bk.ent);
                }
            }
            return list;
        }

        private unsafe void ExportDynHireState(IGameSystem gs, int cid, bool force)
        {
            if (gs == null || cid == 0) return;
            try
            {
                byte* b = Shm();
                if (b == null) return;
                string dir = ReadShmDynDir(b);
                if (string.IsNullOrEmpty(dir)) return;

                StringBuilder sb = new StringBuilder(2048);
                sb.Append("{\"hires\":[");
                if (gs.hireSystem != null)
                {
                    List<CHireSystemData> hs = gs.hireSystem.GetAllHireData();
                    if (hs != null)
                    {
                        for (int i = 0; i < hs.Count; i++)
                        {
                            if (hs[i] == null) continue;
                            if (i > 0) sb.Append(',');
                            sb.Append(hs[i].nID);
                        }
                    }
                }
                sb.Append("],\"workers\":[");
                if (gs.workerSystem != null)
                {
                    List<CWorker> ws = gs.workerSystem.GetAllWorker();
                    if (ws != null)
                    {
                        for (int i = 0; i < ws.Count; i++)
                        {
                            if (ws[i] == null) continue;
                            if (i > 0) sb.Append(',');
                            sb.Append(ws[i].configID);
                        }
                    }
                }
                sb.Append("],\"teamates\":[");
                CCharacterSystemData cdata = (gs.characterSystem != null) ? gs.characterSystem.GetCharacterSystemData(cid) : null;
                if (cdata != null)
                {
                    List<int> tlist = cdata.listTeamateNormal;
                    if (tlist != null)
                    {
                        for (int i = 0; i < tlist.Count; i++)
                        {
                            if (i > 0) sb.Append(',');
                            sb.Append(tlist[i]);
                        }
                    }
                }
                sb.Append("],\"captures\":[");
                if (cdata != null)
                {
                    List<int> clist = cdata.listTeamateCapture;
                    if (clist != null)
                    {
                        for (int i = 0; i < clist.Count; i++)
                        {
                            if (i > 0) sb.Append(',');
                            sb.Append(clist[i]);
                        }
                    }
                }
                sb.Append("],\"forced\":[");
                for (int i = 0; i < _forcedHireIds.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(_forcedHireIds[i]);
                }
                sb.Append("],\"tameRange\":").Append((int)Mathf.Round(_tameScanRadiusM));
                sb.Append(",\"nearby\":[");
                CEntity player = (CGameSceneMgr.Instance != null) ? CGameSceneMgr.Instance.GetCurrentCharacterEntity() : null;
                if (player != null)
                {
                    List<CEntity> allEnts = CollectSceneEntities(true);
                    CFightObj pFo = player.fightObj as CFightObj;
                    int pCamp = (pFo != null) ? pFo.CampID : 0;
                    float maxSq = (_tameScanRadiusM >= 999f) ? float.MaxValue : (_tameScanRadiusM * _tameScanRadiusM);
                    Vector3 pPos = player.transform.position;
                    List<CEntity> candList = new List<CEntity>(48);
                    List<float> candDistSq = new List<float>(48);
                    int skSelf = 0, skNoFo = 0, skDead = 0, skZeroHp = 0, skPiao = 0, skPool = 0, skInact = 0, skFlw = 0, skHireAi = 0, skCamp = 0, skRange = 0;
                    bool doLog = force || (_lastHireStateJson == null);
                    StringBuilder diagSamples = doLog ? new StringBuilder(512) : null;
                    int diagSampleCnt = 0;

                    for (int i = 0; i < allEnts.Count; i++)
                    {
                        CEntity ne = allEnts[i];
                        if (ne == null || ne.gameObject == null) continue;
                        if (ne == player) { skSelf++; continue; }
                        CFightObj fo = ne.fightObj as CFightObj;
                        if (fo == null) { skNoFo++; continue; }
                        bool isTamed = _tamedEntities.ContainsKey(ne.UID);
                        Vector3 np = ne.transform.position;
                        float dx = np.x - pPos.x;
                        float dy = np.y - pPos.y;
                        float dSq = dx * dx + dy * dy;
                        string skipReason = null;
                        if (!isTamed)
                        {
                            if (ne.GetComponentInParent(typeof(CPrefabTemplatePool)) != null) { skPool++; skipReason = "pool"; }
                            else if (fo.IsDead()) { skDead++; skipReason = "dead"; }
                            else if (fo.GetMaxHP() <= 1 || fo.GetCurHP() <= 0) { skZeroHp++; skipReason = "0hp"; }
                            else if (!ne.gameObject.activeInHierarchy && ne.nCustomID == 0 &&
                                     ne.GetComponent(typeof(CActivator_SceneGrid)) == null &&
                                     ne.GetComponent(typeof(CActivator_SceneGridAndFightState)) == null) { skInact++; skipReason = "inact"; }
                            else if (fo.GetType().Name.IndexOf("PiaoLiuTong", StringComparison.OrdinalIgnoreCase) >= 0) { skPiao++; skipReason = "piao"; }
                            else if (ne.gameObject.GetComponent<ModForceAllyFollower>() != null) { skFlw++; skipReason = "flw"; }
                            else if (ne.gameObject.GetComponent(typeof(CAIFSM_Teamate)) != null) { skHireAi++; skipReason = "team"; }
                            else if (ne.gameObject.GetComponent(typeof(CAIFSM_HireTeamate)) != null) { skHireAi++; skipReason = "hire"; }
                            else if (ne.gameObject.GetComponent(typeof(CAIFSM_GuYong_Dog_Out)) != null) { skHireAi++; skipReason = "dogO"; }
                            else if (ne.gameObject.GetComponent(typeof(CAIFSM_GuYong_Dog_Home)) != null) { skHireAi++; skipReason = "dogH"; }
                            else if (fo.CampID == pCamp) { skCamp++; skipReason = "camp"; }
                            else if (dSq > maxSq) { skRange++; skipReason = "range"; }
                        }
                        if (skipReason != null)
                        {
                            if (diagSamples != null && diagSampleCnt < 10 && skipReason != "range" && skipReason != "pool")
                            {
                                diagSampleCnt++;
                                if (diagSampleCnt > 1) diagSamples.Append(" | ");
                                diagSamples.Append('#').Append(ne.nConfigID)
                                           .Append('(').Append(ne.nCustomID).Append(')')
                                           .Append(ResolveEntityName(ne))
                                           .Append(" c=").Append(fo.CampID)
                                           .Append(" hp=").Append(fo.GetCurHP()).Append('/').Append(fo.GetMaxHP())
                                           .Append(" a=").Append(ne.gameObject.activeInHierarchy ? 1 : 0)
                                           .Append(" d=").Append(Mathf.Sqrt(dSq).ToString("F1", System.Globalization.CultureInfo.InvariantCulture))
                                           .Append(':').Append(skipReason);
                            }
                            continue;
                        }
                        candList.Add(ne);
                        candDistSq.Add(dSq);
                    }

                    // Sort ascending by distance (with tamed kept first)
                    for (int i = 0; i < candList.Count - 1; i++)
                    {
                        int best = i;
                        bool bestTamed = _tamedEntities.ContainsKey(candList[best].UID);
                        for (int j = i + 1; j < candList.Count; j++)
                        {
                            bool jTamed = _tamedEntities.ContainsKey(candList[j].UID);
                            if (jTamed && !bestTamed) { best = j; bestTamed = true; continue; }
                            if (jTamed == bestTamed && candDistSq[j] < candDistSq[best]) { best = j; bestTamed = jTamed; }
                        }
                        if (best != i)
                        {
                            CEntity te = candList[i]; candList[i] = candList[best]; candList[best] = te;
                            float td = candDistSq[i]; candDistSq[i] = candDistSq[best]; candDistSq[best] = td;
                        }
                    }

                    int n = (candList.Count > 96) ? 96 : candList.Count;
                    for (int i = 0; i < n; i++)
                    {
                        CEntity ne = candList[i];
                        CFightObj fo = ne.fightObj as CFightObj;
                        bool isTamed = _tamedEntities.ContainsKey(ne.UID);
                        float distM = Mathf.Sqrt(candDistSq[i]);
                        if (i > 0) sb.Append(',');
                        sb.Append("{\"uid\":\"").Append(ne.UID).Append('"');
                        sb.Append(",\"id\":").Append(ne.nConfigID);
                        sb.Append(",\"name\":\"").Append(JsonEsc(ResolveEntityName(ne))).Append('"');
                        sb.Append(",\"camp\":").Append((fo != null) ? fo.CampID : 0);
                        sb.Append(",\"hp\":").Append((fo != null) ? fo.GetCurHP() : 0);
                        sb.Append(",\"maxHp\":").Append((fo != null) ? fo.GetMaxHP() : 0);
                        sb.Append(",\"dist\":").Append(distM.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                        sb.Append(",\"tamed\":").Append(isTamed ? 1 : 0).Append('}');
                    }
                    if (doLog)
                    {
                        int curSid = (CGameSceneMgr.Instance != null) ? CGameSceneMgr.Instance.nSceneID : -1;
                        Debug.Log("[ModBridge] NearbyScan s=" + curSid + " pCamp=" + pCamp + " ents=" + allEnts.Count +
                                  " match=" + candList.Count + " rng=" + (int)Mathf.Round(_tameScanRadiusM) +
                                  " sk(noFo=" + skNoFo + ",pool=" + skPool + ",inact=" + skInact + ",dead=" + skDead +
                                  ",0hp=" + skZeroHp + ",camp=" + skCamp + ",rng=" + skRange + ",hire=" + skHireAi + ",piao=" + skPiao + ")");
                        if (diagSamples != null && diagSamples.Length > 0)
                            Debug.Log("[ModBridge] NearbySamples: " + diagSamples.ToString());
                    }
                }
                sb.Append("]}");
                string json = sb.ToString();
                if (!force && json == _lastHireStateJson) return;
                _lastHireStateJson = json;
                WriteAtomicText(dir + "/dyn_hire_state.json", json);
                BumpDynSeq(b);
            }
            catch { }
        }

        private static void TriggerSceneSpawnersUpdate()
        {
            try
            {
                BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                CHireSpwaner[] hss = UnityEngine.Object.FindObjectsOfType(typeof(CHireSpwaner)) as CHireSpwaner[];
                if (hss != null)
                {
                    MethodInfo mi = typeof(CHireSpwaner).GetMethod("UpdateView", bf);
                    if (mi != null)
                    {
                        for (int i = 0; i < hss.Length; i++)
                            if (hss[i] != null) mi.Invoke(hss[i], null);
                    }
                }
                CSoliderSpawner[] sss = UnityEngine.Object.FindObjectsOfType(typeof(CSoliderSpawner)) as CSoliderSpawner[];
                if (sss != null)
                {
                    MethodInfo mi = typeof(CSoliderSpawner).GetMethod("UpdateView", bf);
                    if (mi != null)
                    {
                        for (int i = 0; i < sss.Length; i++)
                            if (sss[i] != null) mi.Invoke(sss[i], null);
                    }
                }
            }
            catch { }
        }

        private void EnsureWorkerArmed(IGameSystem gs, int workerCustomId)
        {
            if (gs == null || gs.equipmentSystem == null) return;
            try
            {
                CEquipment eq = gs.equipmentSystem.GetEquipmentByType(sEquipmentConfig.eEquipPart.Shortcut, workerCustomId);
                if (eq != null) return;

                IGameDataCenter dc = CClientGlobal.Instance != null ? CClientGlobal.Instance.GetGameDataCenter : null;
                if (dc == null) return;
                CMaterialConfigLoader ml = dc.GetConfigLoader(eGameConfigType.Materiel) as CMaterialConfigLoader;
                if (ml == null) return;
                int chosenWeaponId = 0;
                List<sMaterialConfig> mats = ml.GetAllConfig();
                if (mats != null)
                {
                    for (int i = 0; i < mats.Count; i++)
                    {
                        sEquipmentConfig ec = mats[i] as sEquipmentConfig;
                        if (ec == null || ec.ePart != sEquipmentConfig.eEquipPart.Shortcut) continue;
                        string tpl = GetObjStr(ec, new string[] { "szTemplateName" }, "");
                        if (string.IsNullOrEmpty(tpl)) continue;
                        if (ec.nAmmo == 0)
                        {
                            chosenWeaponId = ec.nID;
                            break;
                        }
                        if (chosenWeaponId == 0) chosenWeaponId = ec.nID;
                    }
                }
                if (chosenWeaponId > 0 && gs.materialSystem != null && gs.containerSystem != null)
                {
                    CMaterial mat = gs.materialSystem.CreateMaterial(chosenWeaponId, 1);
                    if (mat != null)
                    {
                        gs.containerSystem.AddMaterialToContainer(workerCustomId, eContainerTypeEnum.Equipment, mat.UID);
                    }
                }
            }
            catch { }
        }

        private CTerritory GetOrCreateAllyTerritory(CEntity player)
        {
            if (player == null || player.transform == null) return null;
            try
            {
                if (_allyTerritoryObj == null || _allyTerritory == null)
                {
                    _allyTerritoryObj = new GameObject("__ModAllyTerritory");
                    _allyTerritory = _allyTerritoryObj.AddComponent<CTerritory>();
                    _allyTerritory.fRadius = 26f;
                    _allyTerritory.bDebug = false;
                }
                _allyTerritoryObj.transform.position = player.transform.position;
                return _allyTerritory;
            }
            catch { return null; }
        }

        private static string GetYooPackageVersion(object pkg)
        {
            try
            {
                if (pkg == null) return "";
                MethodInfo mi = pkg.GetType().GetMethod("GetPackageVersion", BindingFlags.Public | BindingFlags.Instance);
                return (mi != null) ? (mi.Invoke(pkg, null) as string ?? "") : "";
            }
            catch { return ""; }
        }

        private void ReleaseYooHandle(object handle)
        {
            if (handle == null) return;
            try
            {
                if (s_miYooHandleRelease == null)
                    s_miYooHandleRelease = handle.GetType().GetMethod("Release", BindingFlags.Public | BindingFlags.Instance);
                if (s_miYooHandleRelease != null)
                    s_miYooHandleRelease.Invoke(handle, null);
            }
            catch { }
        }

        private GameObject LoadPrefabByYooAsset(string assetPath, out object outHandle)
        {
            outHandle = null;
            if (string.IsNullOrEmpty(assetPath) || _yooDefaultPkg == null) return null;
            try
            {
                BindingFlags bf = BindingFlags.Public | BindingFlags.Instance;
                Type pkgT = _yooDefaultPkg.GetType();
                object pkgAsset = null;
                if (_yooPathToPkgAsset.TryGetValue(assetPath, out pkgAsset) && pkgAsset != null)
                {
                    if (s_ciYooAssetInfo == null)
                    {
                        Type aiT = pkgT.Assembly.GetType("YooAsset.AssetInfo", false);
                        if (aiT != null)
                            s_ciYooAssetInfo = aiT.GetConstructor(new Type[] { typeof(string), pkgAsset.GetType(), typeof(Type) });
                    }
                    if (s_miYooLoadAssetInfo == null && s_ciYooAssetInfo != null)
                    {
                        s_miYooLoadAssetInfo = pkgT.GetMethod("LoadAssetSync", bf, null, new Type[] { s_ciYooAssetInfo.DeclaringType }, null);
                    }
                    if (s_ciYooAssetInfo != null && s_miYooLoadAssetInfo != null)
                    {
                        object ai = s_ciYooAssetInfo.Invoke(new object[] { "DefaultPackage", pkgAsset, typeof(GameObject) });
                        outHandle = s_miYooLoadAssetInfo.Invoke(_yooDefaultPkg, new object[] { ai });
                    }
                }
                if (outHandle == null)
                {
                    if (s_miYooLoadAssetStr == null)
                        s_miYooLoadAssetStr = pkgT.GetMethod("LoadAssetSync", bf, null, new Type[] { typeof(string), typeof(Type) }, null);
                    if (s_miYooLoadAssetStr != null)
                        outHandle = s_miYooLoadAssetStr.Invoke(_yooDefaultPkg, new object[] { assetPath, typeof(GameObject) });
                }
                if (outHandle != null)
                {
                    if (s_piYooHandleAsset == null)
                        s_piYooHandleAsset = outHandle.GetType().GetProperty("AssetObject", bf);
                    if (s_piYooHandleAsset != null)
                    {
                        GameObject go = s_piYooHandleAsset.GetValue(outHandle, null) as GameObject;
                        if (go != null) return go;
                    }
                    ReleaseYooHandle(outHandle);
                    outHandle = null;
                }
            }
            catch
            {
                if (outHandle != null) { ReleaseYooHandle(outHandle); outHandle = null; }
            }
            return null;
        }

        private void RecordUnitPrefabAliases(int cfgId, string ap)
        {
            if (cfgId <= 0 || string.IsNullOrEmpty(ap)) return;
            try
            {
                IGameDataCenter dc = CClientGlobal.Instance != null ? CClientGlobal.Instance.GetGameDataCenter : null;
                CUnitConfigLoader ul = (dc != null) ? (dc.GetConfigLoader(eGameConfigType.Unit) as CUnitConfigLoader) : null;
                sUnitConfig uc = (ul != null) ? ul.GetConfig(cfgId) : null;
                if (uc == null) return;
                string stem = ExtractStemHint(uc.szIcon);
                if (stem.Length >= 3 && !_unitIconStemToPath.ContainsKey(stem))
                    _unitIconStemToPath[stem] = ap;
                string nm = StripRichTags(uc.szStringName);
                if (!string.IsNullOrEmpty(nm) && !_unitNameToPath.ContainsKey(nm))
                    _unitNameToPath[nm] = ap;
            }
            catch { }
        }

        private void InitYooManifest(string dynDir)
        {
            if (_yooManifestInit) return;
            _yooManifestInit = true;
            try
            {
                Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                Type yooAssetsT = null;
                for (int i = 0; i < asms.Length; i++)
                {
                    yooAssetsT = asms[i].GetType("YooAsset.YooAssets", false);
                    if (yooAssetsT != null) break;
                }
                if (yooAssetsT == null) { Debug.LogWarning("[ModBridge] InitYooManifest: YooAssets type not found"); return; }
                MethodInfo miGetPkg = yooAssetsT.GetMethod("GetPackage", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(string) }, null);
                if (miGetPkg == null) return;
                _yooDefaultPkg = miGetPkg.Invoke(null, new object[] { "DefaultPackage" });
                if (_yooDefaultPkg == null) { Debug.LogWarning("[ModBridge] InitYooManifest: DefaultPackage null"); return; }

                BindingFlags bfAny = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                FieldInfo fiPlayImpl = _yooDefaultPkg.GetType().GetField("_playModeImpl", bfAny);
                object playImpl = (fiPlayImpl != null) ? fiPlayImpl.GetValue(_yooDefaultPkg) : null;
                if (playImpl == null) { Debug.LogWarning("[ModBridge] InitYooManifest: _playModeImpl null"); return; }

                PropertyInfo piManifest = playImpl.GetType().GetProperty("ActiveManifest", bfAny);
                object manifest = (piManifest != null) ? piManifest.GetValue(playImpl, null) : null;
                if (manifest == null)
                {
                    FieldInfo fiManifest = playImpl.GetType().GetField("<ActiveManifest>k__BackingField", bfAny)
                                          ?? playImpl.GetType().GetField("_activeManifest", bfAny);
                    if (fiManifest != null) manifest = fiManifest.GetValue(playImpl);
                }
                if (manifest == null) { Debug.LogWarning("[ModBridge] InitYooManifest: ActiveManifest null"); return; }

                FieldInfo fiAssetList = manifest.GetType().GetField("AssetList", bfAny);
                System.Collections.IList assetList = (fiAssetList != null) ? (fiAssetList.GetValue(manifest) as System.Collections.IList) : null;
                if (assetList == null || assetList.Count == 0) return;

                List<string> prioPaths = new List<string>(1280);
                List<string> secPaths = new List<string>(860);
                List<string> lowPaths = new List<string>(128);
                StringBuilder hireStemDiag = !string.IsNullOrEmpty(dynDir) ? new StringBuilder(1024) : null;
                FieldInfo fiAssetPath = null;
                for (int i = 0; i < assetList.Count; i++)
                {
                    object pa = assetList[i];
                    if (pa == null) continue;
                    if (fiAssetPath == null) fiAssetPath = pa.GetType().GetField("AssetPath", bfAny);
                    string ap = (fiAssetPath != null) ? (fiAssetPath.GetValue(pa) as string) : null;
                    if (string.IsNullOrEmpty(ap)) continue;
                    string lp = ap.ToLowerInvariant();
                    if (!lp.EndsWith(".prefab", StringComparison.Ordinal)) continue;
                    if (lp.Contains("diaoluo") || lp.Contains("/effect") || lp.Contains("/texiao") ||
                        lp.Contains("/bullet") || lp.Contains("/zidan") || lp.Contains("/dead/") ||
                        lp.Contains("/ui/") || lp.Contains("/ui_") ||
                        lp.EndsWith("_fx.prefab", StringComparison.Ordinal) || lp.EndsWith("dead.prefab", StringComparison.Ordinal))
                        continue;

                    bool isPrio = lp.StartsWith("assets/single_bundles/shengwu_prefab/guaiwu/", StringComparison.Ordinal) ||
                                  lp.StartsWith("assets/single_bundles/shengwu_prefab/npc/", StringComparison.Ordinal) ||
                                  lp.StartsWith("assets/single_bundles/shengwu_prefab/zuoqi/", StringComparison.Ordinal) ||
                                  lp.StartsWith("assets/single_bundles/shengwu_prefab/dlc/", StringComparison.Ordinal) ||
                                  lp.StartsWith("assets/single_bundles/shengwu_prefab/base/", StringComparison.Ordinal);
                    bool isSec = !isPrio && (lp.StartsWith("assets/single_bundles/shengwu/guaiwu/", StringComparison.Ordinal) ||
                                             lp.StartsWith("assets/single_bundles/shengwu/guaiwu_big/", StringComparison.Ordinal) ||
                                             lp.StartsWith("assets/single_bundles/shengwu/npc/", StringComparison.Ordinal));
                    bool isExtraBio = !isPrio && !isSec && (lp.StartsWith("assets/single_bundles/shengwu_prefab/", StringComparison.Ordinal) ||
                                                            lp.StartsWith("assets/single_bundles/shengwu/", StringComparison.Ordinal) ||
                                                            lp.Contains("/guyong") || lp.Contains("/hire") || lp.Contains("/pets"));

                    _yooPathToPkgAsset[ap] = pa;
                    string stem = Path.GetFileNameWithoutExtension(lp);
                    if (!string.IsNullOrEmpty(stem))
                    {
                        int score = isPrio ? 100 : (isExtraBio ? 80 : (isSec ? 50 : 20));
                        if (lp.Contains("/lan_") || lp.Contains("multiplayer_battle")) score -= 30;
                        if (stem.EndsWith("_home", StringComparison.Ordinal)) score -= 10;
                        int prevScore;
                        if (!_allPrefabStemToPath.ContainsKey(stem) || (_allPrefabStemScore.TryGetValue(stem, out prevScore) && score > prevScore))
                        {
                            _allPrefabStemToPath[stem] = ap;
                            _allPrefabStemScore[stem] = score;
                        }
                        if (hireStemDiag != null && (lp.Contains("guyong") || lp.Contains("hire") || lp.Contains("nianshou") ||
                            lp.Contains("wurenji") || lp.Contains("weurenji") || lp.Contains("jiangna") || lp.Contains("laowu") ||
                            lp.Contains("niao") || lp.Contains("sot_") || lp.Contains("hashiqi") || lp.Contains("_dog")))
                        {
                            hireStemDiag.Append(stem).Append('=').Append(ap).Append('\n');
                        }
                    }
                    if (!isPrio && !isSec) continue;

                    bool isMultiOrLan = lp.Contains("/lan_") || lp.Contains("multiplayer_battle");
                    if (isMultiOrLan) lowPaths.Add(ap);
                    else if (isPrio) prioPaths.Add(ap);
                    else secPaths.Add(ap);
                }
                if (hireStemDiag != null && hireStemDiag.Length > 0)
                {
                    try { WriteAtomicText(dynDir + "/mod_hire_stems.txt", hireStemDiag.ToString()); } catch { }
                }
                _yooUnitPaths.AddRange(prioPaths);
                _yooUnitPaths.AddRange(secPaths);
                _yooUnitPaths.AddRange(lowPaths);

                // Also load persistent unitId -> assetPath|campId cache if same package version
                if (!string.IsNullOrEmpty(dynDir) && !_unitPrefabCacheLoaded)
                {
                    _unitPrefabCacheLoaded = true;
                    string cpath = dynDir + "/mod_unit_prefabs.txt";
                    string curVer = GetYooPackageVersion(_yooDefaultPkg);
                    if (File.Exists(cpath))
                    {
                        string[] lines = File.ReadAllLines(cpath, s_utf8NoBom);
                        bool verOk = string.IsNullOrEmpty(curVer);
                        bool done = false;
                        bool campDone = false;
                        for (int i = 0; i < lines.Length; i++)
                        {
                            string ln = lines[i];
                            if (string.IsNullOrEmpty(ln)) continue;
                            if (ln.StartsWith("#v=", StringComparison.Ordinal))
                            {
                                string fv = ln.Substring(3).Trim();
                                if (!string.IsNullOrEmpty(curVer) && fv == curVer) verOk = true;
                                continue;
                            }
                            if (ln == "#done=1") { done = true; continue; }
                            if (ln == "#campDone=1") { campDone = true; continue; }
                            if (!verOk) break;
                            int eq = ln.IndexOf('=');
                            if (eq > 0)
                            {
                                int uid;
                                if (int.TryParse(ln.Substring(0, eq), out uid) && uid > 0)
                                {
                                    string rhs = ln.Substring(eq + 1).Trim();
                                    string ap = rhs;
                                    int bar = rhs.IndexOf('|');
                                    int cmpVal = -9999;
                                    bool hasCamp = false;
                                    if (bar > 0)
                                    {
                                        ap = rhs.Substring(0, bar).Trim();
                                        hasCamp = int.TryParse(rhs.Substring(bar + 1).Trim(), out cmpVal);
                                    }
                                    if (!string.IsNullOrEmpty(ap) && !_unitPrefabPathCache.ContainsKey(uid))
                                    {
                                        _unitPrefabPathCache[uid] = ap;
                                        RecordUnitPrefabAliases(uid, ap);
                                    }
                                    if (hasCamp && !_unitCampCache.ContainsKey(uid))
                                        _unitCampCache[uid] = cmpVal;
                                    else if (!hasCamp)
                                        _unitCampPendingIds.Add(uid);
                                }
                            }
                        }
                        if (verOk && done && _unitPrefabPathCache.Count > 50)
                        {
                            _yooScanDone = true;
                            _yooScanCursor = _yooUnitPaths.Count;
                            if (campDone && _unitCampPendingIds.Count == 0)
                                _unitCampScanDone = true;
                        }
                    }
                }
                Debug.Log("[ModBridge] YooManifest creature candidates=" + _yooUnitPaths.Count + " cached=" + _unitPrefabPathCache.Count + " done=" + _yooScanDone + " camps=" + _unitCampCache.Count);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModBridge] InitYooManifest ex: " + ex.Message);
            }
        }

        private void SaveUnitPrefabCacheFile(string dynDir)
        {
            if (string.IsNullOrEmpty(dynDir)) return;
            try
            {
                StringBuilder sb = new StringBuilder(32768);
                string ver = GetYooPackageVersion(_yooDefaultPkg);
                if (!string.IsNullOrEmpty(ver)) sb.Append("#v=").Append(ver).Append('\n');
                if (_yooScanDone) sb.Append("#done=1\n");
                if (_unitCampScanDone) sb.Append("#campDone=1\n");
                foreach (KeyValuePair<int, string> kv in _unitPrefabPathCache)
                {
                    sb.Append(kv.Key).Append('=').Append(kv.Value);
                    int cmpVal;
                    if (_unitCampCache.TryGetValue(kv.Key, out cmpVal))
                        sb.Append('|').Append(cmpVal);
                    sb.Append('\n');
                }
                WriteAtomicText(dynDir + "/mod_unit_prefabs.txt", sb.ToString());
                _unitPrefabDirtyAdds = 0;
            }
            catch { }
        }

        private void RecordUnitCampFromPrefab(int unitId, GameObject go)
        {
            if (unitId <= 0 || go == null) return;
            if (_unitCampCache.ContainsKey(unitId)) return;
            try
            {
                CFightObj fo = go.GetComponent<CFightObj>();
                _unitCampCache[unitId] = (fo != null) ? fo.campID : -9999;
                _unitPrefabDirtyAdds++;
            }
            catch { }
        }

        private GameObject ProbeYooPathAt(int idx, int wantUnitId, out object keptHandle)
        {
            keptHandle = null;
            if (idx < 0 || idx >= _yooUnitPaths.Count) return null;
            string ap = _yooUnitPaths[idx];
            object h;
            GameObject go = LoadPrefabByYooAsset(ap, out h);
            if (go == null) return null;
            CEntity ent = go.GetComponent<CEntity>();
            if (ent != null && ent.nConfigID > 0)
            {
                if (!_unitPrefabPathCache.ContainsKey(ent.nConfigID))
                {
                    _unitPrefabPathCache[ent.nConfigID] = ap;
                    RecordUnitPrefabAliases(ent.nConfigID, ap);
                    _unitPrefabDirtyAdds++;
                }
                RecordUnitCampFromPrefab(ent.nConfigID, go);
                if (wantUnitId > 0 && ent.nConfigID == wantUnitId)
                {
                    keptHandle = h;
                    return go;
                }
            }
            ReleaseYooHandle(h);
            return null;
        }

        private unsafe void StepUnitPrefabIndex(byte* b, int budget)
        {
            string dir = ReadShmDynDir(b);
            if (string.IsNullOrEmpty(dir)) return;
            InitYooManifest(dir);
            if (_yooScanDone && _unitCampScanDone)
            {
                if (!_dynUnitsExportedWithCamps && _dynExportDone && CClientGlobal.Instance != null && CClientGlobal.Instance.GetGameDataCenter != null)
                {
                    _dynUnitsExportedWithCamps = true;
                    ExportDynUnits(CClientGlobal.Instance.GetGameDataCenter, CLanguageMgr.Instance, dir);
                    BumpDynSeq(b);
                }
                return;
            }
            if ((_tick % 2u) != 0u) return;

            if (!_yooScanDone)
            {
                if (_yooUnitPaths.Count == 0)
                {
                    _yooScanDone = true;
                }
                else
                {
                    float t0 = Time.realtimeSinceStartup;
                    int count = 0;
                    while (_yooScanCursor < _yooUnitPaths.Count && count < budget)
                    {
                        object dummy;
                        ProbeYooPathAt(_yooScanCursor, 0, out dummy);
                        _yooScanCursor++;
                        count++;
                        if ((Time.realtimeSinceStartup - t0) > 0.007f) break;
                    }
                    if (_yooScanCursor >= _yooUnitPaths.Count)
                    {
                        _yooScanDone = true;
                        _unitCampScanDone = (_unitCampPendingIds.Count == 0);
                        SaveUnitPrefabCacheFile(dir);
                        _randomHostilePool = null;
                        if (CClientGlobal.Instance != null && CClientGlobal.Instance.GetGameDataCenter != null)
                        {
                            ExportDynUnits(CClientGlobal.Instance.GetGameDataCenter, CLanguageMgr.Instance, dir);
                            BumpDynSeq(b);
                        }
                        Debug.Log("[ModBridge] YooManifest creature scan finished: indexed=" + _unitPrefabPathCache.Count + " camps=" + _unitCampCache.Count);
                    }
                    else if (_unitPrefabDirtyAdds >= 80)
                    {
                        SaveUnitPrefabCacheFile(dir);
                    }
                    return;
                }
            }

            if (_yooScanDone && !_unitCampScanDone)
            {
                float t0 = Time.realtimeSinceStartup;
                int count = 0;
                while (_unitCampPendingCursor < _unitCampPendingIds.Count && count < budget)
                {
                    int uid = _unitCampPendingIds[_unitCampPendingCursor];
                    _unitCampPendingCursor++;
                    count++;
                    string ap;
                    if (!_unitCampCache.ContainsKey(uid) && _unitPrefabPathCache.TryGetValue(uid, out ap) && !string.IsNullOrEmpty(ap))
                    {
                        object h;
                        GameObject go = LoadPrefabByYooAsset(ap, out h);
                        if (go != null) RecordUnitCampFromPrefab(uid, go);
                        else _unitCampCache[uid] = -9999;
                        if (h != null) ReleaseYooHandle(h);
                    }
                    if ((Time.realtimeSinceStartup - t0) > 0.007f) break;
                }
                if (_unitCampPendingCursor >= _unitCampPendingIds.Count)
                {
                    _unitCampScanDone = true;
                    _unitCampPendingIds.Clear();
                    SaveUnitPrefabCacheFile(dir);
                    _randomHostilePool = null;
                    if (CClientGlobal.Instance != null && CClientGlobal.Instance.GetGameDataCenter != null)
                    {
                        _dynUnitsExportedWithCamps = true;
                        ExportDynUnits(CClientGlobal.Instance.GetGameDataCenter, CLanguageMgr.Instance, dir);
                        BumpDynSeq(b);
                    }
                    Debug.Log("[ModBridge] Unit camp scan finished: camps=" + _unitCampCache.Count);
                }
                else if (_unitPrefabDirtyAdds >= 80)
                {
                    SaveUnitPrefabCacheFile(dir);
                }
            }
        }

        private static string ExtractStemHint(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string r = Path.GetFileNameWithoutExtension(s).ToLowerInvariant();
            r = r.Replace("_icon", "").Replace("icon_", "").Replace("head_", "").Replace("touxiang_", "");
            return r;
        }

        private GameObject ResolveUnitNativePrefab(IGameSystem gs, int unitId, out bool isAvatarModel, out object outHandle)
        {
            isAvatarModel = false;
            outHandle = null;
            string dynDir = "";
            unsafe { dynDir = ReadShmDynDir(Shm()); }
            InitYooManifest(dynDir);

            // 1. Cached YooAsset exact unitId -> assetPath
            string cachedPath;
            if (_unitPrefabPathCache.TryGetValue(unitId, out cachedPath) && !string.IsNullOrEmpty(cachedPath))
            {
                GameObject go = LoadPrefabByYooAsset(cachedPath, out outHandle);
                if (go != null) return go;
            }

            // 2. Pool templates with exact serialized CEntity.nConfigID == unitId
            try
            {
                CPrefabTemplatePoolMgr pool = CPrefabTemplatePoolMgr.Instance;
                if (pool != null)
                {
                    FieldInfo fi = typeof(CPrefabTemplatePoolMgr).GetField("m_dicPrefabTemplate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    var dic = (fi != null) ? (fi.GetValue(pool) as SortedDictionary<string, sPrefabTemplateInfo>) : null;
                    if (dic != null)
                    {
                        foreach (KeyValuePair<string, sPrefabTemplateInfo> kv in dic)
                        {
                            GameObject tpl = kv.Value.pTemplateGO;
                            if (tpl == null) continue;
                            CEntity ce = tpl.GetComponent<CEntity>();
                            if (ce != null && ce.nConfigID == unitId) return tpl;
                        }
                    }
                }
            }
            catch { }

            // 3. Animal config loader if matching unitId
            IGameDataCenter dc = CClientGlobal.Instance != null ? CClientGlobal.Instance.GetGameDataCenter : null;
            if (dc != null && CAssetMgr.Instance != null)
            {
                try
                {
                    CAnimalConfigLoader al = dc.GetConfigLoader(eGameConfigType.Animal) as CAnimalConfigLoader;
                    sAnimalConfig ac = (al != null) ? al.GetConfig(unitId) : null;
                    if (ac != null && !string.IsNullOrEmpty(ac.szPrefab))
                    {
                        CAsset asset = CAssetMgr.Instance.Load<GameObject>(ac.szPrefab);
                        GameObject go = (asset != null) ? (asset.asset as GameObject) : null;
                        if (go != null) return go;
                    }
                }
                catch { }
            }

            // 4. Probe icon hint matches in YooAsset candidates, then synchronous scan slice
            string hint = "";
            string cleanName = "";
            try
            {
                CUnitConfigLoader ul = (dc != null) ? (dc.GetConfigLoader(eGameConfigType.Unit) as CUnitConfigLoader) : null;
                sUnitConfig uc = (ul != null) ? ul.GetConfig(unitId) : null;
                if (uc != null)
                {
                    hint = ExtractStemHint(uc.szIcon);
                    cleanName = StripRichTags(uc.szStringName);
                }
            }
            catch { }

            if (_yooUnitPaths.Count > 0 && !_yooScanDone)
            {
                if (hint.Length >= 3)
                {
                    int hintProbes = 0;
                    for (int i = 0; i < _yooUnitPaths.Count && hintProbes < 24; i++)
                    {
                        if (_yooUnitPaths[i].ToLowerInvariant().Contains(hint))
                        {
                            hintProbes++;
                            GameObject hit = ProbeYooPathAt(i, unitId, out outHandle);
                            if (hit != null)
                            {
                                SaveUnitPrefabCacheFile(dynDir);
                                return hit;
                            }
                        }
                    }
                }

                float t0 = Time.realtimeSinceStartup;
                int scanned = 0;
                while (_yooScanCursor < _yooUnitPaths.Count && scanned < 350)
                {
                    GameObject hit = ProbeYooPathAt(_yooScanCursor, unitId, out outHandle);
                    _yooScanCursor++;
                    scanned++;
                    if (hit != null)
                    {
                        SaveUnitPrefabCacheFile(dynDir);
                        return hit;
                    }
                    if ((Time.realtimeSinceStartup - t0) > 0.22f) break;
                }
                if (_yooScanCursor >= _yooUnitPaths.Count)
                {
                    _yooScanDone = true;
                    SaveUnitPrefabCacheFile(dynDir);
                }
            }

            // 4b. Re-check shared icon stem or shared unit name (e.g. Locust variants 4101/4110 sharing base 4100 prefab)
            string aliasPath = null;
            if (!string.IsNullOrEmpty(hint) && hint.Length >= 3)
                _unitIconStemToPath.TryGetValue(hint, out aliasPath);
            if (string.IsNullOrEmpty(aliasPath) && !string.IsNullOrEmpty(cleanName))
                _unitNameToPath.TryGetValue(cleanName, out aliasPath);
            if (!string.IsNullOrEmpty(aliasPath))
            {
                GameObject aliasGo = LoadPrefabByYooAsset(aliasPath, out outHandle);
                if (aliasGo != null)
                {
                    _unitPrefabPathCache[unitId] = aliasPath;
                    return aliasGo;
                }
            }

            // 5. Fallback to Hire Avatarable template for pure avatar-driven workers/NPCs
            isAvatarModel = true;
            return ResolveForcedAllyTemplatePrefab(gs);
        }

        private GameObject ResolveForcedAllyTemplatePrefab(IGameSystem gs)
        {
            try
            {
                CPrefabTemplatePoolMgr pool = CPrefabTemplatePoolMgr.Instance;
                if (pool != null)
                {
                    FieldInfo fi = typeof(CPrefabTemplatePoolMgr).GetField("m_dicPrefabTemplate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    var dic = (fi != null) ? (fi.GetValue(pool) as SortedDictionary<string, sPrefabTemplateInfo>) : null;
                    if (dic != null)
                    {
                        sPrefabTemplateInfo fallback = default(sPrefabTemplateInfo);
                        foreach (KeyValuePair<string, sPrefabTemplateInfo> kv in dic)
                        {
                            if (kv.Value.pTemplateGO == null) continue;
                            string lk = kv.Key != null ? kv.Key.ToLowerInvariant() : "";
                            if (lk.Contains("hire") || lk.Contains("buqiang") || lk.Contains("teamate"))
                            {
                                if (kv.Value.pTemplateGO.GetComponent(typeof(CAIFSM_Avatarable)) != null)
                                    return kv.Value.pTemplateGO;
                            }
                            if (fallback.pTemplateGO == null && kv.Value.pTemplateGO.GetComponent(typeof(CAIFSM_Avatarable)) != null)
                                fallback = kv.Value;
                        }
                        if (fallback.pTemplateGO != null) return fallback.pTemplateGO;
                    }
                }

                IGameDataCenter dc = CClientGlobal.Instance != null ? CClientGlobal.Instance.GetGameDataCenter : null;
                if (dc != null && CAssetMgr.Instance != null)
                {
                    CHireConfigLoader hl = dc.GetConfigLoader(eGameConfigType.Hire) as CHireConfigLoader;
                    if (hl != null)
                    {
                        FieldInfo fi = typeof(CHireConfigLoader).GetField("m_dicConfigs", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        var dic = (fi != null) ? (fi.GetValue(hl) as Dictionary<int, sHireConfig>) : null;
                        if (dic != null)
                        {
                            foreach (KeyValuePair<int, sHireConfig> kv in dic)
                            {
                                sHireConfig hc = kv.Value;
                                if (hc == null || string.IsNullOrEmpty(hc.szPrefabTemplate)) continue;
                                CAsset asset = CAssetMgr.Instance.Load<GameObject>(hc.szPrefabTemplate);
                                GameObject go = (asset != null) ? (asset.asset as GameObject) : null;
                                if (go != null) return go;
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static void ConfigureFightObjAsPlayerAlly(CFightObj fo, CEntity ent, CEntity player, CTerritory allyTerr)
        {
            if (fo == null) return;
            CFightObj pFo = (player != null) ? (player.fightObj as CFightObj) : null;
            int pCamp = (pFo != null) ? pFo.CampID : 0;
            fo.campID = pCamp;
            if (allyTerr != null) fo.territory = allyTerr;
            else if (pFo != null && pFo.territory != null) fo.territory = pFo.territory;

            if (fo.target_searchR < 16f) fo.target_searchR = 16f;
            if (fo.target_searchR_Fight < 18f) fo.target_searchR_Fight = 18f;

            if (fo.dicCamp != null)
            {
                fo.dicCamp.Clear();
                if (pFo != null && pFo.dicCamp != null)
                {
                    foreach (KeyValuePair<int, CFightObj.sCampNode> kv in pFo.dicCamp)
                        fo.dicCamp[kv.Key] = kv.Value;
                }
                CFightObj.sCampNode friendSelf = default(CFightObj.sCampNode);
                friendSelf.nCampID = pCamp;
                friendSelf.eOldCamp = CFightObj.eCampEnum.Friend;
                friendSelf.eCurCamp = CFightObj.eCampEnum.Friend;
                fo.dicCamp[pCamp] = friendSelf;
                CFightObj.sCampNode enemyDefault = default(CFightObj.sCampNode);
                enemyDefault.nCampID = 1916;
                enemyDefault.eOldCamp = CFightObj.eCampEnum.Enemy;
                enemyDefault.eCurCamp = CFightObj.eCampEnum.Enemy;
                fo.dicCamp[1916] = enemyDefault;
            }
            if (ent != null)
            {
                int maxHp = fo.GetMaxHP();
                if (maxHp > 0 && fo.GetCurHP() <= 0 && ent.propertyPart != null)
                {
                    ent.propertyPart.SetValue(ePropValueType.Original, 2, maxHp);
                    fo.SysncHP();
                }
            }
        }

        private static void ConfigureEntityAsPlayerAlly(CEntity ent, CEntity player, CTerritory allyTerr)
        {
            if (ent == null) return;
            try
            {
                if (ent.fightObj != null)
                {
                    ConfigureFightObjAsPlayerAlly(ent.fightObj as CFightObj, ent, player, allyTerr);
                }
                if (ent.gameObject != null)
                {
                    CFightObj[] childFos = ent.gameObject.GetComponentsInChildren<CFightObj>(true);
                    if (childFos != null)
                    {
                        for (int i = 0; i < childFos.Length; i++)
                        {
                            CFightObj cfo = childFos[i];
                            if (cfo == null) continue;
                            CEntity ce = cfo.GetComponent<CEntity>();
                            ConfigureFightObjAsPlayerAlly(cfo, ce ?? ent, player, allyTerr);
                        }
                    }
                }
            }
            catch { }
        }

        private static void PreConfigureAllyAIData(GameObject go, CEntity owner, bool forceFollowMode)
        {
            if (go == null) return;
            try
            {
                CAIData[] aids = go.GetComponentsInChildren<CAIData>(true);
                if (aids != null)
                {
                    FieldInfo fiArea = forceFollowMode
                        ? typeof(CAIData).GetField("Partrol_pArea", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        : null;
                    for (int i = 0; i < aids.Length; i++)
                    {
                        CAIData aid = aids[i];
                        if (aid == null) continue;
                        aid.Partrol_bPauseBySceneGrid = false;
                        if (aid.Follow_fKeepDist < 2.4f) aid.Follow_fKeepDist = 2.6f;
                        if (aid.Follow_fTriggerRadius < 1.5f) aid.Follow_fTriggerRadius = 1.6f;
                        if (aid.Partrol_KeepDist.Max < 2.0f)
                        {
                            aid.Partrol_KeepDist.Min = 2.4f;
                            aid.Partrol_KeepDist.Max = 3.6f;
                            aid.Partrol_KeepDist.Gap = 0.25f;
                        }
                        if (aid.Partrol_TriggerRadius.Max < 0.8f)
                        {
                            aid.Partrol_TriggerRadius.Min = 1.4f;
                            aid.Partrol_TriggerRadius.Max = 1.7f;
                            aid.Partrol_TriggerRadius.Gap = 0.1f;
                        }
                        if (owner != null && owner.transform != null)
                            aid.QuitFight_v3TargetPos = owner.transform.position;
                        if (forceFollowMode &&
                            aid.Partrol_eType != eAIPartrolType.FollowCharacter &&
                            aid.Partrol_eType != eAIPartrolType.Follow &&
                            aid.Partrol_eType != eAIPartrolType.FollowGuestCharacter)
                        {
                            aid.Partrol_eType = eAIPartrolType.FollowCharacter;
                            if (fiArea != null) { try { fiArea.SetValue(aid, null); } catch { } }
                            if (owner != null && owner.transform != null)
                                aid.Partrol_pSinglePoint = owner.transform;
                        }
                    }
                }
            }
            catch { }
        }

        private static void ClearFsmPartStates(CFSMPart fsm)
        {
            if (fsm == null) return;
            try
            {
                fsm.m_bSaveToDB = false;
                fsm.m_bSaveToServer = false;
                if (fsm.m_listState != null)
                {
                    for (int s = 0; s < fsm.m_listState.Count; s++)
                    {
                        CFSMState st = fsm.m_listState[s];
                        if (st == null) continue;
                        st.onEnterUnityEvent = new UnityEngine.Events.UnityEvent();
                        st.onActiveUnityEvent = new UnityEngine.Events.UnityEvent();
                        st.onShutDownUnityEvent = new UnityEngine.Events.UnityEvent();
                        st.onExitUnityEvent = new UnityEngine.Events.UnityEvent();
                        if (st.listEnableGameobj != null) st.listEnableGameobj.Clear();
                    }
                    fsm.m_listState.Clear();
                }
                FieldInfo fiCur = typeof(CFSMPart).GetField("m_pCurState", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (fiCur != null) fiCur.SetValue(fsm, null);
            }
            catch { }
        }

        private static Type s_unityAnimatorType = null;
        private static bool s_unityAnimatorTypeSearched = false;

        private static Type GetUnityAnimatorType()
        {
            if (s_unityAnimatorTypeSearched) return s_unityAnimatorType;
            s_unityAnimatorTypeSearched = true;
            try
            {
                Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < asms.Length; i++)
                {
                    Type t = asms[i].GetType("UnityEngine.Animator", false);
                    if (t != null) { s_unityAnimatorType = t; break; }
                }
            }
            catch { }
            return s_unityAnimatorType;
        }

        private static void EnsureDogSafeComponents(GameObject go, CEntity ent)
        {
            if (go == null) return;
            try
            {
                if (ent == null) ent = go.GetComponent<CEntity>();
                CAnimationPart ap = go.GetComponentInChildren<CAnimationPart>(true);
                if (ap == null)
                {
                    Type animT = GetUnityAnimatorType();
                    Component anim = (animT != null) ? go.GetComponentInChildren(animT, true) : null;
                    GameObject host = (anim != null) ? anim.gameObject : null;
                    if (host == null)
                    {
                        host = new GameObject("ModSafeAnim");
                        host.transform.SetParent(go.transform, false);
                        if (animT != null) host.AddComponent(animT);
                    }
                    ap = host.GetComponent<CAnimationPart>();
                    if (ap == null) ap = host.AddComponent<CAnimationPart>();
                }
                if (ent != null && ent.animationPart == null) ent.animationPart = ap;
            }
            catch { }
        }

        private static bool HasValidUnitAvatar(int unitId)
        {
            try
            {
                if (CGameSystem.Instance != null && CGameSystem.Instance.avatarSystem != null)
                {
                    List<sAvatarResourceConfig> res = CGameSystem.Instance.avatarSystem.GetAvatarRes_Unit(unitId);
                    return res != null && res.Count > 0;
                }
            }
            catch { }
            return false;
        }

        private bool CanUpgradeFallbackUnit(int unitId)
        {
            if (_unitPrefabPathCache.ContainsKey(unitId)) return true;
            try
            {
                IGameDataCenter dc = CClientGlobal.Instance != null ? CClientGlobal.Instance.GetGameDataCenter : null;
                CUnitConfigLoader ul = (dc != null) ? (dc.GetConfigLoader(eGameConfigType.Unit) as CUnitConfigLoader) : null;
                sUnitConfig uc = (ul != null) ? ul.GetConfig(unitId) : null;
                if (uc == null) return false;
                string ap = null;
                string hint = ExtractStemHint(uc.szIcon);
                if (hint.Length >= 3) _unitIconStemToPath.TryGetValue(hint, out ap);
                if (string.IsNullOrEmpty(ap))
                {
                    string nm = StripRichTags(uc.szStringName);
                    if (!string.IsNullOrEmpty(nm)) _unitNameToPath.TryGetValue(nm, out ap);
                }
                if (!string.IsNullOrEmpty(ap))
                {
                    _unitPrefabPathCache[unitId] = ap;
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static FieldInfo s_fiFsmCurState;
        private static FieldInfo s_fiFsmActive;
        private static FieldInfo s_fiFsmEndState;
        private static MethodInfo s_miFsmActive;
        private static FieldInfo s_fiAiFsmActive;
        private static bool s_fsmReflectInit;

        internal static bool IsAiFsmActive(CAIFSM ai)
        {
            if (ai == null) return false;
            EnsureFsmReflection();
            if (s_fiAiFsmActive == null) return true;
            try { return (bool)s_fiAiFsmActive.GetValue(ai); } catch { return true; }
        }

        private static void EnsureFsmReflection()
        {
            if (s_fsmReflectInit) return;
            s_fsmReflectInit = true;
            BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            s_fiFsmCurState = typeof(CFSMPart).GetField("m_pCurState", bf);
            s_fiFsmActive = typeof(CFSMPart).GetField("m_bActive", bf);
            s_fiFsmEndState = typeof(CFSMPart).GetField("m_nEndState", bf);
            s_miFsmActive = typeof(CFSMPart).GetMethod("Active", bf);
            s_fiAiFsmActive = typeof(CAIFSM).GetField("m_bIsActive", bf);
        }

        private static void DisableGameObjectListSafe(List<GameObject> gos)
        {
            if (gos == null) return;
            for (int i = 0; i < gos.Count; i++)
            {
                GameObject g = gos[i];
                if (g != null && g.activeSelf)
                {
                    try { g.SetActive(false); } catch { }
                }
            }
        }

        private static void SanitizeFsmPart(CFSMPart fsm, bool isStoryHire)
        {
            if (fsm == null) return;
            EnsureFsmReflection();
            fsm.m_bSaveToDB = false;
            fsm.m_bSaveToServer = false;
            if (fsm.m_listState == null || fsm.m_listState.Count == 0) return;

            int cutState = isStoryHire ? 20 : 10;
            CFSMState stZero = null;
            for (int i = 0; i < fsm.m_listState.Count; i++)
            {
                CFSMState st = fsm.m_listState[i];
                if (st == null) continue;
                if (st.nStateID != 0)
                    DisableGameObjectListSafe(st.listEnableGameobj);
                else if (stZero == null)
                    stZero = st;
            }
            if (stZero != null && fsm.m_listState[0] != stZero)
            {
                fsm.m_listState.Remove(stZero);
                fsm.m_listState.Insert(0, stZero);
            }

            List<CFSMState> kept = new List<CFSMState>(fsm.m_listState.Count);
            for (int i = 0; i < fsm.m_listState.Count; i++)
            {
                CFSMState st = fsm.m_listState[i];
                if (st == null) continue;
                if (st.listEnableGameobj != null)
                {
                    for (int g = st.listEnableGameobj.Count - 1; g >= 0; g--)
                    {
                        if (st.listEnableGameobj[g] == null)
                            st.listEnableGameobj.RemoveAt(g);
                    }
                }
                if (st.listTriggerData_GameTime != null) st.listTriggerData_GameTime.Clear();
                if (i > 0 && st.nStateID >= cutState)
                {
                    st.onEnterUnityEvent = new UnityEngine.Events.UnityEvent();
                    st.onActiveUnityEvent = new UnityEngine.Events.UnityEvent();
                    st.onShutDownUnityEvent = new UnityEngine.Events.UnityEvent();
                    st.onExitUnityEvent = new UnityEngine.Events.UnityEvent();
                    DisableGameObjectListSafe(st.listEnableGameobj);
                    continue;
                }
                if (st.listNextState != null)
                {
                    for (int t = st.listNextState.Count - 1; t >= 0; t--)
                    {
                        CFSMState.sFSMTransition tr = st.listNextState[t];
                        if (tr == null || tr.nNextStateID >= cutState)
                            st.listNextState.RemoveAt(t);
                    }
                }
                if (st.nStateID != 0)
                {
                    st.onEnterUnityEvent = new UnityEngine.Events.UnityEvent();
                    st.onShutDownUnityEvent = new UnityEngine.Events.UnityEvent();
                    st.onExitUnityEvent = new UnityEngine.Events.UnityEvent();
                }
                kept.Add(st);
            }
            fsm.m_listState.Clear();
            for (int i = 0; i < kept.Count; i++) fsm.m_listState.Add(kept[i]);

            if (s_fiFsmCurState != null) { try { s_fiFsmCurState.SetValue(fsm, null); } catch { } }
            if (s_fiFsmEndState != null)
            {
                int endId = (fsm.m_listState.Count > 0) ? fsm.m_listState[fsm.m_listState.Count - 1].nStateID : 0;
                try { s_fiFsmEndState.SetValue(fsm, endId); } catch { }
            }
        }

        private static bool IsHazardousForcedCloneComponent(MonoBehaviour mb, bool isStoryHire)
        {
            if (mb == null) return false;
            if (mb is CFSMPart) return false;
            if (mb is CActivator_SceneGrid || mb is CActivator_SceneGridAndFightState || mb is CAIEnemyTag) return true;
            if (mb is CFSMTriggerListener_Base) return true;
            if (mb is CFSMFunction_GoOutScene || mb is CFSMFunction_PlayTimeline || mb is CFSMFunction_ConditionSelect || mb is CFSMFunction_DestroyEntity) return true;
            if (mb is CFSMFunction_HireAdd || mb is CFSMFunction_HireRemove || mb is CFSMFunction_RemoveHire) return true;
            if (mb is CColliderTrigger_PlayerEvent || mb is CColliderTrigger_ByMonster || mb is CColliderTriggerPart_UnitAddBuff) return true;

            if (mb is CFSMFunction_SetHPToMax) return !isStoryHire;
            if (mb.GetType() == typeof(CFSMFunction_Base))
            {
                if (!isStoryHire) return true;
                return ((CFSMFunction_Base)mb).m_nFSMParam >= 10;
            }

            string tn = mb.GetType().Name;
            if (tn.StartsWith("CFSMFunction_OpenUI", StringComparison.Ordinal)) return true;
            if (tn.StartsWith("CFSMFunction_AutoDrop", StringComparison.Ordinal)) return true;
            if (tn.StartsWith("CFSMFunction_KillCount", StringComparison.Ordinal)) return true;
            if (tn == "CFSMFunction_RemoveTeamate" || tn == "CFSMFunction_RemoveWorker" || tn == "CFSMFunction_AddEnergyBar") return true;
            if (tn == "CColliderTriggerPart_CharacterAddBuff") return true;
            return false;
        }

        private void SanitizeForcedAllyClone(GameObject go, int unitId, bool isStoryHire)
        {
            if (go == null) return;
            try
            {
                MonoBehaviour[] mbs = go.GetComponentsInChildren<MonoBehaviour>(true);
                if (mbs == null || mbs.Length == 0) return;

                if (!_loggedUnitSpawnComps.ContainsKey(unitId))
                {
                    _loggedUnitSpawnComps[unitId] = true;
                    StringBuilder csb = new StringBuilder(512);
                    for (int i = 0; i < mbs.Length; i++)
                    {
                        MonoBehaviour m = mbs[i];
                        if (m == null) continue;
                        if (csb.Length > 0) csb.Append(", ");
                        csb.Append(m.GetType().Name);
                        if (m.gameObject != go) csb.Append('(').Append(m.gameObject.name).Append(')');
                        if (csb.Length > 900) { csb.Append("..."); break; }
                    }
                    Debug.Log("[ModBridge] Unit #" + unitId + " comps: " + csb.ToString());
                }

                CEntity rootEnt = go.GetComponent<CEntity>();
                CFSMPart rootFsm = go.GetComponent<CFSMPart>();
                CFightObj rootFo = go.GetComponent<CFightObj>();

                List<MonoBehaviour> toRemove = null;
                for (int i = 0; i < mbs.Length; i++)
                {
                    MonoBehaviour m = mbs[i];
                    if (m == null) continue;
                    if (m is CFSMPart)
                    {
                        SanitizeFsmPart((CFSMPart)m, isStoryHire);
                        continue;
                    }
                    if (m.gameObject != go && m.gameObject.name.IndexOf("WeiYa", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        try { m.gameObject.SetActive(false); } catch { }
                    }
                    if (isStoryHire && m is CFSMFunction_OpenUI_ConditionWnd)
                    {
                        try
                        {
                            CFSMFunction_SetHPToMax hpFn = m.gameObject.GetComponent<CFSMFunction_SetHPToMax>();
                            if (hpFn == null) hpFn = m.gameObject.AddComponent<CFSMFunction_SetHPToMax>();
                            hpFn.m_pFSMPart = rootFsm;
                            hpFn.m_pFightObj = rootFo;
                            hpFn.m_nFSMParam = 0;
                            hpFn.m_bTriggerWhenOnEnable = false;
                            COperatePoint[] ops = m.gameObject.GetComponentsInChildren<COperatePoint>(true);
                            if (ops != null)
                            {
                                for (int k = 0; k < ops.Length; k++)
                                {
                                    COperatePoint op = ops[k];
                                    if (op == null) continue;
                                    op.onClickUnityEvent = new UnityEngine.Events.UnityEvent();
                                    op.onClickUnityEvent.AddListener(() => { if (hpFn != null) hpFn.Trigger(0); });
                                }
                            }
                        }
                        catch { }
                    }
                    if (IsHazardousForcedCloneComponent(m, isStoryHire))
                    {
                        if (toRemove == null) toRemove = new List<MonoBehaviour>();
                        toRemove.Add(m);
                        continue;
                    }
                    if (m is CFightObj)
                    {
                        CFightObj fo = (CFightObj)m;
                        if (!isStoryHire)
                        {
                            fo.nDeadStateID = 0;
                            fo.OnDeadUnityEvent = new UnityEngine.Events.UnityEvent();
                        }
                        fo.OnHitUnityEvent = new UnityEngine.Events.UnityEvent();
                        fo.OnHitSuccUnityEvent = new UnityEngine.Events.UnityEvent();
                    }
                    else if (m is CColliderTrigger)
                    {
                        ((CColliderTrigger)m).onCharacterEnterUnityEvent = new UnityEngine.Events.UnityEvent();
                    }
                }

                if (toRemove != null)
                {
                    for (int i = 0; i < toRemove.Count; i++)
                    {
                        MonoBehaviour m = toRemove[i];
                        if (m == null) continue;
                        if (m is IMonoLaunchHandler)
                        {
                            try { CGameSceneMgr.UnSubMonoLaunch((IMonoLaunchHandler)m); } catch { }
                        }
                        try { UnityEngine.Object.DestroyImmediate(m, false); }
                        catch
                        {
                            try { m.enabled = false; } catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModBridge] SanitizeForcedAllyClone ex: " + ex.Message);
            }
        }

        private static void EnsureAllyActivatedAndAlive(GameObject go, CEntity ent, bool isStoryHire, bool forceAvatarUpdate)
        {
            if (go == null) return;
            EnsureFsmReflection();
            try
            {
                CFSMPart[] fsms = go.GetComponentsInChildren<CFSMPart>(true);
                if (fsms != null)
                {
                    for (int i = 0; i < fsms.Length; i++)
                    {
                        CFSMPart fsm = fsms[i];
                        if (fsm == null) continue;
                        fsm.enabled = true;
                        fsm.m_bSaveToDB = false;
                        fsm.m_bSaveToServer = false;
                        if (fsm.m_listState != null && fsm.m_listState.Count > 0)
                        {
                            for (int s = 1; s < fsm.m_listState.Count; s++)
                                DisableGameObjectListSafe(fsm.m_listState[s].listEnableGameobj);

                            int defId = fsm.m_listState[0].nStateID;
                            if (fsm.GetCurrentStateID() != defId)
                                fsm.GotoDefaultState();

                            bool fsmAct = (s_fiFsmActive != null) && ((bool)s_fiFsmActive.GetValue(fsm));
                            if (!fsmAct && s_miFsmActive != null)
                            {
                                try { s_miFsmActive.Invoke(fsm, null); } catch { }
                            }
                            List<GameObject> en = fsm.m_listState[0].listEnableGameobj;
                            if (en != null)
                            {
                                for (int g = 0; g < en.Count; g++)
                                {
                                    if (en[g] != null && !en[g].activeSelf) en[g].SetActive(true);
                                }
                            }
                        }
                    }
                }

                COperatePoint[] ops = go.GetComponentsInChildren<COperatePoint>(true);
                if (ops != null)
                {
                    for (int i = 0; i < ops.Length; i++)
                    {
                        COperatePoint op = ops[i];
                        if (op == null) continue;
                        if (!isStoryHire || op.StringTipsID == 60020 || op.StringTipsID == 60003)
                        {
                            if (op.gameObject.activeSelf) op.gameObject.SetActive(false);
                            if (CGameSceneMgr.Instance != null)
                            {
                                try { CGameSceneMgr.Instance.RemoveOperatePoint(op); } catch { }
                            }
                        }
                    }
                }

                if (ent != null)
                {
                    CFightObj fo = ent.fightObj as CFightObj;
                    if (fo != null)
                    {
                        fo.enabled = true;
                        if (!isStoryHire) fo.nDeadStateID = 0;
                        int maxHp = fo.GetMaxHP();
                        if (maxHp <= 1 && ent.propertyPart != null)
                        {
                            maxHp = 1000;
                            ent.propertyPart.SetValue(ePropValueType.Original, 1, maxHp);
                            ent.propertyPart.SetValue(ePropValueType.Original, 2, maxHp);
                        }
                        if (fo.GetCurHP() < maxHp || fo.GetCurHP() <= 0)
                        {
                            fo.SetHP(maxHp);
                            fo.SysncHP();
                        }
                        if (!fo.IsActive()) fo.Active();
                    }

                    CAIFSM[] aiFsms = go.GetComponentsInChildren<CAIFSM>(true);
                    if (aiFsms != null)
                    {
                        for (int i = 0; i < aiFsms.Length; i++)
                        {
                            CAIFSM ai = aiFsms[i];
                            if (ai == null) continue;
                            ai.enabled = true;
                            bool aiAct = (s_fiAiFsmActive != null) && ((bool)s_fiAiFsmActive.GetValue(ai));
                            if (!aiAct) ai.Active();
                        }
                    }

                    if (fo != null && (fo.GetCurHP() < fo.GetMaxHP() || fo.GetCurHP() <= 0))
                    {
                        fo.SetHP(fo.GetMaxHP());
                        fo.SysncHP();
                    }
                    if (fo != null && !fo.IsActive()) fo.Active();

                    if (ent.avatarPart != null && ent.nConfigID > 0 &&
                        (forceAvatarUpdate || ent.GetComponent(typeof(CAIFSM_Avatarable)) != null))
                    {
                        try { ent.avatarPart.UpdateView(); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModBridge] EnsureAllyActivatedAndAlive ex: " + ex.Message);
            }
        }

        private static void ForceInitializeAllLaunchNodes(GameObject go)
        {
            if (go == null || CGameSceneMgr.Instance == null) return;
            try { CGameSceneMgr.Instance.ForceInitializeLaunchNode(go); } catch { }
            try
            {
                IMonoLaunchHandler[] hs = go.GetComponentsInChildren<IMonoLaunchHandler>(true);
                if (hs == null || hs.Length == 0) return;
                Dictionary<int, bool> seen = new Dictionary<int, bool>();
                seen[go.GetInstanceID()] = true;
                for (int i = 0; i < hs.Length; i++)
                {
                    IMonoLaunchHandler h = hs[i];
                    if (h == null) continue;
                    GameObject hgo = null;
                    try { hgo = h.gameObject; } catch { }
                    if (hgo == null) continue;
                    int gid = hgo.GetInstanceID();
                    if (seen.ContainsKey(gid)) continue;
                    seen[gid] = true;
                    try { CGameSceneMgr.Instance.ForceInitializeLaunchNode(hgo); } catch { }
                }
            }
            catch { }
        }

        private static void SafeTeardownSpawnedObject(GameObject go)
        {
            if (go == null) return;
            try { go.SetActive(false); } catch { }
            try
            {
                IMonoLaunchHandler[] handlers = go.GetComponentsInChildren<IMonoLaunchHandler>(true);
                if (handlers != null && handlers.Length > 0)
                {
                    for (int i = handlers.Length - 1; i >= 0; i--)
                    {
                        IMonoLaunchHandler h = handlers[i];
                        if (h == null || h is CEntity || h is CObserver) continue;
                        try { CGameSceneMgr.UnSubMonoLaunch(h); }
                        catch (Exception ex) { Debug.LogWarning("[ModBridge] SafeTeardown p1 ex: " + ex.Message); }
                    }
                    for (int i = handlers.Length - 1; i >= 0; i--)
                    {
                        IMonoLaunchHandler h = handlers[i];
                        if (h == null || !(h is CEntity || h is CObserver)) continue;
                        try { CGameSceneMgr.UnSubMonoLaunch(h); }
                        catch (Exception ex) { Debug.LogWarning("[ModBridge] SafeTeardown p2 ex: " + ex.Message); }
                    }
                }
            }
            catch { }
            try { UnityEngine.Object.Destroy(go); } catch { }
        }

        private void DespawnAllForcedAllies(bool releaseHandles)
        {
            try
            {
                if (_forcedSpawned.Count > 0)
                {
                    List<int> keys = new List<int>(_forcedSpawned.Keys);
                    for (int i = 0; i < keys.Count; i++)
                    {
                        CEntity ent;
                        if (_forcedSpawned.TryGetValue(keys[i], out ent) && ent != null && ent.gameObject != null)
                        {
                            SafeTeardownSpawnedObject(ent.gameObject);
                        }
                    }
                    _forcedSpawned.Clear();
                }
            }
            catch { }
            if (_allyTerritoryObj != null)
            {
                try { _allyTerritoryObj.SetActive(false); UnityEngine.Object.Destroy(_allyTerritoryObj); } catch { }
                _allyTerritoryObj = null;
                _allyTerritory = null;
            }
            if (releaseHandles)
            {
                foreach (KeyValuePair<int, object> kv in _retainedUnitHandles) ReleaseYooHandle(kv.Value);
                _retainedUnitHandles.Clear();
            }
        }

        private bool TrySpawnForcedAlly(IGameSystem gs, CEntity player, int unitId, int slotIdx)
        {
            if (player == null || CGameSceneMgr.Instance == null || _sceneTransitioning) return false;
            try
            {
                CEntity existing;
                if (_forcedSpawned.TryGetValue(unitId, out existing) && existing != null && existing.gameObject != null)
                {
                    ModForceAllyFollower oldFlw = existing.gameObject.GetComponent<ModForceAllyFollower>();
                    bool canUpgrade = (oldFlw != null && oldFlw.isAvatarFallback && CanUpgradeFallbackUnit(unitId));
                    if (!canUpgrade && existing.fightObj != null && !existing.fightObj.IsDead())
                        return true;
                    DespawnForcedAlly(unitId);
                }

                bool isAvatarModel;
                object yooHandle;
                GameObject tplGo = ResolveUnitNativePrefab(gs, unitId, out isAvatarModel, out yooHandle);
                if (tplGo == null) return false;

                float ang = (slotIdx % 8) * 45f * Mathf.Deg2Rad;
                Vector3 spawnPos = player.transform.position + new Vector3(Mathf.Cos(ang) * 1.8f, Mathf.Sin(ang) * 1.8f, 0f);
                if (!CNavmeshUtil.PointIsOnNavmesh(spawnPos))
                    spawnPos = player.transform.position;

                GameObject go = UnityEngine.Object.Instantiate(tplGo);
                object prevHandle;
                if (_retainedUnitHandles.TryGetValue(unitId, out prevHandle))
                {
                    if (prevHandle != null && !object.ReferenceEquals(prevHandle, yooHandle))
                        ReleaseYooHandle(prevHandle);
                    _retainedUnitHandles.Remove(unitId);
                }
                if (yooHandle != null) _retainedUnitHandles[unitId] = yooHandle;

                SanitizeForcedAllyClone(go, unitId, false);
                PreConfigureAllyAIData(go, player, true);

                go.transform.position = spawnPos;
                go.transform.eulerAngles = Vector3.zero;
                if (!go.activeSelf) go.SetActive(true);

                CTerritory allyTerr = GetOrCreateAllyTerritory(player);
                CEntity ent = go.GetComponent<CEntity>();
                if (ent != null)
                {
                    ent.nConfigID = unitId;
                    ent.nCustomID = isAvatarModel ? (900000 + unitId) : 0;
                }
                CFightObj[] preFos = go.GetComponentsInChildren<CFightObj>(true);
                if (preFos != null)
                {
                    for (int i = 0; i < preFos.Length; i++)
                    {
                        if (preFos[i] == null) continue;
                        preFos[i].campID = 0;
                        if (allyTerr != null) preFos[i].pTerritory = allyTerr;
                    }
                }

                PrepareEntityPropertyNodes(go, unitId, isAvatarModel);
                ForceInitializeAllLaunchNodes(go);
                if (ent != null)
                {
                    ConfigureEntityAsPlayerAlly(ent, player, allyTerr);
                    EnsureAllyActivatedAndAlive(go, ent, false, isAvatarModel);
                    ModForceAllyFollower flw = go.GetComponent<ModForceAllyFollower>();
                    if (flw == null) flw = go.AddComponent<ModForceAllyFollower>();
                    flw.owner = player;
                    flw.allyTerritory = allyTerr;
                    flw.unitId = unitId;
                    flw.isAvatarFallback = isAvatarModel;
                    flw.isStoryHire = false;
                    flw.isTamedLive = false;
                    _forcedSpawned[unitId] = ent;

                    CAIFSM fsm = go.GetComponent<CAIFSM>();
                    CFightObj sFo = ent.fightObj as CFightObj;
                    CFSMPart fp = ent.FSMPart;
                    bool aiAct = (fsm != null && s_fiAiFsmActive != null) ? ((bool)s_fiAiFsmActive.GetValue(fsm)) : false;
                    Debug.Log("[ModBridge] ForceHire spawned #" + unitId + " fallback=" + isAvatarModel +
                              " tpl=" + tplGo.name + " hp=" + (sFo != null ? sFo.GetCurHP() : -1) + "/" + (sFo != null ? sFo.GetMaxHP() : -1) +
                              " dead=" + (sFo != null && sFo.IsDead()) + " foAct=" + (sFo != null && sFo.IsActive()) +
                              " fsmSt=" + (fp != null ? fp.GetCurrentStateID() : -1) + " ai=" + (fsm != null ? fsm.GetType().Name : "none") +
                              "(" + aiAct + ")");
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModBridge] TrySpawnForcedAlly ex: " + ex.Message);
                return false;
            }
        }

        private void DespawnForcedAlly(int unitId)
        {
            CEntity ent;
            if (_forcedSpawned.TryGetValue(unitId, out ent))
            {
                _forcedSpawned.Remove(unitId);
                if (ent != null && ent.gameObject != null)
                {
                    SafeTeardownSpawnedObject(ent.gameObject);
                }
            }
            object h;
            if (_retainedUnitHandles.TryGetValue(unitId, out h))
            {
                _retainedUnitHandles.Remove(unitId);
                ReleaseYooHandle(h);
            }
        }

        private void DespawnAllModHires(bool releaseHandles)
        {
            try
            {
                if (_modHireSpawned.Count > 0)
                {
                    List<int> keys = new List<int>(_modHireSpawned.Keys);
                    for (int i = 0; i < keys.Count; i++)
                    {
                        CEntity ent;
                        if (_modHireSpawned.TryGetValue(keys[i], out ent) && ent != null && ent.gameObject != null)
                        {
                            SafeTeardownSpawnedObject(ent.gameObject);
                        }
                    }
                    _modHireSpawned.Clear();
                }
            }
            catch { }
            if (releaseHandles)
            {
                foreach (KeyValuePair<int, object> kv in _retainedHireHandles) ReleaseYooHandle(kv.Value);
                _retainedHireHandles.Clear();
            }
        }

        private void DespawnModHire(int hireId)
        {
            CEntity ent;
            if (_modHireSpawned.TryGetValue(hireId, out ent))
            {
                _modHireSpawned.Remove(hireId);
                if (ent != null && ent.gameObject != null)
                    SafeTeardownSpawnedObject(ent.gameObject);
            }
            object h;
            if (_retainedHireHandles.TryGetValue(hireId, out h))
            {
                _retainedHireHandles.Remove(hireId);
                ReleaseYooHandle(h);
            }
        }

        private static void CollectNativeHireSpawnerNodes(Dictionary<int, GameObject> liveNative)
        {
            try
            {
                CHireSpwaner[] hss = UnityEngine.Object.FindObjectsOfType(typeof(CHireSpwaner)) as CHireSpwaner[];
                if (hss == null || hss.Length == 0) return;
                BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                if (s_fiHireSpawnerNodes == null)
                    s_fiHireSpawnerNodes = typeof(CHireSpwaner).GetField("m_dicNode", bf);
                if (s_fiHireSpawnerNodes == null) return;
                for (int i = 0; i < hss.Length; i++)
                {
                    if (hss[i] == null) continue;
                    System.Collections.IDictionary dic = s_fiHireSpawnerNodes.GetValue(hss[i]) as System.Collections.IDictionary;
                    if (dic == null) continue;
                    List<object> deadKeys = null;
                    foreach (System.Collections.DictionaryEntry kv in dic)
                    {
                        if (!(kv.Key is int)) continue;
                        int hid = (int)kv.Key;
                        object node = kv.Value;
                        if (node == null) continue;
                        if (s_fiHireNodeGo == null)
                            s_fiHireNodeGo = node.GetType().GetField("pGO", bf);
                        GameObject pgo = (s_fiHireNodeGo != null) ? (s_fiHireNodeGo.GetValue(node) as GameObject) : null;
                        if (pgo != null) liveNative[hid] = pgo;
                        else
                        {
                            if (deadKeys == null) deadKeys = new List<object>();
                            deadKeys.Add(kv.Key);
                        }
                    }
                    if (deadKeys != null)
                    {
                        for (int k = 0; k < deadKeys.Count; k++) dic.Remove(deadKeys[k]);
                    }
                }
            }
            catch { }
        }

        private void EnsureHirePoolLoaded()
        {
            if (_hirePoolLoaded) return;
            _hirePoolLoaded = true;
            try
            {
                string ap;
                if (!_allPrefabStemToPath.TryGetValue("hire_pool", out ap) || string.IsNullOrEmpty(ap))
                {
                    ap = "Assets/single_bundles/shengwu_prefab/prefabpool/hire_pool.prefab";
                }
                object h;
                GameObject poolGo = LoadPrefabByYooAsset(ap, out h);
                if (poolGo == null)
                {
                    Debug.LogWarning("[ModBridge] EnsureHirePoolLoaded: poolGo null for " + ap);
                    return;
                }
                _hirePoolHandle = h;
                CPrefabTemplatePool pool = poolGo.GetComponentInChildren<CPrefabTemplatePool>(true);
                StringBuilder logSb = new StringBuilder(512);
                if (pool != null && pool.listPrefabTemplateInfo != null)
                {
                    for (int i = 0; i < pool.listPrefabTemplateInfo.Count; i++)
                    {
                        sPrefabTemplateInfo info = pool.listPrefabTemplateInfo[i];
                        if (string.IsNullOrEmpty(info.szTemplateName) || info.pTemplateGO == null) continue;
                        string k = info.szTemplateName.Trim().ToLowerInvariant();
                        _hirePoolTemplates[k] = info.pTemplateGO;
                        if (CPrefabTemplatePoolMgr.Instance != null)
                        {
                            sPrefabTemplateInfo existing;
                            if (!CPrefabTemplatePoolMgr.Instance.GetPrefabTemplate(info.szTemplateName, out existing) || existing.pTemplateGO == null)
                                CPrefabTemplatePoolMgr.Instance.AddPrefabTemplate(info);
                        }
                        if (logSb.Length > 0) logSb.Append(", ");
                        logSb.Append(info.szTemplateName.Trim()).Append("->").Append(info.pTemplateGO.name);
                    }
                }
                Debug.Log("[ModBridge] HirePool loaded entries=" + _hirePoolTemplates.Count + ": " + logSb.ToString());
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModBridge] EnsureHirePoolLoaded ex: " + ex.Message);
            }
        }

        private GameObject ResolveHirePrefabTemplate(IGameSystem gs, int hireId, string szPrefabTemplate, out bool isFallback, out object outHandle)
        {
            isFallback = false;
            outHandle = null;
            string dynDir = "";
            unsafe { dynDir = ReadShmDynDir(Shm()); }
            InitYooManifest(dynDir);
            EnsureHirePoolLoaded();

            string raw = (szPrefabTemplate != null) ? szPrefabTemplate : "";
            string trimmed = raw.Trim();
            string lower = trimmed.ToLowerInvariant();

            try
            {
                CPrefabTemplatePoolMgr pool = CPrefabTemplatePoolMgr.Instance;
                if (pool != null)
                {
                    sPrefabTemplateInfo pInfo;
                    if (!string.IsNullOrEmpty(raw) && pool.GetPrefabTemplate(raw, out pInfo) && pInfo.pTemplateGO != null)
                        return pInfo.pTemplateGO;
                    if (!string.IsNullOrEmpty(trimmed) && pool.GetPrefabTemplate(trimmed, out pInfo) && pInfo.pTemplateGO != null)
                        return pInfo.pTemplateGO;
                }
            }
            catch { }

            GameObject poolTpl;
            if (!string.IsNullOrEmpty(lower) && _hirePoolTemplates.TryGetValue(lower, out poolTpl) && poolTpl != null)
                return poolTpl;

            string ap;
            if (!string.IsNullOrEmpty(lower) && _allPrefabStemToPath.TryGetValue(lower, out ap))
            {
                GameObject ygo = LoadPrefabByYooAsset(ap, out outHandle);
                if (ygo != null) return ygo;
            }

            if (!string.IsNullOrEmpty(trimmed) && CAssetMgr.Instance != null)
            {
                try
                {
                    CAsset asset = CAssetMgr.Instance.Load<GameObject>(trimmed);
                    GameObject go = (asset != null) ? (asset.asset as GameObject) : null;
                    if (go != null) return go;
                }
                catch { }
            }

            if (_allPrefabStemToPath.Count > 0 && !string.IsNullOrEmpty(lower))
            {
                string[] candidates = null;
                if (lower == "pets_nianshou")
                    candidates = new string[] { "monster_pets_nianshou", "pets_nianshou" };
                else if (lower == "hire_jiangna")
                    candidates = new string[] { "hire_jiangna", "guyong_jiangna", "npc_jiangna" };
                else if (lower == "guyong_xiaofangwurenji")
                    candidates = new string[] { "monster_xiaofangweurenji", "guyong_xiaofangwurenji", "xiaofangwurenji", "xiaofangweurenji", "huoqu_wurenji" };
                else if (lower == "guyong_dog")
                    candidates = new string[] { "guyong_dog", "guyong_dog_xinshou_out", "guyong_dog_out", "guyong_dog_xinshou_home" };
                else if (lower == "guyong_dog_hashiqi")
                    candidates = new string[] { "guyong_dog_hashiqi", "guyong_dog_hashiqi_out", "guyong_dog_hashiqi_home" };
                else if (lower == "guyong_npc_niao")
                    candidates = new string[] { "guyong_npc_niao", "sot_neo", "npc_niao", "npc_neo" };
                else if (lower == "guyong_npc_k")
                    candidates = new string[] { "guyong_npc_k", "sot_k", "npc_k", "hire_k", "hire_basics_xiandanqiang5" };
                else if (lower == "guyong_npc_laowu")
                    candidates = new string[] { "guyong_npc_laowu", "npc_laowu", "hire_laowu", "laowu" };
                if (candidates != null)
                {
                    for (int c = 0; c < candidates.Length; c++)
                    {
                        if (_hirePoolTemplates.TryGetValue(candidates[c], out poolTpl) && poolTpl != null)
                            return poolTpl;
                        if (_allPrefabStemToPath.TryGetValue(candidates[c], out ap))
                        {
                            GameObject ygo = LoadPrefabByYooAsset(ap, out outHandle);
                            if (ygo != null) return ygo;
                        }
                    }
                }
                foreach (KeyValuePair<string, string> kv in _allPrefabStemToPath)
                {
                    if (kv.Key.Contains(lower) && !kv.Key.EndsWith("_home", StringComparison.Ordinal) &&
                        !kv.Key.EndsWith("_hide", StringComparison.Ordinal) && !kv.Key.StartsWith("enemy_", StringComparison.Ordinal))
                    {
                        GameObject ygo = LoadPrefabByYooAsset(kv.Value, out outHandle);
                        if (ygo != null) return ygo;
                    }
                }
                foreach (KeyValuePair<string, string> kv in _allPrefabStemToPath)
                {
                    if (kv.Key.Contains(lower) && !kv.Key.EndsWith("_home", StringComparison.Ordinal))
                    {
                        GameObject ygo = LoadPrefabByYooAsset(kv.Value, out outHandle);
                        if (ygo != null) return ygo;
                    }
                }
            }

            if (hireId != 2124 && _unitPrefabPathCache.TryGetValue(hireId, out ap) && !string.IsNullOrEmpty(ap))
            {
                GameObject ygo = LoadPrefabByYooAsset(ap, out outHandle);
                if (ygo != null) return ygo;
            }

            try
            {
                IGameDataCenter dc = CClientGlobal.Instance != null ? CClientGlobal.Instance.GetGameDataCenter : null;
                CHireConfigLoader hl = (dc != null) ? (dc.GetConfigLoader(eGameConfigType.Hire) as CHireConfigLoader) : null;
                object hc = (hl != null) ? hl.GetConfig(hireId) : null;
                if (hc != null)
                {
                    string hName = StripRichTags(GetObjStr(hc, new string[] { "szStringName" }, ""));
                    if (string.IsNullOrEmpty(hName))
                    {
                        int nStr = GetObjInt(hc, new string[] { "nStringName" }, 0);
                        if (nStr > 0 && CLanguageMgr.Instance != null) hName = StripRichTags(CLanguageMgr.Instance.GetString(nStr));
                    }
                    if (!string.IsNullOrEmpty(hName) && _unitNameToPath.TryGetValue(hName, out ap) && !string.IsNullOrEmpty(ap))
                    {
                        GameObject ygo = LoadPrefabByYooAsset(ap, out outHandle);
                        if (ygo != null) return ygo;
                    }
                }
            }
            catch { }

            isFallback = true;
            return ResolveForcedAllyTemplatePrefab(gs);
        }

        private static void PrepareEntityPropertyNodes(GameObject go, int configId, bool isFallbackModel)
        {
            if (go == null) return;
            try
            {
                int workerHp = 0;
                try
                {
                    IGameDataCenter dc = CClientGlobal.Instance != null ? CClientGlobal.Instance.GetGameDataCenter : null;
                    CWorkerConfigLoader wl = (dc != null) ? (dc.GetConfigLoader(eGameConfigType.Worker) as CWorkerConfigLoader) : null;
                    sWorkerConfig wc = (wl != null && configId > 0) ? wl.GetConfig(configId) : null;
                    if (wc != null && wc.nHP > 0) workerHp = wc.nHP;
                }
                catch { }
                CPropertyPart[] pps = go.GetComponentsInChildren<CPropertyPart>(true);
                if (pps == null) return;
                for (int i = 0; i < pps.Length; i++)
                {
                    CPropertyPart pp = pps[i];
                    if (pp == null) continue;
                    if (pp.m_listNode == null) pp.m_listNode = new List<sPropertyNode>();
                    sPropertyNode nodeMax = null;
                    sPropertyNode nodeCur = null;
                    int rawMax = 0;
                    int rawCur = 0;
                    for (int k = 0; k < pp.m_listNode.Count; k++)
                    {
                        sPropertyNode nd = pp.m_listNode[k];
                        if (nd == null) continue;
                        nd.bSaveToDB = false;
                        nd.bSaveToServer = false;
                        if (nd.nPropertyKey == 1)
                        {
                            nodeMax = nd;
                            int v = nd.nEditorPropertyValue;
                            if (v == 0) v = nd.nPropertyValue.GetValue(ePropValueType.Original);
                            if (v > rawMax) rawMax = v;
                        }
                        else if (nd.nPropertyKey == 2)
                        {
                            nodeCur = nd;
                            int v = nd.nEditorPropertyValue;
                            if (v == 0) v = nd.nPropertyValue.GetValue(ePropValueType.Original);
                            if (v > rawCur) rawCur = v;
                        }
                    }
                    int resolvedMax = rawMax;
                    if (resolvedMax <= 1)
                        resolvedMax = (workerHp > 1) ? workerHp : 1000;
                    else if (isFallbackModel && workerHp > resolvedMax)
                        resolvedMax = workerHp;
                    else if (isFallbackModel && resolvedMax < 500)
                        resolvedMax = 1000;

                    if (nodeMax == null)
                    {
                        nodeMax = new sPropertyNode();
                        nodeMax.nPropertyKey = 1;
                        nodeMax.bSaveToDB = false;
                        nodeMax.bSaveToServer = false;
                        pp.m_listNode.Add(nodeMax);
                    }
                    if (rawMax != resolvedMax)
                    {
                        nodeMax.nEditorPropertyValue = resolvedMax;
                        nodeMax.nPropertyValue.SetValue(ePropValueType.Original, resolvedMax);
                    }

                    if (nodeCur == null)
                    {
                        nodeCur = new sPropertyNode();
                        nodeCur.nPropertyKey = 2;
                        nodeCur.bSaveToDB = false;
                        nodeCur.bSaveToServer = false;
                        pp.m_listNode.Add(nodeCur);
                    }
                    if (rawCur <= 0 || rawCur != resolvedMax)
                    {
                        nodeCur.nEditorPropertyValue = resolvedMax;
                        nodeCur.nPropertyValue.SetValue(ePropValueType.Original, resolvedMax);
                    }
                }
            }
            catch { }
        }

        private bool TrySpawnModHire(IGameSystem gs, CEntity player, CHireSystemData hdata, int slotIdx)
        {
            if (player == null || hdata == null || CGameSceneMgr.Instance == null || _sceneTransitioning) return false;
            try
            {
                int hireId = hdata.nID;
                CEntity prevSpawned;
                if (_modHireSpawned.TryGetValue(hireId, out prevSpawned))
                {
                    _modHireSpawned.Remove(hireId);
                    if (prevSpawned != null && prevSpawned.gameObject != null)
                        SafeTeardownSpawnedObject(prevSpawned.gameObject);
                }

                string tplStr = (hdata.pConfig != null) ? hdata.pConfig.szPrefabTemplate : "";
                bool isFallback;
                object yooHandle;
                GameObject tplGo = ResolveHirePrefabTemplate(gs, hireId, tplStr, out isFallback, out yooHandle);
                if (tplGo == null) return false;

                float ang = (slotIdx % 6) * 60f * Mathf.Deg2Rad + 2.356f;
                float rad = 2.6f + (slotIdx / 6) * 1.4f;
                Vector3 spawnPos = player.transform.position + new Vector3(Mathf.Cos(ang) * rad, Mathf.Sin(ang) * rad, 0f);
                if (!CNavmeshUtil.PointIsOnNavmesh(spawnPos))
                    spawnPos = player.transform.position;

                GameObject go = UnityEngine.Object.Instantiate(tplGo);
                object prevHandle;
                if (_retainedHireHandles.TryGetValue(hireId, out prevHandle))
                {
                    if (prevHandle != null && !object.ReferenceEquals(prevHandle, yooHandle))
                        ReleaseYooHandle(prevHandle);
                    _retainedHireHandles.Remove(hireId);
                }
                if (yooHandle != null) _retainedHireHandles[hireId] = yooHandle;

                // Diag before Sanitize
                int opBefore = 0;
                try { COperatePoint[] opsB = go.GetComponentsInChildren<COperatePoint>(true); opBefore = (opsB != null) ? opsB.Length : 0; } catch { }

                SanitizeForcedAllyClone(go, hireId, true);
                CEntity ent = go.GetComponent<CEntity>();
                EnsureDogSafeComponents(go, ent);
                PreConfigureAllyAIData(go, player, true);

                go.transform.position = spawnPos;
                go.transform.eulerAngles = Vector3.zero;
                if (!go.activeSelf) go.SetActive(true);

                CTerritory allyTerr = GetOrCreateAllyTerritory(player);
                CFightObj pFo = player.fightObj as CFightObj;
                int pCamp = (pFo != null) ? pFo.CampID : 0;
                bool hasValidAv = HasValidUnitAvatar(hireId);
                bool boundUnitCfg = false;
                if (ent != null)
                {
                    ent.nCustomID = 950000 + (Math.Abs(hireId) % 40000);
                    boundUnitCfg = (hireId != 2124) && hasValidAv &&
                                   (ent.nConfigID <= 0 || isFallback || ent.GetComponent(typeof(CAIFSM_Avatarable)) != null);
                    if (boundUnitCfg)
                        ent.nConfigID = hireId;
                }
                CFightObj[] preFos = go.GetComponentsInChildren<CFightObj>(true);
                if (preFos != null)
                {
                    for (int i = 0; i < preFos.Length; i++)
                    {
                        if (preFos[i] == null) continue;
                        preFos[i].campID = pCamp;
                        if (allyTerr != null) preFos[i].pTerritory = allyTerr;
                    }
                }

                PrepareEntityPropertyNodes(go, hireId, isFallback);
                ForceInitializeAllLaunchNodes(go);
                if (ent != null)
                {
                    ConfigureEntityAsPlayerAlly(ent, player, allyTerr);
                    EnsureAllyActivatedAndAlive(go, ent, true, boundUnitCfg);
                    CFightObj spawnedFo = ent.fightObj as CFightObj;
                    if (spawnedFo != null && (spawnedFo.GetCurHP() <= 0 || !spawnedFo.IsActive()))
                    {
                        if (!spawnedFo.IsActive()) spawnedFo.Respawn();
                        else spawnedFo.SetHPToMax();
                        spawnedFo.SysncHP();
                    }
                    ModForceAllyFollower flw = go.GetComponent<ModForceAllyFollower>();
                    if (flw == null) flw = go.AddComponent<ModForceAllyFollower>();
                    flw.owner = player;
                    flw.allyTerritory = allyTerr;
                    flw.unitId = hireId;
                    flw.isAvatarFallback = isFallback;
                    flw.isStoryHire = true;
                    flw.isTamedLive = false;
                    _modHireSpawned[hireId] = ent;

                    if (!_loggedStoryHireIds.ContainsKey(hireId))
                    {
                        _loggedStoryHireIds[hireId] = true;
                        CAIFSM fsm = go.GetComponent<CAIFSM>();
                        CFSMPart fp = ent.FSMPart;
                        int cHp = (spawnedFo != null) ? spawnedFo.GetCurHP() : -1;
                        int mHp = (spawnedFo != null) ? spawnedFo.GetMaxHP() : -1;
                        bool iDead = (spawnedFo != null) && spawnedFo.IsDead();
                        int deadSt = (spawnedFo != null) ? spawnedFo.nDeadStateID : 0;
                        bool foAct = (spawnedFo != null) && spawnedFo.IsActive();
                        bool aiAct = (fsm != null && s_fiAiFsmActive != null) ? ((bool)s_fiAiFsmActive.GetValue(fsm)) : false;
                        int opActive = 0;
                        try
                        {
                            COperatePoint[] opsA = go.GetComponentsInChildren<COperatePoint>(true);
                            if (opsA != null)
                            {
                                for (int k = 0; k < opsA.Length; k++)
                                    if (opsA[k] != null && opsA[k].gameObject.activeInHierarchy) opActive++;
                            }
                        }
                        catch { }
                        Debug.Log("[ModBridge] StoryHire spawned #" + hireId + " raw='" + tplStr + "' tpl=" + tplGo.name +
                                  " cfg=" + ent.nConfigID + " av=" + hasValidAv + " fb=" + isFallback +
                                  " hp=" + cHp + "/" + mHp + " dead=" + iDead + " foAct=" + foAct +
                                  " fsmSt=" + (fp != null ? fp.GetCurrentStateID() : -1) + "/d" + deadSt +
                                  " opsAct=" + opActive + "/" + opBefore + " ai=" + (fsm != null ? fsm.GetType().Name : "none") + "(" + aiAct + ")");
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModBridge] TrySpawnModHire ex: " + ex.Message);
                return false;
            }
        }

        private void MaintainStoryHires(IGameSystem gs, CEntity player, bool immediate)
        {
            if (gs == null || gs.hireSystem == null || player == null || _sceneTransitioning) return;
            if (!immediate && (_tick % 24u) != 8u) return;

            List<CHireSystemData> activeHires = gs.hireSystem.GetAllHireData();
            Dictionary<int, bool> activeSet = new Dictionary<int, bool>();
            if (activeHires != null)
            {
                for (int i = 0; i < activeHires.Count; i++)
                {
                    if (activeHires[i] != null) activeSet[activeHires[i].nID] = true;
                }
            }

            Dictionary<int, GameObject> liveNative = new Dictionary<int, GameObject>();
            CollectNativeHireSpawnerNodes(liveNative);
            foreach (KeyValuePair<int, GameObject> nkv in liveNative)
            {
                if (nkv.Value != null)
                {
                    EnsureDogSafeComponents(nkv.Value, nkv.Value.GetComponent<CEntity>());
                }
            }

            if (_modHireSpawned.Count > 0)
            {
                List<int> modKeys = new List<int>(_modHireSpawned.Keys);
                for (int i = 0; i < modKeys.Count; i++)
                {
                    int hid = modKeys[i];
                    if (!activeSet.ContainsKey(hid) || liveNative.ContainsKey(hid))
                    {
                        DespawnModHire(hid);
                    }
                }
            }

            if (activeHires == null || activeHires.Count == 0) return;
            for (int i = 0; i < activeHires.Count; i++)
            {
                CHireSystemData hd = activeHires[i];
                if (hd == null) continue;
                int hid = hd.nID;
                if (liveNative.ContainsKey(hid)) continue;

                CEntity curEnt;
                bool hasLive = _modHireSpawned.TryGetValue(hid, out curEnt) && curEnt != null && curEnt.gameObject != null
                               && (curEnt.fightObj == null || !curEnt.fightObj.IsDead());
                if (hasLive) continue;
                if (!immediate && (_tick % 48u) != 8u) continue;
                TrySpawnModHire(gs, player, hd, i);
            }
        }

        private unsafe void MaintainForcedHires(byte* b, IGameSystem gs, int cid, CEntity player)
        {
            if (b == null || gs == null || player == null || _sceneTransitioning) return;
            string dir = ReadShmDynDir(b);
            if (!string.IsNullOrEmpty(dir) && !_forceHireLoaded)
            {
                _forceHireLoaded = true;
                try
                {
                    string fpath = dir + "/mod_force_hires.txt";
                    if (File.Exists(fpath))
                    {
                        string[] parts = File.ReadAllText(fpath, s_utf8NoBom).Split(',');
                        for (int i = 0; i < parts.Length; i++)
                        {
                            int uid;
                            if (int.TryParse(parts[i].Trim(), out uid) && uid > 0 && !_forcedHireIds.Contains(uid))
                                _forcedHireIds.Add(uid);
                        }
                    }
                }
                catch { }
            }

            int curScene = (CGameSceneMgr.Instance != null) ? CGameSceneMgr.Instance.nSceneID : -1;
            if (curScene != _lastForceSceneId)
            {
                _lastForceSceneId = curScene;
                DespawnAllForcedAllies(true);
            }
            if (_forcedHireIds.Count == 0) return;
            if (_allyTerritoryObj != null && player.transform != null)
            {
                _allyTerritoryObj.transform.position = player.transform.position;
            }
            if ((_tick % 12u) != 4u) return;
            for (int i = 0; i < _forcedHireIds.Count; i++)
            {
                int uid = _forcedHireIds[i];
                CEntity curEnt;
                bool hasLive = _forcedSpawned.TryGetValue(uid, out curEnt) && curEnt != null && curEnt.gameObject != null
                               && curEnt.fightObj != null && !curEnt.fightObj.IsDead();
                if (hasLive)
                {
                    ModForceAllyFollower flw = curEnt.gameObject.GetComponent<ModForceAllyFollower>();
                    bool canUpgrade = (flw != null && flw.isAvatarFallback && CanUpgradeFallbackUnit(uid));
                    if (!canUpgrade) continue;
                }
                else if ((_tick % 48u) != 4u)
                {
                    continue;
                }
                TrySpawnForcedAlly(gs, player, uid, i);
            }
        }

        private unsafe void SaveForcedHireIds()
        {
            try
            {
                string dir = ReadShmDynDir(Shm());
                if (string.IsNullOrEmpty(dir)) return;
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < _forcedHireIds.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(_forcedHireIds[i]);
                }
                WriteAtomicText(dir + "/mod_force_hires.txt", sb.ToString());
            }
            catch { }
        }

        private bool ForceTameEntity(IGameSystem gs, CEntity player, int configId, ulong entityUid)
        {
            if (player == null) return false;
            List<CEntity> allEnts = CollectSceneEntities(true);
            CTerritory terr = GetOrCreateAllyTerritory(player);
            int tamed = 0;
            for (int i = 0; i < allEnts.Count; i++)
            {
                CEntity ent = allEnts[i];
                if (ent == null || ent == player || ent.fightObj == null || ent.fightObj.IsDead()) continue;
                bool match = (entityUid != 0UL) ? (ent.UID == entityUid) : (configId > 0 && ent.nConfigID == configId);
                if (!match) continue;
                CFightObj origFo = ent.fightObj as CFightObj;
                CAIData aid = (ent.gameObject != null) ? ent.gameObject.GetComponent<CAIData>() : null;
                if (origFo != null && !_tamedEntities.ContainsKey(ent.UID))
                {
                    TamedBackup bk = new TamedBackup();
                    bk.ent = ent;
                    bk.origCampId = origFo.CampID;
                    bk.origTerritory = origFo.territory;
                    bk.origSearchR = origFo.target_searchR;
                    bk.origSearchRFight = origFo.target_searchR_Fight;
                    bk.origDicCamp = (origFo.dicCamp != null) ? new Dictionary<int, CFightObj.sCampNode>(origFo.dicCamp) : null;
                    bk.origActive = ent.gameObject.activeSelf;
                    if (aid != null)
                    {
                        bk.hadAiData = true;
                        bk.origPartrolType = aid.Partrol_eType;
                        bk.origQuitPos = aid.QuitFight_v3TargetPos;
                        bk.origPauseByGrid = aid.Partrol_bPauseBySceneGrid;
                    }
                    bk.disabledActivators = new List<MonoBehaviour>();
                    MonoBehaviour[] acts = ent.gameObject.GetComponentsInChildren<MonoBehaviour>(true);
                    if (acts != null)
                    {
                        for (int a = 0; a < acts.Length; a++)
                        {
                            MonoBehaviour mb = acts[a];
                            if (mb == null) continue;
                            if ((mb is CActivator_SceneGrid || mb is CActivator_SceneGridAndFightState) && mb.enabled)
                            {
                                mb.enabled = false;
                                bk.disabledActivators.Add(mb);
                            }
                        }
                    }
                    _tamedEntities[ent.UID] = bk;
                }
                if (!ent.gameObject.activeSelf)
                {
                    ent.gameObject.SetActive(true);
                    if (CGameSceneMgr.Instance != null)
                    {
                        try { CGameSceneMgr.Instance.ForceInitializeLaunchNode(ent.gameObject); } catch { }
                        try { CGameSceneMgr.Instance.AddEntity(ent); } catch { }
                    }
                }
                if (aid != null)
                {
                    aid.Partrol_bPauseBySceneGrid = false;
                    aid.QuitFight_v3TargetPos = player.transform.position;
                }
                ConfigureEntityAsPlayerAlly(ent, player, terr);
                try
                {
                    ModForceAllyFollower flw = ent.gameObject.GetComponent<ModForceAllyFollower>();
                    if (flw == null) flw = ent.gameObject.AddComponent<ModForceAllyFollower>();
                    flw.owner = player;
                    flw.allyTerritory = terr;
                    flw.unitId = ent.nConfigID;
                    flw.isAvatarFallback = false;
                    flw.isStoryHire = false;
                    flw.isTamedLive = true;
                }
                catch { }
                tamed++;
                if (entityUid != 0UL) break;
            }
            return tamed > 0;
        }

        private bool UntameEntity(CEntity player, int configId, ulong entityUid)
        {
            List<CEntity> allEnts = CollectSceneEntities(true);
            Dictionary<ulong, CEntity> byUid = new Dictionary<ulong, CEntity>(allEnts.Count);
            for (int i = 0; i < allEnts.Count; i++)
            {
                if (allEnts[i] != null && allEnts[i].UID != 0UL)
                    byUid[allEnts[i].UID] = allEnts[i];
            }
            List<ulong> targets = new List<ulong>();
            foreach (KeyValuePair<ulong, TamedBackup> kv in _tamedEntities)
            {
                if (entityUid != 0UL)
                {
                    if (kv.Key == entityUid) { targets.Add(kv.Key); break; }
                }
                else if (configId > 0 && kv.Value != null && kv.Value.ent != null && kv.Value.ent.nConfigID == configId)
                {
                    targets.Add(kv.Key);
                }
            }
            if (targets.Count == 0 && entityUid != 0UL && byUid.ContainsKey(entityUid))
            {
                targets.Add(entityUid);
            }
            int untamed = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                ulong uid = targets[i];
                TamedBackup bk = null;
                _tamedEntities.TryGetValue(uid, out bk);
                _tamedEntities.Remove(uid);
                CEntity ent = null;
                byUid.TryGetValue(uid, out ent);
                if (ent == null && bk != null) ent = bk.ent;
                if (ent == null || ent.gameObject == null) continue;
                try
                {
                    ModForceAllyFollower flw = ent.gameObject.GetComponent<ModForceAllyFollower>();
                    if (flw != null) UnityEngine.Object.Destroy(flw);
                }
                catch { }
                try
                {
                    CFightObj fo = ent.fightObj as CFightObj;
                    if (fo != null)
                    {
                        fo.RemoveCurTarget();
                        if (bk != null)
                        {
                            fo.campID = bk.origCampId;
                            fo.territory = bk.origTerritory;
                            fo.target_searchR = bk.origSearchR;
                            fo.target_searchR_Fight = bk.origSearchRFight;
                            if (fo.dicCamp != null && bk.origDicCamp != null)
                            {
                                fo.dicCamp.Clear();
                                foreach (KeyValuePair<int, CFightObj.sCampNode> ckv in bk.origDicCamp)
                                    fo.dicCamp[ckv.Key] = ckv.Value;
                            }
                        }
                        else
                        {
                            fo.campID = 1916;
                        }
                    }
                    CAIData aid = ent.gameObject.GetComponent<CAIData>();
                    if (aid != null)
                    {
                        if (bk != null && bk.hadAiData)
                        {
                            aid.Partrol_eType = bk.origPartrolType;
                            aid.Partrol_bPauseBySceneGrid = bk.origPauseByGrid;
                            aid.QuitFight_v3TargetPos = ent.transform.position;
                        }
                        else
                        {
                            aid.QuitFight_v3TargetPos = ent.transform.position;
                        }
                    }
                    if (bk != null && bk.disabledActivators != null)
                    {
                        for (int a = 0; a < bk.disabledActivators.Count; a++)
                        {
                            if (bk.disabledActivators[a] != null) bk.disabledActivators[a].enabled = true;
                        }
                    }
                }
                catch { }
                untamed++;
            }
            return untamed > 0;
        }

        internal static SortedDictionary<ulong, CEntity> GetSceneEntitiesDictStatic()
        {
            CGameSceneMgr mgr = CGameSceneMgr.Instance;
            if (mgr == null) return null;
            if (s_fiSceneEntities == null)
                s_fiSceneEntities = typeof(CGameSceneMgr).GetField("m_dicEntity", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return (s_fiSceneEntities != null) ? (s_fiSceneEntities.GetValue(mgr) as SortedDictionary<ulong, CEntity>) : null;
        }

        internal static int ClassifyHostileStatic(CEntity player, CFightObj pFo, int pCamp, CEntity ent, bool includeNeutral)
        {
            return ClassifyHostile(player, pFo, pCamp, ent, includeNeutral);
        }

        private unsafe int CmdHireOp(IGameSystem gs, int cid, CEntity player, int id, int hireOp, int subArg, ulong entityUid)
        {
            // hireOp:
            // 0 = Add Hire (CHireSystem), 1 = Remove Hire
            // 2 = Add Worker/Teamate (subArg=1 also squad), 3 = Remove Worker/Teamate (subArg=1 squad only)
            // 4 = Force Hire ANY Unit.csv ID, 5 = Remove Force Hire
            // 6 = Force Tame live scene entity by entityUid or configId, 7 = Clear all forced hires
            // 8 = Set tame scan radius (m), 9 = Untame live scene entity
            if (hireOp == 0)
            {
                if (gs.hireSystem == null || id <= 0) { SetStatus(-2, "HireSystem null"); return -2; }
                try
                {
                    IGameDataCenter dc = CClientGlobal.Instance != null ? CClientGlobal.Instance.GetGameDataCenter : null;
                    CHireConfigLoader hl = (dc != null) ? (dc.GetConfigLoader(eGameConfigType.Hire) as CHireConfigLoader) : null;
                    sHireConfig hc = (hl != null) ? hl.GetConfig(id) : null;
                    if (hc != null && !string.IsNullOrEmpty(hc.szPrefabTemplate) && CPrefabTemplatePoolMgr.Instance != null)
                    {
                        sPrefabTemplateInfo pInfo;
                        string rawTpl = hc.szPrefabTemplate;
                        string trTpl = rawTpl.Trim();
                        if (CPrefabTemplatePoolMgr.Instance.GetPrefabTemplate(rawTpl, out pInfo) && pInfo.pTemplateGO != null)
                            EnsureDogSafeComponents(pInfo.pTemplateGO, pInfo.pTemplateGO.GetComponent<CEntity>());
                        else if (CPrefabTemplatePoolMgr.Instance.GetPrefabTemplate(trTpl, out pInfo) && pInfo.pTemplateGO != null)
                            EnsureDogSafeComponents(pInfo.pTemplateGO, pInfo.pTemplateGO.GetComponent<CEntity>());
                    }
                }
                catch { }
                bool ok = false;
                try { ok = gs.hireSystem.AddHire(id); } catch (Exception ex) { Debug.LogWarning("[ModBridge] AddHire ex: " + ex.Message); }
                try { TriggerSceneSpawnersUpdate(); } catch { }
                try { MaintainStoryHires(gs, player, true); } catch (Exception ex) { Debug.LogWarning("[ModBridge] MaintainStoryHires ex: " + ex.Message); }
                ExportDynHireState(gs, cid, true);
                SetStatus(1, ok ? ("Hired #" + id) : ("Synced hire #" + id));
                return 1;
            }
            if (hireOp == 1)
            {
                if (gs.hireSystem == null || id <= 0) return -2;
                gs.hireSystem.RemoveHire(id);
                DespawnModHire(id);
                TriggerSceneSpawnersUpdate();
                ExportDynHireState(gs, cid, true);
                SetStatus(1, "Dismissed hire #" + id);
                return 1;
            }
            if (hireOp == 2)
            {
                if (gs.workerSystem == null || id <= 0) return -2;
                InvokeNamedMethod(gs.workerSystem, "AddWorker", id);
                object w = InvokeNamedMethod(gs.workerSystem, "GetWorker", id);
                if (w != null) InvokeNamedMethod(w, "SetPayState", true);
                if (subArg != 0 && gs.characterSystem != null)
                {
                    EnsureWorkerArmed(gs, id);
                    object cdata = InvokeNamedMethod(gs.characterSystem, "GetCharacterSystemData", cid);
                    if (cdata != null)
                    {
                        InvokeNamedMethod(cdata, "AddTemateNormal", id);
                        InvokeNamedMethod(cdata, "AddTemateCapture", id);
                    }
                    TriggerSceneSpawnersUpdate();
                }
                ExportDynHireState(gs, cid, true);
                SetStatus(1, (subArg != 0 ? "Deployed teammate #" : "Added worker #") + id);
                return 1;
            }
            if (hireOp == 3)
            {
                if (gs.workerSystem == null || id <= 0) return -2;
                if (gs.characterSystem != null)
                {
                    object cdata = InvokeNamedMethod(gs.characterSystem, "GetCharacterSystemData", cid);
                    if (cdata != null)
                    {
                        InvokeNamedMethod(cdata, "RemoveTeamateNormal", id);
                        InvokeNamedMethod(cdata, "RemoveTeamateCapture", id);
                    }
                }
                if (subArg == 0)
                {
                    InvokeNamedMethod(gs.workerSystem, "RemoveWorker", id);
                }
                TriggerSceneSpawnersUpdate();
                ExportDynHireState(gs, cid, true);
                SetStatus(1, (subArg == 0 ? "Removed worker #" : "Recalled teammate #") + id);
                return 1;
            }
            if (hireOp == 4)
            {
                if (id <= 0) return -2;
                if (CGameSceneMgr.Instance != null) _lastForceSceneId = CGameSceneMgr.Instance.nSceneID;
                if (!_forcedHireIds.Contains(id))
                {
                    _forcedHireIds.Add(id);
                    SaveForcedHireIds();
                }
                bool ok = TrySpawnForcedAlly(gs, player, id, _forcedHireIds.Count);
                ExportDynHireState(gs, cid, true);
                SetStatus(ok ? 1 : -1, ok ? ("Force-hired #" + id) : ("Saved force-hire #" + id + " (spawns next frame/map)"));
                return 1;
            }
            if (hireOp == 5)
            {
                if (id <= 0) return -2;
                _forcedHireIds.Remove(id);
                DespawnForcedAlly(id);
                SaveForcedHireIds();
                ExportDynHireState(gs, cid, true);
                SetStatus(1, "Removed force-hire #" + id);
                return 1;
            }
            if (hireOp == 6)
            {
                bool ok = ForceTameEntity(gs, player, id, entityUid);
                ExportDynHireState(gs, cid, true);
                SetStatus(ok ? 1 : -2, ok ? "Converted target to ally" : "Target not found");
                return ok ? 1 : -2;
            }
            if (hireOp == 7)
            {
                for (int i = 0; i < _forcedHireIds.Count; i++)
                    DespawnForcedAlly(_forcedHireIds[i]);
                _forcedHireIds.Clear();
                SaveForcedHireIds();
                ExportDynHireState(gs, cid, true);
                SetStatus(1, "Cleared all force-hires");
                return 1;
            }
            if (hireOp == 8)
            {
                _tameScanRadiusM = Mathf.Clamp((float)id, 5f, 999f);
                ExportDynHireState(gs, cid, true);
                SetStatus(1, "Tame radius " + ((int)_tameScanRadiusM) + "m");
                return 1;
            }
            if (hireOp == 9)
            {
                bool ok = UntameEntity(player, id, entityUid);
                ExportDynHireState(gs, cid, true);
                SetStatus(ok ? 1 : -2, ok ? "Restored target to neutral/enemy" : "Tamed target not found");
                return ok ? 1 : -2;
            }
            return -1;
        }

        // ==================== #22 Fixed UserID=7891 Login & Universal Save Compatibility ====================
        private const long MOD_FIXED_UID = 7891L;

        private sealed class PreLoginForwardHandler : INetworkHandler
        {
            private readonly ModTicker _owner;
            private readonly ulong _bridgePtr;
            private readonly uint _seq;
            private readonly int _cmd;
            private readonly bool _is7891;

            public PreLoginForwardHandler(ModTicker owner, ulong bridgePtr, uint seq, int cmd, bool is7891)
            {
                _owner = owner;
                _bridgePtr = bridgePtr;
                _seq = seq;
                _cmd = cmd;
                _is7891 = is7891;
            }

            public void OnHttpRequestSucc(int nCommand, byte[] resStream, byte[] reqStream)
            {
                byte[] outBytes = resStream ?? new byte[0];
                try
                {
                    if (_cmd == 216 || _cmd == 116 || _cmd == 111 || _cmd == 101 || _cmd == 3 || _cmd == 203)
                    {
                        if (outBytes.Length >= 8)
                        {
                            long srvUid = BitConverter.ToInt64(outBytes, 0);
                            if (srvUid > 0L && srvUid != MOD_FIXED_UID)
                                _owner._origLoginUid = srvUid;

                            if (_is7891)
                            {
                                byte[] clone = new byte[outBytes.Length];
                                Buffer.BlockCopy(outBytes, 0, clone, 0, outBytes.Length);
                                byte[] uidBytes = BitConverter.GetBytes(MOD_FIXED_UID);
                                Buffer.BlockCopy(uidBytes, 0, clone, 0, 8);
                                outBytes = clone;
                                _owner._loginStateMsg = "HTTP 登录#" + _cmd + " 已重定向 UID=7891 (原账号=" + srvUid + ")";
                                _owner.EnsureSaveCompatForUser(MOD_FIXED_UID, false);
                            }
                            else
                            {
                                _owner.EnsureSaveCompatForUser(srvUid, false);
                            }
                        }
                    }
                    else if (_cmd == 12 && _is7891)
                    {
                        _owner.EnsureSaveCompatForUser(MOD_FIXED_UID, false);
                    }
                }
                catch { }
                _owner._preHttpDoneSeq = _seq;
                _owner._preHttpBusySeq = 0u;
                WriteHttpBridgeResponse(_bridgePtr, _seq, 200, true, outBytes);
            }

            public void OnHttpRequestFail(int nCommand, uint nErrorCode, string szMsg, byte[] reqStream, byte[] resStream)
            {
                byte[] syn = null;
                if (_is7891 && _owner.TryBuildSynthetic7891Response(_cmd, out syn))
                {
                    _owner._loginStateMsg = "服务器拒绝#" + _cmd + "(" + (szMsg ?? "") + ")，已本地放行 UID=7891";
                    _owner._preHttpDoneSeq = _seq;
                    _owner._preHttpBusySeq = 0u;
                    WriteHttpBridgeResponse(_bridgePtr, _seq, 200, true, syn);
                    return;
                }
                try
                {
                    DNetwork.sNetwork_Error_Res err = new DNetwork.sNetwork_Error_Res();
                    err.nCode = nErrorCode;
                    err.szMsg = szMsg ?? string.Empty;
                    byte[] ebytes = CStreamUtil.StructToBytes(err);
                    byte[] body = resStream ?? new byte[0];
                    byte[] comb = new byte[ebytes.Length + body.Length];
                    Buffer.BlockCopy(ebytes, 0, comb, 0, ebytes.Length);
                    if (body.Length > 0) Buffer.BlockCopy(body, 0, comb, ebytes.Length, body.Length);
                    _owner._preHttpDoneSeq = _seq;
                    _owner._preHttpBusySeq = 0u;
                    WriteHttpBridgeResponse(_bridgePtr, _seq, 200, false, comb);
                }
                catch
                {
                    _owner._preHttpDoneSeq = _seq;
                    _owner._preHttpBusySeq = 0u;
                    WriteHttpBridgeResponse(_bridgePtr, _seq, 502, false, null);
                }
            }

            public void OnHttpRequestError(int nCommand, string szMsg, byte[] reqStream)
            {
                byte[] syn = null;
                if (_is7891 && _owner.TryBuildSynthetic7891Response(_cmd, out syn))
                {
                    _owner._loginStateMsg = "离线/网络异常#" + _cmd + "，已本地放行 UID=7891";
                    _owner._preHttpDoneSeq = _seq;
                    _owner._preHttpBusySeq = 0u;
                    WriteHttpBridgeResponse(_bridgePtr, _seq, 200, true, syn);
                    return;
                }
                _owner._preHttpDoneSeq = _seq;
                _owner._preHttpBusySeq = 0u;
                WriteHttpBridgeResponse(_bridgePtr, _seq, 502, false, null);
            }
        }

        private bool TryBuildSynthetic7891Response(int cmd, out byte[] payload)
        {
            payload = null;
            try
            {
                long nowUnix = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                if (cmd == 216)
                {
                    HotfixDNetWork.sNetwork_LoginV4_Res r = default(HotfixDNetWork.sNetwork_LoginV4_Res);
                    r.nUserID = MOD_FIXED_UID;
                    r.nTimeStamp = nowUnix;
                    r.nCreateUserTime = nowUnix;
                    payload = CStreamUtil.StructToBytes((object)r);
                    EnsureSaveCompatForUser(MOD_FIXED_UID, false);
                    return true;
                }
                if (cmd == 116)
                {
                    DNetwork.sNetwork_LoginV3_Res r = new DNetwork.sNetwork_LoginV3_Res();
                    r.nUserID = MOD_FIXED_UID;
                    r.nTimeStamp = nowUnix;
                    r.nCreateUserTime = nowUnix;
                    payload = CStreamUtil.StructToBytes(r);
                    EnsureSaveCompatForUser(MOD_FIXED_UID, false);
                    return true;
                }
                if (cmd == 203)
                {
                    HotfixDNetWork.sNetwork_NetLogin_Res r = default(HotfixDNetWork.sNetwork_NetLogin_Res);
                    r.lUserID = MOD_FIXED_UID;
                    r.lTimeStamp = nowUnix;
                    r.lCreateTime = nowUnix;
                    payload = CStreamUtil.StructToBytes((object)r);
                    EnsureSaveCompatForUser(MOD_FIXED_UID, false);
                    return true;
                }
                if (cmd == 37)
                {
                    DNetwork.sNetwork_RequestGameCurrency_Res r = new DNetwork.sNetwork_RequestGameCurrency_Res();
                    payload = CStreamUtil.StructToBytes(r);
                    return true;
                }
                if (cmd == 12)
                {
                    DNetwork.sNetwork_DownloadDB_Res r = new DNetwork.sNetwork_DownloadDB_Res();
                    r.bOwn = false;
                    r.szMD5 = string.Empty;
                    r.szSignMD5 = CMD5Util.MD5(new List<string> { r.bOwn.ToString(), r.szMD5.ToString(), "3j5k2b1n7m8s4f6d5e3r2t1z" });
                    payload = CStreamUtil.StructToBytes(r);
                    EnsureSaveCompatForUser(MOD_FIXED_UID, false);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModBridge] TryBuildSynthetic7891Response ex: " + ex.Message);
            }
            return false;
        }

        private unsafe void SyncAllNetworkUrlsIncludingLaunch(byte* b)
        {
            if (b == null || CNetworkMgr.Instance == null) return;
            if (_tick != _lastLaunchSyncTick && (_tick % 20u) != 1u && _cachedLoginScene == null) return;
            _lastLaunchSyncTick = _tick;
            try
            {
                ulong bridgePtr = *(ulong*)(b + 0x38);
                if (bridgePtr == 0UL) return;
                uint port = *(uint*)bridgePtr;
                if (port == 0u) return;

                CNetworkMgr nm = CNetworkMgr.Instance;
                FieldInfo fiDic = typeof(CNetworkMgr).GetField("m_dicUrl", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                Dictionary<DNetwork.eClientRequireUrl, string> dic = (fiDic != null) ? (fiDic.GetValue(nm) as Dictionary<DNetwork.eClientRequireUrl, string>) : null;
                if (dic == null) return;

                // Redirect GameServer / DBServer right away even on login screen
                string localGs = "http://127.0.0.1:" + port + "/gs";
                string localDb = "http://127.0.0.1:" + port + "/db";
                string localLs = "http://127.0.0.1:" + port + "/ls";

                string curGs = dic.ContainsKey(DNetwork.eClientRequireUrl.GameServer) ? dic[DNetwork.eClientRequireUrl.GameServer] : PlayerPrefs.GetString("GameServer", "");
                if (!string.IsNullOrEmpty(curGs) && !curGs.Contains("127.0.0.1"))
                {
                    _realGameUrl1 = curGs;
                    nm.SetUrl((DNetwork.eClientRequireUrl)99, _realGameUrl1);
                    nm.SetUrl(DNetwork.eClientRequireUrl.GameServer, localGs);
                }

                string curDb = dic.ContainsKey(DNetwork.eClientRequireUrl.DBServer) ? dic[DNetwork.eClientRequireUrl.DBServer] : PlayerPrefs.GetString("DBServer", "");
                if (!string.IsNullOrEmpty(curDb) && !curDb.Contains("127.0.0.1"))
                {
                    _realDbUrl2 = curDb;
                    nm.SetUrl((DNetwork.eClientRequireUrl)98, _realDbUrl2);
                    nm.SetUrl(DNetwork.eClientRequireUrl.DBServer, localDb);
                }

                // Redirect LaunchServer once CLoginScene is loaded (after CLaunchScene hotfix check finishes)
                if (_cachedLoginScene != null)
                {
                    string curLs = dic.ContainsKey(DNetwork.eClientRequireUrl.LaunchServer) ? dic[DNetwork.eClientRequireUrl.LaunchServer] : "";
                    if (!string.IsNullOrEmpty(curLs) && !curLs.Contains("127.0.0.1"))
                    {
                        _realLaunchUrl3 = curLs;
                        nm.SetUrl((DNetwork.eClientRequireUrl)97, _realLaunchUrl3);
                        nm.SetUrl(DNetwork.eClientRequireUrl.LaunchServer, localLs);
                    }
                }
            }
            catch { }
        }

        private unsafe bool PreHandleLoginOrLaunchHttp(byte* b, uint flags, IGameSystem gs)
        {
            if (b == null) return false;
            try
            {
                ulong bridgePtr = *(ulong*)(b + 0x38);
                if (bridgePtr == 0UL) return false;
                byte* hb = (byte*)bridgePtr;
                uint state = *(uint*)(hb + 0x04);
                if (state != 1u)
                {
                    if (state == 0u) _preHttpBusySeq = 0u;
                    return false;
                }
                uint seq = *(uint*)(hb + 0x08);
                int cmd = *(int*)(hb + 0x0C);
                uint routeTag = *(uint*)(hb + 0x1C);
                bool is7891 = (flags & (1u << 20)) != 0u;

                if (_preHttpDoneSeq == seq) return true;
                if (_preHttpBusySeq == seq) return true;

                bool intercept = (routeTag == 3u) ||
                                 (cmd == 216 || cmd == 116 || cmd == 111 || cmd == 101 || cmd == 3 || cmd == 203) ||
                                 (is7891 && (cmd == 37 || cmd == 12));
                if (!intercept) return false;

                _preHttpBusySeq = seq;
                uint reqLen = *(uint*)(hb + 0x14);
                int hdrSize = CStreamUtil.GetBufferSize(typeof(DNetwork.sNetwork_Head_Req));
                byte[] innerReq = new byte[0];
                if ((int)reqLen > hdrSize)
                {
                    int innerLen = (int)reqLen - hdrSize;
                    innerReq = new byte[innerLen];
                    for (int i = 0; i < innerLen; i++) innerReq[i] = hb[0x20 + hdrSize + i];
                }

                string targetUrl = (routeTag == 2u) ? _realDbUrl2 : ((routeTag == 3u) ? _realLaunchUrl3 : _realGameUrl1);
                DNetwork.eClientRequireUrl fwdRoute = (routeTag == 2u) ? (DNetwork.eClientRequireUrl)98 : ((routeTag == 3u) ? (DNetwork.eClientRequireUrl)97 : (DNetwork.eClientRequireUrl)99);
                if (string.IsNullOrEmpty(targetUrl) || CNetworkMgr.Instance == null)
                {
                    byte[] syn = null;
                    if (is7891 && TryBuildSynthetic7891Response(cmd, out syn))
                    {
                        _loginStateMsg = "无服务器URL#" + cmd + "，已本地放行 UID=7891";
                        _preHttpDoneSeq = seq;
                        _preHttpBusySeq = 0u;
                        WriteHttpBridgeResponse(bridgePtr, seq, 200, true, syn);
                        return true;
                    }
                    _preHttpBusySeq = 0u;
                    return false;
                }

                // If 7891 is active and DownLoadDB (cmd=12) was triggered before local compat finished, sync now!
                if (cmd == 12 && is7891) EnsureSaveCompatForUser(MOD_FIXED_UID, false);

                CNetworkMgr.Instance.SendRequest(new PreLoginForwardHandler(this, bridgePtr, seq, cmd, is7891), fwdRoute, cmd, innerReq);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModBridge] PreHandleLoginOrLaunchHttp ex: " + ex.Message);
                _preHttpBusySeq = 0u;
                return false;
            }
        }

        private unsafe void MaintainLogin7891AndSaveCompat(byte* b, uint flags, IGameSystem gs, bool sceneReady)
        {
            bool is7891 = (flags & (1u << 20)) != 0u;
            bool toggled = (is7891 != _prevLogin7891Flag);
            _prevLogin7891Flag = is7891;

            try
            {
                CAccountMgr acc = CAccountMgr.Instance;
                if (acc != null)
                {
                    long curUid = acc.GetUserID();
                    string curTok = acc.GetLoginToken();
                    if (curUid != 0L && curUid != MOD_FIXED_UID) _origLoginUid = curUid;
                    if (!string.IsNullOrEmpty(curTok)) _origLoginToken = curTok;

                    if (toggled)
                    {
                        if (is7891)
                        {
                            ApplyActiveUserId7891(true);
                            EnsureSaveCompatForUser(MOD_FIXED_UID, false);
                        }
                        else if (_origLoginUid > 0L)
                        {
                            acc.SetUserID(_origLoginUid);
                            SetDataBridgeUserId(_origLoginUid, _origLoginToken);
                            EnsureSaveCompatForUser(_origLoginUid, false);
                            _loginStateMsg = "已恢复原账号 UID=" + _origLoginUid;
                        }
                        ExportDynLoginState(b, true);
                    }
                    else if (is7891 && curUid != 0L && curUid != MOD_FIXED_UID)
                    {
                        ApplyActiveUserId7891(false);
                        EnsureSaveCompatForUser(MOD_FIXED_UID, false);
                    }
                }

                if (!sceneReady && (_tick % 10u) == 0u)
                {
                    CLoginScene ls = UnityEngine.Object.FindObjectOfType(typeof(CLoginScene)) as CLoginScene;
                    _cachedLoginScene = ls;
                    if (ls != null)
                    {
                        SyncAllNetworkUrlsIncludingLaunch(b);
                        BindOnline7891Button(ls, is7891);
                        if (acc != null && acc.GetUserID() != 0L)
                        {
                            EnsureSaveCompatForUser(is7891 ? MOD_FIXED_UID : acc.GetUserID(), false);
                        }
                    }
                }
                else if (sceneReady)
                {
                    _cachedLoginScene = null;
                }

                if (is7891 && _cachedLoginScene != null)
                {
                    Tick7891BanDialogSuppress(_cachedLoginScene);
                }
            }
            catch { }
        }

        private void ApplyActiveUserId7891(bool updateMsg)
        {
            try
            {
                CAccountMgr acc = CAccountMgr.Instance;
                if (acc != null) acc.SetUserID(MOD_FIXED_UID);
                string tok = !string.IsNullOrEmpty(_origLoginToken) ? _origLoginToken : "mod_7891_token";
                SetDataBridgeUserId(MOD_FIXED_UID, tok);
                if (updateMsg) _loginStateMsg = "已就绪";
            }
            catch { }
        }

        private static Type FindNamedType(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            try
            {
                Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < asms.Length; i++)
                {
                    Type t = asms[i].GetType(name, false);
                    if (t != null) return t;
                }
            }
            catch { }
            return null;
        }

        private static object GetStaticInstance(Type t)
        {
            if (t == null) return null;
            try
            {
                BindingFlags bf = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                PropertyInfo pi = t.GetProperty("Instance", bf);
                if (pi != null) return pi.GetValue(null, null);
                FieldInfo fi = t.GetField("_Instance", bf) ?? t.GetField("Instance", bf) ?? t.GetField("m_Instance", bf);
                if (fi != null) return fi.GetValue(null);
            }
            catch { }
            return null;
        }

        private static void SetDataBridgeUserId(long uid, string token)
        {
            try
            {
                Type tDb = FindNamedType("DataBridge");
                if (tDb == null) return;
                object db = GetStaticInstance(tDb);
                if (db == null) return;
                FieldInfo fUid = tDb.GetField("m_lUserId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fUid != null) fUid.SetValue(db, uid);
                if (!string.IsNullOrEmpty(token))
                {
                    FieldInfo fTok = tDb.GetField("m_szToken", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (fTok != null)
                    {
                        string old = fTok.GetValue(db) as string;
                        if (string.IsNullOrEmpty(old)) fTok.SetValue(db, token);
                    }
                }
            }
            catch { }
        }

        private void BindOnline7891Button(CLoginScene ls, bool is7891)
        {
            if (ls == null) return;
            try
            {
                FieldInfo fBtn = typeof(CLoginScene).GetField("m_pBt_Online", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fBtn == null) return;
                object btnObj = fBtn.GetValue(ls);
                if (btnObj == null) return;

                PropertyInfo piClick = btnObj.GetType().GetProperty("onClick", BindingFlags.Instance | BindingFlags.Public);
                if (piClick == null) return;
                object evObj = piClick.GetValue(btnObj, null);
                if (evObj == null) return;

                if (btnObj == _boundOnlineBtn && _boundOnlineState == is7891) return;

                MethodInfo miRm = evObj.GetType().GetMethod("RemoveAllListeners", BindingFlags.Instance | BindingFlags.Public);
                MethodInfo miAdd = evObj.GetType().GetMethod("AddListener", BindingFlags.Instance | BindingFlags.Public);
                if (miRm == null || miAdd == null) return;

                miRm.Invoke(evObj, null);
                if (is7891)
                {
                    MethodInfo mi = typeof(ModTicker).GetMethod("OnBypass7891OnlineClicked", BindingFlags.Instance | BindingFlags.NonPublic);
                    Delegate del = Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction), this, mi);
                    miAdd.Invoke(evObj, new object[] { del });
                }
                else
                {
                    MethodInfo mi = typeof(CLoginScene).GetMethod("OnClickOnLineMode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (mi != null)
                    {
                        Delegate del = Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction), ls, mi);
                        miAdd.Invoke(evObj, new object[] { del });
                    }
                }
                _boundOnlineBtn = btnObj;
                _boundOnlineState = is7891;
            }
            catch { }
        }

        private unsafe void OnBypass7891OnlineClicked()
        {
            try
            {
                _suppressBanUntil = Time.realtimeSinceStartup + 5f;
                ApplyActiveUserId7891(true);
                EnsureSaveCompatForUser(MOD_FIXED_UID, false);
                CLoginScene ls = _cachedLoginScene ?? (UnityEngine.Object.FindObjectOfType(typeof(CLoginScene)) as CLoginScene);
                _loginStateMsg = "线上模式已跳过 cmd203 封禁校验，正在以 UID=7891 载入 NetInit";
                ExportDynLoginState(Shm(), true);
                if (ls != null)
                {
                    MethodInfo mi = typeof(CLoginScene).GetMethod("EnterGameWorldOnline", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (mi != null)
                    {
                        mi.Invoke(ls, null);
                        return;
                    }
                }
                UnityEngine.SceneManagement.SceneManager.LoadScene("NetInit");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModBridge] OnBypass7891OnlineClicked ex: " + ex.Message);
            }
        }

        private void Tick7891BanDialogSuppress(CLoginScene ls)
        {
            if (ls == null || Time.realtimeSinceStartup > _suppressBanUntil) return;
            try
            {
                FieldInfo fMsg = typeof(CLoginScene).GetField("m_pPanel_Msg", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                GameObject msgGo = (fMsg != null) ? (fMsg.GetValue(ls) as GameObject) : null;
                if (msgGo != null && msgGo.activeSelf)
                {
                    msgGo.SetActive(false);
                }
            }
            catch { }
        }

        private unsafe void EnsureSaveCompatForUser(long targetUid, bool forceImportNewest)
        {
            if (targetUid <= 0L) return;
            if (!forceImportNewest && _lastSyncedSaveUid == targetUid) return;
            try
            {
                string root = Application.persistentDataPath;
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
                root = root.Replace('\\', '/').TrimEnd('/');

                string targetDir = root + "/db_" + targetUid.ToString();
                string targetDb = targetDir + "/GameWorld.db";
                bool targetExists = File.Exists(targetDb) && new FileInfo(targetDb).Length > 1024L;

                int targetGameSec = 0;
                long targetInnerUid = 0L;
                if (targetExists)
                {
                    ReadDbMeta(targetDb, out targetInnerUid, out targetGameSec);
                }

                // Scan all db_* folders for the most progressed / newest valid GameWorld.db
                string[] dirs = Directory.GetDirectories(root, "db_*");
                string bestOtherDb = null;
                DateTime bestTime = DateTime.MinValue;
                int bestGameSec = -1;

                if (dirs != null)
                {
                    for (int i = 0; i < dirs.Length; i++)
                    {
                        string dNorm = dirs[i].Replace('\\', '/').TrimEnd('/');
                        string folderName = Path.GetFileName(dNorm);
                        if (string.IsNullOrEmpty(folderName) || !folderName.StartsWith("db_")) continue;
                        string subUidStr = folderName.Substring(3);
                        long subUid = 0L;
                        if (!long.TryParse(subUidStr, out subUid) || subUid == targetUid) continue;

                        string cand = dNorm + "/GameWorld.db";
                        if (!File.Exists(cand)) continue;
                        FileInfo fi = new FileInfo(cand);
                        if (fi.Length <= 1024L) continue;

                        long candUidTmp = 0L;
                        int candSec = 0;
                        ReadDbMeta(cand, out candUidTmp, out candSec);
                        bool pick = false;
                        if (bestOtherDb == null) pick = true;
                        else if (candSec > 0 && bestGameSec <= 0) pick = true;
                        else if ((candSec > 0) == (bestGameSec > 0) && fi.LastWriteTimeUtc > bestTime) pick = true;

                        if (pick)
                        {
                            bestOtherDb = cand;
                            bestTime = fi.LastWriteTimeUtc;
                            bestGameSec = candSec;
                        }
                    }
                }

                bool needImport = false;
                if (bestOtherDb != null)
                {
                    if (forceImportNewest) needImport = true;
                    else if (!targetExists) needImport = true;
                    else if (targetGameSec <= 0 && bestGameSec > 0) needImport = true;
                }

                if (needImport && bestOtherDb != null)
                {
                    if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
                    if (targetExists && !File.Exists(targetDb + ".mod_orig.bak"))
                    {
                        try { File.Copy(targetDb, targetDb + ".mod_orig.bak", false); } catch { }
                    }
                    File.Copy(bestOtherDb, targetDb, true);
                    _loginStateMsg = "已将存档 " + Path.GetFileName(Path.GetDirectoryName(bestOtherDb)) + " 兼容导入到 db_" + targetUid;
                    Debug.Log("[ModBridge] SaveCompat copied " + bestOtherDb + " -> " + targetDb);
                }

                if (File.Exists(targetDb) && new FileInfo(targetDb).Length > 1024L)
                {
                    PatchDbFileUserId(targetDb, targetUid);
                }
                _lastSyncedSaveUid = targetUid;
                ExportDynLoginState(Shm(), true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModBridge] EnsureSaveCompatForUser ex: " + ex.Message);
            }
        }

        private static bool IsPlainSqliteFile(string path)
        {
            try
            {
                byte[] hdr = new byte[16];
                using (FileStream fs = File.OpenRead(path))
                {
                    int n = fs.Read(hdr, 0, 16);
                    if (n < 15) return false;
                }
                string magic = Encoding.ASCII.GetString(hdr, 0, 15);
                return magic == "SQLite format 3";
            }
            catch { return false; }
        }

        private static MethodInfo s_miDecodeDb;
        private static MethodInfo s_miEncryptDb;

        private static void InvokeDbCrypto(MethodInfo mi, string path)
        {
            if (mi == null || CDBServer.Instance == null) return;
            mi.Invoke(CDBServer.Instance, new object[] { path });
        }

        private static void ReadDbMeta(string dbPath, out long userId, out int gameTimeSec)
        {
            userId = 0L;
            gameTimeSec = 0;
            string tmp = dbPath + ".mod_readtmp";
            SQLiteDB db = null;
            try
            {
                File.Copy(dbPath, tmp, true);
                if (!IsPlainSqliteFile(tmp))
                {
                    if (s_miDecodeDb == null)
                        s_miDecodeDb = typeof(CDBServer).GetMethod("DecodeFile", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    InvokeDbCrypto(s_miDecodeDb, tmp);
                }
                db = new SQLiteDB();
                db.Open(tmp);
                SQLiteQuery q = CSqliteUtil.GetTableQuery(db, DDBContent.Table_DBData);
                if (q != null)
                {
                    if (q.Step())
                    {
                        int idx1 = 0, idx2 = 0;
                        if (q.IsFieldExist(DDBContent.Field_DBData_UserID, out idx1)) userId = q.GetLong(idx1);
                        if (q.IsFieldExist(DDBContent.Field_DBData_GameTimeSecond, out idx2)) gameTimeSec = q.GetInteger(idx2);
                    }
                    q.Release();
                }
                db.Close();
                db = null;
            }
            catch { }
            finally
            {
                try { if (db != null) db.Close(); } catch { }
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        private static void PatchDbFileUserId(string dbPath, long expectedUid)
        {
            string tmp = dbPath + ".mod_patchtmp";
            SQLiteDB db = null;
            try
            {
                File.Copy(dbPath, tmp, true);
                bool wasPlain = IsPlainSqliteFile(tmp);
                if (!wasPlain)
                {
                    if (s_miDecodeDb == null)
                        s_miDecodeDb = typeof(CDBServer).GetMethod("DecodeFile", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    InvokeDbCrypto(s_miDecodeDb, tmp);
                }
                db = new SQLiteDB();
                db.Open(tmp);

                long curUid = 0L;
                bool hasRow = false;
                SQLiteQuery q = CSqliteUtil.GetTableQuery(db, DDBContent.Table_DBData);
                if (q != null)
                {
                    if (q.Step())
                    {
                        hasRow = true;
                        int idx1 = 0;
                        if (q.IsFieldExist(DDBContent.Field_DBData_UserID, out idx1)) curUid = q.GetLong(idx1);
                    }
                    q.Release();
                }

                if (hasRow && curUid != expectedUid)
                {
                    SQLiteQuery uq = new SQLiteQuery(db, "UPDATE " + DDBContent.Table_DBData + " SET " + DDBContent.Field_DBData_UserID + " = " + expectedUid.ToString());
                    uq.Step();
                    uq.Release();
                    db.Close();
                    db = null;

                    if (s_miEncryptDb == null)
                        s_miEncryptDb = typeof(CDBServer).GetMethod("EncryptFile", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    InvokeDbCrypto(s_miEncryptDb, tmp);
                    File.Copy(tmp, dbPath, true);
                    Debug.Log("[ModBridge] PatchDbFileUserId updated DBData.UserID " + curUid + " -> " + expectedUid + " in " + dbPath);
                }
                else
                {
                    db.Close();
                    db = null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModBridge] PatchDbFileUserId ex: " + ex.Message);
            }
            finally
            {
                try { if (db != null) db.Close(); } catch { }
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        private unsafe int CmdLogin7891Sync(byte* b, int subOp, int forceImport)
        {
            uint flags = (b != null) ? *(uint*)(b + 0x18) : 0u;
            bool is7891 = (flags & (1u << 20)) != 0u;
            long targetUid = is7891 ? MOD_FIXED_UID : ((_origLoginUid > 0L) ? _origLoginUid : (CAccountMgr.Instance != null ? CAccountMgr.Instance.GetUserID() : 0L));
            if (is7891)
            {
                ApplyActiveUserId7891(true);
            }
            else if (_origLoginUid > 0L && CAccountMgr.Instance != null)
            {
                CAccountMgr.Instance.SetUserID(_origLoginUid);
                SetDataBridgeUserId(_origLoginUid, _origLoginToken);
            }
            if (targetUid > 0L)
            {
                if (forceImport != 0) _lastSyncedSaveUid = 0L;
                EnsureSaveCompatForUser(targetUid, forceImport != 0);
            }
            ExportDynLoginState(b, true);
            SetStatus(1, "Login UID=" + targetUid);
            return 1;
        }

        private unsafe void ExportDynLoginState(byte* b, bool force)
        {
            try
            {
                string dir = ReadShmDynDir(b);
                if (string.IsNullOrEmpty(dir)) return;
                long curUid = (CAccountMgr.Instance != null) ? CAccountMgr.Instance.GetUserID() : 0L;
                string root = (!string.IsNullOrEmpty(Application.persistentDataPath)) ? Application.persistentDataPath.Replace('\\', '/').TrimEnd('/') : "";
                string activeDb = (curUid > 0L && !string.IsNullOrEmpty(root)) ? ("db_" + curUid + "/GameWorld.db") : "";
                bool dbExists = !string.IsNullOrEmpty(activeDb) && File.Exists(root + "/" + activeDb);

                StringBuilder sb = new StringBuilder(256);
                sb.Append("{\"uid\":").Append(curUid);
                sb.Append(",\"origUid\":").Append(_origLoginUid);
                sb.Append(",\"activeDb\":\"").Append(JsonEsc(activeDb)).Append('"');
                sb.Append(",\"dbExists\":").Append(dbExists ? 1 : 0);
                sb.Append(",\"msg\":\"").Append(JsonEsc(_loginStateMsg ?? "")).Append("\"}");
                string json = sb.ToString();
                if (!force && json == _lastLoginStateJson) return;
                _lastLoginStateJson = json;
                WriteAtomicText(dir + "/dyn_login_state.json", json);
            }
            catch { }
        }

        // ==================== #23 Map Hierarchy & One-Tap Teleport ====================
        private static int GetSceneChapterCat(sSceneConfig cfg)
        {
            if (cfg == null) return 7;
            int id = cfg.nID;
            if (cfg.eGoOutUI == sSceneConfig.eGoOutUIEnum.LAN) return 8;
            switch (id)
            {
                case 112: case 201: case 202: case 205: case 221: case 415: case 416: case 417: case 418:
                case 425: case 430: case 435: case 445: case 450: case 451: case 452: case 460: case 461:
                case 462: case 470: case 475: case 480: case 482: case 483: case 551: case 560: case 561:
                case 562: case 563: case 564: case 565: case 566: case 590: case 591: case 592: case 600:
                case 601: case 605: case 606: case 620: case 621: case 630: case 631: case 690: case 691:
                case 692: case 695: case 850: case 920: case 921: case 922: case 931: case 933: case 935:
                    return 1; // 主线·蝗虫篇
                case 100: case 101: case 110: case 111: case 115: case 116: case 117: case 118: case 119:
                case 120: case 121: case 130: case 150: case 151: case 152: case 153: case 200: case 400:
                    return 1;
                case 122: case 123: case 125: case 126: case 230: case 580: case 581: case 582: case 583:
                case 584: case 585: case 697: case 698: case 699: case 700: case 704: case 705: case 710:
                case 715: case 750: case 751: case 753: case 754: case 755: case 756: case 757: case 758:
                case 759: case 760: case 761: case 765: case 770: case 775: case 780: case 784: case 785:
                case 786: case 787: case 788: case 789: case 790:
                    return 2; // 镇长篇 DLC
                case 350: case 500: case 501: case 502: case 503: case 504: case 515: case 516: case 517:
                case 518: case 519: case 520: case 540: case 550: case 552: case 553: case 554: case 570:
                case 571: case 572: case 573: case 680: case 681: case 701: case 703: case 716: case 717:
                case 718: case 720: case 721: case 722: case 723: case 724: case 725: case 726: case 727:
                case 728: case 729: case 730: case 731: case 732: case 733: case 734: case 735: case 736:
                case 737: case 738: case 800: case 805: case 806: case 807: case 808: case 809: case 814:
                case 815: case 816: case 819: case 880: case 881: case 882: case 883: case 884: case 885:
                case 886: case 887: case 895: case 896: case 899: case 900: case 901: case 902: case 903:
                case 904: case 905: case 906: case 907: case 908: case 909: case 910: case 911: case 912:
                case 913: case 914: case 915: case 916: case 918: case 919:
                    return 3; // 尼奥·301 & SEAKING
                case 453: case 567: case 568: case 586: case 587: case 588: case 589: case 593: case 740:
                case 741: case 742: case 743: case 744: case 745: case 792: case 794: case 795: case 796:
                case 797: case 811: case 812: case 813: case 817: case 818: case 851: case 852: case 853:
                case 854: case 855: case 856: case 857: case 858: case 875: case 876: case 877: case 878:
                case 879:
                    return 4; // 姜娜·回忆篇
                case 490: case 491: case 492: case 493: case 494: case 495: case 496: case 497: case 498:
                case 499: case 510: case 511: case 512: case 513: case 514: case 810: case 820: case 821:
                case 822: case 823: case 824: case 825: case 826: case 827: case 828: case 829: case 830:
                case 831: case 832: case 833: case 834: case 835: case 836: case 837: case 838: case 839:
                case 840: case 841: case 842: case 843: case 844: case 845: case 846: case 847: case 848:
                case 849: case 860: case 861: case 862: case 863: case 864: case 865: case 866: case 867:
                case 868: case 869: case 871: case 872: case 873: case 874:
                    return 5; // 蚁穴·兵封王座
                case 522: case 523: case 524: case 525: case 526: case 527: case 528: case 529: case 530:
                case 531: case 535: case 536: case 537: case 538:
                    return 6; // 鹰眼组织篇
                case 131: case 132: case 640: case 641: case 642: case 643: case 644: case 645: case 646:
                case 647: case 650: case 651: case 652: case 653: case 654: case 655: case 656: case 657:
                case 658: case 890: case 891: case 892:
                    return 7; // 活动玩法 & 回忆
                case 950: case 955: case 957: case 970: case 971: case 972: case 973: case 974: case 975:
                case 976: case 977: case 978: case 979: case 980: case 981: case 984: case 985: case 986:
                case 987: case 989: case 990: case 991: case 996:
                    return 8; // 联机副本
            }
            string us = (cfg.szUnityScene ?? "").ToLowerInvariant();
            if (us.StartsWith("lan_")) return 8;
            if (us.Contains("_huodong_") || us.Contains("_huiyi")) return 7;
            if (us.Contains("_yingyan")) return 6;
            if (us.Contains("_yixue")) return 5;
            if (us.Contains("_jiangna")) return 4;
            if (us.Contains("_neil_") || us.Contains("_301")) return 3;
            if (us.Contains("_dlc_")) return 2;
            return 1;
        }

        private static MethodInfo s_miYooCheckValid;
        private static bool s_yooCheckResolved = false;
        private static FieldInfo s_fiAssetEditorDic;
        private static bool s_assetDicResolved = false;

        private static Dictionary<string, string> GetAssetEditorDict()
        {
            try
            {
                if (!s_assetDicResolved)
                {
                    s_assetDicResolved = true;
                    Type ta = FindNamedType("CAssetMgr");
                    if (ta != null)
                        s_fiAssetEditorDic = ta.GetField("dicEditorPath", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                }
                if (s_fiAssetEditorDic != null)
                    return s_fiAssetEditorDic.GetValue(null) as Dictionary<string, string>;
            }
            catch { }
            return null;
        }

        private static bool IsSceneLocationValid(string unityScene)
        {
            if (string.IsNullOrEmpty(unityScene)) return false;
            try
            {
                if (!s_yooCheckResolved)
                {
                    s_yooCheckResolved = true;
                    Type ty = FindNamedType("YooAsset.YooAssets");
                    if (ty != null)
                        s_miYooCheckValid = ty.GetMethod("CheckLocationValid", BindingFlags.Static | BindingFlags.Public, null, new Type[] { typeof(string) }, null);
                }

                Dictionary<string, string> edDic = GetAssetEditorDict();
                if (edDic != null && edDic.Count > 0)
                {
                    string keyExt = unityScene.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ? unityScene : (unityScene + ".unity");
                    string loc = null;
                    if (!edDic.TryGetValue(keyExt, out loc))
                        edDic.TryGetValue(unityScene, out loc);
                    if (!string.IsNullOrEmpty(loc))
                    {
                        if (s_miYooCheckValid != null)
                        {
                            object r = s_miYooCheckValid.Invoke(null, new object[] { loc });
                            if (r is bool) return (bool)r;
                        }
                        return true;
                    }
                    // If not found in CAssetMgr.dicEditorPath, check direct YooAssets location just in case before rejecting
                    if (s_miYooCheckValid != null)
                    {
                        object r1 = s_miYooCheckValid.Invoke(null, new object[] { keyExt });
                        if (r1 is bool && (bool)r1) return true;
                        object r2 = s_miYooCheckValid.Invoke(null, new object[] { unityScene });
                        if (r2 is bool && (bool)r2) return true;
                    }
                    return false;
                }

                if (s_miYooCheckValid != null)
                {
                    string keyExt = unityScene.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ? unityScene : (unityScene + ".unity");
                    object r = s_miYooCheckValid.Invoke(null, new object[] { keyExt });
                    if (r is bool && (bool)r) return true;
                }
            }
            catch { }
            return !(unityScene.Contains("migong_658") || unityScene.Contains("neil_cunzhuang_701") || unityScene.Contains("yixue_975") || unityScene.Contains("xinshoulianxi_996"));
        }

        private static int ResolveRootScene(Dictionary<int, sSceneConfig> dic, int sceneId)
        {
            if (dic == null || sceneId == 0) return 0;
            int cur = sceneId;
            for (int depth = 0; depth < 6; depth++)
            {
                sSceneConfig cfg = null;
                if (!dic.TryGetValue(cur, out cfg) || cfg == null || cfg.nBelongScene <= 0 || cfg.nBelongScene == cur)
                    break;
                cur = cfg.nBelongScene;
            }
            return cur;
        }

        private void ExportDynMaps(IGameDataCenter dc, CLanguageMgr lm, string dir)
        {
            try
            {
                CSceneConfigLoader sl = dc.GetConfigLoader(eGameConfigType.Scene) as CSceneConfigLoader;
                if (sl == null) return;
                FieldInfo fiDic = typeof(CSceneConfigLoader).GetField("m_dicScene", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                Dictionary<int, sSceneConfig> dic = (fiDic != null) ? (fiDic.GetValue(sl) as Dictionary<int, sSceneConfig>) : null;
                if (dic == null || dic.Count == 0) return;

                List<sSceneConfig> list = new List<sSceneConfig>(dic.Values);
                list.Sort(delegate(sSceneConfig a, sSceneConfig b) { return a.nID.CompareTo(b.nID); });

                StringBuilder sb = new StringBuilder(list.Count * 140);
                sb.Append('[');
                bool first = true;
                for (int i = 0; i < list.Count; i++)
                {
                    sSceneConfig s = list[i];
                    if (s == null || s.nID <= 0) continue;
                    string nm = !string.IsNullOrEmpty(s.szStringName) ? s.szStringName : (s.nStringName > 0 && lm != null ? lm.GetString(s.nStringName) : ("Scene #" + s.nID));
                    string ds = !string.IsNullOrEmpty(s.szStringDesc) ? s.szStringDesc : (s.nStringDesc > 0 && lm != null ? lm.GetString(s.nStringDesc) : "");
                    int rootId = ResolveRootScene(dic, s.nID);
                    int cat = GetSceneChapterCat(s);
                    bool valid = IsSceneLocationValid(s.szUnityScene);

                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"id\":").Append(s.nID);
                    sb.Append(",\"name\":\"").Append(JsonEsc(nm)).Append('"');
                    sb.Append(",\"desc\":\"").Append(JsonEsc(ds)).Append('"');
                    sb.Append(",\"us\":\"").Append(JsonEsc(s.szUnityScene ?? "")).Append('"');
                    sb.Append(",\"belong\":").Append(s.nBelongScene);
                    sb.Append(",\"root\":").Append(rootId);
                    sb.Append(",\"spawn\":").Append(s.nSpawnPointID);
                    sb.Append(",\"cat\":").Append(cat);
                    sb.Append(",\"ui\":").Append((int)s.eGoOutUI);
                    sb.Append(",\"valid\":").Append(valid ? 1 : 0).Append('}');
                }
                sb.Append(']');
                WriteAtomicText(dir + "/dyn_maps.json", sb.ToString());
                Debug.Log("[ModBridge] Exported dyn_maps.json count=" + list.Count);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModBridge] ExportDynMaps ex: " + ex.Message);
            }
        }

        private unsafe void ExportDynMapState(IGameSystem gs, bool force)
        {
            try
            {
                string dir = ReadShmDynDir(Shm());
                if (string.IsNullOrEmpty(dir)) return;
                int curScene = (CGameSceneMgr.Instance != null)
                    ? CGameSceneMgr.Instance.nSceneID
                    : ((gs != null && gs.sceneSystem != null) ? gs.sceneSystem.GetCurSceneID() : 0);

                List<int> arrived = new List<int>();
                List<int> own = new List<int>();
                if (gs != null && gs.sceneSystem != null && CClientGlobal.Instance != null && CClientGlobal.Instance.GetGameDataCenter != null)
                {
                    CSceneConfigLoader sl = CClientGlobal.Instance.GetGameDataCenter.GetConfigLoader(eGameConfigType.Scene) as CSceneConfigLoader;
                    FieldInfo fiDic = (sl != null) ? typeof(CSceneConfigLoader).GetField("m_dicScene", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) : null;
                    Dictionary<int, sSceneConfig> dic = (fiDic != null) ? (fiDic.GetValue(sl) as Dictionary<int, sSceneConfig>) : null;
                    if (dic != null)
                    {
                        foreach (int sid in dic.Keys)
                        {
                            CScene sc = gs.sceneSystem.GetScene(sid);
                            if (sc != null)
                            {
                                if (sc.bArrived) arrived.Add(sid);
                                if (sc.bOwn) own.Add(sid);
                            }
                        }
                    }
                }
                arrived.Sort();
                own.Sort();

                StringBuilder sb = new StringBuilder(512);
                sb.Append("{\"curScene\":").Append(curScene).Append(",\"arrived\":[");
                for (int i = 0; i < arrived.Count; i++) { if (i > 0) sb.Append(','); sb.Append(arrived[i]); }
                sb.Append("],\"own\":[");
                for (int i = 0; i < own.Count; i++) { if (i > 0) sb.Append(','); sb.Append(own[i]); }
                sb.Append("]}");
                string json = sb.ToString();
                if (!force && json == _lastMapStateJson) return;
                _lastMapStateJson = json;
                WriteAtomicText(dir + "/dyn_map_state.json", json);
            }
            catch { }
        }

        private void HandlePostTeleportSpawn(int sid)
        {
            if (_pendingTeleportScene <= 0 || sid != _pendingTeleportScene) return;
            _pendingTeleportScene = 0;
            try
            {
                CScene sc = (CGameSystem.Instance != null && CGameSystem.Instance.sceneSystem != null)
                    ? CGameSystem.Instance.sceneSystem.GetScene(sid) : null;
                if (sc != null && !sc.bArrived) sc.RecordArrived();

                if (CGameSceneMgr.Instance != null && CGameSceneMgr.Instance.GetSceneSpawnPoint(_pendingTeleportSpawn) == null)
                {
                    // Only adjust if player landed near world origin (0,0) when target scene lacks requested spawnPointId
                    ulong puid = CGameSceneMgr.Instance.GetCurrentCharacterUID();
                    CEntity pe = (puid != 0UL) ? CGameSceneMgr.Instance.GetEntity(puid) : null;
                    if (pe != null && pe.transform.position.sqrMagnitude < 0.01f)
                    {
                        FieldInfo fiSp = typeof(CGameSceneMgr).GetField("m_dicSceneSpawnPoint", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        Dictionary<int, Transform> spDic = (fiSp != null) ? (fiSp.GetValue(CGameSceneMgr.Instance) as Dictionary<int, Transform>) : null;
                        if (spDic != null && spDic.Count > 0)
                        {
                            Transform bestSp = null;
                            int bestKey = int.MaxValue;
                            foreach (KeyValuePair<int, Transform> kv in spDic)
                            {
                                if (kv.Value != null && kv.Key < bestKey) { bestKey = kv.Key; bestSp = kv.Value; }
                            }
                            if (bestSp != null)
                            {
                                if (pe.movementPart != null)
                                    pe.movementPart.SetPositionForce(new Vector3(bestSp.position.x, bestSp.position.y, pe.transform.position.z));
                                else
                                    pe.transform.position = new Vector3(bestSp.position.x, bestSp.position.y, pe.transform.position.z);
                                if (ProCamera2D.Instance != null)
                                    ProCamera2D.Instance.MoveCameraInstantlyToPosition(pe.transform.position);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static bool IsLanSceneConfig(sSceneConfig cfg)
        {
            if (cfg == null) return false;
            if ((int)cfg.eGoOutUI == 3) return true;
            if (!string.IsNullOrEmpty(cfg.szUnityScene) && cfg.szUnityScene.IndexOf("lan_", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static Type s_lobbyMgrType = null;
        private static bool s_lobbyMgrTypeResolved = false;
        private static Type s_netMgrType = null;
        private static bool s_netMgrTypeResolved = false;

        private static Type GetLobbyMgrType()
        {
            if (!s_lobbyMgrTypeResolved)
            {
                s_lobbyMgrTypeResolved = true;
                s_lobbyMgrType = FindNamedType("LAN.CNetworkLobbyManager");
            }
            return s_lobbyMgrType;
        }

        private static Type GetNetMgrType()
        {
            if (!s_netMgrTypeResolved)
            {
                s_netMgrTypeResolved = true;
                s_netMgrType = FindNamedType("Mirror.NetworkManager");
            }
            return s_netMgrType;
        }

        private static Component GetLobbyMgrInstance()
        {
            try
            {
                Type lt = GetLobbyMgrType();
                if (lt == null) return null;
                PropertyInfo pi = lt.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                return (pi != null) ? (pi.GetValue(null, null) as Component) : null;
            }
            catch { return null; }
        }

        private static object GetNetMgrSingleton()
        {
            try
            {
                Type nt = GetNetMgrType();
                if (nt == null) return null;
                PropertyInfo pi = nt.GetProperty("singleton", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                return (pi != null) ? pi.GetValue(null, null) : null;
            }
            catch { return null; }
        }

        private static bool IsLanSessionActive()
        {
            try
            {
                if (GetLobbyMgrInstance() != null) return true;
                if (Mirror.NetworkServer.active || Mirror.NetworkClient.active) return true;
                if (LAN.CLanMainCharacter.localPlayer != null) return true;
                UnityEngine.SceneManagement.Scene ls = UnityEngine.SceneManagement.SceneManager.GetSceneByName("Lobby");
                if (ls.IsValid() && ls.isLoaded) return true;
            }
            catch { }
            return false;
        }

        private static void ForceClearLoadingScreens()
        {
            try
            {
                if (Time.timeScale <= 0f) Time.timeScale = 1f;
                if (CTimeScaleMgr.Instance != null)
                {
                    CTimeScaleMgr.Instance.RemoveTimeScaleNode(CTimeScaleMgr.CONST_ID_LOADSCENE);
                    CTimeScaleMgr.Instance.RemoveTimeScaleNode(CTimeScaleMgr.CONST_ID_OPEN_UI);
                    MethodInfo miReset = typeof(CTimeScaleMgr).GetMethod("ResetPauseLock", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (miReset != null) miReset.Invoke(CTimeScaleMgr.Instance, null);
                }
                if (CUIMgr.Instance != null)
                {
                    try { CUIMgr.Instance.UnSubBlock(); } catch { }
                    CUI_Common_Loading cl = CUIMgr.Instance.GetWnd(CUIDefine.eUIWnd.UI_Common_Loading) as CUI_Common_Loading;
                    if (cl != null)
                    {
                        try { cl.ClearLock(); } catch { }
                        if (cl.IsVisable()) { try { cl.Hide(); } catch { } }
                    }
                    try
                    {
                        foreach (CUIDefine.eUIWnd ev in Enum.GetValues(typeof(CUIDefine.eUIWnd)))
                        {
                            if (ev.ToString() == "UI_Lan_Loading")
                            {
                                CUIBase ll = CUIMgr.Instance.GetWnd(ev);
                                if (ll != null)
                                {
                                    FieldInfo fi = ll.GetType().GetField("m_nLock", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                                    if (fi != null) fi.SetValue(ll, 0);
                                    if (ll.IsVisable()) ll.Hide();
                                }
                                break;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static void CloseGoOutMapWindows()
        {
            try
            {
                if (CUIMgr.Instance == null) return;
                CUIDefine.eUIWnd[] mapWnds = new CUIDefine.eUIWnd[] {
                    CUIDefine.eUIWnd.UI_Game_GoOutMap, CUIDefine.eUIWnd.UI_Game_GoOutMapInformation,
                    CUIDefine.eUIWnd.UI_Game_GoOutMapDesc, CUIDefine.eUIWnd.UI_Game_GoOutMapCollect,
                    CUIDefine.eUIWnd.UI_Game_GoOutTeamate, CUIDefine.eUIWnd.UI_Game_DLC_Map,
                    CUIDefine.eUIWnd.UI_Game_DLC_Info, CUIDefine.eUIWnd.UI_Game_MapNeilDlc,
                    CUIDefine.eUIWnd.UI_Game_GoOutYingYanMap, CUIDefine.eUIWnd.UI_Game_GoOutSeaMap,
                    CUIDefine.eUIWnd.UI_Game_GoOutSeaMap_HaiShe, CUIDefine.eUIWnd.UI_Game_GoOutSeaMap_DianYao,
                    CUIDefine.eUIWnd.UI_Game_GoOutSeaMap_HaiDan, CUIDefine.eUIWnd.UI_Game_NetworkMode
                };
                for (int i = 0; i < mapWnds.Length; i++)
                {
                    CUIBase w = CUIMgr.Instance.GetWnd(mapWnds[i]);
                    if (w != null && w.IsVisable()) w.Hide();
                }
                foreach (CUIDefine.eUIWnd ev in Enum.GetValues(typeof(CUIDefine.eUIWnd)))
                {
                    string en = ev.ToString();
                    if (en.StartsWith("UI_Lan_", StringComparison.Ordinal))
                    {
                        CUIBase lw = CUIMgr.Instance.GetWnd(ev);
                        if (lw != null && lw.IsVisable()) lw.Hide();
                    }
                }
            }
            catch { }
        }

        private static void SetLobbyStaticField(string fieldName, object val)
        {
            try
            {
                Type lt = GetLobbyMgrType();
                if (lt == null) return;
                FieldInfo fi = lt.GetField(fieldName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (fi != null) fi.SetValue(null, val);
            }
            catch { }
        }

        private static void TeardownLanLobbyAndStopHost()
        {
            try
            {
                SetLobbyStaticField("WasActiveLAN", false);
                SetLobbyStaticField("WasLanDisconnected", false);
                SetLobbyStaticField("NetworkMode", LAN.eNetworkMode.Single);
                object nm = GetNetMgrSingleton();
                Type nt = GetNetMgrType();
                if (nm != null && nt != null)
                {
                    MethodInfo miStop = nt.GetMethod("StopHost", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(bool) }, null)
                                     ?? nt.GetMethod("StopHost", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (miStop != null)
                    {
                        ParameterInfo[] ps = miStop.GetParameters();
                        try { miStop.Invoke(nm, (ps.Length == 1) ? new object[] { false } : null); } catch { }
                    }
                }
                Component lobbyComp = GetLobbyMgrInstance();
                if (lobbyComp != null && lobbyComp.gameObject != null)
                {
                    try { UnityEngine.Object.Destroy(lobbyComp.gameObject); } catch { }
                }
                UnityEngine.SceneManagement.Scene ls = UnityEngine.SceneManagement.SceneManager.GetSceneByName("Lobby");
                if (ls.IsValid() && ls.isLoaded)
                {
                    try { UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync("Lobby"); } catch { }
                }
            }
            catch { }
        }

        private void BeginLanSoloEntry(int sceneId, int spawnPointId)
        {
            _lanTeleportSceneId = sceneId;
            _lanTeleportSpawnId = spawnPointId;
            _lanTeleportStart = Time.realtimeSinceStartup;
            _lanLocalReadyAt = 0f;
            _lanLobbyWaitHostInit = false;

            SetLobbyStaticField("NetworkMode", LAN.eNetworkMode.Single);
            SetLobbyStaticField("CurrentSceneID", sceneId);

            bool hasLobby = false;
            int sc = UnityEngine.SceneManagement.SceneManager.sceneCount;
            for (int i = 0; i < sc; i++)
            {
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).name == "Lobby")
                {
                    hasLobby = true;
                    break;
                }
            }
            if (!hasLobby && CAssetMgr.Instance != null)
            {
                CAssetMgr.Instance.LoadLobbyScene("Lobby.unity", UnityEngine.SceneManagement.LoadSceneMode.Additive);
            }
            _lanTeleportStage = 1;
            Debug.Log("[ModBridge] LAN Solo entry stage 1 scene=#" + sceneId + " hasLobby=" + hasLobby);
        }

        private void TickPendingLanTeleport(IGameSystem gs)
        {
            try
            {
                if (_spTeleportStart > 0f && (Time.realtimeSinceStartup - _spTeleportStart) > 12f)
                {
                    Debug.LogWarning("[ModBridge] SP teleport watchdog timeout for #" + _pendingTeleportScene);
                    ForceClearLoadingScreens();
                    _spTeleportStart = 0f;
                }

                if (_lanTeleportStage == 0) return;

                if (_lanTeleportStart > 0f && (Time.realtimeSinceStartup - _lanTeleportStart) > 16f)
                {
                    Debug.LogWarning("[ModBridge] LAN teleport stage " + _lanTeleportStage + " timeout for #" + _lanTeleportSceneId);
                    ForceClearLoadingScreens();
                    _lanTeleportStage = 0;
                    return;
                }

                if (_lanTeleportStage == 5)
                {
                    _lanSpDelayFrames--;
                    if (_lanSpDelayFrames <= 0)
                    {
                        BeginLanSoloEntry(_lanTeleportSceneId, _lanTeleportSpawnId);
                    }
                    return;
                }

                if (_lanTeleportStage == 4)
                {
                    _lanSpDelayFrames--;
                    if (_lanSpDelayFrames <= 0 && gs != null && gs.sceneSystem != null)
                    {
                        ForceClearLoadingScreens();
                        _pendingTeleportScene = _lanSpNextSceneId;
                        _pendingTeleportSpawn = _lanSpNextSpawnId;
                        _spTeleportStart = Time.realtimeSinceStartup;
                        _lanTeleportStage = 0;
                        Debug.Log("[ModBridge] Exited LAN -> loading SP scene #" + _pendingTeleportScene);
                        gs.sceneSystem.LoadScene(_pendingTeleportScene, _pendingTeleportSpawn, true, true, eLoadingColor.White);
                    }
                    return;
                }

                if (_lanTeleportStage == 1)
                {
                    Component lobby = GetLobbyMgrInstance();
                    if (lobby == null) return;
                    if (!Mirror.NetworkServer.active && !Mirror.NetworkClient.active && !_lanLobbyWaitHostInit)
                    {
                        _lanLobbyWaitHostInit = true;
                        SetLobbyStaticField("NetworkMode", LAN.eNetworkMode.Single);
                        SetLobbyStaticField("CurrentSceneID", _lanTeleportSceneId);
                        object nm = GetNetMgrSingleton();
                        Type nt = GetNetMgrType();
                        if (nm != null && nt != null)
                        {
                            MethodInfo miStartHost = nt.GetMethod("StartHost", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null)
                                                   ?? nt.GetMethod("StartHost", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (miStartHost != null) miStartHost.Invoke(nm, null);
                        }
                    }
                    _lanTeleportStage = 2;
                    return;
                }

                if (_lanTeleportStage == 2)
                {
                    Component lobby = GetLobbyMgrInstance();
                    object nm = GetNetMgrSingleton();
                    if (lobby == null || nm == null) return;
                    BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    FieldInfo fiSlots = lobby.GetType().GetField("roomSlots", bf);
                    System.Collections.IList slots = (fiSlots != null) ? (fiSlots.GetValue(lobby) as System.Collections.IList) : null;
                    if (slots == null || slots.Count == 0) return;

                    bool hasLocal = false;
                    for (int i = 0; i < slots.Count; i++)
                    {
                        object slot = slots[i];
                        if (slot == null) continue;
                        PropertyInfo piLoc = slot.GetType().GetProperty("isLocalPlayer", bf);
                        bool isLoc = (piLoc != null && piLoc.GetValue(slot, null) is bool) ? (bool)piLoc.GetValue(slot, null) : false;
                        if (isLoc)
                        {
                            FieldInfo fiReady = slot.GetType().GetField("readyToBegin", bf);
                            if (fiReady != null) fiReady.SetValue(slot, true);
                            hasLocal = true;
                            break;
                        }
                    }
                    if (!hasLocal) return;

                    CSceneConfigLoader sl = (CClientGlobal.Instance != null && CClientGlobal.Instance.GetGameDataCenter != null)
                        ? (CClientGlobal.Instance.GetGameDataCenter.GetConfigLoader(eGameConfigType.Scene) as CSceneConfigLoader) : null;
                    sSceneConfig cfg = (sl != null) ? sl.GetConfig(_lanTeleportSceneId) : null;
                    if (cfg == null || string.IsNullOrEmpty(cfg.szUnityScene))
                    {
                        _lanTeleportStage = 0;
                        return;
                    }

                    ForceClearLoadingScreens();
                    SetLobbyStaticField("NetworkMode", LAN.eNetworkMode.Single);
                    SetLobbyStaticField("CurrentSceneID", _lanTeleportSceneId);

                    try
                    {
                        string bn = CAssetMgr.GetBundleName(cfg.szUnityScene);
                        if (!string.IsNullOrEmpty(bn)) Bundles.Load(bn);
                    }
                    catch { }

                    string[] parts = cfg.szUnityScene.Split('.');
                    string shortScene = (parts.Length > 0) ? parts[0] : cfg.szUnityScene;
                    Debug.Log("[ModBridge] LAN Solo ServerChangeScene -> " + shortScene + " (#" + _lanTeleportSceneId + ")");
                    Type nt = GetNetMgrType();
                    MethodInfo miScs = (nt != null) ? nt.GetMethod("ServerChangeScene", bf, null, new Type[] { typeof(string), typeof(Mirror.SceneOperation) }, null) : null;
                    if (miScs != null)
                        miScs.Invoke(nm, new object[] { shortScene, Mirror.SceneOperation.Normal });
                    _lanTeleportStage = 3;
                    return;
                }

                if (_lanTeleportStage == 3)
                {
                    if (LAN.CLanMainCharacter.localPlayer != null)
                    {
                        if (_lanLocalReadyAt <= 0f) _lanLocalReadyAt = Time.realtimeSinceStartup;
                        if ((Time.realtimeSinceStartup - _lanLocalReadyAt) >= 0.4f)
                        {
                            ForceClearLoadingScreens();
                            if (gs != null && gs.sceneSystem != null)
                            {
                                CScene sc = gs.sceneSystem.GetScene(_lanTeleportSceneId);
                                if (sc != null && !sc.bArrived) sc.RecordArrived();
                                ExportDynMapState(gs, true);
                            }
                            SetStatus(1, "已进入联机副本(单人): #" + _lanTeleportSceneId);
                            Debug.Log("[ModBridge] LAN Solo ready scene=#" + _lanTeleportSceneId);
                            _lanTeleportStage = 0;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModBridge] TickPendingLanTeleport ex: " + ex.Message);
                ForceClearLoadingScreens();
                _lanTeleportStage = 0;
            }
        }

        private int CmdTeleportScene(IGameSystem gs, CEntity player, int sceneId, int spawnPointId, int flagsOpt)
        {
            if (gs == null || gs.sceneSystem == null || sceneId <= 0) return -2;
            try
            {
                if (CClientGlobal.Instance == null || CClientGlobal.Instance.GetGameDataCenter == null) return -2;
                CSceneConfigLoader sl = CClientGlobal.Instance.GetGameDataCenter.GetConfigLoader(eGameConfigType.Scene) as CSceneConfigLoader;
                sSceneConfig cfg = (sl != null) ? sl.GetConfig(sceneId) : null;
                if (cfg == null)
                {
                    SetStatus(-2, "Unknown scene #" + sceneId);
                    return -2;
                }
                if (!IsSceneLocationValid(cfg.szUnityScene))
                {
                    SetStatus(-2, "Scene asset missing: #" + sceneId + " " + (cfg.szUnityScene ?? ""));
                    return -2;
                }

                CloseGoOutMapWindows();
                ForceClearLoadingScreens();

                CScene sc = gs.sceneSystem.GetScene(sceneId);
                if (sc != null && (flagsOpt & 1) != 0)
                {
                    sc.SetOwn(true);
                    sc.RecordArrived();
                }

                int useSpawn = (spawnPointId >= 0) ? spawnPointId : cfg.nSpawnPointID;
                bool targetIsLan = IsLanSceneConfig(cfg);
                bool inLanNow = IsLanSessionActive();

                if (targetIsLan)
                {
                    _lanTeleportSceneId = sceneId;
                    _lanTeleportSpawnId = useSpawn;
                    _lanTeleportStart = Time.realtimeSinceStartup;
                    if (inLanNow)
                    {
                        TeardownLanLobbyAndStopHost();
                        _lanSpDelayFrames = 3;
                        _lanTeleportStage = 5;
                    }
                    else
                    {
                        BeginLanSoloEntry(sceneId, useSpawn);
                    }
                    SetStatus(1, "正在单人进入联机副本 -> #" + sceneId + " " + (cfg.szStringName ?? ""));
                    return 1;
                }

                if (inLanNow)
                {
                    TeardownLanLobbyAndStopHost();
                    _lanSpNextSceneId = sceneId;
                    _lanSpNextSpawnId = useSpawn;
                    _lanSpDelayFrames = 3;
                    _lanTeleportStart = Time.realtimeSinceStartup;
                    _lanTeleportStage = 4;
                    SetStatus(1, "正在退出联机返回 -> #" + sceneId + " " + (cfg.szStringName ?? ""));
                    return 1;
                }

                _pendingTeleportScene = sceneId;
                _pendingTeleportSpawn = useSpawn;
                _spTeleportStart = Time.realtimeSinceStartup;
                Debug.Log("[ModBridge] Teleporting -> #" + sceneId + " spawn=" + useSpawn + " asset=" + (cfg.szUnityScene ?? ""));
                gs.sceneSystem.LoadScene(sceneId, useSpawn, true, true, eLoadingColor.White);
                SetStatus(1, "Teleporting -> #" + sceneId + " " + (cfg.szStringName ?? ""));
                return 1;
            }
            catch (Exception ex)
            {
                SetStatus(-99, "Teleport ex: " + ex.Message);
                Debug.LogWarning("[ModBridge] CmdTeleportScene ex: " + ex.Message);
                return -99;
            }
        }

        // ==================== #24 Native Task / SceneEvent Manager ====================
        private static string FormatSingleConditionText(sConditionConfig c, CSceneConfigLoader sl, CMaterialConfigLoader ml, CUnitConfigLoader ul, CLanguageMgr lm)
        {
            if (c == null) return "";
            int p1 = 0, p2 = 0;
            int.TryParse(c.szParam_1 ?? "", out p1);
            int.TryParse(c.szParam_2 ?? "", out p2);
            switch (c.eType)
            {
                case sConditionConfig.eConditionType.FSM_EqualAndBigger:
                    return (p1 > 0) ? ("状态机#" + p1 + ">=" + p2) : "自动完成/剧情触发";
                case sConditionConfig.eConditionType.SceneArrive:
                {
                    sSceneConfig sc = (sl != null && p1 > 0) ? sl.GetConfig(p1) : null;
                    string sn = (sc != null && !string.IsNullOrEmpty(sc.szStringName)) ? sc.szStringName : ("#" + p1);
                    return "到达[" + sn + "] (#" + p1 + ")";
                }
                case sConditionConfig.eConditionType.SceneOwn:
                {
                    sSceneConfig sc = (sl != null && p1 > 0) ? sl.GetConfig(p1) : null;
                    string sn = (sc != null && !string.IsNullOrEmpty(sc.szStringName)) ? sc.szStringName : ("#" + p1);
                    return (p2 == 1 ? "占领[" : "未占领[") + sn + "] (#" + p1 + ")";
                }
                case sConditionConfig.eConditionType.SceneShow:
                {
                    sSceneConfig sc = (sl != null && p1 > 0) ? sl.GetConfig(p1) : null;
                    string sn = (sc != null && !string.IsNullOrEmpty(sc.szStringName)) ? sc.szStringName : ("#" + p1);
                    return "地图显示[" + sn + "] (#" + p1 + ")";
                }
                case sConditionConfig.eConditionType.KillCount:
                {
                    sUnitConfig uc = (ul != null && p1 > 0) ? ul.GetConfig(p1) : null;
                    string un = (uc != null && !string.IsNullOrEmpty(uc.szStringName)) ? uc.szStringName : ("#" + p1);
                    return "击杀[" + un + "] x" + p2;
                }
                case sConditionConfig.eConditionType.InformationTalkFinish:
                    return "完成情报对话#" + p1;
                case sConditionConfig.eConditionType.InformationOwn:
                    return "拥有情报#" + p1;
                case sConditionConfig.eConditionType.Materiel:
                {
                    sMaterialConfig mc = (ml != null && p1 > 0) ? ml.GetConfig(p1) : null;
                    string mn = (mc != null && !string.IsNullOrEmpty(mc.szStringName)) ? mc.szStringName : ("#" + p1);
                    return "持有[" + mn + "] x" + p2;
                }
                case sConditionConfig.eConditionType.Talk:
                    return "对话#" + p1;
                case sConditionConfig.eConditionType.Task:
                    return "前置任务#" + p1;
                default:
                    return c.eType.ToString() + "(" + (c.szParam_1 ?? "") + "," + (c.szParam_2 ?? "") + ")";
            }
        }

        private static string FormatConditionGroupDesc(CConditionConfigLoader cl, int groupId, CSceneConfigLoader sl, CMaterialConfigLoader ml, CUnitConfigLoader ul, CLanguageMgr lm)
        {
            if (cl == null || groupId <= 0) return "";
            sConditionConfigGroup grp = cl.GetConditionGroup(groupId);
            if (grp == null || grp.listConfig == null || grp.listConfig.Count == 0) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < grp.listConfig.Count; i++)
            {
                string part = FormatSingleConditionText(grp.listConfig[i], sl, ml, ul, lm);
                if (string.IsNullOrEmpty(part)) continue;
                if (sb.Length > 0) sb.Append(" + ");
                sb.Append(part);
            }
            return sb.ToString();
        }

        private void BuildTaskCatalogAndHierarchy(IGameDataCenter dc, out List<sSceneEventConfig> canonList, out Dictionary<int, sSceneConfig> sceneDic)
        {
            canonList = new List<sSceneEventConfig>();
            sceneDic = null;
            _canonEventsCache = new Dictionary<int, sSceneEventConfig>();
            _canonMirrorsCache = new Dictionary<int, List<int>>();
            _taskPrereqCache = new Dictionary<int, List<int>>();
            _taskParentCache = new Dictionary<int, int>();
            _taskDepthCache = new Dictionary<int, int>();

            CSceneConfigLoader sl = dc.GetConfigLoader(eGameConfigType.Scene) as CSceneConfigLoader;
            CSceneEventConfigLoader el = dc.GetConfigLoader(eGameConfigType.SceneEvent) as CSceneEventConfigLoader;
            CConditionConfigLoader cl = dc.GetConfigLoader(eGameConfigType.Condition) as CConditionConfigLoader;
            if (sl == null || el == null || cl == null) return;

            FieldInfo fiSd = typeof(CSceneConfigLoader).GetField("m_dicScene", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            sceneDic = (fiSd != null) ? (fiSd.GetValue(sl) as Dictionary<int, sSceneConfig>) : null;
            FieldInfo fiEd = typeof(CSceneEventConfigLoader).GetField("m_dicConfigs", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Dictionary<int, sSceneEventConfig> evDic = (fiEd != null) ? (fiEd.GetValue(el) as Dictionary<int, sSceneEventConfig>) : null;
            if (evDic == null || evDic.Count == 0) return;

            List<sSceneEventConfig> allEv = new List<sSceneEventConfig>(evDic.Values);
            allEv.Sort(delegate(sSceneEventConfig a, sSceneEventConfig b) { return a.nID.CompareTo(b.nID); });

            // Group duplicates by non-zero ConditionFinish (choosing hub / root scene event as canonical)
            Dictionary<int, List<sSceneEventConfig>> byCf = new Dictionary<int, List<sSceneEventConfig>>();
            for (int i = 0; i < allEv.Count; i++)
            {
                sSceneEventConfig ev = allEv[i];
                if (ev == null || ev.nID <= 0) continue;
                int cfKey = (ev.nConditionFinish > 0) ? ev.nConditionFinish : (-ev.nID);
                List<sSceneEventConfig> grp = null;
                if (!byCf.TryGetValue(cfKey, out grp))
                {
                    grp = new List<sSceneEventConfig>();
                    byCf[cfKey] = grp;
                }
                grp.Add(ev);
            }

            foreach (KeyValuePair<int, List<sSceneEventConfig>> kv in byCf)
            {
                List<sSceneEventConfig> grp = kv.Value;
                sSceneEventConfig best = grp[0];
                int bestScore = int.MaxValue;
                for (int i = 0; i < grp.Count; i++)
                {
                    sSceneEventConfig cand = grp[i];
                    sSceneConfig sc = (sceneDic != null && sceneDic.ContainsKey(cand.nBelongScene)) ? sceneDic[cand.nBelongScene] : null;
                    int score = cand.nID;
                    if (cand.nBelongScene == 871) score -= 900000;
                    else if (cand.nBelongScene == 718 || cand.nBelongScene == 200 || cand.nBelongScene == 750 || cand.nBelongScene == 522 || cand.nBelongScene == 904) score -= 700000;
                    else if (sc != null && sc.nBelongScene == 0) score -= 400000;
                    if (cand.nBelongScene == 0) score += 800000;
                    if (score < bestScore) { bestScore = score; best = cand; }
                }
                canonList.Add(best);
                _canonEventsCache[best.nID] = best;

                List<int> sceneList = new List<int>();
                for (int i = 0; i < grp.Count; i++)
                {
                    int bsid = grp[i].nBelongScene;
                    if (bsid > 0 && !sceneList.Contains(bsid)) sceneList.Add(bsid);
                }
                _canonMirrorsCache[best.nID] = sceneList;
            }
            canonList.Sort(delegate(sSceneEventConfig a, sSceneEventConfig b) { return a.nID.CompareTo(b.nID); });

            // Build lookup: (fsmCid, finishState) -> List of canonical event IDs
            Dictionary<long, List<int>> fsmProducer = new Dictionary<long, List<int>>();
            for (int i = 0; i < canonList.Count; i++)
            {
                sSceneEventConfig ev = canonList[i];
                sConditionConfigGroup grp = (ev.nConditionFinish > 0) ? cl.GetConditionGroup(ev.nConditionFinish) : null;
                if (grp == null || grp.listConfig == null) continue;
                for (int k = 0; k < grp.listConfig.Count; k++)
                {
                    sConditionConfig c = grp.listConfig[k];
                    int cid = 0, st = 0;
                    if (c != null && c.eType == sConditionConfig.eConditionType.FSM_EqualAndBigger &&
                        int.TryParse(c.szParam_1, out cid) && int.TryParse(c.szParam_2, out st) && cid > 0 && st > 0)
                    {
                        long key = (((long)cid) << 32) | (uint)st;
                        List<int> plist = null;
                        if (!fsmProducer.TryGetValue(key, out plist))
                        {
                            plist = new List<int>();
                            fsmProducer[key] = plist;
                        }
                        if (!plist.Contains(ev.nID)) plist.Add(ev.nID);
                    }
                }
            }

            // Link parent & prerequisite lists from ConditionUnlock
            for (int i = 0; i < canonList.Count; i++)
            {
                sSceneEventConfig ev = canonList[i];
                List<int> prereqs = new List<int>();
                int evCat = (sceneDic != null && sceneDic.ContainsKey(ev.nBelongScene)) ? GetSceneChapterCat(sceneDic[ev.nBelongScene]) : 1;
                sConditionConfigGroup ugrp = (ev.nConditionUnlock > 0) ? cl.GetConditionGroup(ev.nConditionUnlock) : null;
                if (ugrp != null && ugrp.listConfig != null)
                {
                    for (int k = 0; k < ugrp.listConfig.Count; k++)
                    {
                        sConditionConfig c = ugrp.listConfig[k];
                        int cid = 0, st = 0;
                        if (c != null && c.eType == sConditionConfig.eConditionType.FSM_EqualAndBigger &&
                            int.TryParse(c.szParam_1, out cid) && int.TryParse(c.szParam_2, out st) && cid > 0 && st > 0)
                        {
                            long key = (((long)cid) << 32) | (uint)st;
                            List<int> plist = null;
                            if (fsmProducer.TryGetValue(key, out plist))
                            {
                                for (int p = 0; p < plist.Count; p++)
                                {
                                    int pid = plist[p];
                                    if (pid != ev.nID && !prereqs.Contains(pid)) prereqs.Add(pid);
                                }
                            }
                        }
                    }
                }
                _taskPrereqCache[ev.nID] = prereqs;

                int bestParent = 0;
                int bestPScore = int.MaxValue;
                for (int p = 0; p < prereqs.Count; p++)
                {
                    int pid = prereqs[p];
                    sSceneEventConfig pev = _canonEventsCache[pid];
                    int pCat = (sceneDic != null && sceneDic.ContainsKey(pev.nBelongScene)) ? GetSceneChapterCat(sceneDic[pev.nBelongScene]) : 1;
                    int score = (pev.nBelongScene == ev.nBelongScene ? 0 : (pCat == evCat ? 1000 : 10000)) + (pid > ev.nID ? 50000 : 0) + p;
                    if (score < bestPScore) { bestPScore = score; bestParent = pid; }
                }
                _taskParentCache[ev.nID] = bestParent;
            }

            // Cycle-break + depth calculation
            for (int i = 0; i < canonList.Count; i++)
            {
                int eid = canonList[i].nID;
                List<int> chain = new List<int>();
                chain.Add(eid);
                int cur = _taskParentCache.ContainsKey(eid) ? _taskParentCache[eid] : 0;
                int prev = eid;
                while (cur > 0)
                {
                    if (chain.Contains(cur) || chain.Count > 12)
                    {
                        _taskParentCache[prev] = 0;
                        break;
                    }
                    chain.Add(cur);
                    prev = cur;
                    cur = _taskParentCache.ContainsKey(cur) ? _taskParentCache[cur] : 0;
                }
                int depth = 0;
                int dcCur = _taskParentCache.ContainsKey(eid) ? _taskParentCache[eid] : 0;
                while (dcCur > 0 && depth < 10)
                {
                    depth++;
                    dcCur = _taskParentCache.ContainsKey(dcCur) ? _taskParentCache[dcCur] : 0;
                }
                _taskDepthCache[eid] = depth;
            }
        }

        private void ExportDynTasks(IGameDataCenter dc, CLanguageMgr lm, string dir)
        {
            try
            {
                List<sSceneEventConfig> canonList = null;
                Dictionary<int, sSceneConfig> sceneDic = null;
                BuildTaskCatalogAndHierarchy(dc, out canonList, out sceneDic);
                if (canonList == null || canonList.Count == 0) return;

                CSceneConfigLoader sl = dc.GetConfigLoader(eGameConfigType.Scene) as CSceneConfigLoader;
                CConditionConfigLoader cl = dc.GetConfigLoader(eGameConfigType.Condition) as CConditionConfigLoader;
                CMaterialConfigLoader ml = dc.GetConfigLoader(eGameConfigType.Begin) as CMaterialConfigLoader;
                CUnitConfigLoader ul = dc.GetConfigLoader(eGameConfigType.Unit) as CUnitConfigLoader;

                StringBuilder sb = new StringBuilder(canonList.Count * 220);
                sb.Append("{\"events\":[");
                for (int i = 0; i < canonList.Count; i++)
                {
                    sSceneEventConfig ev = canonList[i];
                    if (i > 0) sb.Append(',');
                    string nm = !string.IsNullOrEmpty(ev.szStringName) ? ev.szStringName : (ev.nStringName > 0 && lm != null ? lm.GetString(ev.nStringName) : ("Event #" + ev.nID));
                    string ds = !string.IsNullOrEmpty(ev.szStringDesc) ? ev.szStringDesc : (ev.nStringDesc > 0 && lm != null ? lm.GetString(ev.nStringDesc) : "");
                    string rw = !string.IsNullOrEmpty(ev.szStringReward) ? ev.szStringReward : (ev.nStringReward > 0 && lm != null ? lm.GetString(ev.nStringReward) : "");

                    int bsid = ev.nBelongScene;
                    sSceneConfig bsc = (sl != null && bsid > 0) ? sl.GetConfig(bsid) : null;
                    string bsn = (bsc != null && !string.IsNullOrEmpty(bsc.szStringName)) ? bsc.szStringName : (bsid > 0 ? ("#" + bsid) : "通用/剧情");
                    int rootId = (sceneDic != null && bsid > 0) ? ResolveRootScene(sceneDic, bsid) : bsid;
                    sSceneConfig rsc = (sl != null && rootId > 0) ? sl.GetConfig(rootId) : null;
                    string rsn = (rsc != null && !string.IsNullOrEmpty(rsc.szStringName)) ? rsc.szStringName : bsn;
                    int cat = (bsc != null) ? GetSceneChapterCat(bsc) : 1;
                    if (bsid == 0) cat = 1;

                    StringBuilder mnames = new StringBuilder();
                    List<int> ms = null;
                    if (_canonMirrorsCache != null && _canonMirrorsCache.TryGetValue(ev.nID, out ms))
                    {
                        for (int m = 0; m < ms.Count; m++)
                        {
                            sSceneConfig msc = (sl != null) ? sl.GetConfig(ms[m]) : null;
                            if (m > 0) mnames.Append(" / ");
                            mnames.Append(msc != null && !string.IsNullOrEmpty(msc.szStringName) ? msc.szStringName : ("#" + ms[m]));
                            mnames.Append("(#").Append(ms[m]).Append(')');
                        }
                    }

                    string condUnlockStr = FormatConditionGroupDesc(cl, ev.nConditionUnlock, sl, ml, ul, lm);
                    string condFinishStr = FormatConditionGroupDesc(cl, ev.nConditionFinish, sl, ml, ul, lm);
                    string condShowStr = FormatConditionGroupDesc(cl, ev.nConditionShow, sl, ml, ul, lm);

                    int parentId = (_taskParentCache != null && _taskParentCache.ContainsKey(ev.nID)) ? _taskParentCache[ev.nID] : 0;
                    int depth = (_taskDepthCache != null && _taskDepthCache.ContainsKey(ev.nID)) ? _taskDepthCache[ev.nID] : 0;

                    sb.Append("{\"id\":").Append(ev.nID);
                    sb.Append(",\"cf\":").Append(ev.nConditionFinish);
                    sb.Append(",\"cu\":").Append(ev.nConditionUnlock);
                    sb.Append(",\"cs\":").Append(ev.nConditionShow);
                    sb.Append(",\"name\":\"").Append(JsonEsc(nm)).Append('"');
                    sb.Append(",\"desc\":\"").Append(JsonEsc(ds)).Append('"');
                    sb.Append(",\"reward\":\"").Append(JsonEsc(rw)).Append('"');
                    sb.Append(",\"scene\":").Append(bsid);
                    sb.Append(",\"sceneName\":\"").Append(JsonEsc(bsn)).Append('"');
                    sb.Append(",\"root\":").Append(rootId);
                    sb.Append(",\"rootName\":\"").Append(JsonEsc(rsn)).Append('"');
                    sb.Append(",\"scenes\":\"").Append(JsonEsc(mnames.ToString())).Append('"');
                    sb.Append(",\"cat\":").Append(cat);
                    sb.Append(",\"parent\":").Append(parentId);
                    sb.Append(",\"depth\":").Append(depth);
                    sb.Append(",\"unlockTxt\":\"").Append(JsonEsc(condUnlockStr)).Append('"');
                    sb.Append(",\"finishTxt\":\"").Append(JsonEsc(condFinishStr)).Append('"');
                    sb.Append(",\"showTxt\":\"").Append(JsonEsc(condShowStr)).Append("\"}");
                }
                sb.Append("]}");
                WriteAtomicText(dir + "/dyn_tasks.json", sb.ToString());
                Debug.Log("[ModBridge] Exported dyn_tasks.json canonEvents=" + canonList.Count);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModBridge] ExportDynTasks ex: " + ex.Message);
            }
        }

        private unsafe void ExportDynTaskState(IGameSystem gs, int cid, bool force)
        {
            try
            {
                string dir = ReadShmDynDir(Shm());
                if (string.IsNullOrEmpty(dir)) return;
                if (_canonEventsCache == null || _canonEventsCache.Count == 0)
                {
                    if (CClientGlobal.Instance != null && CClientGlobal.Instance.GetGameDataCenter != null)
                    {
                        List<sSceneEventConfig> tmpCanon = null;
                        Dictionary<int, sSceneConfig> tmpScene = null;
                        BuildTaskCatalogAndHierarchy(CClientGlobal.Instance.GetGameDataCenter, out tmpCanon, out tmpScene);
                    }
                }
                if (_canonEventsCache == null || _canonEventsCache.Count == 0) return;

                ulong playerUid = (CGameSceneMgr.Instance != null) ? CGameSceneMgr.Instance.GetCurrentCharacterUID() : 0UL;
                CConditionFactory cfFac = CConditionFactory.Instance;
                CConditionConfigLoader cl = (CClientGlobal.Instance != null && CClientGlobal.Instance.GetGameDataCenter != null)
                    ? (CClientGlobal.Instance.GetGameDataCenter.GetConfigLoader(eGameConfigType.Condition) as CConditionConfigLoader) : null;

                List<int> keys = new List<int>(_canonEventsCache.Keys);
                keys.Sort();

                StringBuilder sb = new StringBuilder(keys.Count * 28);
                sb.Append("{\"cid\":").Append(cid).Append(",\"events\":[");
                for (int i = 0; i < keys.Count; i++)
                {
                    sSceneEventConfig ev = _canonEventsCache[keys[i]];
                    bool unlocked = (ev.nConditionUnlock == 0) || (gs != null && cfFac != null && cfFac.JudgeGroup(playerUid, ev.nConditionUnlock));
                    bool shown = (ev.nConditionShow == 0) || (gs != null && cfFac != null && cfFac.JudgeGroup(playerUid, ev.nConditionShow));
                    bool finished = false;
                    if (gs != null && cfFac != null && ev.nConditionFinish > 0)
                    {
                        sConditionConfigGroup fgrp = (cl != null) ? cl.GetConditionGroup(ev.nConditionFinish) : null;
                        // Empty Param_1/2 rows should not auto-report finished unless real condition is judged true
                        if (fgrp != null && fgrp.listConfig != null && fgrp.listConfig.Count > 0)
                        {
                            bool hasRealCond = false;
                            for (int k = 0; k < fgrp.listConfig.Count; k++)
                            {
                                if (!string.IsNullOrEmpty(fgrp.listConfig[k].szParam_1)) { hasRealCond = true; break; }
                            }
                            if (hasRealCond) finished = cfFac.JudgeGroup(playerUid, ev.nConditionFinish);
                        }
                    }

                    // Live FSM / Kill / Mat summary value for display
                    string liveProg = "";
                    if (gs != null && cl != null && ev.nConditionFinish > 0)
                    {
                        sConditionConfigGroup fgrp = cl.GetConditionGroup(ev.nConditionFinish);
                        if (fgrp != null && fgrp.listConfig != null && fgrp.listConfig.Count > 1)
                        {
                            int sat = 0;
                            for (int k = 0; k < fgrp.listConfig.Count; k++)
                            {
                                if (cfFac != null && cfFac.Judge(playerUid, fgrp.listConfig[k])) sat++;
                            }
                            liveProg = "条件 " + sat + "/" + fgrp.listConfig.Count;
                        }
                        else if (fgrp != null && fgrp.listConfig != null && fgrp.listConfig.Count == 1)
                        {
                            sConditionConfig c0 = fgrp.listConfig[0];
                            int fcid = 0, fst = 0, kcid = 0, kcnt = 0;
                            if (c0.eType == sConditionConfig.eConditionType.FSM_EqualAndBigger && int.TryParse(c0.szParam_1, out fcid) && int.TryParse(c0.szParam_2, out fst) && fcid > 0)
                            {
                                CFSMSystemData fd = (gs.FSMSystem != null) ? gs.FSMSystem.GetData(fcid) : null;
                                liveProg = "FSM " + (fd != null ? fd.nStateID : 0) + "/" + fst;
                            }
                            else if (c0.eType == sConditionConfig.eConditionType.KillCount && int.TryParse(c0.szParam_1, out kcid) && int.TryParse(c0.szParam_2, out kcnt))
                            {
                                int curK = (gs.killCountSystem != null) ? gs.killCountSystem.GetDictionaryByID(kcid) : 0;
                                liveProg = "击杀 " + curK + "/" + kcnt;
                            }
                        }
                    }

                    if (i > 0) sb.Append(',');
                    sb.Append('[').Append(ev.nID)
                      .Append(',').Append(unlocked ? 1 : 0)
                      .Append(',').Append(shown ? 1 : 0)
                      .Append(',').Append(finished ? 1 : 0)
                      .Append(",\"").Append(JsonEsc(liveProg)).Append("\"]");
                }
                sb.Append("]}");
                string json = sb.ToString();
                if (!force && json == _lastTaskStateJson) return;
                _lastTaskStateJson = json;
                WriteAtomicText(dir + "/dyn_task_state.json", json);
            }
            catch { }
        }

        private static CFSMSystemData EnsureFsmData(IFSMSystem fs, int customID)
        {
            if (fs == null || customID <= 0) return null;
            CFSMSystemData d = fs.GetData(customID);
            if (d != null) return d;
            try
            {
                d = new CFSMSystemData();
                FieldInfo fCid = typeof(CFSMSystemData).GetField("m_nCustomID", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (fCid != null) fCid.SetValue(d, customID);
                PropertyInfo pCid = typeof(CFSMSystemData).GetProperty("nCustomID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pCid != null && pCid.CanWrite) pCid.SetValue(d, customID, null);

                FieldInfo fDic = fs.GetType().GetField("m_dicData", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                Dictionary<int, CFSMSystemData> dic = (fDic != null) ? (fDic.GetValue(fs) as Dictionary<int, CFSMSystemData>) : null;
                if (dic != null) dic[customID] = d;
            }
            catch { }
            return d;
        }

        private static void ApplyFsmStateNative(IGameSystem gs, int customID, int targetState, bool onlyRaise)
        {
            if (gs == null || gs.FSMSystem == null || customID <= 0) return;

            // 1) If the FSM object is live in current scene, drive native CFSMPart.GotoTargetState first
            bool liveHandled = false;
            try
            {
                if (CGameSceneMgr.Instance != null)
                {
                    CEntity ent = CGameSceneMgr.Instance.GetEntityByCustomID(customID);
                    if (ent != null)
                    {
                        CFSMPart fsm = ent.GetComponent(typeof(CFSMPart)) as CFSMPart;
                        if (fsm != null && (!onlyRaise || fsm.GetCurrentStateID() < targetState))
                        {
                            for (int i = 0; i < fsm.m_listState.Count; i++)
                            {
                                if (fsm.m_listState[i].nStateID == targetState)
                                {
                                    fsm.GotoTargetState(targetState);
                                    liveHandled = true;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            // 2) Ensure persistent CFSMSystemData + fire native FSM_ChangeState event even off-scene
            CFSMSystemData data = EnsureFsmData(gs.FSMSystem, customID);
            if (data != null && (!onlyRaise || data.nStateID < targetState))
            {
                int oldState = data.nStateID;
                if (oldState != targetState)
                {
                    data.SetState(targetState);
                    if (!liveHandled)
                    {
                        try
                        {
                            eMSG.sFSM_ChangeState msg = default(eMSG.sFSM_ChangeState);
                            msg.nCustomID = customID;
                            msg.nOldState = oldState;
                            msg.nNewState = targetState;
                            CClientGlobal.Instance.GetEventSystem.FireExecoute(eMSG.EVENT.FSM_ChangeState, 0, msg);
                        }
                        catch { }
                    }
                }
            }
        }

        private void ApplyConditionGroupNative(IGameSystem gs, int cid, int groupId, bool onlyRaise)
        {
            if (gs == null || groupId <= 0 || CClientGlobal.Instance == null || CClientGlobal.Instance.GetGameDataCenter == null) return;
            CConditionConfigLoader cl = CClientGlobal.Instance.GetGameDataCenter.GetConfigLoader(eGameConfigType.Condition) as CConditionConfigLoader;
            sConditionConfigGroup grp = (cl != null) ? cl.GetConditionGroup(groupId) : null;
            if (grp == null || grp.listConfig == null) return;

            for (int i = 0; i < grp.listConfig.Count; i++)
            {
                sConditionConfig c = grp.listConfig[i];
                if (c == null) continue;
                int p1 = 0, p2 = 0;
                int.TryParse(c.szParam_1 ?? "", out p1);
                int.TryParse(c.szParam_2 ?? "", out p2);
                switch (c.eType)
                {
                    case sConditionConfig.eConditionType.FSM_EqualAndBigger:
                        if (p1 > 0) ApplyFsmStateNative(gs, p1, p2, onlyRaise);
                        break;
                    case sConditionConfig.eConditionType.SceneArrive:
                        if (p1 > 0 && gs.sceneSystem != null)
                        {
                            CScene sc = gs.sceneSystem.GetScene(p1);
                            if (sc != null) sc.RecordArrived();
                        }
                        break;
                    case sConditionConfig.eConditionType.SceneOwn:
                        if (p1 > 0 && gs.sceneSystem != null)
                        {
                            CScene sc = gs.sceneSystem.GetScene(p1);
                            if (sc != null) sc.SetOwn(p2 == 1);
                        }
                        break;
                    case sConditionConfig.eConditionType.InformationTalkFinish:
                        if (p1 > 0 && gs.informationSystem != null)
                        {
                            gs.informationSystem.FinishTalk(p1);
                        }
                        break;
                    case sConditionConfig.eConditionType.Talk:
                        if (p1 > 0 && gs.talkSystem != null)
                        {
                            InvokeNamedMethod(gs.talkSystem, "FinishTalk", p1);
                        }
                        break;
                    case sConditionConfig.eConditionType.KillCount:
                        if (p1 > 0 && p2 > 0 && gs.killCountSystem != null)
                        {
                            Dictionary<int, int> kdic = gs.killCountSystem.GetDictionary();
                            if (kdic != null && !kdic.ContainsKey(p1)) kdic[p1] = 0;
                            int curK = gs.killCountSystem.GetDictionaryByID(p1);
                            if (curK < p2)
                            {
                                eMSG.sInformation_BossKilled bk = default(eMSG.sInformation_BossKilled);
                                bk.bossID = p1;
                                bk.bossCount = p2 - curK;
                                CClientGlobal.Instance.GetEventSystem.FireExecoute(eMSG.EVENT.Boss_Killed, 0, bk);
                            }
                        }
                        break;
                    case sConditionConfig.eConditionType.Materiel:
                        if (p1 > 0 && p2 > 0 && gs.containerSystem != null && cid != 0)
                        {
                            int have = gs.containerSystem.GetMaterialNumInContainer(cid, eContainerTypeEnum.Bag, p1);
                            if (have < p2) CmdAddItem(p1, p2 - have, 0, 0L, gs, cid);
                        }
                        break;
                }
            }
        }

        private int FindBestResetFsmState(sSceneEventConfig ev, int fsmCid, int finishThreshold, CConditionConfigLoader cl)
        {
            int bestBelow = 0;
            if (cl != null && ev != null)
            {
                // 1) Prefer same-FSM unlock/show threshold from this event so task stays active & unlocked
                int[] ownGrps = new int[] { ev.nConditionUnlock, ev.nConditionShow };
                for (int g = 0; g < ownGrps.Length; g++)
                {
                    if (ownGrps[g] <= 0) continue;
                    sConditionConfigGroup grp = cl.GetConditionGroup(ownGrps[g]);
                    if (grp == null || grp.listConfig == null) continue;
                    for (int i = 0; i < grp.listConfig.Count; i++)
                    {
                        sConditionConfig c = grp.listConfig[i];
                        int c1 = 0, s1 = 0;
                        if (c != null && c.eType == sConditionConfig.eConditionType.FSM_EqualAndBigger &&
                            int.TryParse(c.szParam_1, out c1) && int.TryParse(c.szParam_2, out s1) &&
                            c1 == fsmCid && s1 < finishThreshold && s1 > bestBelow)
                        {
                            bestBelow = s1;
                        }
                    }
                }
                // 2) Also preserve any earlier milestone on this same FSM across other events
                if (_canonEventsCache != null)
                {
                    foreach (sSceneEventConfig other in _canonEventsCache.Values)
                    {
                        if (other.nConditionFinish <= 0) continue;
                        sConditionConfigGroup fgrp = cl.GetConditionGroup(other.nConditionFinish);
                        if (fgrp == null || fgrp.listConfig == null) continue;
                        for (int i = 0; i < fgrp.listConfig.Count; i++)
                        {
                            sConditionConfig c = fgrp.listConfig[i];
                            int c1 = 0, s1 = 0;
                            if (c != null && c.eType == sConditionConfig.eConditionType.FSM_EqualAndBigger &&
                                int.TryParse(c.szParam_1, out c1) && int.TryParse(c.szParam_2, out s1) &&
                                c1 == fsmCid && s1 < finishThreshold && s1 > bestBelow)
                            {
                                bestBelow = s1;
                            }
                        }
                    }
                }
            }
            if (bestBelow == 0 && CGameSceneMgr.Instance != null)
            {
                try
                {
                    CEntity ent = CGameSceneMgr.Instance.GetEntityByCustomID(fsmCid);
                    CFSMPart fsm = (ent != null) ? (ent.GetComponent(typeof(CFSMPart)) as CFSMPart) : null;
                    if (fsm != null && fsm.m_listState.Count > 0 && fsm.m_listState[0].nStateID < finishThreshold)
                    {
                        bestBelow = fsm.m_listState[0].nStateID;
                    }
                }
                catch { }
            }
            return bestBelow;
        }

        private void CompleteEventNative(IGameSystem gs, int cid, int eventId, bool withPrereqs, Dictionary<int, bool> visited)
        {
            sSceneEventConfig ev = null;
            if (_canonEventsCache == null || !_canonEventsCache.TryGetValue(eventId, out ev)) return;
            if (visited.ContainsKey(eventId)) return;
            visited[eventId] = true;

            List<int> prereqs = null;
            if (withPrereqs && _taskPrereqCache != null && _taskPrereqCache.TryGetValue(eventId, out prereqs))
            {
                for (int i = 0; i < prereqs.Count; i++)
                    CompleteEventNative(gs, cid, prereqs[i], true, visited);
            }

            if (ev.nConditionShow > 0) ApplyConditionGroupNative(gs, cid, ev.nConditionShow, true);
            if (ev.nConditionUnlock > 0) ApplyConditionGroupNative(gs, cid, ev.nConditionUnlock, true);
            if (ev.nConditionFinish > 0) ApplyConditionGroupNative(gs, cid, ev.nConditionFinish, true);
        }

        private void UncompleteEventNative(IGameSystem gs, int eventId)
        {
            sSceneEventConfig ev = null;
            if (gs == null || _canonEventsCache == null || !_canonEventsCache.TryGetValue(eventId, out ev)) return;
            if (ev.nConditionFinish <= 0 || CClientGlobal.Instance == null || CClientGlobal.Instance.GetGameDataCenter == null) return;
            CConditionConfigLoader cl = CClientGlobal.Instance.GetGameDataCenter.GetConfigLoader(eGameConfigType.Condition) as CConditionConfigLoader;
            sConditionConfigGroup grp = (cl != null) ? cl.GetConditionGroup(ev.nConditionFinish) : null;
            if (grp == null || grp.listConfig == null) return;

            for (int i = 0; i < grp.listConfig.Count; i++)
            {
                sConditionConfig c = grp.listConfig[i];
                if (c == null) continue;
                int p1 = 0, p2 = 0;
                int.TryParse(c.szParam_1 ?? "", out p1);
                int.TryParse(c.szParam_2 ?? "", out p2);
                switch (c.eType)
                {
                    case sConditionConfig.eConditionType.FSM_EqualAndBigger:
                        if (p1 > 0)
                        {
                            int resetState = FindBestResetFsmState(ev, p1, p2, cl);
                            ApplyFsmStateNative(gs, p1, resetState, false);
                        }
                        break;
                    case sConditionConfig.eConditionType.KillCount:
                        if (p1 > 0 && gs.killCountSystem != null)
                        {
                            Dictionary<int, int> kdic = gs.killCountSystem.GetDictionary();
                            if (kdic != null && kdic.ContainsKey(p1) && kdic[p1] >= p2)
                                kdic[p1] = Mathf.Max(0, p2 - 1);
                        }
                        break;
                    case sConditionConfig.eConditionType.InformationTalkFinish:
                        if (p1 > 0 && gs.informationSystem != null)
                        {
                            FieldInfo fi = gs.informationSystem.GetType().GetField("m_listFinishTalk", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                            List<int> fl = (fi != null) ? (fi.GetValue(gs.informationSystem) as List<int>) : null;
                            if (fl != null) fl.Remove(p1);
                        }
                        break;
                    case sConditionConfig.eConditionType.SceneOwn:
                        if (p1 > 0 && gs.sceneSystem != null)
                        {
                            CScene sc = gs.sceneSystem.GetScene(p1);
                            if (sc != null) sc.SetOwn(false);
                        }
                        break;
                    case sConditionConfig.eConditionType.SceneArrive:
                        if (p1 > 0 && gs.sceneSystem != null)
                        {
                            CScene sc = gs.sceneSystem.GetScene(p1);
                            if (sc != null)
                            {
                                FieldInfo fiA = typeof(CScene).GetField("<bArrived>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                                                ?? typeof(CScene).GetField("m_bArrived", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                                if (fiA != null) fiA.SetValue(sc, false);
                            }
                        }
                        break;
                }
            }
        }

        private int CmdTaskOp(IGameSystem gs, int cid, int subOp, int eventId, int prereqFlag, int groupOrSceneId)
        {
            if (gs == null || CClientGlobal.Instance == null || CClientGlobal.Instance.GetGameDataCenter == null) return -2;
            try
            {
                if (_canonEventsCache == null || _canonEventsCache.Count == 0)
                {
                    List<sSceneEventConfig> tmpCanon = null;
                    Dictionary<int, sSceneConfig> tmpScene = null;
                    BuildTaskCatalogAndHierarchy(CClientGlobal.Instance.GetGameDataCenter, out tmpCanon, out tmpScene);
                }

                if (subOp == 1) // Complete single SceneEvent (with prerequisites by default)
                {
                    sSceneEventConfig ev = null;
                    if (!_canonEventsCache.TryGetValue(eventId, out ev)) return -2;
                    CompleteEventNative(gs, cid, eventId, prereqFlag != 0, new Dictionary<int, bool>());
                    TriggerSceneSpawnersUpdate();
                    ExportDynTaskState(gs, cid, true);
                    ExportDynMapState(gs, true);
                    SetStatus(1, "Task Done: " + (ev.szStringName ?? ("#" + eventId)));
                    return 1;
                }
                if (subOp == 2) // Reset single SceneEvent to uncompleted
                {
                    sSceneEventConfig ev = null;
                    if (!_canonEventsCache.TryGetValue(eventId, out ev)) return -2;
                    UncompleteEventNative(gs, eventId);
                    TriggerSceneSpawnersUpdate();
                    ExportDynTaskState(gs, cid, true);
                    ExportDynMapState(gs, true);
                    SetStatus(1, "Task Reset: " + (ev.szStringName ?? ("#" + eventId)));
                    return 1;
                }
                if (subOp == 5 || subOp == 6) // Batch Complete(5) / Reset(6) all tasks belonging to rootScene or chapter
                {
                    int targetRootOrCat = (groupOrSceneId != 0) ? groupOrSceneId : eventId;
                    Dictionary<int, bool> vis = new Dictionary<int, bool>();
                    int cnt = 0;
                    CSceneConfigLoader sl = CClientGlobal.Instance.GetGameDataCenter.GetConfigLoader(eGameConfigType.Scene) as CSceneConfigLoader;
                    FieldInfo fiSd = (sl != null) ? typeof(CSceneConfigLoader).GetField("m_dicScene", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) : null;
                    Dictionary<int, sSceneConfig> sd = (fiSd != null) ? (fiSd.GetValue(sl) as Dictionary<int, sSceneConfig>) : null;
                    foreach (sSceneEventConfig ev in _canonEventsCache.Values)
                    {
                        int root = (sd != null && ev.nBelongScene > 0) ? ResolveRootScene(sd, ev.nBelongScene) : ev.nBelongScene;
                        if (targetRootOrCat == -1 || ev.nBelongScene == targetRootOrCat || root == targetRootOrCat)
                        {
                            if (subOp == 5) CompleteEventNative(gs, cid, ev.nID, prereqFlag != 0, vis);
                            else UncompleteEventNative(gs, ev.nID);
                            cnt++;
                        }
                    }
                    TriggerSceneSpawnersUpdate();
                    ExportDynTaskState(gs, cid, true);
                    ExportDynMapState(gs, true);
                    SetStatus(1, (subOp == 5 ? "Batch Done " : "Batch Reset ") + cnt + " tasks");
                    return 1;
                }
                return -1;
            }
            catch (Exception ex)
            {
                SetStatus(-99, "TaskOp ex: " + ex.Message);
                Debug.LogWarning("[ModBridge] CmdTaskOp ex: " + ex.Message);
                return -99;
            }
        }

        // ==================== #26 Entity Catalog, Spawner, Cricket Duel & Global Scrambler ====================
        private static MethodInfo s_miCheckTriggerDead = null;
        private static MethodInfo s_miCheckTriggerDeadAct = null;

        private static void EnsureChildDeadReflection()
        {
            if (s_miCheckTriggerDead == null)
                s_miCheckTriggerDead = typeof(CFSMTriggerListener_AllChildUnitDead).GetMethod("CheckTrigger", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (s_miCheckTriggerDeadAct == null)
                s_miCheckTriggerDeadAct = typeof(CFSMTriggerListener_AllChildUnitDeadAndActive).GetMethod("CheckTrigger", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        }

        private void ReleaseTempYooHandles()
        {
            for (int i = 0; i < _sceneTempYooHandles.Count; i++)
            {
                if (_sceneTempYooHandles[i] != null) ReleaseYooHandle(_sceneTempYooHandles[i]);
            }
            _sceneTempYooHandles.Clear();
        }

        private void DespawnAllCustomEntities(bool releaseHandles)
        {
            try
            {
                if (_spawnedCustomEntities.Count > 0)
                {
                    foreach (KeyValuePair<int, List<CEntity>> kv in _spawnedCustomEntities)
                    {
                        List<CEntity> list = kv.Value;
                        if (list == null) continue;
                        for (int i = 0; i < list.Count; i++)
                        {
                            CEntity e = list[i];
                            if (e != null && e.gameObject != null)
                                SafeTeardownSpawnedObject(e.gameObject);
                        }
                    }
                    _spawnedCustomEntities.Clear();
                }
            }
            catch { }
            if (releaseHandles) ReleaseTempYooHandles();
        }

        private void ClearCricketArena(bool releaseHandles)
        {
            try
            {
                if (_cricketFighterA != null && _cricketFighterA.gameObject != null)
                    SafeTeardownSpawnedObject(_cricketFighterA.gameObject);
            }
            catch { }
            try
            {
                if (_cricketFighterB != null && _cricketFighterB.gameObject != null)
                    SafeTeardownSpawnedObject(_cricketFighterB.gameObject);
            }
            catch { }
            _cricketFighterA = null;
            _cricketFighterB = null;
            _cricketNameA = "";
            _cricketNameB = "";
            _cricketAnnouncedEnd = false;
            if (releaseHandles) ReleaseTempYooHandles();
        }

        private static string GetWorkerTypeLabel(int wt)
        {
            switch (wt)
            {
                case 1: return "商人";
                case 2: return "伐木工";
                case 3: return "铁矿工";
                case 4: return "营地战士";
                case 5: return "服务员";
                case 6: return "硝石矿工";
                default: return "营地工人";
            }
        }

        private static int ResolveCampFaction(IGameDataCenter dc, int campId)
        {
            if (campId == 0) return 3;
            if (campId == -9999 || campId < 0) return 0;
            try
            {
                CCampConfigLoader cl = (dc != null) ? (dc.GetConfigLoader(eGameConfigType.Camp) as CCampConfigLoader) : null;
                if (cl != null)
                {
                    sCampConfig c0 = cl.GetConfig(0);
                    sCampConfig cu = cl.GetConfig(campId);
                    if ((c0 != null && c0.listEnemy != null && c0.listEnemy.Contains(campId)) ||
                        (cu != null && cu.listEnemy != null && cu.listEnemy.Contains(0)))
                        return 1;
                    if ((c0 != null && c0.listFriend != null && c0.listFriend.Contains(campId)) ||
                        (cu != null && cu.listFriend != null && cu.listFriend.Contains(0)))
                        return 3;
                    if (c0 != null || cu != null)
                        return 2;
                }
            }
            catch { }
            if (campId == 1916) return 1;
            return 2;
        }

        private int ResolveUnitNativeFaction(IGameDataCenter dc, int unitId, int campId, int cls, string ap, string nm, Dictionary<int, bool> hireIds)
        {
            if (cls == 3 || (hireIds != null && hireIds.ContainsKey(unitId))) return 3;
            string lp = (ap != null) ? ap.ToLowerInvariant() : "";
            string uName = nm ?? "";

            if (lp.Contains("/guyong") || lp.Contains("/dlc/team_") || lp.Contains("/hire/") ||
                lp.Contains("_zhaohuan") || lp.Contains("_zhaomu") || lp.Contains("jinglingqiu") ||
                lp.Contains("tafang_") || uName.Contains("[召唤]") || uName.Contains("[祥瑞]"))
                return 3;

            if (lp.Contains("/guaiwu/shengwu_") || lp.Contains("/animal_") || lp.Contains("monster_pets") ||
                uName.Contains("[养殖]") || uName.Contains("[猎物]"))
                return 2;

            if (campId != -9999 && campId != 0)
            {
                int byCamp = ResolveCampFaction(dc, campId);
                if (byCamp != 0) return byCamp;
            }

            if (campId == 0)
            {
                if (cls == 1)
                {
                    if (lp.Contains("/npc/")) return 3;
                    return 1; // scene-assigned enemy prefabs (e.g. 1164, 1763) have default camp 0 on template
                }
                if (cls == 2)
                {
                    if (lp.Contains("/enemy/")) return 1;
                    return 3;
                }
            }

            if (cls == 2) return lp.Contains("/enemy/") ? 1 : 3;
            if (uName == "鸡" || uName == "羊" || uName == "兔子" || uName == "小猪") return 2;
            return 1;
        }

        private static string ClassifyUnitSubCategory(int unitId, int cls, int wtype, string ap, int faction, Dictionary<int, bool> hireIds)
        {
            if (cls == 3) return GetWorkerTypeLabel(wtype);
            if (hireIds != null && hireIds.ContainsKey(unitId)) return "剧情随从";

            string lp = (ap != null) ? ap.ToLowerInvariant() : "";
            if (lp.Contains("/guyong/") || lp.Contains("_zhaomu") || lp.Contains("_zhaohuan") || lp.Contains("jinglingqiu")) return "剧情/召唤随从";
            if (lp.Contains("/dlc/")) return "DLC·机械单位";
            if (lp.Contains("multiplayer_battle") || lp.Contains("/lan_") || lp.Contains("lan_boss") || lp.Contains("lan_lu")) return "联机/对抗单位";
            if (lp.Contains("tafang_") || lp.Contains("jiguan_")) return "塔防与机关";
            if (lp.Contains("wanshengjie") || lp.Contains("chunjie") || lp.Contains("zhounianqing") || lp.Contains("nianshou") || lp.Contains("/activity/")) return "节日与活动";
            if (lp.Contains("/guaiwu/shengwu_") || lp.Contains("/animal_") || lp.Contains("monster_pets")) return "野生生物";

            bool isBoss = lp.Contains("boss") || lp.Contains("jiachongwang") || lp.Contains("xinshouboss") ||
                          lp.Contains("huangsihai") || lp.Contains("dahaishe") || lp.Contains("haishewang") ||
                          lp.Contains("neng_boss") || lp.Contains("juneng") || lp.Contains("wannianguiwang");
            if (isBoss) return "首领 Boss";

            if (lp.Contains("ganranqu_301") || lp.Contains("zongbuqiang")) return "感染区·301所";
            if (lp.Contains("baozangdao") || lp.Contains("haidao")) return "感染区·海盗岛";
            if (lp.Contains("haiyang") || lp.Contains("meiyurendao") || lp.Contains("haidan") || lp.Contains("haishe") || lp.Contains("shayu") || lp.Contains("pangxie") || lp.Contains("haixing")) return "感染区·海域";
            if (lp.Contains("huaguoshan")) return "感染区·花果山";
            if (lp.Contains("huoqu") || lp.Contains("leiqu") || lp.Contains("neng_")) return "元素·火雷区";
            if (lp.Contains("ganranqu_yixue")) return "感染区·医学区";
            if (lp.Contains("huangchong_") || lp.Contains("tieren") || lp.Contains("yingyan")) return "帮派与武装";
            if (lp.Contains("ganranqu_") || lp.Contains("granqu_")) return "感染区变异";
            if (lp.Contains("bianyi_") || lp.Contains("flood_dragon") || lp.Contains("woniuzu")) return "野外变异怪";

            if (cls == 2)
                return (faction == 1) ? "敌对人类/匪徒" : "剧情与场景NPC";
            return (faction == 3) ? "友方/召唤单位" : (faction == 2 ? "野生/中立生物" : "常规战斗单位");
        }

        private string ResolveUnitDisplayName(IGameDataCenter dc, CLanguageMgr lm, int unitId)
        {
            if (dc != null)
            {
                try
                {
                    CUnitConfigLoader ul = dc.GetConfigLoader(eGameConfigType.Unit) as CUnitConfigLoader;
                    sUnitConfig uc = (ul != null) ? ul.GetConfig(unitId) : null;
                    if (uc != null)
                    {
                        string nm = !string.IsNullOrEmpty(uc.szStringName) ? uc.szStringName : (uc.nStringName > 0 && lm != null ? lm.GetString(uc.nStringName) : "");
                        nm = StripRichTags(nm);
                        if (!string.IsNullOrEmpty(nm)) return nm;
                    }
                }
                catch { }
            }
            return "Unit #" + unitId;
        }

        private void ExportDynUnits(IGameDataCenter dc, CLanguageMgr lm, string dir)
        {
            if (dc == null || string.IsNullOrEmpty(dir)) return;
            try
            {
                InitYooManifest(dir);

                BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                Dictionary<int, bool> hireIds = new Dictionary<int, bool>(32);
                object hl = dc.GetConfigLoader(eGameConfigType.Hire);
                if (hl != null)
                {
                    FieldInfo fiH = hl.GetType().GetField("m_dicConfigs", bf);
                    System.Collections.IDictionary hDic = (fiH != null) ? (fiH.GetValue(hl) as System.Collections.IDictionary) : null;
                    if (hDic != null)
                    {
                        foreach (System.Collections.DictionaryEntry kv in hDic)
                        {
                            int hid = GetObjInt(kv.Value, new string[] { "nID", "nId" }, 0);
                            if (hid > 0) hireIds[hid] = true;
                        }
                    }
                }

                Dictionary<int, int> workerTypeMap = new Dictionary<int, int>(64);
                Dictionary<int, int> workerHpMap = new Dictionary<int, int>(64);
                object wl = dc.GetConfigLoader(eGameConfigType.Worker);
                if (wl != null)
                {
                    FieldInfo fiW = wl.GetType().GetField("m_dicConfigs", bf);
                    System.Collections.IDictionary wDic = (fiW != null) ? (fiW.GetValue(wl) as System.Collections.IDictionary) : null;
                    if (wDic != null)
                    {
                        foreach (System.Collections.DictionaryEntry kv in wDic)
                        {
                            object wc = kv.Value;
                            if (wc == null) continue;
                            int wid = GetObjInt(wc, new string[] { "nID", "nId" }, 0);
                            if (wid <= 0) continue;
                            workerTypeMap[wid] = GetObjInt(wc, new string[] { "eEnum" }, 0);
                            workerHpMap[wid] = GetObjInt(wc, new string[] { "nHP" }, 0);
                        }
                    }
                }

                CUnitConfigLoader ul = dc.GetConfigLoader(eGameConfigType.Unit) as CUnitConfigLoader;
                if (ul == null) return;
                FieldInfo fiU = typeof(CUnitConfigLoader).GetField("m_dicConfigs", bf);
                Dictionary<int, sUnitConfig> dic = (fiU != null) ? (fiU.GetValue(ul) as Dictionary<int, sUnitConfig>) : null;
                if (dic == null || dic.Count == 0) return;

                List<sUnitConfig> list = new List<sUnitConfig>(dic.Values);
                list.Sort(delegate(sUnitConfig a, sUnitConfig b) { return a.nID.CompareTo(b.nID); });

                StringBuilder sb = new StringBuilder(list.Count * 130);
                sb.Append('[');
                bool first = true;
                for (int i = 0; i < list.Count; i++)
                {
                    sUnitConfig uc = list[i];
                    if (uc == null || uc.nID <= 0) continue;
                    int cls = (int)uc.eClass;
                    if (cls == 0) continue;

                    string nm = !string.IsNullOrEmpty(uc.szStringName) ? uc.szStringName : (uc.nStringName > 0 && lm != null ? lm.GetString(uc.nStringName) : "");
                    nm = StripRichTags(nm);
                    if (string.IsNullOrEmpty(nm)) continue;
                    string ds = !string.IsNullOrEmpty(uc.szStringDesc) ? uc.szStringDesc : (uc.nStringDesc > 0 && lm != null ? lm.GetString(uc.nStringDesc) : "");
                    ds = StripRichTags(ds);

                    string ap = null;
                    _unitPrefabPathCache.TryGetValue(uc.nID, out ap);
                    if (string.IsNullOrEmpty(ap))
                    {
                        string hint = ExtractStemHint(uc.szIcon);
                        if (hint.Length >= 3) _unitIconStemToPath.TryGetValue(hint, out ap);
                    }
                    if (string.IsNullOrEmpty(ap)) _unitNameToPath.TryGetValue(nm, out ap);

                    int wt = 0;
                    workerTypeMap.TryGetValue(uc.nID, out wt);
                    int whp = 0;
                    workerHpMap.TryGetValue(uc.nID, out whp);

                    int camp;
                    if (!_unitCampCache.TryGetValue(uc.nID, out camp)) camp = -9999;
                    int fac = ResolveUnitNativeFaction(dc, uc.nID, camp, cls, ap, nm, hireIds);
                    string sub = ClassifyUnitSubCategory(uc.nID, cls, wt, ap, fac, hireIds);

                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"id\":").Append(uc.nID);
                    sb.Append(",\"cls\":").Append(cls);
                    sb.Append(",\"wtype\":").Append(wt);
                    sb.Append(",\"fac\":").Append(fac);
                    sb.Append(",\"camp\":").Append(camp);
                    sb.Append(",\"exp\":").Append(uc.nEXP);
                    sb.Append(",\"hp\":").Append(whp);
                    sb.Append(",\"hasPrefab\":").Append(string.IsNullOrEmpty(ap) ? 0 : 1);
                    sb.Append(",\"name\":\"").Append(JsonEsc(nm)).Append('"');
                    sb.Append(",\"sub\":\"").Append(JsonEsc(sub)).Append('"');
                    sb.Append(",\"desc\":\"").Append(JsonEsc(ds)).Append('"');
                    sb.Append(",\"icon\":\"").Append(JsonEsc(uc.szIcon ?? "")).Append('"');
                    sb.Append('}');
                }
                sb.Append(']');
                WriteAtomicText(dir + "/dyn_units.json", sb.ToString());
                Debug.Log("[ModBridge] Exported dyn_units.json count=" + list.Count + " cachedCamps=" + _unitCampCache.Count);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModBridge] ExportDynUnits ex: " + ex.Message);
            }
        }

        private static void SanitizeSpawnedUnitClone(GameObject go, bool stripInteractive)
        {
            if (go == null) return;
            try
            {
                MonoBehaviour[] mbs = go.GetComponentsInChildren<MonoBehaviour>(true);
                if (mbs == null || mbs.Length == 0) return;
                CFightObj rootFo = go.GetComponent<CFightObj>();
                List<MonoBehaviour> toRemove = null;
                for (int i = 0; i < mbs.Length; i++)
                {
                    MonoBehaviour m = mbs[i];
                    if (m == null) continue;
                    if (m is CFSMPart)
                    {
                        SanitizeFsmPart((CFSMPart)m, false);
                        continue;
                    }
                    if (m is CActivator_SceneGrid || m is CActivator_SceneGridAndFightState ||
                        m is CFSMTriggerListener_Base ||
                        m is CFSMFunction_GoOutScene || m is CFSMFunction_PlayTimeline ||
                        m is CFSMFunction_ConditionSelect || m is CFSMFunction_DestroyEntity ||
                        m is CFSMFunction_HireAdd || m is CFSMFunction_HireRemove || m is CFSMFunction_RemoveHire ||
                        m is CFSMFunction_SetHPToMax ||
                        m is CColliderTrigger_PlayerEvent || m is CColliderTrigger_ByMonster ||
                        m is CColliderTriggerPart_UnitAddBuff)
                    {
                        if (toRemove == null) toRemove = new List<MonoBehaviour>();
                        toRemove.Add(m);
                        continue;
                    }
                    if (m.GetType() == typeof(CFSMFunction_Base))
                    {
                        if (toRemove == null) toRemove = new List<MonoBehaviour>();
                        toRemove.Add(m);
                        continue;
                    }
                    string tn = m.GetType().Name;
                    if (tn.StartsWith("CFSMFunction_OpenUI", StringComparison.Ordinal) ||
                        tn.StartsWith("CFSMFunction_KillCount", StringComparison.Ordinal) ||
                        tn == "CFSMFunction_RemoveTeamate" || tn == "CFSMFunction_RemoveWorker" ||
                        tn == "CColliderTriggerPart_CharacterAddBuff" ||
                        (stripInteractive && tn == "COperatePoint"))
                    {
                        if (toRemove == null) toRemove = new List<MonoBehaviour>();
                        toRemove.Add(m);
                    }
                }
                if (toRemove != null)
                {
                    for (int i = 0; i < toRemove.Count; i++)
                    {
                        try { UnityEngine.Object.DestroyImmediate(toRemove[i]); }
                        catch { try { toRemove[i].enabled = false; } catch { } }
                    }
                }
                if (rootFo != null) rootFo.m_bSendOSS = false;
            }
            catch { }
        }

        private static void ApplyEntityFactionConfig(CEntity ent, CFightObj fo, CEntity player, int faction, int nativeOrigCamp)
        {
            if (fo == null) return;
            int pCamp = (player != null && player.fightObj != null) ? player.fightObj.CampID : 0;
            if (faction == 3)
            {
                ConfigureFightObjAsPlayerAlly(fo, ent, player, null);
                return;
            }
            if (faction == 2)
            {
                fo.campID = (nativeOrigCamp > 0 && nativeOrigCamp != pCamp) ? nativeOrigCamp : 2;
                if (fo.dicCamp != null)
                {
                    CFightObj.sCampNode midNode = default(CFightObj.sCampNode);
                    midNode.nCampID = pCamp;
                    midNode.eOldCamp = CFightObj.eCampEnum.Middle;
                    midNode.eCurCamp = CFightObj.eCampEnum.Middle;
                    midNode.nResetTimeCount = 999999;
                    fo.dicCamp[pCamp] = midNode;
                }
                if (fo.dicTarget != null && player != null)
                    fo.dicTarget.Remove(player.UID);
                if (fo.GetCurTarget() == player)
                    fo.RemoveCurTarget();
                return;
            }
            fo.campID = (nativeOrigCamp > 0 && nativeOrigCamp != pCamp) ? nativeOrigCamp : 1916;
            if (fo.target_searchR < 16f) fo.target_searchR = 16f;
            if (fo.target_searchR_Fight < 18f) fo.target_searchR_Fight = 18f;
            fo.target_canBeSearch = true;
            if (fo.dicCamp != null)
            {
                CFightObj.sCampNode enNode = default(CFightObj.sCampNode);
                enNode.nCampID = pCamp;
                enNode.eOldCamp = CFightObj.eCampEnum.Enemy;
                enNode.eCurCamp = CFightObj.eCampEnum.Enemy;
                enNode.nResetTimeCount = 999999;
                fo.dicCamp[pCamp] = enNode;
                if (pCamp != 0)
                {
                    enNode.nCampID = 0;
                    fo.dicCamp[0] = enNode;
                }
            }
        }

        private static void EnsureSpawnedUnitAlive(GameObject go, CEntity ent, bool isAvatarModel, bool keepOriginalDeathStateId)
        {
            if (go == null) return;
            EnsureFsmReflection();
            try
            {
                if (ent != null)
                {
                    CFightObj fo = ent.fightObj as CFightObj;
                    if (fo != null)
                    {
                        fo.enabled = true;
                        if (!keepOriginalDeathStateId) fo.nDeadStateID = 0;
                        int maxHp = fo.GetMaxHP();
                        if (maxHp <= 1 && ent.propertyPart != null)
                        {
                            maxHp = 1000;
                            ent.propertyPart.SetValue(ePropValueType.Original, 1, maxHp);
                            ent.propertyPart.SetValue(ePropValueType.Original, 2, maxHp);
                        }
                        if (fo.GetCurHP() < maxHp || fo.GetCurHP() <= 0)
                        {
                            fo.SetHP(maxHp);
                            fo.SysncHP();
                        }
                        if (!fo.IsActive()) fo.Active();
                    }
                    CAIFSM[] aiFsms = go.GetComponentsInChildren<CAIFSM>(true);
                    if (aiFsms != null)
                    {
                        for (int i = 0; i < aiFsms.Length; i++)
                        {
                            CAIFSM ai = aiFsms[i];
                            if (ai == null) continue;
                            ai.enabled = true;
                            bool aiAct = (s_fiAiFsmActive != null) && ((bool)s_fiAiFsmActive.GetValue(ai));
                            if (!aiAct) ai.Active();
                        }
                    }
                    if (ent.avatarPart != null && ent.nConfigID > 0 &&
                        (isAvatarModel || ent.GetComponent(typeof(CAIFSM_Avatarable)) != null))
                    {
                        try { ent.avatarPart.UpdateView(); } catch { }
                    }
                }
                CFSMPart[] fsms = go.GetComponentsInChildren<CFSMPart>(true);
                if (fsms != null)
                {
                    for (int i = 0; i < fsms.Length; i++)
                    {
                        CFSMPart fsm = fsms[i];
                        if (fsm == null) continue;
                        fsm.enabled = true;
                        fsm.m_bSaveToDB = false;
                        fsm.m_bSaveToServer = false;
                        if (fsm.m_listState != null && fsm.m_listState.Count > 0)
                        {
                            for (int s = 1; s < fsm.m_listState.Count; s++)
                                DisableGameObjectListSafe(fsm.m_listState[s].listEnableGameobj);
                            int defId = fsm.m_listState[0].nStateID;
                            if (fsm.GetCurrentStateID() != defId) fsm.GotoDefaultState();
                            bool fsmAct = (s_fiFsmActive != null) && ((bool)s_fiFsmActive.GetValue(fsm));
                            if (!fsmAct && s_miFsmActive != null)
                            {
                                try { s_miFsmActive.Invoke(fsm, null); } catch { }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private GameObject InstantiateConfiguredUnit(IGameSystem gs, CEntity player, int unitId, Vector3 spawnPos, int factionOverride, bool stripInteractive, bool keepDeathState, out CEntity outEnt, out int outFaction, out int outFinalCamp)
        {
            outEnt = null;
            outFaction = 1;
            outFinalCamp = 1916;
            bool isAvatarModel;
            object yooHandle;
            GameObject tplGo = ResolveUnitNativePrefab(gs, unitId, out isAvatarModel, out yooHandle);
            if (tplGo == null) return null;
            if (yooHandle != null) _sceneTempYooHandles.Add(yooHandle);

            CFightObj tplFo = tplGo.GetComponent<CFightObj>();
            int cls = 1;
            string ap = null;
            _unitPrefabPathCache.TryGetValue(unitId, out ap);
            IGameDataCenter dc = (CClientGlobal.Instance != null) ? CClientGlobal.Instance.GetGameDataCenter : null;
            string nm = "";
            try
            {
                CUnitConfigLoader ul = (dc != null) ? (dc.GetConfigLoader(eGameConfigType.Unit) as CUnitConfigLoader) : null;
                sUnitConfig uc = (ul != null) ? ul.GetConfig(unitId) : null;
                if (uc != null)
                {
                    cls = (int)uc.eClass;
                    nm = StripRichTags(uc.szStringName);
                }
            }
            catch { }

            int origCamp = (tplFo != null) ? tplFo.campID : ((cls == 1) ? 4 : 0);
            if (tplFo != null) _unitCampCache[unitId] = origCamp;

            outFaction = (factionOverride >= 1 && factionOverride <= 3)
                ? factionOverride
                : ResolveUnitNativeFaction(dc, unitId, (tplFo != null ? origCamp : -9999), cls, ap, nm, null);

            GameObject go = UnityEngine.Object.Instantiate(tplGo);
            SanitizeSpawnedUnitClone(go, stripInteractive);

            go.transform.position = spawnPos;
            go.transform.eulerAngles = Vector3.zero;
            if (!go.activeSelf) go.SetActive(true);

            CAIData[] aids = go.GetComponentsInChildren<CAIData>(true);
            if (aids != null)
            {
                for (int i = 0; i < aids.Length; i++)
                {
                    if (aids[i] == null) continue;
                    aids[i].Partrol_bPauseBySceneGrid = false;
                    aids[i].QuitFight_v3TargetPos = spawnPos;
                }
            }

            CEntity ent = go.GetComponent<CEntity>();
            if (ent != null)
            {
                ent.nConfigID = unitId;
                ent.nCustomID = 0;
                if (ent.UID == 0UL && gs != null && gs.entitySystem != null)
                    ent.UID = gs.entitySystem.GenerateUID();
            }

            PrepareEntityPropertyNodes(go, unitId, isAvatarModel);
            ForceInitializeAllLaunchNodes(go);

            if (ent != null)
            {
                CFightObj fo = ent.fightObj as CFightObj;
                if (fo != null)
                {
                    ApplyEntityFactionConfig(ent, fo, player, outFaction, origCamp);
                    outFinalCamp = fo.campID;
                }
                EnsureSpawnedUnitAlive(go, ent, isAvatarModel, keepDeathState);
                if (fo != null)
                {
                    ApplyEntityFactionConfig(ent, fo, player, outFaction, origCamp);
                    outFinalCamp = fo.campID;
                }
            }
            outEnt = ent;
            return go;
        }

        private int CmdSpawnEntity(IGameSystem gs, CEntity player, int subOp, int unitId, int factionOverride)
        {
            if (subOp == 2)
            {
                DespawnAllCustomEntities(false);
                SetStatus(1, "已清除全部生成实体");
                return 1;
            }
            if (player == null || CGameSceneMgr.Instance == null || _sceneTransitioning)
            {
                SetStatus(-2, "请先进入单人地图后再生成实体");
                return -2;
            }
            if (unitId <= 0) return -2;
            try
            {
                int slotIdx = _spawnedCustomEntities.Count;
                float ang = (slotIdx % 8) * 45f * Mathf.Deg2Rad;
                Vector3 spawnPos = player.transform.position + new Vector3(Mathf.Cos(ang) * 2.2f, Mathf.Sin(ang) * 2.2f, 0f);
                if (!CNavmeshUtil.PointIsOnNavmesh(spawnPos)) spawnPos = player.transform.position;

                CEntity ent;
                int faction, finalCamp;
                GameObject go = InstantiateConfiguredUnit(gs, player, unitId, spawnPos, factionOverride, false, true, out ent, out faction, out finalCamp);
                if (go == null)
                {
                    SetStatus(-3, "找不到实体模板 #" + unitId);
                    return -3;
                }

                ModSpawnedMarker mk = go.GetComponent<ModSpawnedMarker>();
                if (mk == null) mk = go.AddComponent<ModSpawnedMarker>();
                mk.unitId = unitId;
                mk.assignedFaction = faction;

                if (ent != null)
                {
                    List<CEntity> lst;
                    if (!_spawnedCustomEntities.TryGetValue(unitId, out lst))
                    {
                        lst = new List<CEntity>();
                        _spawnedCustomEntities[unitId] = lst;
                    }
                    lst.Add(ent);
                }

                string nm = ResolveUnitDisplayName((CClientGlobal.Instance != null) ? CClientGlobal.Instance.GetGameDataCenter : null, CLanguageMgr.Instance, unitId);
                string fStr = (faction == 3) ? "友军" : ((faction == 2) ? "中立" : "敌对");
                Debug.Log("[ModBridge] SpawnEntity #" + unitId + " name=" + nm + " fac=" + fStr + " camp=" + finalCamp + " pos=" + spawnPos);
                SetStatus(1, "已生成[" + fStr + "] " + nm + " #" + unitId);
                return 1;
            }
            catch (Exception ex)
            {
                SetStatus(-99, "SpawnEntity ex: " + ex.Message);
                Debug.LogWarning("[ModBridge] CmdSpawnEntity ex: " + ex.Message);
                return -99;
            }
        }

        private int CmdCricketDuel(IGameSystem gs, CEntity player, int subOp, int unitIdA, int unitIdB)
        {
            if (subOp == 2)
            {
                ClearCricketArena(false);
                SetStatus(1, "已关闭并清除斗蛐蛐实体");
                return 1;
            }
            if (player == null || CGameSceneMgr.Instance == null || _sceneTransitioning)
            {
                SetStatus(-2, "请先进入单人地图后再发起斗蛐蛐");
                return -2;
            }
            if (unitIdA <= 0 || unitIdB <= 0)
            {
                SetStatus(-2, "请先选择两位参赛实体");
                return -2;
            }
            try
            {
                ClearCricketArena(false);

                Vector3 center = player.transform.position + new Vector3(0f, 2.5f, 0f);
                if (!CNavmeshUtil.PointIsOnNavmesh(center)) center = player.transform.position;
                Vector3 posA = center + new Vector3(-2.0f, 0f, 0f);
                Vector3 posB = center + new Vector3(2.0f, 0f, 0f);
                if (!CNavmeshUtil.PointIsOnNavmesh(posA)) posA = center;
                if (!CNavmeshUtil.PointIsOnNavmesh(posB)) posB = center;

                CEntity entA, entB;
                int fA, fB, cA, cB;
                GameObject goA = InstantiateConfiguredUnit(gs, player, unitIdA, posA, 1, true, true, out entA, out fA, out cA);
                GameObject goB = InstantiateConfiguredUnit(gs, player, unitIdB, posB, 1, true, true, out entB, out fB, out cB);
                if (goA == null || goB == null || entA == null || entB == null)
                {
                    if (goA != null) SafeTeardownSpawnedObject(goA);
                    if (goB != null) SafeTeardownSpawnedObject(goB);
                    SetStatus(-3, "斗蛐蛐实体加载失败");
                    return -3;
                }
                if (entA.UID == 0UL || entA.UID == entB.UID)
                    entA.UID = (gs.entitySystem != null) ? gs.entitySystem.GenerateUID() : 9000001UL;
                if (entB.UID == 0UL || entB.UID == entA.UID)
                    entB.UID = (gs.entitySystem != null) ? gs.entitySystem.GenerateUID() : 9000002UL;

                int playerCamp = (player.fightObj != null) ? player.fightObj.CampID : 0;
                ModCricketDuelist.MaintainCricketLock(entA, entB, CRICKET_CAMP_A, CRICKET_CAMP_B, playerCamp);
                ModCricketDuelist.MaintainCricketLock(entB, entA, CRICKET_CAMP_B, CRICKET_CAMP_A, playerCamp);

                IGameDataCenter dc = (CClientGlobal.Instance != null) ? CClientGlobal.Instance.GetGameDataCenter : null;
                _cricketNameA = ResolveUnitDisplayName(dc, CLanguageMgr.Instance, unitIdA);
                _cricketNameB = ResolveUnitDisplayName(dc, CLanguageMgr.Instance, unitIdB);
                _cricketFighterA = entA;
                _cricketFighterB = entB;
                _cricketAnnouncedEnd = false;

                ModCricketDuelist dA = goA.AddComponent<ModCricketDuelist>();
                dA.selfEnt = entA;
                dA.opponentEnt = entB;
                dA.myCamp = CRICKET_CAMP_A;
                dA.oppCamp = CRICKET_CAMP_B;
                dA.playerCamp = playerCamp;
                dA.arenaCenter = center;
                dA.label = _cricketNameA;

                ModCricketDuelist dB = goB.AddComponent<ModCricketDuelist>();
                dB.selfEnt = entB;
                dB.opponentEnt = entA;
                dB.myCamp = CRICKET_CAMP_B;
                dB.oppCamp = CRICKET_CAMP_A;
                dB.playerCamp = playerCamp;
                dB.arenaCenter = center;
                dB.label = _cricketNameB;

                CFightObj foA = entA.fightObj as CFightObj;
                CFightObj foB = entB.fightObj as CFightObj;
                Debug.Log("[ModBridge] CricketDuel started: A=#" + unitIdA + "(" + _cricketNameA + ",camp=" + CRICKET_CAMP_A + ",hp=" + (foA != null ? foA.GetMaxHP() : -1) +
                          ") vs B=#" + unitIdB + "(" + _cricketNameB + ",camp=" + CRICKET_CAMP_B + ",hp=" + (foB != null ? foB.GetMaxHP() : -1) + ")");
                SetStatus(1, "斗蛐蛐开赛: " + _cricketNameA + " VS " + _cricketNameB);
                return 1;
            }
            catch (Exception ex)
            {
                SetStatus(-99, "CricketDuel ex: " + ex.Message);
                Debug.LogWarning("[ModBridge] CmdCricketDuel ex: " + ex.Message);
                return -99;
            }
        }

        private void TickCricketDuelStatus()
        {
            if (_cricketAnnouncedEnd || _cricketFighterA == null || _cricketFighterB == null) return;
            try
            {
                bool aDead = (_cricketFighterA.fightObj == null) || _cricketFighterA.fightObj.IsDead();
                bool bDead = (_cricketFighterB.fightObj == null) || _cricketFighterB.fightObj.IsDead();
                if (aDead || bDead)
                {
                    _cricketAnnouncedEnd = true;
                    string win = (aDead && bDead) ? "平局 (同归于尽)" : (bDead ? (_cricketNameA + " 获胜!") : (_cricketNameB + " 获胜!"));
                    Debug.Log("[ModBridge] CricketDuel finished: " + win);
                    SetStatus(1, "斗蛐蛐结果: " + win);
                }
            }
            catch { }
        }

        private void BuildRandomHostilePool()
        {
            if (_randomHostilePool != null && _randomHostilePool.Count > 0) return;
            _randomHostilePool = new List<int>(256);
            try
            {
                IGameDataCenter dc = (CClientGlobal.Instance != null) ? CClientGlobal.Instance.GetGameDataCenter : null;
                if (dc == null) return;
                CUnitConfigLoader ul = dc.GetConfigLoader(eGameConfigType.Unit) as CUnitConfigLoader;
                if (ul == null) return;
                FieldInfo fiU = typeof(CUnitConfigLoader).GetField("m_dicConfigs", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                Dictionary<int, sUnitConfig> dic = (fiU != null) ? (fiU.GetValue(ul) as Dictionary<int, sUnitConfig>) : null;
                if (dic == null) return;

                foreach (KeyValuePair<int, sUnitConfig> kv in dic)
                {
                    sUnitConfig uc = kv.Value;
                    if (uc == null || (int)uc.eClass != 1) continue;
                    string ap;
                    if (!_unitPrefabPathCache.TryGetValue(uc.nID, out ap) || string.IsNullOrEmpty(ap)) continue;
                    string lp = ap.ToLowerInvariant();
                    if (lp.Contains("tafang_") || lp.Contains("jiguan_") || lp.Contains("dlc_tuiche") || lp.Contains("team_heli") ||
                        lp.Contains("/guaiwu/shengwu_") || lp.Contains("/animal_") || lp.Contains("monster_pets") ||
                        lp.Contains("jinglingqiu") || lp.Contains("_zhaohuan") || lp.Contains("_zhaomu")) continue;

                    int camp;
                    if (!_unitCampCache.TryGetValue(uc.nID, out camp)) camp = -9999;
                    string uName = StripRichTags(uc.szStringName);
                    int fac = ResolveUnitNativeFaction(dc, uc.nID, camp, 1, ap, uName, null);
                    if (fac == 1)
                    {
                        _randomHostilePool.Add(uc.nID);
                    }
                }
                Debug.Log("[ModBridge] RandomScramble hostilePool built count=" + _randomHostilePool.Count);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModBridge] BuildRandomHostilePool ex: " + ex.Message);
            }
        }

        private static bool HasAncestorOperateOrDialog(GameObject go)
        {
            if (go == null) return false;
            try
            {
                MonoBehaviour[] mbs = go.GetComponentsInChildren<MonoBehaviour>(true);
                if (mbs == null) return false;
                for (int i = 0; i < mbs.Length; i++)
                {
                    if (mbs[i] == null) continue;
                    string tn = mbs[i].GetType().Name;
                    if (tn.Contains("OpenUI_Talk") || tn.Contains("OpenUI_Shop") || tn.Contains("OpenUI_ConditionWnd"))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private void StepRandomEnemyScrambler(IGameSystem gs, int cid, CEntity player, bool randOn)
        {
            if (!randOn)
            {
                _prevRandomSpawnFlag = false;
                return;
            }
            if (!_prevRandomSpawnFlag)
            {
                _prevRandomSpawnFlag = true;
                _lastScrambleTick = 0u;
                BuildRandomHostilePool();
            }
            if (player == null || CGameSceneMgr.Instance == null || _sceneTransitioning) return;
            if ((_tick - _lastScrambleTick) < 3u) return;
            _lastScrambleTick = _tick;

            if (_randomHostilePool == null || _randomHostilePool.Count == 0)
            {
                if (_yooScanDone) BuildRandomHostilePool();
                if (_randomHostilePool == null || _randomHostilePool.Count == 0) return;
            }

            SortedDictionary<ulong, CEntity> entDic = GetSceneEntitiesDict();
            if (entDic == null || entDic.Count == 0) return;

            IGameDataCenter dc = (CClientGlobal.Instance != null) ? CClientGlobal.Instance.GetGameDataCenter : null;
            CFightObj pFo = player.fightObj as CFightObj;
            int playerCamp = (pFo != null) ? pFo.CampID : 0;

            int replaced = 0;
            List<CEntity> batch = new List<CEntity>(10);
            foreach (KeyValuePair<ulong, CEntity> kv in entDic)
            {
                if (batch.Count >= 8) break;
                CEntity ent = kv.Value;
                if (ent == null || ent.gameObject == null || !ent.gameObject.activeInHierarchy) continue;
                if (ent == player || ent.UID == player.UID || ent.nCustomID == cid || ent.nConfigID <= 0) continue;
                if (_scrambledProcessedUids.ContainsKey(ent.UID)) continue;
                int gid = ent.gameObject.GetInstanceID();
                if (_scrambledReplacedGoIds.ContainsKey(gid)) continue;
                if (ent.gameObject.GetComponent<ModForceAllyFollower>() != null ||
                    ent.gameObject.GetComponent<CAIFSM_HireTeamate>() != null ||
                    ent.gameObject.GetComponent<ModSpawnedMarker>() != null ||
                    ent.gameObject.GetComponent<ModCricketDuelist>() != null)
                {
                    _scrambledProcessedUids[ent.UID] = true;
                    continue;
                }
                CFightObj fo = ent.fightObj as CFightObj;
                if (fo == null || fo.IsDead() || fo.CampID == playerCamp || fo.GetMaxHP() <= 0 || fo.GetCurHP() <= 0)
                {
                    _scrambledProcessedUids[ent.UID] = true;
                    continue;
                }
                string foName = fo.GetType().Name;
                if (foName.IndexOf("PiaoLiuTong", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _scrambledProcessedUids[ent.UID] = true;
                    continue;
                }
                bool isHostile = (ClassifyHostile(player, pFo, playerCamp, ent, false) == 1) ||
                                 (ResolveCampFaction(dc, fo.CampID) == 1);
                if (!isHostile || HasAncestorOperateOrDialog(ent.gameObject))
                {
                    _scrambledProcessedUids[ent.UID] = true;
                    continue;
                }
                batch.Add(ent);
            }

            EnsureChildDeadReflection();
            for (int i = 0; i < batch.Count; i++)
            {
                CEntity origEnt = batch[i];
                if (origEnt == null || origEnt.gameObject == null) continue;
                GameObject origGo = origEnt.gameObject;
                _scrambledProcessedUids[origEnt.UID] = true;
                _scrambledReplacedGoIds[origGo.GetInstanceID()] = true;

                int pickIdx = UnityEngine.Random.Range(0, _randomHostilePool.Count);
                int newUnitId = _randomHostilePool[pickIdx];
                Vector3 pos = origEnt.transform.position;
                Transform origParent = origGo.transform.parent;
                int sibIdx = origGo.transform.GetSiblingIndex();
                CFightObj origFo = origEnt.fightObj as CFightObj;
                CTerritory origTerr = (origFo != null) ? origFo.pTerritory : null;

                CEntity newEnt;
                int fac, finalCamp;
                GameObject newGo = InstantiateConfiguredUnit(gs, player, newUnitId, pos, 1, false, true, out newEnt, out fac, out finalCamp);
                if (newGo == null) continue;

                if (origParent != null)
                {
                    newGo.transform.SetParent(origParent, true);
                    newGo.transform.SetSiblingIndex(sibIdx);
                    newGo.transform.position = pos;
                }
                if (newEnt != null)
                {
                    _scrambledProcessedUids[newEnt.UID] = true;
                    _scrambledReplacedGoIds[newGo.GetInstanceID()] = true;
                    CFightObj newFo = newEnt.fightObj as CFightObj;
                    if (newFo != null)
                    {
                        if (origTerr != null) newFo.pTerritory = origTerr;
                        if (origParent != null)
                        {
                            CFSMTriggerListener_AllChildUnitDead[] cDead = origParent.GetComponents<CFSMTriggerListener_AllChildUnitDead>();
                            if (cDead != null && cDead.Length > 0 && s_miCheckTriggerDead != null)
                            {
                                newFo.onDead += delegate(sFightData fd, sFightResultData rd)
                                {
                                    for (int k = 0; k < cDead.Length; k++)
                                    {
                                        if (cDead[k] != null) s_miCheckTriggerDead.Invoke(cDead[k], null);
                                    }
                                };
                            }
                            CFSMTriggerListener_AllChildUnitDeadAndActive[] cDeadAct = origParent.GetComponents<CFSMTriggerListener_AllChildUnitDeadAndActive>();
                            if (cDeadAct != null && cDeadAct.Length > 0 && s_miCheckTriggerDeadAct != null)
                            {
                                newFo.onDead += delegate(sFightData fd, sFightResultData rd)
                                {
                                    for (int k = 0; k < cDeadAct.Length; k++)
                                    {
                                        if (cDeadAct[k] != null) s_miCheckTriggerDeadAct.Invoke(cDeadAct[k], null);
                                    }
                                };
                            }
                        }
                    }
                }

                SafeTeardownSpawnedObject(origGo);
                replaced++;
                if (_scrambleLogCount < 10)
                {
                    _scrambleLogCount++;
                    Debug.Log("[ModBridge] RandomScramble #" + origEnt.nConfigID + " -> #" + newUnitId + " pool=" + _randomHostilePool.Count + " pos=" + pos);
                }
            }
            if (replaced > 0 && _scrambleLogCount <= 10)
            {
                SetStatus(1, "随机生成已打乱本图敌对生物 (+ " + replaced + ")");
            }
        }
    }

    internal class ModForceAllyFollower : MonoBehaviour
    {
        private static readonly List<ModForceAllyFollower> s_followers = new List<ModForceAllyFollower>();

        public CEntity owner;
        public CTerritory allyTerritory;
        public int unitId;
        public bool isAvatarFallback;
        public bool isStoryHire;
        public bool isTamedLive;

        private float _nextCheck;
        private float _lastMoveIssueTime;
        private Vector3 _lastMoveDest;
        private bool _isFollowing;
        private bool _hasFsmChecked;
        private bool _hasAiFsm;
        private CBehaviorConfig _moveWalkCfg;
        private CBehaviorConfig _moveRunCfg;
        private string _idleBehName;
        private bool _behScanned;

        private void OnEnable()
        {
            if (!s_followers.Contains(this)) s_followers.Add(this);
        }

        private void OnDisable()
        {
            s_followers.Remove(this);
        }

        private void OnDestroy()
        {
            s_followers.Remove(this);
        }

        private void EnsureBehaviorsScanned(CEntity self)
        {
            if (_behScanned || self == null) return;
            _behScanned = true;
            CBehaviorPart bp = self.behaviorPart;
            if (bp == null || bp.m_listConfigBehavior == null) return;
            List<CBehaviorConfig> list = bp.m_listConfigBehavior;
            CBehaviorConfig anyMove = null;
            for (int i = 0; i < list.Count; i++)
            {
                CBehaviorConfig c = list[i];
                if (c == null || string.IsNullOrEmpty(c.szBehaviorName)) continue;
                string ln = c.szBehaviorName.ToLowerInvariant();
                if (c.eBehavior == eBehaviorEnum.Move)
                {
                    if (ln == "walk") _moveWalkCfg = c;
                    else if (ln == "run") _moveRunCfg = c;
                    else if (anyMove == null) anyMove = c;
                }
                else if (ln == "stand" || ln == "idle" || ln == "daiji")
                {
                    if (string.IsNullOrEmpty(_idleBehName) || ln == "stand") _idleBehName = c.szBehaviorName;
                }
            }
            if (_moveWalkCfg == null) _moveWalkCfg = _moveRunCfg ?? anyMove;
            if (_moveRunCfg == null) _moveRunCfg = _moveWalkCfg ?? anyMove;
        }

        private static void RotateTowardsDirFrame(CEntity self, Vector3 dir)
        {
            if (self == null) return;
            dir.z = 0f;
            if (dir.sqrMagnitude < 0.0004f) return;
            dir.Normalize();
            if (self.rotationPart != null)
            {
                if (self.rotationPart.percentSpeed < 0.14f) self.rotationPart.percentSpeed = 0.14f;
                self.rotationPart.Rotate2Dir(dir, false);
            }
            else
            {
                float targetAng = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                float curAng = self.transform.eulerAngles.z;
                float nextAng = Mathf.MoveTowardsAngle(curAng, targetAng, 540f * Time.deltaTime);
                self.transform.eulerAngles = new Vector3(0f, 0f, nextAng);
            }
        }

        private void StopFollowMotion(CEntity self)
        {
            _isFollowing = false;
            try
            {
                if (self.movementPart != null) self.movementPart.StopDestination();
                if (self.behaviorPart != null && !string.IsNullOrEmpty(_idleBehName))
                {
                    string curBeh = self.behaviorPart.GetCurrentBehaviorName();
                    if (curBeh != _idleBehName) self.behaviorPart.SetBehavior(_idleBehName, null);
                }
            }
            catch { }
        }

        private void IssueFollowMove(CEntity self, Vector3 dest, bool fast)
        {
            float now = Time.realtimeSinceStartup;
            if ((now - _lastMoveIssueTime) < 0.25f && (dest - _lastMoveDest).sqrMagnitude < 0.45f)
                return;
            _lastMoveIssueTime = now;
            _lastMoveDest = dest;
            _isFollowing = true;

            EnsureBehaviorsScanned(self);
            CBehaviorConfig cfg = fast ? _moveRunCfg : _moveWalkCfg;
            if (cfg != null && self.behaviorPart != null)
            {
                cfg.Move_R_v3TargetPos = dest;
                cfg.Move_R_pRotateAt = null;
                cfg.Move_R_fStoppingDist = 0.28f;
                cfg.Move_bForceRotate2Path = true;
                self.behaviorPart.SetBehavior(cfg.szBehaviorName, null);
                if (self.movementPart != null && self.movementPart.speed < 2.8f)
                    self.movementPart.speed = fast ? 5.2f : 3.8f;
            }
            else if (self.movementPart != null)
            {
                if (self.movementPart.speed < 2.8f) self.movementPart.speed = fast ? 5.2f : 3.8f;
                self.movementPart.SetDestination(dest);
            }
        }

        private Vector3 FindValidRingDestination(Vector3 ownerPos, int slotIdx, Vector3 myPos, out float minNeighborDist, out float distToSlotOut)
        {
            int ring = slotIdx / 6;
            float ringR = 2.75f + ring * 1.45f;
            float baseAng = ((slotIdx % 6) * 60f + 135f) * Mathf.Deg2Rad;
            Vector3 desired = ownerPos + new Vector3(Mathf.Cos(baseAng) * ringR, Mathf.Sin(baseAng) * ringR, 0f);

            minNeighborDist = 999f;
            Vector3 push = Vector3.zero;
            for (int i = 0; i < s_followers.Count; i++)
            {
                ModForceAllyFollower other = s_followers[i];
                if (other == null || other == this) continue;
                Vector3 op = other.transform.position;
                Vector3 diff = myPos - op;
                diff.z = 0f;
                float d = diff.magnitude;
                if (d < minNeighborDist) minNeighborDist = d;
                if (d < 1.85f)
                {
                    if (d < 0.05f)
                    {
                        float jitterAng = (slotIdx + 1) * 1.1f;
                        push += new Vector3(Mathf.Cos(jitterAng), Mathf.Sin(jitterAng), 0f) * 1.85f;
                    }
                    else
                    {
                        push += (diff / d) * (1.85f - d);
                    }
                }
            }

            Vector3 candidate = desired + push;
            if ((candidate - ownerPos).sqrMagnitude < 4.41f)
            {
                Vector3 outDir = candidate - ownerPos;
                outDir.z = 0f;
                if (outDir.sqrMagnitude < 0.01f) outDir = new Vector3(Mathf.Cos(baseAng), Mathf.Sin(baseAng), 0f);
                candidate = ownerPos + outDir.normalized * ringR;
            }

            distToSlotOut = Vector2.Distance(myPos, candidate);
            if (CNavmeshUtil.PointIsOnNavmesh(candidate)) return candidate;

            for (int k = 1; k <= 8; k++)
            {
                float probeAng = baseAng + k * (45f * Mathf.Deg2Rad);
                Vector3 p = ownerPos + new Vector3(Mathf.Cos(probeAng) * ringR, Mathf.Sin(probeAng) * ringR, 0f);
                if (CNavmeshUtil.PointIsOnNavmesh(p)) return p;
            }
            return CNavmeshUtil.PointIsOnNavmesh(ownerPos) ? ownerPos : candidate;
        }

        private void Update()
        {
            if (owner == null || owner.gameObject == null) return;
            CEntity self = GetComponent<CEntity>();
            if (self == null) return;
            CFightObj sFo = self.fightObj as CFightObj;
            CFightObj oFo = owner.fightObj as CFightObj;
            int pCamp = (oFo != null) ? oFo.CampID : 0;
            if (sFo != null && sFo.IsDead()) return;

            if (!_hasFsmChecked)
            {
                _hasFsmChecked = true;
                _hasAiFsm = (GetComponent<CAIFSM>() != null);
            }

            CAIFSM aiFsm = _hasAiFsm ? GetComponent<CAIFSM>() : null;
            bool aiAct = (aiFsm != null) && ModTicker.IsAiFsmActive(aiFsm);
            if (!aiAct && aiFsm != null && (sFo == null || !sFo.IsDead()))
            {
                try { aiFsm.Active(); aiAct = ModTicker.IsAiFsmActive(aiFsm); } catch { }
            }

            CEntity curTgt = (sFo != null) ? sFo.GetCurTarget() : null;
            CAIData aid = GetComponent<CAIData>();
            bool hasNativeFollow = !isTamedLive && aiAct && aid != null &&
                                   (aid.Partrol_eType == eAIPartrolType.FollowCharacter ||
                                    aid.Partrol_eType == eAIPartrolType.Follow ||
                                    aid.Partrol_eType == eAIPartrolType.FollowGuestCharacter);

            if (!hasNativeFollow && (curTgt == null || !_hasAiFsm) && self.movementPart != null)
            {
                Vector3 vel = self.movementPart.velocity;
                if (vel.sqrMagnitude > 0.02f)
                {
                    RotateTowardsDirFrame(self, vel);
                }
                else if (_isFollowing && (_lastMoveDest - transform.position).sqrMagnitude > 0.15f)
                {
                    RotateTowardsDirFrame(self, _lastMoveDest - transform.position);
                }
                else if (curTgt != null && !_hasAiFsm)
                {
                    RotateTowardsDirFrame(self, curTgt.transform.position - transform.position);
                }
            }

            float now = Time.realtimeSinceStartup;
            if (now < _nextCheck) return;
            _nextCheck = now + 0.16f;
            try
            {
                if (allyTerritory != null && allyTerritory.gameObject != null)
                {
                    allyTerritory.transform.position = owner.transform.position;
                }
                if (aid != null) aid.QuitFight_v3TargetPos = owner.transform.position;

                int slotIdx = s_followers.IndexOf(this);
                if (slotIdx < 0) slotIdx = Math.Abs(unitId) % 8;
                Vector3 ownerPos = owner.transform.position;
                Vector3 myP = transform.position;
                float ownerDist = Vector2.Distance(myP, ownerPos);

                if (sFo == null)
                {
                    if (ownerDist > 22f)
                    {
                        float d1, d2;
                        Vector3 wp = FindValidRingDestination(ownerPos, slotIdx, myP, out d1, out d2);
                        if (self.movementPart != null)
                        {
                            self.movementPart.StopDestination();
                            self.movementPart.SetPositionForce(wp);
                        }
                        else transform.position = wp;
                    }
                    return;
                }

                if (sFo.CampID != pCamp) sFo.campID = pCamp;
                if (allyTerritory != null) sFo.territory = allyTerritory;
                else if (oFo != null && oFo.territory != null) sFo.territory = oFo.territory;
                if (sFo.target_searchR < 16f) sFo.target_searchR = 16f;
                if (sFo.target_searchR_Fight < 18f) sFo.target_searchR_Fight = 18f;
                if (sFo.dicCamp != null)
                {
                    CFightObj.sCampNode friendSelf = default(CFightObj.sCampNode);
                    friendSelf.nCampID = pCamp;
                    friendSelf.eOldCamp = CFightObj.eCampEnum.Friend;
                    friendSelf.eCurCamp = CFightObj.eCampEnum.Friend;
                    sFo.dicCamp[pCamp] = friendSelf;
                }

                if (curTgt != null)
                {
                    if (curTgt == owner || ModTicker.ClassifyHostileStatic(owner, oFo, pCamp, curTgt, false) == 0)
                    {
                        sFo.RemoveCurTarget();
                        curTgt = null;
                    }
                }
                if (curTgt == null)
                {
                    SortedDictionary<ulong, CEntity> dic = ModTicker.GetSceneEntitiesDictStatic();
                    if (dic != null && dic.Count > 0)
                    {
                        CEntity bestEnemy = null;
                        float bestSq = 256f;
                        Vector3 myPos = transform.position;
                        foreach (KeyValuePair<ulong, CEntity> kv in dic)
                        {
                            CEntity cand = kv.Value;
                            if (cand == null || cand == self) continue;
                            if (ModTicker.ClassifyHostileStatic(owner, oFo, pCamp, cand, false) != 1) continue;
                            Vector3 cp = cand.transform.position;
                            float dx = cp.x - myPos.x;
                            float dy = cp.y - myPos.y;
                            float dSq = dx * dx + dy * dy;
                            if (dSq < bestSq)
                            {
                                bestSq = dSq;
                                bestEnemy = cand;
                            }
                        }
                        if (bestEnemy != null)
                        {
                            sFo.SetCurTargetStraightly(bestEnemy);
                            curTgt = bestEnemy;
                        }
                    }
                }

                if (ownerDist > 18f)
                {
                    sFo.RemoveCurTarget();
                    float dummy1, dummy2;
                    Vector3 warpPos = FindValidRingDestination(ownerPos, slotIdx, myP, out dummy1, out dummy2);
                    if (self.movementPart != null)
                    {
                        self.movementPart.StopDestination();
                        self.movementPart.SetPositionForce(warpPos);
                    }
                    else
                    {
                        transform.position = warpPos;
                    }
                    _isFollowing = false;
                    return;
                }

                if (hasNativeFollow)
                {
                    _isFollowing = false;
                    return;
                }

                if (curTgt == null)
                {
                    float minNeighborDist;
                    float distToSlot;
                    Vector3 dest = FindValidRingDestination(ownerPos, slotIdx, myP, out minNeighborDist, out distToSlot);

                    if (!_isFollowing)
                    {
                        if (ownerDist > 4.6f || ownerDist < 1.65f || minNeighborDist < 1.45f)
                        {
                            IssueFollowMove(self, dest, ownerDist > 5.5f);
                        }
                    }
                    else
                    {
                        if (distToSlot <= 0.55f || (ownerDist >= 2.1f && ownerDist <= 3.8f && minNeighborDist >= 1.55f))
                        {
                            StopFollowMotion(self);
                        }
                        else
                        {
                            IssueFollowMove(self, dest, ownerDist > 5.5f);
                        }
                    }
                }
                else
                {
                    _isFollowing = false;
                    if (!_hasAiFsm)
                    {
                        float tDist = Vector2.Distance(myP, curTgt.transform.position);
                        if (tDist > 2.0f) IssueFollowMove(self, curTgt.transform.position, true);
                        else StopFollowMotion(self);
                    }
                }
            }
            catch { }
        }
    }

    internal sealed class ModSpawnedMarker : MonoBehaviour
    {
        public int unitId;
        public int assignedFaction;
    }

    internal sealed class ModCricketDuelist : MonoBehaviour
    {
        public CEntity selfEnt;
        public CEntity opponentEnt;
        public int myCamp;
        public int oppCamp;
        public int playerCamp;
        public Vector3 arenaCenter;
        public string label;

        private static FieldInfo s_fiQuitFight = null;
        private static MethodInfo s_miIsLockFind = null;

        public static void MaintainCricketLock(CEntity selfEnt, CEntity opponentEnt, int myCamp, int oppCamp, int playerCamp)
        {
            if (selfEnt == null || selfEnt.fightObj == null) return;
            CFightObj fo = selfEnt.fightObj as CFightObj;
            if (fo == null || fo.IsDead()) return;

            if (s_fiQuitFight == null)
                s_fiQuitFight = typeof(CFightObj).GetField("m_fQuitFightTimeCount", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (s_miIsLockFind == null)
                s_miIsLockFind = typeof(CFightObj).GetMethod("IsLockFindTarget", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            fo.enabled = true;
            if (!fo.IsActive()) fo.Active();
            fo.campID = myCamp;
            if (s_fiQuitFight != null) s_fiQuitFight.SetValue(fo, 0f);
            fo.target_canBeSearch = true;
            if (fo.target_searchR < 80f) fo.target_searchR = 80f;
            if (fo.target_searchR_Fight < 80f) fo.target_searchR_Fight = 80f;

            if (s_miIsLockFind != null)
            {
                int guard = 0;
                while (guard++ < 4)
                {
                    object r = s_miIsLockFind.Invoke(fo, null);
                    if (r is bool && (bool)r) break;
                    fo.SubLockFindTarget();
                }
            }

            if (fo.dicCamp != null)
            {
                List<int> ks = new List<int>(fo.dicCamp.Keys);
                for (int i = 0; i < ks.Count; i++)
                {
                    int k = ks[i];
                    if (k == oppCamp) continue;
                    CFightObj.sCampNode cur;
                    if (!fo.dicCamp.TryGetValue(k, out cur) || cur.eCurCamp != CFightObj.eCampEnum.Friend || cur.eOldCamp != CFightObj.eCampEnum.Friend)
                    {
                        cur.nCampID = k;
                        cur.eOldCamp = CFightObj.eCampEnum.Friend;
                        cur.eCurCamp = CFightObj.eCampEnum.Friend;
                        cur.nResetTimeCount = 999999;
                        fo.dicCamp[k] = cur;
                    }
                }
                CFightObj.sCampNode pn = default(CFightObj.sCampNode);
                pn.nCampID = playerCamp;
                pn.eOldCamp = CFightObj.eCampEnum.Friend;
                pn.eCurCamp = CFightObj.eCampEnum.Friend;
                pn.nResetTimeCount = 999999;
                fo.dicCamp[playerCamp] = pn;
                if (playerCamp != 0)
                {
                    pn.nCampID = 0;
                    fo.dicCamp[0] = pn;
                }

                CFightObj.sCampNode on = default(CFightObj.sCampNode);
                on.nCampID = oppCamp;
                on.eOldCamp = CFightObj.eCampEnum.Enemy;
                on.eCurCamp = CFightObj.eCampEnum.Enemy;
                on.nResetTimeCount = 999999;
                fo.dicCamp[oppCamp] = on;
            }

            if (opponentEnt != null && opponentEnt.fightObj != null && !opponentEnt.fightObj.IsDead())
            {
                if (fo.dicTarget != null)
                {
                    bool needPurge = (fo.dicTarget.Count != 1) || !fo.dicTarget.ContainsKey(opponentEnt.UID);
                    if (needPurge)
                    {
                        fo.dicTarget.Clear();
                        CFightObj.sTargetNode tn = default(CFightObj.sTargetNode);
                        tn.UID = opponentEnt.UID;
                        tn.pEntity = opponentEnt;
                        tn.nHeatred = 100000;
                        fo.dicTarget[opponentEnt.UID] = tn;
                    }
                }
                if (fo.GetCurTarget() != opponentEnt)
                {
                    fo.SetCurTargetStraightly(opponentEnt);
                }
            }
        }

        private void LateUpdate()
        {
            try
            {
                MaintainCricketLock(selfEnt, opponentEnt, myCamp, oppCamp, playerCamp);
            }
            catch { }
        }
    }
}
