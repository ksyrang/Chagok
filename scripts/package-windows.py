"""게시한 Windows EXE로 ZIP과 현재 사용자 설치 EXE를 함께 만든다."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import shutil
import struct
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile


def package(published: Path, output: Path, compiler: str) -> dict:
    root = Path(__file__).resolve().parents[1]
    version = ET.parse(root / 'src/Chagok.Windows/Chagok.Windows.csproj').findtext('.//Version')
    executable = published / 'Chagok.exe'
    data = executable.read_bytes()
    pe = struct.unpack_from('<I', data, 0x3c)[0]
    if data[pe:pe + 4] != b'PE\0\0' or struct.unpack_from('<H', data, pe + 4)[0] != 0x8664:
        raise ValueError('게시 폴더의 Chagok.exe가 Windows x64 파일이 아닙니다.')
    output.mkdir(parents=True, exist_ok=True)
    portable = output / f'Chagok-v{version}-win-x64.zip'
    installer = output / f'Chagok-v{version}-setup-win-x64.exe'
    manifest = output / f'Chagok-v{version}-SHA256SUMS.txt'
    if any(p.exists() for p in [portable, installer, manifest]):
        raise FileExistsError('기존 배포물을 보존했습니다. 비어 있는 다른 출력 폴더를 지정하세요.')
    with tempfile.TemporaryDirectory(prefix='chagok-package-') as temporary:
        stage = Path(temporary)
        payload = stage / 'payload'
        payload.mkdir()
        shutil.copy2(executable, payload / 'Chagok.exe')
        for name in ['LICENSE', 'README.md']:
            shutil.copy2(root / name, payload / name)
        shutil.copy2(root / 'docs/terminal-guide.md', payload / 'terminal-guide.md')
        notice = root / 'src/Chagok.Windows/Assets/Shell/ShellExtension-LICENSES.txt'
        shutil.copy2(notice, payload / notice.name)
        shutil.copy2(root / 'src/Chagok.Windows/Assets/chagok.ico', payload / 'chagok.ico')
        archive = stage / 'portable'
        archive.mkdir()
        for name in ['Chagok.exe', 'LICENSE', 'README.md']:
            shutil.copy2(payload / name, archive / name)
        shutil.copy2(root / 'CHANGELOG.md', archive / 'CHANGELOG.md')
        shutil.copytree(root / 'docs', archive / 'docs', ignore=shutil.ignore_patterns('._*', '__pycache__'))
        target = archive / 'src/Chagok.Windows/Assets/Shell' / notice.name
        target.parent.mkdir(parents=True)
        shutil.copy2(notice, target)
        files = sorted(p for p in archive.rglob('*') if p.is_file())
        checksums = {p.relative_to(archive).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in files}
        (archive / 'SHA256SUMS.txt').write_text(''.join(f'{value}  {name}\n' for name, value in checksums.items()))
        built_zip = stage / portable.name
        with zipfile.ZipFile(built_zip, 'w', zipfile.ZIP_DEFLATED) as z:
            for file in sorted(p for p in archive.rglob('*') if p.is_file()):
                z.write(file, file.relative_to(archive).as_posix())
        with zipfile.ZipFile(built_zip) as z:
            if z.testzip() is not None:
                raise ValueError('ZIP CRC 검사 실패')
            for name, expected in checksums.items():
                if hashlib.sha256(z.read(name)).hexdigest() != expected:
                    raise ValueError('ZIP 파일 해시 불일치: ' + name)
        built_installer = stage / installer.name
        prefix = '/' if os.name == 'nt' else '-'
        subprocess.run([compiler, prefix + 'WX', f'{prefix}DVERSION={version}', f'{prefix}DPAYLOAD={payload}',
                        f'{prefix}DOUTPUT={built_installer}', str(root / 'installer/chagok.nsi')], check=True)
        # 두 형식을 모두 만든 뒤 공개한다. 빌드 실패로 빈 최종 배포물을 남기지 않는다.
        shutil.copy2(built_zip, portable)
        shutil.copy2(built_installer, installer)
    hashes = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in [portable, installer]}
    manifest.write_text(''.join(f'{value}  {name}\n' for name, value in hashes.items()))
    return {'version': version, 'zip': str(portable), 'installer': str(installer), 'sha256': hashes}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('published', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--makensis', default=shutil.which('makensis') or 'makensis')
    args = parser.parse_args()
    print(json.dumps(package(args.published.resolve(), args.output.resolve(), args.makensis), ensure_ascii=False))
