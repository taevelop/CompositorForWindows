# Hue/Saturation 조정

2026-10-06. Image → New adjustment layer → Hue / Saturation…으로 생성하고 Image → Edit adjustment 또는 선택 레이어 패널에서 재편집한다. 기존 이미지 밝기·대비·채도와 달리 독립된 비파괴 조정 레이어다.

## 편집 UI

- Master 및 Reds/Yellows/Greens/Cyans/Blues/Magentas별 Hue/Saturation/Lightness 슬라이더·정밀 입력. 색상군 전환 후 값이 유지된다.
- Colorize는 색조0·채도25부터 시작한다. 끄면 원본과 같이 기본 설정으로 초기화한다.
- Master 외 범위에서 전후 스펙트럼, 네 개 밴드 핸들, 범위 반전을 제공한다. 마우스로 핸들을 끌거나 좌우 화살표로 이동, 위아래로 핸들 선택, Shift로 큰 폭 이동한다.
- 창 왼쪽 이미지 미리보기에서 휠 확대·중간 버튼 이동·Fit을 사용할 수 있다. Sample은 선택 범위를 찍은 색 중심으로 옮기고 Add/Remove는 범위를 넓히거나 좁힌다.
- Target은 찍은 색을 담당하는 범위를 선택해 가로 드래그로 채도를 조절한다. Ctrl을 누르면 색조를 조절하며 같은 드래그 중 다른 축의 변경을 유지한다. 투명하거나 거의 무채색인 픽셀은 샘플링하지 않는다.
- 캡처 상실·비활성화·진행 중 Esc는 대상 드래그를 취소한다. 창 Cancel/Esc는 전체 편집을 취소한다. 비교 상태에서 Apply해도 입력한 설정을 적용한다.
- 큰 창이 필요한 원본 패널을 Windows에서는 이미지 미리보기와 설정으로 나눠 배치했다. 작은 창에서는 설정만 스크롤하고 적용·취소 버튼은 고정한다.

## 저장·합성

구형 hue/saturation/lightness/colorize와 선택적인 hsvSettings를 별도로 보존한다. hsvSettings가 없으면 구형 Master 값으로 계산하며, 구형 레이어의 무변경 재편집은 설정을 새 형태로 바꾸거나 이력을 만들지 않는다.

hsvSettings는 range/colorize/invertRange/adjustments/bands를 저장한다. 원본 ColorRange는 CodingKeyRepresentable을 채택하지 않으므로 Swift 사전은 키·값 교대 배열로 직렬화된다. [Swift 공식 사전 인코딩 설명](https://github.com/swiftlang/swift-evolution/blob/main/proposals/0320-codingkeyrepresentable.md)을 참고했다. 빈/일부 범위 사전은 그대로 보존하고 누락된 범위는 계산 시 기본값을 사용한다. 중복 키·홀수 배열·알 수 없는 색상군/필드·비유한 값은 거부한다. 다른 조정의 보존할 수 없는 비활성 HSV 설정은 계속 거부한다.

원본 HueSaturation.swift의 밴드·HSL·361개 응답표와 33×33×33 색상 큐브 생성을 이식했다. CPU trilinear 보간이 CIColorCube를 대체한다. 알파를 풀고 보간한 뒤 한 번만 premultiply하며 중립값은 바이트를 건드리지 않는다. 아래 합성, 연결 마스크, 레이어/그룹 불투명도와 지원 합성 모드에 적용한다. 원본 픽셀은 유지하고 설정은 불변 값으로 Undo/Redo에 보존한다.

실제 Mac 왕복과 CIColorCube 픽셀별 비교는 보류 중이다. 같은 설계와 대표 수식 테스트를 통과한 근거이며 전체 결과가 동일하다고 단정하지 않는다.

## 검증

Release 경고/오류0, xUnit347개 통과. 원색 회전·회색 보존·색상군별 조정/반전·범위 가중치/샘플 연산·Colorize·모든 알파·8꼭짓점 보간·불변 설정, Swift 형태 fixture/legacy 필드 보존·잘못된 입력 거부·프로젝트 저장 왕복·마스크·합성·Undo를 검사한다.

실제 WPF는 색상군 값 유지, 스펙트럼 키보드 핸들, Sample/Add/Remove, Target/Ctrl 드래그, 버튼 짝·취소, Colorize 기본값, 생성/편집/비교/Apply/Undo/Redo/no-op와 구형 Hue300 보존을 검사했다. 렌더 이미지를 확인했으며 독립 패키지 실행 검사에도 포함한다.

재현:
- ./build.ps1 -Test
- dotnet run --project Compositor.Benchmarks -c Release -- --hue-cube artifacts/hue-cube-benchmark.json
- dotnet run --project Compositor.Benchmarks -c Release -- --hue-layers artifacts/hue-layers-benchmark.json
- ./build.ps1 -Publish -PublishDirectory artifacts/publish-hue-saturation
- ./verify-portable.ps1 -Runs 1 -PackageDirectory (Join-Path $PWD artifacts/publish-hue-saturation)

## 성능·후속 작업

4K RGBA 알파180 버퍼: 큐브 생성15.9ms/CPU 적용1211ms. 원본1개+조정1개, 1000px CPU 뷰포트: 일반 최초1469ms/캐시 이동2.8ms, Colorize 최초1394ms/이동3.1ms, 프로세스 working set419/483MiB. 각1회 측정이며 WPF·입력 대기·샘플링 비용은 제외한다.

동기 전체 재합성과 샘플링용 전체 이미지 렌더가 큰 문서에서 지연을 일으킬 수 있다. 비동기/증분 미리보기 및 CPU/GPU 최적화는 계속 잔여다. [전체 이미지 픽셀 적용 경로](pixel-adjustments.md)는 연결됐다. 선택 범위에만 적용하는 작업은 아직 미구현이다. 원본 새 Gradient Map의 foreground/background 초기 색 연결도 후속 도구 작업에 남아 있다.