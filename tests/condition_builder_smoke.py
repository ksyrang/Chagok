# -*- coding: utf-8 -*-
"""실제 POSIX PTY로 조건 선택·분석·규칙 메타데이터를 검증한다. 파일 이동은 하지 않는다."""
from pathlib import Path
import fcntl,json,os,pty,select,struct,subprocess,sys,tempfile,termios,time

if len(sys.argv) != 3:
    raise SystemExit('python3 tests/condition_builder_smoke.py CLI_DLL DOTNET')
base = [sys.argv[2], str(Path(sys.argv[1]).resolve())]
root = Path(tempfile.mkdtemp(prefix='chagok-conditions-')).resolve()
data,source,target = root/'data',root/'source',root/'target'
source.mkdir()
names = ['[xyz] one.pdf','[xyz] two.pdf','[x] one.pdf','[x] two.pdf',
         '[y] one.pdf','[y] two.pdf','[yellow] one.pdf','[z] one.pdf','plain.txt']
for name in names:
    (source/name).write_text(name)
def call(args):
    result = subprocess.run(base+args+['--data-dir',str(data),'--json'],capture_output=True,text=True,timeout=20)
    assert result.returncode == 0, (result.returncode,result.stdout,result.stderr)
    return json.loads(result.stdout)
folder = call(['folders','add','--name','CONDITIONS','--path',str(source),'--recent-destinations','on'])
call(['rules','add','--folder',folder['Id'],'--name','seed','--destination',str(target),'--match','contains','--pattern','never-match'])
before = (data/'settings.json').read_bytes()
master,slave = pty.openpty()
fcntl.ioctl(slave,termios.TIOCSWINSZ,struct.pack('HHHH',30,120,0,0))
process = subprocess.Popen(base+['tui','--data-dir',str(data)],stdin=slave,stdout=slave,stderr=slave,env={**os.environ,'TERM':'xterm-256color'})
os.close(slave)
history = bytearray()
def read(needle,timeout=15):
    output = bytearray(); end = time.monotonic()+timeout
    while time.monotonic() < end:
        if select.select([master],[],[],.05)[0]:
            output.extend(os.read(master,65536))
        if needle.encode() in output:
            while select.select([master],[],[],.08)[0]:
                output.extend(os.read(master,65536))
            history.extend(output)
            return output.decode(errors='replace')
    history.extend(output)
    raise AssertionError((needle,output.decode(errors='replace')))
def send(keys,needle='항목 선택'):
    os.write(master,keys.encode()); return read(needle)
def select_row(index):
    # Home/아래 방향키를 실제 입력하여 10번 이후 항목도 직접 선택한다.
    send('\x1b[H')
    for _ in range(index):
        send('\x1b[B')
def select_field(index):
    # 기존 조건 행 앞에 내용 제한/괄호 포함 선택이 추가됐다.
    select_row(index if index == 0 else index + 2)
def apply():
    send('s','이 분류 조건을 적용할까요')
    return send('y','C 이 그룹으로 규칙 만들기')
def edit_value(index,value):
    select_field(index); send('e','입력 · 조건')
    send(value+'\r')
try:
    read('검사 폴더'); send('p','C 이 그룹으로 규칙 만들기')
    frame = send('g','분류 조건 선택')
    assert '제한 없음' in frame and '조건 1 종류' not in frame,frame
    select_row(2); send('e')  # 괄호 없는 그룹으로 제한 없이 분류
    frame = apply(); assert '패턴 일치 8' in frame and '그룹 5' in frame,frame
    send('c'); send('e','입력 · 규칙 이름'); send('unrestricted-native\r')
    send('s','위 검사 폴더에 규칙을 저장할까요'); send('y','규칙 저장 완료')
    native = json.loads((data/'settings.json').read_text())['Folders'][0]['Rules'][-1]
    assert native['GroupExpression'] == '' and native['GroupConditions']['LimitContent'] is False and not native['GroupConditions']['IncludeBrackets'],native
    assert native['Pattern'] in ('x','xyz','y'),native
    native_before = (data/'settings.json').read_bytes()
    send('p','C 이 그룹으로 규칙 만들기');send('g','분류 조건 선택')
    select_row(1);send('e')  # 내용 제한: 조건 지정
    select_row(2);send('e')  # 기본 괄호 포함을 끄고 기존 x/y 조건 검증
    select_row(0)
    before = native_before
    # 기본 x 시작 OR y 정확히, 종류/괄호/결합을 직접 선택할 수 있다.
    send('i','전체 값 · 대상 괄호'); send('\x1b')
    send('\x1b[C'); send('\x1b[C'); send('\x1b[C')
    select_field(9); send('e','입력 · 시험할 파일명'); send('[xyz][x][y][yellow]\r')
    select_field(10)
    preview = send('i','전체 값 · 미리보기 결과')
    assert '[xyz]' in preview and '[x]' in preview and '[y]' in preview and '[yellow]' not in preview,preview
    send('\x1b'); send('\x1b[F')
    expression = send('i','전체 값 · 생성된 정규식')
    assert '(?<group>' in expression and '글자 그대로' in expression,expression
    send('\x1b')
    frame = apply()
    assert '패턴 일치 6' in frame and '그룹 3' in frame,frame
    assert (data/'settings.json').read_bytes() == before
    # 작은 창과 취소: 값 변경 후 폼을 버리면 현재 결과를 유지한다.
    fcntl.ioctl(master,termios.TIOCSWINSZ,struct.pack('HHHH',24,60,0,0))
    send('g','분류 조건 선택'); edit_value(3,'q')
    select_field(10); send('i','전체 값 · 미리보기 결과'); send('\x1b')
    frame = send('\x1b','C 이 그룹으로 규칙 만들기')
    # 60열에서는 집계줄이 잘릴 수 있으므로 크기 복원 뒤 다시 그려 확인한다.
    fcntl.ioctl(master,termios.TIOCSWINSZ,struct.pack('HHHH',30,120,0,0))
    frame = send('r','C 이 그룹으로 규칙 만들기')
    assert '패턴 일치 6' in frame,frame
    # 내용 제한을 껐다 켜도 조건 값과 괄호 표시 설정은 유지한다.
    send('g','분류 조건 선택'); edit_value(3,'z')
    select_row(1);send('e'); frame = apply(); assert '패턴 일치 8' in frame,frame
    send('g','분류 조건 선택');select_row(1);send('e');frame = apply()
    assert '패턴 일치 3' in frame and '그룹 2' in frame,frame
    send('g','분류 조건 선택');edit_value(3,'x');apply()
    # 괄호 포함 그룹과 적용 취소도 분석 결과를 보존한다.
    send('g','분류 조건 선택');select_row(2);send('e');frame=apply()
    assert '괄호 포함' in frame and '[xyz]' in frame,frame
    send('g','분류 조건 선택');select_row(2);send('e');apply()
    # 값 수정 적용과 복원.
    send('g','분류 조건 선택'); edit_value(3,'z')
    frame = apply(); assert '패턴 일치 3' in frame and '그룹 2' in frame,frame
    send('g','분류 조건 선택'); edit_value(3,'x')
    # 조건 추가는 빈 값 검증을 통과할 수 없다. 이후 새 조건을 삭제한다.
    select_field(8); send('e'); send('s','이 분류 조건을 적용할까요'); send('y','처리 실패')
    select_field(9); send('e','입력 · 조건 3 값'); send('z\r')
    select_field(8); send('\x1b[D')  # Contains -> EndsWith
    select_field(10); send('e')       # 새 조건 삭제, 선택/스크롤 범위 재계산
    # AND는 같은 괄호 안에서 x 시작과 y 정확히를 동시에 요구하므로 0개.
    select_field(1); send('e'); frame = apply(); assert '패턴 일치 0' in frame,frame
    send('g','분류 조건 선택'); select_field(1); send('e'); frame = apply()
    assert '패턴 일치 6' in frame,frame
    assert (data/'settings.json').read_bytes() == before
    # 분석의 정확한 그룹과 조건 메타데이터를 함께 저장한다.
    send('c'); send('s','위 검사 폴더에 규칙을 저장할까요'); send('y','규칙 저장 완료')
    settings = json.loads((data/'settings.json').read_text())
    rule = settings['Folders'][0]['Rules'][-1]
    assert rule['Match'] == 'Group' and rule['Pattern'] in ('x','xyz','y'),rule
    conditions = rule['GroupConditions']
    assert conditions['Brackets'] == 'Square' and not conditions['All'] and conditions['LimitContent'] and not conditions['IncludeBrackets'],conditions
    assert [(x['Kind'],x['Value']) for x in conditions['Terms']] == [('StartsWith','x'),('Equal','y')],conditions
    assert rule['GroupExpression'] and rule['Destination'] == str(target),rule
    # 규칙 마지막 항목으로 저장된 조건을 다시 열고 취소해도 설정은 유지한다.
    saved = (data/'settings.json').read_bytes()
    send('e','규칙 편집'); send('\x1b[F'); send('e','분류 조건 선택')
    edit_value(3,'discard'); send('\x1b'); send('\x1b','입력 취소')
    assert (data/'settings.json').read_bytes() == saved
    # 직접 E 적용 뒤 다시 G를 열면 기본 조건이다. G 취소·B 복원도 안전하다.
    send('p','C 이 그룹으로 규칙 만들기'); send('g','분류 조건 선택'); select_row(1); send('e'); apply()
    direct = r'\[(?<group>[^\]]+)\]'
    send('e','분류 정규식'); send('e','입력 · 분류 정규식'); send(direct+'\r')
    send('s','다시 분석할까요'); frame = send('y','C 이 그룹으로 규칙 만들기')
    assert '직접 입력:' in frame and '패턴 일치 8' in frame,frame
    send('c'); send('e','입력 · 규칙 이름'); send('direct-mode\r')
    send('s','위 검사 폴더에 규칙을 저장할까요'); send('y','규칙 저장 완료')
    direct_rule = json.loads((data/'settings.json').read_text())['Folders'][0]['Rules'][-1]
    assert direct_rule.get('GroupConditions') is None and direct_rule['GroupExpression'] == direct,direct_rule
    send('p','C 이 그룹으로 규칙 만들기')
    send('g','분류 조건 선택'); select_row(0); send('e'); frame=apply(); assert '( )' in frame and '제한 없음' in frame,frame
    send('\x1b','검사 폴더'); os.write(master,b'q'); process.wait(timeout=10)
    assert process.returncode == 0
finally:
    if process.poll() is None:
        process.terminate(); process.wait(timeout=5)
    os.close(master)
    (root/'tui-output.txt').write_bytes(history)
# CLI의 직접 정규식 수정도 조건 메타데이터를 제거한다.
direct = r'\[(?<group>[^\]]+)\]'
call(['rules','update','--folder',folder['Id'],'--rule',rule['Id'],'--group-expression',direct])
updated = next(r for r in json.loads((data/'settings.json').read_text())['Folders'][0]['Rules'] if r['Id'] == rule['Id'])
assert updated.get('GroupConditions') is None and updated['GroupExpression'] == direct,updated
native_scan = call(['scan','--folder',folder['Id'],'--rule',native['Id']])
assert native_scan['total'] == 2, native_scan
assert sorted(p.name for p in source.iterdir()) == sorted(names) and not target.exists()
assert not list((data/'analysis-cache').glob('scan-*'))
print(json.dumps({'conditionTui':True,'ruleMetadata':True,'directRegex':True,'noFileMove':True,'evidence':str(root)}))
