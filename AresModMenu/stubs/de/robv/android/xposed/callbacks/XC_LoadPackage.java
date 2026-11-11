package de.robv.android.xposed.callbacks;

public class XC_LoadPackage {

    public static final class LoadPackageParam {
        public ClassLoader classLoader;
        public String packageName;
        public String processName;
        public boolean isFirstApplication;
        public android.content.pm.ApplicationInfo appInfo;
    }
}
