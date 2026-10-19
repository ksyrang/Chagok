# -*- coding: utf-8 -*-
from pathlib import Path
import subprocess,tempfile,json,os,pty,fcntl,termios,struct,time,select,sys,shutil
sdk=sys.argv[2] if len(sys.argv)>2 else shutil.which('dotnet')
if not sdk: raise SystemExit('dotnet 경로를 두 번째 인수로 지정하세요.')
if len(sys.argv)<2: raise SystemExit('사용법: python3 tests/terminal_smoke.py CLI_DLL [DOTNET]\nmacOS/POSIX 개발 검증용입니다.')
cli=str(Path(sys.argv[1]).resolve())
root=Path(tempfile.mkdtemp(prefix='chagok-cli-e2e-')).resolve();data=root/'data';src=root/'source';dst=root/'target';clone=root/'clone'
for p in [src,dst,clone,src/'private']:p.mkdir(parents=True,exist_ok=True)
base=[sdk,cli];checks=0

def call(args,code=0):
 global checks
 p=subprocess.run(base+args+['--data-dir',str(data),'--json'],capture_output=True,text=True,timeout=20)
 assert p.returncode==code,(args,p.returncode,p.stdout,p.stderr)
 checks+=1
 return json.loads(p.stdout) if p.stdout.strip() else json.loads(p.stderr)

folder=call(['folders','add','--name','다운로드','--path',str(src),'--exclude',str(src/'private')]);fid=folder['Id']
rule=call(['rules','add','--folder',fid,'--name','PDF 정리','--destination',str(dst),'--match','any','--extensions','.pdf']);rid=rule['Id']
call(['settings','set','--theme','dark','--close','exit','--auto-menu','off'])
call(['rules','update','--folder',fid,'--rule',rid,'--recursive','on'])
settings=call(['settings','show']);assert settings['Theme']=='Dark' and settings['CloseAction']=='Exit' and not settings['AutoRegisterExplorerMenu']
for i in range(105):(src/f'{i:03}.pdf').write_text(str(i))
(src/'skip.jpg').write_text('image');(src/'private'/'private.pdf').write_text('keep')
scan=call(['scan','--folder',fid,'--page','2']);assert scan['total']==105 and len(scan['entries'])==5
assert (src/'000.pdf').exists() and not (dst/'000.pdf').exists()
call(['run','--folder',fid],2)
assert (src/'000.pdf').exists()
report=call(['run','--folder',fid,'--yes']);assert report['Completed']==105 and report['Failed']==0
assert (dst/'104.pdf').read_text()=='104' and (src/'skip.jpg').exists() and (src/'private'/'private.pdf').exists()
call(['scan','--folder',fid,'--forlder','typo'],2)
copied=call(['folders','clone','--folder',fid,'--name','복사본','--path',str(clone)])
assert copied['Id']!=fid and copied['Rules'][0]['Id']!=rid and copied['ExcludedFolders']==[str(clone/'private')]
assert copied['Rules'][0]['Destination']==str(dst)
call(['folders','remove','--folder',copied['Id']],2)
call(['folders','remove','--folder',copied['Id'],'--yes'])
assert clone.exists()
assert any(r['Status']=='완료' for r in call(['history','--limit','5']))
# 비 Windows 교체 실패도 기존 파일과 원본을 보존해야 한다.
(src/'replace.pdf').write_text('new');(dst/'replace.pdf').write_text('old')
call(['rules','update','--folder',fid,'--rule',rid,'--conflict','replace'])
failed=call(['run','--folder',fid,'--yes'],4)
assert failed['Failed']==1 and (src/'replace.pdf').read_text()=='new' and (dst/'replace.pdf').read_text()=='old'
(src/'replace.pdf').unlink()
call(['rules','update','--folder',fid,'--rule',rid,'--conflict','rename'])
for name in ['tui-a.pdf','tui-b.pdf','tui-c.pdf']:(src/name).write_text(name)
# 실제 TUI 프로세스가 보유한 잠금과 커널에 의한 비정상 종료 후 해제 확인.
master,slave=pty.openpty();fcntl.ioctl(slave,termios.TIOCSWINSZ,struct.pack('HHHH',32,110,0,0))
proc=subprocess.Popen(base+['--data-dir',str(data)],stdin=slave,stdout=slave,stderr=slave,env={**os.environ,'TERM':'xterm-256color'});os.close(slave)
buffer=b''
try:
 for _ in range(100):
  if select.select([master],[],[],.1)[0]:buffer+=os.read(master,65536)
  if '검사 폴더'.encode() in buffer:break
 assert proc.poll() is None and '검사 폴더'.encode() in buffer,buffer.decode(errors='replace')
 before=(data/'settings.json').read_bytes();call(['folders','list'],3);assert (data/'settings.json').read_bytes()==before
 # Enter: 규칙 화면, R: 미리보기, Esc: 복귀, H: 기록; 종료는 여기서 프로세스 강제 종료로 검증.
 os.write(master,b'\r');time.sleep(.2);os.write(master,b'r');time.sleep(.3)
 while select.select([master],[],[],.1)[0]:buffer+=os.read(master,65536)
 assert '검사 및 실행'.encode() in buffer,buffer.decode(errors='replace')
 os.write(master,b' ');time.sleep(.1);os.write(master,b'i');time.sleep(.15)
 while select.select([master],[],[],.1)[0]:buffer+=os.read(master,65536)
 assert '파일 상세'.encode() in buffer and str(src/'tui-a.pdf').encode() in buffer
 os.write(master,b'\x1b');time.sleep(.1);os.write(master,b'\r');time.sleep(.1);os.write(master,b'y');time.sleep(.5)
 while select.select([master],[],[],.1)[0]:buffer+=os.read(master,65536)
 assert (src/'tui-a.pdf').exists() and not (dst/'tui-a.pdf').exists()
 assert not (src/'tui-b.pdf').exists() and (dst/'tui-b.pdf').exists() and (dst/'tui-c.pdf').exists()
 proc.kill();proc.wait(timeout=10)
finally:
 if proc.poll() is None:proc.terminate();proc.wait(timeout=10)
 os.close(master)
call(['folders','list'])
(root/'tui-output.txt').write_bytes(buffer)
print(json.dumps({'cliCommandsPassed':checks,'filesMoved':105,'secondPageRows':5,'selectionSharedSettings':True,'crossProcessLock':True,'crashRelease':True,'tuiNavigation':True,'evidence':str(root)},ensure_ascii=False))
