# -*- coding: utf-8 -*-
"""실제 좁은 PTY: 등록 폴더 없음·205건 페이지·전체 그룹 지정·실행 취소."""
from pathlib import Path
import subprocess,tempfile,os,pty,fcntl,termios,struct,time,select,sys,json
base=[sys.argv[2],str(Path(sys.argv[1]).resolve())]
root=Path(tempfile.mkdtemp(prefix='chagok-reference-pages-',dir='/private/tmp' if sys.platform=='darwin' else None)).resolve()
data=root/'data';source=root/'source';sorted_root=root/'sorted';source.mkdir();(sorted_root/'known').mkdir(parents=True)
for i in range(205):(source/f'[G] file{i:03}.pdf').write_text(str(i))
m,s=pty.openpty();fcntl.ioctl(s,termios.TIOCSWINSZ,struct.pack('HHHH',24,61,0,0))
p=subprocess.Popen(base+['tui','--data-dir',str(data)],stdin=s,stdout=s,stderr=s,env={**os.environ,'TERM':'xterm-256color','NO_COLOR':'1'});os.close(s);history=b''
def read(needle,timeout=15):
 global history
 output=b'';end=time.monotonic()+timeout
 while time.monotonic()<end:
  if select.select([m],[],[],.05)[0]:
   try:output+=os.read(m,65536)
   except OSError:break
  if needle.encode() in output:
   while select.select([m],[],[],.08)[0]:output+=os.read(m,65536)
   history+=output;return output.decode(errors='replace')
 history+=output;raise AssertionError((needle,output.decode(errors='replace')))
def send(keys,needle):os.write(m,keys.encode());return read(needle)
def edit(index,label,value):send(str(index),'항목 선택');send('e','입력 · '+label);send(value+'\r','항목 선택')
try:
 read('정리 방식 선택');send('2','기준 폴더 정리 설정')
 edit(1,'검사할 폴더 전체 경로',str(source));edit(2,'정리된 기준 폴더 전체 경로',str(sorted_root))
 send('s','하위 목적지 폴더를 가져올까요');send('y','목적지 검색어')
 send('r','기준 폴더 정리 결과');send('n','2/3페이지');frame=send('n','3/3페이지');assert 'file200.pdf' in frame
 detail=send('i','분석 상세');assert 'file200.pdf' in detail
 send('\x1b','기준 폴더 정리 결과');frame=send('\t','새 그룹');assert '205개' in frame
 send('d','목적지 지정');send('s','그룹의 미분류 205개');send('y','새 그룹')
 send('v','목적지 결정 205개를 검토');send('y','검사 및 실행')
 send('u','선택 0');send('n','페이지 2/3');send(' ','선택 1')
 send('\r','전체 페이지의 선택 1개');send('n','검사 및 실행');send('\x1b','정리 방식 선택')
 os.write(m,b'q');p.wait(timeout=10);assert p.returncode==0
 assert len(list(source.iterdir()))==205 and not (sorted_root/'G').exists()
 assert not (data/'settings.json').exists() and not list((data/'scan-cache').glob('scan-*'))
 print(json.dumps({'emptyRegisteredFolders':True,'sixtyColumns':True,'threePages':True,'assignEntire205FileGroup':True,'selectionAcrossPagesAndCancel':True,'noSettingOrFileChanges':True,'evidence':str(root)}))
finally:
 if p.poll() is None:p.terminate();p.wait(timeout=5)
 os.close(m);(root/'tui-output.txt').write_bytes(history)
