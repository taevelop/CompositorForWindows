# Smudge와 Liquify 전환

원본 기준은 `macOS/Compositor/Document/SmudgeLiquify.swift`다. Smear(R)의 모드는 Liquify, Blur, Smudge이며 이미지 픽셀만 변형한다. 원본 beginWarp는 마스크 선택을 거부한다.

## 문서 좌표 연산

`WarpRasterStroke`는 렌더링된 활성 레이어의 문서 크기 Raster를 받아 private RGBA8 premultiplied 버퍼에서 작업한다. 원본과 같이 diameter 최소 2, hardness 최대 .98, strength 최소 .01을 사용한다. 첫 입력은 시작점이며 Smudge만 색을 집는다. 이동 간격은 Smudge에서 max(1, diameter*.08), Liquify에서 max(1, diameter*.025)다. 간격에 못 미치는 입력은 마지막 시작점을 갱신하지 않는다.

Smudge는 정수로 반올림한 중심 주변의 색을 float 배열에 운반하고 smoothstep falloff로 칠한다. strength는 초기 dab의 직접 불투명도가 아니라 칠한 뒤 운반색을 유지하는 비율이다. Liquify는 dab 전의 주변 영역을 scratch에 복사하고 strength만큼 이동한 역방향 위치에서 bilinear sample한다. 색과 alpha를 같은 방식으로 처리한다. 원본과 같이 한 행/열밖에 없는 scratch에서는 2차원 보간을 건너뛴다.

dab 중심 목록을 유지하고 Append가 성공하면 immutable Raster 스냅샷을 게시한다. 원본과 이전 스냅샷은 변경하지 않는다. row/dab/변환 경계에 취소 확인을 추가했으며 실패·취소된 stroke는 재사용을 거부한다. 취소 도중 private pixels가 부분 수정돼도 마지막 게시 스냅샷은 유지한다.

코어 705개 통과: Smudge의 반투명 red/alpha128 운반·입력 간격·원본/이전 스냅샷 불변·premul, Liquify의 타일 경계 중심에서 독립 0.5px 선형 보간 기준120→110, 범위 밖 픽셀, 취소 후 게시 없음/재사용 거부, 한 행 및 설정·pointer 검사. 이는 몇 가지 독립 수치 기준이며 실제 Mac 전체 픽셀 왕복이나 일반 품질 통과가 아니다.

## 남은 연결

- 활성 레이어 raw document sample 준비와 작업 중 렌더링·좌표/메타데이터 유지.
- 원본 finishWarp처럼 diameter+4의 hard/opacity1 tip과 dab 경로로 layer grid에 최종 반영. 원본 `replacesWithClone=true`는 알파를 포함한 copy 계약이며 기존 Clone source-over와 구분해야 한다.
- selection/feather의 최종 clipping, 레이어 확장/crop, mask 배치 및 이미지/Undo 예산.
- Smear의 세 모드 selector, 탭 상태, 비동기 준비·입력 큐·취소/늦은 결과 보호, 단일 Undo/Redo 및 저장/내보내기 대기.
- 실제 WPF·프로젝트/출력 검증, 자연 이미지·회전/flip·대형/연속 입력 성능 및 배포 갱신.

현재 Append마다 문서 전체 스냅샷을 변환하므로 대형 문서 비용은 아직 높을 수 있다. 대형/장시간 성능을 측정하기 전 완료로 표시하지 않는다. 이번 단계는 연산 기반이며 현재 배포본의 Blur UI에는 Smudge/Liquify 모드가 없다. 실제 Mac 왕복과 별도 PC 실기는 사용자 지시로 보류다.

## 레이어 최종 반영

`WarpLayerStroke`가 시작 문서/레이어/설정을 고정하고 raw active layer를 문서 좌표로 캡처한다. Append는 warp 작업 사본만 변경하며, Complete는 diameter+4·hardness1·opacity1 tip으로 dab 경로를 재생해 레이어 픽셀에 반영한다. CloneStroke의 확장/선택/metadata/mask placement/crop·100MP/Undo 예산을 공유한다. 실패한 warp는 Complete도 거부하며, 성공 결과는 재호출해도 같은 참조다. 마스크/그룹/조정 이미지는 이 경로의 대상이 아니다.

공유 BrushStroke/CloneStroke에 선택적 sample 교체 모드를 추가했다. 기본 Clone/Blur source-over는 그대로다. 교체는 original*(1-amount)+sample*amount를 모든 RGBA에 적용하므로 sample alpha가 낮으면 alpha도 낮추고 투명 sample은 지울 수 있다. 정렬된 격자 확장의 작업 전달에서도 교체 플래그를 유지한다. 일반 브러시 최대2000px는 유지하며 교체 tip만2004px까지 허용해 입력 최대2000의 원본 diameter+4 범위를 처리한다. 원본 makeRasterEdit의2000px 검사와 finishWarp의+4 조합에서 생길 수 있는 상한 문제를 Windows에서 반복하지 않는다.

코어709개 통과: 독립 교체(100,0,0,128)+(0,20,0,32)×.5=(50,10,0,80), 일반 source-over=(94,10,0,136), 투명 지우기, 확장 후alpha96/red70, feather alpha 수식, 일반/교체 tip 상한. Smudge raw결과 alpha128/red128 반영·원본/metadata·단일UndoRedo·.comp 픽셀 왕복, 선택 제외·클릭 no-op·취소 후 반영 거부. 잘못 계산한 일반 source-over 테스트 alpha144를 독립 수식의136으로 수정했다. 기존Clone/Blur WPF는 별도 회귀한다.

이번에는 최종 반영 코어까지 연결했다. 작업 중 표시/세 모드 selector·탭 상태·비동기 준비/큐·저장대기·실제 warp WPF·회전/복합선택·대형/자연품질·새패키지배포는 계속 잔여다. 현재 publish-blur-ui 패키지에는 warp 모드가 없다.

최신 Release 빌드0경고/오류, 기존Clone 및 이미지·마스크Blur 단독 WPF 회귀가 통과했다. 이 검사는 기존 도구의 보존 증거이며 아직 연결되지 않은 warp UI의 검증이 아니다.

## Smear 모드와 비동기 UI

Smear(R) 도구에 Liquify/Blur/Smudge radio selector를 추가했다. 원본처럼 초기 모드는 Liquify이며 공유 Size/Hardness/Strength slider를 사용한다. Blur는 기존 이미지/마스크 경로를 사용하고 Smudge/Liquify는 마스크 입력을 명확히 거부한다. 탭 도구 상태에 선택 모드를 저장·복원한다. 제목/캔버스 위치와 시작 rail scroll 계약을 유지한다.

WarpOwner는 시작 session/document/layer/mode/settings/generation을 고정하고 worker에서 캡처·queued Append·최종 Complete를 순서대로 실행한다. 실행 중 UI는 immutable 작업 Raster를 문서 좌표 display-only 레이어로 보여준다. 기존 mask는 원래 placement에 고정해 표시 위치를 유지한다. 이 display document는 Session.Document/Undo/프로젝트에 들어가지 않는다. 최종 결과만 같은 소유자·모드·image target이면 Preview/Commit한다. 마지막 입력 뒤 기다리는 worker는 입력/end 신호나 취소로 깨어난다.

Esc·mode/tool/tab/session/Dispose 변경은 owner token과 transaction을 취소한다. 늦은 worker는 새 generation을 닫지 않는다. 공통 명령은 진행 중 stroke의 입력 큐를 End 처리하고 WarpCompletion을 기다리며, 대기중 UI를 잠근다. busy Esc는 warp와 명령을 함께 취소한다. 상태 overlay에 준비/현재 모드와 Esc 안내를 표시한다.

실제 --warp-smoke 통과: 두 모드의 Session불변 display preview/단일UndoRedo/.comp·PNG-composite 픽셀일치, TabTools 모드복원, 입력 중 명령대기·UI잠금/busyEsc·원본복원/늦은결과폐기, 새generation 보호. 이미지/마스크Blur 회귀와16tool 시작scroll/compactscroll/고정title/canvas도통과했고 도구 화면을 시각확인했다. Release0경고/오류, 제품코어변경없음·709코어검증유지.

아직 대형문서의 초기 캡처·반복전체Raster snapshot/preview tile 변환·메모리/최대지연·physical 입력, 복합선택·회전/flip·mask/effects 미리보기·최종결과 동등성, 실제 UI Save pending 파일쓰기/읽기, 모드 cycling shortcut·자연품질·새실행패키지배포는 후속이다. 현재 publish-blur-ui는 이전Blur단계이며 이번SmearUI는개발Release에만반영된다. 전체 전환 완료나 Mac parity 통과로 표시하지 않는다.

## 실제 저장 대기와 배포

--warp-smoke가 실제 Save(false)를 호출하는 지연 준비 fixture를 추가했다. 마우스 종료 전 Save가 입력 큐를 End하고 준비 완료를 기다린다. 대기 중 Save Task 미완료·busy/Editor 비활성·대상 .comp 디렉터리 없음, release 후 단일Undo·트랜잭션 종료·saved 상태·저장 픽셀 일치를 검증했다. 실제 ReopenSavedProject UI 경로의 픽셀 일치도 확인했다. 재열기 후 새 session/tool 기본값을 다시 fixture 설정으로 지정해 다음 검사와 분리한다.

취소 fixture도 실제 Save를 사용한다. busy Esc 후 원본 참조/트랜잭션 종료/UI 활성 복원과 대상 프로젝트 없음, 늦은 worker 폐기를 확인했다. 새generation 보호 검사도 유지한다. 테스트 파일은 검증한 artifact parent/warp-save-GUID 루트에서만 정리한다.

개발Release actual Save/reopen/Esc 회귀 통과·빌드0경고/오류. 제품연산변경없음·709코어검증유지. 세모드의 새 패키지는 `artifacts/publish-smear-ui/Compositor.Windows.exe`이며 기존패키지는유지한다. 배포본 결과는아래에기록한다. 복합변형/feather·자연품질·대형/연속입력/원본농도·모드cycling은계속잔여다. 실제Mac왕복/별도PC실기를대체하지않는다.

이패키지의actualwarp WPF Save/reopen/Esc회귀와portable45초gate1회가통과했다. 공백경로copy/제한PATH/패키지내CLR·Skia·C모듈로드관측·전체WPF회귀는같은개발PC의결과다. 일반30초gate의간헐안정화와별도cleanPC실기검증으로확대해해석하지않는다.

## 복합 선택과 수정 타일 스냅샷

코어713개 통과: 두 모드×linked/독립mask에서 rotation37/FlipXY·사각 선택에서 ellipse 빼기·feather2 최종 clipping을 전체 pixel 독립 blend 기준과1LSB이내 비교했다. 원본/Effects/opacity·rotation/flip·mask placement/pixel 공유를 유지한다. 비선택 warp 결과를 동일 source grid로 맞춰 최종 선택 합성을 비교한 내부 기준이며 실제 Mac 전체 연산 결과와의 비교는 아니다.

`--warp report [Smudge|Liquify] [side=4000] [count=12] [diameter=40]` 코어 측정을 추가했다. 합성 red/green 분할 premul alpha128 이미지에서 초기 raw 캡처/작업 준비, 입력, 끝 layer 반영, 최대 입력시간, managed allocation/peak, 원본불변 및 결과SHA를 기록한다. setup은할당에서제외하고peak에는포함한다. hash용추가RGBA와검증은시간/할당·peak확정뒤실행한다.

매 Append의 전체Raster.FromRgba를 수정 타일 게시로 대체했다. Smudge/Liquify의RGBA 연산 중 실제 byte 변경 타일만 추적하고, Append 성공 때 그 타일의 논리 rows를 immutable buffer로 옮긴다. 변경 없는 타일/이전 Raster 스냅샷은 공유한다. 취소·실패는 private 작업을 폐기하며 이전 게시 결과는 유지한다. float 운반색, scratch 및 dab 연산은 그대로다.

| 4K/40px/12입력, 각1회 | Smudge 이전→현재 | Liquify 이전→현재 |
|---|---:|---:|
| 입력 합계 | 342.4→10.2ms | 310.0→12.6ms |
| 단일 입력 최대 | 46.4→5.13ms | 36.4→5.92ms |
| 준비 | 195.3→205.8ms | 192.2→186.2ms |
| 끝 반영 | 67.6→68.2ms | 134.2→129.9ms |
| managed 누적 할당 | 1608.6→206.8MiB | 1622.8→221.4MiB |
| process peak | 1365.5→443.5MiB | 1380.8→444.8MiB |

최종SHA는각모드전후동일: Smudge169C2A1A3A445E7BE46A4D808A6850EAD764485A6BD578AED8D36D627C03798C, Liquify6EEFABAC4188A2A1AA735817C8C31ABD902F5C5EC2030BBB72A52BF7663CEBFC. core713검사에변하지않은edge tile 공유도포함한다. 이표는단일합성core이며WPF응답/물리입력/800px·100MP·자연품질완료가아니다. 초기fullcanvas캡처와큰brush/장시간·preview rendering은후속이다. publish-smear-ui는이전b8fe821단계이며이번최적화의배포갱신은후속이다.

최적화 후 Release0경고/오류·실제warp WPF 미리보기/UndoRedo/실제Save대기·재열기/PNG/탭/Esc/새generation 회귀도통과했다. 성능 표는여전히코어측정이며이기능회귀를UI응답시간측정으로대체하지않는다.
