package de.robv.android.xposed;

import android.app.Application;
import android.content.Context;
import android.util.Log;

import java.lang.reflect.Method;
import java.util.HashMap;
import java.util.Map;

/**
 * 编译期 stub：只声明签名，运行时由 LSPosed/Vector 提供真实现。
 * 打包时这个类不会被打进 dex（用 -sourcepath 编译、不输出到 dex）。
 */
public class XposedBridge {
    public static void log(String text) {
        Log.i("Xposed", text);
    }

    public static void log(Throwable t) {
        Log.e("Xposed", "err", t);
    }

    public static java.util.Set<XC_MethodHook.Unhook> hookAllMethods(Class<?> hookClass, String methodName, XC_MethodHook callback) {
        return java.util.Collections.emptySet();
    }

    public static XC_MethodHook.Unhook hookMethod(java.lang.reflect.Member hookMethod, XC_MethodHook callback) {
        return null;
    }

    public static Object invokeOriginalMethod(Object method, Object thisObject, Object[] args) throws Throwable {
        return null;
    }
}
