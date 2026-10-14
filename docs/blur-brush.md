# Blur 브러시 전환

원본 기준은 `macOS/Compositor/Document/BlurTool.swift`, `EditorSession+Brush.swift`, `BrushStroke.swift`다. 원본 Blur는 stroke 시작 때 활성 raw이미지를문서좌표로렌더링하고 sigma=min(30,max(1.5,diameter/10))로Gaussian blur한sample을고정한다. offset0으로tipcoverage×opacity를통해source-over한다. 원본의replacesWithClone은Blur에서설정되지않으며Smudge계열의별도계약이다. 새stroke는변경된이미지를다시샘플링한다.

## 이미지 연산 기반

BlurBrush.Capture가활성raw이미지의transform/sampling을적용하고mask/effects/opacity를sample에서제외한다. CloneStamp.Capture와독립SkiaGaussianDecal경로를사용한다. 이미지문서경계밖은투명이다. sigma계산과capture前/후·복사/raster변환취소를연결했다. SkiaGaussian자체는중간취소되지않는다. CoreImage와Skia의커널/보간동등성은입증되지않았다.

BlurStroke는고정sample/offset0의CloneStroke를사용해확장격자·selection/feather·metadata/mask배치·source-overopacity·원본rect/newalpha crop·이미지/mask/Undo예산을공유한다. sample은stroke마다한번생성하고그stroke안에서는불변이다. Blur는erase를허용하지않고group/adjustment이미지를거부한다.

코어695개통과:513px흑백경계의256tile걸침/원본·이전preview불변/반불투명독립수식/다음stroke추가흐림/metadata·mask/단일UndoRedo,selection제외no-op,취소/범위검사,sigma1.5/4/30,16px원본투명경계확장·premul/linkedmask/좌표보존. 기존Clone/Healing회귀포함.

## 남은 연결

- Blur 도구 아이콘·단축키·공유Size/Hardness/Opacity슬라이더·탭설정 및pointer transaction UI.
- 대형sample async준비·queued pointer/취소/late generation 보호·끝 저장/내보내기 대기.
- 마스크Blur:원본mask배치와canvasbackground톤·edgeclamp를대조하고회색coverage불투명계약을구현한다. 이번Capture는이미지전용이며mask기능완료로표시하지않는다.
- 실제WPF/저장왕복·screen/export·큰brush/자연이미지/alpha경계품질·배포갱신. 기존Windows최대sweptcoverage와Macsoftstamp누적차이도품질범위에유지한다.

이번단계는코어연산이며실행패키지와UI는이전단계를유지한다. 실제Mac왕복/별도PC검증은사용자지시로보류다.

## 이미지 도구 UI·비동기 연결

Blur(R) 아이콘을rail16번째에추가하고Size/Hardness/Opacity공유slider를표시한다. 색/Clone/Healing옵션은숨긴다. 원본Smear(R)의Blur모드를연결한단계이며Smudge/Liquify와마스크Blur는별도후속이다. 16개tool이기본높이에들어가도록item높이30/세로margin1로조정하고작은창의실제scroll은유지한다.

ClonePreparation을tool소유자가있는frozen-sample준비로확장해Blur가항상worker에서sample을만든다. pointer점은UI에서큐로모으고worker가적용한뒤동일session/document/transaction generation/tool/image target일때만preview/install/commit한다. mouseup전설치면이후live입력을이어가고mouseup후면단일Undo로commit한다. Esc/tool/tab/Dispose는취소한다. captureoverride도시작때고정한다. sourcecrosshair/Alt-source는Clone에만적용된다.

공통명령대기에CloneCompletion을포함해Blur/Clone준비중저장/내보내기가준비결과를기다린다. busy Esc도공유준비를취소한다. 실제WPF Blur검사통과:queued입력/단일UndoRedo/프로젝트왕복/PNG-composite일치,지연capture중Esc·명령대기/UI잠금과복원,lateworker가새transaction을닫지않음. 기존Clone단독회귀와16tool startup scrollbar숨김/compactscroll/복원·title/canvas고정검사도통과했다. Release0경고/오류,제품코어변경없음·695코어검증유지.

마스크Blur·대형실측/중간Gaussian취소·실제MacCoreImage동등성/원본softstamp농도·물리입력/새패키지배포는잔여다. 이번BlurUI는개발Release빌드에만반영됐으며기존publish-healing-cancellable패키지에는없다.
