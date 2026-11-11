# -*- coding: utf-8 -*-
"""从候选 bundle 中导出全部 TextAsset，并用四词（UTF-8 字节）判定真物品表。
只读源目录；导出物写到 assets_build2/cfg_probe/my/。"""
import os
import json
import warnings
import UnityPy

warnings.filterwarnings("ignore")

SRC = r"D:\arsbd\hotupdate_dec"
OUT = r"D:\arsbd\assets_build2\cfg_probe\my"
os.makedirs(OUT, exist_ok=True)

W = {
    "鸡毛": bytes.fromhex("e9b8a1e6af9b"),
    "生铁": bytes.fromhex("e7949fe99381"),
    "木头": bytes.fromhex("e69ca8e5a4b4"),
    "铁制水龙头": bytes.fromhex("e99381e588b6e6b0b4e9be99e5a4b4"),
    "蓝图": bytes.fromhex("e8939de59bbe"),
    "子弹": bytes.fromhex("e5ad90e5bcb9"),
    "工人帽": bytes.fromhex("e5b7a5e4babae5b8bd"),
}

# 候选 bundle：来自二进制扫描（含中文词 / peizhi / csv）
CAND = set()
for ln in open(r"D:\arsbd\assets_build2\dllscan\scan_hits.json", encoding="utf-8"):
    pass
try:
    hits = json.load(open(r"D:\arsbd\assets_build2\dllscan\scan_hits.json", encoding="utf-8"))
    for h in hits:
        CAND.add(h["bundle"])
except Exception as e:
    print("no scan json", e)

EXTRA = [
    "yoo_DefaultPackage_CacheFiles_d8_d86ee7d117493d498c074e3354d430a6.bundle",
    "yoo_DefaultPackage_CacheFiles_fa_facccb986bc2c3895584ef3bdeef00bb.bundle",
    "yoo_DefaultPackage_CacheFiles_ab_ab1abb959b86d2259c1a398ef3c2c7a9.bundle",
]
CAND.update(EXTRA)
CAND = sorted(CAND)
print("candidates=%d" % len(CAND))

rows = []
detail = []
for i, fn in enumerate(CAND):
    p = os.path.join(SRC, fn)
    try:
        env = UnityPy.load(p)
    except Exception as e:
        detail.append("%s LOAD_FAIL %r" % (fn, e))
        continue
    for o in env.objects:
        if o.type.name != "TextAsset":
            continue
        try:
            ta = o.read()
        except Exception as e:
            continue
        name = getattr(ta, "m_Name", "?")
        d = getattr(ta, "m_Script", None)
        if d is None:
            continue
        if isinstance(d, str):
            d = d.encode("utf-8", "surrogateescape")
        cnt = {k: d.count(v) for k, v in W.items()}
        cnt = {k: v for k, v in cnt.items() if v}
        rows.append((name, len(d), fn, cnt))
        if cnt:
            sp = os.path.join(OUT, fn.replace(".bundle", "") + "__" + name.replace("/", "_"))
            with open(sp, "wb") as f:
                f.write(d)
    if (i + 1) % 10 == 0:
        print("  %d/%d ta=%d" % (i + 1, len(CAND), len(rows)))

print("\ntextassets seen = %d" % len(rows))
hit = [r for r in rows if r[3]]
print("textassets with 中文命中 = %d\n" % len(hit))
print("%-58s %10s %-40s %s" % ("asset", "size", "bundle", "hits"))
for name, sz, fn, cnt in sorted(hit, key=lambda x: -sum(x[3].values())):
    print("%-58s %10d %-40s %s" % (name[:58], sz, fn[34:42], cnt))

with open(os.path.join(OUT, "_summary.tsv"), "w", encoding="utf-8", newline="\n") as f:
    f.write("# asset\tsize\tbundle\thits\n")
    for name, sz, fn, cnt in rows:
        f.write("%s\t%d\t%s\t%s\n" % (name, sz, fn, json.dumps(cnt, ensure_ascii=False)))

with open(os.path.join(OUT, "_detail.txt"), "w", encoding="utf-8", newline="\n") as f:
    f.write("\n".join(detail))

print("\n--- 名字含 csv / ConfigCategory 的 TextAsset ---")
for name, sz, fn, cnt in rows:
    if ("csv" in name.lower()) or ("ConfigCategory" in name) or name.endswith(".bytes"):
        print("%-58s %10d %-40s %s" % (name[:58], sz, fn[34:42], cnt))
