package com.android.support;

import android.app.Activity;
import android.graphics.Color;
import android.graphics.Rect;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;
import android.view.View;
import android.view.ViewGroup;
import android.view.ViewParent;
import android.widget.FrameLayout;

/**
 * 把普通 View 叠进 Activity 自己那个窗口里（不新建窗口，不需要 SYSTEM_ALERT_WINDOW）。
 *
 * 原理：Activity 的窗口里已经有 UnityPlayer（内含 SurfaceView）。SurfaceView 的 surface
 * 默认位于窗口 surface 之下，窗口里任何普通 View 都画在它上面；只要本 View 与 UnityPlayer
 * 是同一 content 容器里的兄弟节点、且后加入（后加入者绘制在上），就能盖住游戏画面。
 *
 * 用法：OverlayAttach.attach(activity, rootFrame, POS_X, POS_Y);
 *       OverlayAttach.move(rootFrame, x, y);   // 拖动时改的是 margin，不是 window 参数
 *       OverlayAttach.detach();
 */
public final class OverlayAttach {

    public static final String TAG = "AresModMenu";
    private static FrameLayout sHost;

    private OverlayAttach() {
    }

    public static void attach(final Activity act, final View menu, final int x, final int y) {
        if (act == null || menu == null) {
            Log.e(TAG, "attach: null arg, act=" + act + " menu=" + menu);
            return;
        }
        if (Looper.myLooper() != Looper.getMainLooper()) {
            new Handler(Looper.getMainLooper()).post(new Runnable() {
                @Override
                public void run() {
                    attach(act, menu, x, y);
                }
            });
            return;
        }
        try {
            detach();

            // 同一个 View 不能有两个父容器，否则 IllegalStateException 直接崩主线程
            ViewParent old = menu.getParent();
            if (old instanceof ViewGroup) {
                ((ViewGroup) old).removeView(menu);
            }

            final FrameLayout host = new FrameLayout(act);
            host.setClipChildren(false);
            host.setBackgroundColor(Color.TRANSPARENT);

            FrameLayout.LayoutParams mlp = new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.WRAP_CONTENT,
                    ViewGroup.LayoutParams.WRAP_CONTENT);
            mlp.leftMargin = x;
            mlp.topMargin = y;
            host.addView(menu, mlp);

            // 关键：挂进 Activity 已有的 content 容器（android.R.id.content 的 FrameLayout），
            // 不 new 窗口、不设 token、不要 type、不申请悬浮窗权限。
            act.addContentView(host, new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT,
                    ViewGroup.LayoutParams.MATCH_PARENT));

            sHost = host;
            Log.i(TAG, "overlay attached act=" + act.getClass().getName()
                    + " finishing=" + act.isFinishing() + " destroyed=" + act.isDestroyed()
                    + " menuParent=" + menu.getParent().getClass().getName()
                    + " decorChildren="
                    + ((ViewGroup) act.getWindow().getDecorView()).getChildCount());

            // 布局完成后自证：globalVisibleRect 为空 ⇒ 没画出来
            host.post(new Runnable() {
                @Override
                public void run() {
                    Rect r = new Rect();
                    boolean vis = menu.getGlobalVisibleRect(r);
                    Log.i(TAG, "overlay laid out " + menu.getWidth() + "x" + menu.getHeight()
                            + " shown=" + menu.isShown()
                            + " attachedToWindow=" + menu.isAttachedToWindow()
                            + " rect=" + r + " visible=" + vis);
                }
            });
        } catch (Throwable t) {
            Log.e(TAG, "overlay attach failed", t);
        }
    }

    /** 拖动：改 margin 后 requestLayout，窗口内的 View 没有 updateViewLayout 可用。 */
    public static void move(View menu, int x, int y) {
        if (menu == null) {
            return;
        }
        ViewGroup.LayoutParams lp = menu.getLayoutParams();
        if (!(lp instanceof ViewGroup.MarginLayoutParams)) {
            return;
        }
        ViewGroup.MarginLayoutParams mlp = (ViewGroup.MarginLayoutParams) lp;
        mlp.leftMargin = x;
        mlp.topMargin = y;
        menu.setLayoutParams(mlp);
        menu.requestLayout();
    }

    public static void detach() {
        if (Looper.myLooper() != Looper.getMainLooper()) {
            return;
        }
        FrameLayout host = sHost;
        sHost = null;
        if (host == null) {
            return;
        }
        ViewParent p = host.getParent();
        if (p instanceof ViewGroup) {
            ((ViewGroup) p).removeView(host);
        }
        host.removeAllViews();
        Log.i(TAG, "overlay detached");
    }
}
