package com.android.support;

import android.content.Context;
import android.content.SharedPreferences;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Handler;
import android.os.Looper;
import android.text.Editable;
import android.text.Html;
import android.text.InputType;
import android.text.TextUtils;
import android.text.TextWatcher;
import android.util.DisplayMetrics;
import android.util.LruCache;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.inputmethod.InputMethodManager;
import android.widget.AdapterView;
import android.widget.ArrayAdapter;
import android.widget.BaseAdapter;
import android.widget.Button;
import android.widget.CompoundButton;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.HorizontalScrollView;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ListView;
import android.widget.ScrollView;
import android.widget.SeekBar;
import android.widget.Spinner;
import android.widget.Switch;
import android.widget.TextView;
import android.widget.Toast;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.HashMap;
import java.util.HashSet;
import java.util.Iterator;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.TreeSet;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * Modern landscape glassmorphic Mod Console (6 tabs):
 *  0: 🎒 物品发放 (4-5 col compact icon+id+name grid, long-press rich detail popup, 1/999 + 全选/清空, dynamic reload)
 *  1: 📊 全部属性 (smooth ViewHolder editing & locking across all 50 DPropertyKey items, fixed groups)
 *  2: ✨ Buff施加 (smooth async-icon multi-select, fixed BuffJudge -1/0/1 filter)
 *  3: 🛠️ 其他功能 (Anti-Cheat, Ammo, Durability, Blueprints, Paid Unlock, Instant Interact, Warehouse InfStack, Noclip, MachineGun+Rate)
 *  4: 🎨 主题风格 (5 distinct live-switchable persisted themes + FAB reskin)
 *  5: ⚙️ 诊断设置 (ModShm diagnostics, dynamic resync, reset FAB, save prefs)
 */
public final class ModConsole {

    public interface Callbacks {
        void onMinimize();
        void onHideIcon();
        void onResetIconPos();
        void onThemeChanged(int fabBg, int fabStroke, int fabText);
    }

    // ==================== 5 Distinct Switchable Themes ====================
    private static final class ThemePalette {
        final String name;
        final String desc;
        final boolean isLight;
        final int backdrop;
        final int panelBg;
        final int panelEdge;
        final int sidebarBg;
        final int headerBg;
        final int divider;
        final int cardBg;
        final int cardEdge;
        final int cardOnBg;
        final int cardOnEdge;
        final int selBg;
        final int selEdge;
        final int inputBg;
        final int inputEdge;
        final int textPri;
        final int textSec;
        final int accent;
        final int emerald;
        final int danger;
        final int btnSlate;
        final int btnBlue;
        final int btnGreen;
        final int btnRed;
        final int fabBg;
        final int fabStroke;
        final int fabText;

        ThemePalette(String name, String desc, boolean isLight,
                     int backdrop, int panelBg, int panelEdge, int sidebarBg, int headerBg, int divider,
                     int cardBg, int cardEdge, int cardOnBg, int cardOnEdge, int selBg, int selEdge,
                     int inputBg, int inputEdge, int textPri, int textSec, int accent, int emerald, int danger,
                     int btnSlate, int btnBlue, int btnGreen, int btnRed,
                     int fabBg, int fabStroke, int fabText) {
            this.name = name; this.desc = desc; this.isLight = isLight;
            this.backdrop = backdrop; this.panelBg = panelBg; this.panelEdge = panelEdge;
            this.sidebarBg = sidebarBg; this.headerBg = headerBg; this.divider = divider;
            this.cardBg = cardBg; this.cardEdge = cardEdge; this.cardOnBg = cardOnBg; this.cardOnEdge = cardOnEdge;
            this.selBg = selBg; this.selEdge = selEdge; this.inputBg = inputBg; this.inputEdge = inputEdge;
            this.textPri = textPri; this.textSec = textSec; this.accent = accent;
            this.emerald = emerald; this.danger = danger;
            this.btnSlate = btnSlate; this.btnBlue = btnBlue; this.btnGreen = btnGreen; this.btnRed = btnRed;
            this.fabBg = fabBg; this.fabStroke = fabStroke; this.fabText = fabText;
        }
    }

    private static final ThemePalette[] THEMES = new ThemePalette[] {
        new ThemePalette(
            "极客赛博蓝", "深邃霓虹蓝玻质感 · 默认推荐", false,
            0x6604070C, 0xEE0F1722, 0xFF2B4560, 0xF20A1018, 0xFF131F2E, 0xFF1F3042,
            0xFF172230, 0xFF26384D, 0xFF102C2A, 0xFF10B981, 0xFF1A3652, 0xFF38BDF8,
            0xFF0E1622, 0xFF2C4257, 0xFFF1F5F9, 0xFF94A3B8, 0xFF38BDF8, 0xFF34D399, 0xFFF87171,
            0xFF283548, 0xFF0284C7, 0xFF059669, 0xFFDC2626,
            0xDD0F1722, 0xFF38BDF8, 0xFFF1F5F9
        ),
        new ThemePalette(
            "暗夜绯红", "黑曜石配炽烈绯红 · 高燃战斗风", false,
            0x660C0508, 0xEE180D11, 0xFF7F1D1D, 0xF212080B, 0xFF231017, 0xFF3B1A24,
            0xFF221319, 0xFF47212C, 0xFF2D161E, 0xFFFB7185, 0xFF4C1D2B, 0xFFFB7185,
            0xFF150B0E, 0xFF5C2836, 0xFFFFF1F2, 0xFFFDA4AF, 0xFFFB7185, 0xFF4ADE80, 0xFFEF4444,
            0xFF3F202B, 0xFFE11D48, 0xFF16A34A, 0xFF991B1B,
            0xDD180D11, 0xFFFB7185, 0xFFFFF1F2
        ),
        new ThemePalette(
            "翡翠森境", "暗松绿配荧光青翠 · 清新护眼", false,
            0x66040C08, 0xEE0A1913, 0xFF065F46, 0xF207120E, 0xFF0E231B, 0xFF17382B,
            0xFF12281F, 0xFF1E4435, 0xFF11382B, 0xFF34D399, 0xFF134E3A, 0xFF34D399,
            0xFF08140F, 0xFF21523E, 0xFFECFDF5, 0xFFA7F3D0, 0xFF34D399, 0xFF2DD4BF, 0xFFF87171,
            0xFF1E3A30, 0xFF0D9488, 0xFF059669, 0xFFDC2626,
            0xDD0A1913, 0xFF34D399, 0xFFECFDF5
        ),
        new ThemePalette(
            "琥珀黑金", "曜黑底配鎏金描边 · 典藏尊享风", false,
            0x660B0904, 0xEE18130B, 0xFF92400E, 0xF2120E07, 0xFF231B0E, 0xFF3D2E18,
            0xFF221A0E, 0xFF4A371C, 0xFF2A2611, 0xFFFBBF24, 0xFF453013, 0xFFFBBF24,
            0xFF141008, 0xFF5E4522, 0xFFFEFCE8, 0xFFFDE68A, 0xFFFBBF24, 0xFF4ADE80, 0xFFF87171,
            0xFF3D311E, 0xFFD97706, 0xFF16A34A, 0xFFB91C1C,
            0xDD18130B, 0xFFFBBF24, 0xFFFEFCE8
        ),
        new ThemePalette(
            "晨曦极简白", "通透磨砂浅色面板 · 日间高对比", true,
            0x551E293B, 0xF2F1F5F9, 0xFF94A3B8, 0xF7E2E8F0, 0xFFE2E8F0, 0xFFCBD5E1,
            0xFFFFFFFF, 0xFFCBD5E1, 0xFFD1FAE5, 0xFF10B981, 0xFFDBEAFE, 0xFF2563EB,
            0xFFFFFFFF, 0xFF94A3B8, 0xFF0F172A, 0xFF475569, 0xFF2563EB, 0xFF059669, 0xFFDC2626,
            0xFF64748B, 0xFF2563EB, 0xFF059669, 0xFFDC2626,
            0xEEF8FAFC, 0xFF2563EB, 0xFF0F172A
        )
    };

    // ==================== Async Icon Cache & Thread Pool ====================
    private static final LruCache<String, Bitmap> sIconCache = new LruCache<String, Bitmap>(384);
    private static final Set<String> sMissingIcons = Collections.synchronizedSet(new HashSet<String>());
    private static final ExecutorService sIconExec = Executors.newFixedThreadPool(2);
    private static final ExecutorService sBgExec = Executors.newSingleThreadExecutor();
    private static volatile List<ItemInfo> sItemsCache = null;
    private static volatile List<BuffInfo> sBuffsCache = null;
    private static final Map<Integer, String> sDynSubtypeNames = Collections.synchronizedMap(new HashMap<Integer, String>());

    private static final class ItemInfo {
        int id, cls, type, bt, bst, prio, end, bp, cantCombine;
        String name, desc, detail, icon;
    }

    private static final class BuffInfo {
        int id, judge, timeType, keepTime, show;
        String name, uiTitle, desc, icon;
    }

    private static final class PropDef {
        final int key;
        final int group;
        final float scale;
        final String shortName;
        PropDef(int key, int group, float scale, String shortName) {
            this.key = key;
            this.group = group;
            this.scale = scale;
            this.shortName = shortName;
        }
    }

    private static final PropDef[] PROP_DEFS = new PropDef[] {
        new PropDef(1,   0, 1f,    "生命上限 (hp_shangxian)"),
        new PropDef(2,   0, 1f,    "当前生命 (hp_dangqian)"),
        new PropDef(201, 1, 1f,    "最小攻击力 (gongji_min)"),
        new PropDef(202, 1, 1f,    "最大攻击力 (gongji_max)"),
        new PropDef(210, 1, 1f,    "攻击总增益 (zongzengyi)"),
        new PropDef(215, 1, 1f,    "重击攻击力 (gongji_heavy)"),
        new PropDef(205, 1, 1000f, "攻击距离/范围 (juli)"),
        new PropDef(601, 1, 1000f, "攻击动作倍率 (atk_time)"),
        new PropDef(211, 1, 1000f, "连发射速 (lianfashesu)"),
        new PropDef(212, 1, 1000f, "投射飞速 (feixingsudu)"),
        new PropDef(203, 1, 1000f, "蓄力时间 (xulishijian)"),
        new PropDef(204, 1, 1000f, "攻击后摇 (houyaoshijian)"),
        new PropDef(206, 1, 1000f, "起始角度 (jiaodu_qishi)"),
        new PropDef(207, 1, 1000f, "结束角度 (jiaodu_jieshu)"),
        new PropDef(101, 2, 1000f, "跑步移速 (yisu_paobu)"),
        new PropDef(108, 2, 1000f, "疾跑移速 (yisu_jiasupao)"),
        new PropDef(103, 2, 1000f, "攻击移速 (yisu_gongji)"),
        new PropDef(102, 2, 1000f, "巡逻移速 (yisu_xunluo)"),
        new PropDef(104, 2, 1000f, "走跑分界 (zoupao)"),
        new PropDef(105, 2, 1000f, "平移速度 (yisu_pingyi)"),
        new PropDef(106, 2, 1000f, "搬运移速 (banyun)"),
        new PropDef(107, 2, 1000f, "对峙移速 (yisu_duizhi)"),
        new PropDef(301, 3, 1f,    "防御力 (fangyuli)"),
        new PropDef(302, 3, 1f,    "护盾上限 (shield_max)"),
        new PropDef(303, 3, 1f,    "当前护盾 (shield_cur)"),
        new PropDef(304, 3, 1f,    "格挡上限 (block_max)"),
        new PropDef(305, 3, 1f,    "当前格挡 (block_cur)"),
        new PropDef(402, 4, 1000f, "当前体力 (tili_cur)"),
        new PropDef(403, 4, 1000f, "体力上限 (tili_max)"),
        new PropDef(404, 4, 1000f, "体力恢复率 (tili_add)"),
        new PropDef(401, 4, 1000f, "体力间隔s (tili_gap)"),
        new PropDef(405, 4, 1000f, "攻击耗体 (tili_atkRed)"),
        new PropDef(406, 4, 1000f, "疾跑耗体 (tili_runRed)"),
        new PropDef(411, 4, 1000f, "当前饥饿 (jie_cur)"),
        new PropDef(412, 4, 1000f, "饥饿上限 (jie_max)"),
        new PropDef(413, 4, 1000f, "饥饿消耗 (jie_reduce)"),
        new PropDef(421, 4, 1000f, "当前精力 (jingli_cur)"),
        new PropDef(422, 4, 1000f, "精力上限 (jingli_max)"),
        new PropDef(423, 4, 1000f, "精力消耗 (jingli_reduce)"),
        new PropDef(51,  5, 1000f, "声音半径 (shengyin_r)"),
        new PropDef(52,  5, 1f,    "队友上限 (duiyou_max)"),
        new PropDef(600, 5, 1f,    "操作/钓鱼加速% (diaoyu_jiasu)"),
        new PropDef(500, 6, 1f,    "塔防耐久 (tf_naijiu)"),
        new PropDef(501, 6, 1f,    "塔防攻击 (tf_atk)"),
        new PropDef(502, 6, 1f,    "塔防破甲 (tf_pojia)"),
        new PropDef(503, 6, 1f,    "塔防弹夹 (tf_ammo)"),
        new PropDef(504, 6, 1000f, "塔防精准 (tf_acc)"),
        new PropDef(505, 6, 1000f, "塔防换弹 (tf_reload)"),
        new PropDef(506, 6, 1000f, "塔防飞速 (tf_spd)"),
        new PropDef(507, 6, 1000f, "塔防射速 (tf_rate)")
    };

    // Exact UIBelongSubType names from game v2.2.13 cn.xml (CMaterialConfigLoader.GetStringByUISubType)
    private static final LinkedHashMap<Integer, String> SUBTYPE_NAMES = new LinkedHashMap<Integer, String>();
    static {
        SUBTYPE_NAMES.put(0,  "全部小类");
        SUBTYPE_NAMES.put(11, "11 武器");
        SUBTYPE_NAMES.put(12, "12 装备");
        SUBTYPE_NAMES.put(13, "13 弹药");
        SUBTYPE_NAMES.put(14, "14 工具");
        SUBTYPE_NAMES.put(15, "15 武器模组");
        SUBTYPE_NAMES.put(21, "21 食物");
        SUBTYPE_NAMES.put(22, "22 药品");
        SUBTYPE_NAMES.put(23, "23 种子");
        SUBTYPE_NAMES.put(24, "24 食材药材");
        SUBTYPE_NAMES.put(25, "25 种植养殖");
        SUBTYPE_NAMES.put(31, "31 贵重品");
        SUBTYPE_NAMES.put(32, "32 生物");
        SUBTYPE_NAMES.put(33, "33 材料");
        SUBTYPE_NAMES.put(34, "34 其他物品");
        SUBTYPE_NAMES.put(35, "35 家具家电");
        SUBTYPE_NAMES.put(39, "39 技能称号");
        SUBTYPE_NAMES.put(40, "40 重要物品");
        SUBTYPE_NAMES.put(41, "41 奖杯");
        SUBTYPE_NAMES.put(42, "42 饮品小吃");
        SUBTYPE_NAMES.put(43, "43 海鱼");
        SUBTYPE_NAMES.put(44, "44 家园装饰");
        SUBTYPE_NAMES.put(51, "51 地板");
        SUBTYPE_NAMES.put(52, "52 杂物");
        SUBTYPE_NAMES.put(53, "53 家园家具");
        SUBTYPE_NAMES.put(54, "54 摆件");
    }

    private final Context ctx;
    private final Callbacks cb;
    private final Handler mainH = new Handler(Looper.getMainLooper());
    private final SharedPreferences modPrefs;

    // Active Theme state
    private int curThemeIdx = 0;
    private ThemePalette curTheme = THEMES[0];
    private int themeVersion = 1;

    // Root views to re-skin live
    private FrameLayout backdropView;
    private LinearLayout cardRoot;
    private LinearLayout headerBar;
    private LinearLayout sidebarView;
    private View hDivView, vDivView;
    private TextView tvTitle, tvLivePill, tvDiagText, tvOtherStatus, tvMgRateVal;
    private Button btnHideTop, btnMinTop;

    // 6 sidebar tabs: Items / Props / Buffs / OtherFeatures / Themes / Settings
    private static final int TAB_COUNT = 6;
    private final FrameLayout[] pages = new FrameLayout[TAB_COUNT];
    private final LinearLayout[] sideBtns = new LinearLayout[TAB_COUNT];
    private final View[] sideBars = new View[TAB_COUNT];
    private final TextView[] sideLabels = new TextView[TAB_COUNT];
    private int curTab = 0;

    // Toggle states (synced with NativeQuery(10) & Preferences)
    private boolean flagAC = true;
    private boolean flagAmmo = false;
    private boolean flagEndur = false;
    private boolean flagPaid = false;
    private boolean flagInstant = false;
    private boolean flagInfStack = false;
    private boolean flagNoclip = false;
    private boolean flagMG = false;
    private int mgRateX10 = 100; // default 10.0 shots/s

    // Other-Features cards & status pills for theme/state refresh
    private LinearLayout cardAC, cardAmmo, cardEndur, cardPaid, cardInst, cardInfStk, cardNoclip, cardMG, cardBp, cardAutoProps;
    private TextView tvACState, tvAmmoState, tvEndurState, tvPaidState, tvInstState, tvInfStkState, tvNoclipState, tvMGState, tvAutoPropState;
    private final LinearLayout[] themeCards = new LinearLayout[THEMES.length];
    private final TextView[] themeBadges = new TextView[THEMES.length];
    private LinearLayout diagCard, prefCard;
    private TextView tvDiagHdr, tvPrefText;

    // Detail popup modal overlay
    private FrameLayout detailModalOverlay;
    private LinearLayout detailModalCard;
    private ImageView detailModalIcon;
    private TextView detailModalTitle, detailModalSub, detailModalBody;
    private Button detailModalSelBtn, detailModalAdd1Btn, detailModalAdd999Btn, detailModalCloseBtn;
    private ItemInfo detailModalItem;

    // Live snapshots & refresh hooks
    private JSONObject lastSnap = new JSONObject();
    private final Map<Integer, Integer> liveRaw = new HashMap<Integer, Integer>();
    private final Map<Integer, Boolean> liveLocked = new HashMap<Integer, Boolean>();
    private final Map<Integer, String> editedPropText = new HashMap<Integer, String>();
    private final Map<Integer, Integer> dirtyPropRaw = new HashMap<Integer, Integer>();
    private final Map<Integer, Integer> savedPropVals = new HashMap<Integer, Integer>();
    private final Map<Integer, Boolean> savedPropLocks = new HashMap<Integer, Boolean>();
    private boolean flagAutoProps = true;
    private int lastAutoPropsCid = 0;

    private long lastDynSeq = -1;
    private int lastToastItemCnt = -1;
    private int lastToastBuffCnt = -1;
    private boolean manualDynSyncRequested = false;
    private boolean initialBridgeSynced = false;
    private boolean apkIconsWritten = false;

    private Runnable propSnapshotRefreshHook = null;
    private Runnable itemBoxesRefreshHook = null;
    private Runnable itemDynReloadHook = null;
    private Runnable buffActiveRefreshHook = null;
    private Runnable buffDynReloadHook = null;
    private final List<Runnable> themeRefreshHooks = new ArrayList<Runnable>();

    public ModConsole(Context ctx, Callbacks cb) {
        this.ctx = ctx;
        this.cb = cb;
        this.modPrefs = ctx.getApplicationContext().getSharedPreferences("ares_mod_prefs", Context.MODE_PRIVATE);
        this.curThemeIdx = Math.max(0, Math.min(THEMES.length - 1, modPrefs.getInt("theme_idx", 0)));
        this.curTheme = THEMES[this.curThemeIdx];

        this.flagAC = Preferences.loadPrefBool("全代码拦截作弊检验/上报/封号", 60, true);
        this.flagAmmo = Preferences.loadPrefBool("子弹不减反增", 40, false);
        this.flagEndur = Preferences.loadPrefBool("锁定武器/装备耐久", 41, false);
        this.flagPaid = Preferences.loadPrefBool("一键解锁全部付费道具", 70, false);
        this.flagInstant = Preferences.loadPrefBool("关闭交互时间", 71, false);
        this.flagInfStack = Preferences.loadPrefBool("无限堆叠(仓库)", 72, false);
        this.flagNoclip = Preferences.loadPrefBool("穿墙模式", 73, false);
        this.flagMG = Preferences.loadPrefBool("机关枪模式", 74, false);
        int savedRate = Preferences.loadPrefInt("机关枪连发速度", 75);
        this.mgRateX10 = (savedRate >= 10 && savedRate <= 600) ? savedRate : modPrefs.getInt("mg_rate_x10", 100);
        if (this.mgRateX10 < 10 || this.mgRateX10 > 600) this.mgRateX10 = 100;
        XposedEntry.antiCheatEnabled = this.flagAC;
        loadSavedPropsFromPrefs();

        prepareDynEnvironmentAsync();
    }

    private void loadSavedPropsFromPrefs() {
        savedPropVals.clear();
        savedPropLocks.clear();
        flagAutoProps = modPrefs.getBoolean("auto_restore_props", true);
        String rawJson = modPrefs.getString("saved_props_json", "");
        if (rawJson == null || rawJson.isEmpty()) return;
        try {
            JSONArray arr = new JSONArray(rawJson);
            for (int i = 0; i < arr.length(); i++) {
                JSONObject o = arr.optJSONObject(i);
                if (o == null) continue;
                int k = o.optInt("k", -1);
                if (k < 0) continue;
                savedPropVals.put(k, o.optInt("v", 0));
                savedPropLocks.put(k, o.optInt("lk", 0) != 0);
            }
        } catch (Throwable ignored) {}
    }

    private void saveCurrentPropsConfig() {
        for (PropDef pd : PROP_DEFS) {
            if (!editedPropText.containsKey(pd.key)) continue;
            try {
                String txt = editedPropText.get(pd.key);
                if (txt != null && !txt.trim().isEmpty()) {
                    int raw = (int) Math.round(Double.parseDouble(txt.trim()) * pd.scale);
                    dirtyPropRaw.put(pd.key, raw);
                }
            } catch (Throwable ignored) {}
        }
        Map<Integer, Integer> mergedVals = new HashMap<Integer, Integer>(savedPropVals);
        Map<Integer, Boolean> mergedLocks = new HashMap<Integer, Boolean>(savedPropLocks);
        for (Map.Entry<Integer, Integer> e : dirtyPropRaw.entrySet()) {
            int k = e.getKey();
            mergedVals.put(k, e.getValue());
            mergedLocks.put(k, liveLocked.containsKey(k) && liveLocked.get(k));
        }
        for (Map.Entry<Integer, Boolean> e : liveLocked.entrySet()) {
            int k = e.getKey();
            if (Boolean.TRUE.equals(e.getValue())) {
                int v = dirtyPropRaw.containsKey(k) ? dirtyPropRaw.get(k) : (liveRaw.containsKey(k) ? liveRaw.get(k) : 0);
                mergedVals.put(k, v);
                mergedLocks.put(k, true);
            } else if (mergedLocks.containsKey(k)) {
                mergedLocks.put(k, false);
            }
        }
        if (mergedVals.isEmpty()) {
            Toast.makeText(ctx, "请先在[全部属性]修改或锁定属性后再点击保存", Toast.LENGTH_SHORT).show();
            return;
        }
        savedPropVals.clear();
        savedPropVals.putAll(mergedVals);
        savedPropLocks.clear();
        savedPropLocks.putAll(mergedLocks);
        flagAutoProps = true;
        try {
            JSONArray arr = new JSONArray();
            for (PropDef pd : PROP_DEFS) {
                if (!savedPropVals.containsKey(pd.key)) continue;
                JSONObject o = new JSONObject();
                o.put("k", pd.key);
                o.put("v", savedPropVals.get(pd.key));
                o.put("lk", (savedPropLocks.containsKey(pd.key) && savedPropLocks.get(pd.key)) ? 1 : 0);
                arr.put(o);
            }
            modPrefs.edit()
                    .putString("saved_props_json", arr.toString())
                    .putBoolean("auto_restore_props", true)
                    .apply();
        } catch (Throwable ignored) {}
        syncToggleCardStyles();
        Toast.makeText(ctx, "已保存 " + savedPropVals.size() + " 项属性配置，下次进游戏自动加载", Toast.LENGTH_SHORT).show();
    }

    private void clearSavedPropsConfig() {
        savedPropVals.clear();
        savedPropLocks.clear();
        dirtyPropRaw.clear();
        modPrefs.edit().remove("saved_props_json").apply();
        syncToggleCardStyles();
        Toast.makeText(ctx, "已清空保存的属性配置", Toast.LENGTH_SHORT).show();
    }

    private void scheduleAutoRestoreProps(final int cid) {
        if (cid == 0 || !flagAutoProps || savedPropVals.isEmpty() || lastAutoPropsCid == cid) return;
        lastAutoPropsCid = cid;
        final List<int[]> list = new ArrayList<int[]>();
        for (PropDef pd : PROP_DEFS) {
            if (!savedPropVals.containsKey(pd.key)) continue;
            int v = savedPropVals.get(pd.key);
            int lk = (savedPropLocks.containsKey(pd.key) && savedPropLocks.get(pd.key)) ? 1 : 0;
            list.add(new int[] { pd.key, v, lk });
        }
        if (list.isEmpty()) return;
        mainH.postDelayed(new Runnable() {
            @Override public void run() {
                if (lastSnap.optInt("scene", 0) == 0 || lastSnap.optInt("cid", 0) != cid) {
                    lastAutoPropsCid = 0;
                    return;
                }
                final int batchSize = 20;
                int chunks = (list.size() + batchSize - 1) / batchSize;
                for (int b = 0; b < chunks; b++) {
                    final int s = b * batchSize;
                    final int e = Math.min(list.size(), s + batchSize);
                    final boolean isLast = (b == chunks - 1);
                    mainH.postDelayed(new Runnable() {
                        @Override public void run() {
                            for (int i = s; i < e; i++) {
                                int[] row = list.get(i);
                                Menu.NativeQuery(3, row[0], row[1], row[2], "");
                                liveRaw.put(row[0], row[1]);
                                liveLocked.put(row[0], row[2] != 0);
                                dirtyPropRaw.put(row[0], row[1]);
                                editedPropText.remove(row[0]);
                            }
                            if (isLast) {
                                Menu.NativeQuery(9, 0, 0, 0L, "");
                                if (propSnapshotRefreshHook != null) propSnapshotRefreshHook.run();
                                Toast.makeText(ctx, "已自动加载保存的 " + list.size() + " 项属性配置", Toast.LENGTH_SHORT).show();
                            }
                        }
                    }, b * 140L);
                }
            }
        }, 650);
    }

    private void prepareDynEnvironmentAsync() {
        sBgExec.execute(new Runnable() {
            @Override public void run() {
                try {
                    File filesDir = ctx.getFilesDir();
                    if (filesDir != null) {
                        if (!filesDir.exists()) filesDir.mkdirs();
                        if (!apkIconsWritten) {
                            String[] bundled = XposedEntry.listModuleAssets(ctx, "items/icons");
                            if (bundled != null && bundled.length > 0) {
                                StringBuilder sb = new StringBuilder(bundled.length * 24);
                                for (String fn : bundled) {
                                    if (fn != null && fn.endsWith(".png")) {
                                        sb.append(fn, 0, fn.length() - 4).append('\n');
                                    }
                                }
                                File dst = new File(filesDir, "apk_icons.txt");
                                File tmp = new File(filesDir, "apk_icons.txt.tmp");
                                try (FileOutputStream fos = new FileOutputStream(tmp)) {
                                    fos.write(sb.toString().getBytes("UTF-8"));
                                }
                                if (dst.exists()) dst.delete();
                                tmp.renameTo(dst);
                                apkIconsWritten = true;
                            }
                        }
                        Menu.NativeQuery(12, 1, 0, 0L, filesDir.getAbsolutePath());
                    }
                } catch (Throwable ignored) {}
            }
        });
    }

    private int dp(int v) {
        return (int) TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, v, ctx.getResources().getDisplayMetrics());
    }

    private static GradientDrawable makeRoundBox(int fillColor, int strokeColor, int strokePx, float radiusPx) {
        GradientDrawable gd = new GradientDrawable();
        gd.setColor(fillColor);
        gd.setCornerRadius(radiusPx);
        if (strokePx > 0) gd.setStroke(strokePx, strokeColor);
        return gd;
    }

    private Button makePillBtn(String text, int bgColor, int hDp) {
        Button b = new Button(ctx);
        b.setText(text);
        b.setTextColor(Color.WHITE);
        b.setTextSize(11);
        b.setTypeface(null, Typeface.BOLD);
        b.setAllCaps(false);
        b.setPadding(dp(10), 0, dp(10), 0);
        b.setMinHeight(0);
        b.setMinimumHeight(0);
        b.setMinWidth(0);
        b.setMinimumWidth(0);
        b.setBackground(makeRoundBox(bgColor, 0x33FFFFFF, 1, dp(7)));
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(hDp));
        lp.setMargins(dp(3), 0, dp(3), 0);
        b.setLayoutParams(lp);
        return b;
    }

    private void styleEditBox(EditText et) {
        et.setTextColor(curTheme.textPri);
        et.setHintTextColor(curTheme.textSec);
        et.setBackground(makeRoundBox(curTheme.inputBg, curTheme.inputEdge, dp(1), dp(7)));
    }

    private EditText makeDarkEdit(String hint, boolean numeric) {
        EditText et = new EditText(ctx);
        et.setHint(hint);
        et.setTextSize(12);
        et.setSingleLine(true);
        et.setPadding(dp(8), dp(4), dp(8), dp(4));
        styleEditBox(et);
        if (numeric) {
            et.setInputType(InputType.TYPE_CLASS_NUMBER | InputType.TYPE_NUMBER_FLAG_DECIMAL | InputType.TYPE_NUMBER_FLAG_SIGNED);
        } else {
            et.setInputType(InputType.TYPE_CLASS_TEXT);
        }
        et.setImeOptions(0x10000006); // IME_ACTION_DONE | IME_FLAG_NO_EXTRACT_UI
        return et;
    }

    private void hideKeyboard(View v) {
        try {
            InputMethodManager imm = (InputMethodManager) ctx.getSystemService(Context.INPUT_METHOD_SERVICE);
            if (imm != null) imm.hideSoftInputFromWindow(v.getWindowToken(), 0);
        } catch (Throwable ignored) {}
    }

    private static String readAssetText(Context c, String rel) {
        try (InputStream is = XposedEntry.openModuleAsset(c, rel)) {
            if (is == null) return "";
            ByteArrayOutputStream bos = new ByteArrayOutputStream();
            byte[] buf = new byte[8192];
            int r;
            while ((r = is.read(buf)) > 0) bos.write(buf, 0, r);
            return new String(bos.toByteArray(), "UTF-8");
        } catch (Throwable ignored) {
            return "";
        }
    }

    private static String readFileText(File f) {
        if (f == null || !f.exists() || f.length() < 4) return "";
        try (FileInputStream fis = new FileInputStream(f)) {
            ByteArrayOutputStream bos = new ByteArrayOutputStream((int) f.length());
            byte[] buf = new byte[8192];
            int r;
            while ((r = fis.read(buf)) > 0) bos.write(buf, 0, r);
            return new String(bos.toByteArray(), "UTF-8");
        } catch (Throwable ignored) {
            return "";
        }
    }

    private static Bitmap decodeIconBitmap(Context c, String iconName) {
        if (iconName == null || iconName.isEmpty()) return null;
        // 1) Check dynamic extracted icons first (supports new hotupdate items/buffs)
        try {
            File f = new File(new File(c.getFilesDir(), "dyn_icons"), iconName.replace('/', '_') + ".png");
            if (f.exists() && f.length() > 64) {
                Bitmap bmp = BitmapFactory.decodeFile(f.getAbsolutePath());
                if (bmp != null) return bmp;
            }
        } catch (Throwable ignored) {}
        // 2) Bundled module assets
        String[] paths = new String[] { "items/icons/" + iconName + ".png", "items/" + iconName + ".png" };
        for (String rel : paths) {
            try (InputStream is = XposedEntry.openModuleAsset(c, rel)) {
                if (is != null) {
                    Bitmap bmp = BitmapFactory.decodeStream(is);
                    if (bmp != null) return bmp;
                }
            } catch (Throwable ignored) {}
        }
        return null;
    }

    private static Bitmap getIconSync(Context c, String iconName) {
        if (iconName == null || iconName.isEmpty() || sMissingIcons.contains(iconName)) return null;
        Bitmap cached = sIconCache.get(iconName);
        if (cached != null) return cached;
        Bitmap bmp = decodeIconBitmap(c, iconName);
        if (bmp != null) {
            sIconCache.put(iconName, bmp);
            return bmp;
        }
        sMissingIcons.add(iconName);
        return null;
    }

    /** Zero-stutter async icon loader for ListView cells. */
    private void bindIconAsync(final ImageView iv, final String iconName, final int fallbackBg) {
        if (iconName == null || iconName.isEmpty()) {
            iv.setTag(null);
            iv.setImageDrawable(null);
            iv.setBackgroundColor(fallbackBg);
            return;
        }
        Bitmap cached = sIconCache.get(iconName);
        if (cached != null) {
            iv.setTag(iconName);
            iv.setBackgroundColor(Color.TRANSPARENT);
            iv.setImageBitmap(cached);
            return;
        }
        iv.setTag(iconName);
        iv.setImageDrawable(null);
        iv.setBackgroundColor(fallbackBg);
        if (sMissingIcons.contains(iconName)) return;

        sIconExec.execute(new Runnable() {
            @Override public void run() {
                final Bitmap bmp = decodeIconBitmap(ctx, iconName);
                if (bmp != null) {
                    sIconCache.put(iconName, bmp);
                    mainH.post(new Runnable() {
                        @Override public void run() {
                            if (iconName.equals(iv.getTag())) {
                                iv.setBackgroundColor(Color.TRANSPARENT);
                                iv.setImageBitmap(bmp);
                            }
                        }
                    });
                } else {
                    sMissingIcons.add(iconName);
                }
            }
        });
    }

    private static synchronized List<ItemInfo> loadItems(Context c, boolean forceReload) {
        if (sItemsCache != null && !forceReload) return sItemsCache;
        List<ItemInfo> out = new ArrayList<ItemInfo>(1500);
        try {
            File dynF = new File(c.getFilesDir(), "dyn_items.json");
            String raw = readFileText(dynF);
            if (raw.length() < 100) raw = readAssetText(c, "items/items.json");

            // Load runtime subtype display strings if exported
            try {
                File subF = new File(c.getFilesDir(), "dyn_subtypes.json");
                String sraw = readFileText(subF);
                if (sraw.startsWith("{")) {
                    JSONObject sobj = new JSONObject(sraw);
                    Iterator<String> it = sobj.keys();
                    while (it.hasNext()) {
                        String ks = it.next();
                        int k = Integer.parseInt(ks);
                        String nm = sobj.optString(ks, "").trim();
                        if (!nm.isEmpty()) sDynSubtypeNames.put(k, k + " " + nm);
                    }
                }
            } catch (Throwable ignored) {}

            JSONArray arr = new JSONArray(raw);
            for (int i = 0; i < arr.length(); i++) {
                JSONObject o = arr.getJSONObject(i);
                ItemInfo it = new ItemInfo();
                it.id = o.optInt("id");
                it.name = o.optString("name", "#" + it.id);
                it.desc = o.optString("desc", "");
                it.detail = o.optString("detail", "");
                it.icon = o.optString("icon", "");
                it.cls = o.optInt("cls", 1);
                it.type = o.optInt("type", 0);
                it.bt = o.optInt("bt", 3);
                it.bst = o.optInt("bst", 34);
                it.prio = o.optInt("prio", 0);
                it.end = o.optInt("end", 0);
                it.bp = o.optInt("bp", 0);
                it.cantCombine = o.optInt("cantCombine", 0);
                out.add(it);
            }
            Collections.sort(out, new Comparator<ItemInfo>() {
                @Override public int compare(ItemInfo a, ItemInfo b) {
                    if (a.prio != b.prio) return (b.prio > a.prio) ? 1 : -1;
                    return (a.id < b.id) ? -1 : ((a.id > b.id) ? 1 : 0);
                }
            });
        } catch (Throwable ignored) {}
        if (!out.isEmpty()) sItemsCache = out;
        return sItemsCache != null ? sItemsCache : out;
    }

    private static synchronized List<BuffInfo> loadBuffs(Context c, boolean forceReload) {
        if (sBuffsCache != null && !forceReload) return sBuffsCache;
        List<BuffInfo> out = new ArrayList<BuffInfo>(520);
        try {
            File dynF = new File(c.getFilesDir(), "dyn_buffs.json");
            String raw = readFileText(dynF);
            if (raw.length() < 50) raw = readAssetText(c, "items/buffs.json");
            JSONArray arr = new JSONArray(raw);
            for (int i = 0; i < arr.length(); i++) {
                JSONObject o = arr.getJSONObject(i);
                BuffInfo bf = new BuffInfo();
                bf.id = o.optInt("id");
                bf.name = o.optString("name", "Buff #" + bf.id);
                bf.uiTitle = o.optString("uiTitle", "");
                bf.desc = o.optString("desc", "");
                bf.icon = o.optString("icon", "");
                bf.judge = o.optInt("judge", 0);
                bf.timeType = o.optInt("timeType", 0);
                bf.keepTime = o.optInt("keepTime", 0);
                bf.show = o.optInt("show", 0);
                out.add(bf);
            }
        } catch (Throwable ignored) {}
        if (!out.isEmpty()) sBuffsCache = out;
        return sBuffsCache != null ? sBuffsCache : out;
    }

    private static String resolveSubtypeLabel(int bst) {
        String fixed = SUBTYPE_NAMES.get(bst);
        if (fixed != null) return fixed;
        String dyn = sDynSubtypeNames.get(bst);
        if (dyn != null && !dyn.isEmpty()) return dyn;
        return bst + " 新增分类";
    }

    /** Convert game custom tags (<R>, <CB>, <CG>, <CR>, <CY>, <color=#...>) to clean colored HTML. */
    private static CharSequence formatGameRichText(String raw, int defaultColor) {
        if (raw == null || raw.isEmpty()) return "暂无说明";
        String s = raw.replace("\r\n", "\n").replace("\r", "\n");
        s = s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;");
        s = s.replace("\n", "<br/>")
             .replace("&lt;R&gt;", "<br/>")
             .replace("&lt;CB&gt;", "<font color='#60A5FA'>")
             .replace("&lt;CG&gt;", "<font color='#34D399'>")
             .replace("&lt;CR&gt;", "<font color='#F87171'>")
             .replace("&lt;CY&gt;", "<font color='#FBBF24'>")
             .replace("&lt;CEND&gt;", "</font>")
             .replace("&lt;/color&gt;", "</font>");
        s = s.replaceAll("(?i)&lt;color=#([0-9A-Fa-f]{6})[0-9A-Fa-f]{0,2}&gt;", "<font color='#$1'>");
        s = s.replaceAll("(?i)&lt;color=([^&]+)&gt;", "");
        return Html.fromHtml(s);
    }

    private static String stripGameTags(String raw) {
        if (raw == null || raw.isEmpty()) return "";
        String s = raw.replace("\r\n", " ").replace("\n", " ").replace("\r", " ")
                      .replace("<R>", " ").replace("<CB>", "").replace("<CG>", "")
                      .replace("<CR>", "").replace("<CY>", "").replace("<CEND>", "");
        s = s.replaceAll("(?i)</?color[^>]*>", "");
        return s.replaceAll("\\s+", " ").trim();
    }

    private static String formatPropDisplay(int raw, float scale) {
        if (scale <= 1.5f) return String.valueOf(raw);
        if ((raw % 1000) == 0) return String.valueOf(raw / 1000);
        String s = String.format(Locale.US, "%.3f", raw / scale);
        while (s.contains(".") && (s.endsWith("0") || s.endsWith("."))) {
            if (s.endsWith(".")) { s = s.substring(0, s.length() - 1); break; }
            s = s.substring(0, s.length() - 1);
        }
        return s;
    }

    // ==================== Build Main View ====================
    public View buildView() {
        DisplayMetrics dm = ctx.getResources().getDisplayMetrics();
        int panelW = Math.min(dm.widthPixels * 90 / 100, dp(780));
        int panelH = Math.min(dm.heightPixels * 90 / 100, dp(390));

        backdropView = new FrameLayout(ctx);
        backdropView.setBackgroundColor(curTheme.backdrop);
        backdropView.setLayoutParams(new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        backdropView.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { cb.onMinimize(); }
        });

        cardRoot = new LinearLayout(ctx);
        cardRoot.setOrientation(LinearLayout.VERTICAL);
        cardRoot.setBackground(makeRoundBox(curTheme.panelBg, curTheme.panelEdge, dp(2), dp(14)));
        cardRoot.setClickable(true);
        FrameLayout.LayoutParams cardLp = new FrameLayout.LayoutParams(panelW, panelH, Gravity.CENTER);
        cardRoot.setLayoutParams(cardLp);
        backdropView.addView(cardRoot);

        // ---------------- Top Header Bar ----------------
        headerBar = new LinearLayout(ctx);
        headerBar.setOrientation(LinearLayout.HORIZONTAL);
        headerBar.setGravity(Gravity.CENTER_VERTICAL);
        headerBar.setBackground(makeRoundBox(curTheme.headerBg, 0, 0, dp(12)));
        headerBar.setPadding(dp(12), dp(5), dp(8), dp(5));
        headerBar.setLayoutParams(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(40)));

        tvTitle = new TextView(ctx);
        tvTitle.setText("阿瑞斯病毒2 Mod 控制台");
        tvTitle.setTextColor(curTheme.textPri);
        tvTitle.setTextSize(14);
        tvTitle.setTypeface(null, Typeface.BOLD);
        headerBar.addView(tvTitle);

        tvLivePill = new TextView(ctx);
        tvLivePill.setText("  ● 初始化中...");
        tvLivePill.setTextColor(curTheme.emerald);
        tvLivePill.setTextSize(11);
        tvLivePill.setPadding(dp(10), dp(2), dp(8), dp(2));
        tvLivePill.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        tvLivePill.setSingleLine(true);
        tvLivePill.setEllipsize(TextUtils.TruncateAt.END);
        headerBar.addView(tvLivePill);

        btnHideTop = makePillBtn("隐藏", curTheme.btnSlate, 28);
        btnHideTop.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { cb.onHideIcon(); }
        });
        headerBar.addView(btnHideTop);

        btnMinTop = makePillBtn("收起 ✕", curTheme.btnRed, 28);
        btnMinTop.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { cb.onMinimize(); }
        });
        headerBar.addView(btnMinTop);

        cardRoot.addView(headerBar);

        hDivView = new View(ctx);
        hDivView.setBackgroundColor(curTheme.divider);
        hDivView.setLayoutParams(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 1));
        cardRoot.addView(hDivView);

        // ---------------- Body: Sidebar + Content ----------------
        LinearLayout body = new LinearLayout(ctx);
        body.setOrientation(LinearLayout.HORIZONTAL);
        body.setLayoutParams(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        cardRoot.addView(body);

        int sideW = dp(104);
        sidebarView = new LinearLayout(ctx);
        sidebarView.setOrientation(LinearLayout.VERTICAL);
        sidebarView.setBackgroundColor(curTheme.sidebarBg);
        sidebarView.setPadding(dp(4), dp(6), dp(4), dp(6));
        sidebarView.setLayoutParams(new LinearLayout.LayoutParams(sideW, ViewGroup.LayoutParams.MATCH_PARENT));
        body.addView(sidebarView);

        String[] tabTitles = new String[] {
            "🎒 物品发放", "📊 全部属性", "✨ Buff施加", "🛠️ 其他功能", "🎨 主题风格", "⚙️ 诊断设置"
        };
        for (int i = 0; i < tabTitles.length; i++) {
            final int tIdx = i;
            LinearLayout row = new LinearLayout(ctx);
            row.setOrientation(LinearLayout.HORIZONTAL);
            row.setGravity(Gravity.CENTER_VERTICAL);
            LinearLayout.LayoutParams rLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f);
            rLp.setMargins(dp(2), dp(2), dp(2), dp(2));
            row.setLayoutParams(rLp);

            View bar = new View(ctx);
            bar.setLayoutParams(new LinearLayout.LayoutParams(dp(3), dp(22)));
            row.addView(bar);

            TextView lbl = new TextView(ctx);
            lbl.setText(tabTitles[i]);
            lbl.setTextSize(11.5f);
            lbl.setPadding(dp(7), 0, dp(3), 0);
            lbl.setTypeface(null, Typeface.BOLD);
            row.addView(lbl);

            row.setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) { switchTab(tIdx); }
            });

            sideBtns[i] = row;
            sideBars[i] = bar;
            sideLabels[i] = lbl;
            sidebarView.addView(row);
        }

        vDivView = new View(ctx);
        vDivView.setBackgroundColor(curTheme.divider);
        vDivView.setLayoutParams(new LinearLayout.LayoutParams(1, ViewGroup.LayoutParams.MATCH_PARENT));
        body.addView(vDivView);

        FrameLayout contentWrap = new FrameLayout(ctx);
        contentWrap.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 1f));
        contentWrap.setPadding(dp(8), dp(6), dp(8), dp(6));
        body.addView(contentWrap);

        int availContentW = panelW - sideW - dp(18);
        pages[0] = buildItemsPage(availContentW);
        pages[1] = buildPropsPage();
        pages[2] = buildBuffsPage();
        pages[3] = buildOtherFeaturesPage();
        pages[4] = buildThemesPage();
        pages[5] = buildSettingsPage();

        for (int i = 0; i < pages.length; i++) {
            pages[i].setVisibility(i == 0 ? View.VISIBLE : View.GONE);
            contentWrap.addView(pages[i], new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        }

        buildItemDetailModal();
        backdropView.addView(detailModalOverlay);

        applyTheme(curThemeIdx, false);
        switchTab(0);
        startPoll();
        return backdropView;
    }

    private void buildItemDetailModal() {
        detailModalOverlay = new FrameLayout(ctx);
        detailModalOverlay.setBackgroundColor(0x88000000);
        detailModalOverlay.setVisibility(View.GONE);
        detailModalOverlay.setClickable(true);
        detailModalOverlay.setLayoutParams(new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        detailModalOverlay.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { detailModalOverlay.setVisibility(View.GONE); }
        });

        detailModalCard = new LinearLayout(ctx);
        detailModalCard.setOrientation(LinearLayout.VERTICAL);
        detailModalCard.setClickable(true);
        detailModalCard.setPadding(dp(14), dp(12), dp(14), dp(12));
        FrameLayout.LayoutParams mLp = new FrameLayout.LayoutParams(dp(430), ViewGroup.LayoutParams.WRAP_CONTENT, Gravity.CENTER);
        detailModalOverlay.addView(detailModalCard, mLp);

        LinearLayout top = new LinearLayout(ctx);
        top.setOrientation(LinearLayout.HORIZONTAL);
        top.setGravity(Gravity.CENTER_VERTICAL);
        detailModalIcon = new ImageView(ctx);
        LinearLayout.LayoutParams iLp = new LinearLayout.LayoutParams(dp(50), dp(50));
        iLp.setMargins(0, 0, dp(10), 0);
        top.addView(detailModalIcon, iLp);

        LinearLayout hdrInfo = new LinearLayout(ctx);
        hdrInfo.setOrientation(LinearLayout.VERTICAL);
        hdrInfo.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        detailModalTitle = new TextView(ctx);
        detailModalTitle.setTextSize(15);
        detailModalTitle.setTypeface(null, Typeface.BOLD);
        hdrInfo.addView(detailModalTitle);
        detailModalSub = new TextView(ctx);
        detailModalSub.setTextSize(11);
        detailModalSub.setPadding(0, dp(2), 0, 0);
        hdrInfo.addView(detailModalSub);
        top.addView(hdrInfo);
        detailModalCard.addView(top);

        ScrollView svBody = new ScrollView(ctx);
        LinearLayout.LayoutParams svLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(130));
        svLp.setMargins(0, dp(8), 0, dp(8));
        detailModalBody = new TextView(ctx);
        detailModalBody.setTextSize(12);
        detailModalBody.setLineSpacing(dp(3), 1.05f);
        svBody.addView(detailModalBody);
        detailModalCard.addView(svBody, svLp);

        LinearLayout bot = new LinearLayout(ctx);
        bot.setOrientation(LinearLayout.HORIZONTAL);
        bot.setGravity(Gravity.END);
        detailModalAdd1Btn = makePillBtn("发 x1", curTheme.btnGreen, 30);
        detailModalAdd999Btn = makePillBtn("发 x999", curTheme.btnBlue, 30);
        detailModalSelBtn = makePillBtn("选中/取消", curTheme.btnSlate, 30);
        detailModalCloseBtn = makePillBtn("关闭", curTheme.btnRed, 30);
        detailModalCloseBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { detailModalOverlay.setVisibility(View.GONE); }
        });
        bot.addView(detailModalAdd1Btn);
        bot.addView(detailModalAdd999Btn);
        bot.addView(detailModalSelBtn);
        bot.addView(detailModalCloseBtn);
        detailModalCard.addView(bot);
    }

    private void showItemDetailModal(final ItemInfo it, final Runnable onSelectToggle, final View.OnClickListener onAdd1, final View.OnClickListener onAdd999) {
        detailModalItem = it;
        detailModalCard.setBackground(makeRoundBox(curTheme.panelBg, curTheme.accent, dp(2), dp(14)));
        detailModalTitle.setText(it.name + "   #" + it.id);
        detailModalTitle.setTextColor(curTheme.textPri);
        String btName = (it.bt == 1 ? "武器工具" : (it.bt == 2 ? "补给种养" : (it.bt == 3 ? "物资材料" : (it.bt == 4 ? "收集收藏" : (it.bt == 5 ? "家园建造" : ("类" + it.bt))))));
        String meta = btName + " · " + resolveSubtypeLabel(it.bst);
        if (it.end > 0) meta += " · 耐久上限 " + it.end;
        if (it.cantCombine != 0) meta += " · 原限单格";
        detailModalSub.setText(meta);
        detailModalSub.setTextColor(curTheme.accent);

        String combined = it.desc;
        if (it.detail != null && !it.detail.isEmpty() && !it.detail.equals(it.desc)) {
            combined = (combined.isEmpty() ? "" : combined + "<R><R>") + it.detail;
        }
        detailModalBody.setText(formatGameRichText(combined, curTheme.textPri));
        detailModalBody.setTextColor(curTheme.textPri);

        Bitmap bmp = getIconSync(ctx, it.icon);
        if (bmp != null) detailModalIcon.setImageBitmap(bmp);
        else detailModalIcon.setImageDrawable(null);

        detailModalAdd1Btn.setOnClickListener(onAdd1);
        detailModalAdd999Btn.setOnClickListener(onAdd999);
        detailModalSelBtn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                onSelectToggle.run();
                detailModalOverlay.setVisibility(View.GONE);
            }
        });
        detailModalOverlay.setVisibility(View.VISIBLE);
    }

    private void applyTheme(int idx, boolean showToast) {
        if (idx < 0 || idx >= THEMES.length) idx = 0;
        curThemeIdx = idx;
        curTheme = THEMES[idx];
        themeVersion++;
        modPrefs.edit().putInt("theme_idx", idx).apply();

        if (backdropView != null) backdropView.setBackgroundColor(curTheme.backdrop);
        if (cardRoot != null) cardRoot.setBackground(makeRoundBox(curTheme.panelBg, curTheme.panelEdge, dp(2), dp(14)));
        if (headerBar != null) headerBar.setBackground(makeRoundBox(curTheme.headerBg, 0, 0, dp(12)));
        if (sidebarView != null) sidebarView.setBackgroundColor(curTheme.sidebarBg);
        if (hDivView != null) hDivView.setBackgroundColor(curTheme.divider);
        if (vDivView != null) vDivView.setBackgroundColor(curTheme.divider);
        if (tvTitle != null) tvTitle.setTextColor(curTheme.textPri);
        if (tvLivePill != null) tvLivePill.setTextColor(curTheme.emerald);
        if (btnHideTop != null) btnHideTop.setBackground(makeRoundBox(curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
        if (btnMinTop != null) btnMinTop.setBackground(makeRoundBox(curTheme.btnRed, 0x33FFFFFF, 1, dp(7)));

        if (diagCard != null) diagCard.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(10)));
        if (prefCard != null) prefCard.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(10)));
        if (tvDiagHdr != null) tvDiagHdr.setTextColor(curTheme.accent);
        if (tvDiagText != null) tvDiagText.setTextColor(curTheme.textPri);
        if (tvPrefText != null) tvPrefText.setTextColor(curTheme.textPri);
        if (tvOtherStatus != null) {
            tvOtherStatus.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(7)));
            tvOtherStatus.setTextColor(curTheme.accent);
        }

        syncTabButtonsTheme();
        syncToggleCardStyles();
        syncThemeSelectorCards();
        for (Runnable r : themeRefreshHooks) r.run();
        cb.onThemeChanged(curTheme.fabBg, curTheme.fabStroke, curTheme.fabText);

        if (showToast) {
            Toast.makeText(ctx, "已切换主题: " + curTheme.name, Toast.LENGTH_SHORT).show();
        }
    }

    private void syncTabButtonsTheme() {
        for (int i = 0; i < TAB_COUNT; i++) {
            boolean active = (i == curTab);
            if (sideBtns[i] != null) {
                sideBtns[i].setBackground(active ? makeRoundBox(curTheme.selBg, curTheme.selEdge, dp(1), dp(8)) : null);
                sideBars[i].setBackgroundColor(active ? curTheme.accent : Color.TRANSPARENT);
                sideLabels[i].setTextColor(active ? curTheme.textPri : curTheme.textSec);
            }
        }
    }

    private void syncThemeSelectorCards() {
        for (int i = 0; i < THEMES.length; i++) {
            if (themeCards[i] == null) continue;
            boolean active = (i == curThemeIdx);
            themeCards[i].setBackground(makeRoundBox(
                active ? curTheme.cardOnBg : curTheme.cardBg,
                active ? curTheme.accent : curTheme.cardEdge,
                dp(active ? 2 : 1), dp(10)));
            themeBadges[i].setText(active ? "✓ 当前使用" : "点击切换");
            themeBadges[i].setTextColor(active ? curTheme.emerald : curTheme.textSec);
            // Update first two text lines in card for light/dark readability
            View childCol = themeCards[i].getChildAt(1);
            if (childCol instanceof LinearLayout) {
                LinearLayout lc = (LinearLayout) childCol;
                if (lc.getChildCount() >= 2) {
                    ((TextView) lc.getChildAt(0)).setTextColor(curTheme.textPri);
                    ((TextView) lc.getChildAt(1)).setTextColor(curTheme.textSec);
                }
            }
        }
    }

    public void switchTab(int idx) {
        if (idx < 0 || idx >= TAB_COUNT) idx = 0;
        curTab = idx;
        for (int i = 0; i < pages.length; i++) {
            if (pages[i] != null) pages[i].setVisibility(i == idx ? View.VISIBLE : View.GONE);
        }
        syncTabButtonsTheme();
        if (idx == 0 && itemBoxesRefreshHook != null) itemBoxesRefreshHook.run();
        if (idx == 1) {
            Menu.NativeQuery(9, 0, 0, 0L, "");
            mainH.postDelayed(new Runnable() {
                @Override public void run() {
                    pollSnapshot();
                    if (propSnapshotRefreshHook != null) propSnapshotRefreshHook.run();
                }
            }, 120);
        }
        if (idx == 2 && buffActiveRefreshHook != null) buffActiveRefreshHook.run();
    }

    private void startPoll() {
        mainH.postDelayed(new Runnable() {
            @Override public void run() {
                pollSnapshot();
                mainH.postDelayed(this, 600);
            }
        }, 300);
    }

    public void pollSnapshot() {
        try {
            String raw = Menu.NativeQuery(1, 0, 0, 0L, "");
            if (raw == null || raw.isEmpty() || !raw.startsWith("{")) return;
            JSONObject obj = new JSONObject(raw);
            lastSnap = obj;

            boolean ready = obj.optInt("ready", 0) != 0;
            int scene = obj.optInt("scene", 0);
            int cid = obj.optInt("cid", 0);
            String st = obj.optString("status", "");
            long dynSeq = obj.optLong("dynSeq", 0);

            if (ready && !initialBridgeSynced) {
                initialBridgeSynced = true;
                syncAllFlagsToNative();
                File filesDir = ctx.getFilesDir();
                if (filesDir != null) Menu.NativeQuery(12, 1, 0, 0L, filesDir.getAbsolutePath());
            }
            if (!ready || scene == 0) {
                lastAutoPropsCid = 0;
            } else if (cid != 0) {
                scheduleAutoRestoreProps(cid);
            }

            if (dynSeq > 0 && dynSeq != lastDynSeq) {
                final boolean isUpdate = (lastDynSeq > 0);
                lastDynSeq = dynSeq;
                sBgExec.execute(new Runnable() {
                    @Override public void run() {
                        sMissingIcons.clear();
                        final List<ItemInfo> items = loadItems(ctx, true);
                        final List<BuffInfo> buffs = loadBuffs(ctx, true);
                        mainH.post(new Runnable() {
                            @Override public void run() {
                                if (itemDynReloadHook != null) itemDynReloadHook.run();
                                if (buffDynReloadHook != null) buffDynReloadHook.run();
                                boolean countsChanged = (lastToastItemCnt >= 0 && (items.size() != lastToastItemCnt || buffs.size() != lastToastBuffCnt));
                                lastToastItemCnt = items.size();
                                lastToastBuffCnt = buffs.size();
                                boolean showToast = manualDynSyncRequested || (isUpdate && countsChanged);
                                manualDynSyncRequested = false;
                                if (showToast) {
                                    Toast.makeText(ctx, "动态数据已刷新: 物品 " + items.size() + " / Buff " + buffs.size(), Toast.LENGTH_SHORT).show();
                                }
                            }
                        });
                    }
                });
            }

            // Pull flags from shm after initial sync
            if (initialBridgeSynced) {
                flagAC = obj.optInt("ac", 1) != 0;
                flagAmmo = obj.optInt("ammo", 0) != 0;
                flagEndur = obj.optInt("end", 0) != 0;
                flagPaid = obj.optInt("paid", 0) != 0;
                flagInstant = obj.optInt("inst", 0) != 0;
                flagInfStack = obj.optInt("infStk", 0) != 0;
                flagNoclip = obj.optInt("noclip", 0) != 0;
                flagMG = obj.optInt("mg", 0) != 0;
            }
            syncToggleCardStyles();

            JSONArray props = obj.optJSONArray("props");
            if (props != null) {
                for (int i = 0; i < props.length(); i++) {
                    JSONObject p = props.getJSONObject(i);
                    int k = p.optInt("k");
                    liveRaw.put(k, p.optInt("cur"));
                    liveLocked.put(k, p.optInt("lk") != 0);
                }
            }

            if (tvLivePill != null) {
                if (!ready) {
                    tvLivePill.setText("  ● 等待热更与场景...");
                    tvLivePill.setTextColor(0xFFFBBF24);
                } else if (scene == 0) {
                    tvLivePill.setText("  ● 桥接已就绪 · 请进入存档");
                    tvLivePill.setTextColor(curTheme.accent);
                } else {
                    int hp = liveRaw.containsKey(2) ? liveRaw.get(2) : 0;
                    int hpM = liveRaw.containsKey(1) ? liveRaw.get(1) : 0;
                    tvLivePill.setText("  ● 存档:" + cid + "  HP " + hp + "/" + hpM + (st.isEmpty() ? "" : (" · " + st)));
                    tvLivePill.setTextColor(curTheme.emerald);
                }
            }
            if (tvOtherStatus != null && !st.isEmpty()) {
                tvOtherStatus.setText("桥接反馈: " + st);
            }
            if (tvDiagText != null) {
                JSONArray boxes = obj.optJSONArray("boxes");
                JSONArray bfs = obj.optJSONArray("buffs");
                StringBuilder ds = new StringBuilder();
                ds.append("BridgeReady: ").append(ready ? "YES" : "NO").append("   Tick: ").append(obj.optLong("tick")).append('\n');
                ds.append("SceneReady: ").append(scene).append("   CustomID: ").append(cid).append("   DynSeq: ").append(dynSeq).append('\n');
                ds.append("FlagsBitmask: 0x").append(Integer.toHexString(obj.optInt("flags", 0)).toUpperCase(Locale.US))
                  .append("   MG Rate: ").append(String.format(Locale.US, "%.1f", obj.optInt("mgRate", 100) / 10.0f)).append(" /s\n");
                ds.append("物品库: ").append(sItemsCache != null ? sItemsCache.size() : 0)
                  .append("   Buff库: ").append(sBuffsCache != null ? sBuffsCache.size() : 0)
                  .append("   储物箱: ").append(boxes != null ? boxes.length() : 0)
                  .append("   ModBuff: ").append(bfs != null ? bfs.length() : 0).append('\n');
                ds.append("最近反馈: ").append(st);
                tvDiagText.setText(ds.toString());
            }
        } catch (Throwable ignored) {}
    }

    private long[] readCmdCounters() {
        long[] res = new long[] { 0L, 0L, 0L, 0L };
        try {
            String raw = Menu.NativeQuery(14, 0, 0, 0L, "");
            if (raw != null && raw.startsWith("{")) {
                JSONObject o = new JSONObject(raw);
                res[0] = o.optLong("pending", 0L);
                res[1] = o.optLong("okCnt", 0L);
                res[2] = o.optLong("failCnt", 0L);
                res[3] = o.optLong("lastFailId", 0L);
            }
        } catch (Throwable ignored) {}
        return res;
    }

    private void awaitBatchDrain(final long baseOk, final long baseFail, final int queuedCount, final String label, final Runnable afterDoneHook) {
        if (queuedCount <= 0) return;
        XposedEntry.boostFastPump(Math.max(4000L, queuedCount * 20L));
        Menu.NativeQuery(14, 0, 0, 0L, "");
        if (queuedCount <= 16) {
            mainH.postDelayed(new Runnable() {
                @Override public void run() {
                    XposedEntry.clearFastPump();
                    pollSnapshot();
                    if (afterDoneHook != null) afterDoneHook.run();
                }
            }, 220);
            return;
        }
        Toast.makeText(ctx, label + "中：共 " + queuedCount + " 项，请稍候...", Toast.LENGTH_SHORT).show();
        final int[] tries = new int[] { 0 };
        mainH.postDelayed(new Runnable() {
            @Override public void run() {
                tries[0]++;
                long[] cur = readCmdCounters();
                long pending = cur[0];
                if (pending > 0L && tries[0] < 150) {
                    mainH.postDelayed(this, 100);
                    return;
                }
                XposedEntry.clearFastPump();
                pollSnapshot();
                if (afterDoneHook != null) afterDoneHook.run();
                long okDelta = Math.max(0L, cur[1] - baseOk);
                long failDelta = Math.max(0L, cur[2] - baseFail);
                long lastFailId = cur[3];
                String msg;
                if (pending > 0L) {
                    msg = label + "部分仍在队列中（剩余 " + pending + " 项，已完成 " + okDelta + " 项）";
                } else if (failDelta > 0L) {
                    msg = label + "完成：成功 " + okDelta + " 项，跳过异常 " + failDelta + " 项" + (lastFailId > 0L ? (" (#" + lastFailId + ")") : "");
                } else {
                    msg = label + "完成：全部 " + queuedCount + " 项已生效";
                }
                Toast.makeText(ctx, msg, Toast.LENGTH_LONG).show();
            }
        }, 120);
    }

    private void syncAllFlagsToNative() {
        XposedEntry.antiCheatEnabled = flagAC;
        Menu.NativeQuery(10, 0, flagAC ? 1 : 0, 0L, "");
        Menu.NativeQuery(10, 1, flagAmmo ? 1 : 0, 0L, "");
        Menu.NativeQuery(10, 2, flagEndur ? 1 : 0, 0L, "");
        Menu.NativeQuery(10, 3, flagPaid ? 1 : 0, 0L, "");
        Menu.NativeQuery(10, 4, flagInstant ? 1 : 0, 0L, "");
        Menu.NativeQuery(10, 5, flagInfStack ? 1 : 0, 0L, "");
        Menu.NativeQuery(10, 6, flagNoclip ? 1 : 0, 0L, "");
        Menu.NativeQuery(10, 7, flagMG ? 1 : 0, 0L, "");
        Menu.NativeQuery(11, mgRateX10, 0, 0L, "");
    }

    private void styleToggleCard(LinearLayout card, TextView pill, boolean on, String onTxt, String offTxt, boolean warnWhenOff) {
        if (card == null) return;
        card.setBackground(makeRoundBox(on ? curTheme.cardOnBg : curTheme.cardBg, on ? curTheme.cardOnEdge : curTheme.cardEdge, dp(1), dp(10)));
        if (card.getChildCount() > 0 && card.getChildAt(0) instanceof TextView) {
            ((TextView) card.getChildAt(0)).setTextColor(curTheme.textPri);
        }
        if (card.getChildCount() > 1 && card.getChildAt(1) instanceof TextView) {
            ((TextView) card.getChildAt(1)).setTextColor(curTheme.textSec);
        }
        if (pill != null) {
            pill.setText(on ? onTxt : offTxt);
            pill.setTextColor(on ? curTheme.emerald : (warnWhenOff ? curTheme.danger : curTheme.textSec));
        }
    }

    private void syncToggleCardStyles() {
        styleToggleCard(cardAC, tvACState, flagAC, "● 已全拦截 (安全)", "○ 已关闭", true);
        styleToggleCard(cardAmmo, tvAmmoState, flagAmmo, "● 已开启 (+1)", "○ 未开启", false);
        styleToggleCard(cardEndur, tvEndurState, flagEndur, "● 已锁定满耐久", "○ 未开启", false);
        styleToggleCard(cardPaid, tvPaidState, flagPaid, "● 已本地解锁", "○ 点击开启解锁", false);
        styleToggleCard(cardInst, tvInstState, flagInstant, "● 0秒瞬时交互", "○ 原版耗时", false);
        styleToggleCard(cardInfStk, tvInfStkState, flagInfStack, "● 仓库无限堆叠", "○ 原版堆叠", false);
        styleToggleCard(cardNoclip, tvNoclipState, flagNoclip, "● 穿墙已开启", "○ 正常碰撞", false);
        styleToggleCard(cardMG, tvMGState, flagMG, "● 机关枪生效中", "○ 未开启", false);
        styleToggleCard(cardAutoProps, tvAutoPropState, flagAutoProps && !savedPropVals.isEmpty(),
                "● 自动加载(" + savedPropVals.size() + "项)",
                savedPropVals.isEmpty() ? "○ 未保存配置" : "○ 自动加载关(" + savedPropVals.size() + "项)", false);
        if (cardBp != null) {
            cardBp.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(10)));
            if (cardBp.getChildCount() > 0 && cardBp.getChildAt(0) instanceof TextView)
                ((TextView) cardBp.getChildAt(0)).setTextColor(curTheme.textPri);
            if (cardBp.getChildCount() > 1 && cardBp.getChildAt(1) instanceof TextView)
                ((TextView) cardBp.getChildAt(1)).setTextColor(curTheme.textSec);
        }
    }

    private LinearLayout makeToggleCard(String title, String sub, View.OnClickListener onClick) {
        LinearLayout card = new LinearLayout(ctx);
        card.setOrientation(LinearLayout.VERTICAL);
        card.setPadding(dp(10), dp(7), dp(10), dp(7));
        card.setOnClickListener(onClick);

        TextView t1 = new TextView(ctx);
        t1.setText(title);
        t1.setTextColor(curTheme.textPri);
        t1.setTextSize(12);
        t1.setSingleLine(true);
        t1.setEllipsize(TextUtils.TruncateAt.END);
        t1.setTypeface(null, Typeface.BOLD);
        card.addView(t1);

        TextView t2 = new TextView(ctx);
        t2.setText(sub);
        t2.setTextColor(curTheme.textSec);
        t2.setTextSize(10);
        t2.setPadding(0, dp(2), 0, dp(4));
        t2.setLayoutParams(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        card.addView(t2);
        return card;
    }

    private TextView attachStatusPill(LinearLayout card) {
        TextView tv = new TextView(ctx);
        tv.setTextSize(10.5f);
        tv.setTypeface(null, Typeface.BOLD);
        tv.setGravity(Gravity.CENTER_VERTICAL);
        tv.setMinHeight(dp(24));
        tv.setSingleLine(true);
        tv.setEllipsize(TextUtils.TruncateAt.END);
        card.addView(tv);
        return tv;
    }

    private LinearLayout.LayoutParams gridCardLp(int rMarginDp) {
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 1f);
        lp.setMargins(0, 0, dp(rMarginDp), 0);
        return lp;
    }

    // =========================================================================
    //  TAB 0: 物品发放 (4-5 列紧凑图标格 + 长按详情弹窗 + 1/999 + 全选/清空)
    // =========================================================================
    private FrameLayout buildItemsPage(int availContentW) {
        final FrameLayout wrap = new FrameLayout(ctx);
        LinearLayout root = new LinearLayout(ctx);
        root.setOrientation(LinearLayout.VERTICAL);
        wrap.addView(root, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        final List<ItemInfo> allItems = loadItems(ctx, false);
        final List<ItemInfo> filtered = new ArrayList<ItemInfo>(allItems);
        final Set<Integer> selected = new LinkedHashSet<Integer>();
        final int[] curBt = new int[] { 0 };
        final int[] curBst = new int[] { 0 };
        final int[] targetMode = new int[] { 0 };
        final long[] boxIds = new long[] { 0L };
        final List<Long> knownBoxIds = new ArrayList<Long>();
        final int cols = (availContentW >= dp(560)) ? 5 : 4;

        // Top Bar: Bag vs Box + Spinner + Search + Hint
        LinearLayout top = new LinearLayout(ctx);
        top.setOrientation(LinearLayout.HORIZONTAL);
        top.setGravity(Gravity.CENTER_VERTICAL);
        final Button btnBag = makePillBtn("🎒 背包", curTheme.btnBlue, 30);
        final Button btnBox = makePillBtn("📦 仓库", curTheme.btnSlate, 30);
        final Spinner spBox = new Spinner(ctx);
        spBox.setVisibility(View.GONE);
        LinearLayout.LayoutParams spLp = new LinearLayout.LayoutParams(dp(130), dp(30));
        spLp.setMargins(dp(4), 0, dp(4), 0);
        final ArrayAdapter<String> boxAdapter = new ArrayAdapter<String>(ctx, android.R.layout.simple_spinner_item, new ArrayList<String>());
        boxAdapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        spBox.setAdapter(boxAdapter);

        final EditText etSearch = makeDarkEdit("🔍 搜索名称 / ID (长按看详情)", false);
        etSearch.setLayoutParams(new LinearLayout.LayoutParams(0, dp(30), 1f));
        top.addView(btnBag);
        top.addView(btnBox);
        top.addView(spBox, spLp);
        top.addView(etSearch);
        root.addView(top);

        // Major BelongType + Dynamic SubType rows
        final HorizontalScrollView hsBt = new HorizontalScrollView(ctx);
        hsBt.setHorizontalScrollBarEnabled(false);
        final LinearLayout rowBt = new LinearLayout(ctx);
        rowBt.setOrientation(LinearLayout.HORIZONTAL);
        hsBt.addView(rowBt);
        LinearLayout.LayoutParams bLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(28));
        bLp.setMargins(0, dp(3), 0, dp(2));
        root.addView(hsBt, bLp);

        final HorizontalScrollView hsBst = new HorizontalScrollView(ctx);
        hsBst.setHorizontalScrollBarEnabled(false);
        final LinearLayout rowBst = new LinearLayout(ctx);
        rowBst.setOrientation(LinearLayout.HORIZONTAL);
        hsBst.addView(rowBst);
        LinearLayout.LayoutParams sLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(26));
        sLp.setMargins(0, 0, 0, dp(4));
        root.addView(hsBst, sLp);

        // 4 or 5-column ListView with ViewHolder (Zero stutter)
        final ListView lv = new ListView(ctx);
        lv.setDivider(null);
        lv.setDividerHeight(0);
        lv.setScrollingCacheEnabled(false);
        lv.setFastScrollEnabled(true);
        lv.setLayoutParams(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        root.addView(lv);

        // Bottom Bar: 1 / 999 / Qty Input / 全选 / 清空 / 发放
        LinearLayout bot = new LinearLayout(ctx);
        bot.setOrientation(LinearLayout.HORIZONTAL);
        bot.setGravity(Gravity.CENTER_VERTICAL);
        LinearLayout.LayoutParams botLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(36));
        botLp.setMargins(0, dp(4), 0, 0);

        final EditText etNum = makeDarkEdit("数量", true);
        etNum.setText("1");
        etNum.setGravity(Gravity.CENTER);
        LinearLayout.LayoutParams nLp = new LinearLayout.LayoutParams(dp(58), dp(30));
        nLp.setMargins(0, 0, dp(3), 0);
        bot.addView(etNum, nLp);

        final Button btnPreset1 = makePillBtn("1", curTheme.btnSlate, 28);
        btnPreset1.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { etNum.setText("1"); }
        });
        bot.addView(btnPreset1);

        final Button btnPreset999 = makePillBtn("999", curTheme.btnSlate, 28);
        btnPreset999.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { etNum.setText("999"); }
        });
        bot.addView(btnPreset999);

        final Button btnSelAll = makePillBtn("全选", curTheme.btnBlue, 30);
        final Button btnClear = makePillBtn("清空", curTheme.btnSlate, 30);
        final Button btnGive = makePillBtn("发放 (0)", curTheme.btnGreen, 30);
        LinearLayout.LayoutParams gLp = new LinearLayout.LayoutParams(0, dp(30), 1f);
        gLp.setMargins(dp(3), 0, 0, 0);
        bot.addView(btnSelAll);
        bot.addView(btnClear);
        bot.addView(btnGive, gLp);
        root.addView(bot, botLp);

        final Runnable updateSelBadge = new Runnable() {
            @Override public void run() { btnGive.setText("发放 (" + selected.size() + ")"); }
        };

        final class ItemTileHolder {
            LinearLayout card;
            ImageView icon;
            TextView tvName, tvId;
            boolean lastSel;
            int lastThemeVer = -1;
        }
        final class ItemRowHolder {
            final ItemTileHolder[] tiles = new ItemTileHolder[cols];
        }

        final BaseAdapter adapter = new BaseAdapter() {
            @Override public int getCount() { return (filtered.size() + cols - 1) / cols; }
            @Override public Object getItem(int pos) { return pos; }
            @Override public long getItemId(int pos) { return pos; }

            private void ensureTileStyle(ItemTileHolder h, boolean sel) {
                if (h.lastThemeVer != themeVersion || h.lastSel != sel) {
                    h.card.setBackground(makeRoundBox(
                        sel ? curTheme.selBg : curTheme.cardBg,
                        sel ? curTheme.accent : curTheme.cardEdge,
                        dp(sel ? 2 : 1), dp(8)));
                    h.tvName.setTextColor(curTheme.textPri);
                    h.tvId.setTextColor(sel ? curTheme.emerald : curTheme.accent);
                    h.lastSel = sel;
                    h.lastThemeVer = themeVersion;
                }
            }

            @Override public View getView(int pos, View conv, ViewGroup parent) {
                LinearLayout row;
                ItemRowHolder rh;
                if (conv instanceof LinearLayout && conv.getTag() instanceof ItemRowHolder) {
                    row = (LinearLayout) conv;
                    rh = (ItemRowHolder) conv.getTag();
                } else {
                    row = new LinearLayout(ctx);
                    row.setOrientation(LinearLayout.HORIZONTAL);
                    row.setPadding(0, dp(2), 0, dp(2));
                    rh = new ItemRowHolder();
                    for (int c = 0; c < cols; c++) {
                        ItemTileHolder th = new ItemTileHolder();
                        LinearLayout card = new LinearLayout(ctx);
                        card.setOrientation(LinearLayout.VERTICAL);
                        card.setGravity(Gravity.CENTER_HORIZONTAL);
                        card.setPadding(dp(4), dp(4), dp(4), dp(3));
                        LinearLayout.LayoutParams cLp = new LinearLayout.LayoutParams(0, dp(76), 1f);
                        cLp.setMargins(c == 0 ? 0 : dp(2), 0, c == cols - 1 ? 0 : dp(2), 0);

                        ImageView iv = new ImageView(ctx);
                        iv.setScaleType(ImageView.ScaleType.FIT_CENTER);
                        LinearLayout.LayoutParams iLp = new LinearLayout.LayoutParams(dp(30), dp(30));
                        card.addView(iv, iLp);

                        TextView tvName = new TextView(ctx);
                        tvName.setTextSize(10.5f);
                        tvName.setTypeface(null, Typeface.BOLD);
                        tvName.setGravity(Gravity.CENTER);
                        tvName.setMaxLines(2);
                        tvName.setEllipsize(TextUtils.TruncateAt.END);
                        LinearLayout.LayoutParams nmLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f);
                        nmLp.setMargins(0, dp(1), 0, 0);
                        card.addView(tvName, nmLp);

                        TextView tvId = new TextView(ctx);
                        tvId.setTextSize(9f);
                        tvId.setGravity(Gravity.CENTER);
                        tvId.setSingleLine(true);
                        card.addView(tvId);

                        th.card = card;
                        th.icon = iv;
                        th.tvName = tvName;
                        th.tvId = tvId;
                        rh.tiles[c] = th;
                        row.addView(card, cLp);
                    }
                    row.setTag(rh);
                }

                for (int c = 0; c < cols; c++) {
                    int idx = pos * cols + c;
                    final ItemTileHolder th = rh.tiles[c];
                    if (idx < filtered.size()) {
                        final ItemInfo it = filtered.get(idx);
                        th.card.setVisibility(View.VISIBLE);
                        th.tvName.setText(it.name);
                        th.tvId.setText("#" + it.id);
                        bindIconAsync(th.icon, it.icon, curTheme.inputBg);
                        ensureTileStyle(th, selected.contains(it.id));

                        th.card.setOnClickListener(new View.OnClickListener() {
                            @Override public void onClick(View v) {
                                boolean nowSel;
                                if (selected.contains(it.id)) { selected.remove(it.id); nowSel = false; }
                                else { selected.add(it.id); nowSel = true; }
                                ensureTileStyle(th, nowSel);
                                updateSelBadge.run();
                            }
                        });
                        th.card.setOnLongClickListener(new View.OnLongClickListener() {
                            @Override public boolean onLongClick(View v) {
                                showItemDetailModal(it,
                                    new Runnable() {
                                        @Override public void run() {
                                            boolean nowSel;
                                            if (selected.contains(it.id)) { selected.remove(it.id); nowSel = false; }
                                            else { selected.add(it.id); nowSel = true; }
                                            ensureTileStyle(th, nowSel);
                                            updateSelBadge.run();
                                        }
                                    },
                                    new View.OnClickListener() {
                                        @Override public void onClick(View bv) {
                                            String tStr = (targetMode[0] == 1) ? "1" : "0";
                                            Menu.NativeQuery(2, it.id, 1, boxIds[0], tStr);
                                            Toast.makeText(ctx, "已发放 " + it.name + " x1", Toast.LENGTH_SHORT).show();
                                        }
                                    },
                                    new View.OnClickListener() {
                                        @Override public void onClick(View bv) {
                                            String tStr = (targetMode[0] == 1) ? "1" : "0";
                                            Menu.NativeQuery(2, it.id, 999, boxIds[0], tStr);
                                            Toast.makeText(ctx, "已发放 " + it.name + " x999", Toast.LENGTH_SHORT).show();
                                        }
                                    });
                                return true;
                            }
                        });
                    } else {
                        th.card.setVisibility(View.INVISIBLE);
                        th.card.setOnClickListener(null);
                        th.card.setOnLongClickListener(null);
                    }
                }
                return row;
            }
        };
        lv.setAdapter(adapter);

        final List<Button> btBtns = new ArrayList<Button>();
        final List<Button> bstBtns = new ArrayList<Button>();
        final List<Integer> bstKeys = new ArrayList<Integer>();

        final Runnable applyFilter = new Runnable() {
            @Override public void run() {
                String kw = etSearch.getText().toString().trim().toLowerCase(Locale.ROOT);
                filtered.clear();
                List<ItemInfo> src = (sItemsCache != null) ? sItemsCache : allItems;
                for (int i = 0; i < src.size(); i++) {
                    ItemInfo it = src.get(i);
                    if (curBt[0] != 0 && it.bt != curBt[0]) continue;
                    if (curBst[0] != 0 && it.bst != curBst[0]) continue;
                    if (!kw.isEmpty()) {
                        boolean hit = it.name.toLowerCase(Locale.ROOT).contains(kw)
                                   || String.valueOf(it.id).contains(kw)
                                   || it.desc.toLowerCase(Locale.ROOT).contains(kw);
                        if (!hit) continue;
                    }
                    filtered.add(it);
                }
                adapter.notifyDataSetChanged();
            }
        };

        final Runnable rebuildSubBar = new Runnable() {
            @Override public void run() {
                rowBst.removeAllViews();
                bstBtns.clear();
                bstKeys.clear();
                // Dynamically collect real distinct UIBelongSubType values matching curBt[0]!
                TreeSet<Integer> dynSub = new TreeSet<Integer>();
                List<ItemInfo> src = (sItemsCache != null) ? sItemsCache : allItems;
                for (int i = 0; i < src.size(); i++) {
                    ItemInfo it = src.get(i);
                    if (curBt[0] == 0 || it.bt == curBt[0]) {
                        dynSub.add(it.bst);
                    }
                }
                bstKeys.add(0);
                bstKeys.addAll(dynSub);
                if (!bstKeys.contains(curBst[0])) curBst[0] = 0;

                for (final int k : bstKeys) {
                    String label = (k == 0) ? "全部小类" : resolveSubtypeLabel(k);
                    Button sb = makePillBtn(label, k == curBst[0] ? curTheme.btnBlue : curTheme.btnSlate, 24);
                    sb.setTextSize(10);
                    sb.setOnClickListener(new View.OnClickListener() {
                        @Override public void onClick(View v) {
                            curBst[0] = k;
                            for (int i = 0; i < bstKeys.size(); i++) {
                                bstBtns.get(i).setBackground(makeRoundBox(
                                    bstKeys.get(i) == curBst[0] ? curTheme.btnBlue : curTheme.btnSlate,
                                    0x33FFFFFF, 1, dp(7)));
                            }
                            applyFilter.run();
                        }
                    });
                    bstBtns.add(sb);
                    rowBst.addView(sb);
                }
            }
        };

        final int[] btIds = new int[] { 0, 1, 2, 3, 4, 5 };
        final String[] btNames = new String[] { "全部", "1 武器工具", "2 补给种养", "3 物资材料", "4 收集收藏", "5 家园建造" };
        for (int i = 0; i < btIds.length; i++) {
            final int id = btIds[i];
            Button b = makePillBtn(btNames[i], id == 0 ? curTheme.btnBlue : curTheme.btnSlate, 26);
            b.setTextSize(10.5f);
            b.setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) {
                    curBt[0] = id;
                    curBst[0] = 0;
                    for (int j = 0; j < btIds.length; j++) {
                        btBtns.get(j).setBackground(makeRoundBox(
                            btIds[j] == curBt[0] ? curTheme.btnBlue : curTheme.btnSlate,
                            0x33FFFFFF, 1, dp(7)));
                    }
                    rebuildSubBar.run();
                    applyFilter.run();
                }
            });
            btBtns.add(b);
            rowBt.addView(b);
        }
        rebuildSubBar.run();
        applyFilter.run();

        etSearch.addTextChangedListener(new TextWatcher() {
            @Override public void beforeTextChanged(CharSequence s, int a, int b, int c) {}
            @Override public void onTextChanged(CharSequence s, int a, int b, int c) {}
            @Override public void afterTextChanged(Editable s) { applyFilter.run(); }
        });

        itemBoxesRefreshHook = new Runnable() {
            @Override public void run() {
                try {
                    JSONArray arr = lastSnap.optJSONArray("boxes");
                    knownBoxIds.clear();
                    boxAdapter.clear();
                    if (arr != null && arr.length() > 0) {
                        for (int i = 0; i < arr.length(); i++) {
                            JSONObject b = arr.getJSONObject(i);
                            long id = b.optLong("id");
                            knownBoxIds.add(id);
                            boxAdapter.add("箱#" + id + " (" + b.optInt("used") + "/" + b.optInt("cap") + ")");
                        }
                        if (boxIds[0] == 0L || !knownBoxIds.contains(boxIds[0])) boxIds[0] = knownBoxIds.get(0);
                    } else {
                        knownBoxIds.add(0L);
                        boxAdapter.add("默认家园储物箱");
                        boxIds[0] = 0L;
                    }
                    boxAdapter.notifyDataSetChanged();
                } catch (Throwable ignored) {}
            }
        };

        itemDynReloadHook = new Runnable() {
            @Override public void run() {
                rebuildSubBar.run();
                applyFilter.run();
            }
        };

        spBox.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
            @Override public void onItemSelected(AdapterView<?> p, View v, int pos, long id) {
                if (pos >= 0 && pos < knownBoxIds.size()) boxIds[0] = knownBoxIds.get(pos);
            }
            @Override public void onNothingSelected(AdapterView<?> p) {}
        });

        btnBag.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                targetMode[0] = 0;
                btnBag.setBackground(makeRoundBox(curTheme.btnBlue, 0x33FFFFFF, 1, dp(7)));
                btnBox.setBackground(makeRoundBox(curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                spBox.setVisibility(View.GONE);
            }
        });
        btnBox.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                targetMode[0] = 1;
                btnBox.setBackground(makeRoundBox(curTheme.btnBlue, 0x33FFFFFF, 1, dp(7)));
                btnBag.setBackground(makeRoundBox(curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                spBox.setVisibility(View.VISIBLE);
                if (itemBoxesRefreshHook != null) itemBoxesRefreshHook.run();
            }
        });

        btnSelAll.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                for (int i = 0; i < filtered.size(); i++) selected.add(filtered.get(i).id);
                updateSelBadge.run();
                adapter.notifyDataSetChanged();
                Toast.makeText(ctx, "已全选当前分类共 " + filtered.size() + " 项", Toast.LENGTH_SHORT).show();
            }
        });

        btnClear.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                selected.clear();
                updateSelBadge.run();
                adapter.notifyDataSetChanged();
            }
        });

        btnGive.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                hideKeyboard(v);
                if (selected.isEmpty()) {
                    Toast.makeText(ctx, "请先点击选择物品 (长按可查看详情)", Toast.LENGTH_SHORT).show();
                    return;
                }
                int num = 1;
                try { num = Math.max(1, Integer.parseInt(etNum.getText().toString().trim())); } catch (Throwable ignored) {}
                String tStr = (targetMode[0] == 1) ? "1" : "0";
                long bId = (targetMode[0] == 1) ? boxIds[0] : 0L;
                ArrayList<Integer> batch = new ArrayList<Integer>(selected.size());
                HashSet<Integer> seen = new HashSet<Integer>(selected.size());
                for (int i = 0; i < filtered.size(); i++) {
                    int id = filtered.get(i).id;
                    if (selected.contains(id) && seen.add(id)) batch.add(id);
                }
                for (Integer itemId : selected) {
                    if (seen.add(itemId)) batch.add(itemId);
                }
                long[] base = readCmdCounters();
                int sent = 0;
                for (int i = 0; i < batch.size(); i++) {
                    if ("1".equals(Menu.NativeQuery(2, batch.get(i), num, bId, tStr))) {
                        sent++;
                    }
                }
                String destName = (targetMode[0] == 1) ? "仓库" : "背包";
                if (sent <= 16) {
                    Toast.makeText(ctx, "已发送 " + sent + " 种物品 x" + num + " → " + destName, Toast.LENGTH_SHORT).show();
                }
                awaitBatchDrain(base[1], base[2], sent, "物品发放(" + destName + " x" + num + ")", null);
            }
        });

        themeRefreshHooks.add(new Runnable() {
            @Override public void run() {
                styleEditBox(etSearch);
                styleEditBox(etNum);
                btnBag.setBackground(makeRoundBox(targetMode[0] == 0 ? curTheme.btnBlue : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                btnBox.setBackground(makeRoundBox(targetMode[0] == 1 ? curTheme.btnBlue : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                btnPreset1.setBackground(makeRoundBox(curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                btnPreset999.setBackground(makeRoundBox(curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                btnSelAll.setBackground(makeRoundBox(curTheme.btnBlue, 0x33FFFFFF, 1, dp(7)));
                btnClear.setBackground(makeRoundBox(curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                btnGive.setBackground(makeRoundBox(curTheme.btnGreen, 0x33FFFFFF, 1, dp(7)));
                for (int j = 0; j < btIds.length; j++) {
                    btBtns.get(j).setBackground(makeRoundBox(btIds[j] == curBt[0] ? curTheme.btnBlue : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                }
                for (int i = 0; i < bstKeys.size(); i++) {
                    bstBtns.get(i).setBackground(makeRoundBox(bstKeys.get(i) == curBst[0] ? curTheme.btnBlue : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                }
                adapter.notifyDataSetChanged();
            }
        });

        return wrap;
    }

    // =========================================================================
    //  TAB 1: 人物全部属性 (50 项实时读取 / 防卡顿 ViewHolder / 清晰分类)
    // =========================================================================
    private FrameLayout buildPropsPage() {
        FrameLayout wrap = new FrameLayout(ctx);
        LinearLayout root = new LinearLayout(ctx);
        root.setOrientation(LinearLayout.VERTICAL);
        wrap.addView(root, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        final List<PropDef> filtered = new ArrayList<PropDef>();
        Collections.addAll(filtered, PROP_DEFS);
        final int[] curGrp = new int[] { -1 };

        LinearLayout top = new LinearLayout(ctx);
        top.setOrientation(LinearLayout.HORIZONTAL);
        top.setGravity(Gravity.CENTER_VERTICAL);
        final EditText etSearch = makeDarkEdit("🔍 搜索属性名 / key...", false);
        etSearch.setLayoutParams(new LinearLayout.LayoutParams(0, dp(30), 1f));
        top.addView(etSearch);
        final Button btnRefresh = makePillBtn("刷新实时值", curTheme.btnBlue, 30);
        final Button btnUnlockAll = makePillBtn("解全部锁", curTheme.btnSlate, 30);
        final Button btnApplyAll = makePillBtn("批量应用修改", curTheme.btnGreen, 30);
        top.addView(btnRefresh);
        top.addView(btnUnlockAll);
        top.addView(btnApplyAll);
        root.addView(top);

        HorizontalScrollView hsGrp = new HorizontalScrollView(ctx);
        hsGrp.setHorizontalScrollBarEnabled(false);
        final LinearLayout rowGrp = new LinearLayout(ctx);
        rowGrp.setOrientation(LinearLayout.HORIZONTAL);
        hsGrp.addView(rowGrp);
        LinearLayout.LayoutParams gLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(28));
        gLp.setMargins(0, dp(3), 0, dp(4));
        root.addView(hsGrp, gLp);

        final ListView lv = new ListView(ctx);
        lv.setDivider(null);
        lv.setDividerHeight(0);
        lv.setScrollingCacheEnabled(false);
        lv.setDescendantFocusability(ViewGroup.FOCUS_AFTER_DESCENDANTS);
        lv.setLayoutParams(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        root.addView(lv);

        final class PropCellHolder {
            LinearLayout card;
            TextView tvName, tvCur;
            EditText etVal;
            Button btnLock, btnApply;
            PropDef boundPd;
            boolean binding = false;
            boolean lastLocked = false;
            int lastThemeVer = -1;
        }
        final class PropRowHolder {
            PropCellHolder left, right;
        }

        final BaseAdapter adapter = new BaseAdapter() {
            @Override public int getCount() { return (filtered.size() + 1) / 2; }
            @Override public Object getItem(int pos) { return pos; }
            @Override public long getItemId(int pos) { return pos; }

            private PropCellHolder makeCell() {
                final PropCellHolder h = new PropCellHolder();
                LinearLayout c = new LinearLayout(ctx);
                c.setOrientation(LinearLayout.HORIZONTAL);
                c.setGravity(Gravity.CENTER_VERTICAL);
                c.setPadding(dp(8), dp(4), dp(8), dp(4));

                LinearLayout info = new LinearLayout(ctx);
                info.setOrientation(LinearLayout.VERTICAL);
                info.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
                TextView tvName = new TextView(ctx);
                tvName.setTextSize(11);
                tvName.setTypeface(null, Typeface.BOLD);
                tvName.setSingleLine(true);
                tvName.setEllipsize(TextUtils.TruncateAt.END);
                TextView tvCur = new TextView(ctx);
                tvCur.setTextSize(9.5f);
                tvCur.setSingleLine(true);
                info.addView(tvName);
                info.addView(tvCur);
                c.addView(info);

                final EditText et = makeDarkEdit("值", true);
                et.setGravity(Gravity.CENTER);
                LinearLayout.LayoutParams eLp = new LinearLayout.LayoutParams(dp(68), dp(28));
                eLp.setMargins(dp(4), 0, dp(3), 0);
                c.addView(et, eLp);

                Button btnLock = makePillBtn("🔓", curTheme.btnSlate, 28);
                Button btnGo = makePillBtn("✓", curTheme.btnBlue, 28);
                c.addView(btnLock);
                c.addView(btnGo);

                h.card = c;
                h.tvName = tvName;
                h.tvCur = tvCur;
                h.etVal = et;
                h.btnLock = btnLock;
                h.btnApply = btnGo;

                et.addTextChangedListener(new TextWatcher() {
                    @Override public void beforeTextChanged(CharSequence s, int a, int b, int c2) {}
                    @Override public void onTextChanged(CharSequence s, int a, int b, int c2) {}
                    @Override public void afterTextChanged(Editable s) {
                        if (!h.binding && h.boundPd != null) {
                            editedPropText.put(h.boundPd.key, s.toString());
                        }
                    }
                });
                return h;
            }

            private void ensureStyle(PropCellHolder h, boolean lkd) {
                if (h.lastThemeVer != themeVersion || h.lastLocked != lkd) {
                    h.card.setBackground(makeRoundBox(
                        lkd ? curTheme.cardOnBg : curTheme.cardBg,
                        lkd ? curTheme.cardOnEdge : curTheme.cardEdge,
                        dp(1), dp(8)));
                    h.tvName.setTextColor(curTheme.textPri);
                    h.tvCur.setTextColor(lkd ? curTheme.emerald : curTheme.accent);
                    styleEditBox(h.etVal);
                    h.btnLock.setBackground(makeRoundBox(lkd ? curTheme.btnGreen : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                    h.btnApply.setBackground(makeRoundBox(curTheme.btnBlue, 0x33FFFFFF, 1, dp(7)));
                    h.lastLocked = lkd;
                    h.lastThemeVer = themeVersion;
                }
                h.btnLock.setText(lkd ? "🔒" : "🔓");
            }

            private void bindCell(final PropCellHolder h, final PropDef pd) {
                h.boundPd = pd;
                h.tvName.setText(pd.shortName);
                int curR = liveRaw.containsKey(pd.key) ? liveRaw.get(pd.key) : 0;
                boolean lkd = liveLocked.containsKey(pd.key) && liveLocked.get(pd.key);
                String defDisp = formatPropDisplay(curR, pd.scale);
                h.tvCur.setText("实时: " + defDisp + (lkd ? " [已锁]" : ""));

                String showVal = editedPropText.containsKey(pd.key) ? editedPropText.get(pd.key) : defDisp;
                h.binding = true;
                if (!h.etVal.hasFocus() && !showVal.equals(h.etVal.getText().toString())) {
                    h.etVal.setText(showVal);
                }
                h.binding = false;
                ensureStyle(h, lkd);

                h.btnApply.setOnClickListener(new View.OnClickListener() {
                    @Override public void onClick(View v) {
                        hideKeyboard(v);
                        try {
                            String txt = h.etVal.getText().toString().trim();
                            int raw = (int) Math.round(Double.parseDouble(txt) * pd.scale);
                            boolean curLkd = liveLocked.containsKey(pd.key) && liveLocked.get(pd.key);
                            Menu.NativeQuery(3, pd.key, raw, curLkd ? 1L : 0L, "");
                            editedPropText.remove(pd.key);
                            liveRaw.put(pd.key, raw);
                            dirtyPropRaw.put(pd.key, raw);
                            Toast.makeText(ctx, pd.shortName + " → " + txt, Toast.LENGTH_SHORT).show();
                            mainH.postDelayed(new Runnable() { @Override public void run() { pollSnapshot(); notifyDataSetChanged(); } }, 160);
                        } catch (Throwable ex) {
                            Toast.makeText(ctx, "数值格式无效", Toast.LENGTH_SHORT).show();
                        }
                    }
                });

                h.btnLock.setOnClickListener(new View.OnClickListener() {
                    @Override public void onClick(View v) {
                        hideKeyboard(v);
                        try {
                            String txt = h.etVal.getText().toString().trim();
                            int raw = txt.isEmpty() ? (liveRaw.containsKey(pd.key) ? liveRaw.get(pd.key) : 0)
                                                    : (int) Math.round(Double.parseDouble(txt) * pd.scale);
                            boolean nextLkd = !(liveLocked.containsKey(pd.key) && liveLocked.get(pd.key));
                            Menu.NativeQuery(3, pd.key, raw, nextLkd ? 1L : 0L, "");
                            editedPropText.remove(pd.key);
                            liveRaw.put(pd.key, raw);
                            liveLocked.put(pd.key, nextLkd);
                            dirtyPropRaw.put(pd.key, raw);
                            ensureStyle(h, nextLkd);
                            h.tvCur.setText("实时: " + formatPropDisplay(raw, pd.scale) + (nextLkd ? " [已锁]" : ""));
                        } catch (Throwable ex) {
                            Toast.makeText(ctx, "锁定失败: 数值无效", Toast.LENGTH_SHORT).show();
                        }
                    }
                });
            }

            @Override public View getView(int pos, View conv, ViewGroup parent) {
                LinearLayout row;
                PropRowHolder rh;
                if (conv instanceof LinearLayout && conv.getTag() instanceof PropRowHolder) {
                    row = (LinearLayout) conv;
                    rh = (PropRowHolder) conv.getTag();
                } else {
                    row = new LinearLayout(ctx);
                    row.setOrientation(LinearLayout.HORIZONTAL);
                    row.setPadding(0, dp(2), 0, dp(2));
                    rh = new PropRowHolder();
                    rh.left = makeCell();
                    rh.right = makeCell();
                    LinearLayout.LayoutParams lp1 = new LinearLayout.LayoutParams(0, dp(52), 1f);
                    lp1.setMargins(0, 0, dp(3), 0);
                    LinearLayout.LayoutParams lp2 = new LinearLayout.LayoutParams(0, dp(52), 1f);
                    lp2.setMargins(dp(3), 0, 0, 0);
                    row.addView(rh.left.card, lp1);
                    row.addView(rh.right.card, lp2);
                    row.setTag(rh);
                }
                int i1 = pos * 2, i2 = pos * 2 + 1;
                bindCell(rh.left, filtered.get(i1));
                if (i2 < filtered.size()) {
                    rh.right.card.setVisibility(View.VISIBLE);
                    bindCell(rh.right, filtered.get(i2));
                } else {
                    rh.right.boundPd = null;
                    rh.right.card.setVisibility(View.INVISIBLE);
                }
                return row;
            }
        };
        lv.setAdapter(adapter);

        final int[] grpIds = new int[] { -1, 0, 1, 2, 3, 4, 5, 6 };
        final String[] grpNames = new String[] { "全部(50)", "生命(2)", "战斗输出(12)", "移动速度(8)", "防御护盾(5)", "体/饥/精(12)", "声音/辅助(3)", "塔防(8)" };
        final List<Button> grpBtns = new ArrayList<Button>();

        final Runnable applyFilter = new Runnable() {
            @Override public void run() {
                String kw = etSearch.getText().toString().trim().toLowerCase(Locale.ROOT);
                filtered.clear();
                for (PropDef pd : PROP_DEFS) {
                    if (curGrp[0] != -1 && pd.group != curGrp[0]) continue;
                    if (!kw.isEmpty() && !pd.shortName.toLowerCase(Locale.ROOT).contains(kw) && !String.valueOf(pd.key).contains(kw)) continue;
                    filtered.add(pd);
                }
                adapter.notifyDataSetChanged();
            }
        };

        for (int i = 0; i < grpIds.length; i++) {
            final int gid = grpIds[i];
            Button b = makePillBtn(grpNames[i], gid == -1 ? curTheme.btnBlue : curTheme.btnSlate, 26);
            b.setTextSize(10.5f);
            b.setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) {
                    curGrp[0] = gid;
                    for (int j = 0; j < grpIds.length; j++) {
                        grpBtns.get(j).setBackground(makeRoundBox(
                            grpIds[j] == curGrp[0] ? curTheme.btnBlue : curTheme.btnSlate,
                            0x33FFFFFF, 1, dp(7)));
                    }
                    applyFilter.run();
                }
            });
            grpBtns.add(b);
            rowGrp.addView(b);
        }

        etSearch.addTextChangedListener(new TextWatcher() {
            @Override public void beforeTextChanged(CharSequence s, int a, int b, int c) {}
            @Override public void onTextChanged(CharSequence s, int a, int b, int c) {}
            @Override public void afterTextChanged(Editable s) { applyFilter.run(); }
        });

        propSnapshotRefreshHook = new Runnable() {
            @Override public void run() { adapter.notifyDataSetChanged(); }
        };

        btnRefresh.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                Menu.NativeQuery(9, 0, 0, 0L, "");
                editedPropText.clear();
                mainH.postDelayed(new Runnable() { @Override public void run() { pollSnapshot(); adapter.notifyDataSetChanged(); } }, 180);
                Toast.makeText(ctx, "已重新读取全部 50 项人物属性", Toast.LENGTH_SHORT).show();
            }
        });

        btnUnlockAll.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                Menu.NativeQuery(8, 0, 0, 0L, "");
                liveLocked.clear();
                adapter.notifyDataSetChanged();
                Toast.makeText(ctx, "已清除全部属性锁定", Toast.LENGTH_SHORT).show();
            }
        });

        btnApplyAll.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                hideKeyboard(v);
                int applied = 0;
                for (PropDef pd : PROP_DEFS) {
                    if (!editedPropText.containsKey(pd.key)) continue;
                    try {
                        int raw = (int) Math.round(Double.parseDouble(editedPropText.get(pd.key).trim()) * pd.scale);
                        boolean lkd = liveLocked.containsKey(pd.key) && liveLocked.get(pd.key);
                        Menu.NativeQuery(3, pd.key, raw, lkd ? 1L : 0L, "");
                        dirtyPropRaw.put(pd.key, raw);
                        applied++;
                    } catch (Throwable ignored) {}
                }
                editedPropText.clear();
                Toast.makeText(ctx, "已批量提交 " + applied + " 项修改", Toast.LENGTH_SHORT).show();
                mainH.postDelayed(new Runnable() { @Override public void run() { pollSnapshot(); adapter.notifyDataSetChanged(); } }, 180);
            }
        });

        themeRefreshHooks.add(new Runnable() {
            @Override public void run() {
                styleEditBox(etSearch);
                btnRefresh.setBackground(makeRoundBox(curTheme.btnBlue, 0x33FFFFFF, 1, dp(7)));
                btnUnlockAll.setBackground(makeRoundBox(curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                btnApplyAll.setBackground(makeRoundBox(curTheme.btnGreen, 0x33FFFFFF, 1, dp(7)));
                for (int j = 0; j < grpIds.length; j++) {
                    grpBtns.get(j).setBackground(makeRoundBox(grpIds[j] == curGrp[0] ? curTheme.btnBlue : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                }
                adapter.notifyDataSetChanged();
            }
        });

        return wrap;
    }

    // =========================================================================
    //  TAB 2: Buff 状态选择器 (异步图标无卡顿 + 修复 BuffJudge=-1 负面分类)
    // =========================================================================
    private FrameLayout buildBuffsPage() {
        FrameLayout wrap = new FrameLayout(ctx);
        LinearLayout root = new LinearLayout(ctx);
        root.setOrientation(LinearLayout.VERTICAL);
        wrap.addView(root, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        final List<BuffInfo> allBuffs = loadBuffs(ctx, false);
        final List<BuffInfo> filtered = new ArrayList<BuffInfo>(allBuffs);
        final Set<Integer> selected = new LinkedHashSet<Integer>();
        final int[] judgeFilter = new int[] { 99 }; // 99=All, 1=Good(+), -1=Bad(-), 0=Normal(·)

        LinearLayout top = new LinearLayout(ctx);
        top.setOrientation(LinearLayout.HORIZONTAL);
        top.setGravity(Gravity.CENTER_VERTICAL);
        final EditText etSearch = makeDarkEdit("🔍 搜索 Buff 名称 / ID / 效果...", false);
        etSearch.setLayoutParams(new LinearLayout.LayoutParams(0, dp(30), 1f));
        top.addView(etSearch);
        final Button btnAll = makePillBtn("全部", curTheme.btnBlue, 30);
        final Button btnGood = makePillBtn("增益+", curTheme.btnSlate, 30);
        final Button btnBad = makePillBtn("负面-", curTheme.btnSlate, 30);
        final Button btnNorm = makePillBtn("中性·", curTheme.btnSlate, 30);
        top.addView(btnAll); top.addView(btnGood); top.addView(btnBad); top.addView(btnNorm);
        root.addView(top);

        final TextView tvActive = new TextView(ctx);
        tvActive.setText("Mod 控时生效中: 无 (可点此关闭全部)");
        tvActive.setTextColor(curTheme.emerald);
        tvActive.setTextSize(10.5f);
        tvActive.setPadding(dp(8), dp(3), dp(8), dp(3));
        tvActive.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(6)));
        LinearLayout.LayoutParams aLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        aLp.setMargins(0, dp(3), 0, dp(4));
        tvActive.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                Menu.NativeQuery(5, 0, 0, 0L, "");
                Toast.makeText(ctx, "已移除全部 Mod Buff", Toast.LENGTH_SHORT).show();
                mainH.postDelayed(buffActiveRefreshHook, 200);
            }
        });
        root.addView(tvActive, aLp);

        final ListView lv = new ListView(ctx);
        lv.setDivider(null);
        lv.setDividerHeight(0);
        lv.setScrollingCacheEnabled(false);
        lv.setFastScrollEnabled(true);
        lv.setLayoutParams(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        root.addView(lv);

        LinearLayout bot = new LinearLayout(ctx);
        bot.setOrientation(LinearLayout.HORIZONTAL);
        bot.setGravity(Gravity.CENTER_VERTICAL);
        LinearLayout.LayoutParams botLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(36));
        botLp.setMargins(0, dp(4), 0, 0);

        final EditText etSec = makeDarkEdit("秒(0=永久)", true);
        etSec.setText("0");
        etSec.setGravity(Gravity.CENTER);
        LinearLayout.LayoutParams dLp = new LinearLayout.LayoutParams(dp(74), dp(30));
        dLp.setMargins(0, 0, dp(3), 0);
        bot.addView(etSec, dLp);

        final Button bP0 = makePillBtn("永久", curTheme.btnSlate, 28);
        bP0.setOnClickListener(new View.OnClickListener() { @Override public void onClick(View v) { etSec.setText("0"); } });
        final Button bP60 = makePillBtn("60s", curTheme.btnSlate, 28);
        bP60.setOnClickListener(new View.OnClickListener() { @Override public void onClick(View v) { etSec.setText("60"); } });
        final Button bP300 = makePillBtn("300s", curTheme.btnSlate, 28);
        bP300.setOnClickListener(new View.OnClickListener() { @Override public void onClick(View v) { etSec.setText("300"); } });
        bot.addView(bP0); bot.addView(bP60); bot.addView(bP300);

        final Button btnClr = makePillBtn("清空", curTheme.btnSlate, 30);
        final Button btnRm = makePillBtn("移除选中", curTheme.btnRed, 30);
        final Button btnApply = makePillBtn("施加 (0)", curTheme.btnGreen, 30);
        LinearLayout.LayoutParams apLp = new LinearLayout.LayoutParams(0, dp(30), 1f);
        apLp.setMargins(dp(3), 0, 0, 0);
        bot.addView(btnClr); bot.addView(btnRm); bot.addView(btnApply, apLp);
        root.addView(bot, botLp);

        final Runnable updateSelBadge = new Runnable() {
            @Override public void run() { btnApply.setText("施加 (" + selected.size() + ")"); }
        };

        final class BuffCellHolder {
            LinearLayout card;
            ImageView iv;
            TextView tvT, tvD;
            boolean lastSel;
            int lastThemeVer = -1;
        }
        final class BuffRowHolder {
            BuffCellHolder left, right;
        }

        final BaseAdapter adapter = new BaseAdapter() {
            @Override public int getCount() { return (filtered.size() + 1) / 2; }
            @Override public Object getItem(int pos) { return pos; }
            @Override public long getItemId(int pos) { return pos; }

            private BuffCellHolder makeCell() {
                BuffCellHolder h = new BuffCellHolder();
                LinearLayout c = new LinearLayout(ctx);
                c.setOrientation(LinearLayout.HORIZONTAL);
                c.setGravity(Gravity.CENTER_VERTICAL);
                c.setPadding(dp(7), dp(4), dp(7), dp(4));

                ImageView iv = new ImageView(ctx);
                iv.setScaleType(ImageView.ScaleType.FIT_CENTER);
                LinearLayout.LayoutParams iLp = new LinearLayout.LayoutParams(dp(30), dp(30));
                iLp.setMargins(0, 0, dp(7), 0);
                c.addView(iv, iLp);

                LinearLayout txt = new LinearLayout(ctx);
                txt.setOrientation(LinearLayout.VERTICAL);
                txt.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
                TextView t1 = new TextView(ctx);
                t1.setTextSize(11.5f);
                t1.setTypeface(null, Typeface.BOLD);
                t1.setSingleLine(true);
                t1.setEllipsize(TextUtils.TruncateAt.END);
                TextView t2 = new TextView(ctx);
                t2.setTextSize(9.5f);
                t2.setSingleLine(true);
                t2.setEllipsize(TextUtils.TruncateAt.END);
                txt.addView(t1);
                txt.addView(t2);
                c.addView(txt);

                h.card = c;
                h.iv = iv;
                h.tvT = t1;
                h.tvD = t2;
                return h;
            }

            private void ensureStyle(BuffCellHolder h, BuffInfo bf, boolean sel) {
                if (h.lastThemeVer != themeVersion || h.lastSel != sel) {
                    h.card.setBackground(makeRoundBox(
                        sel ? curTheme.selBg : curTheme.cardBg,
                        sel ? curTheme.accent : curTheme.cardEdge,
                        dp(sel ? 2 : 1), dp(8)));
                    h.lastSel = sel;
                    h.lastThemeVer = themeVersion;
                }
                int titleColor = curTheme.textPri;
                if (bf.judge == 1) titleColor = curTheme.emerald;
                else if (bf.judge == -1) titleColor = curTheme.danger;
                h.tvT.setTextColor(titleColor);
                h.tvD.setTextColor(curTheme.textSec);
            }

            private void bindCell(final BuffCellHolder h, final BuffInfo bf) {
                String tag = (bf.judge == 1) ? "[+]" : (bf.judge == -1 ? "[-]" : "[·]");
                String title = bf.name + (bf.uiTitle.isEmpty() || bf.uiTitle.equals(bf.name) ? "" : (" (" + bf.uiTitle + ")"));
                h.tvT.setText(tag + " " + title + " #" + bf.id);
                String descClean = stripGameTags(bf.desc);
                h.tvD.setText(descClean.isEmpty() ? ("默认周期 " + bf.keepTime + "s") : descClean);
                bindIconAsync(h.iv, bf.icon, curTheme.inputBg);
                ensureStyle(h, bf, selected.contains(bf.id));

                h.card.setOnClickListener(new View.OnClickListener() {
                    @Override public void onClick(View v) {
                        boolean nowSel;
                        if (selected.contains(bf.id)) { selected.remove(bf.id); nowSel = false; }
                        else { selected.add(bf.id); nowSel = true; }
                        ensureStyle(h, bf, nowSel);
                        updateSelBadge.run();
                    }
                });
            }

            @Override public View getView(int pos, View conv, ViewGroup parent) {
                LinearLayout row;
                BuffRowHolder rh;
                if (conv instanceof LinearLayout && conv.getTag() instanceof BuffRowHolder) {
                    row = (LinearLayout) conv;
                    rh = (BuffRowHolder) conv.getTag();
                } else {
                    row = new LinearLayout(ctx);
                    row.setOrientation(LinearLayout.HORIZONTAL);
                    row.setPadding(0, dp(2), 0, dp(2));
                    rh = new BuffRowHolder();
                    rh.left = makeCell();
                    rh.right = makeCell();
                    LinearLayout.LayoutParams lp1 = new LinearLayout.LayoutParams(0, dp(48), 1f);
                    lp1.setMargins(0, 0, dp(3), 0);
                    LinearLayout.LayoutParams lp2 = new LinearLayout.LayoutParams(0, dp(48), 1f);
                    lp2.setMargins(dp(3), 0, 0, 0);
                    row.addView(rh.left.card, lp1);
                    row.addView(rh.right.card, lp2);
                    row.setTag(rh);
                }
                int i1 = pos * 2, i2 = pos * 2 + 1;
                bindCell(rh.left, filtered.get(i1));
                if (i2 < filtered.size()) {
                    rh.right.card.setVisibility(View.VISIBLE);
                    bindCell(rh.right, filtered.get(i2));
                } else {
                    rh.right.card.setVisibility(View.INVISIBLE);
                }
                return row;
            }
        };
        lv.setAdapter(adapter);

        final Runnable applyFilter = new Runnable() {
            @Override public void run() {
                String kw = etSearch.getText().toString().trim().toLowerCase(Locale.ROOT);
                filtered.clear();
                List<BuffInfo> src = (sBuffsCache != null) ? sBuffsCache : allBuffs;
                for (int i = 0; i < src.size(); i++) {
                    BuffInfo bf = src.get(i);
                    if (judgeFilter[0] != 99 && bf.judge != judgeFilter[0]) continue;
                    if (!kw.isEmpty()) {
                        boolean hit = bf.name.toLowerCase(Locale.ROOT).contains(kw)
                                   || bf.uiTitle.toLowerCase(Locale.ROOT).contains(kw)
                                   || bf.desc.toLowerCase(Locale.ROOT).contains(kw)
                                   || String.valueOf(bf.id).contains(kw);
                        if (!hit) continue;
                    }
                    filtered.add(bf);
                }
                adapter.notifyDataSetChanged();
            }
        };

        buffDynReloadHook = applyFilter;

        View.OnClickListener filterClick = new View.OnClickListener() {
            @Override public void onClick(View v) {
                if (v == btnAll) judgeFilter[0] = 99;
                else if (v == btnGood) judgeFilter[0] = 1;
                else if (v == btnBad) judgeFilter[0] = -1;
                else judgeFilter[0] = 0;
                btnAll.setBackground(makeRoundBox(judgeFilter[0] == 99 ? curTheme.btnBlue : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                btnGood.setBackground(makeRoundBox(judgeFilter[0] == 1 ? curTheme.btnGreen : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                btnBad.setBackground(makeRoundBox(judgeFilter[0] == -1 ? curTheme.btnRed : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                btnNorm.setBackground(makeRoundBox(judgeFilter[0] == 0 ? curTheme.btnBlue : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                applyFilter.run();
            }
        };
        btnAll.setOnClickListener(filterClick);
        btnGood.setOnClickListener(filterClick);
        btnBad.setOnClickListener(filterClick);
        btnNorm.setOnClickListener(filterClick);

        etSearch.addTextChangedListener(new TextWatcher() {
            @Override public void beforeTextChanged(CharSequence s, int a, int b, int c) {}
            @Override public void onTextChanged(CharSequence s, int a, int b, int c) {}
            @Override public void afterTextChanged(Editable s) { applyFilter.run(); }
        });

        buffActiveRefreshHook = new Runnable() {
            @Override public void run() {
                try {
                    JSONArray bfs = lastSnap.optJSONArray("buffs");
                    if (bfs == null || bfs.length() == 0) {
                        tvActive.setText("Mod 控时生效中: 无 (点击此处一键移除全部 Mod Buff)");
                    } else {
                        StringBuilder sb = new StringBuilder("生效中 (" + bfs.length() + ", 点此全清): ");
                        for (int i = 0; i < Math.min(4, bfs.length()); i++) {
                            JSONObject o = bfs.getJSONObject(i);
                            int rem = o.optInt("rem", -1);
                            sb.append("#").append(o.optInt("id")).append(rem < 0 ? "(永久) " : ("(" + rem + "s) "));
                        }
                        tvActive.setText(sb.toString());
                    }
                } catch (Throwable ignored) {}
            }
        };

        btnClr.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                selected.clear();
                updateSelBadge.run();
                adapter.notifyDataSetChanged();
            }
        });
        btnRm.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                if (selected.isEmpty()) { Menu.NativeQuery(5, 0, 0, 0L, ""); Toast.makeText(ctx, "已清全部 Mod Buff", Toast.LENGTH_SHORT).show(); }
                else {
                    long[] base = readCmdCounters();
                    int sent = 0;
                    for (Integer id : selected) {
                        if ("1".equals(Menu.NativeQuery(5, id, 0, 0L, ""))) sent++;
                    }
                    if (sent <= 16) Toast.makeText(ctx, "已移除选中 " + sent + " 个 Buff", Toast.LENGTH_SHORT).show();
                    awaitBatchDrain(base[1], base[2], sent, "移除 Buff", buffActiveRefreshHook);
                    return;
                }
                mainH.postDelayed(buffActiveRefreshHook, 200);
            }
        });
        btnApply.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                hideKeyboard(v);
                if (selected.isEmpty()) {
                    Toast.makeText(ctx, "请先选择 Buff", Toast.LENGTH_SHORT).show();
                    return;
                }
                int sec = 0;
                try { sec = Math.max(0, Integer.parseInt(etSec.getText().toString().trim())); } catch (Throwable ignored) {}
                long[] base = readCmdCounters();
                int sent = 0;
                for (Integer id : selected) {
                    if ("1".equals(Menu.NativeQuery(4, id, sec, 0L, ""))) sent++;
                }
                if (sent <= 16) Toast.makeText(ctx, "已施加 " + sent + " 个 Buff (" + (sec == 0 ? "永久" : (sec + "s")) + ")", Toast.LENGTH_SHORT).show();
                awaitBatchDrain(base[1], base[2], sent, "施加 Buff", buffActiveRefreshHook);
            }
        });

        themeRefreshHooks.add(new Runnable() {
            @Override public void run() {
                styleEditBox(etSearch);
                styleEditBox(etSec);
                tvActive.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(6)));
                tvActive.setTextColor(curTheme.emerald);
                bP0.setBackground(makeRoundBox(curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                bP60.setBackground(makeRoundBox(curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                bP300.setBackground(makeRoundBox(curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                btnClr.setBackground(makeRoundBox(curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                btnRm.setBackground(makeRoundBox(curTheme.btnRed, 0x33FFFFFF, 1, dp(7)));
                btnApply.setBackground(makeRoundBox(curTheme.btnGreen, 0x33FFFFFF, 1, dp(7)));
                btnAll.setBackground(makeRoundBox(judgeFilter[0] == 99 ? curTheme.btnBlue : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                btnGood.setBackground(makeRoundBox(judgeFilter[0] == 1 ? curTheme.btnGreen : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                btnBad.setBackground(makeRoundBox(judgeFilter[0] == -1 ? curTheme.btnRed : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                btnNorm.setBackground(makeRoundBox(judgeFilter[0] == 0 ? curTheme.btnBlue : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                adapter.notifyDataSetChanged();
            }
        });

        return wrap;
    }

    // =========================================================================
    //  TAB 3: 其他功能 (全开关 + 蓝图/付费全解锁 + 交互/堆叠/穿墙/机关枪)
    // =========================================================================
    private FrameLayout buildOtherFeaturesPage() {
        FrameLayout wrap = new FrameLayout(ctx);
        ScrollView sv = new ScrollView(ctx);
        sv.setVerticalScrollBarEnabled(true);
        LinearLayout col = new LinearLayout(ctx);
        col.setOrientation(LinearLayout.VERTICAL);
        sv.addView(col);
        wrap.addView(sv, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        tvOtherStatus = new TextView(ctx);
        tvOtherStatus.setText("状态: 所有战斗/探索/付费/反作弊开关实时生效");
        tvOtherStatus.setTextColor(curTheme.accent);
        tvOtherStatus.setTextSize(11);
        tvOtherStatus.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(7)));
        tvOtherStatus.setPadding(dp(10), dp(5), dp(10), dp(5));
        tvOtherStatus.setSingleLine(true);
        tvOtherStatus.setEllipsize(TextUtils.TruncateAt.END);
        LinearLayout.LayoutParams stLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        stLp.setMargins(0, 0, 0, dp(6));
        col.addView(tvOtherStatus, stLp);

        // Row 1: Anti-Cheat + Ammo + Durability
        LinearLayout r1 = new LinearLayout(ctx);
        r1.setOrientation(LinearLayout.HORIZONTAL);
        r1.setBaselineAligned(false);
        LinearLayout.LayoutParams rLp1 = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        rLp1.setMargins(0, 0, 0, dp(6));

        cardAC = makeToggleCard("🔒 全链路反作弊保护", "C#+协议+Java检测封号拦截 (默认开)", new View.OnClickListener() {
            @Override public void onClick(View v) {
                flagAC = !flagAC;
                XposedEntry.antiCheatEnabled = flagAC;
                Preferences.changeFeatureBool("全代码拦截作弊检验/上报/封号", 60, flagAC);
                Menu.NativeQuery(10, 0, flagAC ? 1 : 0, 0L, "");
                syncToggleCardStyles();
            }
        });
        tvACState = attachStatusPill(cardAC);
        r1.addView(cardAC, gridCardLp(6));

        cardAmmo = makeToggleCard("🔫 子弹不减反增", "远程武器开火消耗弹药反而 +1", new View.OnClickListener() {
            @Override public void onClick(View v) {
                flagAmmo = !flagAmmo;
                Preferences.changeFeatureBool("子弹不减反增", 40, flagAmmo);
                Menu.NativeQuery(10, 1, flagAmmo ? 1 : 0, 0L, "");
                syncToggleCardStyles();
            }
        });
        tvAmmoState = attachStatusPill(cardAmmo);
        r1.addView(cardAmmo, gridCardLp(6));

        cardEndur = makeToggleCard("🛡️ 锁定装备耐久", "全部武器/防具零损耗", new View.OnClickListener() {
            @Override public void onClick(View v) {
                flagEndur = !flagEndur;
                Preferences.changeFeatureBool("锁定武器/装备耐久", 41, flagEndur);
                Menu.NativeQuery(10, 2, flagEndur ? 1 : 0, 0L, "");
                syncToggleCardStyles();
            }
        });
        LinearLayout footEndur = new LinearLayout(ctx);
        footEndur.setOrientation(LinearLayout.HORIZONTAL);
        footEndur.setGravity(Gravity.CENTER_VERTICAL);
        tvEndurState = new TextView(ctx);
        tvEndurState.setTextSize(10.5f);
        tvEndurState.setTypeface(null, Typeface.BOLD);
        tvEndurState.setGravity(Gravity.CENTER_VERTICAL);
        tvEndurState.setMinHeight(dp(24));
        tvEndurState.setSingleLine(true);
        tvEndurState.setEllipsize(TextUtils.TruncateAt.END);
        tvEndurState.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        final Button btnFixEndur = makePillBtn("补满耐久", curTheme.btnBlue, 24);
        btnFixEndur.setTextSize(10);
        btnFixEndur.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                Menu.NativeQuery(7, 0, 0, 0L, "");
                Toast.makeText(ctx, "已补满身上及背包装备耐久", Toast.LENGTH_SHORT).show();
            }
        });
        footEndur.addView(tvEndurState);
        footEndur.addView(btnFixEndur);
        cardEndur.addView(footEndur);
        r1.addView(cardEndur, gridCardLp(0));
        col.addView(r1, rLp1);

        // Row 2: Instant Interact + Warehouse Infinite Stack + Noclip
        LinearLayout r2 = new LinearLayout(ctx);
        r2.setOrientation(LinearLayout.HORIZONTAL);
        r2.setBaselineAligned(false);
        LinearLayout.LayoutParams rLp2 = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        rLp2.setMargins(0, 0, 0, dp(6));

        cardInst = makeToggleCard("⚡ 关闭交互时间", "采集/拾取/砍伐/回家无需读条(可关恢复)", new View.OnClickListener() {
            @Override public void onClick(View v) {
                flagInstant = !flagInstant;
                Preferences.changeFeatureBool("关闭交互时间", 71, flagInstant);
                Menu.NativeQuery(10, 4, flagInstant ? 1 : 0, 0L, "");
                syncToggleCardStyles();
            }
        });
        tvInstState = attachStatusPill(cardInst);
        r2.addView(cardInst, gridCardLp(6));

        cardInfStk = makeToggleCard("📦 无限堆叠 (仓库)", "仓库物品无上限(含战术背心等装备)", new View.OnClickListener() {
            @Override public void onClick(View v) {
                flagInfStack = !flagInfStack;
                Preferences.changeFeatureBool("无限堆叠(仓库)", 72, flagInfStack);
                Menu.NativeQuery(10, 5, flagInfStack ? 1 : 0, 0L, "");
                syncToggleCardStyles();
            }
        });
        tvInfStkState = attachStatusPill(cardInfStk);
        r2.addView(cardInfStk, gridCardLp(6));

        cardNoclip = makeToggleCard("👻 穿墙模式", "关闭主角实体碰撞箱，自由走过任何障碍", new View.OnClickListener() {
            @Override public void onClick(View v) {
                flagNoclip = !flagNoclip;
                Preferences.changeFeatureBool("穿墙模式", 73, flagNoclip);
                Menu.NativeQuery(10, 6, flagNoclip ? 1 : 0, 0L, "");
                syncToggleCardStyles();
            }
        });
        tvNoclipState = attachStatusPill(cardNoclip);
        r2.addView(cardNoclip, gridCardLp(0));
        col.addView(r2, rLp2);

        // Row 3: Unlock Blueprints + Unlock Paid Items + Save/Auto-Load Props
        LinearLayout r3 = new LinearLayout(ctx);
        r3.setOrientation(LinearLayout.HORIZONTAL);
        r3.setBaselineAligned(false);
        LinearLayout.LayoutParams rLp3 = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        rLp3.setMargins(0, 0, 0, dp(6));

        cardBp = makeToggleCard("📜 一键解锁全部蓝图", "全部 65+ 工作台/装备/建筑蓝图", new View.OnClickListener() {
            @Override public void onClick(View v) {
                Menu.NativeQuery(6, 0, 0, 0L, "");
                mainH.postDelayed(new Runnable() { @Override public void run() { pollSnapshot(); } }, 220);
                Toast.makeText(ctx, "已静默解锁全部蓝图", Toast.LENGTH_SHORT).show();
            }
        });
        final Button btnBpGo = makePillBtn("立即解锁全部蓝图", curTheme.btnGreen, 24);
        btnBpGo.setTextSize(10);
        btnBpGo.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                Menu.NativeQuery(6, 0, 0, 0L, "");
                mainH.postDelayed(new Runnable() { @Override public void run() { pollSnapshot(); } }, 220);
                Toast.makeText(ctx, "已静默解锁全部蓝图", Toast.LENGTH_SHORT).show();
            }
        });
        cardBp.addView(btnBpGo);
        r3.addView(cardBp, gridCardLp(6));

        cardPaid = makeToggleCard("💎 解锁全部付费道具", "猎人小屋/哈士奇/猪王/商城/DLC", new View.OnClickListener() {
            @Override public void onClick(View v) {
                flagPaid = !flagPaid;
                Preferences.changeFeatureBool("一键解锁全部付费道具", 70, flagPaid);
                Menu.NativeQuery(10, 3, flagPaid ? 1 : 0, 0L, "");
                syncToggleCardStyles();
                Toast.makeText(ctx, flagPaid ? "已本地开启全付费道具解锁" : "已恢复原始付费状态", Toast.LENGTH_SHORT).show();
            }
        });
        tvPaidState = attachStatusPill(cardPaid);
        r3.addView(cardPaid, gridCardLp(6));

        cardAutoProps = makeToggleCard("💾 保存属性配置", "保存数值与冻结状态，下次进游自动加载", new View.OnClickListener() {
            @Override public void onClick(View v) {
                flagAutoProps = !flagAutoProps;
                modPrefs.edit().putBoolean("auto_restore_props", flagAutoProps).apply();
                syncToggleCardStyles();
                Toast.makeText(ctx, flagAutoProps ? "下次进游戏将自动加载属性配置" : "已暂停下次进游戏自动加载", Toast.LENGTH_SHORT).show();
            }
        });
        LinearLayout footProps = new LinearLayout(ctx);
        footProps.setOrientation(LinearLayout.HORIZONTAL);
        footProps.setGravity(Gravity.CENTER_VERTICAL);
        tvAutoPropState = new TextView(ctx);
        tvAutoPropState.setTextSize(10.5f);
        tvAutoPropState.setTypeface(null, Typeface.BOLD);
        tvAutoPropState.setGravity(Gravity.CENTER_VERTICAL);
        tvAutoPropState.setMinHeight(dp(24));
        tvAutoPropState.setSingleLine(true);
        tvAutoPropState.setEllipsize(TextUtils.TruncateAt.END);
        tvAutoPropState.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        final Button btnSaveProps = makePillBtn("保存配置", curTheme.btnGreen, 24);
        btnSaveProps.setTextSize(10);
        btnSaveProps.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { saveCurrentPropsConfig(); }
        });
        btnSaveProps.setOnLongClickListener(new View.OnLongClickListener() {
            @Override public boolean onLongClick(View v) { clearSavedPropsConfig(); return true; }
        });
        footProps.addView(tvAutoPropState);
        footProps.addView(btnSaveProps);
        cardAutoProps.addView(footProps);
        r3.addView(cardAutoProps, gridCardLp(0));
        col.addView(r3, rLp3);

        // Row 4: Full-width Machine Gun Card + Fire Rate Speed Slider & Presets
        cardMG = makeToggleCard("🔥 机关枪模式 (仅远程枪械/弓弩有效，近战刀棍不受影响)",
                "开启后滑动右攻击轮盘不松手即可持续连射，松手即停",
                new View.OnClickListener() {
                    @Override public void onClick(View v) {
                        flagMG = !flagMG;
                        Preferences.changeFeatureBool("机关枪模式", 74, flagMG);
                        Menu.NativeQuery(10, 7, flagMG ? 1 : 0, 0L, "");
                        syncToggleCardStyles();
                    }
                });
        tvMGState = attachStatusPill(cardMG);

        LinearLayout mgRateRow = new LinearLayout(ctx);
        mgRateRow.setOrientation(LinearLayout.HORIZONTAL);
        mgRateRow.setGravity(Gravity.CENTER_VERTICAL);
        mgRateRow.setPadding(0, dp(3), 0, 0);

        tvMgRateVal = new TextView(ctx);
        tvMgRateVal.setText(String.format(Locale.US, "连发速度: %.1f 发/秒", mgRateX10 / 10.0f));
        tvMgRateVal.setTextColor(curTheme.accent);
        tvMgRateVal.setTextSize(11.5f);
        tvMgRateVal.setTypeface(null, Typeface.BOLD);
        LinearLayout.LayoutParams rLblLp = new LinearLayout.LayoutParams(dp(120), ViewGroup.LayoutParams.WRAP_CONTENT);
        mgRateRow.addView(tvMgRateVal, rLblLp);

        final SeekBar sbMg = new SeekBar(ctx);
        sbMg.setMax(590); // maps 0..590 -> 10..600 (1.0 .. 60.0 shots/s)
        sbMg.setProgress(Math.max(0, Math.min(590, mgRateX10 - 10)));
        sbMg.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        sbMg.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener() {
            @Override public void onProgressChanged(SeekBar seekBar, int progress, boolean fromUser) {
                mgRateX10 = 10 + progress;
                tvMgRateVal.setText(String.format(Locale.US, "连发速度: %.1f 发/秒", mgRateX10 / 10.0f));
                if (fromUser) {
                    Menu.NativeQuery(11, mgRateX10, 0, 0L, "");
                    modPrefs.edit().putInt("mg_rate_x10", mgRateX10).apply();
                    Preferences.changeFeatureInt("机关枪连发速度", 75, mgRateX10);
                }
            }
            @Override public void onStartTrackingTouch(SeekBar seekBar) {}
            @Override public void onStopTrackingTouch(SeekBar seekBar) {}
        });
        mgRateRow.addView(sbMg);

        final int[] ratePresets = new int[] { 50, 100, 200, 350, 600 };
        final String[] rateLbls = new String[] { "5/s", "10/s", "20/s", "35/s", "60/s" };
        final List<Button> rateBtns = new ArrayList<Button>();
        for (int i = 0; i < ratePresets.length; i++) {
            final int rv = ratePresets[i];
            Button rb = makePillBtn(rateLbls[i], curTheme.btnSlate, 24);
            rb.setTextSize(10);
            rb.setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) {
                    mgRateX10 = rv;
                    sbMg.setProgress(rv - 10);
                    Menu.NativeQuery(11, mgRateX10, 0, 0L, "");
                    modPrefs.edit().putInt("mg_rate_x10", mgRateX10).apply();
                    Preferences.changeFeatureInt("机关枪连发速度", 75, mgRateX10);
                }
            });
            rateBtns.add(rb);
            mgRateRow.addView(rb);
        }
        cardMG.addView(mgRateRow);
        col.addView(cardMG, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        themeRefreshHooks.add(new Runnable() {
            @Override public void run() {
                btnFixEndur.setBackground(makeRoundBox(curTheme.btnBlue, 0x33FFFFFF, 1, dp(7)));
                btnBpGo.setBackground(makeRoundBox(curTheme.btnGreen, 0x33FFFFFF, 1, dp(7)));
                btnSaveProps.setBackground(makeRoundBox(curTheme.btnGreen, 0x33FFFFFF, 1, dp(7)));
                tvMgRateVal.setTextColor(curTheme.accent);
                for (Button rb : rateBtns) {
                    rb.setBackground(makeRoundBox(curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                }
            }
        });

        return wrap;
    }

    // =========================================================================
    //  TAB 4: 主题风格 (5 套完整切换配色)
    // =========================================================================
    private FrameLayout buildThemesPage() {
        FrameLayout wrap = new FrameLayout(ctx);
        ScrollView sv = new ScrollView(ctx);
        LinearLayout col = new LinearLayout(ctx);
        col.setOrientation(LinearLayout.VERTICAL);
        sv.addView(col);
        wrap.addView(sv, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        for (int i = 0; i < THEMES.length; i++) {
            final int idx = i;
            final ThemePalette tp = THEMES[i];
            LinearLayout card = new LinearLayout(ctx);
            card.setOrientation(LinearLayout.HORIZONTAL);
            card.setGravity(Gravity.CENTER_VERTICAL);
            card.setPadding(dp(12), dp(9), dp(12), dp(9));
            LinearLayout.LayoutParams cLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            cLp.setMargins(0, 0, 0, dp(6));

            // Color preview swatches (panel + accent + emerald)
            LinearLayout swatch = new LinearLayout(ctx);
            swatch.setOrientation(LinearLayout.HORIZONTAL);
            swatch.setGravity(Gravity.CENTER_VERTICAL);
            View s1 = new View(ctx); s1.setBackground(makeRoundBox(tp.panelBg | 0xFF000000, tp.panelEdge, dp(1), dp(6)));
            View s2 = new View(ctx); s2.setBackground(makeRoundBox(tp.accent, 0x33FFFFFF, 1, dp(6)));
            View s3 = new View(ctx); s3.setBackground(makeRoundBox(tp.emerald, 0x33FFFFFF, 1, dp(6)));
            LinearLayout.LayoutParams swLp = new LinearLayout.LayoutParams(dp(20), dp(20));
            swLp.setMargins(0, 0, dp(3), 0);
            swatch.addView(s1, swLp);
            swatch.addView(s2, swLp);
            swatch.addView(s3, new LinearLayout.LayoutParams(dp(20), dp(20)));
            LinearLayout.LayoutParams scLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            scLp.setMargins(0, 0, dp(10), 0);
            card.addView(swatch, scLp);

            LinearLayout info = new LinearLayout(ctx);
            info.setOrientation(LinearLayout.VERTICAL);
            info.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
            TextView tName = new TextView(ctx);
            tName.setText(tp.name);
            tName.setTextSize(13);
            tName.setTypeface(null, Typeface.BOLD);
            TextView tDesc = new TextView(ctx);
            tDesc.setText(tp.desc);
            tDesc.setTextSize(10.5f);
            info.addView(tName);
            info.addView(tDesc);
            card.addView(info);

            TextView tBadge = new TextView(ctx);
            tBadge.setTextSize(11.5f);
            tBadge.setTypeface(null, Typeface.BOLD);
            card.addView(tBadge);

            card.setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) { applyTheme(idx, true); }
            });

            themeCards[i] = card;
            themeBadges[i] = tBadge;
            col.addView(card, cLp);
        }

        return wrap;
    }

    // =========================================================================
    //  TAB 5: 诊断与设置
    // =========================================================================
    private FrameLayout buildSettingsPage() {
        FrameLayout wrap = new FrameLayout(ctx);
        ScrollView sv = new ScrollView(ctx);
        LinearLayout col = new LinearLayout(ctx);
        col.setOrientation(LinearLayout.VERTICAL);
        sv.addView(col);
        wrap.addView(sv, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        diagCard = new LinearLayout(ctx);
        diagCard.setOrientation(LinearLayout.VERTICAL);
        diagCard.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(10)));
        diagCard.setPadding(dp(12), dp(10), dp(12), dp(10));

        tvDiagHdr = new TextView(ctx);
        tvDiagHdr.setText("运行诊断与共享内存状态 (ModBridge + ModShm)");
        tvDiagHdr.setTextColor(curTheme.accent);
        tvDiagHdr.setTextSize(13);
        tvDiagHdr.setTypeface(null, Typeface.BOLD);
        diagCard.addView(tvDiagHdr);

        tvDiagText = new TextView(ctx);
        tvDiagText.setText("读取中...");
        tvDiagText.setTextColor(curTheme.textPri);
        tvDiagText.setTextSize(11.5f);
        tvDiagText.setPadding(0, dp(6), 0, dp(6));
        diagCard.addView(tvDiagText);

        LinearLayout rowBtns = new LinearLayout(ctx);
        rowBtns.setOrientation(LinearLayout.HORIZONTAL);
        final Button btnRef = makePillBtn("刷新游戏快照", curTheme.btnBlue, 32);
        btnRef.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                Menu.NativeQuery(9, 0, 0, 0L, "");
                mainH.postDelayed(new Runnable() { @Override public void run() { pollSnapshot(); } }, 180);
            }
        });
        rowBtns.addView(btnRef);

        final Button btnSyncDyn = makePillBtn("重新扫描游戏配置/新图标", curTheme.btnGreen, 32);
        btnSyncDyn.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                manualDynSyncRequested = true;
                File f = ctx.getFilesDir();
                if (f != null) Menu.NativeQuery(12, 1, 0, 0L, f.getAbsolutePath());
                Menu.NativeQuery(13, 0, 0, 0L, "");
                Toast.makeText(ctx, "已触发动态配置与缺失图标重扫", Toast.LENGTH_SHORT).show();
            }
        });
        rowBtns.addView(btnSyncDyn);

        final Button btnResetPos = makePillBtn("重置悬浮球位置", curTheme.btnSlate, 32);
        btnResetPos.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                cb.onResetIconPos();
                Toast.makeText(ctx, "已复位到左上角", Toast.LENGTH_SHORT).show();
            }
        });
        rowBtns.addView(btnResetPos);
        diagCard.addView(rowBtns);
        col.addView(diagCard);

        prefCard = new LinearLayout(ctx);
        prefCard.setOrientation(LinearLayout.HORIZONTAL);
        prefCard.setGravity(Gravity.CENTER_VERTICAL);
        prefCard.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(10)));
        prefCard.setPadding(dp(12), dp(10), dp(12), dp(10));
        LinearLayout.LayoutParams pLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        pLp.setMargins(0, dp(8), 0, 0);

        tvPrefText = new TextView(ctx);
        tvPrefText.setText("自动记住开关状态 (Save feature preferences)");
        tvPrefText.setTextColor(curTheme.textPri);
        tvPrefText.setTextSize(12);
        tvPrefText.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        prefCard.addView(tvPrefText);

        final Switch sw = new Switch(ctx);
        sw.setChecked(Preferences.loadPrefBool("Save feature preferences", -1, false));
        sw.setOnCheckedChangeListener(new CompoundButton.OnCheckedChangeListener() {
            @Override public void onCheckedChanged(CompoundButton b, boolean isOn) {
                Preferences.changeFeatureBool("Save feature preferences", -1, isOn);
                Preferences.with(ctx).writeBoolean(-1, isOn);
                if (!isOn) Preferences.with(ctx).clear();
            }
        });
        prefCard.addView(sw);
        col.addView(prefCard, pLp);

        themeRefreshHooks.add(new Runnable() {
            @Override public void run() {
                btnRef.setBackground(makeRoundBox(curTheme.btnBlue, 0x33FFFFFF, 1, dp(7)));
                btnSyncDyn.setBackground(makeRoundBox(curTheme.btnGreen, 0x33FFFFFF, 1, dp(7)));
                btnResetPos.setBackground(makeRoundBox(curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
            }
        });

        return wrap;
    }
}
