package com.android.support;

import android.util.Log;

import de.robv.android.xposed.XC_MethodHook;
import de.robv.android.xposed.XposedHelpers;

/**
 * 把 SurfaceView 的 z 序按回窗口之下。
 *
 * Unity 2020.3 的 UnityPlayer.CreateGlView() 里：窗口不透明时会调
 * setZOrderOnTop(true)（实测反汇编 mod版.apk/classes2.dex），把 surface 抬到
 * 窗口之上，于是窗口层级里任何普通 View 都看不见。只有把这次调用改成 false，
 * 窗口内叠加（addContentView）才可能显示。
 *
 * 必须在目标进程最早的时机安装：XposedEntry.handleLoadPackage 里、任何
 * Activity.onCreate 之前。
 */
public final class ZOrderFix {

    public static final String TAG = "AresModMenu";

    private ZOrderFix() {
    }

    public static void install(ClassLoader cl) {
        try {
            XposedHelpers.findAndHookMethod("android.view.SurfaceView", cl,
                    "setZOrderOnTop", boolean.class, new XC_MethodHook() {
                        @Override
                        protected void beforeHookedMethod(MethodHookParam param) {
                            if (param.args != null && param.args.length == 1
                                    && Boolean.TRUE.equals(param.args[0])) {
                                param.args[0] = Boolean.FALSE;
                                Log.i(TAG, "force SurfaceView.setZOrderOnTop(false)");
                            }
                        }
                    });
            Log.i(TAG, "SurfaceView.setZOrderOnTop hooked");
        } catch (Throwable t) {
            Log.e(TAG, "z-order hook failed", t);
        }
    }
}
