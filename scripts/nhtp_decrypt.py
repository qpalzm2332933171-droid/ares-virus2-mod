#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
nhtp 容器解密器  (Assembly-CSharp.dll  ->  .NET assembly 明文)

逆向来源: libil2cpp.dump.so @ RVA 0x5e58e1c (size 0x12ac)
容器布局: 密文文件整体就是明文 PE, 没有独立 header 段; magic "nhtp" 只是
          明文 "MZ\\x90\\0" + DOS stub 前 4 字节被替换后的样子, 解密时被
          data[4..0x1B] 那一轮覆盖掉, 最后再写回 MZ\\x90\\0。

算法分三层 (全部在同一个函数里内联):

 1) 头部第 1 轮  @0x5e593b0(KSA) 0x5e5940c(PRGA)
      extra = LE32(data[0x1c]) ^ 0x2013BABA
      KSA   : S=0..255; for i in 0..255: j=(j+S[i]+extra[i&3])&0xff; swap
      PRGA  : i=j=0; for t in 0..0x1FF: i=(i+1)&0xff; j=(j+S[i])&0xff;
              swap; k=S[(S[i]+S[j])&0xff]; data[0x20+t] ^= (ror8(k,2)+0x11)
 2) 头部第 2 轮  @0x5e59460..0x5e595c0
      extra = LE32(data[0x1c]) ^ 0xEDB88320
      同 KSA / 同 PRGA(ror8+C, C=0x11), 但作用区间 data[4..0x1B] (24 字节)
 3) .text 节块循环 @0x5e599a4..0x5e5a06c
      每块 256 字节; 密钥 K[blk&15] 由 PE 头字段经 CRC 变体派生;
      块类型由 919 项调度表 T[blk % 919] 决定:
        T%5==0        -> A: 32-bit 前缀异或链 (0x5e59ebc)
        T&1           -> B: KSA+PRGA(rol8(k,2)+8) (0x5e59f0c)
        else          -> C: 第二层分组密码 f(dst,src,len,K) @0x5e0a740
      尾块 (len<256) 走 B 的 PRGA。
 4) 收尾: data[0x1c:0x20] = 0 (原 extra 种子位置清零)

注意 (与早期阶段结论的差异, 均已由实测确认):
  * key 字符串 "hellocwhguochan007" 不参与 RC4。KSA 的第三个加法项是
    extra[i&3], 不是 key[i]。该字符串只用于派生 K[] 的 CRC 初值。
  * PRGA 输出是 ror8(k,2) + C (加法), 不是异或。
  * A 分支的异或链一直做到块内第 0 个字 (w[0] 也会被改写)。

用法:
  python nhtp_decrypt.py <cipher.dll> [-o out.dll] [--plain gt.dll] [--so libil2cpp.dump.so]
"""
from __future__ import annotations

import argparse
import hashlib
import struct
import sys
from pathlib import Path

# ---------------------------------------------------------------- 常量
POLY        = 0x8F6E37A0      # 0x5e9b230 的 d8[0:4]
CRC_ADD     = 0x00000010      # d8[4:8]
CRC_FINAL   = 0x1EDC6F41      # 对象 [+8]
KEY_MATERIAL = b"hellocwhguochan007"
KEY_HASH_BASE = 0xF0B6C238    # 0xf0b6c239 - 1

SCHED_TABLE_RVA = 0x5E9BF90
SCHED_TABLE_LEN = 919
SCHED_TABLE_SIZE = 0xE5C

FN_HEAD = 0x5E58E1C           # 参考: 整个解密函数
FN_BLOCKCIPHER = 0x5E0A740    # C 分支调用的第二层密码入口
FN_TPRINTF_THUNK = 0x5E0AA44  # = b 0x5e0a740


# ---------------------------------------------------------------- CRC 变体
def _crc_table(poly: int) -> list[int]:
    tbl = []
    for n in range(256):
        c = n
        for _ in range(8):
            c = (c >> 1) ^ (poly if c & 1 else 0)
        tbl.append(c & 0xFFFFFFFF)
    return tbl


_CRCT = _crc_table(POLY)


def crc_variant(data: bytes) -> int:
    """0x5e59804..0x5e59824 的循环: reflected CRC + 每字节加 CRC_ADD。"""
    c = 0xFFFFFFFF
    for b in data:
        c = _CRCT[(c ^ b) & 0xFF] ^ (c >> 8)
        c = (c + CRC_ADD) & 0xFFFFFFFF
    return c


def key_hash() -> int:
    """0x5e5925c..0x5e592a0: key 字符串的 CRC, 结果只当 K[] 的种子。"""
    c = crc_variant(KEY_MATERIAL)
    return ((KEY_HASH_BASE - c) + CRC_FINAL) & 0xFFFFFFFF


# ---------------------------------------------------------------- RC4 变种
def _ror8(v: int, n: int) -> int:
    return ((v >> n) | (v << (8 - n))) & 0xFF


def _rol8(v: int, n: int) -> int:
    return ((v << n) | (v >> (8 - n))) & 0xFF


def rc4_stream(extra4: bytes, add: int, n: int, rotate) -> bytes:
    """KSA(extra[i&3]) + PRGA(k' = rotate(k,2) + add)。"""
    S = list(range(256))
    j = 0
    for i in range(256):
        j = (j + S[i] + extra4[i & 3]) & 0xFF
        S[i], S[j] = S[j], S[i]
    out = bytearray(n)
    i = j = 0
    for t in range(n):
        i = (i + 1) & 0xFF
        j = (j + S[i]) & 0xFF
        S[i], S[j] = S[j], S[i]
        k = S[(S[i] + S[j]) & 0xFF]
        out[t] = (rotate(k, 2) + add) & 0xFF
    return bytes(out)


# ---------------------------------------------------------------- A 分支
def block_type_a(buf: bytearray, off: int, k: int, nwords: int = 64) -> None:
    """0x5e59ebc: 32-bit 前缀异或链 (逆序), 覆盖 w[n-1] .. w[0]。"""
    w = list(struct.unpack_from("<%dI" % nwords, buf, off))
    acc = (w[0] ^ k ^ w[nwords - 1]) & 0xFFFFFFFF
    w[nwords - 1] = acc
    for idx in range(nwords - 2, -1, -1):
        acc = (acc ^ k ^ w[idx]) & 0xFFFFFFFF
        w[idx] = acc
    struct.pack_into("<%dI" % nwords, buf, off, *w)


# ---------------------------------------------------------------- K 派生
def derive_block_keys(pe: dict, v20: int, count: int = 16) -> list[int]:
    """0x5e59674..0x5e59854: 16 个 32-bit 块密钥。"""
    state = 0x2016DEAD
    keys = []
    for n in range(count):
        state = (state + n + 0x1212) & 0xFFFFFFFF
        blob = struct.pack(
            "<8I",
            pe["SizeOfImage"],
            state,
            pe["BaseOfData"],
            pe["TimeDateStamp"],
            pe["CheckSum"],
            pe["VirtualSize"],
            pe["SizeOfRawData"],
            pe["SizeOfCode"],
        )
        c = crc_variant(blob)
        keys.append((CRC_FINAL + (~c & 0xFFFFFFFF)) & 0xFFFFFFFF)
    return keys


# ---------------------------------------------------------------- PE 头
def parse_pe(buf: bytearray) -> tuple[dict, int]:
    e = struct.unpack_from("<I", buf, 0x3C)[0]
    if buf[e : e + 4] != b"PE\0\0":
        raise ValueError("PE signature @0x%x not found" % e)
    nsec = struct.unpack_from("<H", buf, e + 6)[0]
    optsize = struct.unpack_from("<H", buf, e + 0x14)[0]
    opt = e + 0x18
    raw = lambda o: struct.unpack_from("<I", buf, o)[0]
    sec_tbl = opt + optsize
    chosen = None
    for i in range(nsec):
        o = sec_tbl + i * 40
        ch = raw(o + 0x24)
        if ch & 0x20:  # IMAGE_SCN_CNT_CODE
            chosen = o
            break
    if chosen is None:
        raise ValueError("no IMAGE_SCN_CNT_CODE section")
    vsz, va, rsz, rp = struct.unpack_from("<IIII", buf, chosen + 8)
    pe = dict(
        e_lfanew=e,
        SizeOfImage=raw(opt + 0x38),
        BaseOfData=raw(opt + 0x18),
        TimeDateStamp=raw(e + 8),
        CheckSum=raw(opt + 0x40),
        SizeOfCode=raw(opt + 0x04),
        VirtualSize=vsz,
        SizeOfRawData=rsz,
        PointerToRawData=rp,
    )
    return pe, chosen


# ---------------------------------------------------------------- 919 表
def load_sched_table(so_path: Path | None, v20: int) -> list[int]:
    """读 919 项表, 并按 0x5e596d0..0x5e596dc 覆盖第 0/1/4/7 项。"""
    if so_path and so_path.is_file():
        with so_path.open("rb") as f:
            f.seek(SCHED_TABLE_RVA)
            blob = f.read(SCHED_TABLE_SIZE)
        if len(blob) != SCHED_TABLE_SIZE:
            raise IOError("short read of 919-entry table")
        tbl = list(struct.unpack("<%dI" % SCHED_TABLE_LEN, blob))
    else:  # 找不到 so 时的常量回退 (实测覆盖后 919 项全部 %5==0 或奇数, 仅第 22 项为 C)
        tbl = [0] * SCHED_TABLE_LEN
        for i in range(SCHED_TABLE_LEN):
            tbl[i] = 0
        # 关键 4 项在下面被覆盖; 第 22 项保持 C 分支
        for i in (0, 1, 4, 7):
            tbl[i] = 0
        tbl[22] = 64
    tbl[0] = ((v20 >> 24) - 0x0A) & 0xFFFFFFFF
    tbl[1] = (((v20 >> 8) & 0xFF) - 0x1F) & 0xFFFFFFFF
    tbl[4] = ((v20 & 0xFF) + 0x1BC) & 0xFFFFFFFF
    tbl[7] = (((v20 >> 16) & 0xFF) + 8) & 0xFFFFFFFF
    return tbl


# ---------------------------------------------------------------- C 分支 (unicorn)
class _Emu:
    """把 libil2cpp.dump.so 的映射段装进 unicorn, 直接执行 0x5e0a740。"""

    def __init__(self, so_path: Path):
        from unicorn import Uc, UC_ARCH_ARM64, UC_MODE_ARM, UC_PROT_ALL
        from unicorn.arm64_const import (
            UC_ARM64_REG_SP, UC_ARM64_REG_X0, UC_ARM64_REG_X1,
            UC_ARM64_REG_X2, UC_ARM64_REG_X3, UC_ARM64_REG_LR,
            UC_ARM64_REG_PC,
        )
        self._R = dict(
            SP=UC_ARM64_REG_SP, X0=UC_ARM64_REG_X0, X1=UC_ARM64_REG_X1,
            X2=UC_ARM64_REG_X2, X3=UC_ARM64_REG_X3, LR=UC_ARM64_REG_LR,
            PC=UC_ARM64_REG_PC,
        )
        img = so_path.read_bytes()
        self.mu = Uc(UC_ARCH_ARM64, UC_MODE_ARM)
        self.mu.mem_map(0, 0x7000000, UC_PROT_ALL)
        self.mu.mem_write(0, img)
        self.stack = 0x200000000
        self.mu.mem_map(self.stack, 0x100000, UC_PROT_ALL)
        self.buf = 0x300000000
        self.mu.mem_map(self.buf, 0x10000, UC_PROT_ALL)
        self.ret = 0x05F00000
        self.mu.mem_write(self.ret, b"\xc0\x03\x5f\xd6")  # ret

    def call(self, fn: int, x0: int, x1: int, w2: int, w3: int) -> int:
        R = self._R
        self.mu.reg_write(R["SP"], self.stack + 0x80000)
        self.mu.reg_write(R["X0"], x0)
        self.mu.reg_write(R["X1"], x1)
        self.mu.reg_write(R["X2"], w2)
        self.mu.reg_write(R["X3"], w3)
        self.mu.reg_write(R["LR"], self.ret)
        self.mu.reg_write(R["PC"], fn)
        self.mu.emu_start(fn, self.ret, count=50_000_000)
        return self.mu.reg_read(R["X0"])

    def block_cipher(self, blob: bytes, k: int) -> bytes:
        self.mu.mem_write(self.buf, blob)
        self.call(FN_BLOCKCIPHER, self.buf, self.buf, len(blob), k)
        return bytes(self.mu.mem_read(self.buf, len(blob)))


# ---------------------------------------------------------------- 主流程
def decrypt(cipher: bytes, so_path: Path | None = None, log=print) -> bytes:
    data = bytearray(cipher)

    # ---- 头部两轮 ----
    seed = struct.unpack_from("<I", data, 0x1C)[0]
    e1 = struct.pack("<I", seed ^ 0x2013BABA)
    e2 = struct.pack("<I", seed ^ 0xEDB88320)
    s1 = rc4_stream(e1, 0x11, 0x200, _ror8)
    for t in range(0x200):
        data[0x20 + t] ^= s1[t]
    s2 = rc4_stream(e2, 0x11, 0x18, _ror8)
    for t in range(0x18):
        data[0x04 + t] ^= s2[t]
    data[0:4] = b"MZ\x90\x00"

    # ---- PE 解析 / 密钥 / 调度表 ----
    pe, _ = parse_pe(data)
    v20 = key_hash()
    keys = derive_block_keys(pe, v20)
    sched = load_sched_table(so_path, v20)

    rsz = pe["SizeOfRawData"]
    rp = pe["PointerToRawData"]
    nblk = (rsz + 0xFF) // 0x100
    kinds = {0: 0, 1: 0, 2: 0}
    cblocks = []
    offsets_a = []
    for blk in range(nblk):
        off = rp + blk * 0x100
        rem = min(0x100, rsz - blk * 0x100)
        k = keys[blk & 15]
        if rem != 0x100:
            st = rc4_stream(struct.pack("<I", k), 8, rem, _rol8)
            for t in range(rem):
                data[off + t] ^= st[t]
            kinds[1] += 1
            continue
        v = sched[blk % SCHED_TABLE_LEN]
        if v % 5 == 0:
            block_type_a(data, off, k)
            kinds[0] += 1
        elif v & 1:
            st = rc4_stream(struct.pack("<I", k), 8, 0x100, _rol8)
            for t in range(0x100):
                data[off + t] ^= st[t]
            kinds[1] += 1
        else:
            cblocks.append((blk, off))
    log("block kinds: A=%d B=%d C=%d (C at %s)" % (
        kinds[0], kinds[1], len(cblocks), [b for b, _ in cblocks][:20]))

    if cblocks:
        if not (so_path and so_path.is_file()):
            raise RuntimeError(
                "%d 个块需要 C 分支 (第二层分组密码), 必须提供 --so libil2cpp.dump.so"
                % len(cblocks))
        emu = _Emu(so_path)
        for blk, off in cblocks:
            data[off : off + 0x100] = emu.block_cipher(bytes(data[off : off + 0x100]),
                                                       keys[blk & 15])

    # ---- 收尾 ----
    data[0x1C:0x20] = b"\0\0\0\0"
    return bytes(data)


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="nhtp container decryptor")
    ap.add_argument("cipher")
    ap.add_argument("-o", "--out")
    ap.add_argument("--plain", help="ground-truth plaintext for byte compare")
    ap.add_argument("--so", default=r"D:\arsbd\devtmp\libil2cpp.dump.so",
                    help="libil2cpp.dump.so (needed for 919 table + C branch)")
    a = ap.parse_args(argv)

    cpath = Path(a.cipher)
    cipher = cpath.read_bytes()
    plain = decrypt(cipher, Path(a.so) if a.so else None)

    out = Path(a.out) if a.out else cpath.with_suffix(".dec.dll")
    out.write_bytes(plain)
    print("wrote %s (%d bytes) md5=%s" % (out, len(plain), hashlib.md5(plain).hexdigest()))

    if a.plain:
        gt = Path(a.plain).read_bytes()
        n = min(len(plain), len(gt))
        same = sum(1 for i in range(n) if plain[i] == gt[i])
        first = next((i for i in range(n) if plain[i] != gt[i]), None)
        print("compare vs %s: len(dec)=%d len(gt)=%d compared=%d same=%d diff=%d"
              % (a.plain, len(plain), len(gt), n, same, n - same))
        print("first diff:", ("0x%x" % first) if first is not None else "none")
        print("dec[:64] :", plain[:64].hex(" "))
        print("gt[:64]  :", gt[:64].hex(" "))
        print("md5 dec=%s gt=%s" % (hashlib.md5(plain).hexdigest(),
                                    hashlib.md5(gt[: len(plain)]).hexdigest()))
        if len(gt) > len(plain):
            print("gt tail %d bytes: %s" % (len(gt) - len(plain),
                                            gt[len(plain):].hex(" ")))
        return 0 if n == same else 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
