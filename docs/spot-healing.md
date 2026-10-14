# Spot Healing 전환

## 원본 규칙과 연산 기반

BrushStroke.swift의 SpotHealingMode 순서는 Content-Aware(0), Create Texture(1), Proximity Match(2)다. source 원본 pixels를 읽고 stroke 끝에서 heal하며, 입력 중 dark wash는 결과 계산에 사용하지 않는다. kernel 결과는 coverage×opacity로 원본과 섞고 마지막에 selection clip을 적용한다. 검색 영역은 painted bounds를 (max(width,height)+32)×3.2만큼 확장하고 이미지 경계로 clip한다.

HealPixels.c SHA256은 macOS/Windows 모두 098D25186D80A0C923019393AC4FC398EEDDDF5EEF7103A890636C81B56318AE다. 고정폭 int32 dimensions/mode, uint32 seed, float opacity bridge를 추가했다. C 내부 long는 MSVC32bit이며 허용100MP count/stride에 들어간다. 원본 커널은 변경하지 않았다.

SpotHealing.Apply는 premultiplied RGBA Raster와 동일격자의 gray coverage를 받는다. 원본과 동일한 ROI를 찾아 tile에서 ROI buffer로 복사하고 native kernel을 호출한 뒤 변경tile만 교체한다. 비변경tile과 원본snapshot을 유지한다. native할당실패는 오류로 반환하며 caller의 pixels/coverage는 변경하지 않는다. 취소는 스캔/복사/결과tile 및 native 호출 전후에서 확인한다. native 내부 중간 취소는 아직없다.

## 검증

코어672개 및 Release0경고오류·기존Clone 단독WPF 회귀통과. 513×17의 x255 tile경계 defect를 세모드로 검사하고 coverage 밖 모든채널/원본/비변경tile공유·seed재현성을 확인했다. Content-Aware/Proximity는 일정 주변tone을 복원한다. Create Texture는 검정hole에 인접한 ring pixel의 neighbour detail을 포함해 grain을 만들므로 상수주변이어도 결과가 흔들린다. 고정seed/hash/Box-Muller 및 24pixel ring의 독립수식으로 이 값을 1LSB이내 대조했다. 반투명 RGBA의 coverage128×opacity0.5 적용·zero/no-op·잘못된mode·사전취소를 검사했다.

## 남은 연결

- 문서좌표 stroke coverage·회전/flip 및 원본raster 확장·selection 최종blend·image-only guard.
- 입력 중 dark wash·J 도구/세모드·Size/Hardness/Opacity slider·단일Undo/Redo.
- 취소 가능한 끝연산 worker·document/transaction ownership·Esc/캡처상실/탭전환.
- 저장/출력·패널/물리입력·자연texture 품질·대형ROI 성능/메모리·portable.

현재는 연산 기반이며 사용자 Spot Healing 도구가 완성된 상태가 아니다. 실제 Mac 왕복 및 별도Windows PC 검증은 보류한다. 원본soft tip 밀도와 Windows max swept coverage 동등성도 품질잔여다.

## 문서 명령 연결

SpotHealing.Compute는image layer만허용하고원본pixels에서커널결과를만든뒤 document selection을layer.Transform좌표로최종blend한다. kernel coverage를selection으로미리축소하지않아원본patch검색/edge tone규칙을유지한다. mask/effects/opacity/blend/transform은with Pixels로보존한다. same-result는원본문서참조를반환하고doc검증·256MiB Undo예산·취소검사후결과를반환한다.

코어673개/전체Release0경고오류통과. 지정pixel선택clip/선택밖no-op·원본pixels/metadata공유·단일UndoRedo/문서참조복원·.comp재열기pixel일치를검사했다. 아직stroke coverage수집/변형복합·raster확장·J UI/wash·끝연산worker/Esc·WPF/출력·대형품질은후속이다. source 및 coverage는현재layer raster격자이며사용자도구완료로판정하지않는다.

## J 도구 UI 1차 연결

EditorTool.SpotHealing index14/J/rail·Content-Aware/Create Texture/Proximity Match radio와Size/Hardness/Opacity slider를연결했다. TabTools에mode를저장한다. image-only/visible/group/adjustment guard를따르고color/mask입력은숨긴다. brush coverage는원본과분리한white/opacity1 raster에모으고현재source에는31gray/45% darkwash를selectionclip과함께preview한다. 원본Document와mode/opacity/seed는stroke시작에고정된다.

mouseup후graycoverage추출/원본기반Compute를serialworker에서실행한다. document/session/transaction세대·tool/image target/layer일치일때만설치하고단일Commit한다. Esc/캡처상실/전환/Dispose는owntransaction을복원하고늦은결과를폐기한다. native 내부중간취소는없고전후/managed단계취소만있다. 진행표시를추가했다.

WPF단독3모드/washdraft·원본에서보정(wash를결과로쓰지않음)·UndoRedo/mask·gesturecancel·Esc/늦은worker·새transaction보호·mode snapshot복원 통과, 화면검토통과. 15도구가기본높이에맞도록rail높이32/margin3,1/padding4로조정했으며도구UI 최초scroll없음/최소크기palette접근/높이복원검사통과. 코어673/Release0경고오류. 전체WPF 첫실행SelectionAutoScrollSmoke의outline size실패, 같은조건재검사는마지막stage27.03초/exit0. 원인미확정품질항목으로유지하고반복안정성완료로표시하지않는다.

현재stroke는layer의기존raster크기내수집이다. 경계밖확장·회전/flip/feather복합·저장/출력전용WPF·pendingSave/탭전환·대형ROI/자연texture/softcoverage원본동등성·실기·배포갱신이후속이다. 사용자도구전체완료로판정하지않는다.

## 보정 중 저장/명령 대기

공통ResolveGradientBeforeAction에HealingCompletion대기를추가했다. 저장/내보내기등이pendingwash를직접쓰지않고보정성공·동일session/documentID·transaction종료를확인한후진행한다. 대기중Editor입력을잠그고finally에복원한다. busy상태에서도Healing준비중Esc를허용해작업과후속저장을취소한다.

지연주입WPF에서Save요청시파일없음/Task미완료/UI잠금→release후단일Undo·saved상태·.comp최종픽셀을검사했다. 실제UI닫기/재열기와PNG전체픽셀이canvas와같음을확인했다. 대기중Esc는원본/활성UI를복원하고취소destination에프로젝트를만들지않았다. fixture는별도문서tab에격리하고원래tab/path를유지한다. 최초fixturetimeout은재열기후기본Brush로초기화된상태에서Healingworker진입을기다린테스트오류였고J/brush설정을다시지정해수정했다.

최신Release0경고오류·Healing단독WPF통과. 이번단계core연산은변경없으며이전673개검증을유지한다. 기존raster범위/경계확장·복합선택/회전·대형/자연이미지품질·실기/배포갱신은계속후속이다. fullWPF의autoscroll간헐실패/30초timeout품질항목을이검사로대체하지않는다.

## 확장 격자·복합 선택 연결

HealingStroke가시작document/source/settings/mode/seed를고정하고coverage·wash·원본base를동일격자에유지한다. canvasclip된swept영역을source.Transform의pixel좌표로변환해필요frame을확장하고RasterReframe으로원본pixel위치/회전flip·linkedmask white padding·independent mask pin을유지한다. frame변경시coverage/wash는시작base에서points를재생한다. 끝worker는wash가아닌확장된base에서커널/최종selection을계산하고원본rect+새alpha영역으로crop한다. 내부원본tile은final alpha스캔에서생략한다. opacity/선택밖no-op은시작document참조를반환하며image/mask·256MiB Undo예산을검사한다.

코어677개통과: source16→17/new alpha·linked/independent mask·원본/UndoRedo·selection제외/no-op·취소, rotation37/FlipXY/feather1의rawhealed+최종selection참조와전pixel1LSB이내합성검사. 실제J WPF에서도16→17px draft/complete mask·Undo/Redo·UI저장canvas출력일치검사통과. 최신Release0경고오류·기존3모드/Esc/lateworker/pendingSave/재열기PNG회귀통과. 입력중selectioncoverage를중복생성하지않게정리했다.

비정렬 frame변경은points재생이므로긴확장stroke/큰tip의preview비용이남는다. nativekernel 중간취소는없다. dense/대형ROI·자연texture/색공간/softcoverage원본동등성·budget극한·실기·배포갱신 및fullWPF간헐항목은후속이며전체Healing품질완료로판정하지않는다.

## 코어 성능·자연 사진 품질 probe

--healing report [count=48] [diameter=40] [expand] 측정CLI와 --healing-natural source report [cx cy]를추가했다. source는기존공개rembg 예제사진 artifacts/subject-plants.jpg(987×1481)이다. natural probe는radius3 black defect를주입하고원본groundtruth의29pixel RGB오차/결함밖모든채널불변/손상source스냅샷불변을검사한다. 원본·손상·3모드의32px patch를4배확대한comparison PNG도시각검토했다.

4Kdense/48입력: 40px input35.6ms/최대2.13ms/complete83.7ms/managed131.6MiB/peak322MiB; 800px input526ms/최대19.8ms/complete543ms/managed400.7MiB/peak439MiB. 작은64px source를계속확장한40px48입력은239ms/최대10.4ms/complete37.9ms이지만managed755MiB/peak64MiB로누적할당이높다. managed는source준비제외,peak는준비포함이고각1회core이다. WPFframe/취소응답을입증하지않는다. expand는성능probe이며투명영역보정의자연품질을대체하지않는다.

고정seed123 자연probe RMSE(원본0–255 RGB 대비): 평탄부 손상174.8→ContentAware/Proximity1.10, CreateTexture43.2; wood76.2→17.6/17.6/27.5; leaf154.4→16.0/22.6/47.8. 순서는ContentAware/Proximity/CreateTexture이며원본미손상pixels는세모드모두같다. 확대검토에서ContentAware/Proximity는결함을줄였으나원래결/잎디테일복원은불완전, CreateTexture는grain자국이눈에띈다. 한사진세위치/작은합성결함이며일반품질·실제Mac동등성통과가아니다. 다중사진/큰선택/에지/전처리·대형UI/취소/할당최적화·배포검증은잔여다. 이번변경은benchmark만이며제품연산은그대로다.

## 정렬된 확장 작업 재사용

Healing의 상대 frame 원점이 256px 타일에 정렬되면 BrushStroke.ReframeAligned로 coverage와 wash의 작업 버퍼/이전 입력을 옮기고 현재 segment만 적용한다. 원점이 비정렬이면 기존 전체 재생 경로를 유지한다. native 보정에 전달하는 frame은 계속 정확한 필요 크기이며 ROI/seed를 바꾸는 여유 영역을 추가하지 않는다. 부분 edge tile은 새 base에서 original/output을 구성하고 기존 논리 범위만 복사하며 새 영역의 selection weights를 다시 계산한다. Clone도 같은 이전 작업 전달 계약을 사용한다.

코어 680개 통과: solid tile을64px로crop해 남긴 비영 padding,48point의256px경계 확장, feather 선택과 새 셀, 왼쪽 비정렬 fallback, 전체 재생 대비 coverage/wash/최종 CreateTexture 픽셀 일치, 이전 preview스냅샷 불변. 기존 Clone·mask·rotationFlip검사 포함. Release0경고/오류, 단독 실제 Healing WPF의3모드/취소/late worker/대기저장/왕복/경계확장검사 통과. fullWPF간헐 실패의 해결 증거로 사용하지 않는다.

동일4K canvas/64px source/40px tip/48point/seed123 측정: 이전 input257.0ms/최대13.0ms/완료40.4ms/누적할당755.5MiB, 최종 input88.7ms/최대15.3ms/완료38.8ms/할당170.1MiB. 누적할당 약77.5% 감소. 이전 측정의 해시용 작은RGBA 할당 약0.1MiB는 포함됐으며 최종 측정은 해시 계산 전 할당값을 고정한다. 결과 SHA256은 둘 다 A9D244361781046B83740DF2B1060D8D5ED4AA42445DA014885BE2A6DBA66991. 각 단일 core 실행이며 최대 입력 지연 개선은 입증하지 못했다. dense800px/UI응답/native취소/자연품질/비정렬 확장 비용은 계속 잔여다.

## 실제 WPF 대형 브러시 측정

`Compositor.Windows.exe --healing-benchmark report 4000`은 dense4K 원본의 검은 결함을800px/48point로 보정한다. 입력 사이 Background Dispatcher를 진행시켜 실제 rendering을 포함한다. 단일Undo/원본Undo복원/결함 개선을 검사하고10ms Input timer 간격과 단일입력 시간을 기록한다. 취소 probe는 실제 SpotHealing.Compute delegate 진입 신호를 받은 뒤Esc를 보내 원본 즉시복원/transaction종료/Undo없음/늦은결과폐기를 검사한다. 신호는native호출전이며 실제native내부취소를 입증하지 않는다.

개발 실행: 입력+render1078ms/단일입력최대30.6ms/끝worker541ms/Dispatcher최대165ms/EscUI복원2.99ms/worker정리3.29ms. self-contained 배포 실행:1086ms/27.1ms/564ms/156ms/2.94ms/3.39ms. 누적할당651MiB·peak809MiB는검증용fullRGBA2벌/취소probe를포함하므로이전corestroke할당과직접비교하지않는다. 각1회이며물리입력/일반자연품질/Mac동등성 검증이아니다. rendering포함UIgap과native중간취소는개선대상으로남긴다.

최신실행패키지는 `artifacts/publish-healing-ui/Compositor.Windows.exe`다. 기존 실행패키지폴더는유지한다. 빌드0경고/오류, portable45초gate1회통과:공백경로copy/제한PATH·무효sharedruntime경로/패키지내CLR·Skia·C모듈관측 및전체WPF회귀. 같은개발PC이며별도cleanWindows실기를대체하지않고일반30초gate안정화증거로사용하지않는다. 제품연산변경없음,이전680코어검증유지.
