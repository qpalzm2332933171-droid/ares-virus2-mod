# -*- coding: utf-8 -*-
"""解析真正的物品表 Material.csv（CMaterialConfigLoader 的 m_szName = "Material.csv"）。"""
import csv
import io
import json
import os
import sys

SRC = sys.argv[1] if len(sys.argv) > 1 else "assets_build/Material.csv"
OUT = "assets_build2"
os.makedirs(OUT, exist_ok=True)

raw = open(SRC, "rb").read()
for enc in ("utf-8-sig", "utf-8", "gbk"):
    try:
        text = raw.decode(enc)
        print("decode with", enc)
        break
    except UnicodeDecodeError:
        continue

rd = csv.DictReader(io.StringIO(text))
rows = list(rd)
print("列数", len(rd.fieldnames), "行数", len(rows))
print("全部列:", rd.fieldnames)

# 关键列
key = ["Id", "Name_Text", "Desc_Text", "Icon", "Class", "Type",
       "UIBelongType", "UIBelongSubType", "DependBlueprint", "Endurance", "Priority"]
out = []
for r in rows:
    if not r.get("Id"):
        continue
    try:
        _id = int(r["Id"])
    except ValueError:
        continue
    rec = {"id": _id}
    for k in key:
        if k == "Id":
            continue
        v = r.get(k, "")
        if v != "":
            rec[k] = v
    out.append(rec)

print("有效条目", len(out))
import collections
c = collections.Counter((x.get("Class", ""), x.get("UIBelongType", "")) for x in out)
print("Class × UIBelongType 分布:")
for k, v in c.most_common(30):
    print("  %-14s %-16s %d" % (k[0], k[1], v))

bp = [x for x in out if x.get("DependBlueprint")]
print("带 DependBlueprint 的物品数:", len(bp))
for x in bp[:10]:
    print("   ", x["id"], x.get("Name_Text"), "->", x["DependBlueprint"])

p = os.path.join(OUT, "Material.json")
json.dump(out, open(p, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print("写出", p, os.path.getsize(p))

for probe in ["鸡毛", "生铁", "铁制水龙头", "木头"]:
    hit = [x for x in out if x.get("Name_Text") == probe]
    print("  验证 %-8s -> %s" % (probe, hit[0] if hit else "未命中"))
