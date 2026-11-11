#!/usr/bin/env python3
"""Turn a vaddr-indexed libil2cpp memory dump into an Il2CppDumper-loadable ELF.

Usage: python patch_il2cpp_dump.py dump.bin libil2cpp.dump.so [--pad-to-extent]

* requires the dump to be written at file offset == (mapping_start - module_base)
* rewrites every PT_LOAD: p_offset = p_vaddr, p_filesz = p_memsz
* zeroes e_shoff / e_shnum / e_shstrndx so Il2CppDumper's CheckSection() fails
  and it takes the "this may be a dump file" path (asks for the load base)
"""
import struct, sys, os

PT_LOAD = 1
EHDR = '<16sHHIQQQIHHHHHH'
PHDR = '<IIQQQQQQ'


def main():
    src, dst = sys.argv[1], sys.argv[2]
    buf = bytearray(open(src, 'rb').read())
    if buf[:4] != b'\x7fELF' or buf[4] != 2 or buf[5] != 1:
        sys.exit('not a little-endian ELF64 dump')
    (e_phoff,) = struct.unpack_from('<Q', buf, 32)
    (e_shoff,) = struct.unpack_from('<Q', buf, 40)
    e_phnum, = struct.unpack_from('<H', buf, 56)
    e_shnum, e_shstrndx = struct.unpack_from('<HH', buf, 60)

    extent = 0
    for i in range(e_phnum):
        o = e_phoff + i * 56
        p_type, p_flags, p_off, p_vaddr, p_paddr, p_fsz, p_msz, p_align = \
            struct.unpack_from(PHDR, buf, o)
        if p_type != PT_LOAD:
            continue
        struct.pack_into('<Q', buf, o + 8, p_vaddr)    # p_offset = p_vaddr
        struct.pack_into('<Q', buf, o + 32, p_msz)     # p_filesz = p_memsz
        extent = max(extent, p_vaddr + p_msz)
        print('PT_LOAD vaddr=%#x memsz=%#x -> off=%#x filesz=%#x' % (p_vaddr, p_msz, p_vaddr, p_msz))

    extent = (extent + 0xFFF) & ~0xFFF
    if len(buf) < extent:
        print('pading %#x -> %#x' % (len(buf), extent))
        buf.extend(b'\x00' * (extent - len(buf)))
    elif len(buf) > extent:
        print('truncating %#x -> %#x' % (len(buf), extent))
        del buf[extent:]

    struct.pack_into('<Q', buf, 40, 0)   # e_shoff
    struct.pack_into('<H', buf, 60, 0)   # e_shnum
    struct.pack_into('<H', buf, 62, 0)   # e_shstrndx
    open(dst, 'wb').write(buf)
    print('wrote %s size=%#x (extent=%#x) old e_shoff=%#x shnum=%d shstrndx=%d'
          % (dst, len(buf), extent, e_shoff, e_shnum, e_shstrndx))


if __name__ == '__main__':
    main()
