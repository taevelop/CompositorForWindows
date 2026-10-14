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
