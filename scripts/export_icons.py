# -*- coding: utf-8 -*-
"""从解密后的 bundle 导出全部 ol_icon_* Sprite 为 PNG（统一 128x128）。"""
import os
import sys
import warnings
import UnityPy

warnings.filterwarnings("ignore")
SRC = "hotupdate_dec"
OUT = "assets_build/icons"
SIZE = 128
os.makedirs(OUT, exist_ok=True)

want = set()
import json
try:
    for r in json.load(open("assets_build/MaterialConfigCategory.json", encoding="utf-8")):
        ic = r.get("icon")
        if ic:
            want.add(ic.replace(".png", "").replace(".PNG", ""))
except Exception:
    pass
print("配置表里引用的图标数:", len(want))

saved = 0
miss = []
for fn in sorted(os.listdir(SRC)):
    p = os.path.join(SRC, fn)
    try:
        env = UnityPy.load(p)
    except Exception:
        continue
    for o in env.objects:
        if o.type.name not in ("Sprite", "Texture2D"):
            continue
        try:
            obj = o.read()
        except Exception:
            continue
        nm = getattr(obj, "m_Name", "")
        if nm not in want:
            continue
        dst = os.path.join(OUT, nm + ".png")
        if os.path.exists(dst):
            continue
        try:
            img = obj.image
            img = img.convert("RGBA").resize((SIZE, SIZE))
            img.save(dst)
            saved += 1
        except Exception as e:
            miss.append((nm, str(e)[:40]))
print("导出:", saved, " 失败:", len(miss))
if miss:
    print("失败样例:", miss[:5])
got = set(os.path.splitext(f)[0] for f in os.listdir(OUT))
print("配置表覆盖:", len(got & want), "/", len(want))
notfound = sorted(want - got)
print("缺图标数:", len(notfound), notfound[:10])
