# -*- coding: utf-8 -*-
"""실제 PTY: 시작 화면·임시 검사·별칭·복수 목적지·그룹·명시 이동·설정 보존."""
from pathlib import Path
import subprocess,tempfile,json,os,pty,fcntl,termios,struct,time,select,sys,shutil
if len(sys.argv)<2:raise SystemExit('사용법: python3 reference_tui_smoke.py CLI_DLL [DOTNET]')
sdk=sys.argv[2] if len(sys.argv)>2 else shutil.which('dotnet');dll=str(Path(sys.argv[1]).resolve())
root=Path(tempfile.mkdtemp(prefix='chagok-reference-tui-',dir='/private/tmp' if sys.platform=='darwin' else None)).resolve()
data=root/'data';registered=root/'registered';source=root/'temporary';sorted_root=root/'sorted';a=sorted_root/'거래처A';b=sorted_root/'거래처B'
for folder in [registered,source,a,b]:folder.mkdir(parents=True)
base=[sdk,dll]
def call(args):
 p=subprocess.run(base+args+['--data-dir',str(data),'--json'],capture_output=True,text=True,timeout=15)
 assert p.returncode==0,p.stderr
 return json.loads(p.stdout)
call(['folders','add','--name','REGISTERED','--path',str(registered)])
(registered/'keep.txt').write_text('registered untouched')
for name in ['거래처A_견적.pdf','[A]별칭.pdf','거래처A_거래처B_공통.pdf','[C] one.pdf','[C] two.pdf','plain.txt']:(source/name).write_text(name)
before=(data/'settings.json').read_bytes()
m,s=pty.openpty();fcntl.ioctl(s,termios.TIOCSWINSZ,struct.pack('HHHH',26,110,0,0))
env={**os.environ,'TERM':'xterm-256color','NO_COLOR':'1'}
p=subprocess.Popen(base+['tui','--data-dir',str(data)],stdin=s,stdout=s,stderr=s,env=env);os.close(s);history=b''
def read(needle,timeout=15):
 global history
 buf=b'';end=time.monotonic()+timeout
 while time.monotonic()<end:
  if select.select([m],[],[],.05)[0]:
   try:buf+=os.read(m,65536)
   except OSError:break
  if needle.encode() in buf:
   while select.select([m],[],[],.08)[0]:buf+=os.read(m,65536)
   history+=buf;return buf.decode(errors='replace')
 history+=buf
 raise AssertionError((needle,buf.decode(errors='replace')))
def send(keys,needle):os.write(m,keys.encode());return read(needle)
def edit(index,label,value):
 send(str(index),'항목 선택');send('e','입력 · '+label)
 return send(value+'\r','항목 선택')
try:
 read('정리 방식 선택');send('1','선택 규칙');send('o','정리 방식 선택')
 send('2','기준 폴더 정리 설정')
 edit(1,'검사할 폴더 전체 경로',str(source));edit(2,'정리된 기준 폴더 전체 경로',str(sorted_root))
 send('s','하위 목적지 폴더를 가져올까요');send('y','기준 폴더 · 목적지 검색어')
 send('e','목적지 검색어 편집');edit(2,'검색어·별칭 (; 구분)','거래처A;[A]')
 send('s','이번 분석에 검색어를 적용할까요');send('y','기준 폴더 · 목적지 검색어')
 frame=send('r','기준 폴더 정리 결과');assert '전체 6' in frame and '결정 2' in frame
 assert (data/'settings.json').read_bytes()==before and len(list(source.iterdir()))==6
 send('f','목적지 결정');send('f','선택 필요')
 detail=send('i','분석 상세');assert str(a) in detail and str(b) in detail
 send('\x1b','기준 폴더 정리 결과');send('d','목적지 지정')
 send('2','기존 목적지');send('\x1b[C','기존 목적지')
 send('s','이 파일의 목적지를 지정할까요');send('y','기준 폴더 정리 결과')
 frame=send('\t','새 그룹 · 목적지 없는 파일');assert '[C]' in frame
 send('d','목적지 지정');edit(3,'새 폴더명','새거래처C')
 send('s','그룹의 미분류 2개');send('y','새 그룹 · 목적지 없는 파일')
 assert not (sorted_root/'새거래처C').exists()
 send('v','목적지 결정 5개를 검토');frame=send('y','검사 및 실행');assert '기준 폴더 · 확정 목적지' in frame
 send('\r','전체 페이지의 선택 5개');send('n','검사 및 실행');assert len(list(source.iterdir()))==6
 send('\r','전체 페이지의 선택 5개');send('y','아무 키나 눌러 결과 확인')
 assert (a/'거래처A_견적.pdf').exists() and (a/'[A]별칭.pdf').exists()
 assert (b/'거래처A_거래처B_공통.pdf').exists()
 assert len(list((sorted_root/'새거래처C').iterdir()))==2
 assert [f.name for f in source.iterdir()]==['plain.txt']
 assert (registered/'keep.txt').read_text()=='registered untouched'
 assert (data/'settings.json').read_bytes()==before
 send(' ','파일별 처리 결과');send('\r','정리 방식 선택')
 frame=send('2','기준 폴더 정리 설정');assert str(registered) in frame
 send('\x1b','정리 방식 선택');os.write(m,b'q');p.wait(timeout=10);assert p.returncode==0
 assert not list((data/'scan-cache').glob('scan-*'))
 print(json.dumps({'startRoutes':True,'temporarySourcePreservesSettings':True,'aliases':True,'ambiguousDestinationChosen':True,'groupAssignAndNewFolder':True,'reviewCancelAndExplicitMove':True,'cacheCleanup':True,'evidence':str(root)}))
finally:
 if p.poll() is None:p.terminate();p.wait(timeout=5)
 os.close(m);(root/'tui-output.txt').write_bytes(history)
