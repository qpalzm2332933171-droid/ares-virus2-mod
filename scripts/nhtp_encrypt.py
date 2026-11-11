#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""nhtp 加密器 —— `tools/nhtp_decrypt.py` 的严格逆过程。

用途：游戏 `Assembly.Load(byte[], byte[])` 收到的是 **nhtp 密文**（头 "nhtp"，4,054,528 B）。
要在进程里注入自己的补丁 DLL，必须先把补丁加密成同样的容器格式，才能原位 memcpy 覆盖。

算法来源：6.34 已逐字节验过的解密器（`decrypt(密文) == ground truth`，diff=0）。
本文件只做两件事：
  1. `block_type_a_inv` —— 那一层「32-bit 前缀异或链」的逆；
  2. `encrypt()` —— 按 decrypt 的**逆序**重放，其余变换（RC4 变种、头部两轮）都是自逆的，直接复用。

C 分支（第二层分组密码 0x5e0a740，仅 18/15,830 块）不做逆，直接沿用原密文；
调用方应先做 `decrypt(encrypt(x)) == x` 往返校验，确认改动块不落在 C 上。

用法：
    python tools/nhtp_encrypt.py <plain.dll> <orig_cipher> -o <out.cipher>
    python tools/nhtp_encrypt.py <plain.dll> <orig_cipher> --roundtrip
"""
from __future__ import annotations

import argparse
import hashlib
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import nhtp_decrypt as nd  # noqa: E402


def block_type_a_inv(buf: bytearray, off: int, k: int, nwords: int = 64) -> None:
    """`block_type_a` 的逆。

    正向：new[63] = old[0] ^ old[63] ^ k
          new[i]  = old[i] ^ new[i+1] ^ k      (i = 62..0)
    逆：  old[i] = new[i] ^ new[i+1] ^ k      (i = 0..62)
          old[63] = new[63] ^ old[0] ^ k
    """
    w = list(struct.unpack_from("<%dI" % nwords, buf, off))
    old = [0] * nwords
    for i in range(nwords - 1):
        old[i] = (w[i] ^ w[i + 1] ^ k) & 0xFFFFFFFF
    old[nwords - 1] = (w[nwords - 1] ^ old[0] ^ k) & 0xFFFFFFFF
    struct.pack_into("<%dI" % nwords, buf, off, *old)


def encrypt(plain: bytes, orig_cipher: bytes, so_path: Path | None = None, log=print) -> bytes:
    """把明文 DLL 加密成 nhtp 容器。原密文只提供两样东西：`seed` 与被覆盖的前 4 字节。"""
    if len(plain) != len(orig_cipher):
        raise ValueError("长度必须一致：plain=%d cipher=%d" % (len(plain), len(orig_cipher)))

    data = bytearray(plain)
    seed = struct.unpack_from("<I", orig_cipher, 0x1C)[0]
    struct.pack_into("<I", data, 0x1C, seed)          # decrypt 收尾会清零这 4 字节，这里补回

    pe, _ = nd.parse_pe(data)
    v20 = nd.key_hash()
    keys = nd.derive_block_keys(pe, v20)
    sched = nd.load_sched_table(so_path, v20)

    rsz, rp = pe["SizeOfRawData"], pe["PointerToRawData"]
    nblk = (rsz + 0xFF) // 0x100
    kinds = {0: 0, 1: 0, 2: 0}
    c_blocks = []
    changed_blocks = 0

    for blk in range(nblk):
        off = rp + blk * 0x100
        rem = min(0x100, rsz - blk * 0x100)
        k = keys[blk & 15]
        if rem != 0x100:
            st = nd.rc4_stream(struct.pack("<I", k), 8, rem, nd._rol8)
            for t in range(rem):
                data[off + t] ^= st[t]
            kinds[1] += 1
            continue
        v = sched[blk % nd.SCHED_TABLE_LEN]
        if v % 5 == 0:
            block_type_a_inv(data, off, k)
            kinds[0] += 1
        elif v & 1:
            st = nd.rc4_stream(struct.pack("<I", k), 8, 0x100, nd._rol8)
            for t in range(0x100):
                data[off + t] ^= st[t]
            kinds[1] += 1
        else:
            # C 分支：分组密码的逆没做。整块沿用原密文 —— 只有在「明文这一块也没变」时才是正确的，
            # 所以下面单独统计「明文变了却落在 C 上」的块数，非零就拒绝出结果。
            data[off : off + 0x100] = orig_cipher[off : off + 0x100]
            if bytes(data[off : off + 0x100]) != bytes(orig_cipher[off : off + 0x100]):
                pass
            if so_path and so_path.exists() and plain[off : off + 0x100] != nd_orig_plain_slice(orig_cipher, so_path, off):
                c_blocks.append(blk)
            kinds[2] += 1

    log("block kinds (encrypt): A=%d B=%d C=%d，其中明文有变却落在 C 的块: %d"
        % (kinds[0], kinds[1], kinds[2], len(c_blocks)))
    if c_blocks:
        raise RuntimeError(
            "有 %d 个 C 分支块的明文被改过（%s…），当前实现无法为它们生成密文"
            % (len(c_blocks), c_blocks[:10]))

    data[0:4] = orig_cipher[0:4]                      # 还原被 decrypt 硬写成 "MZ\\x90\\0" 的 4 字节

    # 头部两轮：XOR keystream，自逆，顺序与 decrypt 相反
    e2 = struct.pack("<I", seed ^ 0xEDB88320)
    s2 = nd.rc4_stream(e2, 0x11, 0x18, nd._ror8)
    for t in range(0x18):
        data[0x04 + t] ^= s2[t]

    e1 = struct.pack("<I", seed ^ 0x2013BABA)
    s1 = nd.rc4_stream(e1, 0x11, 0x200, nd._ror8)
    for t in range(0x200):
        data[0x20 + t] ^= s1[t]

    return bytes(data)


_PLAIN_CACHE: dict[int, bytes] = {}


def nd_orig_plain_slice(orig_cipher: bytes, so_path: Path | None, off: int) -> bytes:
    """取「原密文解密后」在 off 处的 256 字节，用来判断某个 C 块的明文到底有没有被改动。"""
    key = id(orig_cipher)
    if key not in _PLAIN_CACHE:
        _PLAIN_CACHE[key] = nd.decrypt(orig_cipher, so_path, log=lambda *_: None)
    return _PLAIN_CACHE[key][off : off + 0x100]


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="nhtp container encryptor")
    ap.add_argument("plain", help="要注入的明文 DLL（必须与 origin cipher 等长）")
    ap.add_argument("cipher", help="原始 nhtp 密文（提供 seed 与前 4 字节）")
    ap.add_argument("-o", "--out")
    ap.add_argument("--so", default=r"D:\arsbd\devtmp\libil2cpp.dump.so")
    ap.add_argument("--roundtrip", action="store_true", help="出结果后立刻 decrypt 回来比对")
    a = ap.parse_args(argv)

    so = Path(a.so) if a.so else None
    plain = Path(a.plain).read_bytes()
    orig = Path(a.cipher).read_bytes()
    out = encrypt(plain, orig, so)
    op = Path(a.out) if a.out else Path(a.plain).with_suffix(".nhtp")
    op.write_bytes(out)
    print("wrote %s (%d B) md5=%s" % (op, len(out), hashlib.md5(out).hexdigest()))
    print("head:", out[:8].hex(" "))

    if a.roundtrip:
        back = nd.decrypt(out, so, log=print)
        n = min(len(back), len(plain))
        same = sum(1 for i in range(n) if back[i] == plain[i])
        first = next((i for i in range(n) if back[i] != plain[i]), None)
        print("roundtrip: compared=%d same=%d diff=%d first_diff=%s"
              % (n, same, n - same, ("0x%x" % first) if first is not None else "none"))
        return 0 if same == n else 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
