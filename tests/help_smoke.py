# -*- coding: utf-8 -*-
"""CLI 기능 설명은 설정/잠금/파일 이동 없이 읽을 수 있어야 한다."""
from pathlib import Path
import subprocess, tempfile, json, sys, shutil
if len(sys.argv)<2:raise SystemExit('사용법: python3 tests/help_smoke.py CLI_DLL [DOTNET]')
sdk=sys.argv[2] if len(sys.argv)>2 else shutil.which('dotnet')
if not sdk:raise SystemExit('dotnet 경로를 지정하세요.')
root=Path(tempfile.mkdtemp(prefix='chagok-help-')).resolve();data=root/'must-not-be-created'
base=[sdk,str(Path(sys.argv[1]).resolve())];checks=0

def call(args,code=0):
 global checks
 p=subprocess.run(base+args+['--data-dir',str(data)],input='',capture_output=True,text=True,timeout=15)
 assert p.returncode==code,(args,p.returncode,p.stdout,p.stderr)
 assert not data.exists(),'도움말이 데이터 폴더를 만들었습니다.'
 checks+=1
 return json.loads(p.stdout if code==0 else p.stderr)

catalog=call(['help','--json']);assert catalog['schemaVersion']==1 and len(catalog['commands'])==18
assert catalog==json.loads((Path(__file__).resolve().parents[1]/'docs/cli-reference.json').read_text())
for command in catalog['commands']:
 topic=command['name'].split()
 a=call(['help']+topic+['--json']);b=call(topic+['--help','--json'])
 assert a==b and a['commands'][0]['name']==command['name'], command['name']
 assert command['usage'] and command['description'] and command['effects'] and command['confirmation']
call(['help','unknown','--json'],2)
call(['run','--help','--yes','--json'],2)
assert call(['--version','--json'])['version']==catalog['version']
print(json.dumps({'helpCallsPassed':checks,'all18CommandsDescribed':True,'exportMatchesRuntime':True,'noSettingsOrLockCreated':True},ensure_ascii=False))
