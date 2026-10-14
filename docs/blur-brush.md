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
