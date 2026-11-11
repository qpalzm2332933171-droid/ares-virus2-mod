package de.robv.android.xposed;

import java.lang.reflect.Field;
import java.lang.reflect.Method;

public class XposedHelpers {

    public static XC_MethodHook.Unhook findAndHookMethod(Class<?> clazz, String methodName,
                                                         Object... parameterTypesAndCallback) {
        return null;
    }

    public static XC_MethodHook.Unhook findAndHookMethod(String className, ClassLoader classLoader,
                                                         String methodName, Object... parameterTypesAndCallback) {
        return null;
    }

    public static Object getObjectField(Object obj, String fieldName) {
        try {
            Field f = obj.getClass().getField(fieldName);
            f.setAccessible(true);
            return f.get(obj);
        } catch (Throwable t) {
            return null;
        }
    }

    public static Object getStaticObjectField(Class<?> clazz, String fieldName) {
        try {
            Field f = clazz.getDeclaredField(fieldName);
            f.setAccessible(true);
            return f.get(null);
        } catch (Throwable t) {
            return null;
        }
    }

    public static void setObjectField(Object obj, String fieldName, Object value) {
        try {
            Field f = obj.getClass().getField(fieldName);
            f.setAccessible(true);
            f.set(obj, value);
        } catch (Throwable ignored) {
        }
    }

    public static Object callMethod(Object obj, String methodName, Object... args) {
        return null;
    }

    public static Object callStaticMethod(Class<?> clazz, String methodName, Object... args) {
        return null;
    }

    public static Class<?> findClass(String className, ClassLoader cl) {
        return null;
    }
}
