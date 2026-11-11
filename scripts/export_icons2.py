# -*- coding: utf-8 -*-
"""按 Material_full.json 的 Icon 列，从解密后的 bundle 里导出全部物品图标（128x128 PNG）。"""
import json
import os
import sys
import warnings

import UnityPy

warnings.filterwarnings("ignore")
SRC = "hotupdate_dec"
OUT = "assets_build2/icons"
SIZE = 128
os.makedirs(OUT, exist_ok=True)

try:
    rows = json.load(open("assets_build2/Material_full.json", encoding="utf-8"))
except FileNotFoundError:
    rows = json.load(open("assets_build2/Material.json", encoding="utf-8"))

want = set()
for r in rows:
    ic = r.get("Icon") or ""
    if ic:
        want.add(ic[:-4] if ic.lower().endswith(".png") else ic)
print("需要图标 %d 个" % len(want))

have = set(os.path.splitext(f)[0] for f in os.listdir(OUT))
todo = want - have
print("已有 %d，待导出 %d" % (len(have), len(todo)))
if not todo:
    sys.exit(0)

saved = 0
for fn in sorted(os.listdir(SRC)):
    if not todo:
        break
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
        if nm not in todo:
            continue
        dst = os.path.join(OUT, nm + ".png")
        try:
            img = obj.image.convert("RGBA").resize((SIZE, SIZE))
            img.save(dst)
            saved += 1
            todo.discard(nm)
        except Exception:
            pass
    if saved and saved % 200 == 0:
        print("  ...%d" % saved, flush=True)

print("导出 %d，仍缺 %d" % (saved, len(todo)))
if todo:
    print("缺样例:", sorted(todo)[:10])
