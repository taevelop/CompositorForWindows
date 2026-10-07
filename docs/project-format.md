# Windows 프로젝트 규격 — 버전 1–8

이 문서는 Windows 구현인 [ProjectStore.cs](../Compositor.Imaging/ProjectStore.cs)의 저장 형식과 읽기 제한을 설명합니다. macOS의 전체 기능 규격은 [macOS 원본 문서](../macOS/docs/project-format.md)에 있습니다. **Windows는 버전 1–8을 읽고 버전 8로 저장하지만, 해당 버전의 모든 기능을 지원하지는 않습니다.**

## 패키지와 필드

`.comp`는 단일 파일이 아니라 다음 내용을 담는 폴더입니다. PNG에 원본 픽셀과 투명도를 보관하고 레이어 변환은 별도 메타데이터로 저장하므로, 가져온 원본 사진을 이동하거나 삭제해도 프로젝트를 열 수 있습니다.

```text
Project.comp/
  manifest.json
  images/
    <레이어 UUID>.png
    <레이어 UUID>.mask.png  # 선택: 8비트 회색조, 알파 없음
```

manifest의 `format`은 `com.compositor.project`, `colorSpace`는 `sRGB`입니다. `documentID`, 정수 픽셀 크기 `width`·`height`, 같은 부모 안에서 아래에서 위 순서를 나타내는 `layers` 배열을 저장합니다. 자식은 파일에서 부모 바로 뒤에 연속해서 있을 필요가 없으며 부모 관계로 합성 순서를 구성합니다. 선택 레이어는 `activeLayerID`로 저장하며, 값이 있으면 실제 레이어를 가리켜야 합니다. 선택 정보가 없거나 null인 문서도 읽습니다. `resolution`은 pixels/inch 단위의 1–9600 값이고, 없거나 null이면 72입니다.

각 기본 픽셀 레이어는 다음 필드를 사용합니다.

| 필드 | 규격 |
|---|---|
| `id`, `name`, `isVisible` | 고유 UUID, 비어 있지 않은 이름, 표시 여부 |
| `isGroup`, `parentID` | 버전 2 이상 그룹 여부와 부모 그룹 UUID. 생략/null은 일반 레이어·문서 루트 |
| `imageFile` | `images/` 안의 `<레이어 UUID>.png`; 없거나 null이면 빈 레이어 |
| `maskFile` | 버전 4 이상, `images/<레이어 UUID>.mask.png`; 없거나 null이면 마스크 없음 |
| `maskEnabled`, `maskLinked` | 마스크 파일이 있을 때만 사용. enabled 기본 true, linked는 true 또는 생략/null만 지원 |
| `adjustment` | 버전 7 이상, 지원 범위의 `kind: Exposure`, `kind: Levels`, `kind: Curves`. `imageFile` 없음/null, 그룹 아님. 아래 Exposure 절 참고 |
| `opacity` | 0–1; 없거나 null이면 1 |
| `blendMode` | `Normal`, `Multiply`, `Screen`, `Overlay`, `Darken`, `Lighten`, `Difference`; 없거나 null이면 `Normal` |
| `transform.origin`, `transform.size` | Swift Codable의 CGPoint/CGSize와 같은 2원소 숫자 배열 |
| `transform.rotation` | 도 단위 시계 방향 회전; 좌표 원점은 왼쪽 위 |
| `transform.flipX`, `transform.flipY` | 가로·세로 뒤집기 |
| `transform.sampling` | `Nearest`, `Smooth`, `High quality` |

버전 1–2는 불투명도 1과 Normal 합성만 허용합니다. Windows의 Smooth와 High quality는 현재 모두 bilinear 샘플링이며, macOS의 고품질 필터와 동일하지 않습니다. 저장 시 UUID는 대문자를 사용하고, 이미지 파일명은 해당 UUID의 대문자 또는 소문자 표기만 읽습니다.

## 지원하지 않는 문서

Windows는 보존할 수 없는 기능을 버리고 열지 않습니다. 다음 항목이 있으면 프로젝트 전체 열기를 거부합니다.

- 그룹 마스크 및 Normal 외 그룹 합성 모드, 참조 마스크(`maskSourceID`), 독립 배치(`maskPlacement`), 연결 해제(`maskLinked: false`).
- Exposure·Levels·Curves 외의 조정 레이어 또는 보존할 수 없는 비활성 조정 설정, 도형, 색상 오버레이 외 효과 또는 그룹·조정 레이어의 효과, 텍스트 메타데이터와 비어 있지 않은 가이드 목록.
- 미지원 합성·샘플링 모드, 알 수 없는 manifest·레이어·변환 필드, 중복 JSON 필드.

미지원 레이어 필드는 null이 아닌 값이면 거부합니다. `effects`는 아래 지원 범위를 따르며, 일반 픽셀 레이어의 빈 객체는 허용합니다. `maskEnabled: false`는 유효한 `maskFile`이 있을 때 지원하며, 비활성 마스크 데이터도 보존합니다. 위 미지원 레이어 필드의 null 값, `isGroup: false`, 빈 가이드 목록은 허용합니다. 버전 번호가 8이라는 이유만으로 그룹 마스크·독립 배치 마스크 등 macOS 기능 전체를 읽을 수 있는 것은 아닙니다.

## 그룹과 계층

그룹은 `isGroup: true`, `imageFile: null`, `blendMode: Normal`로 저장하며 자체 원본 픽셀이 없습니다. 부모는 실제 그룹이어야 하고 순환·자기 참조·누락된 부모를 거부합니다. 최대 64단계 중첩 그룹과 그 아래 일반 레이어를 지원합니다. 그룹을 포함해 총 10,000개 항목 제한을 적용합니다.

버전 1의 그룹/부모 관계는 거부합니다. 버전 2–7의 그룹 불투명도는 1만 허용하며 버전 8부터 0–1을 지원합니다. 그룹은 별도 이미지로 합치지 않고 각 자식의 기존 합성 모드를 배경에 직접 적용합니다. 자식의 최종 불투명도는 자기 값과 모든 부모 값의 곱이며 부모가 하나라도 숨김이면 표시하지 않습니다.

자식 변환은 부모에 상대적인 좌표가 아닌 문서의 절대 좌표입니다. 그룹 이동은 모든 하위 항목의 변환 원점을 같은 양만큼 바꾸고, 그룹 자체 `transform`은 자식에 추가 적용하지 않습니다. 그룹 transform 메타데이터는 저장 시 보존합니다. 그룹 마스크는 아직 열지 않습니다. 그룹 접힘 상태는 파일에 저장하지 않습니다.

## 연결된 레이어 마스크

마스크는 8비트 회색조 PNG(color type 0, alpha 없음)이며 흰색 255는 표시, 검정 0은 숨김입니다. 커버리지에 색 공간 변환을 적용하지 않습니다. 원본 이미지와 같은 픽셀 크기 또는 1×1 균일 마스크만 읽습니다. 다른 해상도, 회전 EXIF, 알파가 있는 마스크는 거부합니다. `maskPlacement`는 null/생략, `maskLinked`는 true/null/생략인 마스크만 지원하므로 레이어 이동·크기·회전·뒤집기를 그대로 따라갑니다.

1×1 마스크는 처음 픽셀이 변하는 스트로크에서 원본 크기의 공유 불변 타일로 확장합니다. 합성은 원본 RGBA premultiplied 채널에 커버리지를 곱한 뒤 레이어 샘플링·불투명도·합성 모드를 적용합니다. Mac의 별도 마스크 샘플링 및 고품질 필터와 변환된 가장자리가 완전히 같음을 보장하지 않습니다.

마스크가 있는 투명한 빈 레이어도 원본 크기의 투명 PNG를 저장해, 변환된 크기와 픽셀 격자가 달라도 재열기에서 마스크 크기를 유지합니다. 마스크를 삭제한 뒤 저장하면 이전 `.mask.png`가 새 패키지에서 제거됩니다.

## 기본 색상 조정의 저장

Windows의 밝기·대비·채도 편집은 선택 이미지의 픽셀을 변경하고 기존 `imageFile` PNG에 저장한다. manifest에 별도 조정 필드나 Windows 전용 메타데이터를 추가하지 않는다. 원본 PNG 외에 마스크·변환·그룹 속성은 그대로 유지한다. Undo는 현재 세션의 불변 타일로 복원하며, 프로젝트 재열기 후에는 조정 전 픽셀이나 슬라이더 값을 복원하지 않는다.

이 픽셀 편집과 아래의 Exposure 조정 레이어는 별도 기능이다. 픽셀 편집 계산 방식은 [색상 조정 문서](color-adjustments.md)를 참고한다.

## 비파괴 Exposure 조정 레이어

버전 7–8의 `adjustment.kind: "Exposure"`를 읽고 버전 8로 저장한다. `exposureSettings`는 `exposure`(−20~20), `offset`(−0.5~0.5), `gamma`(0.01~9.99)이며 모두 유한 수다. `exposureSettings`가 없거나 null이면 0/0/1이다. 객체가 있으면 세 필드를 모두 요구한다.

Swift Codable에 필요한 `hue`, `saturation`, `lightness`, `colorize`, `levels`, `curves`는 Mac 기본값으로 저장한다. 입력에 이 값들이 있으면 기본값만 허용하며, `hsvSettings`, `gradientMapSettings`, `grainSettings`, `blackWhiteSettings`, `colorBalanceSettings`는 없거나 null만 허용한다. 사용하지 않는 설정이라도 비기본값을 조용히 제거하지 않고 문서 열기를 거부한다. 모든 중첩 중복/알 수 없는 필드와 지원하지 않는 kind를 거부한다. Levels·Curves kind의 활성 설정은 아래 절을 따른다.

조정 레이어는 그룹이 아니고 `imageFile`이 없거나 null이어야 한다. 별도 이미지 PNG를 저장하지 않으며 연결 마스크 PNG는 보존한다. 마스크 없는 내부 빈 픽셀 격자는 문서 크기이고, 1×1 외 마스크를 불러오면 그 마스크 크기로 빈 격자를 맞춘다. 이 빈 격자는 원본 이미지 100MP 합계에서 제외하고 마스크는 기존 마스크 100MP 합계에 포함한다.

합성 순서상 아래의 모든 표시 레이어를 조정한다. 그룹은 pass-through이므로 조정이 같은 그룹 내부에만 제한되지는 않는다. 유효 불투명도는 부모 값과 곱하고, 레이어 합성 모드·연결 마스크 커버리지로 원래 합성과 조정 결과를 섞되 알파는 유지한다. 변환은 마스크의 범위에만 적용하며 마스크가 없으면 전역 조정이다. 원본 픽셀과 설정은 별도로 보존하므로 재열기 후에도 설정을 편집할 수 있다.

실제 Mac 앱 왕복은 미검증이다. 지원 범위와 기준 소스는 [Exposure 문서](exposure-adjustment.md)를 참고한다.

## 검증과 저장 보호

캔버스와 이미지 크기는 한 변 최대 30,000px 및 100MP, 전체 소스 이미지는 합계 100MP, 마스크도 별도 합계 100MP, 레이어는 10,000개까지 허용합니다. manifest는 4MiB, 인코딩된 이미지 자산은 개당 512MiB로 제한합니다. 문서·레이어 ID, 중복 레이어, 유한한 변환·불투명도 값, 이미지 경로·누락·형식도 검사하며 심볼릭 링크와 junction은 거부합니다. 자세한 메타데이터 검증은 [Document.cs](../Compositor.Core/Document.cs)를 참고하십시오.

저장은 같은 상위 폴더의 임시 패키지에 작성한 후 다시 읽어 검증합니다. 기존 `.comp`를 `.comp.recovery`로 옮기고 새 패키지를 게시하며, 게시 실패 시 기존 폴더를 복구합니다. 두 번의 폴더 이름 변경 전체가 하나의 원자적 연산은 아닙니다. 복구 사본이 남아 있으면 다음 저장을 차단하므로, 원본과 복구 폴더를 확인하고 보존할 사본을 결정해야 합니다. 같은 경로의 동시 저장은 `.write-lock` 파일 핸들로 차단하며, 남아 있는 빈 잠금 파일 자체는 저장 차단을 뜻하지 않습니다.

Undo/Redo 히스토리와 화면 확대·이동 상태는 저장하지 않습니다. PNG/JPEG 내보내기는 합성된 별도 이미지이며 프로젝트 저장을 대신하지 않습니다. 실제 macOS 앱에서 생성한 대표 문서의 양방향 열기는 아직 검증하지 않았고, 현재 근거는 원본 코드·Swift 형태 fixture·저장 왕복 테스트입니다.


## 비파괴 Levels 조정 레이어

버전 7–8의 `adjustment.kind: "Levels"`를 읽고 버전 8로 저장한다. `levels.channel`은 RGB/Red/Green/Blue, `levels.ranges`는 해당 순서의 정확히 네 범위다. 각 범위에는 `black`, `gamma`, `white`, `outputBlack`, `outputWhite`가 필요하다. 검정 0~254, 흰색 검정+1~255, 감마 0.1~9.99, 출력 각각 0~255의 유한 수만 허용하며 출력 반전을 지원한다.

활성 `levels` 이외의 필수 조정 필드는 기본값이어야 한다. 비활성 `exposureSettings`는 없음/null 또는 중립값 0/0/1만 허용한다. Exposure와 같이 imageFile 없이 저장하고 마스크 파일·그룹·Undo를 지원한다. 계산은 개별 색 채널 후 RGB 순서다. 상세 범위와 검증은 [Levels 문서](levels-adjustment.md)를 참고한다.


## 비파괴 Curves 조정 레이어

버전 7–8의 `adjustment.kind: "Curves"`를 읽고 버전 8로 저장한다. `curves.channel`은 RGB/Red/Green/Blue, `curves.channels`는 해당 순서의 정확히 네 배열이다. 각 배열은 2~32개의 `{x,y}` 객체이며 x/y는 0~255의 유한 수, x는 엄격한 오름차순, 처음/마지막 x는 0/255다. 보간 기울기가 비유한 수가 되는 극단적 간격도 거부한다.

비활성 조정 설정은 기존과 같이 기본값만 보존한다. imageFile 없이 저장하고 선택 채널·점 목록·연결 마스크를 복원한다. 지원하지 않는 필드/중복 필드와 잘못된 점은 열기와 덮어쓰기를 거부한다. [Curves 검증 문서](curves-adjustment.md)에 보간과 실제 검증 범위를 설명한다.

## 색상 오버레이 효과

일반 픽셀 레이어의 `effects.colorOverlay`를 지원한다. 필수 필드는 `red`, `green`, `blue`, `opacity`(0~1 유한 수)이며 선택적인 `enabled`는 없음/null이면 true다. 효과의 색·불투명도와 비활성 상태를 원본 PNG와 별도로 보존한다. effects 없음/null/빈 객체도 허용한다.

stroke/shadow/innerShadow/outerGlow도 지원한다. 각각의 필드·합성 범위는 [Stroke](stroke-effect.md), [Drop Shadow](drop-shadow.md), [Soft effects](soft-effects.md)를 따른다. 그룹·조정 레이어의 effects와 알 수 없는/중복된 필드도 거부한다. 입력은 기존 버전 1~8, 출력은 버전 8이다. 효과 자체의 최소 버전 제한은 Mac 코드와 같이 별도로 두지 않는다.

마스크를 적용한 원본 위에 source-over 방식으로 덧입히므로 반투명 픽셀의 알파가 증가할 수 있다. 저장하는 원본 PNG에는 효과가 적용되지 않는다. 수식과 검증은 [색상 오버레이 문서](color-overlay.md)를 참고한다.

## 추가 비파괴 조정

Invert, Black & White, Color Balance, Grain을 지원한다. 필드와 범위는 [반전·흑백](blackwhite-invert.md), [색상 균형·그레인](color-balance-grain.md)을 따른다. 공통 필수 기본 필드, imageFile 없는 조정 레이어, 연결 마스크 및 비지원 비활성 설정 거부 규칙을 유지한다. Gradient Map도 지원하며 gradientMapSettings의 shadows/highlights 색과 reversed를 보존한다. [Gradient Map 규격과 검증](gradient-map.md)을 참고한다. Hue/Saturation도 지원한다. 구형 scalar 필드와 선택적인 hsvSettings를 따로 보존하며 색상군 사전은 Swift의 키·값 교대 배열이다. [Hue/Saturation 규격과 검증](hue-saturation.md)을 참고한다.

## 도형 메타데이터 지원 (2026-10-07)
Rectangle/Ellipse/Line의 shape 스타일과 PNG를 함께 읽고 저장한다. 위 미지원 목록의 도형 항목은 이 변경으로 대체한다. 픽셀 편집 후에는 shape를 제거한다. 도형 편집 UI와 크기 변경 시 재래스터화는 후속 작업이며 실제 Mac 왕복은 보류다.

## 독립 마스크 저장 지원 (2026-10-07)
maskPlacement 및 maskLinked를 보존해 저장/열기를 지원한다. 위 미지원 목록의 독립 배치/연결 해제 항목은 이 변경으로 대체한다. maskSourceID·그룹 마스크는 계속 미지원이다. Image Size의 독립 마스크는 아직 처리 전 거부하며 실제 Mac 왕복은 보류다.

2026-10-07 후속: 독립 마스크 Image Size 및 도형 재생성 거부 제한을 제거하고 원본 격자/배치 보존을 연결했다. 위 Image Size 거부 설명은 이 변경으로 대체한다.
