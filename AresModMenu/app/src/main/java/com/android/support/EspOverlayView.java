package com.android.support;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.RectF;
import android.view.SurfaceView;
import android.view.View;
import android.view.ViewGroup;

import java.lang.ref.WeakReference;

/**
 * Full-screen non-touchable transparent ESP overlay.
 * Maps Unity Screen-normalized coordinates ([0..1] top-left origin) onto the
 * underlying Unity SurfaceView rectangle so boxes & tracer lines align across
 * display cutouts, aspect ratios, high-DPI phones, and x86_64 emulators.
 */
public final class EspOverlayView extends View {

    private static final int MAX_ENTS = 64;

    private volatile boolean espEnabled = false;
    private volatile boolean drawBox = true;
    private volatile boolean drawLine = true;
    private volatile boolean drawInfo = true;
    private volatile int lineMode = 0; // 0=top center, 1=screen center, 2=player

    private boolean animScheduled = false;
    private WeakReference<SurfaceView> cachedSurface = null;

    // Reusable zero-alloc JNI buffers
    private final float[] metaBuf = new float[4]; // pNx, pNy, sw, sh
    private final float[] rectBuf = new float[MAX_ENTS * 7]; // x1,y1,x2,y2,cx,cy,distM
    private final int[] intBuf = new int[MAX_ENTS * 5]; // hp, maxHp, unitId, hostileType, onScreen
    private final String[] nameBuf = new String[MAX_ENTS];
    private final int[] tmpSvLoc = new int[2];
    private final int[] tmpMyLoc = new int[2];
    private final RectF tmpRect = new RectF();

    private final Paint pEnemyBox = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pNeutralBox = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pLineEnemy = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pLineNeutral = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pTextBg = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pText = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pHpBg = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pHpFill = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint pOffDot = new Paint(Paint.ANTI_ALIAS_FLAG);

    private float density = 2f;

    public EspOverlayView(Context ctx) {
        super(ctx);
        density = ctx.getResources().getDisplayMetrics().density;
        setClickable(false);
        setFocusable(false);
        setFocusableInTouchMode(false);
        setWillNotDraw(false);

        pEnemyBox.setStyle(Paint.Style.STROKE);
        pEnemyBox.setStrokeWidth(Math.max(2f, 1.8f * density));
        pEnemyBox.setColor(0xFFF43F5E);

        pNeutralBox.setStyle(Paint.Style.STROKE);
        pNeutralBox.setStrokeWidth(Math.max(1.5f, 1.4f * density));
        pNeutralBox.setColor(0xFFFBBF24);

        pLineEnemy.setStyle(Paint.Style.STROKE);
        pLineEnemy.setStrokeWidth(Math.max(1.5f, 1.3f * density));
        pLineEnemy.setColor(0xCCFB7185);

        pLineNeutral.setStyle(Paint.Style.STROKE);
        pLineNeutral.setStrokeWidth(Math.max(1.2f, 1.0f * density));
        pLineNeutral.setColor(0xBBFDE047);

        pTextBg.setStyle(Paint.Style.FILL);
        pTextBg.setColor(0xAA0A1018);

        pText.setStyle(Paint.Style.FILL);
        pText.setColor(0xFFF8FAFC);
        pText.setTextSize(10.5f * density);
        pText.setFakeBoldText(true);

        pHpBg.setStyle(Paint.Style.FILL);
        pHpBg.setColor(0xAA1E293B);

        pHpFill.setStyle(Paint.Style.FILL);
        pHpFill.setColor(0xFF22C55E);

        pOffDot.setStyle(Paint.Style.FILL);
        pOffDot.setColor(0xFFF43F5E);
    }

    @Override
    public boolean hasOverlappingRendering() {
        return false;
    }

    @Override
    public boolean onTouchEvent(android.view.MotionEvent event) {
        return false;
    }

    public void updateOptions(boolean enabled, boolean box, boolean line, boolean info, int mode) {
        this.espEnabled = enabled;
        this.drawBox = box;
        this.drawLine = line;
        this.drawInfo = info;
        this.lineMode = mode;
        post(new Runnable() {
            @Override public void run() {
                if (espEnabled) {
                    setVisibility(View.VISIBLE);
                    scheduleNextFrame();
                } else {
                    setVisibility(View.GONE);
                }
                invalidate();
            }
        });
    }

    private void scheduleNextFrame() {
        if (!espEnabled || animScheduled) return;
        animScheduled = true;
        postOnAnimation(frameRunnable);
    }

    private final Runnable frameRunnable = new Runnable() {
        @Override public void run() {
            animScheduled = false;
            if (!espEnabled) return;
            invalidate();
            scheduleNextFrame();
        }
    };

    @Override
    protected void onAttachedToWindow() {
        super.onAttachedToWindow();
        if (espEnabled) scheduleNextFrame();
    }

    @Override
    protected void onDetachedFromWindow() {
        removeCallbacks(frameRunnable);
        animScheduled = false;
        super.onDetachedFromWindow();
    }

    private static SurfaceView findSurfaceView(View root) {
        if (root instanceof SurfaceView) return (SurfaceView) root;
        if (root instanceof ViewGroup) {
            ViewGroup vg = (ViewGroup) root;
            for (int i = 0; i < vg.getChildCount(); i++) {
                SurfaceView sv = findSurfaceView(vg.getChildAt(i));
                if (sv != null) return sv;
            }
        }
        return null;
    }

    private void resolveCanvasArea(float[] out) {
        out[0] = 0f;
        out[1] = 0f;
        out[2] = getWidth();
        out[3] = getHeight();
        try {
            SurfaceView sv = (cachedSurface != null) ? cachedSurface.get() : null;
            if (sv == null || !sv.isAttachedToWindow()) {
                View r = getRootView();
                sv = (r != null) ? findSurfaceView(r) : null;
                if (sv != null) cachedSurface = new WeakReference<SurfaceView>(sv);
            }
            if (sv != null && sv.getWidth() > 0 && sv.getHeight() > 0) {
                sv.getLocationOnScreen(tmpSvLoc);
                this.getLocationOnScreen(tmpMyLoc);
                out[0] = (float) (tmpSvLoc[0] - tmpMyLoc[0]);
                out[1] = (float) (tmpSvLoc[1] - tmpMyLoc[1]);
                out[2] = (float) sv.getWidth();
                out[3] = (float) sv.getHeight();
            }
        } catch (Throwable ignored) {}
    }

    private final float[] areaBuf = new float[4];

    @Override
    protected void onDraw(Canvas canvas) {
        if (!espEnabled) return;
        int cnt;
        try {
            cnt = Menu.NativeGetEsp(metaBuf, rectBuf, intBuf, nameBuf);
        } catch (Throwable t) {
            return;
        }
        if (cnt <= 0) return;

        resolveCanvasArea(areaBuf);
        float ox = areaBuf[0];
        float oy = areaBuf[1];
        float vw = areaBuf[2];
        float vh = areaBuf[3];
        if (vw <= 1f || vh <= 1f) return;

        float startX, startY;
        if (lineMode == 1) {
            startX = ox + vw * 0.5f;
            startY = oy + vh * 0.5f;
        } else if (lineMode == 2) {
            float pnx = metaBuf[0];
            float pny = metaBuf[1];
            if (pnx >= -0.2f && pnx <= 1.2f && pny >= -0.2f && pny <= 1.2f) {
                startX = ox + pnx * vw;
                startY = oy + pny * vh;
            } else {
                startX = ox + vw * 0.5f;
                startY = oy + vh * 0.5f;
            }
        } else {
            startX = ox + vw * 0.5f;
            startY = oy + 4f * density;
        }

        float cornerLenFactor = 0.25f;
        float pad = 3f * density;
        float barH = Math.max(3f, 2.8f * density);
        float margin = 12f * density;

        for (int i = 0; i < cnt; i++) {
            int rb = i * 7;
            int ib = i * 5;
            float x1 = ox + rectBuf[rb] * vw;
            float y1 = oy + rectBuf[rb + 1] * vh;
            float x2 = ox + rectBuf[rb + 2] * vw;
            float y2 = oy + rectBuf[rb + 3] * vh;
            float cx = ox + rectBuf[rb + 4] * vw;
            float cy = oy + rectBuf[rb + 5] * vh;
            float distM = rectBuf[rb + 6];

            int hp = intBuf[ib];
            int maxHp = intBuf[ib + 1];
            int htype = intBuf[ib + 3];
            boolean onScreen = (intBuf[ib + 4] != 0);
            String name = nameBuf[i];
            if (name == null || name.isEmpty()) name = "#" + intBuf[ib + 2];

            boolean isHostile = (htype == 1);
            Paint boxP = isHostile ? pEnemyBox : pNeutralBox;
            Paint lineP = isHostile ? pLineEnemy : pLineNeutral;

            if (!onScreen) {
                // Draw edge radar indicator + tracer when offscreen
                float ex = Math.max(ox + margin, Math.min(ox + vw - margin, cx));
                float ey = Math.max(oy + margin, Math.min(oy + vh - margin, cy));
                if (drawLine) {
                    canvas.drawLine(startX, startY, ex, ey, lineP);
                }
                pOffDot.setColor(isHostile ? 0xFFF43F5E : 0xFFFBBF24);
                canvas.drawCircle(ex, ey, 4.5f * density, pOffDot);
                if (drawInfo) {
                    String tag = String.format(java.util.Locale.US, "%s %.0fm", name, distM);
                    float tw = pText.measureText(tag);
                    float tx = Math.max(ox + margin, Math.min(ox + vw - tw - margin, ex - tw * 0.5f));
                    float ty = Math.max(oy + margin + 12f * density, Math.min(oy + vh - margin, ey - 6f * density));
                    tmpRect.set(tx - pad, ty - 11f * density, tx + tw + pad, ty + 3f * density);
                    canvas.drawRoundRect(tmpRect, 3f * density, 3f * density, pTextBg);
                    canvas.drawText(tag, tx, ty, pText);
                }
                continue;
            }

            if (drawLine) {
                float endX = 0.5f * (x1 + x2);
                float endY = (lineMode == 0) ? y1 : cy;
                canvas.drawLine(startX, startY, endX, endY, lineP);
            }

            if (drawBox) {
                float w = x2 - x1;
                float h = y2 - y1;
                float clx = w * cornerLenFactor;
                float cly = h * cornerLenFactor;
                // Faint full frame + crisp corner brackets
                int origAlpha = boxP.getAlpha();
                boxP.setAlpha(110);
                canvas.drawRect(x1, y1, x2, y2, boxP);
                boxP.setAlpha(origAlpha);

                canvas.drawLine(x1, y1, x1 + clx, y1, boxP);
                canvas.drawLine(x1, y1, x1, y1 + cly, boxP);
                canvas.drawLine(x2, y1, x2 - clx, y1, boxP);
                canvas.drawLine(x2, y1, x2, y1 + cly, boxP);
                canvas.drawLine(x1, y2, x1 + clx, y2, boxP);
                canvas.drawLine(x1, y2, x1, y2 - cly, boxP);
                canvas.drawLine(x2, y2, x2 - clx, y2, boxP);
                canvas.drawLine(x2, y2, x2, y2 - cly, boxP);
            }

            if (drawInfo) {
                if (maxHp > 0) {
                    float frac = Math.max(0f, Math.min(1f, (float) hp / (float) maxHp));
                    float by1 = y1 - barH - 2f * density;
                    float by2 = y1 - 2f * density;
                    tmpRect.set(x1, by1, x2, by2);
                    canvas.drawRect(tmpRect, pHpBg);
                    pHpFill.setColor(frac > 0.5f ? 0xFF22C55E : (frac > 0.25f ? 0xFFFACC15 : 0xFFEF4444));
                    tmpRect.set(x1, by1, x1 + (x2 - x1) * frac, by2);
                    canvas.drawRect(tmpRect, pHpFill);
                }

                String lbl = (maxHp > 0)
                    ? String.format(java.util.Locale.US, "%s %.1fm [%d]", name, distM, hp)
                    : String.format(java.util.Locale.US, "%s %.1fm", name, distM);
                float tw = pText.measureText(lbl);
                float tx = 0.5f * (x1 + x2) - tw * 0.5f;
                float ty = y1 - barH - 5f * density;
                if (ty < oy + 14f * density) ty = y2 + 12f * density;
                tmpRect.set(tx - pad, ty - 10.5f * density, tx + tw + pad, ty + 2.5f * density);
                canvas.drawRoundRect(tmpRect, 3f * density, 3f * density, pTextBg);
                canvas.drawText(lbl, tx, ty, pText);
            }
        }
    }
}
