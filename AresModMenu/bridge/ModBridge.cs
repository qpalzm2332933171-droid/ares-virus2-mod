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

        public static bool IsAnyCurrencyModded()
        {
            return s_modDiamond || s_modCoin || s_modPoint || s_modCumul;
        }

        public void ForceSet(int type, int val)
        {
            if (val < 0) val = 0;
            if (type == 0) { s_modDiamond = true; s_valDiamond = val; }
            else if (type == 1) { s_modCoin = true; s_valCoin = val; }
            else if (type == 2) { s_modPoint = true; s_valPoint = val; }
            else if (type == 3) { s_modCumul = true; s_valCumul = val; }
            SetRaw(type, val, true);
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
            if (s_modDiamond && nNum < s_valDiamond)
            {
                base.SetDiamondNum(s_valDiamond);
                return;
            }
            if (s_modDiamond) s_valDiamond = nNum;
            base.SetDiamondNum(nNum);
        }

        public new int GetGardenCoinNum()
        {
            return s_modCoin ? s_valCoin : base.GetGardenCoinNum();
        }

        public new void SetGardenCoinNum(int nNum)
        {
            if (s_modCoin && nNum < s_valCoin)
            {
                base.SetGardenCoinNum(s_valCoin);
                return;
            }
            if (s_modCoin) s_valCoin = nNum;
            base.SetGardenCoinNum(nNum);
        }

        public new int GetGardenPointNum()
        {
            return s_modPoint ? s_valPoint : base.GetGardenPointNum();
        }

        public new void SetGardenPointNum(int nNum)
        {
            if (s_modPoint && nNum < s_valPoint)
            {
                base.SetGardenPointNum(s_valPoint);
                return;
            }
            if (s_modPoint) s_valPoint = nNum;
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
            if (s_modCumul && nNum < s_valCumul)
            {
                base.SetCumulativePayment(s_valCumul);
                return;
            }
            if (s_modCumul) s_valCumul = nNum;
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
            s_instance = this;
            Debug.Log("[ModBridge] ModTicker Awake");
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
            if (gs != null)
            {
                EnsureModSystems(gs);
            }
            PollHttpBridge(b, gs);
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

                // Periodic export of dynamic warehouse and currency state
                if ((_tick % 60u) == 12u)
                {
                    ExportDynWarehouseAll(gs, false);
                    ExportDynCurrencyFast(gs);
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

        private unsafe int ExecuteCmd(int ctype, int arg1, int arg2, int arg3, long argL, IGameSystem gs, int cid, CEntity player, LAN.CLanEntity lanPlayer)
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

        private int CmdSetCurrencyV2(IGameSystem gs, int ctype, int val)
        {
            if (gs == null || gs.paySystem == null)
            {
                SetStatus(-2, "PaySystem null");
                return -2;
            }
            if (val < 0) val = 0;
            ModPaySystem mps = gs.paySystem as ModPaySystem;
            if (mps != null)
            {
                mps.ForceSet(ctype, val);
            }
            else
            {
                IPaySystem ps = gs.paySystem;
                if (ctype == 0) ps.SetDiamondNum(val);
                else if (ctype == 1) ps.SetGardenCoinNum(val);
                else if (ctype == 2) ps.SetGardenPointNum(val);
                else if (ctype == 3) ps.SetCumulativePayment(val);
            }
            ExportDynCurrencyFast(gs);
            SetStatus(1, "Currency updated");
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

        private void SetCurrencyChecked(IGameSystem gs, int ctype, int val)
        {
            if (gs == null || gs.paySystem == null) return;
            ModPaySystem mps = gs.paySystem as ModPaySystem;
            if (mps != null) mps.ForceSet(ctype, val);
            else if (ctype == 0) gs.paySystem.SetDiamondNum(val);
            else if (ctype == 1) gs.paySystem.SetGardenCoinNum(val);
            else if (ctype == 2) gs.paySystem.SetGardenPointNum(val);
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
    }
}
