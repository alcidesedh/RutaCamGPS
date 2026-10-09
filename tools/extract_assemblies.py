"""Extrae los ensamblados .NET (DLL/PDB) embebidos en los ELF lib_*.so de un APK .NET for Android / MAUI (.NET 9+).

Cada lib_<Nombre>.dll.so es un ELF con una sección 'payload' que contiene el PE/PDB original
(opcionalmente comprimido con LZ4 bajo la cabecera 'XALZ').
"""
import struct
import sys
from pathlib import Path


def elf_payload(data: bytes) -> bytes | None:
    if data[:4] != b"\x7fELF" or data[4] != 2:  # solo ELF64
        return None
    e_shoff = struct.unpack_from("<Q", data, 0x28)[0]
    e_shentsize, e_shnum, e_shstrndx = struct.unpack_from("<HHH", data, 0x3A)
    def sh(i):
        off = e_shoff + i * e_shentsize
        name, _type, _flags, _addr, offset, size = struct.unpack_from("<IIQQQQ", data, off)
        return name, offset, size
    _, stroff, _ = sh(e_shstrndx)
    for i in range(e_shnum):
        name, offset, size = sh(i)
        end = data.index(b"\0", stroff + name)
        if data[stroff + name:end] == b"payload":
            return data[offset:offset + size]
    return None


def maybe_decompress(buf: bytes) -> bytes:
    if buf[:4] != b"XALZ":
        return buf
    try:
        import lz4.block  # pip install lz4
    except ImportError:
        print("  [!] payload comprimido XALZ: instala 'lz4' para descomprimir")
        return buf
    size = struct.unpack_from("<I", buf, 8)[0]
    return lz4.block.decompress(buf[12:], uncompressed_size=size)


def main(src: str, dst: str) -> None:
    out = Path(dst)
    out.mkdir(parents=True, exist_ok=True)
    for so in sorted(Path(src).glob("lib*_*.so")):
        name = so.name
        if not (name.endswith(".dll.so") or name.endswith(".pdb.so")):
            continue
        payload = elf_payload(so.read_bytes())
        if payload is None:
            print(f"  [-] sin payload: {name}")
            continue
        payload = maybe_decompress(payload)
        # lib_Foo.dll.so -> Foo.dll ; lib-es_Foo.resources.dll.so -> es/Foo.resources.dll
        base = name[:-3]
        prefix, _, asm = base.partition("_")
        culture = prefix[4:] if prefix.startswith("lib-") else ""
        target = out / culture / asm if culture else out / asm
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(payload)
    print(f"Listo -> {out.resolve()}")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "unpacked/lib/arm64-v8a",
         sys.argv[2] if len(sys.argv) > 2 else "assemblies")
