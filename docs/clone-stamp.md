# Clone Stamp 전환

## 원본 규칙 및 현재 기반

`macOS/Compositor/Document/CloneStamp.swift`와 `EditorSession+Brush.swift`를 대조했다. Alt-click은 소스를 설정하고 이전 offset을 지운다. Aligned 기본 true는 첫 스트로크의 정수 offset을 다음 스트로크에도 유지한다. false는 매 스트로크 소스에서 다시 시작한다. 반 픽셀 반올림은 Swift처럼 0에서 멀어지는 방향이다. 스트로크 중 crosshair는 고정 offset을 따라가며 비정렬 idle에서는 원래 소스를 표시한다.

`CloneSource`에 이 상태 전이를 구현했다. `CloneStamp.Capture`는 스트로크 시작 문서 크기의 불변 Raster를 만든다. 기본 raw active layer sample은 Transform만 유지하며 mask/effects/opacity/visibility를 제외한다. Sample All Layers는 보이는 합성 결과를 사용한다. 그룹/조정/없는 활성 레이어는 투명 샘플이다. 취소는 렌더러와 전후 체크를 따른다.

검사 범위: 소스 없음, 양수/음수 반 픽셀, aligned 유지/비정렬 재설정, 새 소스/비유한 입력, crosshair 상태, raw/composite 분리와 원본 보존/사전 취소. 아직 Clone Stamp 사용자 도구는 아니다.

## 남은 구현

- 고정 샘플을 tip coverage로 source-over 합성하는 스트로크, 선택/타일 경계/회전 및 투명 소스 검사.
- 기존 brush raster 확장과 mask 좌표·pixel budget·Undo transaction 규칙 연결.
- S 도구·Alt-click·Aligned/All layers 옵션·소스 crosshair/팁 미리보기 및 문서별 상태.
- 입력 취소/도구 변경/탭 전환, 단일 Undo/Redo, 저장 후 재열기와 출력 검증.
- 대형 샘플 메모리/연속 스트로크 성능 및 물리 입력 검증.

원본은 image clone을 mask 편집에 적용하지 않는다. 별도 Blur는 softened clone을 사용하며 Smudge/Liquify의 copy 모드는 Clone Stamp와 별도 범위다. Spot Healing의 Content-Aware/Proximity/Texture 모드도 별도로 대조/구현한다. 실제 Mac 왕복 및 별도 Windows PC 검증은 보류한다.

## 검증 기록

Release build 경고/오류0, 코어654개 통과. 전체 WPF 검사는 동일30초 제한 두 번 모두 timeout: 마지막 표본은 inner-shadow 패널까지 진행했다. 기존 간헐적 timeout의 원인과 단계별 시간은 미확정이며 이번 단계의 전체WPF 통과로 표시하지 않는다. 새 Clone 코드는 아직 WPF에서 호출하지 않는다.

## 스트로크 기반 연결

BrushStroke의 optional clone sample/offset 경로를 추가했다. 고정 source를 document 좌표의 premultiplied bilinear로 읽고 maximum swept coverage·opacity·selection을 곱해 시작 pixels에 source-over 합성한다. transparent sample은 no-op, copy-mode Blur/Smudge는 별도다. 동일 point 반복은 density를 증가시키지 않는다. 기존 paint/erase 경로는 그대로 사용한다. source/target snapshot 보존·반투명 수치 참조·흰색 target·타일경계·selection·소수 offset 검사를 포함해 코어657개 통과. 전체WPF30초timeout 재발로 미통과 유지.

현재는 기존 layer raster 크기 안에서 stroke 기반만 연결됐다. raster 확장/변형 마스크 예산·S 도구/UI·문서별source·Undo/저장·직접입력은 후속이다. CoreGraphics medium interpolation과 Windows bilinear의 실제Mac 결과 동등성은 아직 검증하지 않았다.

## UI 1차 연결

S/rail index13 CloneStamp, Alt-click 소스 지정, Size/Hardness/Opacity 기존slider와 Aligned/All layers checkbox를 연결했다. color/mask 옵션은숨기고 image-only guard를 적용한다. 기존 Session Begin/Preview/Commit/Cancel로 단일Undo/Redo·cancel을 처리한다. Clone 설정/소스상태는 TabTools에 저장하고 새탭은 독립 빈소스로 시작한다.

실제WPF BeginPointer Alt 소스지정(Undo없음)·클릭결과·Undo/Redo·cancel·새탭/복원 검사통과. 코어657개 및 fullWPF 기존30초제한1회통과, 빌드경고/오류0. 도구레이아웃 검사는14도구로확장했다. 물리 S/Alt/drag는별도미검증. 현재target raster크기내 편집이며 raster확장/마스크배치예산·source crosshair/팁미리보기·저장전용검사·대형sampleworker는후속이다. 사용자도구 전체완료로판정하지 않는다.

## 소스 표시와 저장 검증

Clone source crosshair/브러시범위원을 device크기 일정한 black/white outline으로 연결했다. idle/stroke aligned samplepoint 규칙을 따르며 pointerleave와 sessionchange에서 hover를 지운다. 준비중 frame에서도 표시한다. 팁 안 실제source image 미리보기는 아직없다.

--clone-smoke report CLI로 전체회귀와 동일한 Clone 전용WPF검사를30초한도내 실행한다. sourceoffset 표시·클릭/UndoRedo/cancel·독립tab에 더해 .comp저장재열기 composite 및 PNG모든픽셀 일치를 검사했다. 단독exit0/화면검토통과, 빌드0경고오류·코어657개통과. fullWPF는30초timeout재발로미통과이며 단독검사로대체하지 않는다. target raster확장·tippreview·대형source worker/메모리·물리입력 및 배포갱신은잔여다.

## 팁 이미지 미리보기

원본clonePreview의 화면해상도/1024px한변상한과 raw/all-visible 규칙을 대조했다. source-offset으로 이동한 작은영역만 표시bitmap에 렌더하고 tip radialcoverage/hardness·opacity를 premultipliedRGBA에 적용한다. idle에서만 표시하며 stroke진행/Alt누름에는숨긴다. pointer/samplecenter·Document/activeLayer·settings·deviceZoom key의 단일SKImage캐시로 동일state는재사용하고 session변경/Dispose에서해제한다. 숫자invalid draft는Paint를중단하지않고 tip표시만생략한다.

반투명/opacity·raw/all-visible·1024상한·원본보존·취소검사 코어658개통과. 최신빌드0경고오류·Clone단독WPF source/Undo/cancel/tab/저장/PNG와 tip시각검토통과. fullWPF는이번단계재검사하지않았으며 이전30초timeout미해결을유지한다. 축소tip와CoreGraphics medium보간동등성·복합문서대형hover/frame비용·입력/샘플worker·renderer캐시/메모리·target raster확장 및 배포갱신은잔여다. 출력bitmap상한이source/effect렌더메모리상한을뜻하지않는다.

## 대형 raw tip 미리보기 측정

--clone-preview report [side] 벤치마크 추가: 합성ellipse·800px tip·세개의인접 sourcecenter, 출력800px/중앙alpha204·원본Raster참조보존을검사한다. 4K 기존경로최초45ms/후속17ms, 재측정47/18/18ms·managed각10.23MiB·프로세스피크276MiB. 100MP는47/19/19ms·각10.33MiB·피크1517MiB. 피크는전체합성source생성과3회실행을포함하며 preview만의메모리로해석하지 않는다.

CanvasRenderer는transform하의 LocalClipBounds에교차하는tile만 TileImage로변환한다. rawpreview가전체sourcebitmap을매hover복사한다는가정은이번소스/측정에서지지되지 않았다. managed직접bilinear실험은4K최초197/후속72·63ms로느려지고피크차이도작아되돌렸다. 최종제품알고리즘은기존경로를유지한다. 코어측정은 WPFframe·100MP복합효과·all-layer·지속hover·stroke시작fullcapture·취소성능을증명하지 않는다. target raster확장과이들항목은계속잔여다.

## 래스터 확장 연결

CloneStroke는stroke시작문서/source/sample/offset을고정하고 문서canvas로clip한swept brush영역을source pixel좌표에매핑해frame을확장한다. RasterReframe으로기존pixel위치·회전/flip·linkedmask흰여백·independentmaskPlacement pin을유지한다. frame변경시기존points를시작pixels에재생하므로opacity/선택이preview에서누적되지 않는다. frame내에서는기존BrushStroke를이어쓴다. preview는항상시작document기반이며transparent/no-op은원본참조를반환한다. 할당전image/mask총예산과결과Undo256MiB를검사한다.

EditorCanvas Clone경로에연결하고cancel/end에서worker상태를해제한다. Core662개통과: 경계성장/기존coverage·white mask·source보존·UndoRedo·독립mask핀·transparent무변경·예산실패·회전+FlipXY16원본픽셀문서위치 검사. 최신WPF단독Clone30초내exit0: 작은layer밖clone/linkedmask/Undo/저장재열기까지통과. 빌드경고/오류0. fullWPF30초timeout및실제Mac/별도PC보류는그대로유지한다.

반복frame변경은point list를재생하므로긴확장stroke성능/할당·최종alpha경계trim·큰sourcecapture/취소worker·all-layer/선택/복합mask추가검증·배포갱신·물리입력은후속이다. 기본확장경로연결로전체도구품질완료를주장하지 않는다.

## 최종 경계 정리

원본 paintSnapshot은 sourceRect를항상유지하고새coverage alpha영역과union한다. CloneStroke.Complete도원본사각형을유지하고확장margin의tile만native AlphaBounds로스캔해crop한다. 원본내부tile은스캔하지않고 RasterReframe으로transform·linkedmask/독립placement를보존한다. 확정결과검증/Undo예산검사후게시하며중복Complete는동일참조를반환하고이후Append는거부한다. 취소는정리하지않고원본문서로복원한다.

Core663개통과: 11×11초안을10×10으로정리하면서원본rect/white mask유지, 전후16×16canvas모든채널일치·원본/완료불변 검사. WPF단독Clone30초내exit0: 큰softtip draft7px→mouseup확정5px·mask·Undo/Redo·저장재열기검사. 최신빌드0경고오류. fullWPF30초timeout및Mac/별도PC보류는여전히별도다. 긴확장stroke재생비용·큰samplecapture/worker·복합sample·물리입력·배포후속이다.

## 확장 작업 경계 예약 및 측정

필요frame을 먼저검증한후256px tile 경계까지여유를예약한다. 예약frame이크기/image/mask예산을넘으면검증된정확frame으로fallback한다. 각pointerpixel마다reframe/replay하지않고예약경계내에서stroke를이어쓴다. 최종Complete는예약투명여백을제거하므로확정/출력geometry를유지한다. 30000px 한계test에서좌표변환극소오차가ceil30001을만드는문제를발견해1e-7px 이내정수좌표를정규화했다.

--clone-stroke report [count=48] [diameter=40] 측정CLI추가. 4K·40px·48입력기준 최초비교: growth48→2, input830→172ms, 최대47→42ms, complete2.90→0.80ms, managed737→39MiB, processpeak425→332MiB. PNG SHA256 C8D43E3E02C2B120F92769A16B80337DF58603CB92E477494A3E4727C6FAAF0C 동일. 최신정규화후40px재측정268ms/최대116ms로변동도기록하고PNG동일성재확인했다. source원본bytes보존·result.Validate를검사한다. 할당은PNG출력을포함하며peak는source준비포함이다.

800px·48입력추가측정은growth3·총2523ms·최대910ms·complete7.14ms·managed417MiB·peak394MiB. 최대입력지연은높아미완료성능항목이다. 모두합성core각1회이며WPF프레임/물리입력/자연texture증거가아니다. 코어664개/빌드0경고오류·최신Clone단독WPF통과. fullWPF30초timeout별도유지. 큰coverage재합성/경계replay·sourcecaptureworker/취소·복합sample·배포검증이남는다.

## Clone coverage delta 최적화

compositor_clone_coverage 고정폭export를추가했다. sweptgeometry/linearhardness의max floatcoverage를유지하고새로coverage가증가한pixel만delta로표시한다. paint/erase커널과기존export의pixel-change계약은변경하지않았다. clone은Work.Output을재사용하며delta가없는tile/pixel은샘플조회·source-over재합성을생략한다. 기존originalpixels와frozenclone source를계속사용하므로stroke교차/반복에서opacity가누적되지않는다.

4K800px48입력단일비교: input2523→1439ms, 최대910→442ms, complete7.14→6.12ms, managed417→168MiB, peak394→370MiB. 출력PNG SHA동일 확인. 각1회합성core/파일출력포함이며비교최대값의반복안정성및WPF지연을입증하지않는다. 최대442ms는여전히높아성능완료가아니다.

반투명gradientsource+fractionaloffset·삼각교차stroke를독립segment거리/source-over참조와전픽셀1LSB이내비교하고반복동일point의Raster참조유지를검사했다. 코어665개·MSVC/Release0경고오류·Clone단독WPF source/Undo/cancel/tab/확장/mask/저장검사통과. 큰frame재생·sourcecaptureworker/취소·복합sample·배포/물리입력과fullWPF30초timeout은잔여다.

## 경계 확장 시 작업 타일 이동

타일정렬확장에서는BrushStroke.ReframeClone으로oldcoverage/Output/previouspoint를shift된key로이동하고이전points전체를재생하지않는다. 정렬되지않는정확frame fallback은기존재생을유지한다. 부분edge tile은grownbase로새cells를초기화하고old논리영역Output만복사한다. selectioncoverage도새논리영역까지갱신하며zero-change stroke는grownbase참조를유지해transparent/no-op확장Undo를방지한다.

초기측정은800px48입력952ms/최대170ms이나이전PNG와536pixels·최대1LSB차이가있어동일성불통과로진단했다. sample위치를다른frame폭에의존한나눗셈대신nativecoverage와동일affine식으로계산해half-opacity부동소수반올림을안정화했다. 최종측정912ms/최대163ms·complete8.96ms·managed97.3MiB·peak368MiB. 이전PNG와912pixels/최대1LSB차이를명시한다(이전경로와bit-exact주장안함). --pixel-diff 두파일 report 로RGBA변경수/최대오차/예시좌표를재현한다. 비교이미지16Mpixel이며source준비/PNG조건은기존benchmark와동일하다.

수학적reference로canvaspixelcenter의128alpha/halfRGB·확장전후불변, 잘린200px tile의nonzero padding이newmargin에누출되지않는것, 기존gradient/삼각coverage/source-over·core667개 검사통과. 최신빌드0경고오류·Clone단독WPF source/Undo/cancel/tab/확장/mask/저장검사통과. 163ms최대도낮은입력지연완료로판정하지않으며반복/회전복합확장·worker샘플준비/취소·배포/물리입력과fullWPF30초timeout은잔여다.

## 샘플 타일 복사 취소 기반

Raster.FromRgba의선택적CancellationToken을추가해시작/각256px tile복사/최종immutable반환직전에검사한다. CloneStamp.Capture/Preview가렌더링·tip계산뿐아니라최종tile스냅샷복사에도동일token을전달한다. 취소중의부분builder는게시하지않으며기존호출자는default token으로같은출력을유지한다.

사전취소/입력buffer불변·257×263tile경계출력·완료Raster의외부입력변경격리 검사포함해core668개통과. 전체Release0경고오류·Clone단독WPF Undo/cancel/tab/확장/mask/저장통과. --clone-capture-cancel report [side=4000]은실제Capture에5ms후취소요청을보내28.5ms/OCE및빈source불변을확인했다. 정확한취소phase는bitmap준비/render/tilecopy중어디인지증명하지않으므로copy중취소또는WPF Esc응답으로대체해주장하지 않는다.

현재캔버스의큰sample Capture는아직동기호출이다. background worker/입력queue·문서/transaction세대보호·진행표시/Esc/캡처상실취소연결·실기·배포갱신이후속이며UI응답완료로표시하지않는다. fullWPF30초timeout별도유지.

## 큰 샘플 준비 worker 연결

1MP이상canvas는sample Capture와CloneStroke/selectioncoverage초기화를Task.Run에서준비한다. 시작session/document/transaction generation·layer/settings/offset을고정하고serialgate로capture를제한한다. 준비중MovePointer를UI queue에보관하고각batch를worker에서순서대로Append한다. mouseup은Ended표시만하며준비결과설치후Complete/단일Undo확정한다. 이후live입력은기존CloneStroke경로다.

결과설치는currentCanvas/session/tool/image-target/activeLayer·originalDocument참조·transaction세대가일치할때만한다. Esc/캡처상실/도구·문서전환/Dispose는준비token을취소하고자신의transaction만복원한다. 늦은완료는새document/transaction을닫지않는다. preparing표시중에는tip source preview를생략한다. Clone selectioncoverage를UI에서중복생성하지않게했다.

지연주입capture를사용한WPF검사: 준비중원본/Undo0유지·initial+queuedpoint·mouseup단일Undo, windowKeyDown Esc취소/늦은결과폐기, session.Load+새Begin generation유지 통과. 기존Clone source/Undo/cancel/tab/확장/mask/저장도동일검사에포함됐고최신Release0경고오류·단독WPF30초내exit0. standalone테스트는Canvas Loaded/Fit을먼저dispatcher idle에서완료한뒤입력을시작한다. 실제4K/100MP worker의dispatcher지연·queue길이/취소응답·진행시각/물리입력·배포본은후속이다. fullWPF30초timeout미해결유지. core668개의이전검증코드는변경하지않았다.
