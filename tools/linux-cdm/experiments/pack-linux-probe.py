"""Wrap an assembled .text binary in a minimal Linux x86-64 ELF executable."""
import pathlib
import struct
import sys

code = pathlib.Path(sys.argv[1]).read_bytes()
base = 0x200000000000
size = 120 + len(code)
ident = b"\x7fELF" + bytes([2, 1, 1]) + bytes(9)
header = struct.pack("<16sHHIQQQIHHHHHH", ident, 2, 62, 1, base + 120,
                     64, 0, 0, 64, 56, 1, 0, 0, 0)
segment = struct.pack("<IIQQQQQQ", 1, 7, 0, base, base, size, size, 4096)
pathlib.Path(sys.argv[2]).write_bytes(header + segment + code)
