# -*- coding: utf-8 -*-
"""macOS/POSIX PTY: TUI 규칙 추가·즉시 표시·개별 실행 회귀 검사."""
from pathlib import Path
import subprocess,tempfile,json,os,pty,fcntl,termios,struct,time,select,sys,shutil,re
if len(sys.argv)<2: raise SystemExit('사용법: python3 tests/tui_rule_smoke.py CLI_DLL [DOTNET]')
sdk=sys.argv[2] if len(sys.argv)>2 else shutil.which('dotnet')
if not sdk: raise SystemExit('dotnet 경로를 지정하세요.')
dll=str(Path(sys.argv[1]).resolve());root=Path(tempfile.mkdtemp(prefix='chagok-tui-rule-')).resolve()
data=root/'data';src=root/'source';dst=root/'target';src.mkdir();dst.mkdir()
base=[sdk,dll];common=['--data-dir',str(data),'--json']
def call(args):
 p=subprocess.run(base+args+common,capture_output=True,text=True,timeout=15)
 assert p.returncode==0,p.stderr
 return json.loads(p.stdout)
other=root/'other';other.mkdir()
call(['folders','add','--name','OTHER','--path',str(other)])
f=call(['folders','add','--name','SOURCE','--path',str(src)])
# 작은 화면에서 새 규칙이 기존 표시 범위를 벗어나는 개수.
for i in range(12):call(['rules','add','--folder',f['Id'],'--name',f'old-{i:02}','--destination',str(dst),'--match','any','--extensions','.pdf'])
m,s=pty.openpty();fcntl.ioctl(s,termios.TIOCSWINSZ,struct.pack('HHHH',24,110,0,0))
env={**os.environ,'TERM':'xterm-256color'};env.pop('NO_COLOR',None)
p=subprocess.Popen(base+['tui','--data-dir',str(data)],stdin=s,stdout=s,stderr=s,env=env)
os.close(s);history=b''
def read_until(needle,timeout=10):
 global history
 buf=b'';end=time.monotonic()+timeout
 while time.monotonic()<end:
  if select.select([m],[],[],.05)[0]:buf+=os.read(m,65536)
  if needle.encode() in buf:
   # 마지막 프레임까지 수신해 조작 안내도 검사한다.
   while select.select([m],[],[],.1)[0]:buf+=os.read(m,65536)
   history+=buf;return buf.decode(errors='replace')
 history+=buf;raise AssertionError((needle,buf.decode(errors='replace')))
def send(value,needle):os.write(m,value.encode());return read_until(needle)
def compact(frame):
 return re.sub(r'\s|[│]', '', re.sub(r'\x1b\[[0-9;?]*[A-Za-z]', '',frame))

def focus(index):
 if index<=9:return send(str(index),'항목 선택')
 send('\x1b[H','항목 선택')
 for _ in range(index-1):frame=send('\x1b[B','항목 선택')
 return frame
def edit(index,label,value):
 focus(index);send('e','입력 · '+label)
 return send(value+'\r','항목 선택')
def save_rule():
 send('s','위 검사 폴더에 규칙을 저장할까요')
 send('\x1b[F','규칙 ID')
 return send('y','규칙 저장 완료')
try:
 first=read_until('검사 폴더')
 assert '⡜⠛⡄⢸⠀⢸' in first and '████' not in first
 send('\x1b[B','선택 규칙  old-00')
 before=(data/'settings.json').read_bytes()
 detail=send('i','폴더 상세');assert str(src) in detail
 send('\x1b','선택 규칙  old-00')
 # 설정의 직접 선택/취소와 도움말 상태 보존.
 send('s','공유 설정 · 항목 선택');send('4','선택 항목 설명')
 send('\x1b[C','저장하지 않은 변경');send('?','기능 도움말')
 send('\x1b','공유 설정 · 항목 선택');send('\x1b','설정 변경 취소')
 assert (data/'settings.json').read_bytes()==before
 # 입력칸 Esc는 항목만 취소. 목록 Esc가 전체 폼 취소다.
 context=send('n','항목 선택')
 assert '선택한 검사 폴더: SOURCE' in context and str(src) in compact(context)
 send('e','입력 · 규칙 이름');send('cancelled\x1b','항목 편집 취소')
 assert (data/'settings.json').read_bytes()==before
 send('e','입력 · 규칙 이름');send('/cancel\r','항목 편집 취소')
 send('\x1b','입력 취소');assert (data/'settings.json').read_bytes()==before
 for key in ['f','e','c']:
  send(key,'항목 선택');send('e','입력 · 표시 이름')
  send('cancelled\x1b','항목 편집 취소');send('\x1b','입력 취소')
  assert (data/'settings.json').read_bytes()==before
 # 목적지 누락 저장 실패 후 같은 폼에서 수정한다.
 send('n','항목 선택');send('s','위 검사 폴더에 규칙을 저장할까요')
 send('y','처리 실패');assert (data/'settings.json').read_bytes()==before
 focus(5);send('e','파일명 제한 없음')
 focus(4);send('\x1b[C','단어 포함')
 edit(5,'단어 또는 패턴','temporary')
 focus(4);send('\x1b[D','제한 없음')
 # 경로/확장자부터 수정해 앞 항목을 거치지 않고 저장할 수 있어야 한다.
 edit(7,'이동할 폴더 전체 경로',str(dst));edit(6,'확장자 (.pdf,.jpg)','.txt')
 edit(1,'규칙 이름','가나\x7f\x7fnew-rulX\x7fe\x1b[H\x1b[C\x1b[3~e\x1b[F')
 send('?','기능 도움말');send('\x1b','항목 선택')
 send('s','위 검사 폴더에 규칙을 저장할까요');send('\x1b','저장 확인 취소')
 assert (data/'settings.json').read_bytes()==before
 # 이름을 빈 Enter로 유지하고 최종 저장.
 edit(1,'규칙 이름','')
 frame=save_rule();assert '선택 규칙  new-rule' in frame and '> [켜짐] new-rule' in frame
 saved=json.loads((data/'settings.json').read_text());folder=next(x for x in saved['Folders'] if x['Id']==f['Id'])
 new_rule=folder['Rules'][-1]
 assert len(folder['Rules'])==13 and new_rule['Name']=='new-rule' and new_rule['Extensions']=='.txt'
 assert new_rule['Destination']==str(dst) and new_rule['Match']=='Any' and new_rule['Pattern']=='temporary'
 assert not new_rule['Recursive'] and new_rule['Enabled']
 # 개별 실행/폴더 전체 범위/취소 회귀.
 (src/'only.txt').write_text('selected');(src/'keep.pdf').write_text('other rule')
 frame=send('r','검사 및 실행');assert '선택 규칙 · new-rule' in frame and '대상 1 ' in frame
 send('?','기능 도움말');send('\x1b','검사 및 실행')
 send('\r','실행할까요');send('y','아무 키나 눌러 결과 확인')
 assert (dst/'only.txt').read_text()=='selected' and not (src/'only.txt').exists()
 assert (src/'keep.pdf').exists() and not (dst/'keep.pdf').exists()
 send(' ','파일별 처리 결과');send('\r','선택 규칙  new-rule')
 send('\t','▶ 검사 폴더');frame=send('r','검사 및 실행');assert '대상 0 ' in frame
 send('\x1b','선택 규칙  new-rule');frame=send('a','검사 및 실행');assert '대상 1 ' in frame
 send('\x1b','선택 규칙  new-rule')
 # 편집은 한 필드만 변경하고 ID와 나머지 조건을 보존한다.
 send('e','항목 선택');edit(6,'확장자 (.pdf,.jpg)','.log');save_rule()
 actual=next(x for x in json.loads((data/'settings.json').read_text())['Folders'] if x['Id']==f['Id'])['Rules'][-1]
 assert actual=={**new_rule,'Extensions':'.log'}
 before=(data/'settings.json').read_bytes()
 send('i','규칙 상세');send('\x1b[6~','이동 동작');send('\x1b','선택 규칙  new-rule')
 assert (data/'settings.json').read_bytes()==before
 # 폴더 추가: 제외 경로를 먼저 입력해도 검사 경로/조건을 독립적으로 지정 가능.
 new_source=root/'new-source';(new_source/'private').mkdir(parents=True)
 cloned_source=root/'cloned-source';(cloned_source/'private').mkdir(parents=True)
 program=root/'example.exe';program.write_bytes(b'test-program')
 send('f','항목 선택')
 edit(3,'제외할 하위 폴더',str(new_source/'private'))
 edit(2,'검사할 폴더 전체 경로',str(new_source));edit(1,'표시 이름','NEW-FOLDER')
 focus(5);send('e','실행 조건이 수동')
 focus(4);send('\x1b[C','프로그램 시작');edit(5,'감지할 .exe 전체 경로',str(program))
 send('s','폴더 설정을 저장할까요');send('y','폴더 저장 완료')
 new_folder=next(x for x in json.loads((data/'settings.json').read_text())['Folders'] if x['Name']=='NEW-FOLDER')
 assert new_folder['Trigger']=='ProgramStarted' and new_folder['ProgramPath']==str(program)
 assert new_folder['ExcludedFolders']==[str(new_source/'private')]
 # 복제 시 경로 수정 즉시 제외 폴더가 새 기준으로 재배치된다.
 send('c','항목 선택');edit(2,'검사할 폴더 전체 경로',str(cloned_source))
 edit(1,'표시 이름','CLONED-FOLDER')
 focus(3);full=send('i','전체 값 · 제외할 하위 폴더')
 assert str(cloned_source/'private') in compact(full)
 send('\x1b','항목 선택');send('s','폴더 설정과 규칙을 복제할까요');send('y','폴더 저장 완료')
 cloned=next(x for x in json.loads((data/'settings.json').read_text())['Folders'] if x['Name']=='CLONED-FOLDER')
 assert cloned['Id']!=new_folder['Id'] and cloned['ExcludedFolders']==[str(cloned_source/'private')]
 assert next(x for x in json.loads((data/'settings.json').read_text())['Folders'] if x['Id']==new_folder['Id'])==new_folder
 send('e','항목 선택');edit(1,'표시 이름','RENAMED-FOLDER')
 send('s','폴더 설정을 저장할까요');send('y','폴더 저장 완료')
 assert next(x for x in json.loads((data/'settings.json').read_text())['Folders'] if x['Id']==cloned['Id'])=={**cloned,'Name':'RENAMED-FOLDER'}
 # 최근 위치와 규칙명 하위 폴더: 폴더 설정 → 저장 → 다른 규칙에서 최근 기본 위치 선택.
 send('e','항목 선택');focus(7);send(' ','켜짐')
 send('s','폴더 설정을 저장할까요');send('y','폴더 저장 완료')
 nested_dst=root/'nested-target'
 send('n','항목 선택');edit(1,'규칙 이름','NESTED');edit(7,'이동할 폴더 전체 경로',str(nested_dst))
 focus(13);send(' ','켜짐')
 focus(8);forced=send('e','규칙명 폴더 사용이 켜져')
 assert '켜짐 · 자동 적용' in forced
 full=send('i','전체 값 · 대상 폴더 자동 생성');assert '자동 생성이 적용' in full
 send('\x1b','항목 선택');save_rule()
 state=json.loads((data/'settings.json').read_text());nf=next(x for x in state['Folders'] if x['Id']==cloned['Id'])
 assert nf['RecentDestinations']==[str(nested_dst)] and nf['Rules'][-1]['AppendRuleName']
 assert not nested_dst.exists()
 send('n','항목 선택');edit(1,'규칙 이름','RECENT')
 focus(7);filled=send('i','전체 값 · 이동할 폴더 전체 경로')
 assert str(nested_dst) in compact(filled) and '기능과 연계 항목' in filled
 send('\x1b','항목 선택');edit(7,'이동할 폴더 전체 경로',str(root/'temporary-base'))
 focus(14);send('\x1b[C','수정 중')
 chosen=send('i','전체 값 · 최근 사용한 이동 위치');assert str(nested_dst) in compact(chosen)
 send('\x1b','항목 선택')
 focus(13);send(' ','켜짐');save_rule()
 state=json.loads((data/'settings.json').read_text());nf=next(x for x in state['Folders'] if x['Id']==cloned['Id'])
 assert nf['Rules'][-1]['Destination']==str(nested_dst) and nf['RecentDestinations']==[str(nested_dst)]
 (cloned_source/'nested.txt').write_text('nested-content')
 send('r','검사 및 실행');send('\r','실행할까요');send('y','아무 키나 눌러 결과 확인')
 assert (nested_dst/'RECENT'/'nested.txt').read_text()=='nested-content'
 send(' ','파일별 처리 결과');send('\r','선택 규칙  RECENT')
 before_cancel=(data/'settings.json').read_bytes();send('n','항목 선택')
 focus(14);send('\x1b[C','수정 중');send('\x1b','입력 취소')
 assert (data/'settings.json').read_bytes()==before_cancel
 # 최소 창: 목록 스크롤/끝 항목/긴 입력의 항목 취소/전체 취소.
 fcntl.ioctl(m,termios.TIOCSWINSZ,struct.pack('HHHH',24,61,0,0))
 before_small=(data/'settings.json').read_bytes();send('n','항목 선택')
 last=send('\x1b[F','최종 이동 폴더');assert 'S 검토·저장' in last and '사용 안 함' in last
 focus(7);send('e','입력 · 이동할 폴더 전체 경로')
 send('x'*180+'\x1b','항목 편집 취소');send('\x1b','입력 취소')
 assert (data/'settings.json').read_bytes()==before_small
 # 설정 선택형 화면과 저장 실패 재시도도 유지한다.
 settings_before=json.loads((data/'settings.json').read_text())
 send('s','공유 설정 · 항목 선택');send('4','선택 항목 설명');send('\x1b[C','저장하지 않은 변경')
 blocked=data/'settings.json.tmp';blocked.mkdir();send('s','저장 실패')
 assert json.loads((data/'settings.json').read_text())==settings_before
 blocked.rmdir();send('s','설정 저장 완료')
 expected={**settings_before,'AutoRegisterExplorerMenu':not settings_before['AutoRegisterExplorerMenu']}
 assert json.loads((data/'settings.json').read_text())==expected
 os.write(m,b'q');p.wait(timeout=10);assert p.returncode==0
finally:
 if p.poll() is None:p.terminate();p.wait(timeout=5)
 os.close(m);(root/'tui-output.txt').write_bytes(history)
print(json.dumps({'recentDestinationSelection':True,'ruleNameSubfolderMove':True,'directRuleCreateAndEdit':True,'directFolderCreateCloneAndEdit':True,'fieldCancelVsFormCancel':True,'validationRetryKeepsDraft':True,'reviewCancelKeepsDraft':True,'disabledFieldsPreserveValues':True,'cloneRebasesExclusions':True,'smallFormScrolling':True,'singleRuleMoveAndFolderScope':True,'settingsSaveRetry':True,'evidence':str(root)},ensure_ascii=False))
