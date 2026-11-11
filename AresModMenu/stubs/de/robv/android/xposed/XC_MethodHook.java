package de.robv.android.xposed;

public abstract class XC_MethodHook {

    public class Unhook {
        public void unhook() {
        }
    }

    protected void beforeHookedMethod(MethodHookParam param) throws Throwable {
    }

    protected void afterHookedMethod(MethodHookParam param) throws Throwable {
    }

    public static final class MethodHookParam {
        public java.lang.reflect.Member method;
        public Object thisObject;
        public Object[] args;
        private Object result;
        private Throwable throwable;

        public Object getResult() { return result; }

        public void setResult(Object result) { this.result = result; }

        public Throwable getThrowable() { return throwable; }

        public void setThrowable(Throwable t) { this.throwable = t; }

        public boolean hasThrowable() { return throwable != null; }
    }
}
