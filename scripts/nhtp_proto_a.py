#!/usr/bin/env python3
# nhtp decryptor prototype — Tier2 impl A (independent rebuild)
#
# 依据: libil2cpp @0x5e58e1c (nhtp) 反汇编
#   1) KSA: j=(j+S[i]+extra[i&3])&0xff, swap, S 初值 = 恒等 0..255, i:0..255
#   2) PRGA: i=(i+1)&0xff; j=(j+S[i])&0xff; swap; k=S[(S[i]+S[j])&0xff];
#            ks = (ror8(k,2)+0x11)&0xff  (header 段)   / (rol8(k,2)+0x08)&0xff (block B)
#   3) header: [0x20,0x220) 用 seed^0x2013BABA; [4,0x1c) 用 seed^0xEDB88320
#   4) block 密钥 K[16]: state=0x2016DEAD; 每项 state+=n+0x1212; CRC-32C 变体
#   5) 每 256B 块: t=T[blk%919] -> t%5==0: A(32bit 异或链) / t&1: B(RC4变体) / else C
import struct, hashlib

SO    = r'D:\arsbd\devtmp\libil2cpp.dump.so'
CIPH  = r'D:\arsbd\extracted_dll\Assembly-CSharp.dll'
PLAIN = r'D:\arsbd\extracted_dll\Assembly-CSharp.plain.dll'
T919_OFF  = 0x5e9bf90
HASH_OFF  = 0x5e9b230
KEY       = b'hellocwhguochan007'

# ---------------- RC4-like ----------------
def ksa(extra):
    S = list(range(256)); j = 0
    for i in range(256):
        j = (j + S[i] + ((extra >> (8*(i & 3))) & 0xff)) & 0xff
        S[i], S[j] = S[j], S[i]
    return S

def prga(S, n, mode, c):
    out = bytearray(); i = j = 0
    for _ in range(n):
        i = (i + 1) & 0xff
        j = (j + S[i]) & 0xff
        S[i], S[j] = S[j], S[i]
        k = S[(S[i] + S[j]) & 0xff]
        v = ((k >> 2) | ((k & 3) << 6)) if mode == 'ror2' else (((k << 2) & 0xff) | (k >> 6))
        out.append((v + c) & 0xff)
    return out

# ---------------- CRC 工具 ----------------
def crc_table(poly):
    t = []
    for i in range(256):
        x = i
        for _ in range(8):
            x = (x >> 1) ^ (poly if x & 1 else 0)
        t.append(x & 0xffffffff)
    return t

def crc_fold(buf, tab, add):
    crc = 0xffffffff
    for b in buf:
        crc = tab[(crc & 0xff) ^ b] ^ (crc >> 8)
        crc = (crc + add) & 0xffffffff
    return crc

# ---------------- header ----------------
def decrypt_header(c, seed):
    c = bytearray(c)
    for rng, extra, mode, cc in (((0x20, 0x220), seed ^ 0x2013baba, 'ror2', 0x11),
                                 ((0x04, 0x1c), seed ^ 0xedb88320, 'ror2', 0x11)):
        ks = prga(ksa(extra), rng[1]-rng[0], mode, cc)
        for i, x in enumerate(range(*rng)):
            c[x] ^= ks[i]
    c[0:4] = struct.pack('<I', 0x00905a4d)     # str w8=0x00905a4d,[x19]
    c[0x1c:0x20] = b'\0'*4                     # str wzr,[x19,#0x1c]
    return c

def pe_code_section(c):
    e = struct.unpack_from('<I', c, 0x3c)[0]
    if c[e:e+4] != b'PE\0\0':
        raise SystemExit('PE sig not found @%x' % e)
    nsec  = struct.unpack_from('<H', c, e+6)[0]
    optsz = struct.unpack_from('<H', c, e+0x14)[0]
    off = e + 0x18 + optsz
    for _ in range(nsec):
        ch = struct.unpack_from('<I', c, off+0x24)[0]
        if ch & 0x20:                                    # IMAGE_SCN_CNT_CODE
            vsize, va, rsize, rptr = struct.unpack_from('<IIII', c, off+8)
            return dict(e=e, hdr=off, vsize=vsize, rsize=rsize, rptr=rptr)
        off += 0x28
    raise SystemExit('no code section')

# ---------------- K[16] / T[919] ----------------
def build_keys(h, sec):
    so = open(SO, 'rb').read()
    poly, add = struct.unpack('<II', so[HASH_OFF:HASH_OFF+8])
    tab = crc_table(poly)
    fin = 0x1edc6f41
    u = lambda o: struct.unpack_from('<I', h, o)[0]
    base = bytearray(struct.pack('<8I', u(sec['e']+0x50), 0, u(sec['e']+0x30),
                                 u(sec['e']+8), u(sec['e']+0x58),
                                 u(sec['hdr']+8), u(sec['hdr']+0x10),
                                 u(sec['e']+0x1c)))
    K = []
    for i in range(16):
        b = bytearray(base)
        struct.pack_into('<I', b, 4, (0x2016dead + 0x1212*(i+1) + i*(i+1)//2) & 0xffffffff)
        K.append((fin - crc_fold(b, tab, add) - 1) & 0xffffffff)
    # w20: key 的 CRC -> 覆盖 919 表 4 项 (栈上 sp+0x1b4 与 hash buffer 重叠)
    w20 = (0xf0b6c238 - crc_fold(KEY, tab, add) + fin) & 0xffffffff
    T = list(struct.unpack_from('<919I', so, T919_OFF))
    T[0] = ((w20 >> 24) - 0x0a) & 0xffffffff
    T[1] = (((w20 >> 8) & 0xff) - 0x1f) & 0xffffffff
    T[4] = ((w20 & 0xff) + 0x1bc) & 0xffffffff
    T[7] = (((w20 >> 16) & 0xff) + 8) & 0xffffffff
    return K, T, w20

# ---------------- 分支 A: 32-bit 异或链 (0x5e59ebc) ----------------
def path_A(blk, E):
    d = bytearray(blk)
    W = lambda o: struct.unpack_from('<I', d, o)[0]
    w = (W(0) ^ E ^ W(0xfc)) & 0xffffffff
    struct.pack_into('<I', d, 0xfc, w)
    for j in range(62, -1, -1):          # j=62..0 (cmp w9,#0 后 b.gt → 含 j=0)
        w = (w ^ E ^ W(4*j)) & 0xffffffff
        struct.pack_into('<I', d, 4*j, w)
    return bytes(d)

# ---------------- 分支 B: RC4 变体 rol2 + 8 ----------------
def path_B(blk, E):
    ks = prga(ksa(E), len(blk), 'rol2', 8)
    return bytes(a ^ b for a, b in zip(blk, ks))

# ---------------- 分支 C: 0x5e0aa44 -> tail-call 0x5e0a740(dst,src,n,w3=extra) ----
_uc = {}
def path_C(blk, E):
    """用 unicorn 直接跑 .so 里的 0x5e0a740 —— 该分支是 16B 分组密码, 未手写实现"""
    from unicorn import Uc, UcError, UC_ARCH_ARM64, UC_MODE_ARM, UC_HOOK_CODE
    from unicorn.arm64_const import (UC_ARM64_REG_SP, UC_ARM64_REG_X0, UC_ARM64_REG_X1,
                                     UC_ARM64_REG_X2, UC_ARM64_REG_X3, UC_ARM64_REG_LR,
                                     UC_ARM64_REG_PC, UC_ARM64_REG_TPIDR_EL0)
    if not _uc:
        so = open(SO, 'rb').read()
        size = (len(so) + 0xfffff) & ~0xfffff
        mu = Uc(UC_ARCH_ARM64, UC_MODE_ARM)
        mu.mem_map(0, size, 7)
        mu.mem_write(0, so)
        mu.mem_map(0x70000000, 0x200000, 7)          # stack
        _uc['mu'] = mu
        _uc['buf'] = 0x60000000
        mu.mem_map(_uc['buf'], 0x10000, 7)
        try:
            mu.reg_write(UC_ARM64_REG_TPIDR_EL0, 0x70000000)
        except Exception:
            pass
    mu = _uc['mu']
    mu.mem_write(_uc['buf'], bytes(blk))
    mu.reg_write(UC_ARM64_REG_SP, 0x70100000)
    mu.reg_write(UC_ARM64_REG_X0, _uc['buf'])
    mu.reg_write(UC_ARM64_REG_X1, _uc['buf'])
    mu.reg_write(UC_ARM64_REG_X2, len(blk))
    mu.reg_write(UC_ARM64_REG_X3, E)
    mu.reg_write(UC_ARM64_REG_LR, 0xFFFFFFFF)        # 哨兵返回地址
    mu.emu_start(0x5e0a740, 0xFFFFFFFF, count=2000000)
    return bytes(mu.mem_read(_uc['buf'], len(blk)))

def main():
    c = bytearray(open(CIPH, 'rb').read())
    p = open(PLAIN, 'rb').read()
    seed = struct.unpack_from('<I', c, 0x1c)[0]
    print('seed = %08x' % seed)
    h = decrypt_header(c, seed)
    print('hdr[0:32] ', h[0:32].hex(' '))
    print('pln[0:32] ', p[0:32].hex(' '))
    print('header 0..0x200 identical:', h[0:0x200] == p[0:0x200])
    sec = pe_code_section(h)
    print('code sec: rptr=%#x rsize=%#x vsize=%#x' % (sec['rptr'], sec['rsize'], sec['vsize']))
    K, T, w20 = build_keys(h, sec)
    print('w20 = %08x' % w20)
    print('K =', ['%08x' % x for x in K])
    print('T[0:8] =', T[:8], ' T[22]=', T[22])
    out = bytearray(h)
    kinds = {'A': 0, 'B': 0, 'C': 0}
    nb = sec['rsize'] // 256
    for j in range(nb):
        off = sec['rptr'] + 256*j
        blk = bytes(out[off:off+256])
        E = K[j & 15]
        t = T[j % 919]
        if t % 5 == 0:
            kinds['A'] += 1; out[off:off+256] = path_A(blk, E)
        elif t & 1:
            kinds['B'] += 1; out[off:off+256] = path_B(blk, E)
        else:
            kinds['C'] += 1; out[off:off+256] = path_C(blk, E)
    print('block kinds:', kinds, 'blocks', nb)
    n = min(len(out), len(p))
    diff = [i for i in range(n) if out[i] != p[i]]
    print('compared=%d same=%d diff=%d' % (n, n-len(diff), len(diff)))
    if diff:
        print('first diff %#x' % diff[0])
        rngs = []
        s = diff[0]; prev = diff[0]
        for x in diff[1:]:
            if x != prev+1:
                rngs.append((s, prev)); s = x
            prev = x
        rngs.append((s, prev))
        print('diff runs:', [(hex(a), hex(b)) for a, b in rngs[:30]], 'total runs', len(rngs))
    print('md5 out   ', hashlib.md5(bytes(out[:n])).hexdigest())
    print('md5 plain ', hashlib.md5(p[:n]).hexdigest())
    open(r'D:\arsbd\tools\_nhtp_out_a.bin', 'wb').write(bytes(out[:n])) if False else None

if __name__ == '__main__':
    main()
