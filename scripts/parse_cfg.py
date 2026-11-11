# -*- coding: utf-8 -*-
"""解析 Luban 导出的 protobuf 配置表（MaterialConfigCategory），导出物品表 JSON。

格式：顶层是 repeated MaterialConfig（field 1, length-delimited），
每个条目的字段号对应 ET.MaterialConfig 上的 [ProtoMember(n)]。
"""
import sys
import json
import os
import warnings
import UnityPy

warnings.filterwarnings("ignore")

BUNDLE = sys.argv[1] if len(sys.argv) > 1 else \
    "hotupdate_dec/yoo_DefaultPackage_CacheFiles_56_561f7e493fd458b086d5010e684e7eea.bundle"
OUTDIR = "assets_build"
WANT = sys.argv[2] if len(sys.argv) > 2 else "MaterialConfigCategory"


def read_varint(b, i):
    r = 0
    s = 0
    while True:
        x = b[i]
        i += 1
        r |= (x & 0x7F) << s
        if not x & 0x80:
            return r, i
        s += 7


def parse_msg(b, start, end):
    """返回 {field: [values]}，string 解成 str，varint 解成 int，嵌套保留 bytes。"""
    out = {}
    i = start
    while i < end:
        key, i = read_varint(b, i)
        f, wt = key >> 3, key & 7
        if wt == 0:
            v, i = read_varint(b, i)
        elif wt == 2:
            ln, i = read_varint(b, i)
            v = b[i:i + ln]
            i += ln
        elif wt == 5:
            v = b[i:i + 4]
            i += 4
        elif wt == 1:
            v = b[i:i + 8]
            i += 8
        else:
            raise ValueError("wire type %d" % wt)
        out.setdefault(f, []).append(v)
    return out


def txt(v):
    if isinstance(v, bytes):
        try:
            return v.decode("utf-8")
        except UnicodeDecodeError:
            return v.hex()
    return v


def main():
    env = UnityPy.load(BUNDLE)
    blob = None
    for o in env.objects:
        if o.type.name != "TextAsset":
            continue
        ta = o.read()
        if getattr(ta, "m_Name", "") != WANT:
            continue
        d = getattr(ta, "m_Script", None)
        if isinstance(d, str):
            d = d.encode("utf-8", "surrogateescape")
        blob = bytes(d)
        break
    if blob is None:
        print("未找到", WANT)
        return 1

    # 顶层 repeated MaterialConfig 放在 field 1
    top = parse_msg(blob, 0, len(blob))
    items = []
    for raw in top.get(1, []):
        m = parse_msg(raw, 0, len(raw))
        items.append(m)

    print("条目数:", len(items))

    # 字段统计，帮助确认字段含义
    import collections
    fc = collections.Counter()
    for m in items:
        for k in m:
            fc[k] += 1
    print("字段出现次数:", dict(sorted(fc.items())))

    def g(m, k):
        v = m.get(k)
        return txt(v[0]) if v else None

    rows = []
    for m in items:
        rows.append({
            "id": g(m, 1),
            "name": g(m, 2),
            "icon": g(m, 3),
            "desc": g(m, 4),
            "shortDesc": g(m, 5),
            "materialType": g(m, 6),
            "initValue": g(m, 7),
            "stackMax": g(m, 8),
            "uiBelongType": g(m, 9),
            "uiBelongSmallType": g(m, 10),
            "storageScoreValue": g(m, 11),
            "campValue": g(m, 12),
            "canDonate": g(m, 13),
            "isExpensive": g(m, 14),
            "totalValue": g(m, 15),
            "f16": g(m, 16),
            "f17": g(m, 17),
        })

    os.makedirs(OUTDIR, exist_ok=True)
    p = os.path.join(OUTDIR, WANT + ".json")
    with open(p, "w", encoding="utf-8") as f:
        json.dump(rows, f, ensure_ascii=False, indent=1)
    print("写出", p, os.path.getsize(p), "字节")
    print("样例:")
    for r in rows[:5]:
        print("  ", r["id"], r["name"], "|", r["icon"], "|", r["uiBelongType"], "/", r["uiBelongSmallType"])
    return 0


if __name__ == "__main__":
    sys.exit(main())
