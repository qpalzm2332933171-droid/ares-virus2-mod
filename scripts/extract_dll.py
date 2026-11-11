# -*- coding: utf-8 -*-
"""遍历解密后的 bundle，列出所有 TextAsset（dll 通常以 TextAsset 存），并导出疑似程序集。"""
import os
import sys
import warnings
import UnityPy

warnings.filterwarnings("ignore")
SRC = "hotupdate_dec"
OUT = "extracted_dll"
os.makedirs(OUT, exist_ok=True)

files = sorted(os.listdir(SRC))
n = 0
found = []
for fn in files:
    p = os.path.join(SRC, fn)
    try:
        env = UnityPy.load(p)
    except Exception:
        continue
    for o in env.objects:
        if o.type.name != "TextAsset":
            continue
        try:
            ta = o.read()
        except Exception:
            continue
        name = getattr(ta, "m_Name", "?")
        data = getattr(ta, "m_Script", None)
        if data is None:
            continue
        if isinstance(data, str):
            data = data.encode("utf-8", "surrogateescape")
        if isinstance(data, (bytes, bytearray)):
            sz = len(data)
        else:
            sz = -1
        if sz > 100000 and (b"MZ" in bytes(data[:8]) or bytes(data[:2]) == b"MZ" or b"BSJB" in bytes(data[:4096])):
            out = os.path.join(OUT, name.replace("/", "_") + ".bin")
            open(out, "wb").write(bytes(data))
            found.append((name, sz, fn))
            print("[dll] %-50s %9d  <- %s" % (name, sz, fn), flush=True)
        n += 1
print("TextAsset scanned=%d, dll-like=%d" % (n, len(found)))
