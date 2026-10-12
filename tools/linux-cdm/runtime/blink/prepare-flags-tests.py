"""Extract the actual patched arithmetic routines for differential testing."""
from pathlib import Path
import sys

source = Path(sys.argv[1])
text = (source / "blink/uop.c").read_text(encoding="utf-8")
start = text.index("#if X86_INTRINSICS", text.index("// ARITHMETIC"))
end = text.index("MICRO_OP u32 JustMul32", start)
(source / "flags-under-test.inc").write_text(text[start:end], encoding="utf-8", newline="\n")
