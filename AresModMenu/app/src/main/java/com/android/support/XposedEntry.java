package com.android.support;

import android.app.Activity;
import android.app.Application;
import android.content.Context;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.lang.reflect.Method;

import de.robv.android.xposed.IXposedHookLoadPackage;
import de.robv.android.xposed.XC_MethodHook;
import de.robv.android.xposed.XposedBridge;
import de.robv.android.xposed.XposedHelpers;
import de.robv.android.xposed.callbacks.XC_LoadPackage;

/**
 * LSPosed / Vector 入口。
 *
 * 注意：Android 16 上 `Application.attachBaseContext` 无法被 Xposed 反射命中
 *      （实测 Vector 报 NoSuchMethodError: Application#attachBaseContext(Context)#exact），
 *      所以改挂 Activity.onCreate —— 此时 Application、Activity、WindowManager 全部就绪，
 *      而且 Activity 自带 theme，悬浮窗控件不会退化成 Gingerbread 外观。
 */
public class XposedEntry implements IXposedHookLoadPackage {

    public static final String TARGET_PKG = "com.zzonegame.aresvirus2.android";
    public static volatile String sCurrentPkg = TARGET_PKG;
    private static final String TAG = "AresModMenu";
    private static boolean loaded = false;
    private static volatile Activity sCurrentActivity = null;
    private static volatile Activity sMenuActivity = null;
    private static volatile Menu sMenu = null;

    private static boolean isActivityAlive(Activity a) {
        if (a == null || a.isFinishing()) return false;
        if (android.os.Build.VERSION.SDK_INT >= 17 && a.isDestroyed()) return false;
        return true;
    }

    private static boolean isUnityGameActivity(Activity a) {
        if (a == null) return false;
        String cn = a.getClass().getName();
        return cn.contains("UnityPlayer") || cn.contains("aresvirus");
    }

    @Override
    public void handleLoadPackage(XC_LoadPackage.LoadPackageParam lpparam) throws Throwable {
        if (lpparam == null || lpparam.packageName == null) return;
        final String pkg = lpparam.packageName;
        // 兼容官服与全部渠道服（OPPO/vivo/小米/九游/B站/华为/4399/TapTap/应用宝等）：
        // 只要用户在 LSPosed/Vector 作用域中勾选了对应渠道的《阿瑞斯病毒2》，除模块自身与系统进程外全部放行
        if ("com.android.support".equals(pkg) || "android".equals(pkg) || pkg.startsWith("com.android.")) {
            return;
        }
        // 只在主进程挂菜单；:gcsdk / :push 等渠道 SDK 子进程挂了会跟主进程抢 WebView data dir
        if (lpparam.processName != null && (!pkg.equals(lpparam.processName) || lpparam.processName.contains(":"))) {
            XposedBridge.log(TAG + ": skip sub-process " + lpparam.processName + " (pkg=" + pkg + ")");
            return;
        }
        sCurrentPkg = pkg;
        XposedBridge.log(TAG + ": hooking " + pkg + " proc=" + lpparam.processName);

        // Unity 不透明窗口会把 SurfaceView 抬到窗口之上，窗口内任何 View 都被盖住
        try {
            ZOrderFix.install(lpparam.classLoader);
            XposedBridge.log(TAG + ": ZOrderFix installed");
        } catch (Throwable t) {
            XposedBridge.log(TAG + ": ZOrderFix failed: " + t);
        }

        try {
            XposedHelpers.findAndHookMethod("android.app.Activity", lpparam.classLoader,
                    "onCreate", Bundle.class, new XC_MethodHook() {
                        @Override
                        protected void afterHookedMethod(MethodHookParam param) {
                            final Activity act = (Activity) param.thisObject;
                            if (act == null) return;
                            if (isUnityGameActivity(act) || !isActivityAlive(sCurrentActivity)) {
                                sCurrentActivity = act;
                            }
                            if (!loaded) {
                                loaded = true;
                                try {
                                    install(act);
                                } catch (Throwable t) {
                                    XposedBridge.log(TAG + ": install failed: " + t);
                                }
                            } else if (isUnityGameActivity(act) && sMenuActivity != act) {
                                // 兼容部分渠道服先启动 SplashActivity 再跳转 UnityPlayerActivity 的流程
                                new Handler(Looper.getMainLooper()).postDelayed(new Runnable() {
                                    @Override
                                    public void run() {
                                        attachMenuIfNeeded(act);
                                    }
                                }, 1200);
                            }
                            inspectAntiCheatJava(act);
                        }
                    });
            XposedBridge.log(TAG + ": Activity.onCreate hooked");
        } catch (Throwable t) {
            XposedBridge.log(TAG + ": hook failed: " + t);
        }
    }

    private static volatile android.content.res.AssetManager sModAssets = null;

    public static String[] listModuleAssets(Context ctx, String dirPath) {
        try {
            if (sModAssets == null && ctx != null) {
                synchronized (XposedEntry.class) {
                    if (sModAssets == null) {
                        Context modCtx = ctx.createPackageContext("com.android.support", Context.CONTEXT_IGNORE_SECURITY);
                        sModAssets = modCtx.getAssets();
                    }
                }
            }
            if (sModAssets != null) {
                return sModAssets.list(dirPath);
            }
        } catch (Throwable ignored) {}
        return new String[0];
    }

    public static InputStream openModuleAsset(Context ctx, String assetName) {
        if (assetName == null) return null;
        try {
            if (sModAssets == null && ctx != null) {
                synchronized (XposedEntry.class) {
                    if (sModAssets == null) {
                        Context modCtx = ctx.createPackageContext("com.android.support", Context.CONTEXT_IGNORE_SECURITY);
                        sModAssets = modCtx.getAssets();
                    }
                }
            }
            if (sModAssets != null) {
                return sModAssets.open(assetName);
            }
        } catch (Throwable ignored) {}
        try {
            return XposedEntry.class.getClassLoader().getResourceAsStream("assets/" + assetName);
        } catch (Throwable ignored) {}
        return null;
    }

    private static String readSmallString(InputStream is) {
        if (is == null) return "";
        try (InputStream in = is) {
            ByteArrayOutputStream bos = new ByteArrayOutputStream();
            byte[] buf = new byte[256];
            int r;
            while ((r = in.read(buf)) > 0) bos.write(buf, 0, r);
            return new String(bos.toByteArray(), "UTF-8").trim();
        } catch (Throwable ignored) { return ""; }
    }

    private static boolean copyAssetToFile(Activity act, String assetName, File dst) {
        try (InputStream is = openModuleAsset(act, assetName)) {
            if (is == null) return false;
            File tmp = new File(dst.getParentFile(), dst.getName() + ".tmp");
            try (FileOutputStream fos = new FileOutputStream(tmp)) {
                byte[] buf = new byte[32768];
                int r;
                while ((r = is.read(buf)) > 0) fos.write(buf, 0, r);
            }
            if (dst.exists()) dst.delete();
            return tmp.renameTo(dst);
        } catch (Throwable t) {
            XposedBridge.log(TAG + ": copyAsset " + assetName + " failed: " + t);
            return false;
        }
    }

    private void syncModuleAssets(Activity act) {
        File dir = act.getFilesDir();
        String newStamp = readSmallString(openModuleAsset(act, "build_stamp.txt"));
        File stampFile = new File(dir, "mod_build_stamp.txt");
        String oldStamp = "";
        if (stampFile.exists()) {
            try (FileInputStream fis = new FileInputStream(stampFile)) {
                oldStamp = readSmallString(fis);
            } catch (Throwable ignored) {}
        }
        String[] files = new String[] { "libmodmenu.so", "arsmod.dll", "ModBridge.dll" };
        boolean needSync = newStamp.length() > 0 && !newStamp.equals(oldStamp);
        for (String f : files) {
            if (!new File(dir, f).exists()) needSync = true;
        }
        if (needSync) {
            for (String f : files) {
                boolean ok = copyAssetToFile(act, f, new File(dir, f));
                XposedBridge.log(TAG + ": sync " + f + " -> " + ok);
            }
            if (newStamp.length() > 0) {
                try (FileOutputStream fos = new FileOutputStream(stampFile)) {
                    fos.write(newStamp.getBytes("UTF-8"));
                } catch (Throwable ignored) {}
            }
        }
    }

    public static volatile boolean antiCheatEnabled = true;
    private static final java.util.Set<String> sHookedClasses =
            java.util.Collections.synchronizedSet(new java.util.HashSet<String>());

    private static String safeObjStr(Object o) {
        if (o == null) return "null";
        try {
            if (o instanceof byte[]) return "byte[" + ((byte[]) o).length + "]";
            StringBuilder sb = new StringBuilder(o.getClass().getSimpleName()).append("{");
            java.lang.reflect.Field[] fs = o.getClass().getDeclaredFields();
            for (int i = 0; i < fs.length && i < 6; i++) {
                fs[i].setAccessible(true);
                if (i > 0) sb.append(",");
                sb.append(fs[i].getName()).append("=").append(String.valueOf(fs[i].get(o)));
            }
            return sb.append("}").toString();
        } catch (Throwable t) {
            return String.valueOf(o);
        }
    }

    private void hookHTPCallbackClass(final Class<?> cbCls) {
        if (cbCls == null) return;
        String name = cbCls.getName();
        if (!sHookedClasses.add("cb:" + name)) return;
        try {
            XposedHelpers.findAndHookMethod(cbCls, "onReceive", int.class, String.class, new XC_MethodHook() {
                @Override
                protected void beforeHookedMethod(MethodHookParam param) {
                    int code = (Integer) param.args[0];
                    String msg = (String) param.args[1];
                    XposedBridge.log(TAG + ": [HTPCallback] onReceive code=" + code + " msg=" + msg + " acBlock=" + antiCheatEnabled);
                    if (antiCheatEnabled && code != 200 && code != 0) {
                        XposedBridge.log(TAG + ": [HTPCallback] BLOCKED non-OK callback code=" + code);
                        param.setResult(null);
                    }
                }
            });
            XposedBridge.log(TAG + ": Hooked HTPCallback impl: " + name);
        } catch (Throwable t) {
            XposedBridge.log(TAG + ": hookHTPCallbackClass err: " + t);
        }
    }

    private static void hookAllByName(Class<?> cls, String methodName, XC_MethodHook cb) {
        if (cls == null || methodName == null) return;
        for (Method m : cls.getDeclaredMethods()) {
            if (methodName.equals(m.getName())) {
                try {
                    Class<?>[] pts = m.getParameterTypes();
                    Object[] args = new Object[pts.length + 1];
                    System.arraycopy(pts, 0, args, 0, pts.length);
                    args[pts.length] = cb;
                    XposedHelpers.findAndHookMethod(cls, methodName, args);
                } catch (Throwable ignored) {}
            }
        }
    }

    private void inspectAntiCheatJava(Activity act) {
        try {
            final ClassLoader cl = act.getClassLoader();
            Class<?> actCls = act.getClass();
            if (sHookedClasses.add("act:" + actCls.getName())) {
                XposedBridge.log(TAG + ": Activity class = " + actCls.getName());

                // 1. Hook native UnityPlayerActivity anti-cheat / token bridges
                for (final String actMethod : new String[] { "RequestGetAntiCheatToken", "HTProjectSerRoleInfo", "fetchToken_Login" }) {
                    hookAllByName(actCls, actMethod, new XC_MethodHook() {
                        @Override
                        protected void beforeHookedMethod(MethodHookParam param) {
                            XposedBridge.log(TAG + ": [AC-Act] " + actMethod + " called (acBlock=" + antiCheatEnabled + ")");
                        }
                    });
                }
            }

            // 2. Hook UnityPlayer.nativeRender (runs on UnityMain thread!) + UnitySendMessage
            if (!sHookedClasses.contains("unity_send_msg")) {
                try {
                    Class<?> upCls = Class.forName("com.unity3d.player.UnityPlayer", false, cl);
                    if (upCls != null && sHookedClasses.add("unity_send_msg")) {
                        hookAllByName(upCls, "nativeRender", new XC_MethodHook() {
                            private long lastProbeMs = 0L;
                            private int probeCount = 0;
                            @Override
                            protected void beforeHookedMethod(MethodHookParam param) {
                                if (!nativeLoaded) return;
                                long now = android.os.SystemClock.uptimeMillis();
                                long interval = (probeCount < 50) ? 100L : 1000L;
                                if (now - lastProbeMs >= interval) {
                                    lastProbeMs = now;
                                    probeCount++;
                                    try {
                                        Menu.ProbeOnMain();
                                    } catch (Throwable t) {
                                        if (probeCount <= 5) {
                                            XposedBridge.log(TAG + ": UnityMain ProbeOnMain err: " + t);
                                        }
                                    }
                                }
                            }
                        });
                        XposedBridge.log(TAG + ": Hooked UnityPlayer.nativeRender for UnityMain probing");
                        hookAllByName(upCls, "UnitySendMessage", new XC_MethodHook() {
                            @Override
                            protected void beforeHookedMethod(MethodHookParam param) {
                                if (param.args != null && param.args.length >= 3) {
                                    String m = String.valueOf(param.args[1]);
                                    if (m.contains("AntiCheat") || m.contains("Cheat") || m.contains("Token")) {
                                        XposedBridge.log(TAG + ": [UnitySendMessage] " + param.args[0] + "." + m + "(" + param.args[2] + ")");
                                    }
                                }
                            }
                        });
                    }
                } catch (Throwable ignored) {}
            }

            // 3. Hook com.netease.htprotect.HTProtect & HTPCallback
            try {
                final Class<?> htp = Class.forName("com.netease.htprotect.HTProtect", false, cl);
                final Class<?> cbIface = Class.forName("com.netease.htprotect.callback.HTPCallback", false, cl);

                if (sHookedClasses.add("htp_all")) {
                    hookAllByName(htp, "init", new XC_MethodHook() {
                        @Override
                        protected void beforeHookedMethod(MethodHookParam param) {
                            if (param.args != null) {
                                for (Object a : param.args) {
                                    if (a != null && cbIface.isAssignableFrom(a.getClass())) {
                                        hookHTPCallbackClass(a.getClass());
                                    }
                                }
                            }
                        }
                    });

                    for (final String fn : new String[] { "getToken", "getTokenAsync", "ioctl", "safeCommToServer", "safeCommToServerCSharp", "safeCommToServerByte", "safeCommToServerV30", "safeCommToServerByteV30", "safeCommToServerCSharpByte" }) {
                        hookAllByName(htp, fn, new XC_MethodHook() {
                            @Override
                            protected void beforeHookedMethod(MethodHookParam param) {
                                if (param.args != null) {
                                    for (Object a : param.args) {
                                        if (a != null && cbIface.isAssignableFrom(a.getClass())) {
                                            hookHTPCallbackClass(a.getClass());
                                        }
                                    }
                                }
                            }
                            @Override
                            protected void afterHookedMethod(MethodHookParam param) {
                                XposedBridge.log(TAG + ": [HTProtect." + fn + "] ret=" + safeObjStr(param.getResult()));
                            }
                        });
                    }
                    XposedBridge.log(TAG + ": HTProtect Java hooks installed cleanly via hookMethod");
                }
            } catch (Throwable t) {
                XposedBridge.log(TAG + ": HTProtect hook note: " + t);
            }

            // 4. Guard Process.killProcess against NetEase anti-cheat native kills when enabled
            if (sHookedClasses.add("process_kill")) {
                try {
                    XposedHelpers.findAndHookMethod(android.os.Process.class, "killProcess", int.class, new XC_MethodHook() {
                        @Override
                        protected void beforeHookedMethod(MethodHookParam param) {
                            if (!antiCheatEnabled) return;
                            StackTraceElement[] st = Thread.currentThread().getStackTrace();
                            for (StackTraceElement e : st) {
                                String cn = e.getClassName().toLowerCase();
                                if (cn.contains("htprotect") || cn.contains("secsdk") || cn.contains("nesec") || cn.contains("nis.wrapper")) {
                                    XposedBridge.log(TAG + ": [AC-KILL] BLOCKED Process.killProcess from " + e.getClassName());
                                    param.setResult(null);
                                    return;
                                }
                            }
                        }
                    });
                } catch (Throwable ignored) {}
            }
        } catch (Throwable t) {
            XposedBridge.log(TAG + ": inspectAntiCheatJava err: " + t);
        }
    }

    private void attachMenuIfNeeded(Activity candidate) {
        Activity target = (candidate != null && isActivityAlive(candidate)) ? candidate : sCurrentActivity;
        if (!isActivityAlive(target)) return;
        if (sMenuActivity == target && sMenu != null) return;
        if (isActivityAlive(sMenuActivity) && isUnityGameActivity(sMenuActivity) && !isUnityGameActivity(target)) {
            return;
        }
        try {
            sMenuActivity = target;
            Menu menu = new Menu(target);
            menu.ShowInActivity(target);
            sMenu = menu;
            XposedBridge.log(TAG + ": menu attached to " + target.getClass().getName());
        } catch (Throwable t) {
            XposedBridge.log(TAG + ": menu failed: " + t);
        }
    }

    private static volatile boolean nativeLoaded = false;

    private void install(final Activity act) throws Throwable {
        syncModuleAssets(act);

        File so = new File(act.getFilesDir(), "libmodmenu.so");
        if (!so.exists()) so = new File("/data/local/tmp/ares/libmodmenu.so");
        if (!so.exists()) {
            XposedBridge.log(TAG + ": libmodmenu.so 缺失，先跑 deploy");
            return;
        }
        XposedBridge.log(TAG + ": loading " + so.getAbsolutePath());
        System.load(so.getAbsolutePath());

        try {
            Menu.NativeQuery(12, 0, 0, 0L, act.getFilesDir().getAbsolutePath());
        } catch (Throwable t) {
            XposedBridge.log(TAG + ": initial NativeQuery(12) failed: " + t);
        }
        nativeLoaded = true;

        new Handler(Looper.getMainLooper()).postDelayed(new Runnable() {
            @Override
            public void run() {
                attachMenuIfNeeded(sCurrentActivity != null ? sCurrentActivity : act);
            }
        }, 2500);
    }
}
