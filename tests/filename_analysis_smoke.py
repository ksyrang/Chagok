# -*- coding: utf-8 -*-
"""CLI/실제 POSIX PTY: 같은 분석 결과, 필터·괄호·목록·규칙 초안·명시 실행."""
from pathlib import Path
import subprocess,tempfile,json,os,pty,fcntl,termios,struct,time,select,sys,re
if len(sys.argv)<3:raise SystemExit('python3 tests/filename_analysis_smoke.py CLI_DLL DOTNET')
base=[sys.argv[2],str(Path(sys.argv[1]).resolve())]
root=Path(tempfile.mkdtemp(prefix='chagok-analysis-')).resolve();data=root/'data';src=root/'source';other=root/'other';target=root/'target'
src.mkdir();other.mkdir()
for name in ['[a] one.pdf','[a][a] two.txt','[A] caps.pdf','[b] single.pdf','plain.txt','(p) one.pdf','(p) two.pdf','{x} one.pdf','{x} two.pdf']:(src/name).write_text(name)
(src/'sub').mkdir();(src/'sub'/'[a] nested.pdf').write_text('nested')
(src/'private').mkdir();(src/'private'/'[a] secret.pdf').write_text('excluded')
(other/'[z] one.txt').write_text('z1');(other/'[z] two.txt').write_text('z2')
def call(args,code=0):
 p=subprocess.run(base+args+['--data-dir',str(data),'--json'],capture_output=True,text=True,timeout=20)
 assert p.returncode==code,(p.returncode,p.stdout,p.stderr)
 return json.loads(p.stdout) if code in (0,4) else p
f=call(['folders','add','--name','SOURCE','--path',str(src),'--exclude',str(src/'private'),'--recent-destinations','on'])
g=call(['folders','add','--name','OTHER','--path',str(other)])
call(['rules','add','--folder',f['Id'],'--name','seed','--destination',str(target),'--match','contains','--pattern','never-match'])
before=(data/'settings.json').read_bytes()
a=call(['analyze','--folder',f['Id'],'--group','[a]'])
assert a['Folders'][0]['Total']==9 and a['Folders'][0]['Matched']==4 and a['FileCount']==2,a
assert a['GroupCount']==3 and a['WarningCount']==0
assert call(['analyze','--folder',f['Id'],'--minimum','2'])['Groups']==[{'Key':'[a]','Count':2}]
assert call(['analyze','--folder',f['Id'],'--recursive','on','--group','[a]'])['FileCount']==3
assert call(['analyze','--folder',f['Id'],'--brackets','round','--group','(p)'])['FileCount']==2
assert call(['analyze','--folder',f['Id'],'--brackets','curly','--group','{x}'])['FileCount']==2
regex=r'\[(?<group>[^\]]+)\]'
assert call(['analyze','--folder',f['Id'],'--regex',regex,'--group','a'])['FileCount']==2
assert call(['analyze','--folder',f['Id'],'--regex',r'(?<group>\.[^.]+)$','--group','.pdf'])['FileCount']==7
call(['analyze','--folder',f['Id'],'--regex','['],2)
all_folders=call(['analyze']);assert len(all_folders['Folders'])==2 and all_folders['GroupCount']==4
call(['analyze','--folder',f['Id'],'--group','[a]','--file-page','2'],2)
assert (data/'settings.json').read_bytes()==before
assert not target.exists() and len(list(src.iterdir()))==11
assert not list((data/'analysis-cache').glob('scan-*'))
m,s=pty.openpty();fcntl.ioctl(s,termios.TIOCSWINSZ,struct.pack('HHHH',30,120,0,0))
env={**os.environ,'TERM':'xterm-256color'};env.pop('NO_COLOR',None)
p=subprocess.Popen(base+['tui','--data-dir',str(data)],stdin=s,stdout=s,stderr=s,env=env);os.close(s);history=b''
def read(needle,timeout=15):
 global history
 buf=b'';end=time.monotonic()+timeout
 while time.monotonic()<end:
  if select.select([m],[],[],.05)[0]:buf+=os.read(m,65536)
  if needle.encode() in buf:
   while select.select([m],[],[],.08)[0]:buf+=os.read(m,65536)
   history+=buf;return buf.decode(errors='replace')
 history+=buf;raise AssertionError((needle,buf.decode(errors='replace')))
def send(value,needle):os.write(m,value.encode());return read(needle)
def compact(s):return re.sub(r'\s|[│]','',re.sub(r'\x1b\[[0-9;?]*[A-Za-z]','',s))
try:
 read('검사 폴더');frame=send('p','C 이 그룹으로 규칙 만들기')
 assert '[a]' in frame and '패턴 일치 4' in frame
 send('?','기능 도움말');send('\x1b','C 이 그룹으로 규칙 만들기')
 send('m','전체 그룹');send('m','2개 이상만')
 send('b','분류 ( )');send('b','분류 { }');send('b','분류 [ ]')
 send('s','하위 폴더 포함');frame=send('s','하위 폴더 제외')
 # 정규식 입력 방법/미리보기, 잘못된 입력 저장 차단, 취소 후 기존 결과 유지.
 send('e','분류 정규식');send('e','입력 · 분류 정규식')
 send('[\r','항목 선택');send('s','다시 분석할까요');send('y','처리 실패')
 send('e','입력 · 분류 정규식');send(regex+'\r','항목 선택')
 send('3','미리보기 그룹');preview=send('i','전체 값 · 미리보기 그룹');assert 'a' in preview
 send('\x1b','항목 선택');send('s','다시 분석할까요');frame=send('y','직접 입력:')
 assert '패턴 일치 4' in frame
 send('e','분류 정규식');send('\x1b','C 이 그룹으로 규칙 만들기')
 # 작은 창에서는 활성 영역 하나를 보이며 Tab/전체 경로 조회를 유지한다.
 fcntl.ioctl(m,termios.TIOCSWINSZ,struct.pack('HHHH',24,70,0,0))
 send('\t','구분값');send('\t','파일 목록')
 detail=send('i','분석 상세');assert str(src) in compact(detail)
 send('\x1b','C 이 그룹으로 규칙 만들기')
 # 원본·설정이 분석/필터 변경만으로 바뀌지 않는다.
 assert (data/'settings.json').read_bytes()==before and not target.exists()
 fcntl.ioctl(m,termios.TIOCSWINSZ,struct.pack('HHHH',30,120,0,0))
 send('c','항목 선택');send('s','위 검사 폴더에 규칙을 저장할까요');send('y','규칙 저장 완료')
 settings=json.loads((data/'settings.json').read_text());rule=next(x for x in settings['Folders'] if x['Id']==f['Id'])['Rules'][-1]
 assert rule['Match']=='Group' and rule['Pattern']=='a' and rule['GroupExpression']==regex and rule['Destination']==str(target) and not rule['Recursive']
 assert not target.exists()
 os.write(m,b'q');p.wait(timeout=10);assert p.returncode==0
finally:
 if p.poll() is None:p.terminate();p.wait(timeout=5)
 os.close(m);(root/'tui-output.txt').write_bytes(history)
assert not list((data/'analysis-cache').glob('scan-*'))
scan=call(['scan','--folder',f['Id'],'--rule',rule['Id']]);assert len(scan['entries'])==2
call(['run','--folder',f['Id'],'--rule',rule['Id'],'--yes'])
assert (target/'[a] one.pdf').exists() and (target/'[a][a] two.txt').exists()
assert (src/'[A] caps.pdf').exists() and (src/'sub'/'[a] nested.pdf').exists() and (src/'private'/'[a] secret.pdf').exists()
print(json.dumps({'cliGuiSharedEngine':True,'filtersAndBrackets':True,'analysisDoesNotMutate':True,'tuiThreePanesAndSmallWindow':True,'groupToRuleDraftAndConfirmedMove':True,'customRegexPreviewAndValidation':True,'cacheCleanup':True,'evidence':str(root)}))
