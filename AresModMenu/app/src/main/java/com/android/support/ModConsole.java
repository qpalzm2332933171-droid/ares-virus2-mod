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
 *  0: 物品发放 (4-5 col compact icon+id+name grid, long-press rich detail popup, 1/999 + 全选/清空, dynamic reload)
 *  1: 全部属性 (smooth ViewHolder editing & locking across all 50 DPropertyKey items, fixed groups)
 *  2: Buff施加 (smooth async-icon multi-select, fixed BuffJudge -1/0/1 filter)
 *  3: 其他功能 (Anti-Cheat, Ammo, Durability, Blueprints, Paid Unlock, Instant Interact, Warehouse InfStack, Noclip, MachineGun+Rate)
 *  4: 主题风格 (5 distinct live-switchable persisted themes + FAB reskin)
 *  5: 诊断设置 (ModShm diagnostics, dynamic resync, reset FAB, save prefs)
 */
public final class ModConsole {

    public static final String CURRENT_VERSION_NAME = "v1.4.8";
    public static final int CURRENT_VERSION_CODE = 10;
    private static boolean s_hasCheckedUpdateOnLaunch = false;

    private void checkUpdate(final boolean manual) {
        if (manual) {
            Toast.makeText(ctx, "正在检测最新版本...", Toast.LENGTH_SHORT).show();
        }
        new Thread(new Runnable() {
            @Override public void run() {
                String[] urls = new String[] {
                    "https://gh-proxy.org/https://raw.githubusercontent.com/qpalzm2332933171-droid/ares-virus2-mod/main/version.json",
                    "https://cdn.gh-proxy.org/https://raw.githubusercontent.com/qpalzm2332933171-droid/ares-virus2-mod/main/version.json",
                    "https://hk.gh-proxy.org/https://raw.githubusercontent.com/qpalzm2332933171-droid/ares-virus2-mod/main/version.json",
                    "https://fastly.jsdelivr.net/gh/qpalzm2332933171-droid/ares-virus2-mod@main/version.json",
                    "https://gcore.jsdelivr.net/gh/qpalzm2332933171-droid/ares-virus2-mod@main/version.json",
                    "https://cdn.jsdelivr.net/gh/qpalzm2332933171-droid/ares-virus2-mod@main/version.json",
                    "https://raw.githubusercontent.com/qpalzm2332933171-droid/ares-virus2-mod/main/version.json"
                };
                JSONObject res = null;
                for (String u : urls) {
                    try {
                        java.net.URL url = new java.net.URL(u);
                        java.net.HttpURLConnection conn = (java.net.HttpURLConnection) url.openConnection();
                        conn.setRequestMethod("GET");
                        conn.setInstanceFollowRedirects(true);
                        conn.setRequestProperty("User-Agent", "AresModMenu/" + CURRENT_VERSION_NAME);
                        conn.setRequestProperty("Accept", "application/json,*/*");
                        conn.setConnectTimeout(3500);
                        conn.setReadTimeout(3500);
                        conn.setUseCaches(false);
                        if (conn.getResponseCode() == 200) {
                            java.io.InputStream in = conn.getInputStream();
                            java.io.ByteArrayOutputStream baos = new java.io.ByteArrayOutputStream();
                            byte[] buf = new byte[1024];
                            int len;
                            while ((len = in.read(buf)) != -1) {
                                baos.write(buf, 0, len);
                            }
                            in.close();
                            String raw = baos.toString("UTF-8");
                            if (raw.length() > 0 && raw.charAt(0) == '\uFEFF') raw = raw.substring(1);
                            res = new JSONObject(raw.trim());
                            conn.disconnect();
                            break;
                        }
                        conn.disconnect();
                    } catch (Throwable ignored) {}
                }

                final JSONObject finalRes = res;
                mainH.post(new Runnable() {
                    @Override public void run() {
                        if (finalRes == null) {
                            if (manual) Toast.makeText(ctx, "检测更新失败，请检查网络", Toast.LENGTH_SHORT).show();
                            return;
                        }
                        int latestCode = finalRes.optInt("versionCode", 0);
                        final String latestName = finalRes.optString("versionName", "");
                        final String changelog = finalRes.optString("changelog", "有新版本可用");
                        String rawDl = finalRes.optString("downloadUrl", "https://gh-proxy.org/https://github.com/qpalzm2332933171-droid/ares-virus2-mod/releases/latest");
                        if (rawDl.contains("https://ghproxy.net/")) rawDl = rawDl.replace("https://ghproxy.net/", "https://gh-proxy.org/");
                        final String downloadUrl = rawDl;

                        if (latestCode > CURRENT_VERSION_CODE) {
                            showUpdateDialog(latestName, changelog, downloadUrl);
                        } else {
                            if (manual) {
                                Toast.makeText(ctx, "当前已是最新版本 (" + CURRENT_VERSION_NAME + ")", Toast.LENGTH_SHORT).show();
                            }
                        }
                    }
                });
            }
        }).start();
    }

    private void showUpdateDialog(String versionName, String changelog, final String downloadUrl) {
        try {
            android.app.AlertDialog.Builder builder = new android.app.AlertDialog.Builder(ctx);
            builder.setTitle("发现新版本 " + versionName);
            builder.setMessage(changelog);
            builder.setPositiveButton("立即更新 (国内高速)", new android.content.DialogInterface.OnClickListener() {
                @Override public void onClick(android.content.DialogInterface dialog, int which) {
                    try {
                        android.content.Intent intent = new android.content.Intent(android.content.Intent.ACTION_VIEW, android.net.Uri.parse(downloadUrl));
                        intent.addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK);
                        ctx.startActivity(intent);
                    } catch (Throwable t) {
                        Toast.makeText(ctx, "无法打开浏览器: " + t.getMessage(), Toast.LENGTH_SHORT).show();
                    }
                }
            });
            builder.setNegativeButton("稍后再说", null);
            android.app.AlertDialog dlg = builder.create();
            if (!(ctx instanceof android.app.Activity) && dlg.getWindow() != null) {
                if (android.os.Build.VERSION.SDK_INT >= 26) {
                    dlg.getWindow().setType(android.view.WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY);
                } else {
                    dlg.getWindow().setType(android.view.WindowManager.LayoutParams.TYPE_PHONE);
                }
            }
            dlg.show();
        } catch (Throwable t) {
            Toast.makeText(ctx, "发现新版本: " + versionName + "\n请至GitHub仓库下载", Toast.LENGTH_LONG).show();
        }
    }


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
    private static volatile List<TalentInfo> sTalentsCache = null;
    private static volatile List<HireDefInfo> sHiresCache = null;
    private static volatile List<WorkerDefInfo> sWorkersCache = null;
    private static volatile List<UnitDefInfo> sUnitsCache = null;
    private static final Map<Integer, String> sDynSubtypeNames = Collections.synchronizedMap(new HashMap<Integer, String>());

    private static final class TalentLevelInfo {
        int lv;
        String param, lname;
    }

    private static final class TalentInfo {
        int id, type, prop, rate;
        String name;
        final List<TalentLevelInfo> levels = new ArrayList<TalentLevelInfo>();
        int minLv = 1, maxLv = 1;
    }

    private static final class HireDefInfo {
        int id;
        String name, tpl;
    }

    private static final class WorkerDefInfo {
        int id, wtype, hp, price, spd;
        String name;
    }

    private static final class UnitDefInfo {
        int id, cls;
        String name;
    }

    private static final class TargetCharInfo {
        int cid;
        String name;
    }

    private static final class NearbyEntityInfo {
        String uid;
        int id, camp, hp, maxHp;
        float dist;
        boolean tamed;
        String name;
    }

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
        SUBTYPE_NAMES.put(55, "55 观赏鱼");
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
    private TextView tvTitle, tvLivePill, tvDiagText, tvOtherStatus, tvCombatStatus, tvMgRateVal;
    private Button btnHideTop, btnMinTop;
    private EditText etGlobalSearch;
    private Button btnClearGlobalSearch;
    private FrameLayout globalSearchOverlay;
    private LinearLayout globalSearchCard;
    private ListView lvGlobalSearch;

    // 15 sidebar tabs
    private static final int TAB_COUNT = 15;
    private final FrameLayout[] pages = new FrameLayout[TAB_COUNT];
    private final LinearLayout[] sideBtns = new LinearLayout[TAB_COUNT];
    private final View[] sideBars = new View[TAB_COUNT];
    private final TextView[] sideLabels = new TextView[TAB_COUNT];
    private int curTab = 0;

    // Toggle states (synced with NativeQuery(10) & Preferences)
    private boolean flagAC = true;
    private boolean flagBlockCloud = true;
    private boolean flagLogin7891 = false;
    private boolean flagAmmo = false;
    private boolean flagEndur = false;
    private boolean flagPaid = false;
    private boolean flagInstant = false;
    private boolean flagInfStack = false;
    private boolean flagNoclip = false;
    private boolean flagMG = false;
    private boolean flagSewer = false;
    private boolean flagEvent = false;
    private boolean flagPhono = false;
    private boolean flagRandomSpawn = false;
    private boolean flagAimbot = false;
    private boolean flagAimIgnoreWall = false;
    private boolean flagEsp = false;
    private boolean flagEspBox = true;
    private boolean flagEspLine = true;
    private boolean flagEspInfo = true;
    private boolean flagEspNeutral = false;
    private boolean flagNoShake = false;
    private int mgRateX10 = 100; // default 10.0 shots/s
    private int aimMaxRangeX10 = 0; // 0 = unlimited
    private int espLineMode = 0; // 0=top, 1=center, 2=player

    // Combat, Entertainment & Assist/Security switch widgets
    private Switch swAC, swBlockCloud, swLogin7891, swAmmo, swEndur, swInstant, swInfStack, swNoclip, swSewer, swEvent, swPhono, swRandomSpawn, swPaid, swAutoProps, swMG;
    private Switch swAimbot, swAimIgnoreWall, swEsp, swEspBox, swEspLine, swEspInfo, swEspNeutral, swNoShake;
    private LinearLayout cardMG, cardAimbot, cardEsp, cardLogin7891, cardRandomSpawn;
    private TextView tvMGState, tvAimState, tvAimRangeVal, tvEspState, tvLogin7891State, tvEntStatus, tvCricketState, tvRandomSpawnState;
    private ScrollView svCombat, svAssist, svEnt;
    private final List<View> otherFeatureRows = new ArrayList<View>();
    private final List<TextView> otherFeatureTitles = new ArrayList<TextView>();
    private final List<TextView> otherFeatureSubs = new ArrayList<TextView>();
    private final List<Button> hireSubTabBtns = new ArrayList<Button>();

    // Global feature search registry
    private static class FeatureSearchEntry {
        String title;
        String desc;
        String keywords;
        String category;
        int tabIdx;
        View targetView;
        ScrollView scrollView;
        Runnable subAction;
        int score;
    }
    private final List<FeatureSearchEntry> searchRegistry = new ArrayList<FeatureSearchEntry>();

    // Map Teleport & Task Manager models
    private static class MapEntry {
        int id;
        String name;
        String desc;
        String us;
        int belong;
        int root;
        int spawn;
        int cat;
        int ui;
        boolean valid;
        int depth;
        int childCount;
        boolean expanded;
    }
    private static class TaskEntry {
        int id;
        int cf;
        String name;
        String desc;
        String reward;
        int scene;
        String sceneName;
        int root;
        String rootName;
        String scenes;
        int cat;
        int parent;
        int depth;
        String unlockTxt;
        String finishTxt;
        String showTxt;
    }
    private static final String[] CHAPTER_NAMES = new String[] {
        "全部", "主线·蝗虫篇", "镇长篇DLC", "尼奥·301篇", "姜娜·回忆篇", "蚁穴·兵封王座", "鹰眼组织篇", "活动玩法", "联机副本"
    };
    private static class SpawnUnitInfo {
        int id;
        int cls;
        int wtype;
        int fac;
        int camp;
        int exp;
        int hp;
        int hasPrefab;
        String name;
        String sub;
        String desc;
        String icon;
    }
    private static volatile List<SpawnUnitInfo> sSpawnUnitsCache = null;
    private Runnable unitDynReloadHook = null;
    private Runnable cricketPickHook = null;
    private static volatile List<MapEntry> sMapsCache = null;
    private static volatile List<TaskEntry> sTasksCache = null;
    private long lastMapStateMtime = -1L;
    private long lastTaskStateMtime = -1L;
    private long lastLoginStateMtime = -1L;
    private Runnable mapDynReloadHook = null;
    private Runnable taskDynReloadHook = null;
    private Runnable mapStateRefreshHook = null;
    private Runnable taskStateRefreshHook = null;
    private Runnable loginStateRefreshHook = null;

    // Warehouse Item Model & Modal
    private static class WarehouseItem {
        long boxId;
        long uid;
        int id;
        String name;
        int num;
        int end;
        int refit;
        int slots;
        String icon;
    }
    private FrameLayout delModalOverlay;
    private LinearLayout delModalCard;
    private ImageView delModalIcon;
    private TextView delModalTitle, delModalSub;
    private EditText etDelModalNum;
    private Button btnDelModalConfirm, btnDelModalCancel;
    private WarehouseItem delModalItem;
    private Runnable delModalOnConfirm;
    private FrameLayout noticeModalOverlay;
    private LinearLayout noticeModalCard;

    // Currency values & hooks
    private final int[] liveCurrency = new int[4];
    private final TextView[] tvCurrencyVals = new TextView[4];
    private Runnable warehouseRefreshHook = null;
    private Runnable currencyRefreshHook = null;
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
    private boolean suppressToggleCallbacks = false;
    private boolean apkIconsWritten = false;

    private Runnable propSnapshotRefreshHook = null;
    private Runnable itemBoxesRefreshHook = null;
    private Runnable itemDynReloadHook = null;
    private Runnable buffActiveRefreshHook = null;
    private Runnable buffDynReloadHook = null;
    private Runnable talentStateRefreshHook = null;
    private Runnable hireStateRefreshHook = null;
    private final List<Runnable> themeRefreshHooks = new ArrayList<Runnable>();

    public ModConsole(Context ctx, Callbacks cb) {
        this.ctx = ctx;
        this.cb = cb;
        this.modPrefs = ctx.getApplicationContext().getSharedPreferences("ares_mod_prefs", Context.MODE_PRIVATE);
        this.curThemeIdx = Math.max(0, Math.min(THEMES.length - 1, modPrefs.getInt("theme_idx", 0)));
        this.curTheme = THEMES[this.curThemeIdx];

        Preferences.loadPrefBool("Save feature preferences", -1, true);
        this.flagAC = Preferences.loadPrefBool("全代码拦截作弊检验/上报/封号", 60, true);
        this.flagBlockCloud = Preferences.loadPrefBool("100%拦截云存档上传", 84, true);
        this.flagLogin7891 = Preferences.loadPrefBool("固定7891登录", 128, false);
        this.flagAmmo = Preferences.loadPrefBool("子弹不减反增", 40, false);
        this.flagEndur = Preferences.loadPrefBool("锁定武器/装备耐久", 41, false);
        this.flagPaid = Preferences.loadPrefBool("一键解锁全部付费道具", 70, false);
        this.flagInstant = Preferences.loadPrefBool("关闭交互时间", 71, false);
        this.flagInfStack = Preferences.loadPrefBool("无限堆叠(仓库)", 72, false);
        this.flagNoclip = Preferences.loadPrefBool("穿墙模式", 73, false);
        this.flagMG = Preferences.loadPrefBool("机关枪模式", 74, false);
        this.flagSewer = Preferences.loadPrefBool("下水道高亮", 81, false);
        this.flagEvent = Preferences.loadPrefBool("限时活动开放", 82, false);
        this.flagPhono = Preferences.loadPrefBool("留声机播放本地音乐", 83, false);
        this.flagRandomSpawn = Preferences.loadPrefBool("随机生成敌对生物", 129, false);
        this.flagAimbot = Preferences.loadPrefBool("自瞄最近敌对", 120, false);
        this.flagAimIgnoreWall = Preferences.loadPrefBool("自瞄无视掩体", 121, false);
        this.flagEsp = Preferences.loadPrefBool("绘制透视总开关", 122, false);
        this.flagEspBox = Preferences.loadPrefBool("绘制方框", 123, true);
        this.flagEspLine = Preferences.loadPrefBool("绘制射线", 124, true);
        this.flagEspInfo = Preferences.loadPrefBool("绘制信息血条", 125, true);
        this.flagEspNeutral = Preferences.loadPrefBool("绘制中立生物", 126, false);
        this.flagNoShake = Preferences.loadPrefBool("关闭开枪受击震动", 127, false);
        int savedRate = Preferences.loadPrefInt("机关枪连发速度", 75);
        this.mgRateX10 = (savedRate >= 10 && savedRate <= 600) ? savedRate : modPrefs.getInt("mg_rate_x10", 100);
        if (this.mgRateX10 < 10 || this.mgRateX10 > 600) this.mgRateX10 = 100;
        this.aimMaxRangeX10 = Math.max(0, Math.min(500, modPrefs.getInt("aim_max_range_x10", 0)));
        this.espLineMode = Math.max(0, Math.min(2, modPrefs.getInt("esp_line_mode", 0)));
        XposedEntry.antiCheatEnabled = this.flagAC;
        loadSavedPropsFromPrefs();

        prepareDynEnvironmentAsync();

        if (!s_hasCheckedUpdateOnLaunch) {
            s_hasCheckedUpdateOnLaunch = true;
            mainH.postDelayed(new Runnable() {
                @Override public void run() {
                    checkUpdate(false);
                }
            }, 3000);
        }
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
            String s = new String(bos.toByteArray(), "UTF-8");
            if (s.length() > 0 && s.charAt(0) == '\uFEFF') s = s.substring(1);
            return s.trim();
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
        int panelW = Math.min(dm.widthPixels * 92 / 100, dp(810));
        int panelH = Math.min(dm.heightPixels * 92 / 100, dp(410));

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
        headerBar.setPadding(dp(10), dp(4), dp(6), dp(4));
        headerBar.setLayoutParams(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(38)));

        tvTitle = new TextView(ctx);
        tvTitle.setText("阿瑞斯病毒2 控制台");
        tvTitle.setTextColor(curTheme.textPri);
        tvTitle.setTextSize(13);
        tvTitle.setTypeface(null, Typeface.BOLD);
        headerBar.addView(tvTitle);

        tvLivePill = new TextView(ctx);
        tvLivePill.setText("  ● 初始化中...");
        tvLivePill.setTextColor(curTheme.emerald);
        tvLivePill.setTextSize(10.5f);
        tvLivePill.setPadding(dp(8), dp(2), dp(6), dp(2));
        tvLivePill.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        tvLivePill.setSingleLine(true);
        tvLivePill.setEllipsize(TextUtils.TruncateAt.END);
        headerBar.addView(tvLivePill);

        etGlobalSearch = makeDarkEdit("🔍 搜功能/跳转...", false);
        etGlobalSearch.setTextSize(11);
        etGlobalSearch.setPadding(dp(8), dp(2), dp(8), dp(2));
        LinearLayout.LayoutParams gsLp = new LinearLayout.LayoutParams(dp(145), dp(27));
        gsLp.setMargins(dp(4), 0, dp(2), 0);
        headerBar.addView(etGlobalSearch, gsLp);

        btnClearGlobalSearch = makePillBtn("✕", curTheme.btnSlate, 26);
        btnClearGlobalSearch.setTextSize(10);
        btnClearGlobalSearch.setPadding(dp(6), 0, dp(6), 0);
        btnClearGlobalSearch.setVisibility(View.GONE);
        headerBar.addView(btnClearGlobalSearch);

        btnHideTop = makePillBtn("隐藏", curTheme.btnSlate, 27);
        btnHideTop.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { cb.onHideIcon(); }
        });
        headerBar.addView(btnHideTop);

        btnMinTop = makePillBtn("收起 ✕", curTheme.btnRed, 27);
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

        int sideW = dp(82);
        ScrollView sideScroll = new ScrollView(ctx);
        sideScroll.setVerticalScrollBarEnabled(false);
        sideScroll.setLayoutParams(new LinearLayout.LayoutParams(sideW, ViewGroup.LayoutParams.MATCH_PARENT));
        sideScroll.setFillViewport(true);

        sidebarView = new LinearLayout(ctx);
        sidebarView.setOrientation(LinearLayout.VERTICAL);
        sidebarView.setBackgroundColor(curTheme.sidebarBg);
        sidebarView.setPadding(dp(3), dp(3), dp(3), dp(3));
        sidebarView.setLayoutParams(new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        sideScroll.addView(sidebarView);
        body.addView(sideScroll);

        String[] tabTitles = new String[] {
            "物品发放", "物品删除", "货币修改", "全部属性", "Buff施加",
            "天赋技能", "战斗辅助", "雇佣随从", "生成实体", "地图传送",
            "任务管理", "娱乐功能", "保护辅助", "主题风格", "诊断设置"
        };
        for (int i = 0; i < tabTitles.length; i++) {
            final int tIdx = i;
            LinearLayout row = new LinearLayout(ctx);
            row.setOrientation(LinearLayout.HORIZONTAL);
            row.setGravity(Gravity.CENTER_VERTICAL);
            LinearLayout.LayoutParams rLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(26));
            rLp.setMargins(dp(1), dp(1), dp(1), dp(1));
            row.setLayoutParams(rLp);

            View bar = new View(ctx);
            bar.setLayoutParams(new LinearLayout.LayoutParams(dp(3), dp(18)));
            row.addView(bar);

            TextView lbl = new TextView(ctx);
            lbl.setText(tabTitles[i]);
            lbl.setTextSize(13.5f);
            lbl.setSingleLine(true);
            lbl.setGravity(Gravity.CENTER_VERTICAL | Gravity.START);
            lbl.setPadding(dp(4), 0, dp(2), 0);
            lbl.setTypeface(null, Typeface.BOLD);
            lbl.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 1f));
            row.addView(lbl);

            row.setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) {
                    if (globalSearchOverlay != null) globalSearchOverlay.setVisibility(View.GONE);
                    switchTab(tIdx);
                }
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

        searchRegistry.clear();
        otherFeatureRows.clear();
        otherFeatureTitles.clear();
        otherFeatureSubs.clear();

        int availContentW = panelW - sideW - dp(18);
        pages[0] = buildItemsPage(availContentW);
        pages[1] = buildWarehouseDeletePage(availContentW);
        pages[2] = buildCurrencyPage();
        pages[3] = buildPropsPage();
        pages[4] = buildBuffsPage();
        pages[5] = buildTalentSkillPage();
        pages[6] = buildCombatPage();
        pages[7] = buildHirePage();
        pages[8] = buildSpawnEntityPage();
        pages[9] = buildMapTeleportPage();
        pages[10] = buildTaskManagerPage();
        pages[11] = buildEntertainmentPage();
        pages[12] = buildAssistSecurityPage();
        pages[13] = buildThemesPage();
        pages[14] = buildSettingsPage();

        for (int i = 0; i < pages.length; i++) {
            pages[i].setVisibility(i == 0 ? View.VISIBLE : View.GONE);
            contentWrap.addView(pages[i], new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        }

        buildGlobalSearchOverlay(contentWrap);
        registerExtraGlobalSearchEntries();

        buildItemDetailModal();
        backdropView.addView(detailModalOverlay);
        buildWarehouseDeleteModal();
        backdropView.addView(delModalOverlay);
        buildNoticeModal();
        backdropView.addView(noticeModalOverlay);

        applyTheme(curThemeIdx, false);
        switchTab(0);
        startPoll();
        return backdropView;
    }

    public void onMenuOpened() {
        try {
            if (modPrefs != null && modPrefs.getBoolean("hide_anti_resale_notice", false)) return;
            if (noticeModalOverlay != null) {
                noticeModalOverlay.setVisibility(View.VISIBLE);
                noticeModalOverlay.bringToFront();
            }
        } catch (Throwable ignored) {}
    }

    private void buildNoticeModal() {
        noticeModalOverlay = new FrameLayout(ctx);
        noticeModalOverlay.setBackgroundColor(0x99000000);
        noticeModalOverlay.setVisibility(View.GONE);
        noticeModalOverlay.setClickable(true);
        noticeModalOverlay.setLayoutParams(new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        noticeModalCard = new LinearLayout(ctx);
        noticeModalCard.setOrientation(LinearLayout.VERTICAL);
        noticeModalCard.setClickable(true);
        noticeModalCard.setPadding(dp(16), dp(14), dp(16), dp(12));
        noticeModalCard.setBackground(makeRoundBox(curTheme.panelBg | 0xFF000000, curTheme.accent, dp(2), dp(12)));
        FrameLayout.LayoutParams cardLp = new FrameLayout.LayoutParams(dp(440), ViewGroup.LayoutParams.WRAP_CONTENT, Gravity.CENTER);
        noticeModalOverlay.addView(noticeModalCard, cardLp);

        final TextView tvTitleNotice = new TextView(ctx);
        tvTitleNotice.setText("禁止倒卖");
        tvTitleNotice.setTextSize(16f);
        tvTitleNotice.setTypeface(null, Typeface.BOLD);
        tvTitleNotice.setTextColor(curTheme.accent);
        tvTitleNotice.setGravity(Gravity.CENTER_HORIZONTAL);
        tvTitleNotice.setPadding(0, 0, 0, dp(8));
        noticeModalCard.addView(tvTitleNotice);

        ScrollView svBody = new ScrollView(ctx);
        svBody.setVerticalScrollBarEnabled(true);
        LinearLayout.LayoutParams svLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        svLp.setMargins(0, 0, 0, dp(12));

        final TextView tvBody = new TextView(ctx);
        tvBody.setText(
            "mod作者:三角会一半\n"
            + "本mod完全开源免费,项目地址https://github.com/qpalzm2332933171-droid/ares-virus2-mod,如果你是从别人手中花钱买的,那么恭喜你被骗了\n"
            + "如果你觉得mod有用,请给作者的github仓库点个star吧!"
        );
        tvBody.setTextSize(12.5f);
        tvBody.setTextColor(curTheme.textPri);
        tvBody.setSingleLine(false);
        tvBody.setHorizontallyScrolling(false);
        tvBody.setLineSpacing(dp(3), 1.15f);
        tvBody.setLayoutParams(new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        svBody.addView(tvBody);
        noticeModalCard.addView(svBody, svLp);

        LinearLayout btnRow = new LinearLayout(ctx);
        btnRow.setOrientation(LinearLayout.HORIZONTAL);
        btnRow.setGravity(Gravity.END | Gravity.CENTER_VERTICAL);

        final Button btnNever = makePillBtn("不再显示", curTheme.btnRed, 32);
        btnNever.setTextSize(11.5f);
        btnNever.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                try {
                    if (modPrefs != null) modPrefs.edit().putBoolean("hide_anti_resale_notice", true).apply();
                } catch (Throwable ignored) {}
                noticeModalOverlay.setVisibility(View.GONE);
            }
        });
        btnRow.addView(btnNever);

        final Button btnClose = makePillBtn("关闭", curTheme.btnSlate, 32);
        btnClose.setTextSize(11.5f);
        btnClose.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                noticeModalOverlay.setVisibility(View.GONE);
            }
        });
        btnRow.addView(btnClose);

        final Button btnStar = makePillBtn("点个star", curTheme.btnGreen, 32);
        btnStar.setTextSize(11.5f);
        btnStar.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                try {
                    android.content.Intent intent = new android.content.Intent(
                        android.content.Intent.ACTION_VIEW,
                        android.net.Uri.parse("https://github.com/qpalzm2332933171-droid/ares-virus2-mod")
                    );
                    intent.addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK);
                    ctx.startActivity(intent);
                } catch (Throwable t) {
                    Toast.makeText(ctx, "请访问: https://github.com/qpalzm2332933171-droid/ares-virus2-mod", Toast.LENGTH_LONG).show();
                }
            }
        });
        btnRow.addView(btnStar);
        noticeModalCard.addView(btnRow);

        themeRefreshHooks.add(new Runnable() {
            @Override public void run() {
                if (noticeModalCard != null) {
                    noticeModalCard.setBackground(makeRoundBox(curTheme.panelBg | 0xFF000000, curTheme.accent, dp(2), dp(12)));
                }
                tvTitleNotice.setTextColor(curTheme.accent);
                tvBody.setTextColor(curTheme.textPri);
                btnNever.setBackground(makeRoundBox(curTheme.btnRed, 0x33FFFFFF, 1, dp(7)));
                btnClose.setBackground(makeRoundBox(curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                btnStar.setBackground(makeRoundBox(curTheme.btnGreen, 0x33FFFFFF, 1, dp(7)));
            }
        });
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
        if (tvCombatStatus != null) {
            tvCombatStatus.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(7)));
            tvCombatStatus.setTextColor(curTheme.accent);
        }
        if (etGlobalSearch != null) styleEditBox(etGlobalSearch);
        if (btnClearGlobalSearch != null) btnClearGlobalSearch.setBackground(makeRoundBox(curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
        if (globalSearchCard != null) globalSearchCard.setBackground(makeRoundBox(curTheme.panelBg | 0xFF000000, curTheme.accent, dp(2), dp(10)));

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
        if (idx == 1 && warehouseRefreshHook != null) warehouseRefreshHook.run();
        if (idx == 2 && currencyRefreshHook != null) currencyRefreshHook.run();
        if (idx == 3) {
            Menu.NativeQuery(9, 0, 0, 0L, "");
            mainH.postDelayed(new Runnable() {
                @Override public void run() {
                    pollSnapshot();
                    if (propSnapshotRefreshHook != null) propSnapshotRefreshHook.run();
                }
            }, 120);
        }
        if (idx == 4 && buffActiveRefreshHook != null) buffActiveRefreshHook.run();
        if (idx == 5 && talentStateRefreshHook != null) talentStateRefreshHook.run();
        if (idx == 7 && hireStateRefreshHook != null) hireStateRefreshHook.run();
        if (idx == 8 && unitDynReloadHook != null) unitDynReloadHook.run();
        if (idx == 9 && mapStateRefreshHook != null) mapStateRefreshHook.run();
        if (idx == 10 && taskStateRefreshHook != null) taskStateRefreshHook.run();
        if (idx == 12 && loginStateRefreshHook != null) loginStateRefreshHook.run();
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

            boolean syncedJustNow = false;
            if (ready && !initialBridgeSynced) {
                initialBridgeSynced = true;
                syncedJustNow = true;
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
                        loadTalentsAndSkills(ctx, true);
                        loadHireCatalog(ctx, true);
                        loadMapsCatalog(ctx, true);
                        loadTasksCatalog(ctx, true);
                        loadUnitsCatalog(ctx, true);
                        mainH.post(new Runnable() {
                            @Override public void run() {
                                if (itemDynReloadHook != null) itemDynReloadHook.run();
                                if (buffDynReloadHook != null) buffDynReloadHook.run();
                                if (talentStateRefreshHook != null) talentStateRefreshHook.run();
                                if (hireStateRefreshHook != null) hireStateRefreshHook.run();
                                if (unitDynReloadHook != null) unitDynReloadHook.run();
                                if (mapDynReloadHook != null) mapDynReloadHook.run();
                                if (taskDynReloadHook != null) taskDynReloadHook.run();
                                if (loginStateRefreshHook != null) loginStateRefreshHook.run();
                                boolean countsChanged = (lastToastItemCnt >= 0 && (items.size() != lastToastItemCnt || buffs.size() != lastToastBuffCnt));
                                lastToastItemCnt = items.size();
                                lastToastBuffCnt = buffs.size();
                                boolean showToast = manualDynSyncRequested || (isUpdate && countsChanged);
                                manualDynSyncRequested = false;
                                if (showToast) {
                                    Toast.makeText(ctx, "已扫描运行期全量配表: 物品 " + items.size() + " 项 / Buff " + buffs.size() + " 项", Toast.LENGTH_SHORT).show();
                                }
                            }
                        });
                    }
                });
            }

            // Pull flags from shm after initial sync (skip this tick if we just pushed initial flags)
            if (initialBridgeSynced && !syncedJustNow) {
                flagAC = obj.optInt("ac", 1) != 0;
                flagBlockCloud = obj.optInt("blockCloud", 1) != 0;
                flagAmmo = obj.optInt("ammo", 0) != 0;
                flagEndur = obj.optInt("end", 0) != 0;
                flagPaid = obj.optInt("paid", 0) != 0;
                flagInstant = obj.optInt("inst", 0) != 0;
                flagInfStack = obj.optInt("infStk", 0) != 0;
                flagNoclip = obj.optInt("noclip", 0) != 0;
                flagMG = obj.optInt("mg", 0) != 0;
                flagSewer = obj.optInt("sewer", 0) != 0;
                flagEvent = obj.optInt("event", 0) != 0;
                flagPhono = obj.optInt("phono", 0) != 0;
                flagAimbot = obj.optInt("aim", 0) != 0;
                flagEsp = obj.optInt("esp", 0) != 0;
                flagNoShake = obj.optInt("noShake", 0) != 0;
                flagAimIgnoreWall = obj.optInt("aimWall", 0) != 0;
                flagEspBox = obj.optInt("espBox", 1) != 0;
                flagEspLine = obj.optInt("espLine", 1) != 0;
                flagEspInfo = obj.optInt("espInfo", 1) != 0;
                flagEspNeutral = obj.optInt("espNeu", 0) != 0;
                flagLogin7891 = obj.optInt("login7891", 0) != 0;
                flagRandomSpawn = obj.optInt("randSpawn", 0) != 0;
                syncEspOverlayToMenu(Menu.getInstance());
            }
            syncToggleCardStyles();

            // Refresh dynamic map/task/login state files automatically whenever mtime advances
            try {
                File fDir = ctx.getFilesDir();
                if (fDir != null) {
                    File fm = new File(fDir, "dyn_map_state.json");
                    long mt = fm.exists() ? fm.lastModified() : -1L;
                    if (mt > 0L && mt != lastMapStateMtime) {
                        lastMapStateMtime = mt;
                        if (mapStateRefreshHook != null) mapStateRefreshHook.run();
                    }
                    File ft = new File(fDir, "dyn_task_state.json");
                    long tt = ft.exists() ? ft.lastModified() : -1L;
                    if (tt > 0L && tt != lastTaskStateMtime) {
                        lastTaskStateMtime = tt;
                        if (taskStateRefreshHook != null) taskStateRefreshHook.run();
                    }
                    File fl = new File(fDir, "dyn_login_state.json");
                    long lt = fl.exists() ? fl.lastModified() : -1L;
                    if (lt > 0L && lt != lastLoginStateMtime) {
                        lastLoginStateMtime = lt;
                        if (loginStateRefreshHook != null) loginStateRefreshHook.run();
                    }
                }
            } catch (Throwable ignored) {}

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
            if (tvCombatStatus != null && !st.isEmpty()) {
                tvCombatStatus.setText("战斗模块反馈: " + st);
            }
            if (tvEntStatus != null && !st.isEmpty()) {
                tvEntStatus.setText("娱乐模块反馈: " + st);
            }
            if (tvCricketState != null && (st.contains("斗蛐蛐") || st.contains("获胜") || st.contains("平局"))) {
                tvCricketState.setText("当前战况: " + st);
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

    private void saveAllFeatureSwitchesToPrefs() {
        Preferences.changeFeatureBool("全代码拦截作弊检验/上报/封号", 60, flagAC);
        Preferences.changeFeatureBool("100%拦截云存档上传", 84, flagBlockCloud);
        Preferences.changeFeatureBool("子弹不减反增", 40, flagAmmo);
        Preferences.changeFeatureBool("锁定武器/装备耐久", 41, flagEndur);
        Preferences.changeFeatureBool("一键解锁全部付费道具", 70, flagPaid);
        Preferences.changeFeatureBool("关闭交互时间", 71, flagInstant);
        Preferences.changeFeatureBool("无限堆叠(仓库)", 72, flagInfStack);
        Preferences.changeFeatureBool("穿墙模式", 73, flagNoclip);
        Preferences.changeFeatureBool("机关枪模式", 74, flagMG);
        Preferences.changeFeatureBool("下水道高亮", 81, flagSewer);
        Preferences.changeFeatureBool("限时活动开放", 82, flagEvent);
        Preferences.changeFeatureBool("留声机播放本地音乐", 83, flagPhono);
        Preferences.changeFeatureBool("自瞄最近敌对", 120, flagAimbot);
        Preferences.changeFeatureBool("自瞄无视掩体", 121, flagAimIgnoreWall);
        Preferences.changeFeatureBool("绘制透视总开关", 122, flagEsp);
        Preferences.changeFeatureBool("绘制方框", 123, flagEspBox);
        Preferences.changeFeatureBool("绘制射线", 124, flagEspLine);
        Preferences.changeFeatureBool("绘制信息血条", 125, flagEspInfo);
        Preferences.changeFeatureBool("绘制中立生物", 126, flagEspNeutral);
        Preferences.changeFeatureBool("关闭开枪受击震动", 127, flagNoShake);
        Preferences.changeFeatureBool("固定7891登录", 128, flagLogin7891);
        Preferences.changeFeatureBool("随机生成敌对生物", 129, flagRandomSpawn);
        Preferences.changeFeatureInt("机关枪连发速度", 75, mgRateX10);
    }

    public void syncEspOverlayToMenu(Menu m) {
        if (m != null) {
            m.updateEspView(flagEsp, flagEspBox, flagEspLine, flagEspInfo, espLineMode);
        }
    }

    public void ensureBridgeSynced() {
        if (initialBridgeSynced) return;
        try {
            String raw = Menu.NativeQuery(1, 0, 0, 0L, "");
            if (raw == null || raw.isEmpty() || !raw.startsWith("{")) return;
            JSONObject obj = new JSONObject(raw);
            if (obj.optInt("ready", 0) != 0) {
                initialBridgeSynced = true;
                syncAllFlagsToNative();
                File filesDir = ctx.getFilesDir();
                if (filesDir != null) Menu.NativeQuery(12, 1, 0, 0L, filesDir.getAbsolutePath());
            }
            int cid = obj.optInt("cid", 0);
            if (cid != 0 && obj.optInt("scene", 0) != 0) {
                scheduleAutoRestoreProps(cid);
            }
        } catch (Throwable ignored) {}
    }

    private void syncAllFlagsToNative() {
        XposedEntry.antiCheatEnabled = flagAC;
        Menu.NativeQuery(10, 0, flagAC ? 1 : 0, 0L, "");
        Menu.NativeQuery(10, 11, flagBlockCloud ? 1 : 0, 0L, "");
        Menu.NativeQuery(10, 1, flagAmmo ? 1 : 0, 0L, "");
        Menu.NativeQuery(10, 2, flagEndur ? 1 : 0, 0L, "");
        Menu.NativeQuery(10, 3, flagPaid ? 1 : 0, 0L, "");
        Menu.NativeQuery(10, 4, flagInstant ? 1 : 0, 0L, "");
        Menu.NativeQuery(10, 5, flagInfStack ? 1 : 0, 0L, "");
        Menu.NativeQuery(10, 6, flagNoclip ? 1 : 0, 0L, "");
        Menu.NativeQuery(10, 7, flagMG ? 1 : 0, 0L, "");
        Menu.NativeQuery(10, 8, flagSewer ? 1 : 0, 0L, "");
        Menu.NativeQuery(10, 9, flagEvent ? 1 : 0, 0L, "");
        Menu.NativeQuery(10, 10, flagPhono ? 1 : 0, 0L, "");
        Menu.NativeQuery(10, 12, flagAimbot ? 1 : 0, 0L, "");
        Menu.NativeQuery(10, 13, flagEsp ? 1 : 0, 0L, "");
        Menu.NativeQuery(10, 14, flagNoShake ? 1 : 0, 0L, "");
        Menu.NativeQuery(10, 15, flagAimIgnoreWall ? 1 : 0, 0L, "");
        Menu.NativeQuery(10, 16, flagEspBox ? 1 : 0, 0L, "");
        Menu.NativeQuery(10, 17, flagEspLine ? 1 : 0, 0L, "");
        Menu.NativeQuery(10, 18, flagEspInfo ? 1 : 0, 0L, "");
        Menu.NativeQuery(10, 19, flagEspNeutral ? 1 : 0, 0L, "");
        Menu.NativeQuery(10, 20, flagLogin7891 ? 1 : 0, 0L, "");
        Menu.NativeQuery(10, 21, flagRandomSpawn ? 1 : 0, 0L, "");
        Menu.NativeQuery(11, mgRateX10, 0, 0L, "");
        Menu.NativeQuery(17, aimMaxRangeX10, espLineMode, 0L, "");
        syncEspOverlayToMenu(Menu.getInstance());
    }

    private void safeSetChecked(Switch sw, boolean checked) {
        if (sw != null && sw.isChecked() != checked) {
            boolean prev = suppressToggleCallbacks;
            suppressToggleCallbacks = true;
            try {
                sw.setChecked(checked);
            } finally {
                suppressToggleCallbacks = prev;
            }
        }
    }

    private void syncToggleCardStyles() {
        safeSetChecked(swAC, flagAC);
        safeSetChecked(swBlockCloud, flagBlockCloud);
        safeSetChecked(swLogin7891, flagLogin7891);
        safeSetChecked(swAmmo, flagAmmo);
        safeSetChecked(swEndur, flagEndur);
        safeSetChecked(swInstant, flagInstant);
        safeSetChecked(swInfStack, flagInfStack);
        safeSetChecked(swNoclip, flagNoclip);
        safeSetChecked(swSewer, flagSewer);
        safeSetChecked(swEvent, flagEvent);
        safeSetChecked(swPhono, flagPhono);
        safeSetChecked(swPaid, flagPaid);
        safeSetChecked(swAutoProps, flagAutoProps && !savedPropVals.isEmpty());
        safeSetChecked(swMG, flagMG);
        safeSetChecked(swAimbot, flagAimbot);
        safeSetChecked(swAimIgnoreWall, flagAimIgnoreWall);
        safeSetChecked(swEsp, flagEsp);
        safeSetChecked(swEspBox, flagEspBox);
        safeSetChecked(swEspLine, flagEspLine);
        safeSetChecked(swEspInfo, flagEspInfo);
        safeSetChecked(swEspNeutral, flagEspNeutral);
        safeSetChecked(swNoShake, flagNoShake);
        safeSetChecked(swRandomSpawn, flagRandomSpawn);
        if (cardAimbot != null) {
            cardAimbot.setBackground(makeRoundBox(flagAimbot ? curTheme.cardOnBg : curTheme.cardBg, flagAimbot ? curTheme.cardOnEdge : curTheme.cardEdge, dp(1), dp(10)));
        }
        if (tvAimState != null) {
            tvAimState.setText(flagAimbot ? "● 自瞄锁定中" : "○ 未开启");
            tvAimState.setTextColor(flagAimbot ? curTheme.emerald : curTheme.textSec);
        }
        if (cardEsp != null) {
            cardEsp.setBackground(makeRoundBox(flagEsp ? curTheme.cardOnBg : curTheme.cardBg, flagEsp ? curTheme.cardOnEdge : curTheme.cardEdge, dp(1), dp(10)));
        }
        if (tvEspState != null) {
            tvEspState.setText(flagEsp ? "● 绘制透视中" : "○ 未开启");
            tvEspState.setTextColor(flagEsp ? curTheme.emerald : curTheme.textSec);
        }
        if (cardMG != null) {
            cardMG.setBackground(makeRoundBox(flagMG ? curTheme.cardOnBg : curTheme.cardBg, flagMG ? curTheme.cardOnEdge : curTheme.cardEdge, dp(1), dp(10)));
        }
        if (tvMGState != null) {
            tvMGState.setText(flagMG ? "● 机关枪生效中" : "○ 未开启");
            tvMGState.setTextColor(flagMG ? curTheme.emerald : curTheme.textSec);
        }
        if (cardRandomSpawn != null) {
            cardRandomSpawn.setBackground(makeRoundBox(flagRandomSpawn ? curTheme.cardOnBg : curTheme.cardBg, flagRandomSpawn ? curTheme.cardOnEdge : curTheme.cardEdge, dp(1), dp(10)));
        }
        if (tvRandomSpawnState != null) {
            tvRandomSpawnState.setText(flagRandomSpawn ? "● 全图敌对生物随机打乱生效中" : "○ 未开启 (保持原图刷怪)");
            tvRandomSpawnState.setTextColor(flagRandomSpawn ? curTheme.emerald : curTheme.textSec);
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
        final Button btnBag = makePillBtn("背包", curTheme.btnBlue, 30);
        final Button btnBox = makePillBtn("仓库", curTheme.btnSlate, 30);
        final Spinner spBox = new Spinner(ctx);
        spBox.setVisibility(View.GONE);
        LinearLayout.LayoutParams spLp = new LinearLayout.LayoutParams(dp(130), dp(30));
        spLp.setMargins(dp(4), 0, dp(4), 0);
        final ArrayAdapter<String> boxAdapter = new ArrayAdapter<String>(ctx, android.R.layout.simple_spinner_item, new ArrayList<String>());
        boxAdapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        spBox.setAdapter(boxAdapter);

        final EditText etSearch = makeDarkEdit("搜索名称 / ID (长按查看详情)", false);
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
        final EditText etSearch = makeDarkEdit("搜索属性名 / key...", false);
        etSearch.setLayoutParams(new LinearLayout.LayoutParams(0, dp(30), 1f));
        top.addView(etSearch);
        final Button btnRefresh = makePillBtn("刷新实时值", curTheme.btnBlue, 30);
        final Button btnUnlockAll = makePillBtn("解全部锁", curTheme.btnSlate, 30);
        final Button btnApplyAll = makePillBtn("批量应用修改", curTheme.btnGreen, 30);
        final Button btnSaveCfg = makePillBtn("保存配置", curTheme.btnGreen, 30);
        final Button btnClearCfg = makePillBtn("删除已存配置", curTheme.btnRed, 30);
        btnSaveCfg.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                saveCurrentPropsConfig();
                Toast.makeText(ctx, "已保存当前 " + savedPropVals.size() + " 项属性配置", Toast.LENGTH_SHORT).show();
            }
        });
        btnClearCfg.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                clearSavedPropsConfig();
                Toast.makeText(ctx, "已删除当前保存的属性配置", Toast.LENGTH_SHORT).show();
            }
        });
        top.addView(btnRefresh);
        top.addView(btnUnlockAll);
        top.addView(btnApplyAll);
        top.addView(btnSaveCfg);
        top.addView(btnClearCfg);
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
                btnSaveCfg.setBackground(makeRoundBox(curTheme.btnGreen, 0x33FFFFFF, 1, dp(7)));
                btnClearCfg.setBackground(makeRoundBox(curTheme.btnRed, 0x33FFFFFF, 1, dp(7)));
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
        final EditText etSearch = makeDarkEdit("搜索 Buff 名称 / ID / 效果...", false);
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
    //  TAB 1: 仓库物品删除 (实时扫描 dyn_warehouse.json + 箱子筛选 + 弹窗精细删除)
    // =========================================================================
    private List<WarehouseItem> loadWarehouseItems() {
        List<WarehouseItem> list = new ArrayList<WarehouseItem>();
        try {
            File f = new File(ctx.getFilesDir(), "dyn_warehouse.json");
            String raw = readFileText(f);
            if (raw != null && raw.startsWith("[")) {
                List<ItemInfo> cat = loadItems(ctx, false);
                Map<Integer, ItemInfo> itemMap = new HashMap<Integer, ItemInfo>(cat.size() * 2 + 16);
                for (ItemInfo ii : cat) itemMap.put(ii.id, ii);
                JSONArray arr = new JSONArray(raw);
                for (int i = 0; i < arr.length(); i++) {
                    JSONObject o = arr.getJSONObject(i);
                    WarehouseItem wi = new WarehouseItem();
                    wi.boxId = o.has("box") ? o.optLong("box", 0L) : o.optLong("boxId", 0L);
                    String su = o.optString("uid", "");
                    try {
                        wi.uid = (!su.isEmpty()) ? Long.parseLong(su) : o.optLong("uid");
                    } catch (Throwable ignored) {
                        wi.uid = o.optLong("uid");
                    }
                    wi.id = o.optInt("id");
                    wi.num = o.optInt("num", 1);
                    wi.end = o.optInt("end", 0);
                    wi.refit = o.optInt("refit", 0);
                    wi.slots = o.optInt("slots", 1);
                    ItemInfo ref = itemMap.get(wi.id);
                    String rawName = o.optString("name", "");
                    String rawIcon = o.optString("icon", "");
                    wi.name = !rawName.isEmpty() ? rawName : ((ref != null && ref.name != null && !ref.name.isEmpty()) ? ref.name : ("物品 #" + wi.id));
                    wi.icon = !rawIcon.isEmpty() ? rawIcon : ((ref != null && ref.icon != null) ? ref.icon : "");
                    list.add(wi);
                }
            }
        } catch (Throwable ignored) {}
        return list;
    }

    private void buildWarehouseDeleteModal() {
        delModalOverlay = new FrameLayout(ctx);
        delModalOverlay.setBackgroundColor(0x88000000);
        delModalOverlay.setVisibility(View.GONE);
        delModalOverlay.setClickable(true);
        delModalOverlay.setLayoutParams(new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        delModalOverlay.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { delModalOverlay.setVisibility(View.GONE); }
        });

        delModalCard = new LinearLayout(ctx);
        delModalCard.setOrientation(LinearLayout.VERTICAL);
        delModalCard.setClickable(true);
        delModalCard.setPadding(dp(16), dp(14), dp(16), dp(14));
        FrameLayout.LayoutParams mLp = new FrameLayout.LayoutParams(dp(400), ViewGroup.LayoutParams.WRAP_CONTENT, Gravity.CENTER);
        delModalOverlay.addView(delModalCard, mLp);

        LinearLayout top = new LinearLayout(ctx);
        top.setOrientation(LinearLayout.HORIZONTAL);
        top.setGravity(Gravity.CENTER_VERTICAL);
        delModalIcon = new ImageView(ctx);
        LinearLayout.LayoutParams iLp = new LinearLayout.LayoutParams(dp(46), dp(46));
        iLp.setMargins(0, 0, dp(10), 0);
        top.addView(delModalIcon, iLp);

        LinearLayout hdrInfo = new LinearLayout(ctx);
        hdrInfo.setOrientation(LinearLayout.VERTICAL);
        hdrInfo.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        delModalTitle = new TextView(ctx);
        delModalTitle.setTextSize(14.5f);
        delModalTitle.setTypeface(null, Typeface.BOLD);
        hdrInfo.addView(delModalTitle);
        delModalSub = new TextView(ctx);
        delModalSub.setTextSize(11);
        delModalSub.setPadding(0, dp(2), 0, 0);
        hdrInfo.addView(delModalSub);
        top.addView(hdrInfo);
        delModalCard.addView(top);

        // Stepper row
        LinearLayout stepRow = new LinearLayout(ctx);
        stepRow.setOrientation(LinearLayout.HORIZONTAL);
        stepRow.setGravity(Gravity.CENTER_VERTICAL);
        LinearLayout.LayoutParams stLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        stLp.setMargins(0, dp(12), 0, dp(12));

        TextView tvDelLbl = new TextView(ctx);
        tvDelLbl.setText("删除数量: ");
        tvDelLbl.setTextSize(12);
        tvDelLbl.setTextColor(curTheme.textPri);
        stepRow.addView(tvDelLbl);

        final Button btnSub10 = makePillBtn("-10", curTheme.btnSlate, 28);
        final Button btnSub1 = makePillBtn("-1", curTheme.btnSlate, 28);
        etDelModalNum = makeDarkEdit("数量", true);
        etDelModalNum.setText("1");
        etDelModalNum.setGravity(Gravity.CENTER);
        LinearLayout.LayoutParams etLp = new LinearLayout.LayoutParams(dp(65), dp(30));
        etLp.setMargins(dp(3), 0, dp(3), 0);
        final Button btnAdd1 = makePillBtn("+1", curTheme.btnSlate, 28);
        final Button btnAdd10 = makePillBtn("+10", curTheme.btnSlate, 28);
        final Button btnAll = makePillBtn("全部", curTheme.btnBlue, 28);

        btnSub10.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                if (delModalItem == null) return;
                int cur = 1;
                try { cur = Integer.parseInt(etDelModalNum.getText().toString().trim()); } catch (Throwable ignored) {}
                etDelModalNum.setText(String.valueOf(Math.max(1, cur - 10)));
            }
        });
        btnSub1.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                if (delModalItem == null) return;
                int cur = 1;
                try { cur = Integer.parseInt(etDelModalNum.getText().toString().trim()); } catch (Throwable ignored) {}
                etDelModalNum.setText(String.valueOf(Math.max(1, cur - 1)));
            }
        });
        btnAdd1.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                if (delModalItem == null) return;
                int cur = 1;
                try { cur = Integer.parseInt(etDelModalNum.getText().toString().trim()); } catch (Throwable ignored) {}
                etDelModalNum.setText(String.valueOf(Math.min(delModalItem.num, cur + 1)));
            }
        });
        btnAdd10.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                if (delModalItem == null) return;
                int cur = 1;
                try { cur = Integer.parseInt(etDelModalNum.getText().toString().trim()); } catch (Throwable ignored) {}
                etDelModalNum.setText(String.valueOf(Math.min(delModalItem.num, cur + 10)));
            }
        });
        btnAll.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                if (delModalItem == null) return;
                etDelModalNum.setText(String.valueOf(delModalItem.num));
            }
        });

        stepRow.addView(btnSub10);
        stepRow.addView(btnSub1);
        stepRow.addView(etDelModalNum, etLp);
        stepRow.addView(btnAdd1);
        stepRow.addView(btnAdd10);
        stepRow.addView(btnAll);
        delModalCard.addView(stepRow, stLp);

        // Action buttons
        LinearLayout bot = new LinearLayout(ctx);
        bot.setOrientation(LinearLayout.HORIZONTAL);
        bot.setGravity(Gravity.END);

        btnDelModalCancel = makePillBtn("取消", curTheme.btnSlate, 30);
        btnDelModalConfirm = makePillBtn("确认删除", curTheme.btnRed, 30);

        btnDelModalCancel.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { delModalOverlay.setVisibility(View.GONE); }
        });
        btnDelModalConfirm.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                if (delModalItem == null) return;
                int count = 1;
                try { count = Integer.parseInt(etDelModalNum.getText().toString().trim()); } catch (Throwable ignored) {}
                if (count <= 0) count = 1;
                if (count > delModalItem.num) count = delModalItem.num;
                Menu.NativeQuery(15, count, (int) delModalItem.boxId, delModalItem.uid, "");
                Toast.makeText(ctx, "已删除 " + count + " 个 " + delModalItem.name, Toast.LENGTH_SHORT).show();
                delModalOverlay.setVisibility(View.GONE);
                if (delModalOnConfirm != null) delModalOnConfirm.run();
            }
        });

        bot.addView(btnDelModalCancel);
        bot.addView(btnDelModalConfirm);
        delModalCard.addView(bot);
    }

    private void showWarehouseDeleteModal(final WarehouseItem item, final Runnable onConfirm) {
        delModalItem = item;
        delModalOnConfirm = onConfirm;
        delModalCard.setBackground(makeRoundBox(curTheme.panelBg, curTheme.accent, dp(2), dp(14)));
        delModalTitle.setText(item.name + "   #" + item.id);
        delModalTitle.setTextColor(curTheme.textPri);
        String sub = (item.boxId == 0L ? "随身仓库" : ("箱子 #" + item.boxId)) + " · 当前存量: " + item.num;
        if (item.end > 0) sub += " · 耐久 " + item.end;
        if (item.refit > 0) sub += " · 已改造";
        if (item.slots > 1) sub += " · " + item.slots + "格";
        delModalSub.setText(sub);
        delModalSub.setTextColor(curTheme.accent);

        etDelModalNum.setText(String.valueOf(item.num));
        Bitmap bmp = getIconSync(ctx, item.icon);
        if (bmp != null) delModalIcon.setImageBitmap(bmp);
        else delModalIcon.setImageDrawable(null);

        delModalOverlay.setVisibility(View.VISIBLE);
    }

    private FrameLayout buildWarehouseDeletePage(int availContentW) {
        final FrameLayout wrap = new FrameLayout(ctx);
        LinearLayout root = new LinearLayout(ctx);
        root.setOrientation(LinearLayout.VERTICAL);
        wrap.addView(root, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        final List<WarehouseItem> allItems = new ArrayList<WarehouseItem>();
        final List<WarehouseItem> filtered = new ArrayList<WarehouseItem>();
        final long[] selectedBoxId = new long[] { -1L };
        final String[] searchKey = new String[] { "" };
        final List<Long> boxList = new ArrayList<Long>();

        // Top bar: Search + Refresh
        LinearLayout top = new LinearLayout(ctx);
        top.setOrientation(LinearLayout.HORIZONTAL);
        top.setGravity(Gravity.CENTER_VERTICAL);
        final EditText etSearch = makeDarkEdit("搜索仓库物品名称 / ID...", false);
        etSearch.setLayoutParams(new LinearLayout.LayoutParams(0, dp(30), 1f));
        final Button btnRef = makePillBtn("刷新仓库", curTheme.btnBlue, 30);
        top.addView(etSearch);
        top.addView(btnRef);
        root.addView(top);

        // Filter pills for boxes
        final HorizontalScrollView hsBoxes = new HorizontalScrollView(ctx);
        hsBoxes.setHorizontalScrollBarEnabled(false);
        final LinearLayout rowBoxes = new LinearLayout(ctx);
        rowBoxes.setOrientation(LinearLayout.HORIZONTAL);
        hsBoxes.addView(rowBoxes);
        LinearLayout.LayoutParams bLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(28));
        bLp.setMargins(0, dp(3), 0, dp(3));
        root.addView(hsBoxes, bLp);

        final TextView tvSummary = new TextView(ctx);
        tvSummary.setTextSize(10.5f);
        tvSummary.setTextColor(curTheme.textSec);
        tvSummary.setPadding(dp(4), 0, dp(4), dp(2));
        root.addView(tvSummary);

        // 2-column ListView
        final ListView lv = new ListView(ctx);
        lv.setDivider(null);
        lv.setDividerHeight(0);
        lv.setScrollingCacheEnabled(false);
        lv.setLayoutParams(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        root.addView(lv);

        final class WhCellHolder {
            LinearLayout card;
            ImageView iv;
            TextView tvName, tvInfo, tvNum;
            Button btnDel;
            WarehouseItem boundItem;
        }
        final class WhRowHolder {
            WhCellHolder left, right;
        }

        final BaseAdapter adapter = new BaseAdapter() {
            @Override public int getCount() { return (filtered.size() + 1) / 2; }
            @Override public Object getItem(int pos) { return pos; }
            @Override public long getItemId(int pos) { return pos; }

            private WhCellHolder makeCell() {
                final WhCellHolder h = new WhCellHolder();
                LinearLayout c = new LinearLayout(ctx);
                c.setOrientation(LinearLayout.HORIZONTAL);
                c.setGravity(Gravity.CENTER_VERTICAL);
                c.setPadding(dp(8), dp(5), dp(8), dp(5));

                h.iv = new ImageView(ctx);
                LinearLayout.LayoutParams ivLp = new LinearLayout.LayoutParams(dp(38), dp(38));
                ivLp.setMargins(0, 0, dp(8), 0);
                c.addView(h.iv, ivLp);

                LinearLayout mid = new LinearLayout(ctx);
                mid.setOrientation(LinearLayout.VERTICAL);
                mid.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

                h.tvName = new TextView(ctx);
                h.tvName.setTextSize(11.5f);
                h.tvName.setTypeface(null, Typeface.BOLD);
                h.tvName.setSingleLine(true);
                h.tvName.setEllipsize(TextUtils.TruncateAt.END);
                mid.addView(h.tvName);

                h.tvInfo = new TextView(ctx);
                h.tvInfo.setTextSize(9.5f);
                h.tvInfo.setSingleLine(true);
                mid.addView(h.tvInfo);
                c.addView(mid);

                LinearLayout rightCol = new LinearLayout(ctx);
                rightCol.setOrientation(LinearLayout.VERTICAL);
                rightCol.setGravity(Gravity.END | Gravity.CENTER_VERTICAL);

                h.tvNum = new TextView(ctx);
                h.tvNum.setTextSize(11.5f);
                h.tvNum.setTypeface(null, Typeface.BOLD);
                h.tvNum.setTextColor(curTheme.emerald);
                rightCol.addView(h.tvNum);

                h.btnDel = makePillBtn("删除", curTheme.btnRed, 22);
                h.btnDel.setTextSize(9.5f);
                rightCol.addView(h.btnDel);
                c.addView(rightCol);

                h.card = c;
                return h;
            }

            private void bindCell(final WhCellHolder h, final WarehouseItem it) {
                h.boundItem = it;
                if (it == null) {
                    h.card.setVisibility(View.INVISIBLE);
                    return;
                }
                h.card.setVisibility(View.VISIBLE);
                h.card.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(8)));
                h.tvName.setText(it.name);
                h.tvName.setTextColor(curTheme.textPri);
                String sub = (it.boxId == 0L ? "随身仓库" : ("箱子#" + it.boxId)) + " · ID:" + it.id;
                if (it.end > 0) sub += " · 耐久" + it.end;
                if (it.refit > 0) sub += " · 已改造";
                if (it.slots > 1) sub += " · " + it.slots + "格";
                h.tvInfo.setText(sub);
                h.tvInfo.setTextColor(curTheme.textSec);
                h.tvNum.setText("x" + it.num);
                bindIconAsync(h.iv, it.icon, curTheme.inputBg);

                View.OnClickListener onDelClick = new View.OnClickListener() {
                    @Override public void onClick(View v) {
                        showWarehouseDeleteModal(it, new Runnable() {
                            @Override public void run() {
                                mainH.postDelayed(new Runnable() {
                                    @Override public void run() {
                                        if (warehouseRefreshHook != null) warehouseRefreshHook.run();
                                    }
                                }, 150);
                            }
                        });
                    }
                };
                h.card.setOnClickListener(onDelClick);
                h.btnDel.setOnClickListener(onDelClick);
            }

            @Override public View getView(int pos, View cv, ViewGroup p) {
                WhRowHolder rh;
                if (cv == null) {
                    LinearLayout row = new LinearLayout(ctx);
                    row.setOrientation(LinearLayout.HORIZONTAL);
                    row.setPadding(0, dp(2), 0, dp(2));
                    rh = new WhRowHolder();
                    rh.left = makeCell();
                    rh.right = makeCell();
                    LinearLayout.LayoutParams cLp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
                    cLp.setMargins(dp(2), 0, dp(2), 0);
                    row.addView(rh.left.card, cLp);
                    row.addView(rh.right.card, cLp);
                    row.setTag(rh);
                    cv = row;
                } else {
                    rh = (WhRowHolder) cv.getTag();
                }
                int lIdx = pos * 2;
                int rIdx = lIdx + 1;
                bindCell(rh.left, lIdx < filtered.size() ? filtered.get(lIdx) : null);
                bindCell(rh.right, rIdx < filtered.size() ? filtered.get(rIdx) : null);
                return cv;
            }
        };
        lv.setAdapter(adapter);

        final Runnable applyFilter = new Runnable() {
            @Override public void run() {
                filtered.clear();
                String q = searchKey[0].trim().toLowerCase(Locale.US);
                long bId = selectedBoxId[0];
                for (WarehouseItem wi : allItems) {
                    if (bId != -1L && wi.boxId != bId) continue;
                    if (!q.isEmpty()) {
                        String nameL = (wi.name != null) ? wi.name.toLowerCase(Locale.US) : "";
                        String idStr = String.valueOf(wi.id);
                        if (!nameL.contains(q) && !idStr.contains(q)) continue;
                    }
                    filtered.add(wi);
                }
                tvSummary.setText("共检测到 " + allItems.size() + " 项，当前筛选显示 " + filtered.size() + " 项");
                adapter.notifyDataSetChanged();
            }
        };

        final Runnable rebuildBoxPills = new Runnable() {
            @Override public void run() {
                rowBoxes.removeAllViews();
                boxList.clear();
                HashSet<Long> seen = new HashSet<Long>();
                for (WarehouseItem wi : allItems) {
                    if (seen.add(wi.boxId)) boxList.add(wi.boxId);
                }
                // All boxes button
                final Button btnAllBoxes = makePillBtn("全部 (" + allItems.size() + ")", selectedBoxId[0] == -1L ? curTheme.btnBlue : curTheme.btnSlate, 26);
                btnAllBoxes.setOnClickListener(new View.OnClickListener() {
                    @Override public void onClick(View v) {
                        selectedBoxId[0] = -1L;
                        applyFilter.run();
                        for (int k = 0; k < rowBoxes.getChildCount(); k++) {
                            View c = rowBoxes.getChildAt(k);
                            if (c instanceof Button) {
                                boolean active = (k == 0);
                                c.setBackground(makeRoundBox(active ? curTheme.btnBlue : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                            }
                        }
                    }
                });
                rowBoxes.addView(btnAllBoxes);

                for (final Long bId : boxList) {
                    int bCnt = 0;
                    for (WarehouseItem wi : allItems) if (wi.boxId == bId) bCnt++;
                    String bLabel = (bId == 0L) ? ("随身仓库 (" + bCnt + ")") : ("箱子 #" + bId + " (" + bCnt + ")");
                    Button bBtn = makePillBtn(bLabel, selectedBoxId[0] == bId ? curTheme.btnBlue : curTheme.btnSlate, 26);
                    bBtn.setOnClickListener(new View.OnClickListener() {
                        @Override public void onClick(View v) {
                            selectedBoxId[0] = bId;
                            applyFilter.run();
                            for (int k = 0; k < rowBoxes.getChildCount(); k++) {
                                View c = rowBoxes.getChildAt(k);
                                if (c instanceof Button) {
                                    boolean active = (k > 0 && boxList.get(k - 1).longValue() == selectedBoxId[0]);
                                    c.setBackground(makeRoundBox(active ? curTheme.btnBlue : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                                }
                            }
                        }
                    });
                    rowBoxes.addView(bBtn);
                }
            }
        };

        warehouseRefreshHook = new Runnable() {
            @Override public void run() {
                sBgExec.execute(new Runnable() {
                    @Override public void run() {
                        final List<WarehouseItem> items = loadWarehouseItems();
                        mainH.post(new Runnable() {
                            @Override public void run() {
                                allItems.clear();
                                allItems.addAll(items);
                                rebuildBoxPills.run();
                                applyFilter.run();
                            }
                        });
                    }
                });
            }
        };

        btnRef.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                Menu.NativeQuery(9, 0, 0, 0L, "");
                Toast.makeText(ctx, "正在扫描仓库物品...", Toast.LENGTH_SHORT).show();
                mainH.postDelayed(new Runnable() {
                    @Override public void run() {
                        if (warehouseRefreshHook != null) warehouseRefreshHook.run();
                    }
                }, 200);
            }
        });

        etSearch.addTextChangedListener(new TextWatcher() {
            @Override public void beforeTextChanged(CharSequence s, int start, int count, int after) {}
            @Override public void onTextChanged(CharSequence s, int start, int before, int count) {
                searchKey[0] = (s != null) ? s.toString() : "";
                applyFilter.run();
            }
            @Override public void afterTextChanged(Editable s) {}
        });

        themeRefreshHooks.add(new Runnable() {
            @Override public void run() {
                btnRef.setBackground(makeRoundBox(curTheme.btnBlue, 0x33FFFFFF, 1, dp(7)));
                styleEditBox(etSearch);
                rebuildBoxPills.run();
                adapter.notifyDataSetChanged();
            }
        });

        return wrap;
    }

    // =========================================================================
    //  TAB 2: 货币修改 (原生 CPaySystem: 钻石 / 游园币 / 游园积分 / 累计充值)
    // =========================================================================
    private FrameLayout buildCurrencyPage() {
        final FrameLayout wrap = new FrameLayout(ctx);
        ScrollView sv = new ScrollView(ctx);
        sv.setVerticalScrollBarEnabled(true);
        final LinearLayout col = new LinearLayout(ctx);
        col.setOrientation(LinearLayout.VERTICAL);
        sv.addView(col);
        wrap.addView(sv, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        // Tip banner
        LinearLayout tipCard = new LinearLayout(ctx);
        tipCard.setOrientation(LinearLayout.HORIZONTAL);
        tipCard.setGravity(Gravity.CENTER_VERTICAL);
        tipCard.setPadding(dp(12), dp(8), dp(12), dp(8));
        tipCard.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(8)));
        LinearLayout.LayoutParams tpLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        tpLp.setMargins(0, 0, 0, dp(8));

        TextView tvTip = new TextView(ctx);
        tvTip.setText("原生 CPaySystem 货币修改 (已支持本地持久化保留)\n修改或消费后的余额自动保存，重启游戏 / 重新登录均保持生效");
        tvTip.setTextColor(curTheme.accent);
        tvTip.setTextSize(11.5f);
        tvTip.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        tipCard.addView(tvTip);

        final Button btnRefCurr = makePillBtn("刷新", curTheme.btnBlue, 28);
        btnRefCurr.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                Menu.NativeQuery(9, 0, 0, 0L, "");
                mainH.postDelayed(new Runnable() {
                    @Override public void run() {
                        if (currencyRefreshHook != null) currencyRefreshHook.run();
                        Toast.makeText(ctx, "货币数值已同步", Toast.LENGTH_SHORT).show();
                    }
                }, 150);
            }
        });
        tipCard.addView(btnRefCurr);

        final Button btnClearCurr = makePillBtn("重置全部持久化", curTheme.btnRed, 28);
        btnClearCurr.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                for (int t = 0; t < 4; t++) Menu.NativeQuery(16, t, -1, 0L, "");
                Toast.makeText(ctx, "已清除全部持久化货币锁，下次同步恢复服务器原值", Toast.LENGTH_SHORT).show();
                mainH.postDelayed(new Runnable() {
                    @Override public void run() {
                        if (currencyRefreshHook != null) currencyRefreshHook.run();
                    }
                }, 150);
            }
        });
        tipCard.addView(btnClearCurr);
        col.addView(tipCard, tpLp);

        final String[] names = new String[] { "钻石 (Diamond)", "游园币 (Garden Coin)", "游园积分 (Garden Point)", "累计充值额度 (Cumulative Payment)" };
        final int[][] quickSets = new int[][] {
            { 1000, 10000, 100000, 999999 },
            { 1000, 10000, 100000, 999999 },
            { 1000, 10000, 100000, 999999 },
            { 648, 6480, 64800, 99999 }
        };

        final List<LinearLayout> cCards = new ArrayList<LinearLayout>();
        final List<TextView> cPriTexts = new ArrayList<TextView>();
        final List<TextView> cSecTexts = new ArrayList<TextView>();
        final List<EditText> cEdits = new ArrayList<EditText>();
        final List<Button> cGreenBtns = new ArrayList<Button>();
        final List<Button> cBlueBtns = new ArrayList<Button>();
        final List<Button> cSlateBtns = new ArrayList<Button>();

        cCards.add(tipCard);
        cSecTexts.add(tvTip);
        cBlueBtns.add(btnRefCurr);

        for (int i = 0; i < 4; i++) {
            final int type = i;
            final String cName = names[i];
            final int[] qVals = quickSets[i];

            final LinearLayout card = new LinearLayout(ctx);
            card.setOrientation(LinearLayout.VERTICAL);
            card.setPadding(dp(12), dp(10), dp(12), dp(10));
            card.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(10)));
            LinearLayout.LayoutParams cLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            cLp.setMargins(0, 0, 0, dp(8));

            // Top: Name + Current value
            LinearLayout topR = new LinearLayout(ctx);
            topR.setOrientation(LinearLayout.HORIZONTAL);
            topR.setGravity(Gravity.CENTER_VERTICAL);

            TextView tvTitle = new TextView(ctx);
            tvTitle.setText(cName);
            tvTitle.setTextSize(13);
            tvTitle.setTypeface(null, Typeface.BOLD);
            tvTitle.setTextColor(curTheme.textPri);
            tvTitle.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
            topR.addView(tvTitle);
            cPriTexts.add(tvTitle);

            final TextView tvVal = new TextView(ctx);
            tvVal.setText("当前: 0");
            tvVal.setTextSize(13);
            tvVal.setTypeface(null, Typeface.BOLD);
            tvVal.setTextColor(curTheme.emerald);
            topR.addView(tvVal);
            tvCurrencyVals[i] = tvVal;
            card.addView(topR);

            // Quick add buttons row
            LinearLayout qRow = new LinearLayout(ctx);
            qRow.setOrientation(LinearLayout.HORIZONTAL);
            qRow.setGravity(Gravity.CENTER_VERTICAL);
            LinearLayout.LayoutParams qLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            qLp.setMargins(0, dp(6), 0, dp(6));

            TextView tvQLbl = new TextView(ctx);
            tvQLbl.setText("快捷修改: ");
            tvQLbl.setTextSize(11);
            tvQLbl.setTextColor(curTheme.textSec);
            qRow.addView(tvQLbl);
            cSecTexts.add(tvQLbl);

            for (final int qv : qVals) {
                final boolean isMax = (qv == 999999 || qv == 99999);
                String btnText = isMax ? ("设为 " + qv) : ("+" + qv);
                Button qb = makePillBtn(btnText, isMax ? curTheme.btnGreen : curTheme.btnBlue, 26);
                qb.setTextSize(10);
                qb.setOnClickListener(new View.OnClickListener() {
                    @Override public void onClick(View v) {
                        int target;
                        if (isMax) target = qv;
                        else target = liveCurrency[type] + qv;
                        if (target < 0) target = 0;
                        Menu.NativeQuery(16, type, target, 0L, "");
                        liveCurrency[type] = target;
                        tvVal.setText("当前: " + target);
                        Toast.makeText(ctx, cName + " 已设置为 " + target, Toast.LENGTH_SHORT).show();
                    }
                });
                qRow.addView(qb);
                if (isMax) cGreenBtns.add(qb); else cBlueBtns.add(qb);
            }
            card.addView(qRow, qLp);

            // Custom input row
            LinearLayout inRow = new LinearLayout(ctx);
            inRow.setOrientation(LinearLayout.HORIZONTAL);
            inRow.setGravity(Gravity.CENTER_VERTICAL);

            final EditText etVal = makeDarkEdit("自定义数值...", true);
            etVal.setLayoutParams(new LinearLayout.LayoutParams(0, dp(30), 1f));
            inRow.addView(etVal);
            cEdits.add(etVal);

            final Button btnSet = makePillBtn("设定此数值", curTheme.btnGreen, 30);
            btnSet.setTextSize(10.5f);
            btnSet.setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) {
                    hideKeyboard(v);
                    int val = 0;
                    try { val = Integer.parseInt(etVal.getText().toString().trim()); } catch (Throwable ignored) {}
                    if (val < 0) val = 0;
                    Menu.NativeQuery(16, type, val, 0L, "");
                    liveCurrency[type] = val;
                    tvVal.setText("当前: " + val);
                    Toast.makeText(ctx, cName + " 已设定为 " + val, Toast.LENGTH_SHORT).show();
                }
            });
            inRow.addView(btnSet);
            cGreenBtns.add(btnSet);

            final Button btnAdd = makePillBtn("增加该值", curTheme.btnSlate, 30);
            btnAdd.setTextSize(10.5f);
            btnAdd.setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) {
                    hideKeyboard(v);
                    int delta = 0;
                    try { delta = Integer.parseInt(etVal.getText().toString().trim()); } catch (Throwable ignored) {}
                    int target = liveCurrency[type] + delta;
                    if (target < 0) target = 0;
                    Menu.NativeQuery(16, type, target, 0L, "");
                    liveCurrency[type] = target;
                    tvVal.setText("当前: " + target);
                    Toast.makeText(ctx, cName + " 已设置为 " + target, Toast.LENGTH_SHORT).show();
                }
            });
            inRow.addView(btnAdd);
            cSlateBtns.add(btnAdd);

            final Button btnResetOne = makePillBtn("取消锁定", curTheme.btnRed, 30);
            btnResetOne.setTextSize(10f);
            btnResetOne.setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) {
                    hideKeyboard(v);
                    Menu.NativeQuery(16, type, -1, 0L, "");
                    Toast.makeText(ctx, cName + " 已取消持久化锁定", Toast.LENGTH_SHORT).show();
                    mainH.postDelayed(new Runnable() {
                        @Override public void run() {
                            if (currencyRefreshHook != null) currencyRefreshHook.run();
                        }
                    }, 150);
                }
            });
            inRow.addView(btnResetOne);

            card.addView(inRow);
            cCards.add(card);
            col.addView(card, cLp);
        }

        currencyRefreshHook = new Runnable() {
            @Override public void run() {
                try {
                    File f = new File(ctx.getFilesDir(), "dyn_currency.json");
                    String raw = readFileText(f);
                    if (raw != null && raw.startsWith("{")) {
                        JSONObject o = new JSONObject(raw);
                        liveCurrency[0] = o.optInt("diamond", 0);
                        liveCurrency[1] = o.optInt("gardenCoin", 0);
                        liveCurrency[2] = o.optInt("gardenPoint", 0);
                        liveCurrency[3] = o.has("cumulativePayment") ? o.optInt("cumulativePayment", 0) : o.optInt("cumulativePay", 0);
                        for (int k = 0; k < 4; k++) {
                            if (tvCurrencyVals[k] != null) {
                                tvCurrencyVals[k].setText("当前: " + liveCurrency[k]);
                            }
                        }
                    }
                } catch (Throwable ignored) {}
            }
        };

        themeRefreshHooks.add(new Runnable() {
            @Override public void run() {
                for (LinearLayout c : cCards) {
                    c.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(10)));
                }
                for (TextView tv : cPriTexts) {
                    tv.setTextColor(curTheme.textPri);
                }
                for (TextView tv : cSecTexts) {
                    tv.setTextColor(curTheme.textSec);
                }
                for (EditText et : cEdits) {
                    et.setBackground(makeRoundBox(curTheme.inputBg, curTheme.cardEdge, dp(1), dp(6)));
                    et.setTextColor(curTheme.textPri);
                    et.setHintTextColor(curTheme.textSec);
                }
                for (Button b : cGreenBtns) {
                    b.setBackground(makeRoundBox(curTheme.btnGreen, 0x33FFFFFF, 1, dp(7)));
                    b.setTextColor(0xFFFFFFFF);
                }
                for (Button b : cBlueBtns) {
                    b.setBackground(makeRoundBox(curTheme.btnBlue, 0x33FFFFFF, 1, dp(7)));
                    b.setTextColor(0xFFFFFFFF);
                }
                for (Button b : cSlateBtns) {
                    b.setBackground(makeRoundBox(curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                    b.setTextColor(0xFFFFFFFF);
                }
                for (TextView tv : tvCurrencyVals) {
                    if (tv != null) tv.setTextColor(curTheme.emerald);
                }
            }
        });

        return wrap;
    }

    // =========================================================================
    //  动态加载 天赋/技能/雇佣 配置 + TAB 5 (天赋技能) + TAB 6 (雇佣随从)
    // =========================================================================
    private static synchronized void loadTalentsAndSkills(Context c, boolean forceReload) {
        if (sTalentsCache != null && !forceReload) return;
        List<TalentInfo> tOut = new ArrayList<TalentInfo>(180);
        try {
            String raw = readFileText(new File(c.getFilesDir(), "dyn_talents_skills.json"));
            if (raw.startsWith("{")) {
                JSONObject root = new JSONObject(raw);
                JSONArray ta = root.optJSONArray("talents");
                if (ta != null) {
                    for (int i = 0; i < ta.length(); i++) {
                        JSONObject o = ta.optJSONObject(i);
                        if (o == null) continue;
                        TalentInfo ti = new TalentInfo();
                        ti.id = o.optInt("id");
                        ti.type = o.optInt("type", 1);
                        ti.prop = o.optInt("prop", 0);
                        ti.rate = o.optInt("rate", 0);
                        ti.name = stripGameTags(o.optString("name", "Talent #" + ti.id));
                        JSONArray la = o.optJSONArray("levels");
                        ti.minLv = Integer.MAX_VALUE;
                        ti.maxLv = 1;
                        if (la != null) {
                            for (int j = 0; j < la.length(); j++) {
                                JSONObject lo = la.optJSONObject(j);
                                if (lo == null) continue;
                                TalentLevelInfo li = new TalentLevelInfo();
                                li.lv = lo.optInt("lv", 1);
                                li.param = stripGameTags(lo.optString("param", ""));
                                li.lname = stripGameTags(lo.optString("lname", ""));
                                ti.levels.add(li);
                                if (li.lv < ti.minLv) ti.minLv = li.lv;
                                if (li.lv > ti.maxLv) ti.maxLv = li.lv;
                            }
                            Collections.sort(ti.levels, new Comparator<TalentLevelInfo>() {
                                @Override public int compare(TalentLevelInfo a, TalentLevelInfo b) {
                                    return (a.lv < b.lv) ? -1 : ((a.lv > b.lv) ? 1 : 0);
                                }
                            });
                        }
                        if (ti.minLv == Integer.MAX_VALUE) ti.minLv = 1;
                        tOut.add(ti);
                    }
                    Collections.sort(tOut, new Comparator<TalentInfo>() {
                        @Override public int compare(TalentInfo a, TalentInfo b) {
                            if (a.type != b.type) return (a.type < b.type) ? -1 : 1;
                            return (a.id < b.id) ? -1 : ((a.id > b.id) ? 1 : 0);
                        }
                    });
                }
            }
        } catch (Throwable ignored) {}
        if (!tOut.isEmpty() || sTalentsCache == null) sTalentsCache = tOut;
    }

    private static synchronized void loadHireCatalog(Context c, boolean forceReload) {
        if (sHiresCache != null && sWorkersCache != null && sUnitsCache != null && !forceReload) return;
        List<HireDefInfo> hOut = new ArrayList<HireDefInfo>(24);
        List<WorkerDefInfo> wOut = new ArrayList<WorkerDefInfo>(36);
        List<UnitDefInfo> uOut = new ArrayList<UnitDefInfo>(400);
        try {
            String raw = readFileText(new File(c.getFilesDir(), "dyn_hires.json"));
            if (raw.startsWith("{")) {
                JSONObject root = new JSONObject(raw);
                JSONArray ha = root.optJSONArray("hires");
                if (ha != null) {
                    for (int i = 0; i < ha.length(); i++) {
                        JSONObject o = ha.optJSONObject(i);
                        if (o == null) continue;
                        HireDefInfo hi = new HireDefInfo();
                        hi.id = o.optInt("id");
                        hi.name = stripGameTags(o.optString("name", "Hire #" + hi.id));
                        hi.tpl = o.optString("tpl", "");
                        hOut.add(hi);
                    }
                }
                JSONArray wa = root.optJSONArray("workers");
                if (wa != null) {
                    for (int i = 0; i < wa.length(); i++) {
                        JSONObject o = wa.optJSONObject(i);
                        if (o == null) continue;
                        WorkerDefInfo wi = new WorkerDefInfo();
                        wi.id = o.optInt("id");
                        wi.name = stripGameTags(o.optString("name", "Worker #" + wi.id));
                        wi.wtype = o.optInt("wtype", 0);
                        wi.hp = o.optInt("hp", 0);
                        wi.price = o.optInt("price", 0);
                        wi.spd = o.optInt("spd", 0);
                        wOut.add(wi);
                    }
                }
                JSONArray ua = root.optJSONArray("units");
                if (ua != null) {
                    for (int i = 0; i < ua.length(); i++) {
                        JSONObject o = ua.optJSONObject(i);
                        if (o == null) continue;
                        UnitDefInfo ui = new UnitDefInfo();
                        ui.id = o.optInt("id");
                        ui.cls = o.optInt("cls", 1);
                        ui.name = stripGameTags(o.optString("name", "Unit #" + ui.id));
                        uOut.add(ui);
                    }
                    Collections.sort(uOut, new Comparator<UnitDefInfo>() {
                        @Override public int compare(UnitDefInfo a, UnitDefInfo b) {
                            if (a.cls != b.cls) return (b.cls > a.cls) ? 1 : -1;
                            return (a.id < b.id) ? -1 : ((a.id > b.id) ? 1 : 0);
                        }
                    });
                }
            }
        } catch (Throwable ignored) {}
        if (!hOut.isEmpty() || sHiresCache == null) sHiresCache = hOut;
        if (!wOut.isEmpty() || sWorkersCache == null) sWorkersCache = wOut;
        if (!uOut.isEmpty() || sUnitsCache == null) sUnitsCache = uOut;
    }

    private static int stepTalentLevel(TalentInfo ti, int curLv, boolean up) {
        if (ti == null || ti.levels.isEmpty()) {
            return up ? Math.max(1, curLv + 1) : Math.max(0, curLv - 1);
        }
        if (up) {
            for (int i = 0; i < ti.levels.size(); i++) {
                int lv = ti.levels.get(i).lv;
                if (lv > curLv) return lv;
            }
            return ti.maxLv;
        } else {
            int prev = 0;
            for (int i = 0; i < ti.levels.size(); i++) {
                int lv = ti.levels.get(i).lv;
                if (lv < curLv) prev = lv;
            }
            return prev;
        }
    }

    private FrameLayout buildTalentSkillPage() {
        final FrameLayout wrap = new FrameLayout(ctx);
        LinearLayout root = new LinearLayout(ctx);
        root.setOrientation(LinearLayout.VERTICAL);
        wrap.addView(root, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        loadTalentsAndSkills(ctx, false);

        final int[] curTypeFilter = new int[] { 0 }; // 0=All, 1=擅长技能(type1), 2=称号(type2), 3=已有
        final int[] selectedTargetCid = new int[] { 0 };
        final List<TargetCharInfo> targetsList = new ArrayList<TargetCharInfo>();
        final Map<Integer, Integer> ownedTalentsForTarget = new HashMap<Integer, Integer>(); // talentId -> lv
        final Map<Integer, Map<Integer, Integer>> allOwnedTalents = new HashMap<Integer, Map<Integer, Integer>>();

        final List<TalentInfo> filteredTalents = new ArrayList<TalentInfo>();

        // Row 1: Target character spinner + Refresh
        LinearLayout rTop = new LinearLayout(ctx);
        rTop.setOrientation(LinearLayout.HORIZONTAL);
        rTop.setGravity(Gravity.CENTER_VERTICAL);

        TextView tvModeBadge = new TextView(ctx);
        tvModeBadge.setText("天赋 / 擅长 / 称号");
        tvModeBadge.setTextColor(curTheme.accent);
        tvModeBadge.setTextSize(11.5f);
        tvModeBadge.setTypeface(null, Typeface.BOLD);
        rTop.addView(tvModeBadge);

        TextView tvTgtLbl = new TextView(ctx);
        tvTgtLbl.setText("   对象:");
        tvTgtLbl.setTextColor(curTheme.textSec);
        tvTgtLbl.setTextSize(10.5f);
        rTop.addView(tvTgtLbl);

        final ArrayAdapter<String> tgtAdapter = new ArrayAdapter<String>(ctx, android.R.layout.simple_spinner_item);
        tgtAdapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        tgtAdapter.add("当前主角");
        final Spinner spTarget = new Spinner(ctx);
        spTarget.setAdapter(tgtAdapter);
        spTarget.setLayoutParams(new LinearLayout.LayoutParams(0, dp(30), 1f));
        rTop.addView(spTarget);

        final Button btnRefT = makePillBtn("刷新", curTheme.btnGreen, 28);
        btnRefT.setTextSize(10f);
        rTop.addView(btnRefT);
        root.addView(rTop);

        // Row 2: Search + filter pills
        LinearLayout rFilter = new LinearLayout(ctx);
        rFilter.setOrientation(LinearLayout.HORIZONTAL);
        rFilter.setGravity(Gravity.CENTER_VERTICAL);
        LinearLayout.LayoutParams fLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        fLp.setMargins(0, dp(3), 0, dp(3));

        final EditText etSearch = makeDarkEdit("搜索名称或 ID...", false);
        etSearch.setLayoutParams(new LinearLayout.LayoutParams(0, dp(29), 1f));
        rFilter.addView(etSearch);

        final String[] tfLabels = new String[] { "全部", "擅长技能", "称号", "已有" };
        final List<Button> tfBtns = new ArrayList<Button>();
        for (int i = 0; i < tfLabels.length; i++) {
            final int fi = i;
            Button fb = makePillBtn(tfLabels[i], i == 0 ? curTheme.btnBlue : curTheme.btnSlate, 26);
            fb.setTextSize(10f);
            tfBtns.add(fb);
            rFilter.addView(fb);
        }
        root.addView(rFilter, fLp);

        final TextView tvSummary = new TextView(ctx);
        tvSummary.setTextColor(curTheme.textSec);
        tvSummary.setTextSize(10.5f);
        tvSummary.setPadding(dp(2), 0, dp(2), dp(2));
        root.addView(tvSummary);

        final ListView lv = new ListView(ctx);
        lv.setDivider(null);
        lv.setDividerHeight(0);
        lv.setLayoutParams(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        root.addView(lv);

        final class TsHolder {
            LinearLayout card;
            ImageView iv;
            TextView tvTitle, tvSub, tvLv;
            Button bDec, bAdd, bMax, bDel;
        }

        final Runnable[] rebuildRef = new Runnable[1];
        final BaseAdapter adapter = new BaseAdapter() {
            @Override public int getCount() {
                return filteredTalents.size();
            }
            @Override public Object getItem(int pos) { return pos; }
            @Override public long getItemId(int pos) { return pos; }

            @Override public View getView(int pos, View convertView, ViewGroup parent) {
                TsHolder h;
                if (convertView == null) {
                    h = new TsHolder();
                    LinearLayout row = new LinearLayout(ctx);
                    row.setOrientation(LinearLayout.HORIZONTAL);
                    row.setGravity(Gravity.CENTER_VERTICAL);
                    row.setPadding(dp(10), dp(6), dp(10), dp(6));

                    LinearLayout outer = new LinearLayout(ctx);
                    outer.setOrientation(LinearLayout.VERTICAL);
                    outer.setPadding(0, dp(2), 0, dp(2));
                    outer.addView(row, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

                    h.iv = new ImageView(ctx);
                    LinearLayout.LayoutParams ivLp = new LinearLayout.LayoutParams(dp(32), dp(32));
                    ivLp.setMargins(0, 0, dp(8), 0);
                    row.addView(h.iv, ivLp);

                    LinearLayout mid = new LinearLayout(ctx);
                    mid.setOrientation(LinearLayout.VERTICAL);
                    mid.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
                    h.tvTitle = new TextView(ctx);
                    h.tvTitle.setTextSize(12f);
                    h.tvTitle.setTypeface(null, Typeface.BOLD);
                    h.tvTitle.setSingleLine(true);
                    h.tvTitle.setEllipsize(TextUtils.TruncateAt.END);
                    mid.addView(h.tvTitle);

                    h.tvSub = new TextView(ctx);
                    h.tvSub.setTextSize(10f);
                    h.tvSub.setSingleLine(true);
                    h.tvSub.setEllipsize(TextUtils.TruncateAt.END);
                    mid.addView(h.tvSub);
                    row.addView(mid);

                    h.tvLv = new TextView(ctx);
                    h.tvLv.setTextSize(11f);
                    h.tvLv.setTypeface(null, Typeface.BOLD);
                    h.tvLv.setPadding(dp(4), 0, dp(6), 0);
                    row.addView(h.tvLv);

                    h.bDec = makePillBtn("-1", curTheme.btnSlate, 25);
                    h.bAdd = makePillBtn("+1", curTheme.btnGreen, 25);
                    h.bMax = makePillBtn("满级", curTheme.btnBlue, 25);
                    h.bDel = makePillBtn("移除", curTheme.btnRed, 25);
                    h.bDec.setTextSize(10f); h.bAdd.setTextSize(10f); h.bMax.setTextSize(10f); h.bDel.setTextSize(10f);
                    row.addView(h.bDec);
                    row.addView(h.bAdd);
                    row.addView(h.bMax);
                    row.addView(h.bDel);

                    h.card = row;
                    convertView = outer;
                    convertView.setTag(h);
                } else {
                    h = (TsHolder) convertView.getTag();
                }

                final int tc = selectedTargetCid[0];
                final TalentInfo ti = filteredTalents.get(pos);
                final int curLv = ownedTalentsForTarget.containsKey(ti.id) ? ownedTalentsForTarget.get(ti.id) : 0;
                boolean owned = (curLv > 0);
                h.iv.setVisibility(View.GONE);
                h.card.setBackground(makeRoundBox(owned ? curTheme.cardOnBg : curTheme.cardBg, owned ? curTheme.cardOnEdge : curTheme.cardEdge, dp(1), dp(8)));
                String tag = (ti.type == 2) ? "[称号] " : "[擅长] ";
                h.tvTitle.setText(tag + ti.name + "   #" + ti.id);
                h.tvTitle.setTextColor(curTheme.textPri);

                StringBuilder lvInfo = new StringBuilder();
                lvInfo.append("级数:").append(ti.levels.size()).append(" (").append(ti.minLv).append("~").append(ti.maxLv).append(")");
                for (TalentLevelInfo li : ti.levels) {
                    if ((owned && li.lv == curLv) || (!owned && li.lv == ti.minLv)) {
                        if (li.param != null && !li.param.isEmpty()) lvInfo.append(" · 数值:").append(li.param);
                        break;
                    }
                }
                h.tvSub.setText(lvInfo.toString());
                h.tvSub.setTextColor(curTheme.textSec);

                h.tvLv.setText(owned ? ("Lv." + curLv) : "未习得");
                h.tvLv.setTextColor(owned ? curTheme.emerald : curTheme.textSec);

                h.bDec.setVisibility(owned ? View.VISIBLE : View.GONE);
                h.bAdd.setVisibility(View.VISIBLE);
                h.bAdd.setText(owned ? "升级" : "添加");
                h.bMax.setVisibility(ti.maxLv > ti.minLv ? View.VISIBLE : View.GONE);
                h.bDel.setVisibility(owned ? View.VISIBLE : View.GONE);

                h.bDec.setOnClickListener(new View.OnClickListener() {
                    @Override public void onClick(View v) {
                        int next = stepTalentLevel(ti, curLv, false);
                        Menu.NativeQuery(18, ti.id, next <= 0 ? 1 : 0, (long) tc, String.valueOf(next));
                        mainH.postDelayed(new Runnable() { @Override public void run() { if (talentStateRefreshHook != null) talentStateRefreshHook.run(); } }, 180);
                    }
                });
                h.bAdd.setOnClickListener(new View.OnClickListener() {
                    @Override public void onClick(View v) {
                        int next = stepTalentLevel(ti, curLv, true);
                        Menu.NativeQuery(18, ti.id, 0, (long) tc, String.valueOf(next));
                        mainH.postDelayed(new Runnable() { @Override public void run() { if (talentStateRefreshHook != null) talentStateRefreshHook.run(); } }, 180);
                    }
                });
                h.bMax.setOnClickListener(new View.OnClickListener() {
                    @Override public void onClick(View v) {
                        Menu.NativeQuery(18, ti.id, 0, (long) tc, String.valueOf(ti.maxLv));
                        mainH.postDelayed(new Runnable() { @Override public void run() { if (talentStateRefreshHook != null) talentStateRefreshHook.run(); } }, 180);
                    }
                });
                h.bDel.setOnClickListener(new View.OnClickListener() {
                    @Override public void onClick(View v) {
                        Menu.NativeQuery(18, ti.id, 1, (long) tc, "0");
                        mainH.postDelayed(new Runnable() { @Override public void run() { if (talentStateRefreshHook != null) talentStateRefreshHook.run(); } }, 180);
                    }
                });
                return convertView;
            }
        };
        lv.setAdapter(adapter);

        final Runnable applyFilter = new Runnable() {
            @Override public void run() {
                String q = etSearch.getText().toString().trim().toLowerCase(Locale.US);
                ownedTalentsForTarget.clear();
                Map<Integer, Integer> tgtMap = allOwnedTalents.get(selectedTargetCid[0]);
                if (tgtMap != null) ownedTalentsForTarget.putAll(tgtMap);

                filteredTalents.clear();
                List<TalentInfo> src = sTalentsCache;
                if (src != null) {
                    for (TalentInfo ti : src) {
                        int f = curTypeFilter[0];
                        if (f == 1 && ti.type != 1) continue;
                        if (f == 2 && ti.type != 2) continue;
                        if (f == 3 && !ownedTalentsForTarget.containsKey(ti.id)) continue;
                        if (!q.isEmpty() && !ti.name.toLowerCase(Locale.US).contains(q) && !String.valueOf(ti.id).contains(q))
                            continue;
                        filteredTalents.add(ti);
                    }
                }
                tvSummary.setText("目标对象已拥有天赋/擅长: " + ownedTalentsForTarget.size() + " 项 · 当前显示 " + filteredTalents.size() + " 项 (已自动绕过主角黑名单)");
                adapter.notifyDataSetChanged();
            }
        };
        rebuildRef[0] = applyFilter;

        talentStateRefreshHook = new Runnable() {
            @Override public void run() {
                loadTalentsAndSkills(ctx, false);
                try {
                    String raw = readFileText(new File(ctx.getFilesDir(), "dyn_talent_state.json"));
                    if (raw.startsWith("{")) {
                        JSONObject o = new JSONObject(raw);
                        int curCid = o.optInt("cid", 0);
                        targetsList.clear();
                        JSONArray ta = o.optJSONArray("targets");
                        if (ta != null) {
                            for (int i = 0; i < ta.length(); i++) {
                                JSONObject to = ta.optJSONObject(i);
                                if (to == null) continue;
                                TargetCharInfo tc = new TargetCharInfo();
                                tc.cid = to.optInt("cid");
                                tc.name = to.optString("name", "#" + tc.cid);
                                targetsList.add(tc);
                            }
                        }
                        if (selectedTargetCid[0] == 0 && !targetsList.isEmpty()) {
                            selectedTargetCid[0] = targetsList.get(0).cid;
                        }
                        tgtAdapter.clear();
                        int selIdx = 0;
                        for (int i = 0; i < targetsList.size(); i++) {
                            TargetCharInfo tci = targetsList.get(i);
                            tgtAdapter.add(tci.name + " #" + tci.cid);
                            if (tci.cid == selectedTargetCid[0]) selIdx = i;
                        }
                        if (targetsList.isEmpty()) {
                            tgtAdapter.add(curCid > 0 ? ("主角 #" + curCid) : "未进入存档");
                            selectedTargetCid[0] = curCid;
                        }
                        tgtAdapter.notifyDataSetChanged();
                        if (selIdx < tgtAdapter.getCount()) spTarget.setSelection(selIdx);

                        allOwnedTalents.clear();
                        JSONArray oa = o.optJSONArray("owned");
                        if (oa != null) {
                            for (int i = 0; i < oa.length(); i++) {
                                JSONObject oo = oa.optJSONObject(i);
                                if (oo == null) continue;
                                int tc = oo.optInt("tc");
                                int tid = oo.optInt("id");
                                int lv = oo.optInt("lv");
                                Map<Integer, Integer> m = allOwnedTalents.get(tc);
                                if (m == null) { m = new HashMap<Integer, Integer>(); allOwnedTalents.put(tc, m); }
                                m.put(tid, lv);
                            }
                        }
                    }
                } catch (Throwable ignored) {}
                applyFilter.run();
            }
        };

        spTarget.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
            @Override public void onItemSelected(AdapterView<?> p, View v, int pos, long id) {
                if (pos >= 0 && pos < targetsList.size()) {
                    int ncid = targetsList.get(pos).cid;
                    if (selectedTargetCid[0] != ncid) {
                        selectedTargetCid[0] = ncid;
                        applyFilter.run();
                    }
                }
            }
            @Override public void onNothingSelected(AdapterView<?> p) {}
        });

        for (int i = 0; i < tfBtns.size(); i++) {
            final int fi = i;
            tfBtns.get(i).setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) {
                    curTypeFilter[0] = fi;
                    for (int k = 0; k < tfBtns.size(); k++) {
                        tfBtns.get(k).setBackground(makeRoundBox(k == fi ? curTheme.btnBlue : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                    }
                    applyFilter.run();
                }
            });
        }

        btnRefT.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                Menu.NativeQuery(9, 0, 0, 0L, "");
                mainH.postDelayed(new Runnable() {
                    @Override public void run() {
                        if (talentStateRefreshHook != null) talentStateRefreshHook.run();
                    }
                }, 180);
            }
        });

        etSearch.addTextChangedListener(new TextWatcher() {
            @Override public void beforeTextChanged(CharSequence s, int a, int b, int c) {}
            @Override public void onTextChanged(CharSequence s, int a, int b, int c) {}
            @Override public void afterTextChanged(Editable s) { applyFilter.run(); }
        });

        themeRefreshHooks.add(new Runnable() {
            @Override public void run() {
                styleEditBox(etSearch);
                tvSummary.setTextColor(curTheme.textSec);
                tvModeBadge.setTextColor(curTheme.accent);
                btnRefT.setBackground(makeRoundBox(curTheme.btnGreen, 0x33FFFFFF, 1, dp(7)));
                for (int k = 0; k < tfBtns.size(); k++) {
                    tfBtns.get(k).setBackground(makeRoundBox(k == curTypeFilter[0] ? curTheme.btnBlue : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                }
                adapter.notifyDataSetChanged();
            }
        });

        applyFilter.run();
        return wrap;
    }

    private FrameLayout buildHirePage() {
        final FrameLayout wrap = new FrameLayout(ctx);
        LinearLayout root = new LinearLayout(ctx);
        root.setOrientation(LinearLayout.VERTICAL);
        wrap.addView(root, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        loadHireCatalog(ctx, false);

        final int[] curSubTab = new int[] { 0 }; // 0=Hire(剧情随从), 1=Worker(战士/工人), 2=ForceUnit(全Unit强雇), 3=Nearby(现场收编)
        final int[] curForceCat = new int[] { 0 }; // 0=全部, 1=工人/角色(cls==3), 2=NPC(cls==2), 3=单位/首领(cls!=2&&cls!=3)
        final int[] tameRangeM = new int[] { 25 };
        final boolean[] tameTracking = new boolean[] { false };
        final Runnable[] applyFilterRef = new Runnable[1];
        final Set<Integer> ownedHires = new HashSet<Integer>();
        final Set<Integer> ownedWorkers = new HashSet<Integer>();
        final Set<Integer> activeTeamates = new HashSet<Integer>();
        final Set<Integer> forcedIds = new HashSet<Integer>();
        final List<NearbyEntityInfo> nearbyList = new ArrayList<NearbyEntityInfo>();

        final List<HireDefInfo> filHires = new ArrayList<HireDefInfo>();
        final List<WorkerDefInfo> filWorkers = new ArrayList<WorkerDefInfo>();
        final List<UnitDefInfo> filUnits = new ArrayList<UnitDefInfo>();
        final List<NearbyEntityInfo> filNearby = new ArrayList<NearbyEntityInfo>();

        // Row 1: Sub-tab selector buttons
        LinearLayout rTabs = new LinearLayout(ctx);
        rTabs.setOrientation(LinearLayout.HORIZONTAL);
        rTabs.setGravity(Gravity.CENTER_VERTICAL);
        final String[] hNames = new String[] { "剧情随从", "营地战士/工人", "强制雇佣 (全NPC)", "现场收编" };
        final List<Button> hTabBtns = new ArrayList<Button>();
        hireSubTabBtns.clear();
        for (int i = 0; i < hNames.length; i++) {
            final int ti = i;
            Button b = makePillBtn(hNames[i], i == 0 ? curTheme.btnBlue : curTheme.btnSlate, 28);
            b.setTextSize(10f);
            hTabBtns.add(b);
            hireSubTabBtns.add(b);
            rTabs.addView(b);
        }
        root.addView(rTabs);

        // Row 2: Search + Refresh + Clear Force-Hire
        LinearLayout rSearch = new LinearLayout(ctx);
        rSearch.setOrientation(LinearLayout.HORIZONTAL);
        rSearch.setGravity(Gravity.CENTER_VERTICAL);
        LinearLayout.LayoutParams sLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        sLp.setMargins(0, dp(3), 0, dp(3));

        final EditText etSearch = makeDarkEdit("搜索随从 / NPC 名称或 ID...", false);
        etSearch.setLayoutParams(new LinearLayout.LayoutParams(0, dp(29), 1f));
        rSearch.addView(etSearch);

        final Button btnRefH = makePillBtn("刷新列表", curTheme.btnGreen, 28);
        final Button btnClearForce = makePillBtn("清空强雇", curTheme.btnRed, 28);
        btnClearForce.setVisibility(View.GONE);
        rSearch.addView(btnRefH);
        rSearch.addView(btnClearForce);
        root.addView(rSearch, sLp);

        // Row 2b: Force-Hire sub-category filter bar ([全部] [工人/角色] [NPC] [单位/首领])
        final LinearLayout rForceCat = new LinearLayout(ctx);
        rForceCat.setOrientation(LinearLayout.HORIZONTAL);
        rForceCat.setGravity(Gravity.CENTER_VERTICAL);
        rForceCat.setVisibility(View.GONE);
        LinearLayout.LayoutParams fcLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        fcLp.setMargins(0, 0, 0, dp(3));
        final String[] fcNames = new String[] { "全部", "工人/角色", "NPC", "单位/首领" };
        final List<Button> fcBtns = new ArrayList<Button>();
        for (int i = 0; i < fcNames.length; i++) {
            final int fi = i;
            Button fb = makePillBtn(fcNames[i], i == 0 ? curTheme.btnBlue : curTheme.btnSlate, 25);
            fb.setTextSize(10f);
            fb.setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) {
                    curForceCat[0] = fi;
                    for (int k = 0; k < fcBtns.size(); k++) {
                        fcBtns.get(k).setBackground(makeRoundBox(k == fi ? curTheme.btnBlue : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                    }
                    if (applyFilterRef[0] != null) applyFilterRef[0].run();
                }
            });
            fcBtns.add(fb);
            rForceCat.addView(fb);
        }
        root.addView(rForceCat, fcLp);

        // Row 2c: Nearby Tame scan range bar (SeekBar 5m..200m + quick presets including 全图)
        final LinearLayout rTameDist = new LinearLayout(ctx);
        rTameDist.setOrientation(LinearLayout.VERTICAL);
        rTameDist.setVisibility(View.GONE);
        LinearLayout.LayoutParams tdLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        tdLp.setMargins(0, 0, 0, dp(3));

        LinearLayout rTameTop = new LinearLayout(ctx);
        rTameTop.setOrientation(LinearLayout.HORIZONTAL);
        rTameTop.setGravity(Gravity.CENTER_VERTICAL);

        final TextView tvTameRange = new TextView(ctx);
        tvTameRange.setText("扫描范围: 25m");
        tvTameRange.setTextColor(curTheme.accent);
        tvTameRange.setTextSize(10.5f);
        tvTameRange.setTypeface(null, Typeface.BOLD);
        rTameTop.addView(tvTameRange, new LinearLayout.LayoutParams(dp(92), ViewGroup.LayoutParams.WRAP_CONTENT));

        final SeekBar sbTame = new SeekBar(ctx);
        sbTame.setMax(195); // 5m .. 200m
        sbTame.setProgress(20);
        sbTame.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        rTameTop.addView(sbTame);
        rTameDist.addView(rTameTop);

        LinearLayout rTamePresets = new LinearLayout(ctx);
        rTamePresets.setOrientation(LinearLayout.HORIZONTAL);
        rTamePresets.setGravity(Gravity.CENTER_VERTICAL);
        LinearLayout.LayoutParams tpLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        tpLp.setMargins(0, dp(2), 0, 0);
        final int[] tamePresetVals = new int[] { 15, 25, 50, 100, 999 };
        final String[] tamePresetLabels = new String[] { "15m", "25m", "50m", "100m", "全图" };
        final List<Button> tamePresetBtns = new ArrayList<Button>();
        final Runnable syncTameDistUi = new Runnable() {
            @Override public void run() {
                int r = tameRangeM[0];
                tvTameRange.setText(r >= 999 ? "扫描范围: 全图" : ("扫描范围: " + r + "m"));
                sbTame.setProgress(Math.min(195, Math.max(0, r - 5)));
                for (int k = 0; k < tamePresetBtns.size(); k++) {
                    boolean active = (r >= 999 ? tamePresetVals[k] >= 999 : tamePresetVals[k] == r);
                    tamePresetBtns.get(k).setBackground(makeRoundBox(active ? curTheme.btnBlue : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                }
            }
        };
        for (int i = 0; i < tamePresetVals.length; i++) {
            final int pv = tamePresetVals[i];
            Button pb = makePillBtn(tamePresetLabels[i], pv == 25 ? curTheme.btnBlue : curTheme.btnSlate, 24);
            pb.setTextSize(9.5f);
            pb.setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) {
                    tameRangeM[0] = pv;
                    syncTameDistUi.run();
                    Menu.NativeQuery(19, pv, 8, 0L, "0");
                    mainH.postDelayed(new Runnable() {
                        @Override public void run() {
                            if (hireStateRefreshHook != null) hireStateRefreshHook.run();
                        }
                    }, 160);
                }
            });
            tamePresetBtns.add(pb);
            rTamePresets.addView(pb);
        }
        rTameDist.addView(rTamePresets, tpLp);

        sbTame.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener() {
            @Override public void onProgressChanged(SeekBar s, int progress, boolean fromUser) {
                if (!fromUser) return;
                int r = Math.max(5, Math.min(200, progress + 5));
                tameRangeM[0] = r;
                tvTameRange.setText("扫描范围: " + r + "m");
                for (int k = 0; k < tamePresetBtns.size(); k++) {
                    tamePresetBtns.get(k).setBackground(makeRoundBox(tamePresetVals[k] == r ? curTheme.btnBlue : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                }
            }
            @Override public void onStartTrackingTouch(SeekBar s) { tameTracking[0] = true; }
            @Override public void onStopTrackingTouch(SeekBar s) {
                tameTracking[0] = false;
                Menu.NativeQuery(19, tameRangeM[0], 8, 0L, "0");
                mainH.postDelayed(new Runnable() {
                    @Override public void run() {
                        if (hireStateRefreshHook != null) hireStateRefreshHook.run();
                    }
                }, 160);
            }
        });
        root.addView(rTameDist, tdLp);

        final TextView tvSummary = new TextView(ctx);
        tvSummary.setTextColor(curTheme.textSec);
        tvSummary.setTextSize(10.5f);
        tvSummary.setPadding(dp(2), 0, dp(2), dp(2));
        root.addView(tvSummary);

        final ListView lv = new ListView(ctx);
        lv.setDivider(null);
        lv.setDividerHeight(0);
        lv.setLayoutParams(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        root.addView(lv);

        final class HireHolder {
            LinearLayout card;
            TextView tvTitle, tvSub, tvState;
            Button b1, b2, b3;
        }

        final BaseAdapter adapter = new BaseAdapter() {
            @Override public int getCount() {
                if (curSubTab[0] == 0) return filHires.size();
                if (curSubTab[0] == 1) return filWorkers.size();
                if (curSubTab[0] == 2) return filUnits.size();
                return filNearby.size();
            }
            @Override public Object getItem(int pos) { return pos; }
            @Override public long getItemId(int pos) { return pos; }

            @Override public View getView(int pos, View convertView, ViewGroup parent) {
                HireHolder h;
                if (convertView == null) {
                    h = new HireHolder();
                    LinearLayout row = new LinearLayout(ctx);
                    row.setOrientation(LinearLayout.HORIZONTAL);
                    row.setGravity(Gravity.CENTER_VERTICAL);
                    row.setPadding(dp(10), dp(6), dp(10), dp(6));

                    LinearLayout outer = new LinearLayout(ctx);
                    outer.setOrientation(LinearLayout.VERTICAL);
                    outer.setPadding(0, dp(2), 0, dp(2));
                    outer.addView(row, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

                    LinearLayout mid = new LinearLayout(ctx);
                    mid.setOrientation(LinearLayout.VERTICAL);
                    mid.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
                    h.tvTitle = new TextView(ctx);
                    h.tvTitle.setTextSize(12f);
                    h.tvTitle.setTypeface(null, Typeface.BOLD);
                    h.tvTitle.setSingleLine(true);
                    h.tvTitle.setEllipsize(TextUtils.TruncateAt.END);
                    mid.addView(h.tvTitle);

                    h.tvSub = new TextView(ctx);
                    h.tvSub.setTextSize(10f);
                    h.tvSub.setSingleLine(true);
                    h.tvSub.setEllipsize(TextUtils.TruncateAt.END);
                    mid.addView(h.tvSub);
                    row.addView(mid);

                    h.tvState = new TextView(ctx);
                    h.tvState.setTextSize(10.5f);
                    h.tvState.setTypeface(null, Typeface.BOLD);
                    h.tvState.setPadding(dp(4), 0, dp(6), 0);
                    row.addView(h.tvState);

                    h.b1 = makePillBtn("雇佣", curTheme.btnGreen, 25);
                    h.b2 = makePillBtn("出战", curTheme.btnBlue, 25);
                    h.b3 = makePillBtn("解除", curTheme.btnRed, 25);
                    h.b1.setTextSize(10f); h.b2.setTextSize(10f); h.b3.setTextSize(10f);
                    row.addView(h.b1);
                    row.addView(h.b2);
                    row.addView(h.b3);

                    h.card = row;
                    convertView = outer;
                    convertView.setTag(h);
                } else {
                    h = (HireHolder) convertView.getTag();
                }

                if (curSubTab[0] == 0) {
                    final HireDefInfo hi = filHires.get(pos);
                    boolean on = ownedHires.contains(hi.id);
                    h.card.setBackground(makeRoundBox(on ? curTheme.cardOnBg : curTheme.cardBg, on ? curTheme.cardOnEdge : curTheme.cardEdge, dp(1), dp(8)));
                    h.tvTitle.setText(hi.name + "   #" + hi.id);
                    h.tvTitle.setTextColor(curTheme.textPri);
                    h.tvSub.setText("剧情随从模板: " + hi.tpl);
                    h.tvSub.setTextColor(curTheme.textSec);
                    h.tvState.setText(on ? "● 随行中" : "未雇佣");
                    h.tvState.setTextColor(on ? curTheme.emerald : curTheme.textSec);
                    h.b1.setVisibility(View.VISIBLE);
                    h.b1.setText("雇佣随行");
                    h.b2.setVisibility(View.GONE);
                    h.b3.setVisibility(on ? View.VISIBLE : View.GONE);
                    h.b3.setText("解除");

                    h.b1.setOnClickListener(new View.OnClickListener() {
                        @Override public void onClick(View v) {
                            Menu.NativeQuery(19, hi.id, 0, 0L, "0");
                            Toast.makeText(ctx, "已雇佣剧情随从 " + hi.name, Toast.LENGTH_SHORT).show();
                            mainH.postDelayed(new Runnable() { @Override public void run() { if (hireStateRefreshHook != null) hireStateRefreshHook.run(); } }, 200);
                        }
                    });
                    h.b3.setOnClickListener(new View.OnClickListener() {
                        @Override public void onClick(View v) {
                            Menu.NativeQuery(19, hi.id, 1, 0L, "0");
                            mainH.postDelayed(new Runnable() { @Override public void run() { if (hireStateRefreshHook != null) hireStateRefreshHook.run(); } }, 200);
                        }
                    });
                } else if (curSubTab[0] == 1) {
                    final WorkerDefInfo wi = filWorkers.get(pos);
                    boolean isW = ownedWorkers.contains(wi.id);
                    boolean isT = activeTeamates.contains(wi.id);
                    h.card.setBackground(makeRoundBox((isW || isT) ? curTheme.cardOnBg : curTheme.cardBg, (isW || isT) ? curTheme.cardOnEdge : curTheme.cardEdge, dp(1), dp(8)));
                    String role = (wi.wtype == 4 ? "战士" : (wi.wtype == 1 ? "商人" : (wi.wtype == 2 ? "伐木" : (wi.wtype == 3 ? "铁矿" : (wi.wtype == 6 ? "硝石" : "营地")))));
                    h.tvTitle.setText("[" + role + "] " + wi.name + "   #" + wi.id);
                    h.tvTitle.setTextColor(curTheme.textPri);
                    h.tvSub.setText("HP:" + wi.hp + " · 日薪:" + wi.price + " · 生产速度:" + wi.spd);
                    h.tvSub.setTextColor(curTheme.textSec);
                    h.tvState.setText(isT ? "● 出战随行" : (isW ? "● 营地已雇" : "未雇佣"));
                    h.tvState.setTextColor(isT ? curTheme.emerald : (isW ? curTheme.accent : curTheme.textSec));

                    h.b1.setVisibility(View.VISIBLE);
                    h.b1.setText("进营地");
                    h.b2.setVisibility(View.VISIBLE);
                    h.b2.setText("出战随行");
                    h.b3.setVisibility((isW || isT) ? View.VISIBLE : View.GONE);
                    h.b3.setText(isT ? "召回/解雇" : "解雇");

                    h.b1.setOnClickListener(new View.OnClickListener() {
                        @Override public void onClick(View v) {
                            Menu.NativeQuery(19, wi.id, 2, 0L, "0");
                            Toast.makeText(ctx, "已添加至营地并付薪: " + wi.name, Toast.LENGTH_SHORT).show();
                            mainH.postDelayed(new Runnable() { @Override public void run() { if (hireStateRefreshHook != null) hireStateRefreshHook.run(); } }, 200);
                        }
                    });
                    h.b2.setOnClickListener(new View.OnClickListener() {
                        @Override public void onClick(View v) {
                            Menu.NativeQuery(19, wi.id, 2, 0L, "1");
                            Toast.makeText(ctx, "已出战跟随 (自动配武器): " + wi.name, Toast.LENGTH_SHORT).show();
                            mainH.postDelayed(new Runnable() { @Override public void run() { if (hireStateRefreshHook != null) hireStateRefreshHook.run(); } }, 200);
                        }
                    });
                    h.b3.setOnClickListener(new View.OnClickListener() {
                        @Override public void onClick(View v) {
                            Menu.NativeQuery(19, wi.id, 3, 0L, "0");
                            mainH.postDelayed(new Runnable() { @Override public void run() { if (hireStateRefreshHook != null) hireStateRefreshHook.run(); } }, 200);
                        }
                    });
                } else if (curSubTab[0] == 2) {
                    final UnitDefInfo ui = filUnits.get(pos);
                    boolean on = forcedIds.contains(ui.id);
                    h.card.setBackground(makeRoundBox(on ? curTheme.cardOnBg : curTheme.cardBg, on ? curTheme.cardOnEdge : curTheme.cardEdge, dp(1), dp(8)));
                    String clsNm = (ui.cls == 2 ? "NPC" : (ui.cls == 3 ? "工人/角色" : "单位/首领"));
                    h.tvTitle.setText("[" + clsNm + "] " + ui.name + "   #" + ui.id);
                    h.tvTitle.setTextColor(curTheme.textPri);
                    h.tvSub.setText("突破原版限制：强制赋予随从AI与对应外观召唤跟随");
                    h.tvSub.setTextColor(curTheme.textSec);
                    h.tvState.setText(on ? "● 强雇随行中" : "可强雇");
                    h.tvState.setTextColor(on ? curTheme.emerald : curTheme.textSec);

                    h.b1.setVisibility(View.VISIBLE);
                    h.b1.setText("强制雇佣");
                    h.b2.setVisibility(View.GONE);
                    h.b3.setVisibility(on ? View.VISIBLE : View.GONE);
                    h.b3.setText("撤回");

                    h.b1.setOnClickListener(new View.OnClickListener() {
                        @Override public void onClick(View v) {
                            Menu.NativeQuery(19, ui.id, 4, 0L, "0");
                            Toast.makeText(ctx, "正在强制召唤: " + ui.name, Toast.LENGTH_SHORT).show();
                            mainH.postDelayed(new Runnable() { @Override public void run() { if (hireStateRefreshHook != null) hireStateRefreshHook.run(); } }, 250);
                        }
                    });
                    h.b3.setOnClickListener(new View.OnClickListener() {
                        @Override public void onClick(View v) {
                            Menu.NativeQuery(19, ui.id, 5, 0L, "0");
                            mainH.postDelayed(new Runnable() { @Override public void run() { if (hireStateRefreshHook != null) hireStateRefreshHook.run(); } }, 250);
                        }
                    });
                } else {
                    final NearbyEntityInfo ne = filNearby.get(pos);
                    final boolean tamed = ne.tamed;
                    h.card.setBackground(makeRoundBox(tamed ? curTheme.cardOnBg : curTheme.cardBg, tamed ? curTheme.cardOnEdge : curTheme.cardEdge, dp(1), dp(8)));
                    h.tvTitle.setText(ne.name + "   #" + ne.id);
                    h.tvTitle.setTextColor(curTheme.textPri);
                    h.tvSub.setText(String.format(Locale.US, "距离:%.1fm · 阵营:%d · HP:%d/%d · UID:%s", ne.dist, ne.camp, ne.hp, ne.maxHp, ne.uid));
                    h.tvSub.setTextColor(curTheme.textSec);
                    h.tvState.setText(tamed ? "● 已收编" : "在场单位");
                    h.tvState.setTextColor(tamed ? curTheme.emerald : curTheme.accent);
                    if (tamed) {
                        h.b1.setVisibility(View.GONE);
                        h.b2.setVisibility(View.GONE);
                        h.b3.setVisibility(View.VISIBLE);
                        h.b3.setText("撤回");
                        h.b3.setOnClickListener(new View.OnClickListener() {
                            @Override public void onClick(View v) {
                                long uidLong = 0L;
                                try { uidLong = new java.math.BigInteger(ne.uid).longValue(); } catch (Throwable ignored) {}
                                Menu.NativeQuery(19, ne.id, 9, uidLong, "0");
                                Toast.makeText(ctx, "已撤回收编 " + ne.name, Toast.LENGTH_SHORT).show();
                                mainH.postDelayed(new Runnable() { @Override public void run() { if (hireStateRefreshHook != null) hireStateRefreshHook.run(); } }, 200);
                            }
                        });
                    } else {
                        h.b1.setVisibility(View.VISIBLE);
                        h.b1.setText("收编为友军");
                        h.b2.setVisibility(View.GONE);
                        h.b3.setVisibility(View.GONE);
                        h.b1.setOnClickListener(new View.OnClickListener() {
                            @Override public void onClick(View v) {
                                long uidLong = 0L;
                                try { uidLong = new java.math.BigInteger(ne.uid).longValue(); } catch (Throwable ignored) {}
                                Menu.NativeQuery(19, ne.id, 6, uidLong, "0");
                                Toast.makeText(ctx, "已将 " + ne.name + " 收编为友军!", Toast.LENGTH_SHORT).show();
                                mainH.postDelayed(new Runnable() { @Override public void run() { if (hireStateRefreshHook != null) hireStateRefreshHook.run(); } }, 200);
                            }
                        });
                    }
                }
                return convertView;
            }
        };
        lv.setAdapter(adapter);

        final Runnable applyFilter = new Runnable() {
            @Override public void run() {
                String q = etSearch.getText().toString().trim().toLowerCase(Locale.US);
                if (curSubTab[0] == 0) {
                    filHires.clear();
                    if (sHiresCache != null) {
                        for (HireDefInfo hi : sHiresCache) {
                            if (q.isEmpty() || hi.name.toLowerCase(Locale.US).contains(q) || String.valueOf(hi.id).contains(q))
                                filHires.add(hi);
                        }
                    }
                    tvSummary.setText("Hire.csv 剧情随从 (尼奥/K/姜娜/老吴/消防无人机等): " + filHires.size() + " 项 · 当前已雇 " + ownedHires.size() + " 位");
                } else if (curSubTab[0] == 1) {
                    filWorkers.clear();
                    if (sWorkersCache != null) {
                        for (WorkerDefInfo wi : sWorkersCache) {
                            if (q.isEmpty() || wi.name.toLowerCase(Locale.US).contains(q) || String.valueOf(wi.id).contains(q))
                                filWorkers.add(wi);
                        }
                    }
                    tvSummary.setText("Worker.csv 营地战士/工人: " + filWorkers.size() + " 项 · 已雇 " + ownedWorkers.size() + " · 出战 " + activeTeamates.size() + " (无视人数上限)");
                } else if (curSubTab[0] == 2) {
                    filUnits.clear();
                    int fc = curForceCat[0];
                    if (sUnitsCache != null) {
                        for (UnitDefInfo ui : sUnitsCache) {
                            if (fc == 1 && ui.cls != 3) continue;
                            if (fc == 2 && ui.cls != 2) continue;
                            if (fc == 3 && (ui.cls == 2 || ui.cls == 3)) continue;
                            if (q.isEmpty() || ui.name.toLowerCase(Locale.US).contains(q) || String.valueOf(ui.id).contains(q))
                                filUnits.add(ui);
                        }
                    }
                    tvSummary.setText("Unit.csv 强制雇佣 [" + fcNames[fc] + "]: " + filUnits.size() + " 项 · 当前强雇随行 " + forcedIds.size() + " 位");
                } else {
                    filNearby.clear();
                    int tamedCnt = 0;
                    for (NearbyEntityInfo ne : nearbyList) {
                        if (!ne.tamed) continue;
                        tamedCnt++;
                        if (q.isEmpty() || ne.name.toLowerCase(Locale.US).contains(q) || String.valueOf(ne.id).contains(q))
                            filNearby.add(ne);
                    }
                    for (NearbyEntityInfo ne : nearbyList) {
                        if (ne.tamed) continue;
                        if (q.isEmpty() || ne.name.toLowerCase(Locale.US).contains(q) || String.valueOf(ne.id).contains(q))
                            filNearby.add(ne);
                    }
                    String rngTxt = tameRangeM[0] >= 999 ? "全图" : (tameRangeM[0] + "m");
                    tvSummary.setText("周围 " + rngTxt + " 存活单位: " + filNearby.size() + " 位 · 已收编 " + tamedCnt + " 位 (列表保留可撤回)");
                }
                adapter.notifyDataSetChanged();
            }
        };
        applyFilterRef[0] = applyFilter;

        hireStateRefreshHook = new Runnable() {
            @Override public void run() {
                loadHireCatalog(ctx, false);
                try {
                    String raw = readFileText(new File(ctx.getFilesDir(), "dyn_hire_state.json"));
                    if (raw.startsWith("{")) {
                        JSONObject o = new JSONObject(raw);
                        int srvRange = o.optInt("tameRange", tameRangeM[0]);
                        if (!tameTracking[0] && srvRange >= 5 && srvRange <= 999) {
                            tameRangeM[0] = srvRange;
                            syncTameDistUi.run();
                        }

                        ownedHires.clear();
                        JSONArray ha = o.optJSONArray("hires");
                        if (ha != null) for (int i = 0; i < ha.length(); i++) ownedHires.add(ha.optInt(i));

                        ownedWorkers.clear();
                        JSONArray wa = o.optJSONArray("workers");
                        if (wa != null) for (int i = 0; i < wa.length(); i++) ownedWorkers.add(wa.optInt(i));

                        activeTeamates.clear();
                        JSONArray ta = o.optJSONArray("teamates");
                        if (ta != null) for (int i = 0; i < ta.length(); i++) activeTeamates.add(ta.optInt(i));
                        JSONArray ca = o.optJSONArray("captures");
                        if (ca != null) for (int i = 0; i < ca.length(); i++) activeTeamates.add(ca.optInt(i));

                        forcedIds.clear();
                        JSONArray fa = o.optJSONArray("forced");
                        if (fa != null) for (int i = 0; i < fa.length(); i++) forcedIds.add(fa.optInt(i));

                        nearbyList.clear();
                        JSONArray na = o.optJSONArray("nearby");
                        if (na != null) {
                            for (int i = 0; i < na.length(); i++) {
                                JSONObject no = na.optJSONObject(i);
                                if (no == null) continue;
                                NearbyEntityInfo ne = new NearbyEntityInfo();
                                ne.uid = no.optString("uid", "0");
                                ne.id = no.optInt("id");
                                ne.name = no.optString("name", "#" + ne.id);
                                ne.camp = no.optInt("camp");
                                ne.hp = no.optInt("hp");
                                ne.maxHp = no.optInt("maxHp");
                                ne.dist = (float) no.optDouble("dist", 0.0);
                                ne.tamed = no.optInt("tamed", 0) != 0;
                                nearbyList.add(ne);
                            }
                        }
                    }
                } catch (Throwable ignored) {}
                applyFilter.run();
            }
        };

        for (int i = 0; i < hTabBtns.size(); i++) {
            final int ti = i;
            hTabBtns.get(i).setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) {
                    curSubTab[0] = ti;
                    btnClearForce.setVisibility(ti == 2 ? View.VISIBLE : View.GONE);
                    rForceCat.setVisibility(ti == 2 ? View.VISIBLE : View.GONE);
                    rTameDist.setVisibility(ti == 3 ? View.VISIBLE : View.GONE);
                    for (int k = 0; k < hTabBtns.size(); k++) {
                        hTabBtns.get(k).setBackground(makeRoundBox(k == ti ? curTheme.btnBlue : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                    }
                    if (ti == 3) {
                        Menu.NativeQuery(9, 0, 0, 0L, "");
                        mainH.postDelayed(new Runnable() {
                            @Override public void run() {
                                if (hireStateRefreshHook != null) hireStateRefreshHook.run();
                            }
                        }, 180);
                    }
                    applyFilter.run();
                }
            });
        }

        btnRefH.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                Menu.NativeQuery(9, 0, 0, 0L, "");
                mainH.postDelayed(new Runnable() {
                    @Override public void run() {
                        if (hireStateRefreshHook != null) hireStateRefreshHook.run();
                    }
                }, 180);
            }
        });

        btnClearForce.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                Menu.NativeQuery(19, 0, 7, 0L, "0");
                Toast.makeText(ctx, "已清空所有强制雇佣角色", Toast.LENGTH_SHORT).show();
                mainH.postDelayed(new Runnable() {
                    @Override public void run() {
                        if (hireStateRefreshHook != null) hireStateRefreshHook.run();
                    }
                }, 200);
            }
        });

        etSearch.addTextChangedListener(new TextWatcher() {
            @Override public void beforeTextChanged(CharSequence s, int a, int b, int c) {}
            @Override public void onTextChanged(CharSequence s, int a, int b, int c) {}
            @Override public void afterTextChanged(Editable s) { applyFilter.run(); }
        });

        themeRefreshHooks.add(new Runnable() {
            @Override public void run() {
                styleEditBox(etSearch);
                tvSummary.setTextColor(curTheme.textSec);
                tvTameRange.setTextColor(curTheme.accent);
                btnRefH.setBackground(makeRoundBox(curTheme.btnGreen, 0x33FFFFFF, 1, dp(7)));
                btnClearForce.setBackground(makeRoundBox(curTheme.btnRed, 0x33FFFFFF, 1, dp(7)));
                for (int k = 0; k < hTabBtns.size(); k++) {
                    hTabBtns.get(k).setBackground(makeRoundBox(k == curSubTab[0] ? curTheme.btnBlue : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                }
                for (int k = 0; k < fcBtns.size(); k++) {
                    fcBtns.get(k).setBackground(makeRoundBox(k == curForceCat[0] ? curTheme.btnBlue : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                }
                syncTameDistUi.run();
                adapter.notifyDataSetChanged();
            }
        });

        applyFilter.run();
        return wrap;
    }

    // =========================================================================
    //  TAB 7: 其他功能 (战斗辅助: 自瞄 / ESP绘制 / 去震动 + 原有开关 + 机关枪卡片)
    // =========================================================================
    private LinearLayout makeSwitchRow(String title, String sub, boolean initialVal, final Switch[] outSw, final CompoundButton.OnCheckedChangeListener listener) {
        LinearLayout row = new LinearLayout(ctx);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setGravity(Gravity.CENTER_VERTICAL);
        row.setPadding(dp(12), dp(8), dp(12), dp(8));
        row.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(8)));
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        lp.setMargins(0, 0, 0, dp(5));
        row.setLayoutParams(lp);

        LinearLayout textCol = new LinearLayout(ctx);
        textCol.setOrientation(LinearLayout.VERTICAL);
        textCol.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

        TextView tvTitle = new TextView(ctx);
        tvTitle.setText(title);
        tvTitle.setTextSize(12f);
        tvTitle.setTextColor(curTheme.textPri);
        tvTitle.setTypeface(null, Typeface.BOLD);
        textCol.addView(tvTitle);

        if (sub != null && !sub.isEmpty()) {
            TextView tvSub = new TextView(ctx);
            tvSub.setText(sub);
            tvSub.setTextSize(10.5f);
            tvSub.setTextColor(curTheme.textSec);
            tvSub.setPadding(0, dp(2), 0, 0);
            textCol.addView(tvSub);
            otherFeatureSubs.add(tvSub);
        }
        row.addView(textCol);
        otherFeatureTitles.add(tvTitle);
        otherFeatureRows.add(row);

        final Switch sw = new Switch(ctx);
        sw.setChecked(initialVal);
        sw.setOnCheckedChangeListener(new CompoundButton.OnCheckedChangeListener() {
            @Override public void onCheckedChanged(CompoundButton b, boolean isOn) {
                if (suppressToggleCallbacks || listener == null) return;
                listener.onCheckedChanged(b, isOn);
            }
        });
        row.addView(sw);
        if (outSw != null && outSw.length > 0) outSw[0] = sw;

        row.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { sw.toggle(); }
        });
        return row;
    }

    // =========================================================================
    //  动态地图 & 任务配表加载 + 全局搜索注册器
    // =========================================================================
    private EditText etMapSearchBox, etTaskSearchBox;

    private static List<MapEntry> loadMapsCatalog(Context c, boolean forceReload) {
        if (sMapsCache != null && !forceReload) return sMapsCache;
        List<MapEntry> out = new ArrayList<MapEntry>(380);
        try {
            File f = new File(c.getFilesDir(), "dyn_maps.json");
            String raw = readFileText(f);
            if (raw != null && raw.startsWith("[")) {
                JSONArray arr = new JSONArray(raw);
                Map<Integer, MapEntry> byId = new HashMap<Integer, MapEntry>();
                for (int i = 0; i < arr.length(); i++) {
                    JSONObject o = arr.optJSONObject(i);
                    if (o == null) continue;
                    MapEntry m = new MapEntry();
                    m.id = o.optInt("id");
                    m.name = o.optString("name", "").trim();
                    if (m.name.isEmpty()) m.name = "场景 #" + m.id;
                    m.desc = o.optString("desc", "").trim();
                    m.us = o.optString("us", "").trim();
                    m.belong = o.optInt("belong", 0);
                    m.root = o.optInt("root", m.id);
                    m.spawn = o.optInt("spawn", 0);
                    m.cat = o.optInt("cat", 1);
                    m.ui = o.optInt("ui", 0);
                    m.valid = o.optInt("valid", 1) != 0;
                    m.depth = 0;
                    m.childCount = 0;
                    m.expanded = false;
                    out.add(m);
                    byId.put(m.id, m);
                }
                for (MapEntry m : out) {
                    int d = 0;
                    int cur = m.belong;
                    int guard = 0;
                    while (cur > 0 && cur != m.id && byId.containsKey(cur) && guard++ < 8) {
                        d++;
                        MapEntry p = byId.get(cur);
                        if (p == null) break;
                        if (d == 1) p.childCount++;
                        int nxt = p.belong;
                        if (nxt == cur || nxt == 0) break;
                        cur = nxt;
                    }
                    m.depth = d;
                }
                sMapsCache = out;
                return out;
            }
        } catch (Throwable ignored) {}
        return sMapsCache != null ? sMapsCache : out;
    }

    private static List<TaskEntry> loadTasksCatalog(Context c, boolean forceReload) {
        if (sTasksCache != null && !forceReload) return sTasksCache;
        List<TaskEntry> out = new ArrayList<TaskEntry>(300);
        try {
            File f = new File(c.getFilesDir(), "dyn_tasks.json");
            String raw = readFileText(f);
            JSONArray arr = null;
            if (raw != null) {
                if (raw.startsWith("{")) {
                    JSONObject obj = new JSONObject(raw);
                    arr = obj.optJSONArray("events");
                } else if (raw.startsWith("[")) {
                    arr = new JSONArray(raw);
                }
            }
            if (arr != null) {
                for (int i = 0; i < arr.length(); i++) {
                    JSONObject o = arr.optJSONObject(i);
                    if (o == null) continue;
                    TaskEntry t = new TaskEntry();
                    t.id = o.optInt("id");
                    t.cf = o.optInt("cf", t.id);
                    t.name = o.optString("name", "").trim();
                    if (t.name.isEmpty()) t.name = "任务 #" + t.id;
                    t.desc = o.optString("desc", "").trim();
                    t.reward = o.optString("reward", "").trim();
                    t.scene = o.optInt("scene", 0);
                    t.sceneName = o.optString("sceneName", "").trim();
                    t.root = o.optInt("root", t.scene);
                    t.rootName = o.optString("rootName", "").trim();
                    t.scenes = o.optString("scenes", "").trim();
                    t.cat = o.optInt("cat", 1);
                    t.parent = o.optInt("parent", 0);
                    t.depth = o.optInt("depth", 0);
                    t.unlockTxt = o.optString("unlockTxt", "").trim();
                    t.finishTxt = o.optString("finishTxt", "").trim();
                    t.showTxt = o.optString("showTxt", "").trim();
                    out.add(t);
                }
                Collections.sort(out, new Comparator<TaskEntry>() {
                    @Override public int compare(TaskEntry a, TaskEntry b) {
                        if (a.cat != b.cat) return a.cat - b.cat;
                        if (a.root != b.root) return a.root - b.root;
                        if (a.depth != b.depth) return a.depth - b.depth;
                        return a.id - b.id;
                    }
                });
                sTasksCache = out;
                return out;
            }
        } catch (Throwable ignored) {}
        return sTasksCache != null ? sTasksCache : out;
    }

    private static List<SpawnUnitInfo> loadUnitsCatalog(Context c, boolean forceReload) {
        if (sSpawnUnitsCache != null && !forceReload) return sSpawnUnitsCache;
        List<SpawnUnitInfo> out = new ArrayList<SpawnUnitInfo>(720);
        try {
            File f = new File(c.getFilesDir(), "dyn_units.json");
            String raw = readFileText(f);
            if (raw != null && raw.startsWith("[")) {
                JSONArray arr = new JSONArray(raw);
                for (int i = 0; i < arr.length(); i++) {
                    JSONObject o = arr.optJSONObject(i);
                    if (o == null) continue;
                    SpawnUnitInfo u = new SpawnUnitInfo();
                    u.id = o.optInt("id");
                    u.cls = o.optInt("cls", 1);
                    u.wtype = o.optInt("wtype", 0);
                    u.fac = o.optInt("fac", 1);
                    u.camp = o.optInt("camp", -9999);
                    u.exp = o.optInt("exp", 0);
                    u.hp = o.optInt("hp", 0);
                    u.hasPrefab = o.optInt("hasPrefab", 0);
                    u.name = o.optString("name", "").trim();
                    if (u.name.isEmpty()) u.name = "Entity #" + u.id;
                    u.sub = o.optString("sub", "").trim();
                    if (u.sub.isEmpty()) u.sub = "常规单位";
                    u.desc = o.optString("desc", "").trim();
                    u.icon = o.optString("icon", "").trim();
                    out.add(u);
                }
                Collections.sort(out, new Comparator<SpawnUnitInfo>() {
                    @Override public int compare(SpawnUnitInfo a, SpawnUnitInfo b) {
                        if (a.hasPrefab != b.hasPrefab) return b.hasPrefab - a.hasPrefab;
                        return a.id - b.id;
                    }
                });
                sSpawnUnitsCache = out;
                return out;
            }
        } catch (Throwable ignored) {}
        return sSpawnUnitsCache != null ? sSpawnUnitsCache : out;
    }

    private void registerFeature(String category, String title, String desc, String keywords, int tabIdx, View targetView, ScrollView scrollView, Runnable subAction) {
        FeatureSearchEntry e = new FeatureSearchEntry();
        e.category = category != null ? category : "";
        e.title = title != null ? title : "";
        e.desc = desc != null ? desc : "";
        e.keywords = keywords != null ? keywords : "";
        e.tabIdx = tabIdx;
        e.targetView = targetView;
        e.scrollView = scrollView;
        e.subAction = subAction;
        searchRegistry.add(e);
    }

    // =========================================================================
    //  TAB 6: 战斗辅助 (自瞄 / ESP透视绘制 / 机关枪模式 / 震动 / 子弹 / 耐久)
    // =========================================================================
    private FrameLayout buildCombatPage() {
        FrameLayout wrap = new FrameLayout(ctx);
        svCombat = new ScrollView(ctx);
        svCombat.setVerticalScrollBarEnabled(true);
        LinearLayout col = new LinearLayout(ctx);
        col.setOrientation(LinearLayout.VERTICAL);
        svCombat.addView(col);
        wrap.addView(svCombat, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        tvCombatStatus = new TextView(ctx);
        tvCombatStatus.setText("战斗辅助: 自瞄、ESP透视、机关枪连射、后坐力与弹药耐久集中控制");
        tvCombatStatus.setTextColor(curTheme.accent);
        tvCombatStatus.setTextSize(11);
        tvCombatStatus.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(7)));
        tvCombatStatus.setPadding(dp(10), dp(5), dp(10), dp(5));
        tvCombatStatus.setSingleLine(true);
        tvCombatStatus.setEllipsize(TextUtils.TruncateAt.END);
        LinearLayout.LayoutParams stLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        stLp.setMargins(0, 0, 0, dp(6));
        col.addView(tvCombatStatus, stLp);

        // 1. Aimbot Card
        cardAimbot = new LinearLayout(ctx);
        cardAimbot.setOrientation(LinearLayout.VERTICAL);
        cardAimbot.setPadding(dp(12), dp(10), dp(12), dp(10));
        cardAimbot.setBackground(makeRoundBox(flagAimbot ? curTheme.cardOnBg : curTheme.cardBg, flagAimbot ? curTheme.cardOnEdge : curTheme.cardEdge, dp(1), dp(10)));
        LinearLayout.LayoutParams aimLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        aimLp.setMargins(0, 0, 0, dp(6));

        LinearLayout aimTop = new LinearLayout(ctx);
        aimTop.setOrientation(LinearLayout.HORIZONTAL);
        aimTop.setGravity(Gravity.CENTER_VERTICAL);

        final TextView tvAimT = new TextView(ctx);
        tvAimT.setText("智能自瞄 (锁定最近敌对 · 蓄力散布自动0.5°)");
        tvAimT.setTextSize(12.5f);
        tvAimT.setTextColor(curTheme.textPri);
        tvAimT.setTypeface(null, Typeface.BOLD);
        tvAimT.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        aimTop.addView(tvAimT);

        tvAimState = new TextView(ctx);
        tvAimState.setTextSize(10.5f);
        tvAimState.setTypeface(null, Typeface.BOLD);
        tvAimState.setPadding(0, 0, dp(6), 0);
        tvAimState.setText(flagAimbot ? "● 自瞄锁定中" : "○ 未开启");
        tvAimState.setTextColor(flagAimbot ? curTheme.emerald : curTheme.textSec);
        aimTop.addView(tvAimState);

        swAimbot = new Switch(ctx);
        swAimbot.setChecked(flagAimbot);
        swAimbot.setOnCheckedChangeListener(new CompoundButton.OnCheckedChangeListener() {
            @Override public void onCheckedChanged(CompoundButton b, boolean isOn) {
                if (suppressToggleCallbacks) return;
                flagAimbot = isOn;
                Preferences.changeFeatureBool("自瞄最近敌对", 120, flagAimbot);
                Menu.NativeQuery(10, 12, flagAimbot ? 1 : 0, 0L, "");
                cardAimbot.setBackground(makeRoundBox(flagAimbot ? curTheme.cardOnBg : curTheme.cardBg, flagAimbot ? curTheme.cardOnEdge : curTheme.cardEdge, dp(1), dp(10)));
                tvAimState.setText(flagAimbot ? "● 自瞄锁定中" : "○ 未开启");
                tvAimState.setTextColor(flagAimbot ? curTheme.emerald : curTheme.textSec);
            }
        });
        aimTop.addView(swAimbot);
        cardAimbot.addView(aimTop);

        LinearLayout aimSubRow = new LinearLayout(ctx);
        aimSubRow.setOrientation(LinearLayout.HORIZONTAL);
        aimSubRow.setGravity(Gravity.CENTER_VERTICAL);
        final TextView tvWallLbl = new TextView(ctx);
        tvWallLbl.setText("无视墙壁障碍物瞄准 (否则仅锁定视线无阻挡目标)");
        tvWallLbl.setTextSize(10.5f);
        tvWallLbl.setTextColor(curTheme.textSec);
        tvWallLbl.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        aimSubRow.addView(tvWallLbl);

        swAimIgnoreWall = new Switch(ctx);
        swAimIgnoreWall.setChecked(flagAimIgnoreWall);
        swAimIgnoreWall.setOnCheckedChangeListener(new CompoundButton.OnCheckedChangeListener() {
            @Override public void onCheckedChanged(CompoundButton b, boolean isOn) {
                if (suppressToggleCallbacks) return;
                flagAimIgnoreWall = isOn;
                Preferences.changeFeatureBool("自瞄无视掩体", 121, flagAimIgnoreWall);
                Menu.NativeQuery(10, 15, flagAimIgnoreWall ? 1 : 0, 0L, "");
            }
        });
        aimSubRow.addView(swAimIgnoreWall);
        cardAimbot.addView(aimSubRow);

        LinearLayout aimRangeRow = new LinearLayout(ctx);
        aimRangeRow.setOrientation(LinearLayout.HORIZONTAL);
        aimRangeRow.setGravity(Gravity.CENTER_VERTICAL);
        aimRangeRow.setPadding(0, dp(3), 0, 0);

        tvAimRangeVal = new TextView(ctx);
        tvAimRangeVal.setText(aimMaxRangeX10 <= 0 ? "锁定距离: 不限" : String.format(Locale.US, "锁定距离: %.0fm", aimMaxRangeX10 / 10.0f));
        tvAimRangeVal.setTextColor(curTheme.accent);
        tvAimRangeVal.setTextSize(11f);
        tvAimRangeVal.setTypeface(null, Typeface.BOLD);
        aimRangeRow.addView(tvAimRangeVal, new LinearLayout.LayoutParams(dp(110), ViewGroup.LayoutParams.WRAP_CONTENT));

        final SeekBar sbAim = new SeekBar(ctx);
        sbAim.setMax(50);
        sbAim.setProgress(aimMaxRangeX10 / 10);
        sbAim.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        sbAim.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener() {
            @Override public void onProgressChanged(SeekBar s, int progress, boolean fromUser) {
                aimMaxRangeX10 = progress * 10;
                tvAimRangeVal.setText(aimMaxRangeX10 <= 0 ? "锁定距离: 不限" : String.format(Locale.US, "锁定距离: %.0fm", aimMaxRangeX10 / 10.0f));
                if (fromUser) {
                    modPrefs.edit().putInt("aim_max_range_x10", aimMaxRangeX10).apply();
                    Menu.NativeQuery(17, aimMaxRangeX10, espLineMode, 0L, "");
                }
            }
            @Override public void onStartTrackingTouch(SeekBar s) {}
            @Override public void onStopTrackingTouch(SeekBar s) {}
        });
        aimRangeRow.addView(sbAim);
        cardAimbot.addView(aimRangeRow);
        col.addView(cardAimbot, aimLp);
        registerFeature("战斗辅助", "智能自瞄", "锁定最近敌对 · 支持无视掩体与距离限制", "自瞄 瞄准 锁头 穿墙 无视掩体 aimbot", 6, cardAimbot, svCombat, null);

        // 2. ESP Overlay Card
        cardEsp = new LinearLayout(ctx);
        cardEsp.setOrientation(LinearLayout.VERTICAL);
        cardEsp.setPadding(dp(12), dp(10), dp(12), dp(10));
        cardEsp.setBackground(makeRoundBox(flagEsp ? curTheme.cardOnBg : curTheme.cardBg, flagEsp ? curTheme.cardOnEdge : curTheme.cardEdge, dp(1), dp(10)));
        LinearLayout.LayoutParams espLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        espLp.setMargins(0, 0, 0, dp(6));

        LinearLayout espTop = new LinearLayout(ctx);
        espTop.setOrientation(LinearLayout.HORIZONTAL);
        espTop.setGravity(Gravity.CENTER_VERTICAL);

        final TextView tvEspT = new TextView(ctx);
        tvEspT.setText("全图敌对绘制 ESP (方框 + 射线 + 血量距离)");
        tvEspT.setTextSize(12.5f);
        tvEspT.setTextColor(curTheme.textPri);
        tvEspT.setTypeface(null, Typeface.BOLD);
        tvEspT.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        espTop.addView(tvEspT);

        tvEspState = new TextView(ctx);
        tvEspState.setTextSize(10.5f);
        tvEspState.setTypeface(null, Typeface.BOLD);
        tvEspState.setPadding(0, 0, dp(6), 0);
        tvEspState.setText(flagEsp ? "● 绘制透视中" : "○ 未开启");
        tvEspState.setTextColor(flagEsp ? curTheme.emerald : curTheme.textSec);
        espTop.addView(tvEspState);

        swEsp = new Switch(ctx);
        swEsp.setChecked(flagEsp);
        swEsp.setOnCheckedChangeListener(new CompoundButton.OnCheckedChangeListener() {
            @Override public void onCheckedChanged(CompoundButton b, boolean isOn) {
                if (suppressToggleCallbacks) return;
                flagEsp = isOn;
                Preferences.changeFeatureBool("绘制透视总开关", 122, flagEsp);
                Menu.NativeQuery(10, 13, flagEsp ? 1 : 0, 0L, "");
                cardEsp.setBackground(makeRoundBox(flagEsp ? curTheme.cardOnBg : curTheme.cardBg, flagEsp ? curTheme.cardOnEdge : curTheme.cardEdge, dp(1), dp(10)));
                tvEspState.setText(flagEsp ? "● 绘制透视中" : "○ 未开启");
                tvEspState.setTextColor(flagEsp ? curTheme.emerald : curTheme.textSec);
                syncEspOverlayToMenu(Menu.getInstance());
            }
        });
        espTop.addView(swEsp);
        cardEsp.addView(espTop);

        LinearLayout espSub1 = new LinearLayout(ctx);
        espSub1.setOrientation(LinearLayout.HORIZONTAL);
        espSub1.setGravity(Gravity.CENTER_VERTICAL);
        espSub1.setPadding(0, dp(4), 0, 0);

        final TextView tvB = new TextView(ctx); tvB.setText("方框 "); tvB.setTextSize(10.5f); tvB.setTextColor(curTheme.textSec);
        swEspBox = new Switch(ctx); swEspBox.setChecked(flagEspBox);
        swEspBox.setOnCheckedChangeListener(new CompoundButton.OnCheckedChangeListener() {
            @Override public void onCheckedChanged(CompoundButton b, boolean isOn) {
                if (suppressToggleCallbacks) return;
                flagEspBox = isOn;
                Preferences.changeFeatureBool("绘制方框", 123, flagEspBox);
                Menu.NativeQuery(10, 16, flagEspBox ? 1 : 0, 0L, "");
                syncEspOverlayToMenu(Menu.getInstance());
            }
        });
        final TextView tvL = new TextView(ctx); tvL.setText("  射线 "); tvL.setTextSize(10.5f); tvL.setTextColor(curTheme.textSec);
        swEspLine = new Switch(ctx); swEspLine.setChecked(flagEspLine);
        swEspLine.setOnCheckedChangeListener(new CompoundButton.OnCheckedChangeListener() {
            @Override public void onCheckedChanged(CompoundButton b, boolean isOn) {
                if (suppressToggleCallbacks) return;
                flagEspLine = isOn;
                Preferences.changeFeatureBool("绘制射线", 124, flagEspLine);
                Menu.NativeQuery(10, 17, flagEspLine ? 1 : 0, 0L, "");
                syncEspOverlayToMenu(Menu.getInstance());
            }
        });
        final TextView tvI = new TextView(ctx); tvI.setText("  名字血量 "); tvI.setTextSize(10.5f); tvI.setTextColor(curTheme.textSec);
        swEspInfo = new Switch(ctx); swEspInfo.setChecked(flagEspInfo);
        swEspInfo.setOnCheckedChangeListener(new CompoundButton.OnCheckedChangeListener() {
            @Override public void onCheckedChanged(CompoundButton b, boolean isOn) {
                if (suppressToggleCallbacks) return;
                flagEspInfo = isOn;
                Preferences.changeFeatureBool("绘制信息血条", 125, flagEspInfo);
                Menu.NativeQuery(10, 18, flagEspInfo ? 1 : 0, 0L, "");
                syncEspOverlayToMenu(Menu.getInstance());
            }
        });
        espSub1.addView(tvB); espSub1.addView(swEspBox);
        espSub1.addView(tvL); espSub1.addView(swEspLine);
        espSub1.addView(tvI); espSub1.addView(swEspInfo);
        cardEsp.addView(espSub1);

        LinearLayout espSub2 = new LinearLayout(ctx);
        espSub2.setOrientation(LinearLayout.HORIZONTAL);
        espSub2.setGravity(Gravity.CENTER_VERTICAL);
        espSub2.setPadding(0, dp(4), 0, 0);

        final TextView tvN = new TextView(ctx); tvN.setText("含中立生物 "); tvN.setTextSize(10.5f); tvN.setTextColor(curTheme.textSec);
        swEspNeutral = new Switch(ctx); swEspNeutral.setChecked(flagEspNeutral);
        swEspNeutral.setOnCheckedChangeListener(new CompoundButton.OnCheckedChangeListener() {
            @Override public void onCheckedChanged(CompoundButton b, boolean isOn) {
                if (suppressToggleCallbacks) return;
                flagEspNeutral = isOn;
                Preferences.changeFeatureBool("绘制中立生物", 126, flagEspNeutral);
                Menu.NativeQuery(10, 19, flagEspNeutral ? 1 : 0, 0L, "");
            }
        });
        espSub2.addView(tvN); espSub2.addView(swEspNeutral);

        final TextView tvOrg = new TextView(ctx); tvOrg.setText("  射线起点:"); tvOrg.setTextSize(10.5f); tvOrg.setTextColor(curTheme.textSec);
        espSub2.addView(tvOrg);
        final String[] modeNames = new String[] { "屏幕顶部", "屏幕中心", "玩家角色" };
        final List<Button> modeBtns = new ArrayList<Button>();
        for (int m = 0; m < 3; m++) {
            final int mm = m;
            Button mb = makePillBtn(modeNames[m], espLineMode == m ? curTheme.btnBlue : curTheme.btnSlate, 24);
            mb.setTextSize(9.5f);
            mb.setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) {
                    espLineMode = mm;
                    modPrefs.edit().putInt("esp_line_mode", espLineMode).apply();
                    Menu.NativeQuery(17, aimMaxRangeX10, espLineMode, 0L, "");
                    syncEspOverlayToMenu(Menu.getInstance());
                    for (int k = 0; k < modeBtns.size(); k++) {
                        modeBtns.get(k).setBackground(makeRoundBox(k == espLineMode ? curTheme.btnBlue : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                    }
                }
            });
            modeBtns.add(mb);
            espSub2.addView(mb);
        }
        cardEsp.addView(espSub2);
        col.addView(cardEsp, espLp);
        registerFeature("战斗辅助", "全图透视绘制 (ESP)", "方框、射线、名字血条与中立生物显示", "绘制 透视 esp 方框 射线 血条", 6, cardEsp, svCombat, null);

        // 3. Machine Gun mode Card
        cardMG = new LinearLayout(ctx);
        cardMG.setOrientation(LinearLayout.VERTICAL);
        cardMG.setPadding(dp(12), dp(10), dp(12), dp(10));
        cardMG.setBackground(makeRoundBox(flagMG ? curTheme.cardOnBg : curTheme.cardBg, flagMG ? curTheme.cardOnEdge : curTheme.cardEdge, dp(1), dp(10)));
        LinearLayout.LayoutParams mgLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        mgLp.setMargins(0, 0, 0, dp(6));

        LinearLayout mgTop = new LinearLayout(ctx);
        mgTop.setOrientation(LinearLayout.HORIZONTAL);
        mgTop.setGravity(Gravity.CENTER_VERTICAL);

        final TextView tvMgT = new TextView(ctx);
        tvMgT.setText("机关枪模式 (仅远程枪械/弓弩有效，近战刀棍不受影响)");
        tvMgT.setTextSize(12.5f);
        tvMgT.setTextColor(curTheme.textPri);
        tvMgT.setTypeface(null, Typeface.BOLD);
        tvMgT.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        mgTop.addView(tvMgT);

        tvMGState = new TextView(ctx);
        tvMGState.setTextSize(10.5f);
        tvMGState.setTypeface(null, Typeface.BOLD);
        tvMGState.setPadding(0, 0, dp(6), 0);
        tvMGState.setText(flagMG ? "● 机关枪生效中" : "○ 未开启");
        tvMGState.setTextColor(flagMG ? curTheme.emerald : curTheme.textSec);
        mgTop.addView(tvMGState);

        swMG = new Switch(ctx);
        swMG.setChecked(flagMG);
        swMG.setOnCheckedChangeListener(new CompoundButton.OnCheckedChangeListener() {
            @Override public void onCheckedChanged(CompoundButton b, boolean isOn) {
                if (suppressToggleCallbacks) return;
                flagMG = isOn;
                Preferences.changeFeatureBool("机关枪模式", 74, flagMG);
                Menu.NativeQuery(10, 7, flagMG ? 1 : 0, 0L, "");
                cardMG.setBackground(makeRoundBox(flagMG ? curTheme.cardOnBg : curTheme.cardBg, flagMG ? curTheme.cardOnEdge : curTheme.cardEdge, dp(1), dp(10)));
                tvMGState.setText(flagMG ? "● 机关枪生效中" : "○ 未开启");
                tvMGState.setTextColor(flagMG ? curTheme.emerald : curTheme.textSec);
            }
        });
        mgTop.addView(swMG);
        cardMG.addView(mgTop);

        final TextView tvMgSub = new TextView(ctx);
        tvMgSub.setText("开启后滑动右攻击轮盘不松手即可持续连射，松手即停");
        tvMgSub.setTextSize(10.5f);
        tvMgSub.setTextColor(curTheme.textSec);
        tvMgSub.setPadding(0, dp(2), 0, dp(4));
        cardMG.addView(tvMgSub);

        LinearLayout mgRateRow = new LinearLayout(ctx);
        mgRateRow.setOrientation(LinearLayout.HORIZONTAL);
        mgRateRow.setGravity(Gravity.CENTER_VERTICAL);
        mgRateRow.setPadding(0, dp(3), 0, 0);

        tvMgRateVal = new TextView(ctx);
        tvMgRateVal.setText(String.format(Locale.US, "连发速度: %.1f 发/秒", mgRateX10 / 10.0f));
        tvMgRateVal.setTextColor(curTheme.accent);
        tvMgRateVal.setTextSize(11.5f);
        tvMgRateVal.setTypeface(null, Typeface.BOLD);
        mgRateRow.addView(tvMgRateVal, new LinearLayout.LayoutParams(dp(120), ViewGroup.LayoutParams.WRAP_CONTENT));

        final SeekBar sbMg = new SeekBar(ctx);
        sbMg.setMax(590);
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
        col.addView(cardMG, mgLp);
        registerFeature("战斗辅助", "机关枪模式", "枪械/弓弩持续连射与自定义射速 (1~60发/秒)", "机关枪 连射 射速 弓弩 mg", 6, cardMG, svCombat, null);

        // 4. NoShake
        final Switch[] swHolderNoShake = new Switch[1];
        LinearLayout rNoShake = makeSwitchRow("关闭开枪震动与受击震屏", "屏蔽开火重震 (Heavy) 与受击震屏 (Normal)，大幅提升连射视野稳定性", flagNoShake, swHolderNoShake, new CompoundButton.OnCheckedChangeListener() {
            @Override public void onCheckedChanged(CompoundButton b, boolean isOn) {
                flagNoShake = isOn;
                Preferences.changeFeatureBool("关闭开枪受击震动", 127, flagNoShake);
                Menu.NativeQuery(10, 14, flagNoShake ? 1 : 0, 0L, "");
                Toast.makeText(ctx, flagNoShake ? "已屏蔽开火重震与受击震屏" : "已恢复原版镜头震动", Toast.LENGTH_SHORT).show();
            }
        });
        swNoShake = swHolderNoShake[0];
        col.addView(rNoShake);
        registerFeature("战斗辅助", "关闭开枪受击震动", "屏蔽开火后坐震动与受击震屏", "震动 后坐 镜头 抖动 noshake", 6, rNoShake, svCombat, null);

        // 5. Ammo
        final Switch[] swHolderAmmo = new Switch[1];
        LinearLayout rAmmo = makeSwitchRow("子弹不减反增", "远程枪械/弓弩开火消耗弹药反而 +1", flagAmmo, swHolderAmmo, new CompoundButton.OnCheckedChangeListener() {
            @Override public void onCheckedChanged(CompoundButton b, boolean isOn) {
                flagAmmo = isOn;
                Preferences.changeFeatureBool("子弹不减反增", 40, flagAmmo);
                Menu.NativeQuery(10, 1, flagAmmo ? 1 : 0, 0L, "");
            }
        });
        swAmmo = swHolderAmmo[0];
        col.addView(rAmmo);
        registerFeature("战斗辅助", "子弹不减反增", "远程开火弹药不减反增", "子弹 弹药 箭矢 无限 ammo", 6, rAmmo, svCombat, null);

        // 6. Durability + Fix button
        final Switch[] swHolderEndur = new Switch[1];
        LinearLayout rEndur = makeSwitchRow("锁定装备耐久", "全部武器、护甲防具零损耗，支持一键补满耐久", flagEndur, swHolderEndur, new CompoundButton.OnCheckedChangeListener() {
            @Override public void onCheckedChanged(CompoundButton b, boolean isOn) {
                flagEndur = isOn;
                Preferences.changeFeatureBool("锁定武器/装备耐久", 41, flagEndur);
                Menu.NativeQuery(10, 2, flagEndur ? 1 : 0, 0L, "");
            }
        });
        swEndur = swHolderEndur[0];
        final Button btnFixEndur = makePillBtn("补满耐久", curTheme.btnBlue, 26);
        btnFixEndur.setTextSize(10);
        btnFixEndur.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                Menu.NativeQuery(7, 0, 0, 0L, "");
                Toast.makeText(ctx, "已补满身上及背包装备耐久", Toast.LENGTH_SHORT).show();
            }
        });
        rEndur.addView(btnFixEndur, rEndur.getChildCount() - 1);
        col.addView(rEndur);
        registerFeature("战斗辅助", "锁定与补满装备耐久", "锁定武器防具零损耗并可一键补满耐久", "耐久 损坏 武器 防具 修复", 6, rEndur, svCombat, null);

        themeRefreshHooks.add(new Runnable() {
            @Override public void run() {
                btnFixEndur.setBackground(makeRoundBox(curTheme.btnBlue, 0x33FFFFFF, 1, dp(7)));
                tvMgRateVal.setTextColor(curTheme.accent);
                tvAimT.setTextColor(curTheme.textPri);
                tvWallLbl.setTextColor(curTheme.textSec);
                tvEspT.setTextColor(curTheme.textPri);
                tvB.setTextColor(curTheme.textSec);
                tvL.setTextColor(curTheme.textSec);
                tvI.setTextColor(curTheme.textSec);
                tvN.setTextColor(curTheme.textSec);
                tvOrg.setTextColor(curTheme.textSec);
                tvMgT.setTextColor(curTheme.textPri);
                tvMgSub.setTextColor(curTheme.textSec);
                for (Button rb : rateBtns) rb.setBackground(makeRoundBox(curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                for (int k = 0; k < modeBtns.size(); k++) {
                    modeBtns.get(k).setBackground(makeRoundBox(k == espLineMode ? curTheme.btnBlue : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                }
                if (cardAimbot != null) cardAimbot.setBackground(makeRoundBox(flagAimbot ? curTheme.cardOnBg : curTheme.cardBg, flagAimbot ? curTheme.cardOnEdge : curTheme.cardEdge, dp(1), dp(10)));
                if (tvAimRangeVal != null) tvAimRangeVal.setTextColor(curTheme.accent);
                if (cardEsp != null) cardEsp.setBackground(makeRoundBox(flagEsp ? curTheme.cardOnBg : curTheme.cardBg, flagEsp ? curTheme.cardOnEdge : curTheme.cardEdge, dp(1), dp(10)));
                if (cardMG != null) cardMG.setBackground(makeRoundBox(flagMG ? curTheme.cardOnBg : curTheme.cardBg, flagMG ? curTheme.cardOnEdge : curTheme.cardEdge, dp(1), dp(10)));
            }
        });

        return wrap;
    }

    // =========================================================================
    //  TAB 8: 生成实体 (官方分类与细分 + 搜索 + 原生阵营精确保留)
    // =========================================================================
    private interface UnitPickCallback {
        void onPicked(SpawnUnitInfo unit);
    }

    private FrameLayout unitPickModalOverlay;
    private LinearLayout unitPickModalCard;
    private TextView unitPickTitle;
    private EditText etUnitPickSearch;
    private final List<SpawnUnitInfo> unitPickRows = new ArrayList<SpawnUnitInfo>();
    private BaseAdapter unitPickAdapter;
    private UnitPickCallback unitPickCb;

    private void showUnitPickerModal(String title, UnitPickCallback callback) {
        unitPickCb = callback;
        if (unitPickModalOverlay == null) {
            unitPickModalOverlay = new FrameLayout(ctx);
            unitPickModalOverlay.setBackgroundColor(0xB8000000);
            unitPickModalOverlay.setVisibility(View.GONE);
            unitPickModalOverlay.setClickable(true);
            unitPickModalOverlay.setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) { unitPickModalOverlay.setVisibility(View.GONE); }
            });

            unitPickModalCard = new LinearLayout(ctx);
            unitPickModalCard.setOrientation(LinearLayout.VERTICAL);
            unitPickModalCard.setPadding(dp(12), dp(10), dp(12), dp(10));
            unitPickModalCard.setBackground(makeRoundBox(curTheme.panelBg | 0xFF000000, curTheme.accent, dp(2), dp(12)));
            unitPickModalCard.setClickable(true);
            FrameLayout.LayoutParams cLp = new FrameLayout.LayoutParams(dp(440), dp(300), Gravity.CENTER);
            unitPickModalOverlay.addView(unitPickModalCard, cLp);

            LinearLayout hdr = new LinearLayout(ctx);
            hdr.setOrientation(LinearLayout.HORIZONTAL);
            hdr.setGravity(Gravity.CENTER_VERTICAL);
            unitPickTitle = new TextView(ctx);
            unitPickTitle.setTextColor(curTheme.textPri);
            unitPickTitle.setTextSize(13f);
            unitPickTitle.setTypeface(null, Typeface.BOLD);
            unitPickTitle.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
            hdr.addView(unitPickTitle);

            Button btnClosePick = makePillBtn("关闭", curTheme.btnRed, 26);
            btnClosePick.setTextSize(10f);
            btnClosePick.setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) { unitPickModalOverlay.setVisibility(View.GONE); }
            });
            hdr.addView(btnClosePick);
            unitPickModalCard.addView(hdr);

            etUnitPickSearch = makeDarkEdit("搜索任意实体名称 / ID / 描述 (全量无分类限制)...", false);
            etUnitPickSearch.setTextSize(11f);
            LinearLayout.LayoutParams eLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(30));
            eLp.setMargins(0, dp(6), 0, dp(6));
            unitPickModalCard.addView(etUnitPickSearch, eLp);

            final Runnable filterPick = new Runnable() {
                @Override public void run() {
                    String q = etUnitPickSearch.getText().toString().trim().toLowerCase(Locale.US);
                    unitPickRows.clear();
                    List<SpawnUnitInfo> all = loadUnitsCatalog(ctx, false);
                    for (SpawnUnitInfo u : all) {
                        if (!q.isEmpty()) {
                            String hay = (u.name + " " + u.id + " " + u.sub + " " + u.desc).toLowerCase(Locale.US);
                            if (!hay.contains(q)) continue;
                        }
                        unitPickRows.add(u);
                    }
                    if (unitPickAdapter != null) unitPickAdapter.notifyDataSetChanged();
                }
            };
            cricketPickHook = filterPick;

            etUnitPickSearch.addTextChangedListener(new TextWatcher() {
                @Override public void beforeTextChanged(CharSequence s, int a, int b, int c) {}
                @Override public void onTextChanged(CharSequence s, int a, int b, int c) { filterPick.run(); }
                @Override public void afterTextChanged(Editable s) {}
            });

            ListView lv = new ListView(ctx);
            lv.setDividerHeight(dp(4));
            lv.setLayoutParams(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
            unitPickAdapter = new BaseAdapter() {
                @Override public int getCount() { return unitPickRows.size(); }
                @Override public Object getItem(int pos) { return unitPickRows.get(pos); }
                @Override public long getItemId(int pos) { return unitPickRows.get(pos).id; }
                @Override public View getView(int pos, View cv, ViewGroup parent) {
                    final SpawnUnitInfo u = unitPickRows.get(pos);
                    LinearLayout row = new LinearLayout(ctx);
                    row.setOrientation(LinearLayout.HORIZONTAL);
                    row.setGravity(Gravity.CENTER_VERTICAL);
                    row.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(8)));
                    row.setPadding(dp(8), dp(6), dp(8), dp(6));

                    LinearLayout col = new LinearLayout(ctx);
                    col.setOrientation(LinearLayout.VERTICAL);
                    col.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
                    TextView t1 = new TextView(ctx);
                    t1.setText(u.name + "  #" + u.id);
                    t1.setTextColor(curTheme.textPri);
                    t1.setTextSize(11.5f);
                    t1.setTypeface(null, Typeface.BOLD);
                    col.addView(t1);

                    String facTxt = (u.fac == 3 ? "原:友军" : (u.fac == 2 ? "原:中立" : "原:敌对"));
                    TextView t2 = new TextView(ctx);
                    t2.setText(u.sub + " · " + facTxt + (u.exp > 0 ? (" · EXP " + u.exp) : ""));
                    t2.setTextColor(curTheme.textSec);
                    t2.setTextSize(9.8f);
                    col.addView(t2);
                    row.addView(col);

                    TextView pickLbl = new TextView(ctx);
                    pickLbl.setText("选择");
                    pickLbl.setTextColor(curTheme.accent);
                    pickLbl.setTextSize(11f);
                    pickLbl.setTypeface(null, Typeface.BOLD);
                    row.addView(pickLbl);

                    row.setOnClickListener(new View.OnClickListener() {
                        @Override public void onClick(View v) {
                            unitPickModalOverlay.setVisibility(View.GONE);
                            if (unitPickCb != null) unitPickCb.onPicked(u);
                        }
                    });
                    return row;
                }
            };
            lv.setAdapter(unitPickAdapter);
            unitPickModalCard.addView(lv);

            if (backdropView != null) {
                backdropView.addView(unitPickModalOverlay, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
            }
        }
        unitPickModalCard.setBackground(makeRoundBox(curTheme.panelBg | 0xFF000000, curTheme.accent, dp(2), dp(12)));
        styleEditBox(etUnitPickSearch);
        unitPickTitle.setText(title);
        etUnitPickSearch.setText("");
        if (cricketPickHook != null) cricketPickHook.run();
        unitPickModalOverlay.setVisibility(View.VISIBLE);
    }

    private FrameLayout buildSpawnEntityPage() {
        final FrameLayout wrap = new FrameLayout(ctx);
        LinearLayout root = new LinearLayout(ctx);
        root.setOrientation(LinearLayout.VERTICAL);
        wrap.addView(root, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        final int[] curCls = new int[] { 0 }; // 0=全部, 1=Unit(战斗/野外), 2=NPC, 3=Worker(营地工人)
        final int[] curFac = new int[] { 0 }; // 0=全部, 1=敌对, 2=中立, 3=友军
        final String[] curSub = new String[] { "全部细分" };
        final List<SpawnUnitInfo> visibleUnits = new ArrayList<SpawnUnitInfo>();

        LinearLayout topBar = new LinearLayout(ctx);
        topBar.setOrientation(LinearLayout.HORIZONTAL);
        topBar.setGravity(Gravity.CENTER_VERTICAL);
        final TextView tvUnitStatus = new TextView(ctx);
        tvUnitStatus.setText("生成实体: 读取官方实体分类库中...");
        tvUnitStatus.setTextColor(curTheme.accent);
        tvUnitStatus.setTextSize(10.5f);
        tvUnitStatus.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(7)));
        tvUnitStatus.setPadding(dp(8), dp(4), dp(8), dp(4));
        tvUnitStatus.setSingleLine(true);
        tvUnitStatus.setEllipsize(TextUtils.TruncateAt.END);
        tvUnitStatus.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        topBar.addView(tvUnitStatus);

        final Button btnClearSpawned = makePillBtn("清除已生成", curTheme.btnRed, 26);
        btnClearSpawned.setTextSize(9.8f);
        btnClearSpawned.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                Menu.NativeQuery(23, 2, 0, 0L, "");
                Toast.makeText(ctx, "已发送清除全部生成实体指令", Toast.LENGTH_SHORT).show();
            }
        });
        topBar.addView(btnClearSpawned);

        final Button btnRescanUnits = makePillBtn("刷新实体库", curTheme.btnSlate, 26);
        btnRescanUnits.setTextSize(9.8f);
        btnRescanUnits.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                File fDir = ctx.getFilesDir();
                if (fDir != null) Menu.NativeQuery(12, 1, 0, 0L, fDir.getAbsolutePath());
                mainH.postDelayed(new Runnable() {
                    @Override public void run() {
                        loadUnitsCatalog(ctx, true);
                        if (unitDynReloadHook != null) unitDynReloadHook.run();
                    }
                }, 250);
            }
        });
        topBar.addView(btnRescanUnits);
        LinearLayout.LayoutParams tbLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        tbLp.setMargins(0, 0, 0, dp(4));
        root.addView(topBar, tbLp);

        // Row 1: Official eUnitClass bar
        HorizontalScrollView hsvCls = new HorizontalScrollView(ctx);
        hsvCls.setHorizontalScrollBarEnabled(false);
        LinearLayout clsRow = new LinearLayout(ctx);
        clsRow.setOrientation(LinearLayout.HORIZONTAL);
        hsvCls.addView(clsRow);
        LinearLayout.LayoutParams hcLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        hcLp.setMargins(0, 0, 0, dp(4));
        root.addView(hsvCls, hcLp);

        // Row 2: Dynamic fine sub-classification bar
        HorizontalScrollView hsvSub = new HorizontalScrollView(ctx);
        hsvSub.setHorizontalScrollBarEnabled(false);
        final LinearLayout subRow = new LinearLayout(ctx);
        subRow.setOrientation(LinearLayout.HORIZONTAL);
        hsvSub.addView(subRow);
        LinearLayout.LayoutParams hsLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        hsLp.setMargins(0, 0, 0, dp(4));
        root.addView(hsvSub, hsLp);

        // Row 3: Search + Faction filter pill
        LinearLayout searchRow = new LinearLayout(ctx);
        searchRow.setOrientation(LinearLayout.HORIZONTAL);
        searchRow.setGravity(Gravity.CENTER_VERTICAL);
        final EditText etUnitSearch = makeDarkEdit("搜索实体名称 / ID / 细分 / 描述...", false);
        etUnitSearch.setTextSize(11f);
        etUnitSearch.setLayoutParams(new LinearLayout.LayoutParams(0, dp(29), 1f));
        searchRow.addView(etUnitSearch);

        final String[] facTitles = new String[] { "阵营:全部", "原阵营:敌对", "原阵营:中立", "原阵营:友军" };
        final Button btnFacFilter = makePillBtn("阵营:全部", curTheme.btnBlue, 27);
        btnFacFilter.setTextSize(9.5f);
        searchRow.addView(btnFacFilter);
        LinearLayout.LayoutParams srLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        srLp.setMargins(0, 0, 0, dp(4));
        root.addView(searchRow, srLp);

        final ListView lvUnits = new ListView(ctx);
        lvUnits.setDividerHeight(dp(4));
        lvUnits.setLayoutParams(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        root.addView(lvUnits);

        final BaseAdapter unitAdapter = new BaseAdapter() {
            @Override public int getCount() { return visibleUnits.size(); }
            @Override public Object getItem(int pos) { return visibleUnits.get(pos); }
            @Override public long getItemId(int pos) { return visibleUnits.get(pos).id; }
            @Override public View getView(int pos, View cv, ViewGroup parent) {
                final SpawnUnitInfo u = visibleUnits.get(pos);
                LinearLayout card = new LinearLayout(ctx);
                card.setOrientation(LinearLayout.HORIZONTAL);
                card.setGravity(Gravity.CENTER_VERTICAL);
                card.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(8)));
                card.setPadding(dp(10), dp(6), dp(8), dp(6));

                LinearLayout info = new LinearLayout(ctx);
                info.setOrientation(LinearLayout.VERTICAL);
                info.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

                String facBadge = (u.fac == 3 ? " [友军]" : (u.fac == 2 ? " [中立]" : " [敌对]"));
                int facColor = (u.fac == 3 ? curTheme.emerald : (u.fac == 2 ? 0xFFFBBF24 : 0xFFF87171));
                TextView t1 = new TextView(ctx);
                t1.setText(u.name + "  #" + u.id + facBadge);
                t1.setTextSize(11.8f);
                t1.setTypeface(null, Typeface.BOLD);
                t1.setTextColor(facColor);
                info.addView(t1);

                String clsName = (u.cls == 3 ? "官方:Worker营地工人" : (u.cls == 2 ? "官方:NPC人物" : "官方:Unit作战单位"));
                StringBuilder meta = new StringBuilder();
                meta.append(clsName).append(" · 细分:").append(u.sub);
                if (u.hp > 0) meta.append(" · HP ").append(u.hp);
                if (u.exp > 0) meta.append(" · EXP ").append(u.exp);
                if (!u.desc.isEmpty()) meta.append(" · ").append(u.desc);

                TextView t2 = new TextView(ctx);
                t2.setText(meta.toString());
                t2.setTextSize(9.8f);
                t2.setTextColor(curTheme.textSec);
                t2.setMaxLines(2);
                t2.setEllipsize(TextUtils.TruncateAt.END);
                info.addView(t2);
                card.addView(info);

                Button btnSpawn = makePillBtn("生成", u.fac == 3 ? curTheme.btnGreen : (u.fac == 2 ? curTheme.btnBlue : curTheme.btnRed), 26);
                btnSpawn.setTextSize(10f);
                View.OnClickListener spawnClick = new View.OnClickListener() {
                    @Override public void onClick(View v) {
                        Menu.NativeQuery(23, 1, u.id, 0L, "");
                        String fLbl = (u.fac == 3 ? "友军" : (u.fac == 2 ? "中立" : "敌对"));
                        Toast.makeText(ctx, "正在当前位置生成[" + fLbl + "] " + u.name + " (#" + u.id + ")", Toast.LENGTH_SHORT).show();
                    }
                };
                btnSpawn.setOnClickListener(spawnClick);
                card.setOnClickListener(spawnClick);
                card.addView(btnSpawn);
                return card;
            }
        };
        lvUnits.setAdapter(unitAdapter);

        final List<Button> clsBtns = new ArrayList<Button>();
        final List<Button> subBtns = new ArrayList<Button>();
        final Runnable rebuildUnitList = new Runnable() {
            @Override public void run() {
                List<SpawnUnitInfo> all = loadUnitsCatalog(ctx, false);
                visibleUnits.clear();
                String q = etUnitSearch.getText().toString().trim().toLowerCase(Locale.US);
                int enemyCnt = 0, neuCnt = 0, allyCnt = 0;
                for (SpawnUnitInfo u : all) {
                    if (curCls[0] != 0 && u.cls != curCls[0]) continue;
                    if (!"全部细分".equals(curSub[0]) && !curSub[0].equals(u.sub)) continue;
                    if (curFac[0] != 0 && u.fac != curFac[0]) continue;
                    if (!q.isEmpty()) {
                        String hay = (u.name + " " + u.id + " " + u.sub + " " + u.desc).toLowerCase(Locale.US);
                        if (!hay.contains(q)) continue;
                    }
                    visibleUnits.add(u);
                    if (u.fac == 1) enemyCnt++;
                    else if (u.fac == 2) neuCnt++;
                    else allyCnt++;
                }
                unitAdapter.notifyDataSetChanged();
                tvUnitStatus.setText("共 " + all.size() + " 实体 · 显示 " + visibleUnits.size() +
                    " (敌:" + enemyCnt + " 中:" + neuCnt + " 友:" + allyCnt + ") · 点击即按原阵营生成");
            }
        };

        final Runnable rebuildSubChips = new Runnable() {
            @Override public void run() {
                subRow.removeAllViews();
                subBtns.clear();
                List<SpawnUnitInfo> all = loadUnitsCatalog(ctx, false);
                final LinkedHashMap<String, Integer> counts = new LinkedHashMap<String, Integer>();
                int totalCls = 0;
                for (SpawnUnitInfo u : all) {
                    if (curCls[0] != 0 && u.cls != curCls[0]) continue;
                    totalCls++;
                    Integer c = counts.get(u.sub);
                    counts.put(u.sub, c == null ? 1 : (c + 1));
                }
                List<String> subs = new ArrayList<String>(counts.keySet());
                Collections.sort(subs, new Comparator<String>() {
                    @Override public int compare(String a, String b) {
                        return counts.get(b) - counts.get(a);
                    }
                });
                subs.add(0, "全部细分");
                if (!subs.contains(curSub[0])) curSub[0] = "全部细分";

                for (final String s : subs) {
                    int cnt = "全部细分".equals(s) ? totalCls : counts.get(s);
                    final Button sb = makePillBtn(s + "(" + cnt + ")", s.equals(curSub[0]) ? curTheme.btnGreen : curTheme.btnSlate, 23);
                    sb.setTextSize(9.5f);
                    sb.setTag(s);
                    sb.setOnClickListener(new View.OnClickListener() {
                        @Override public void onClick(View v) {
                            curSub[0] = s;
                            for (Button b : subBtns) {
                                boolean act = s.equals(b.getTag());
                                b.setBackground(makeRoundBox(act ? curTheme.btnGreen : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                            }
                            rebuildUnitList.run();
                        }
                    });
                    subBtns.add(sb);
                    subRow.addView(sb);
                }
            }
        };

        String[] clsLabels = new String[] { "全部实体", "Unit 战斗/生物", "NPC 剧情/角色", "Worker 营地工人" };
        for (int i = 0; i < clsLabels.length; i++) {
            final int ci = i;
            Button cbBtn = makePillBtn(clsLabels[i], i == 0 ? curTheme.btnBlue : curTheme.btnSlate, 25);
            cbBtn.setTextSize(9.8f);
            cbBtn.setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) {
                    curCls[0] = ci;
                    curSub[0] = "全部细分";
                    for (int k = 0; k < clsBtns.size(); k++) {
                        clsBtns.get(k).setBackground(makeRoundBox(k == curCls[0] ? curTheme.btnBlue : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                    }
                    rebuildSubChips.run();
                    rebuildUnitList.run();
                }
            });
            clsBtns.add(cbBtn);
            clsRow.addView(cbBtn);
        }

        btnFacFilter.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                curFac[0] = (curFac[0] + 1) % 4;
                btnFacFilter.setText(facTitles[curFac[0]]);
                rebuildUnitList.run();
            }
        });
        etUnitSearch.addTextChangedListener(new TextWatcher() {
            @Override public void beforeTextChanged(CharSequence s, int a, int b, int c) {}
            @Override public void onTextChanged(CharSequence s, int a, int b, int c) { rebuildUnitList.run(); }
            @Override public void afterTextChanged(Editable s) {}
        });

        unitDynReloadHook = new Runnable() {
            @Override public void run() {
                rebuildSubChips.run();
                rebuildUnitList.run();
            }
        };
        themeRefreshHooks.add(new Runnable() {
            @Override public void run() {
                tvUnitStatus.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(7)));
                tvUnitStatus.setTextColor(curTheme.accent);
                btnClearSpawned.setBackground(makeRoundBox(curTheme.btnRed, 0x33FFFFFF, 1, dp(7)));
                btnRescanUnits.setBackground(makeRoundBox(curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                styleEditBox(etUnitSearch);
                btnFacFilter.setBackground(makeRoundBox(curTheme.btnBlue, 0x33FFFFFF, 1, dp(7)));
                for (int k = 0; k < clsBtns.size(); k++) {
                    clsBtns.get(k).setBackground(makeRoundBox(k == curCls[0] ? curTheme.btnBlue : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                }
                for (Button b : subBtns) {
                    boolean act = curSub[0].equals(b.getTag());
                    b.setBackground(makeRoundBox(act ? curTheme.btnGreen : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                }
                unitAdapter.notifyDataSetChanged();
            }
        });

        loadUnitsCatalog(ctx, false);
        rebuildSubChips.run();
        rebuildUnitList.run();
        registerFeature("生成实体", "生成全游戏实体 (官方分类与原阵营)", "按Unit/NPC/Worker官方大类与细分列出全实体并按原阵营生成", "生成 实体 刷怪 召唤 boss 野生 工人 spawn entity", 8, wrap, null, null);
        return wrap;
    }

    // =========================================================================
    //  TAB 9: 地图传送 (严格按原游戏章节与父子Belong归属分级 · 支持搜索与一键传送)
    // =========================================================================
    private FrameLayout buildMapTeleportPage() {
        final FrameLayout wrap = new FrameLayout(ctx);
        LinearLayout root = new LinearLayout(ctx);
        root.setOrientation(LinearLayout.VERTICAL);
        wrap.addView(root, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        final int[] curCat = new int[] { 0 };
        final int[] curMode = new int[] { 0 }; // 0=树形分级, 1=仅大地图, 2=已去过
        final boolean[] showInvalid = new boolean[] { false };
        final Set<Integer> expandedIds = new HashSet<Integer>();
        final Set<Integer> arrivedSet = new HashSet<Integer>();
        final Set<Integer> ownSet = new HashSet<Integer>();
        final int[] curSceneId = new int[] { 0 };
        final List<MapEntry> visibleMaps = new ArrayList<MapEntry>();
        final Map<Integer, MapEntry> mapById = new HashMap<Integer, MapEntry>();

        // Top info bar
        LinearLayout topBar = new LinearLayout(ctx);
        topBar.setOrientation(LinearLayout.HORIZONTAL);
        topBar.setGravity(Gravity.CENTER_VERTICAL);
        final TextView tvMapStatus = new TextView(ctx);
        tvMapStatus.setText("地图传送: 读取全量地图配置中...");
        tvMapStatus.setTextColor(curTheme.accent);
        tvMapStatus.setTextSize(10.5f);
        tvMapStatus.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(7)));
        tvMapStatus.setPadding(dp(8), dp(4), dp(8), dp(4));
        tvMapStatus.setSingleLine(true);
        tvMapStatus.setEllipsize(TextUtils.TruncateAt.END);
        tvMapStatus.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        topBar.addView(tvMapStatus);

        final Button btnRefreshMap = makePillBtn("刷新地图", curTheme.btnSlate, 26);
        btnRefreshMap.setTextSize(10);
        btnRefreshMap.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                Menu.NativeQuery(13, 0, 0, 0L, "");
                mainH.postDelayed(new Runnable() {
                    @Override public void run() {
                        loadMapsCatalog(ctx, true);
                        if (mapStateRefreshHook != null) mapStateRefreshHook.run();
                    }
                }, 250);
            }
        });
        topBar.addView(btnRefreshMap);
        LinearLayout.LayoutParams tbLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        tbLp.setMargins(0, 0, 0, dp(4));
        root.addView(topBar, tbLp);

        // Chapter filter bar
        HorizontalScrollView hsv = new HorizontalScrollView(ctx);
        hsv.setHorizontalScrollBarEnabled(false);
        LinearLayout catRow = new LinearLayout(ctx);
        catRow.setOrientation(LinearLayout.HORIZONTAL);
        final List<Button> catBtns = new ArrayList<Button>();
        hsv.addView(catRow);
        LinearLayout.LayoutParams hvLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        hvLp.setMargins(0, 0, 0, dp(4));
        root.addView(hsv, hvLp);

        // Search & filter bar
        LinearLayout searchRow = new LinearLayout(ctx);
        searchRow.setOrientation(LinearLayout.HORIZONTAL);
        searchRow.setGravity(Gravity.CENTER_VERTICAL);
        etMapSearchBox = makeDarkEdit("搜索地图名称 / 场景ID / 描述...", false);
        etMapSearchBox.setTextSize(11);
        etMapSearchBox.setLayoutParams(new LinearLayout.LayoutParams(0, dp(29), 1f));
        searchRow.addView(etMapSearchBox);

        final String[] modeTitles = new String[] { "树形分级", "仅大地图", "仅已去过" };
        final Button btnMapMode = makePillBtn("树形分级", curTheme.btnBlue, 27);
        btnMapMode.setTextSize(9.5f);
        searchRow.addView(btnMapMode);

        final Button btnToggleValid = makePillBtn("含未实装:关", curTheme.btnSlate, 27);
        btnToggleValid.setTextSize(9.5f);
        searchRow.addView(btnToggleValid);

        LinearLayout.LayoutParams srLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        srLp.setMargins(0, 0, 0, dp(4));
        root.addView(searchRow, srLp);

        final ListView lvMaps = new ListView(ctx);
        lvMaps.setDividerHeight(dp(4));
        lvMaps.setLayoutParams(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        root.addView(lvMaps);

        final BaseAdapter mapAdapter = new BaseAdapter() {
            @Override public int getCount() { return visibleMaps.size(); }
            @Override public Object getItem(int pos) { return visibleMaps.get(pos); }
            @Override public long getItemId(int pos) { return visibleMaps.get(pos).id; }
            @Override public View getView(int pos, View convertView, ViewGroup parent) {
                final MapEntry m = visibleMaps.get(pos);
                LinearLayout card = new LinearLayout(ctx);
                card.setOrientation(LinearLayout.HORIZONTAL);
                card.setGravity(Gravity.CENTER_VERTICAL);
                boolean isCur = (m.id == curSceneId[0]);
                boolean isParent = (m.childCount > 0 || m.depth == 0);
                card.setBackground(makeRoundBox(
                    isCur ? curTheme.cardOnBg : curTheme.cardBg,
                    isCur ? curTheme.emerald : (isParent ? curTheme.panelEdge : curTheme.cardEdge),
                    dp(1), dp(8)));

                String q = etMapSearchBox.getText() != null ? etMapSearchBox.getText().toString().trim() : "";
                int indentDp = q.isEmpty() && curMode[0] == 0 ? Math.min(3, m.depth) * 14 : 0;
                card.setPadding(dp(8 + indentDp), dp(6), dp(8), dp(6));

                if (q.isEmpty() && curMode[0] == 0 && m.childCount > 0) {
                    TextView tvExp = new TextView(ctx);
                    tvExp.setText(expandedIds.contains(m.id) ? "▾" : "▸");
                    tvExp.setTextColor(curTheme.accent);
                    tvExp.setTextSize(14);
                    tvExp.setTypeface(null, Typeface.BOLD);
                    tvExp.setPadding(dp(2), 0, dp(6), 0);
                    card.addView(tvExp);
                } else if (m.depth > 0 && q.isEmpty() && curMode[0] == 0) {
                    TextView tvLeaf = new TextView(ctx);
                    tvLeaf.setText("↳");
                    tvLeaf.setTextColor(curTheme.textSec);
                    tvLeaf.setTextSize(12);
                    tvLeaf.setPadding(0, 0, dp(5), 0);
                    card.addView(tvLeaf);
                }

                LinearLayout info = new LinearLayout(ctx);
                info.setOrientation(LinearLayout.VERTICAL);
                info.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

                String chap = (m.cat >= 1 && m.cat < CHAPTER_NAMES.length) ? CHAPTER_NAMES[m.cat] : "主线";
                StringBuilder titleSb = new StringBuilder();
                if (!m.valid) titleSb.append("[测试] ");
                titleSb.append(m.name).append(" (#").append(m.id).append(")");
                if (isCur) titleSb.append("  ★当前");
                else if (ownSet.contains(m.id)) titleSb.append("  [已占领]");
                else if (arrivedSet.contains(m.id)) titleSb.append("  [已去过]");

                TextView t1 = new TextView(ctx);
                t1.setText(titleSb.toString());
                t1.setTextSize(m.depth == 0 ? 12.2f : 11.4f);
                t1.setTypeface(null, m.depth == 0 ? Typeface.BOLD : Typeface.NORMAL);
                t1.setTextColor(isCur ? curTheme.emerald : curTheme.textPri);
                info.addView(t1);

                StringBuilder subSb = new StringBuilder();
                subSb.append(chap);
                if (m.belong > 0 && m.belong != m.id) {
                    MapEntry pm = mapById.get(m.belong);
                    subSb.append(" · 属: ").append(pm != null ? pm.name : ("#" + m.belong));
                } else if (m.childCount > 0) {
                    subSb.append(" · 含 ").append(m.childCount).append(" 个直属子地图");
                }
                if (!m.us.isEmpty()) subSb.append(" · ").append(m.us);
                else if (!m.desc.isEmpty()) subSb.append(" · ").append(m.desc);

                TextView t2 = new TextView(ctx);
                t2.setText(subSb.toString());
                t2.setTextSize(10f);
                t2.setTextColor(curTheme.textSec);
                t2.setMaxLines(2);
                t2.setEllipsize(TextUtils.TruncateAt.END);
                info.addView(t2);
                card.addView(info);

                Button btnGo = makePillBtn(isCur ? "重传" : "传送", isCur ? curTheme.btnSlate : curTheme.btnGreen, 26);
                btnGo.setTextSize(10f);
                btnGo.setOnClickListener(new View.OnClickListener() {
                    @Override public void onClick(View v) {
                        Menu.NativeQuery(20, m.id, m.spawn, 1L, "");
                        Toast.makeText(ctx, "正在传送至: " + m.name + " (#" + m.id + ")", Toast.LENGTH_SHORT).show();
                        mainH.postDelayed(new Runnable() {
                            @Override public void run() {
                                pollSnapshot();
                                if (mapStateRefreshHook != null) mapStateRefreshHook.run();
                            }
                        }, 700);
                    }
                });
                card.addView(btnGo);

                if (m.childCount > 0 && q.isEmpty() && curMode[0] == 0) {
                    card.setOnClickListener(new View.OnClickListener() {
                        @Override public void onClick(View v) {
                            if (expandedIds.contains(m.id)) expandedIds.remove(m.id);
                            else expandedIds.add(m.id);
                            if (mapDynReloadHook != null) mapDynReloadHook.run();
                        }
                    });
                }
                return card;
            }
        };
        lvMaps.setAdapter(mapAdapter);

        final Runnable rebuildMaps = new Runnable() {
            @Override public void run() {
                List<MapEntry> all = loadMapsCatalog(ctx, false);
                mapById.clear();
                for (MapEntry m : all) mapById.put(m.id, m);

                String q = etMapSearchBox.getText() != null ? etMapSearchBox.getText().toString().trim().toLowerCase(Locale.US) : "";
                visibleMaps.clear();

                if (!q.isEmpty() || curMode[0] != 0) {
                    for (MapEntry m : all) {
                        if (!showInvalid[0] && !m.valid) continue;
                        if (curCat[0] != 0 && m.cat != curCat[0]) continue;
                        if (curMode[0] == 1 && m.depth > 0 && m.belong > 0 && m.belong != m.id) continue;
                        if (curMode[0] == 2 && !arrivedSet.contains(m.id) && m.id != curSceneId[0]) continue;
                        if (!q.isEmpty()) {
                            String hay = (m.id + " " + m.name + " " + m.us + " " + m.desc + " " + m.belong).toLowerCase(Locale.US);
                            if (!hay.contains(q)) continue;
                        }
                        visibleMaps.add(m);
                    }
                } else {
                    // Build DFS hierarchical tree adhering strictly to game BelongScene
                    Map<Integer, List<MapEntry>> childrenByParent = new HashMap<Integer, List<MapEntry>>();
                    List<MapEntry> roots = new ArrayList<MapEntry>();
                    for (MapEntry m : all) {
                        if (!showInvalid[0] && !m.valid) continue;
                        if (curCat[0] != 0 && m.cat != curCat[0]) continue;
                        boolean hasVisibleParent = (m.belong > 0 && m.belong != m.id && mapById.containsKey(m.belong)
                            && (curCat[0] == 0 || mapById.get(m.belong).cat == curCat[0])
                            && (showInvalid[0] || mapById.get(m.belong).valid));
                        if (!hasVisibleParent) {
                            roots.add(m);
                        } else {
                            List<MapEntry> cl = childrenByParent.get(m.belong);
                            if (cl == null) { cl = new ArrayList<MapEntry>(); childrenByParent.put(m.belong, cl); }
                            cl.add(m);
                        }
                    }
                    final List<MapEntry> stack = new ArrayList<MapEntry>(roots);
                    Set<Integer> visited = new HashSet<Integer>();
                    // DFS traversal without recursion stack overflow
                    for (int i = 0; i < stack.size(); i++) {
                        MapEntry node = stack.get(i);
                        if (visited.contains(node.id)) continue;
                        visited.add(node.id);
                        visibleMaps.add(node);
                        if (expandedIds.contains(node.id)) {
                            List<MapEntry> ch = childrenByParent.get(node.id);
                            if (ch != null && !ch.isEmpty()) {
                                int insertPos = i + 1;
                                for (int j = 0; j < ch.size(); j++) {
                                    stack.add(insertPos + j, ch.get(j));
                                }
                            }
                        }
                    }
                }

                MapEntry curM = mapById.get(curSceneId[0]);
                String curName = curM != null ? curM.name : (curSceneId[0] > 0 ? ("#" + curSceneId[0]) : "未进存档");
                tvMapStatus.setText("当前位置: " + curName + " (#" + curSceneId[0] + ") · 显示 " + visibleMaps.size() + " / 共 " + all.size() + " 张地图 (已去过 " + arrivedSet.size() + ")");
                mapAdapter.notifyDataSetChanged();
            }
        };

        for (int i = 0; i < CHAPTER_NAMES.length; i++) {
            final int ci = i;
            Button cbBtn = makePillBtn(CHAPTER_NAMES[i], i == 0 ? curTheme.btnBlue : curTheme.btnSlate, 24);
            cbBtn.setTextSize(10f);
            cbBtn.setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) {
                    curCat[0] = ci;
                    for (int k = 0; k < catBtns.size(); k++) {
                        catBtns.get(k).setBackground(makeRoundBox(k == curCat[0] ? curTheme.btnBlue : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                    }
                    rebuildMaps.run();
                }
            });
            catBtns.add(cbBtn);
            catRow.addView(cbBtn);
        }

        btnMapMode.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                curMode[0] = (curMode[0] + 1) % 3;
                btnMapMode.setText(modeTitles[curMode[0]]);
                rebuildMaps.run();
            }
        });
        btnToggleValid.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                showInvalid[0] = !showInvalid[0];
                btnToggleValid.setText(showInvalid[0] ? "含未实装:开" : "含未实装:关");
                btnToggleValid.setBackground(makeRoundBox(showInvalid[0] ? curTheme.btnGreen : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                rebuildMaps.run();
            }
        });
        etMapSearchBox.addTextChangedListener(new TextWatcher() {
            @Override public void beforeTextChanged(CharSequence s, int a, int b, int c) {}
            @Override public void onTextChanged(CharSequence s, int a, int b, int c) { rebuildMaps.run(); }
            @Override public void afterTextChanged(Editable s) {}
        });

        mapDynReloadHook = rebuildMaps;
        mapStateRefreshHook = new Runnable() {
            @Override public void run() {
                try {
                    String raw = readFileText(new File(ctx.getFilesDir(), "dyn_map_state.json"));
                    if (raw != null && raw.startsWith("{")) {
                        JSONObject o = new JSONObject(raw);
                        curSceneId[0] = o.optInt("curScene", 0);
                        arrivedSet.clear();
                        JSONArray aa = o.optJSONArray("arrived");
                        if (aa != null) for (int i = 0; i < aa.length(); i++) arrivedSet.add(aa.optInt(i));
                        ownSet.clear();
                        JSONArray oa = o.optJSONArray("own");
                        if (oa != null) for (int i = 0; i < oa.length(); i++) ownSet.add(oa.optInt(i));
                    }
                } catch (Throwable ignored) {}
                rebuildMaps.run();
            }
        };

        themeRefreshHooks.add(new Runnable() {
            @Override public void run() {
                tvMapStatus.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(7)));
                tvMapStatus.setTextColor(curTheme.accent);
                btnRefreshMap.setBackground(makeRoundBox(curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                styleEditBox(etMapSearchBox);
                btnMapMode.setBackground(makeRoundBox(curTheme.btnBlue, 0x33FFFFFF, 1, dp(7)));
                btnToggleValid.setBackground(makeRoundBox(showInvalid[0] ? curTheme.btnGreen : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                for (int k = 0; k < catBtns.size(); k++) {
                    catBtns.get(k).setBackground(makeRoundBox(k == curCat[0] ? curTheme.btnBlue : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                }
                mapAdapter.notifyDataSetChanged();
            }
        });

        loadMapsCatalog(ctx, false);
        mapStateRefreshHook.run();
        registerFeature("地图传送", "全地图与子地图传送", "按游戏原章节与Belong归属分级显示全地图，支持搜索与一键传送(兼容联机副本单人进入)", "地图 传送 跳转 副本 大地图 子地图 联机 teleport map", 9, wrap, null, null);
        return wrap;
    }

    // =========================================================================
    //  TAB 10: 任务管理 (原游戏章节+场景归属+前置树形结构 · 原生完成与重置推进)
    // =========================================================================
    private static class TaskListItem {
        boolean isHeader;
        int rootId;
        String headerTitle;
        int total;
        int done;
        TaskEntry task;
    }

    private FrameLayout buildTaskManagerPage() {
        final FrameLayout wrap = new FrameLayout(ctx);
        LinearLayout root = new LinearLayout(ctx);
        root.setOrientation(LinearLayout.VERTICAL);
        wrap.addView(root, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        final int[] curCat = new int[] { 0 };
        final int[] curFilter = new int[] { 0 }; // 0=全部, 1=可做/已解锁, 2=已完成, 3=未解锁
        final boolean[] withPrereqs = new boolean[] { true };
        final Set<Integer> collapsedRoots = new HashSet<Integer>();
        final Map<Integer, int[]> taskStates = new HashMap<Integer, int[]>(); // [unlocked, shown, finished]
        final Map<Integer, String> taskLiveProg = new HashMap<Integer, String>();
        final List<TaskListItem> listRows = new ArrayList<TaskListItem>();

        // Top info bar
        LinearLayout topBar = new LinearLayout(ctx);
        topBar.setOrientation(LinearLayout.HORIZONTAL);
        topBar.setGravity(Gravity.CENTER_VERTICAL);
        final TextView tvTaskStatus = new TextView(ctx);
        tvTaskStatus.setText("任务管理: 读取场景事件与状态树中...");
        tvTaskStatus.setTextColor(curTheme.accent);
        tvTaskStatus.setTextSize(10.5f);
        tvTaskStatus.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(7)));
        tvTaskStatus.setPadding(dp(8), dp(4), dp(8), dp(4));
        tvTaskStatus.setSingleLine(true);
        tvTaskStatus.setEllipsize(TextUtils.TruncateAt.END);
        tvTaskStatus.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        topBar.addView(tvTaskStatus);

        final Button btnPrereq = makePillBtn("补全前置:开", curTheme.btnGreen, 26);
        btnPrereq.setTextSize(9.5f);
        topBar.addView(btnPrereq);

        final Button btnRefTask = makePillBtn("刷新", curTheme.btnSlate, 26);
        btnRefTask.setTextSize(9.5f);
        topBar.addView(btnRefTask);

        LinearLayout.LayoutParams tbLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        tbLp.setMargins(0, 0, 0, dp(4));
        root.addView(topBar, tbLp);

        // Chapter filter bar
        HorizontalScrollView hsv = new HorizontalScrollView(ctx);
        hsv.setHorizontalScrollBarEnabled(false);
        LinearLayout catRow = new LinearLayout(ctx);
        catRow.setOrientation(LinearLayout.HORIZONTAL);
        final List<Button> catBtns = new ArrayList<Button>();
        hsv.addView(catRow);
        LinearLayout.LayoutParams hvLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        hvLp.setMargins(0, 0, 0, dp(4));
        root.addView(hsv, hvLp);

        // Search + Status filter + Batch actions row
        LinearLayout filterRow = new LinearLayout(ctx);
        filterRow.setOrientation(LinearLayout.HORIZONTAL);
        filterRow.setGravity(Gravity.CENTER_VERTICAL);

        etTaskSearchBox = makeDarkEdit("搜索任务名 / 归属 / 条件 / ID...", false);
        etTaskSearchBox.setTextSize(11);
        etTaskSearchBox.setLayoutParams(new LinearLayout.LayoutParams(0, dp(29), 1f));
        filterRow.addView(etTaskSearchBox);

        final String[] stFilterNames = new String[] { "状态:全部", "状态:已解锁", "状态:已完成", "状态:未解锁" };
        final Button btnStFilter = makePillBtn(stFilterNames[0], curTheme.btnBlue, 27);
        btnStFilter.setTextSize(9.5f);
        filterRow.addView(btnStFilter);

        final Button btnBatchDone = makePillBtn("本篇全完", curTheme.btnGreen, 27);
        btnBatchDone.setTextSize(9.5f);
        filterRow.addView(btnBatchDone);

        final Button btnBatchReset = makePillBtn("本篇重置", curTheme.btnRed, 27);
        btnBatchReset.setTextSize(9.5f);
        filterRow.addView(btnBatchReset);

        LinearLayout.LayoutParams frLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        frLp.setMargins(0, 0, 0, dp(4));
        root.addView(filterRow, frLp);

        final ListView lvTasks = new ListView(ctx);
        lvTasks.setDividerHeight(dp(4));
        lvTasks.setLayoutParams(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        root.addView(lvTasks);

        final Runnable[] rebuildTasksRef = new Runnable[1];

        final BaseAdapter taskAdapter = new BaseAdapter() {
            @Override public int getCount() { return listRows.size(); }
            @Override public Object getItem(int pos) { return listRows.get(pos); }
            @Override public long getItemId(int pos) {
                TaskListItem it = listRows.get(pos);
                return it.isHeader ? (100000L + it.rootId) : it.task.id;
            }
            @Override public View getView(int pos, View convertView, ViewGroup parent) {
                final TaskListItem item = listRows.get(pos);
                if (item.isHeader) {
                    LinearLayout hdr = new LinearLayout(ctx);
                    hdr.setOrientation(LinearLayout.HORIZONTAL);
                    hdr.setGravity(Gravity.CENTER_VERTICAL);
                    hdr.setBackground(makeRoundBox(curTheme.selBg, curTheme.panelEdge, dp(1), dp(7)));
                    hdr.setPadding(dp(10), dp(6), dp(10), dp(6));

                    boolean collapsed = collapsedRoots.contains(item.rootId);
                    TextView tvH = new TextView(ctx);
                    tvH.setText((collapsed ? "▸ " : "▾ ") + item.headerTitle + "   (" + item.done + "/" + item.total + " 已完成)");
                    tvH.setTextColor(curTheme.accent);
                    tvH.setTextSize(11.5f);
                    tvH.setTypeface(null, Typeface.BOLD);
                    tvH.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
                    hdr.addView(tvH);

                    hdr.setOnClickListener(new View.OnClickListener() {
                        @Override public void onClick(View v) {
                            if (collapsedRoots.contains(item.rootId)) collapsedRoots.remove(item.rootId);
                            else collapsedRoots.add(item.rootId);
                            if (rebuildTasksRef[0] != null) rebuildTasksRef[0].run();
                        }
                    });
                    return hdr;
                }

                final TaskEntry t = item.task;
                int[] st = taskStates.get(t.id);
                boolean unlocked = st != null && st[0] != 0;
                boolean shown = st != null && st[1] != 0;
                final boolean finished = st != null && st[2] != 0;
                String liveP = taskLiveProg.get(t.id);

                LinearLayout card = new LinearLayout(ctx);
                card.setOrientation(LinearLayout.HORIZONTAL);
                card.setGravity(Gravity.CENTER_VERTICAL);
                card.setBackground(makeRoundBox(
                    finished ? curTheme.cardOnBg : curTheme.cardBg,
                    finished ? curTheme.emerald : (unlocked ? curTheme.accent : curTheme.cardEdge),
                    dp(1), dp(8)));
                int indent = Math.min(3, Math.max(0, t.depth)) * 12;
                card.setPadding(dp(9 + indent), dp(6), dp(8), dp(6));

                LinearLayout info = new LinearLayout(ctx);
                info.setOrientation(LinearLayout.VERTICAL);
                info.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

                String tier = t.depth == 0 ? "[大任务]" : (t.depth == 1 ? "[子任务]" : "[子子任务]");
                String stTag = finished ? "  ✓已完成" : (shown ? "  ●追踪中" : (unlocked ? "  ●已解锁" : "  ○未解锁"));

                TextView t1 = new TextView(ctx);
                t1.setText(tier + " " + t.name + " (#" + t.id + ")" + stTag);
                t1.setTextSize(t.depth == 0 ? 11.8f : 11.2f);
                t1.setTypeface(null, t.depth == 0 ? Typeface.BOLD : Typeface.NORMAL);
                t1.setTextColor(finished ? curTheme.emerald : (unlocked ? curTheme.textPri : curTheme.textSec));
                info.addView(t1);

                String chap = (t.cat >= 1 && t.cat < CHAPTER_NAMES.length) ? CHAPTER_NAMES[t.cat] : "主线";
                StringBuilder belongSb = new StringBuilder();
                belongSb.append(chap).append(" · 归属: ").append(t.sceneName.isEmpty() ? ("#" + t.scene) : t.sceneName);
                if (!t.scenes.isEmpty() && t.scenes.indexOf('|') >= 0) belongSb.append(" [同步场景]");
                if (liveP != null && !liveP.isEmpty()) belongSb.append(" · 进度: ").append(liveP);

                TextView t2 = new TextView(ctx);
                t2.setText(belongSb.toString());
                t2.setTextSize(10f);
                t2.setTextColor(curTheme.accent);
                info.addView(t2);

                StringBuilder condSb = new StringBuilder();
                if (!t.desc.isEmpty()) condSb.append("说明: ").append(t.desc).append('\n');
                if (!t.unlockTxt.isEmpty()) condSb.append("解锁条件: ").append(t.unlockTxt).append('\n');
                if (!t.finishTxt.isEmpty()) condSb.append("完成条件: ").append(t.finishTxt);
                else if (!t.showTxt.isEmpty()) condSb.append("触发条件: ").append(t.showTxt);
                if (!t.reward.isEmpty()) condSb.append(condSb.length() > 0 ? "\n奖励: " : "奖励: ").append(t.reward);
                String condFinal = condSb.toString().trim();
                if (!condFinal.isEmpty()) {
                    TextView t3 = new TextView(ctx);
                    t3.setText(condFinal);
                    t3.setTextSize(9.8f);
                    t3.setTextColor(curTheme.textSec);
                    info.addView(t3);
                }
                card.addView(info);

                Button btnOp = makePillBtn(finished ? "设未完成" : "设已完成", finished ? curTheme.btnRed : curTheme.btnGreen, 28);
                btnOp.setTextSize(9.8f);
                btnOp.setOnClickListener(new View.OnClickListener() {
                    @Override public void onClick(View v) {
                        int subOp = finished ? 2 : 1;
                        String arg = (!finished && withPrereqs[0]) ? "1" : "0";
                        Menu.NativeQuery(21, subOp, t.id, 0L, arg);
                        Toast.makeText(ctx, (finished ? "重置任务: " : "原生完成任务: ") + t.name, Toast.LENGTH_SHORT).show();
                        mainH.postDelayed(new Runnable() {
                            @Override public void run() {
                                pollSnapshot();
                                if (taskStateRefreshHook != null) taskStateRefreshHook.run();
                            }
                        }, 300);
                    }
                });
                card.addView(btnOp);
                return card;
            }
        };
        lvTasks.setAdapter(taskAdapter);

        final Runnable rebuildTasks = new Runnable() {
            @Override public void run() {
                List<TaskEntry> all = loadTasksCatalog(ctx, false);
                String q = etTaskSearchBox.getText() != null ? etTaskSearchBox.getText().toString().trim().toLowerCase(Locale.US) : "";
                listRows.clear();

                // Group strictly by (cat, root) keeping original order
                Map<Integer, List<TaskEntry>> grouped = new LinkedHashMap<Integer, List<TaskEntry>>();
                Map<Integer, String> rootNames = new HashMap<Integer, String>();
                int totalFiltered = 0;
                int doneFiltered = 0;

                for (TaskEntry t : all) {
                    if (curCat[0] != 0 && t.cat != curCat[0]) continue;
                    int[] st = taskStates.get(t.id);
                    boolean unlocked = st != null && st[0] != 0;
                    boolean finished = st != null && st[2] != 0;
                    if (curFilter[0] == 1 && (finished || !unlocked)) continue;
                    if (curFilter[0] == 2 && !finished) continue;
                    if (curFilter[0] == 3 && (unlocked || finished)) continue;

                    if (!q.isEmpty()) {
                        String hay = (t.id + " " + t.name + " " + t.desc + " " + t.sceneName + " " + t.rootName + " " + t.unlockTxt + " " + t.finishTxt).toLowerCase(Locale.US);
                        if (!hay.contains(q)) continue;
                    }
                    totalFiltered++;
                    if (finished) doneFiltered++;

                    int grpKey = t.root > 0 ? t.root : (t.scene > 0 ? t.scene : (t.cat * 10000));
                    List<TaskEntry> gl = grouped.get(grpKey);
                    if (gl == null) { gl = new ArrayList<TaskEntry>(); grouped.put(grpKey, gl); }
                    gl.add(t);
                    if (!rootNames.containsKey(grpKey)) {
                        String chap = (t.cat >= 1 && t.cat < CHAPTER_NAMES.length) ? CHAPTER_NAMES[t.cat] : "主线";
                        String rn = !t.rootName.isEmpty() ? t.rootName : (!t.sceneName.isEmpty() ? t.sceneName : chap);
                        rootNames.put(grpKey, chap + " · " + rn + " (#" + grpKey + ")");
                    }
                }

                for (Map.Entry<Integer, List<TaskEntry>> ent : grouped.entrySet()) {
                    int gk = ent.getKey();
                    List<TaskEntry> gl = ent.getValue();
                    int gd = 0;
                    for (TaskEntry te : gl) {
                        int[] st = taskStates.get(te.id);
                        if (st != null && st[2] != 0) gd++;
                    }
                    TaskListItem hdr = new TaskListItem();
                    hdr.isHeader = true;
                    hdr.rootId = gk;
                    hdr.headerTitle = rootNames.get(gk);
                    hdr.total = gl.size();
                    hdr.done = gd;
                    listRows.add(hdr);

                    if (q.isEmpty() && collapsedRoots.contains(gk)) continue;
                    for (TaskEntry te : gl) {
                        TaskListItem row = new TaskListItem();
                        row.isHeader = false;
                        row.rootId = gk;
                        row.task = te;
                        listRows.add(row);
                    }
                }

                tvTaskStatus.setText("任务总览: 匹配 " + totalFiltered + " 项 (" + doneFiltered + " 已完成) · 点击设完成自动联动原游戏触发器");
                taskAdapter.notifyDataSetChanged();
            }
        };
        rebuildTasksRef[0] = rebuildTasks;

        for (int i = 0; i < CHAPTER_NAMES.length; i++) {
            final int ci = i;
            Button cbBtn = makePillBtn(CHAPTER_NAMES[i], i == 0 ? curTheme.btnBlue : curTheme.btnSlate, 24);
            cbBtn.setTextSize(10f);
            cbBtn.setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) {
                    curCat[0] = ci;
                    for (int k = 0; k < catBtns.size(); k++) {
                        catBtns.get(k).setBackground(makeRoundBox(k == curCat[0] ? curTheme.btnBlue : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                    }
                    rebuildTasks.run();
                }
            });
            catBtns.add(cbBtn);
            catRow.addView(cbBtn);
        }

        btnPrereq.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                withPrereqs[0] = !withPrereqs[0];
                btnPrereq.setText(withPrereqs[0] ? "补全前置:开" : "补全前置:关");
                btnPrereq.setBackground(makeRoundBox(withPrereqs[0] ? curTheme.btnGreen : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
            }
        });
        btnRefTask.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                Menu.NativeQuery(13, 0, 0, 0L, "");
                mainH.postDelayed(new Runnable() {
                    @Override public void run() {
                        loadTasksCatalog(ctx, true);
                        if (taskStateRefreshHook != null) taskStateRefreshHook.run();
                    }
                }, 250);
            }
        });
        btnStFilter.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                curFilter[0] = (curFilter[0] + 1) % 4;
                btnStFilter.setText(stFilterNames[curFilter[0]]);
                rebuildTasks.run();
            }
        });
        btnBatchDone.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                Menu.NativeQuery(21, 5, curCat[0], (long) curCat[0], withPrereqs[0] ? "1" : "0");
                Toast.makeText(ctx, "正在批量原生完成 [" + CHAPTER_NAMES[curCat[0]] + "] 任务...", Toast.LENGTH_SHORT).show();
                mainH.postDelayed(new Runnable() {
                    @Override public void run() {
                        pollSnapshot();
                        if (taskStateRefreshHook != null) taskStateRefreshHook.run();
                    }
                }, 400);
            }
        });
        btnBatchReset.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                Menu.NativeQuery(21, 6, curCat[0], (long) curCat[0], "0");
                Toast.makeText(ctx, "正在批量重置 [" + CHAPTER_NAMES[curCat[0]] + "] 任务...", Toast.LENGTH_SHORT).show();
                mainH.postDelayed(new Runnable() {
                    @Override public void run() {
                        pollSnapshot();
                        if (taskStateRefreshHook != null) taskStateRefreshHook.run();
                    }
                }, 400);
            }
        });
        etTaskSearchBox.addTextChangedListener(new TextWatcher() {
            @Override public void beforeTextChanged(CharSequence s, int a, int b, int c) {}
            @Override public void onTextChanged(CharSequence s, int a, int b, int c) { rebuildTasks.run(); }
            @Override public void afterTextChanged(Editable s) {}
        });

        taskDynReloadHook = rebuildTasks;
        taskStateRefreshHook = new Runnable() {
            @Override public void run() {
                try {
                    String raw = readFileText(new File(ctx.getFilesDir(), "dyn_task_state.json"));
                    if (raw != null && raw.startsWith("{")) {
                        JSONObject o = new JSONObject(raw);
                        JSONArray evs = o.optJSONArray("events");
                        taskStates.clear();
                        taskLiveProg.clear();
                        if (evs != null) {
                            for (int i = 0; i < evs.length(); i++) {
                                JSONArray r = evs.optJSONArray(i);
                                if (r == null || r.length() < 4) continue;
                                int eid = r.optInt(0);
                                taskStates.put(eid, new int[] { r.optInt(1), r.optInt(2), r.optInt(3) });
                                if (r.length() >= 5) {
                                    String p = r.optString(4, "").trim();
                                    if (!p.isEmpty()) taskLiveProg.put(eid, p);
                                }
                            }
                        }
                    }
                } catch (Throwable ignored) {}
                rebuildTasks.run();
            }
        };

        themeRefreshHooks.add(new Runnable() {
            @Override public void run() {
                tvTaskStatus.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(7)));
                tvTaskStatus.setTextColor(curTheme.accent);
                btnPrereq.setBackground(makeRoundBox(withPrereqs[0] ? curTheme.btnGreen : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                btnRefTask.setBackground(makeRoundBox(curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                styleEditBox(etTaskSearchBox);
                btnStFilter.setBackground(makeRoundBox(curTheme.btnBlue, 0x33FFFFFF, 1, dp(7)));
                btnBatchDone.setBackground(makeRoundBox(curTheme.btnGreen, 0x33FFFFFF, 1, dp(7)));
                btnBatchReset.setBackground(makeRoundBox(curTheme.btnRed, 0x33FFFFFF, 1, dp(7)));
                for (int k = 0; k < catBtns.size(); k++) {
                    catBtns.get(k).setBackground(makeRoundBox(k == curCat[0] ? curTheme.btnBlue : curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                }
                taskAdapter.notifyDataSetChanged();
            }
        });

        loadTasksCatalog(ctx, false);
        taskStateRefreshHook.run();
        registerFeature("任务管理", "任务列表与完成状态管理", "列出全剧情/支线/子任务及条件，原生函数设置已完成/未完成", "任务 剧情 支线 触发 完成 重置 条件 quest task", 10, wrap, null, null);
        return wrap;
    }

    // =========================================================================
    //  TAB 11: 娱乐功能 (留声机本地音乐 + 电子斗蛐蛐 + 全图敌对随机生成)
    // =========================================================================
    private FrameLayout buildEntertainmentPage() {
        FrameLayout wrap = new FrameLayout(ctx);
        svEnt = new ScrollView(ctx);
        svEnt.setVerticalScrollBarEnabled(true);
        LinearLayout col = new LinearLayout(ctx);
        col.setOrientation(LinearLayout.VERTICAL);
        svEnt.addView(col);
        wrap.addView(svEnt, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        tvEntStatus = new TextView(ctx);
        tvEntStatus.setText("娱乐功能: 留声机播放本地MP3、电子斗蛐蛐死锁对决、全图敌对均等随机生成");
        tvEntStatus.setTextColor(curTheme.accent);
        tvEntStatus.setTextSize(11f);
        tvEntStatus.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(7)));
        tvEntStatus.setPadding(dp(10), dp(5), dp(10), dp(5));
        tvEntStatus.setSingleLine(true);
        tvEntStatus.setEllipsize(TextUtils.TruncateAt.END);
        LinearLayout.LayoutParams stLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        stLp.setMargins(0, 0, 0, dp(6));
        col.addView(tvEntStatus, stLp);

        // 1. Phonograph local MP3 (moved cleanly from 保护辅助)
        final Switch[] swHolderPhono = new Switch[1];
        LinearLayout rPhono = makeSwitchRow("留声机播放本地音乐", "家中留声机扫描播放 /sdcard/Music 或 /sdcard/AresMod/Music 的MP3", flagPhono, swHolderPhono, new CompoundButton.OnCheckedChangeListener() {
            @Override public void onCheckedChanged(CompoundButton b, boolean isOn) {
                flagPhono = isOn;
                Preferences.changeFeatureBool("留声机播放本地音乐", 83, flagPhono);
                Menu.NativeQuery(10, 10, flagPhono ? 1 : 0, 0L, "");
                Toast.makeText(ctx, flagPhono ? "已开启留声机播放本地MP3 (/sdcard/Music 或 /sdcard/AresMod/Music)" : "已恢复原版留声机音乐", Toast.LENGTH_SHORT).show();
            }
        });
        swPhono = swHolderPhono[0];
        col.addView(rPhono);
        registerFeature("娱乐功能", "留声机播放本地音乐", "播放本地目录MP3歌曲", "留声机 音乐 mp3 播放 娱乐", 11, rPhono, svEnt, null);

        // 2. Cyber Cricket Duel Card (电子斗蛐蛐)
        final int[] duelIdA = new int[] { 1510 };
        final int[] duelIdB = new int[] { 1520 };
        final String[] duelNameA = new String[] { "蝎子 (#1510)" };
        final String[] duelNameB = new String[] { "螳螂 (#1520)" };

        final LinearLayout cardCricket = new LinearLayout(ctx);
        cardCricket.setOrientation(LinearLayout.VERTICAL);
        cardCricket.setPadding(dp(12), dp(9), dp(12), dp(9));
        cardCricket.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(10)));
        LinearLayout.LayoutParams ckLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        ckLp.setMargins(0, 0, 0, dp(6));

        final TextView tvCkT = new TextView(ctx);
        tvCkT.setText("电子斗蛐蛐 (双实体互殴死锁仇恨)");
        tvCkT.setTextColor(curTheme.textPri);
        tvCkT.setTextSize(12.5f);
        tvCkT.setTypeface(null, Typeface.BOLD);
        cardCricket.addView(tvCkT);

        final TextView tvCkSub = new TextView(ctx);
        tvCkSub.setText("自由搜索并选择任意两个实体（无分类限制）；生成后强行互设敌对且视玩家为友军，永久互锁仇恨，玩家靠近或攻击也不会转移目标。");
        tvCkSub.setTextColor(curTheme.textSec);
        tvCkSub.setTextSize(10.2f);
        tvCkSub.setPadding(0, dp(2), 0, dp(6));
        cardCricket.addView(tvCkSub);

        LinearLayout pickRow = new LinearLayout(ctx);
        pickRow.setOrientation(LinearLayout.HORIZONTAL);
        pickRow.setGravity(Gravity.CENTER_VERTICAL);

        final Button btnPickA = makePillBtn("蓝方 #1: " + duelNameA[0], curTheme.btnBlue, 30);
        btnPickA.setTextSize(10.5f);
        LinearLayout.LayoutParams paLp = new LinearLayout.LayoutParams(0, dp(32), 1f);
        paLp.setMargins(0, 0, dp(6), 0);
        pickRow.addView(btnPickA, paLp);

        final Button btnPickB = makePillBtn("红方 #2: " + duelNameB[0], curTheme.btnRed, 30);
        btnPickB.setTextSize(10.5f);
        LinearLayout.LayoutParams pbLp = new LinearLayout.LayoutParams(0, dp(32), 1f);
        pickRow.addView(btnPickB, pbLp);
        cardCricket.addView(pickRow);

        btnPickA.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                showUnitPickerModal("选择 斗蛐蛐·蓝方实体 #1", new UnitPickCallback() {
                    @Override public void onPicked(SpawnUnitInfo u) {
                        duelIdA[0] = u.id;
                        duelNameA[0] = u.name + " (#" + u.id + ")";
                        btnPickA.setText("蓝方 #1: " + duelNameA[0]);
                    }
                });
            }
        });
        btnPickB.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                showUnitPickerModal("选择 斗蛐蛐·红方实体 #2", new UnitPickCallback() {
                    @Override public void onPicked(SpawnUnitInfo u) {
                        duelIdB[0] = u.id;
                        duelNameB[0] = u.name + " (#" + u.id + ")";
                        btnPickB.setText("红方 #2: " + duelNameB[0]);
                    }
                });
            }
        });

        LinearLayout duelBtnRow = new LinearLayout(ctx);
        duelBtnRow.setOrientation(LinearLayout.HORIZONTAL);
        duelBtnRow.setGravity(Gravity.CENTER_VERTICAL);
        LinearLayout.LayoutParams dbLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        dbLp.setMargins(0, dp(6), 0, dp(4));

        final Button btnStartDuel = makePillBtn("开战! 在附近生成斗蛐蛐", curTheme.btnGreen, 30);
        btnStartDuel.setTextSize(10.8f);
        btnStartDuel.setLayoutParams(new LinearLayout.LayoutParams(0, dp(31), 1f));
        btnStartDuel.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                Menu.NativeQuery(24, duelIdA[0], duelIdB[0], 1L, "");
                Toast.makeText(ctx, "斗蛐蛐开战: " + duelNameA[0] + " VS " + duelNameB[0], Toast.LENGTH_SHORT).show();
            }
        });
        duelBtnRow.addView(btnStartDuel);

        final Button btnClearDuel = makePillBtn("清除擂台", curTheme.btnSlate, 30);
        btnClearDuel.setTextSize(10.2f);
        btnClearDuel.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                Menu.NativeQuery(24, 0, 0, 2L, "");
                Toast.makeText(ctx, "已清除斗蛐蛐擂台", Toast.LENGTH_SHORT).show();
            }
        });
        duelBtnRow.addView(btnClearDuel);
        cardCricket.addView(duelBtnRow, dbLp);

        tvCricketState = new TextView(ctx);
        tvCricketState.setText("当前战况: 请选择两位实体后点击开战");
        tvCricketState.setTextColor(curTheme.accent);
        tvCricketState.setTextSize(10.2f);
        cardCricket.addView(tvCricketState);

        col.addView(cardCricket, ckLp);
        registerFeature("娱乐功能", "电子斗蛐蛐 (互殴死锁仇恨)", "全实体自由搜索选择两人互相对决，忽略玩家攻击", "斗蛐蛐 电子斗蛐蛐 互殴 对战 仇恨 锁定 duel", 11, cardCricket, svEnt, null);

        // 3. Random Enemy Scrambler Card (随机生成)
        cardRandomSpawn = new LinearLayout(ctx);
        cardRandomSpawn.setOrientation(LinearLayout.VERTICAL);
        cardRandomSpawn.setPadding(dp(12), dp(9), dp(12), dp(9));
        cardRandomSpawn.setBackground(makeRoundBox(flagRandomSpawn ? curTheme.cardOnBg : curTheme.cardBg, flagRandomSpawn ? curTheme.cardOnEdge : curTheme.cardEdge, dp(1), dp(10)));
        LinearLayout.LayoutParams rsLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        rsLp.setMargins(0, 0, 0, dp(6));

        LinearLayout rsTop = new LinearLayout(ctx);
        rsTop.setOrientation(LinearLayout.HORIZONTAL);
        rsTop.setGravity(Gravity.CENTER_VERTICAL);

        LinearLayout rsCol = new LinearLayout(ctx);
        rsCol.setOrientation(LinearLayout.VERTICAL);
        rsCol.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        final TextView tvRsT = new TextView(ctx);
        tvRsT.setText("随机生成 (全图敌对生物均等概率打乱)");
        tvRsT.setTextColor(curTheme.textPri);
        tvRsT.setTextSize(12.5f);
        tvRsT.setTypeface(null, Typeface.BOLD);
        rsCol.addView(tvRsT);

        final TextView tvRsSub = new TextView(ctx);
        tvRsSub.setText("开启后，所有地图的敌对生物刷新均从全游戏跨地图敌对生物池中以完全均等概率随机生成（保留波次触发与友军NPC）。");
        tvRsSub.setTextColor(curTheme.textSec);
        tvRsSub.setTextSize(10.2f);
        tvRsSub.setPadding(0, dp(2), dp(6), 0);
        rsCol.addView(tvRsSub);
        rsTop.addView(rsCol);

        swRandomSpawn = new Switch(ctx);
        swRandomSpawn.setChecked(flagRandomSpawn);
        swRandomSpawn.setOnCheckedChangeListener(new CompoundButton.OnCheckedChangeListener() {
            @Override public void onCheckedChanged(CompoundButton b, boolean isOn) {
                if (suppressToggleCallbacks) return;
                flagRandomSpawn = isOn;
                Preferences.changeFeatureBool("随机生成敌对生物", 129, flagRandomSpawn);
                Menu.NativeQuery(10, 21, flagRandomSpawn ? 1 : 0, 0L, "");
                syncToggleCardStyles();
                Toast.makeText(ctx, flagRandomSpawn ? "已开启全图敌对生物均等随机生成!" : "已关闭随机生成 (新加载地图恢复原样)", Toast.LENGTH_SHORT).show();
            }
        });
        rsTop.addView(swRandomSpawn);
        cardRandomSpawn.addView(rsTop);

        tvRandomSpawnState = new TextView(ctx);
        tvRandomSpawnState.setText(flagRandomSpawn ? "● 全图敌对生物随机打乱生效中" : "○ 未开启 (保持原图刷怪)");
        tvRandomSpawnState.setTextColor(flagRandomSpawn ? curTheme.emerald : curTheme.textSec);
        tvRandomSpawnState.setTextSize(10.2f);
        tvRandomSpawnState.setPadding(0, dp(4), 0, 0);
        cardRandomSpawn.addView(tvRandomSpawnState);

        col.addView(cardRandomSpawn, rsLp);
        registerFeature("娱乐功能", "随机生成 (全图敌对生物均等打乱)", "全地图敌对怪物按均等概率跨地图随机替换生成", "随机生成 乱斗 刷怪 随机 打乱 怪物 random", 11, cardRandomSpawn, svEnt, null);

        themeRefreshHooks.add(new Runnable() {
            @Override public void run() {
                tvEntStatus.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(7)));
                tvEntStatus.setTextColor(curTheme.accent);
                cardCricket.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(10)));
                tvCkT.setTextColor(curTheme.textPri);
                tvCkSub.setTextColor(curTheme.textSec);
                tvCricketState.setTextColor(curTheme.accent);
                btnPickA.setBackground(makeRoundBox(curTheme.btnBlue, 0x33FFFFFF, 1, dp(7)));
                btnPickB.setBackground(makeRoundBox(curTheme.btnRed, 0x33FFFFFF, 1, dp(7)));
                btnStartDuel.setBackground(makeRoundBox(curTheme.btnGreen, 0x33FFFFFF, 1, dp(7)));
                btnClearDuel.setBackground(makeRoundBox(curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
                tvRsT.setTextColor(curTheme.textPri);
                tvRsSub.setTextColor(curTheme.textSec);
                if (cardRandomSpawn != null) {
                    cardRandomSpawn.setBackground(makeRoundBox(flagRandomSpawn ? curTheme.cardOnBg : curTheme.cardBg, flagRandomSpawn ? curTheme.cardOnEdge : curTheme.cardEdge, dp(1), dp(10)));
                }
            }
        });

        return wrap;
    }

    // =========================================================================
    //  TAB 12: 保护辅助 (账号反作弊 + 云存档拦截 + 固定7891登录 + 解锁与便利功能)
    // =========================================================================
    private FrameLayout buildAssistSecurityPage() {
        FrameLayout wrap = new FrameLayout(ctx);
        svAssist = new ScrollView(ctx);
        svAssist.setVerticalScrollBarEnabled(true);
        LinearLayout col = new LinearLayout(ctx);
        col.setOrientation(LinearLayout.VERTICAL);
        svAssist.addView(col);
        wrap.addView(svAssist, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        tvOtherStatus = new TextView(ctx);
        tvOtherStatus.setText("保护辅助: 账号防封、云存档拦截、7891登录兼容、地图探索与存档管理");
        tvOtherStatus.setTextColor(curTheme.accent);
        tvOtherStatus.setTextSize(11);
        tvOtherStatus.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(7)));
        tvOtherStatus.setPadding(dp(10), dp(5), dp(10), dp(5));
        tvOtherStatus.setSingleLine(true);
        tvOtherStatus.setEllipsize(TextUtils.TruncateAt.END);
        LinearLayout.LayoutParams stLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        stLp.setMargins(0, 0, 0, dp(6));
        col.addView(tvOtherStatus, stLp);

        final List<TextView> secHdrs = new ArrayList<TextView>();
        final TextView hdrSec = new TextView(ctx);
        hdrSec.setText("— 账号安全与固定 7891 登录 —");
        hdrSec.setTextColor(curTheme.accent);
        hdrSec.setTextSize(11.5f);
        hdrSec.setTypeface(null, Typeface.BOLD);
        hdrSec.setPadding(dp(2), dp(2), 0, dp(4));
        col.addView(hdrSec);
        secHdrs.add(hdrSec);

        // 1. Anti-cheat switch
        final Switch[] swHolderAC = new Switch[1];
        LinearLayout rAC = makeSwitchRow("全链路反作弊保护", "C#协议+底层反作弊拦截，防止封号、校验与异常上报 (推荐常开)", flagAC, swHolderAC, new CompoundButton.OnCheckedChangeListener() {
            @Override public void onCheckedChanged(CompoundButton b, boolean isOn) {
                flagAC = isOn;
                XposedEntry.antiCheatEnabled = flagAC;
                Preferences.changeFeatureBool("全代码拦截作弊检验/上报/封号", 60, flagAC);
                Menu.NativeQuery(10, 0, flagAC ? 1 : 0, 0L, "");
            }
        });
        swAC = swHolderAC[0];
        col.addView(rAC);
        registerFeature("保护辅助", "全链路反作弊保护", "拦截作弊检验、上报与封号检测", "反作弊 封号 防封 保护 anticheat", 12, rAC, svAssist, null);

        // 2. Block cloud save upload
        final Switch[] swHolderBlockCloud = new Switch[1];
        LinearLayout rBlockCloud = makeSwitchRow("拦截云存档上传", "反作弊核心项：100%拦截游园镇获取重要物品自动云存及所有手动云存 (推荐常开)", flagBlockCloud, swHolderBlockCloud, new CompoundButton.OnCheckedChangeListener() {
            @Override public void onCheckedChanged(CompoundButton b, boolean isOn) {
                flagBlockCloud = isOn;
                Preferences.changeFeatureBool("100%拦截云存档上传", 84, flagBlockCloud);
                Menu.NativeQuery(10, 11, flagBlockCloud ? 1 : 0, 0L, "");
            }
        });
        swBlockCloud = swHolderBlockCloud[0];
        col.addView(rBlockCloud);
        registerFeature("保护辅助", "拦截云存档上传", "阻止所有自动与手动云存档上传至官方服务器", "云存档 上传 拦截 游园镇", 12, rBlockCloud, svAssist, null);

        // 3. Fixed UserID = 7891 Login Card (NO custom input, full local save compatibility)
        cardLogin7891 = new LinearLayout(ctx);
        cardLogin7891.setOrientation(LinearLayout.VERTICAL);
        cardLogin7891.setPadding(dp(12), dp(9), dp(12), dp(9));
        cardLogin7891.setBackground(makeRoundBox(flagLogin7891 ? curTheme.cardOnBg : curTheme.cardBg, flagLogin7891 ? curTheme.cardOnEdge : curTheme.cardEdge, dp(1), dp(10)));
        LinearLayout.LayoutParams l78Lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        l78Lp.setMargins(0, 0, 0, dp(6));

        LinearLayout l78Top = new LinearLayout(ctx);
        l78Top.setOrientation(LinearLayout.HORIZONTAL);
        l78Top.setGravity(Gravity.CENTER_VERTICAL);

        LinearLayout l78Col = new LinearLayout(ctx);
        l78Col.setOrientation(LinearLayout.VERTICAL);
        l78Col.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        final TextView tv78T = new TextView(ctx);
        tv78T.setText("固定 UserID=7891 登录 (免账号 / 全本地存档兼容)");
        tv78T.setTextColor(curTheme.textPri);
        tv78T.setTextSize(12f);
        tv78T.setTypeface(null, Typeface.BOLD);
        l78Col.addView(tv78T);

        final TextView tv78Sub = new TextView(ctx);
        tv78Sub.setText("拦截登录与联机校验，自动解密重写 DBData.UserID 兼容任意本地存档读取");
        tv78Sub.setTextColor(curTheme.textSec);
        tv78Sub.setTextSize(10.2f);
        tv78Sub.setPadding(0, dp(2), dp(6), 0);
        l78Col.addView(tv78Sub);
        l78Top.addView(l78Col);

        swLogin7891 = new Switch(ctx);
        swLogin7891.setChecked(flagLogin7891);
        swLogin7891.setOnCheckedChangeListener(new CompoundButton.OnCheckedChangeListener() {
            @Override public void onCheckedChanged(CompoundButton b, boolean isOn) {
                if (suppressToggleCallbacks) return;
                flagLogin7891 = isOn;
                Preferences.changeFeatureBool("固定7891登录", 128, flagLogin7891);
                Menu.NativeQuery(10, 20, flagLogin7891 ? 1 : 0, 0L, "");
                cardLogin7891.setBackground(makeRoundBox(flagLogin7891 ? curTheme.cardOnBg : curTheme.cardBg, flagLogin7891 ? curTheme.cardOnEdge : curTheme.cardEdge, dp(1), dp(10)));
                mainH.postDelayed(new Runnable() {
                    @Override public void run() {
                        pollSnapshot();
                        if (loginStateRefreshHook != null) loginStateRefreshHook.run();
                    }
                }, 350);
                Toast.makeText(ctx, flagLogin7891 ? "已开启固定 UserID=7891 登录与跨账号本地存档兼容" : "已关闭固定7891登录 (恢复原账号并保持本地存档兼容)", Toast.LENGTH_SHORT).show();
            }
        });
        l78Top.addView(swLogin7891);
        cardLogin7891.addView(l78Top);

        LinearLayout l78Foot = new LinearLayout(ctx);
        l78Foot.setOrientation(LinearLayout.HORIZONTAL);
        l78Foot.setGravity(Gravity.CENTER_VERTICAL);
        l78Foot.setPadding(0, dp(5), 0, 0);

        tvLogin7891State = new TextView(ctx);
        tvLogin7891State.setText("状态: 等待登录模块同步...");
        tvLogin7891State.setTextColor(curTheme.accent);
        tvLogin7891State.setTextSize(10f);
        tvLogin7891State.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        l78Foot.addView(tvLogin7891State);

        final Button btnSyncSave7891 = makePillBtn("兼容导入最新本地存档", curTheme.btnBlue, 25);
        btnSyncSave7891.setTextSize(9.8f);
        btnSyncSave7891.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                Menu.NativeQuery(22, 1, 1, 0L, "");
                mainH.postDelayed(new Runnable() {
                    @Override public void run() {
                        pollSnapshot();
                        if (loginStateRefreshHook != null) loginStateRefreshHook.run();
                    }
                }, 350);
                Toast.makeText(ctx, "已执行全本地存档 UID 兼容迁移与校验", Toast.LENGTH_SHORT).show();
            }
        });
        l78Foot.addView(btnSyncSave7891);
        cardLogin7891.addView(l78Foot);
        col.addView(cardLogin7891, l78Lp);
        registerFeature("保护辅助", "固定 UserID=7891 登录", "固定以7891账号登录并跨UID兼容读取任何本地存档", "7891 userid 登录 账号 存档 兼容 login", 12, cardLogin7891, svAssist, null);

        loginStateRefreshHook = new Runnable() {
            @Override public void run() {
                try {
                    String raw = readFileText(new File(ctx.getFilesDir(), "dyn_login_state.json"));
                    if (raw != null && raw.startsWith("{") && tvLogin7891State != null) {
                        JSONObject o = new JSONObject(raw);
                        long uid = o.optLong("uid", 0L);
                        long ouid = o.optLong("origUid", 0L);
                        String db = o.optString("activeDb", "");
                        int ex = o.optInt("dbExists", 0);
                        tvLogin7891State.setText("当前UID:" + uid + (ouid > 0 && ouid != uid ? (" (原:" + ouid + ")") : "") + " · 存档:" + db + (ex != 0 ? " ✓" : " (待生成)"));
                    }
                } catch (Throwable ignored) {}
            }
        };
        loginStateRefreshHook.run();

        final TextView hdrWorld = new TextView(ctx);
        hdrWorld.setText("— 解锁内容与地图探索便利 —");
        hdrWorld.setTextColor(curTheme.accent);
        hdrWorld.setTextSize(11.5f);
        hdrWorld.setTypeface(null, Typeface.BOLD);
        hdrWorld.setPadding(dp(2), dp(4), 0, dp(4));
        col.addView(hdrWorld);
        secHdrs.add(hdrWorld);

        // 4. Paid unlock
        final Switch[] swHolderPaid = new Switch[1];
        LinearLayout rPaid = makeSwitchRow("一键解锁全部付费道具", "猎人小屋/哈士奇/猪王/商城与DLC扩展内容", flagPaid, swHolderPaid, new CompoundButton.OnCheckedChangeListener() {
            @Override public void onCheckedChanged(CompoundButton b, boolean isOn) {
                flagPaid = isOn;
                Preferences.changeFeatureBool("一键解锁全部付费道具", 70, flagPaid);
                Menu.NativeQuery(10, 3, flagPaid ? 1 : 0, 0L, "");
                Toast.makeText(ctx, flagPaid ? "已本地开启全付费道具解锁" : "已恢复原始付费状态", Toast.LENGTH_SHORT).show();
            }
        });
        swPaid = swHolderPaid[0];
        col.addView(rPaid);
        registerFeature("保护辅助", "一键解锁全部付费道具", "解锁DLC、猎人小屋、哈士奇、猪王及内购物品", "付费 内购 免费 dlc 猎人 哈士奇 猪王", 12, rPaid, svAssist, null);

        // 5. Blueprints button row
        LinearLayout rBp = new LinearLayout(ctx);
        rBp.setOrientation(LinearLayout.HORIZONTAL);
        rBp.setGravity(Gravity.CENTER_VERTICAL);
        rBp.setPadding(dp(12), dp(8), dp(12), dp(8));
        rBp.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(8)));
        LinearLayout.LayoutParams bpLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        bpLp.setMargins(0, 0, 0, dp(5));
        rBp.setLayoutParams(bpLp);

        LinearLayout bpCol = new LinearLayout(ctx);
        bpCol.setOrientation(LinearLayout.VERTICAL);
        bpCol.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        TextView tvBpT = new TextView(ctx);
        tvBpT.setText("一键解锁全部蓝图");
        tvBpT.setTextSize(12f);
        tvBpT.setTextColor(curTheme.textPri);
        tvBpT.setTypeface(null, Typeface.BOLD);
        bpCol.addView(tvBpT);
        TextView tvBpS = new TextView(ctx);
        tvBpS.setText("解锁全部 65+ 工作台/武器/防具/建筑蓝图");
        tvBpS.setTextSize(10.5f);
        tvBpS.setTextColor(curTheme.textSec);
        tvBpS.setPadding(0, dp(2), 0, 0);
        bpCol.addView(tvBpS);
        rBp.addView(bpCol);

        final Button btnBpGo = makePillBtn("立即解锁全部蓝图", curTheme.btnGreen, 26);
        btnBpGo.setTextSize(10);
        btnBpGo.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                Menu.NativeQuery(6, 0, 0, 0L, "");
                mainH.postDelayed(new Runnable() { @Override public void run() { pollSnapshot(); } }, 220);
                Toast.makeText(ctx, "已静默解锁全部蓝图", Toast.LENGTH_SHORT).show();
            }
        });
        rBp.addView(btnBpGo);
        otherFeatureRows.add(rBp);
        otherFeatureTitles.add(tvBpT);
        otherFeatureSubs.add(tvBpS);
        col.addView(rBp);
        registerFeature("保护辅助", "一键解锁全部蓝图", "立刻解锁所有工作台/装备/建筑蓝图", "蓝图 配方 制作 解锁 blueprint", 12, rBp, svAssist, null);

        // 6. Noclip
        final Switch[] swHolderNoclip = new Switch[1];
        LinearLayout rNoclip = makeSwitchRow("穿墙模式", "关闭主角实体碰撞箱，自由穿行任何障碍与地图", flagNoclip, swHolderNoclip, new CompoundButton.OnCheckedChangeListener() {
            @Override public void onCheckedChanged(CompoundButton b, boolean isOn) {
                flagNoclip = isOn;
                Preferences.changeFeatureBool("穿墙模式", 73, flagNoclip);
                Menu.NativeQuery(10, 6, flagNoclip ? 1 : 0, 0L, "");
            }
        });
        swNoclip = swHolderNoclip[0];
        col.addView(rNoclip);
        registerFeature("保护辅助", "穿墙模式", "关闭碰撞体自由穿过墙壁与障碍物", "穿墙 碰撞 飞天 noclip", 12, rNoclip, svAssist, null);

        // 7. Instant interact
        final Switch[] swHolderInst = new Switch[1];
        LinearLayout rInst = makeSwitchRow("关闭交互时间", "砍树/采矿/砍家具/采集/拾取/开箱/回家0秒瞬时交互无需读条", flagInstant, swHolderInst, new CompoundButton.OnCheckedChangeListener() {
            @Override public void onCheckedChanged(CompoundButton b, boolean isOn) {
                flagInstant = isOn;
                Preferences.changeFeatureBool("关闭交互时间", 71, flagInstant);
                Menu.NativeQuery(10, 4, flagInstant ? 1 : 0, 0L, "");
            }
        });
        swInstant = swHolderInst[0];
        col.addView(rInst);
        registerFeature("保护辅助", "关闭交互时间", "采集/开箱/砍树/回家 0 秒瞬时交互", "交互 秒开 读条 采集 砍树 挖矿", 12, rInst, svAssist, null);

        // 8. Sewer highlight
        final Switch[] swHolderSewer = new Switch[1];
        LinearLayout rSewer = makeSwitchRow("下水道/洞穴全图高亮", "下水道、洞穴等黑暗地图始终全图照明高亮", flagSewer, swHolderSewer, new CompoundButton.OnCheckedChangeListener() {
            @Override public void onCheckedChanged(CompoundButton b, boolean isOn) {
                flagSewer = isOn;
                Preferences.changeFeatureBool("下水道高亮", 81, flagSewer);
                Menu.NativeQuery(10, 8, flagSewer ? 1 : 0, 0L, "");
                Toast.makeText(ctx, flagSewer ? "已开启下水道/洞穴全图高亮" : "已关闭下水道高亮", Toast.LENGTH_SHORT).show();
            }
        });
        swSewer = swHolderSewer[0];
        col.addView(rSewer);
        registerFeature("保护辅助", "下水道/洞穴全图高亮", "黑暗场景全图常亮视野无死角", "下水道 高亮 夜视 照明 洞穴", 12, rSewer, svAssist, null);

        // 9. Always event
        final Switch[] swHolderEvent = new Switch[1];
        LinearLayout rEvent = makeSwitchRow("限时活动全开放", "周末/节日/限时开放的活动玩法与商人常驻开放", flagEvent, swHolderEvent, new CompoundButton.OnCheckedChangeListener() {
            @Override public void onCheckedChanged(CompoundButton b, boolean isOn) {
                flagEvent = isOn;
                Preferences.changeFeatureBool("限时活动开放", 82, flagEvent);
                Menu.NativeQuery(10, 9, flagEvent ? 1 : 0, 0L, "");
                Toast.makeText(ctx, flagEvent ? "已开启限时活动全开放" : "已恢复原版限时活动开放规则", Toast.LENGTH_SHORT).show();
            }
        });
        swEvent = swHolderEvent[0];
        col.addView(rEvent);
        registerFeature("保护辅助", "限时活动全开放", "周末及限时节日玩法与商人常驻", "活动 周末 节日 开放", 12, rEvent, svAssist, null);

        final TextView hdrMisc = new TextView(ctx);
        hdrMisc.setText("— 仓库存储与配置持久化 —");
        hdrMisc.setTextColor(curTheme.accent);
        hdrMisc.setTextSize(11.5f);
        hdrMisc.setTypeface(null, Typeface.BOLD);
        hdrMisc.setPadding(dp(2), dp(4), 0, dp(4));
        col.addView(hdrMisc);
        secHdrs.add(hdrMisc);

        // 10. Infinite stack
        final Switch[] swHolderInfStk = new Switch[1];
        LinearLayout rInfStk = makeSwitchRow("仓库无限堆叠", "仅仓库生效，且仅限相同NBT/耐久度物品无限堆叠", flagInfStack, swHolderInfStk, new CompoundButton.OnCheckedChangeListener() {
            @Override public void onCheckedChanged(CompoundButton b, boolean isOn) {
                flagInfStack = isOn;
                Preferences.changeFeatureBool("无限堆叠(仓库)", 72, flagInfStack);
                Menu.NativeQuery(10, 5, flagInfStack ? 1 : 0, 0L, "");
            }
        });
        swInfStack = swHolderInfStk[0];
        col.addView(rInfStk);
        registerFeature("保护辅助", "仓库无限堆叠", "仓库物品同NBT/耐久突破上限无限堆叠", "仓库 堆叠 无限 stack", 12, rInfStk, svAssist, null);

        // 11. Saved Props row
        final Switch[] swHolderProps = new Switch[1];
        LinearLayout rProps = makeSwitchRow("属性配置持久化", "进存档自动加载已保存的数值与锁定状态", flagAutoProps, swHolderProps, new CompoundButton.OnCheckedChangeListener() {
            @Override public void onCheckedChanged(CompoundButton b, boolean isOn) {
                flagAutoProps = isOn;
                modPrefs.edit().putBoolean("auto_restore_props", flagAutoProps).apply();
                Toast.makeText(ctx, flagAutoProps ? "下次进游戏将自动加载属性配置" : "已暂停下次进游戏自动加载", Toast.LENGTH_SHORT).show();
            }
        });
        swAutoProps = swHolderProps[0];

        final Button btnSaveProps = makePillBtn("保存配置", curTheme.btnGreen, 26);
        btnSaveProps.setTextSize(10);
        btnSaveProps.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                saveCurrentPropsConfig();
                Toast.makeText(ctx, "已保存当前 " + savedPropVals.size() + " 项属性配置", Toast.LENGTH_SHORT).show();
            }
        });

        final Button btnClearProps = makePillBtn("删除已存配置", curTheme.btnRed, 26);
        btnClearProps.setTextSize(10);
        btnClearProps.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                clearSavedPropsConfig();
                Toast.makeText(ctx, "已删除当前保存的属性配置", Toast.LENGTH_SHORT).show();
            }
        });

        rProps.addView(btnSaveProps, rProps.getChildCount() - 1);
        rProps.addView(btnClearProps, rProps.getChildCount() - 1);
        col.addView(rProps);
        registerFeature("保护辅助", "属性配置持久化", "进存档自动恢复已保存属性与锁定配置", "属性 保存 持久化 自动恢复", 12, rProps, svAssist, null);

        themeRefreshHooks.add(new Runnable() {
            @Override public void run() {
                for (View r : otherFeatureRows) r.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(8)));
                for (TextView t : otherFeatureTitles) t.setTextColor(curTheme.textPri);
                for (TextView s : otherFeatureSubs) s.setTextColor(curTheme.textSec);
                for (TextView h : secHdrs) h.setTextColor(curTheme.accent);
                tv78T.setTextColor(curTheme.textPri);
                tv78Sub.setTextColor(curTheme.textSec);
                if (tvLogin7891State != null) tvLogin7891State.setTextColor(curTheme.accent);
                btnSyncSave7891.setBackground(makeRoundBox(curTheme.btnBlue, 0x33FFFFFF, 1, dp(7)));
                if (cardLogin7891 != null) cardLogin7891.setBackground(makeRoundBox(flagLogin7891 ? curTheme.cardOnBg : curTheme.cardBg, flagLogin7891 ? curTheme.cardOnEdge : curTheme.cardEdge, dp(1), dp(10)));
                btnBpGo.setBackground(makeRoundBox(curTheme.btnGreen, 0x33FFFFFF, 1, dp(7)));
                btnSaveProps.setBackground(makeRoundBox(curTheme.btnGreen, 0x33FFFFFF, 1, dp(7)));
                btnClearProps.setBackground(makeRoundBox(curTheme.btnRed, 0x33FFFFFF, 1, dp(7)));
            }
        });

        return wrap;
    }

    // =========================================================================
    //  顶部全局模糊搜索浮层 + 跳转高亮引擎
    // =========================================================================
    private int scoreFuzzyQuery(String qRaw, FeatureSearchEntry e) {
        if (qRaw == null) return 0;
        String q = qRaw.trim().toLowerCase(Locale.US);
        if (q.isEmpty()) return 0;
        String t = e.title.toLowerCase(Locale.US);
        String d = e.desc.toLowerCase(Locale.US);
        String k = e.keywords.toLowerCase(Locale.US);
        String c = e.category.toLowerCase(Locale.US);

        if (t.equals(q)) return 300;
        if (t.startsWith(q)) return 240;
        if (t.contains(q)) return 180;
        if (k.contains(q)) return 145;
        if (c.contains(q)) return 120;
        if (d.contains(q)) return 100;

        String[] tokens = q.split("\\s+");
        if (tokens.length > 1) {
            String hay = c + " " + t + " " + k + " " + d;
            boolean allOk = true;
            for (String tok : tokens) {
                if (!tok.isEmpty() && !hay.contains(tok)) { allOk = false; break; }
            }
            if (allOk) return 110;
        }

        // Character-order fuzzy subsequence match
        String compact = q.replace(" ", "");
        String hay = t + " " + k + " " + c;
        int pos = -1;
        int span = 0;
        for (int i = 0; i < compact.length(); i++) {
            char ch = compact.charAt(i);
            int idx = hay.indexOf(ch, pos + 1);
            if (idx < 0) return 0;
            if (pos >= 0) span += (idx - pos);
            pos = idx;
        }
        return Math.max(20, 90 - Math.min(60, span));
    }

    private void jumpToSearchEntry(final FeatureSearchEntry e) {
        try {
            InputMethodManager imm = (InputMethodManager) ctx.getSystemService(Context.INPUT_METHOD_SERVICE);
            if (imm != null && etGlobalSearch != null) {
                imm.hideSoftInputFromWindow(etGlobalSearch.getWindowToken(), 0);
            }
        } catch (Throwable ignored) {}
        if (globalSearchOverlay != null) globalSearchOverlay.setVisibility(View.GONE);
        switchTab(e.tabIdx);
        if (e.subAction != null) {
            try { e.subAction.run(); } catch (Throwable ignored) {}
        }
        if (e.scrollView != null && e.targetView != null) {
            mainH.postDelayed(new Runnable() {
                @Override public void run() {
                    try {
                        int top = 0;
                        View v = e.targetView;
                        while (v != null && v != e.scrollView && v.getParent() instanceof View) {
                            top += v.getTop();
                            v = (View) v.getParent();
                        }
                        e.scrollView.smoothScrollTo(0, Math.max(0, top - dp(24)));
                    } catch (Throwable ignored) {}
                }
            }, 60);
        }
        if (e.targetView != null) {
            final View hv = e.targetView;
            hv.setAlpha(0.35f);
            mainH.postDelayed(new Runnable() { @Override public void run() { hv.setAlpha(1f); } }, 160);
            mainH.postDelayed(new Runnable() { @Override public void run() { hv.setAlpha(0.45f); } }, 320);
            mainH.postDelayed(new Runnable() { @Override public void run() { hv.setAlpha(1f); } }, 500);
        }
    }

    private void buildGlobalSearchOverlay(FrameLayout contentWrap) {
        globalSearchOverlay = new FrameLayout(ctx);
        globalSearchOverlay.setBackgroundColor(0x77000000);
        globalSearchOverlay.setVisibility(View.GONE);
        globalSearchOverlay.setClickable(true);
        globalSearchOverlay.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { globalSearchOverlay.setVisibility(View.GONE); }
        });

        globalSearchCard = new LinearLayout(ctx);
        globalSearchCard.setOrientation(LinearLayout.VERTICAL);
        globalSearchCard.setClickable(true);
        globalSearchCard.setPadding(dp(10), dp(8), dp(10), dp(8));
        globalSearchCard.setBackground(makeRoundBox(curTheme.panelBg | 0xFF000000, curTheme.accent, dp(2), dp(10)));
        FrameLayout.LayoutParams cardLp = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(290), Gravity.TOP);
        cardLp.setMargins(dp(10), dp(4), dp(10), 0);

        final TextView tvHint = new TextView(ctx);
        tvHint.setText("模糊搜索全部功能 / 地图 / 任务（点击直接跳转定位）");
        tvHint.setTextColor(curTheme.accent);
        tvHint.setTextSize(11f);
        tvHint.setTypeface(null, Typeface.BOLD);
        tvHint.setPadding(0, 0, 0, dp(4));
        globalSearchCard.addView(tvHint);

        lvGlobalSearch = new ListView(ctx);
        lvGlobalSearch.setDividerHeight(dp(4));
        lvGlobalSearch.setLayoutParams(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        globalSearchCard.addView(lvGlobalSearch);

        globalSearchOverlay.addView(globalSearchCard, cardLp);
        contentWrap.addView(globalSearchOverlay, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        final List<FeatureSearchEntry> results = new ArrayList<FeatureSearchEntry>();
        final BaseAdapter gsAdapter = new BaseAdapter() {
            @Override public int getCount() { return results.size(); }
            @Override public Object getItem(int pos) { return results.get(pos); }
            @Override public long getItemId(int pos) { return pos; }
            @Override public View getView(int pos, View convertView, ViewGroup parent) {
                final FeatureSearchEntry e = results.get(pos);
                LinearLayout row = new LinearLayout(ctx);
                row.setOrientation(LinearLayout.HORIZONTAL);
                row.setGravity(Gravity.CENTER_VERTICAL);
                row.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(7)));
                row.setPadding(dp(9), dp(6), dp(9), dp(6));

                LinearLayout lc = new LinearLayout(ctx);
                lc.setOrientation(LinearLayout.VERTICAL);
                lc.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
                TextView t1 = new TextView(ctx);
                t1.setText("[" + e.category + "] " + e.title);
                t1.setTextColor(curTheme.textPri);
                t1.setTextSize(11.8f);
                t1.setTypeface(null, Typeface.BOLD);
                lc.addView(t1);
                if (!e.desc.isEmpty()) {
                    TextView t2 = new TextView(ctx);
                    t2.setText(e.desc);
                    t2.setTextColor(curTheme.textSec);
                    t2.setTextSize(10f);
                    t2.setSingleLine(true);
                    t2.setEllipsize(TextUtils.TruncateAt.END);
                    lc.addView(t2);
                }
                row.addView(lc);

                TextView tGo = new TextView(ctx);
                tGo.setText("跳转 ▸");
                tGo.setTextColor(curTheme.accent);
                tGo.setTextSize(11f);
                tGo.setTypeface(null, Typeface.BOLD);
                row.addView(tGo);

                row.setOnClickListener(new View.OnClickListener() {
                    @Override public void onClick(View v) { jumpToSearchEntry(e); }
                });
                return row;
            }
        };
        lvGlobalSearch.setAdapter(gsAdapter);

        final Runnable updateSearch = new Runnable() {
            @Override public void run() {
                String q = etGlobalSearch != null && etGlobalSearch.getText() != null ? etGlobalSearch.getText().toString().trim() : "";
                if (btnClearGlobalSearch != null) btnClearGlobalSearch.setVisibility(q.isEmpty() ? View.GONE : View.VISIBLE);
                if (q.isEmpty()) {
                    globalSearchOverlay.setVisibility(View.GONE);
                    return;
                }
                results.clear();
                for (FeatureSearchEntry e : searchRegistry) {
                    int sc = scoreFuzzyQuery(q, e);
                    if (sc > 0) {
                        FeatureSearchEntry copy = new FeatureSearchEntry();
                        copy.title = e.title; copy.desc = e.desc; copy.keywords = e.keywords;
                        copy.category = e.category; copy.tabIdx = e.tabIdx;
                        copy.targetView = e.targetView; copy.scrollView = e.scrollView;
                        copy.subAction = e.subAction; copy.score = sc;
                        results.add(copy);
                    }
                }
                String ql = q.toLowerCase(Locale.US);
                if (sMapsCache != null) {
                    int mCnt = 0;
                    for (final MapEntry m : sMapsCache) {
                        if (!m.valid) continue;
                        if (m.name.toLowerCase(Locale.US).contains(ql) || String.valueOf(m.id).equals(q)) {
                            FeatureSearchEntry me = new FeatureSearchEntry();
                            me.category = "地图传送";
                            me.title = m.name + " (#" + m.id + ")";
                            me.desc = (m.cat >= 1 && m.cat < CHAPTER_NAMES.length ? CHAPTER_NAMES[m.cat] : "") + " · 定位到地图传送并筛查";
                            me.tabIdx = 8;
                            me.score = 135;
                            me.subAction = new Runnable() {
                                @Override public void run() {
                                    if (etMapSearchBox != null) etMapSearchBox.setText(m.name);
                                }
                            };
                            results.add(me);
                            if (++mCnt >= 6) break;
                        }
                    }
                }
                if (sTasksCache != null) {
                    int tCnt = 0;
                    for (final TaskEntry t : sTasksCache) {
                        if (t.name.toLowerCase(Locale.US).contains(ql) || t.sceneName.toLowerCase(Locale.US).contains(ql) || String.valueOf(t.id).equals(q)) {
                            FeatureSearchEntry te = new FeatureSearchEntry();
                            te.category = "任务管理";
                            te.title = t.name + " (#" + t.id + ")";
                            te.desc = "归属:" + t.sceneName + (t.finishTxt.isEmpty() ? "" : (" · " + t.finishTxt));
                            te.tabIdx = 9;
                            te.score = 130;
                            te.subAction = new Runnable() {
                                @Override public void run() {
                                    if (etTaskSearchBox != null) etTaskSearchBox.setText(t.name);
                                }
                            };
                            results.add(te);
                            if (++tCnt >= 6) break;
                        }
                    }
                }
                Collections.sort(results, new Comparator<FeatureSearchEntry>() {
                    @Override public int compare(FeatureSearchEntry a, FeatureSearchEntry b) { return b.score - a.score; }
                });
                tvHint.setText(results.isEmpty()
                    ? ("未找到匹配 \"" + q + "\" 的功能 / 地图 / 任务")
                    : ("找到 " + results.size() + " 个匹配结果（点击立即跳转定位）"));
                globalSearchOverlay.setVisibility(View.VISIBLE);
                globalSearchOverlay.bringToFront();
                gsAdapter.notifyDataSetChanged();
            }
        };

        if (etGlobalSearch != null) {
            etGlobalSearch.addTextChangedListener(new TextWatcher() {
                @Override public void beforeTextChanged(CharSequence s, int a, int b, int c) {}
                @Override public void onTextChanged(CharSequence s, int a, int b, int c) { updateSearch.run(); }
                @Override public void afterTextChanged(Editable s) {}
            });
            etGlobalSearch.setOnFocusChangeListener(new View.OnFocusChangeListener() {
                @Override public void onFocusChange(View v, boolean hasFocus) {
                    if (hasFocus && etGlobalSearch.getText() != null && etGlobalSearch.getText().length() > 0) {
                        updateSearch.run();
                    }
                }
            });
        }
        if (btnClearGlobalSearch != null) {
            btnClearGlobalSearch.setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) {
                    if (etGlobalSearch != null) etGlobalSearch.setText("");
                    globalSearchOverlay.setVisibility(View.GONE);
                }
            });
        }
    }

    private void registerExtraGlobalSearchEntries() {
        registerFeature("物品发放", "物品添加与批量发送", "搜索全游戏物品、武器、防具、蓝图并发送至邮箱或背包", "发物品 刷物品 武器 装备 道具 邮箱 背包", 0, pages[0], null, null);
        registerFeature("物品删除", "仓库物品清理与销毁", "按数量或一键清除指定仓库储物箱多余物品", "删物品 清理 仓库 储物箱 销毁", 1, pages[1], null, null);
        registerFeature("货币修改", "银币 / 银券 / 黑市币 / 邮票", "直接设置与锁定各类游戏内货币数值", "钱 金币 银币 银券 黑市 邮票 货币", 2, pages[2], null, null);
        registerFeature("全部属性", "生命 / 攻击 / 攻速 / 移速 / 暴击", "逐项设置与锁定主角实时属性数值", "属性 血量 攻击 移速 攻速 无敌 秒杀 体力", 3, pages[3], null, null);
        registerFeature("Buff施加", "施加/清除状态Buff与光环", "一键叠加或移除游戏内各种正负面Buff状态", "buff 状态 增益 光环 无敌", 4, pages[4], null, null);
        registerFeature("天赋技能", "解锁与升级全角色天赋技能", "为主角或随从一键解锁满级天赋技能", "天赋 技能 加点 等级 满级", 5, pages[5], null, null);

        registerFeature("雇佣随从", "剧情随从招募", "虎哥、小哈士奇、强子、老吴、K、尼奥、姜娜等剧情角色招募", "随从 剧情 虎哥 哈士奇 强子 老吴 姜娜 尼奥", 7, pages[7], null, new Runnable() {
            @Override public void run() { if (hireSubTabBtns.size() > 0) hireSubTabBtns.get(0).performClick(); }
        });
        registerFeature("雇佣随从", "营地战士/工人", "招募或增员营地可工作采集与守卫单位", "营地 工人 战士 招募 矿工 伐木", 7, pages[7], null, new Runnable() {
            @Override public void run() { if (hireSubTabBtns.size() > 1) hireSubTabBtns.get(1).performClick(); }
        });
        registerFeature("雇佣随从", "强制雇佣 (全NPC/首领)", "无视阵营强制召唤游戏内任意NPC/首领/怪作为随从", "强雇 强制雇佣 首领 boss npc 召唤", 7, pages[7], null, new Runnable() {
            @Override public void run() { if (hireSubTabBtns.size() > 2) hireSubTabBtns.get(2).performClick(); }
        });
        registerFeature("雇佣随从", "现场收编 (扫描与撤回)", "可调距离扫描身边活体直接收编入队并支持撤回", "现场收编 扫描 收服 撤回 抓捕", 7, pages[7], null, new Runnable() {
            @Override public void run() { if (hireSubTabBtns.size() > 3) hireSubTabBtns.get(3).performClick(); }
        });

        registerFeature("主题风格", "控制台界面配色主题切换", "深海/赛博/翡翠/烈焰/极简浅色等主题切换", "主题 皮肤 颜色 外观 界面", 13, pages[13], null, null);
        registerFeature("诊断设置", "版本检查更新 / 配置重扫 / 运行诊断", "检查Mod更新、重扫游戏动态配表与缺失图标、重置悬浮球位置", "诊断 设置 更新 扫描 图标 悬浮球", 14, pages[14], null, null);
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

        final LinearLayout verCard = new LinearLayout(ctx);
        verCard.setOrientation(LinearLayout.VERTICAL);
        verCard.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(10)));
        verCard.setPadding(dp(12), dp(10), dp(12), dp(10));
        LinearLayout.LayoutParams vLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        vLp.setMargins(0, 0, 0, dp(8));

        final TextView tvVerTitle = new TextView(ctx);
        tvVerTitle.setText("Mod 版本与在线更新");
        tvVerTitle.setTextColor(curTheme.accent);
        tvVerTitle.setTextSize(13);
        tvVerTitle.setTypeface(null, Typeface.BOLD);
        verCard.addView(tvVerTitle);

        final TextView tvVerInfo = new TextView(ctx);
        tvVerInfo.setText("Mod版本: " + CURRENT_VERSION_NAME + " (Build " + CURRENT_VERSION_CODE + ")\n支持国内多镜像直连检测与下载 (无需VPN)");
        tvVerInfo.setTextColor(curTheme.textPri);
        tvVerInfo.setTextSize(11.5f);
        tvVerInfo.setPadding(0, dp(6), 0, dp(6));
        verCard.addView(tvVerInfo);

        final Button btnCheckUpdate = makePillBtn("检查更新", curTheme.btnGreen, 32);
        btnCheckUpdate.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                checkUpdate(true);
            }
        });
        verCard.addView(btnCheckUpdate);
        col.addView(verCard, vLp);

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
        sw.setChecked(Preferences.loadPrefBool("Save feature preferences", -1, true));
        sw.setOnCheckedChangeListener(new CompoundButton.OnCheckedChangeListener() {
            @Override public void onCheckedChanged(CompoundButton b, boolean isOn) {
                if (suppressToggleCallbacks) return;
                Preferences.loadPref = isOn;
                Preferences.changeFeatureBool("Save feature preferences", -1, isOn);
                Preferences.with(ctx).writeBoolean(-1, isOn);
                if (!isOn) {
                    Preferences.with(ctx).clear();
                    Preferences.with(ctx).writeBoolean(-1, false);
                    Preferences.loadPref = false;
                } else {
                    saveAllFeatureSwitchesToPrefs();
                }
            }
        });
        prefCard.addView(sw);
        col.addView(prefCard, pLp);

        themeRefreshHooks.add(new Runnable() {
            @Override public void run() {
                verCard.setBackground(makeRoundBox(curTheme.cardBg, curTheme.cardEdge, dp(1), dp(10)));
                tvVerTitle.setTextColor(curTheme.accent);
                tvVerInfo.setTextColor(curTheme.textPri);
                btnCheckUpdate.setBackground(makeRoundBox(curTheme.btnGreen, 0x33FFFFFF, 1, dp(7)));
                btnRef.setBackground(makeRoundBox(curTheme.btnBlue, 0x33FFFFFF, 1, dp(7)));
                btnSyncDyn.setBackground(makeRoundBox(curTheme.btnGreen, 0x33FFFFFF, 1, dp(7)));
                btnResetPos.setBackground(makeRoundBox(curTheme.btnSlate, 0x33FFFFFF, 1, dp(7)));
            }
        });

        return wrap;
    }
}
