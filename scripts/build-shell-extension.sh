#!/usr/bin/env bash
set -euo pipefail
task_root="$(cd "$(dirname "$0")/.." && pwd)"
task_zig="${CHAGOK_ZIG:-zig}"
export ZIG_GLOBAL_CACHE_DIR="${ZIG_GLOBAL_CACHE_DIR:-${TMPDIR:-/tmp}/chagok-zig-global-cache}"
export ZIG_LOCAL_CACHE_DIR="${ZIG_LOCAL_CACHE_DIR:-${TMPDIR:-/tmp}/chagok-zig-local-cache}"
task_build="$(mktemp -d "${TMPDIR:-/tmp}/chagok-shell.XXXXXX")"
trap 'rm -rf -- "$task_build"' EXIT
"$task_zig" c++ -target x86_64-windows-gnu -shared -O2 -s -fno-exceptions -fno-rtti -nostdlib++ -Wall -Wextra -Werror \
 "$task_root/src/Chagok.ShellExtension/ShellExtension.cpp" "$task_root/src/Chagok.ShellExtension/Exports.def" \
 -lole32 -lshell32 -ladvapi32 -luuid -o "$task_build/Chagok.ShellExtension.dll"
# 외장 드라이브에 링커가 직접 출력한 파일이 나중에 0으로 변한 사례가 있어,
# 임시 디스크에서 완료한 PE를 일반 쓰기·동기화로 복사하고 해시를 재확인한다.
python3 - "$task_build/Chagok.ShellExtension.dll" "$task_root/src/Chagok.Windows/Assets/Shell/Chagok.ShellExtension.dll" <<'PY'
from pathlib import Path
import hashlib, os, struct, sys
source, destination = map(Path, sys.argv[1:])
data = source.read_bytes()
assert data[:2] == b'MZ'
pe = struct.unpack_from('<I', data, 60)[0]
assert data[pe:pe + 4] == b'PE\0\0' and struct.unpack_from('<H', data, pe + 4)[0] == 0x8664
temporary = destination.with_suffix('.dll.copying')
with temporary.open('wb') as output:
    output.write(data)
    output.flush()
    os.fsync(output.fileno())
os.replace(temporary, destination)
assert hashlib.sha256(destination.read_bytes()).digest() == hashlib.sha256(data).digest()
print('Native DLL SHA256:', hashlib.sha256(data).hexdigest())
PY
