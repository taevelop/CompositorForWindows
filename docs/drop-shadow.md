# Drop Shadow

2026-10-06. Effects → Drop shadow… 또는 Selected layer의 Drop shadow…로 픽셀 레이어에 적용한다.

## 사용과 저장

- 각도(-360..360°), 거리(0..5000px), 흐림(0..500px), 불투명도(0..100%)는 슬라이더와 숫자 입력을 제공한다. 색상은 기존 색상표를 재사용한다.
- 활성화, 미리보기 비교, 초기화, 제거, 적용/취소를 지원한다. Apply 한 번은 Undo 한 항목이고, 취소·no-op는 기존 이력을 보존한다.
- 원본 픽셀은 수정하지 않는다. 마스크 적용 후의 원본 알파에서 그림자를 만들며, 색상 오버레이와 공존한다. 한 효과를 제거/수정해도 다른 효과를 보존한다.
- 원본 Swift의 ShadowEffect와 동일하게 effects.shadow의 angle/distance/blur/red/green/blue/opacity 및 선택 enabled를 저장한다. 누락/null enabled는 true다. 비활성 효과의 설정도 보존한다. 잘못된 필드/범위/중복 필드는 거부한다.
- 그룹/조정 레이어의 효과와 Stroke/Inner Shadow/Outer Glow는 아직 지원하지 않는다.

## 합성

마스크 → 원본 알파 기반 그림자 → 색상 오버레이를 적용한 원본 → 전체 레이어 불투명도/합성 모드 순서다. 그림자 위치는 x=-cos(angle)*distance, y=sin(angle)*distance로, 90°는 아래쪽이다. 흐림 sigma는 원본 CPU 경로와 같은 blur/2를 사용한다. Skia와 CoreImage의 필터가 픽셀 단위로 같다고 주장하지 않는다.

[SkiaSharp DropShadowOnly API](https://learn.microsoft.com/en-us/dotnet/api/skiasharp.skimagefilter.createdropshadowonly)를 사용하며 레이어 영역 밖으로 확장된 그림자를 문서 경계에서 자른다. 전체 소스 이미지를 사용해 타일 경계 흐림 이음새를 피한다. 화면/출력은 같은 합성기를 사용한다. 유효 그림자가 있는 문서의 픽셀 변경은 전체 뷰포트를 갱신한다.

마스크 적용 소스와 오버레이 소스 캐시는 렌더러당 마지막 한 레이어만 보유한다. 원본·마스크·오버레이 값 변경 시 폐기하며 그림자가 없는 문서로 바꾸거나 렌더러를 폐기할 때 해제한다. 레이어 크기에 비례하는 CPU 이미지 메모리가 추가된다.

## 검증과 성능

- xUnit: 각도, 확장 영역, 레이어 불투명도, 활성/비활성, 균일/전체 마스크, 타일 경계, 브러시 후 캐시, 회전·뒤집기·화면/출력 일치, 저장 왕복·Undo/Redo, 잘못된 값 거부.
- WPF: 미리보기·비교·적용·취소·no-op·잘못된 입력·제거·오버레이 공존, 실제 설정 창 렌더.
- 재현: build.ps1 -Test, dotnet run --project Compositor.Benchmarks -c Release -- --shadow artifacts/shadow-benchmark.json.
- 배포: build.ps1 -Publish -PublishDirectory artifacts/publish-shadow, verify-portable.ps1 -Runs 1 -PackageDirectory (Join-Path $PWD artifacts/publish-shadow).

4K 레이어, 1000px CPU 뷰포트, 그림자 각도120/거리45/흐림24, 800px 브러시 10회 조건의 로컬 측정이다. 180ms 입력 대기와 WPF 실제 표시 지연은 제외한다.

| 조건 | 효과 변경 | 이동 | 브러시 입력+표시 |
|---|---:|---:|---:|
| 마스크 없음 | 34–39ms | 35–37ms | 87–170ms |
| 1×1 회색 마스크 | 42–52ms | 38–45ms | 144–232ms |

그림자가 있는 문서의 브러시 연속 입력은 기존 기본 문서의 33.3ms 기준에 미달한다. 기능 지원과 성능 완료를 구분하며, 전체 전환의 품질·성능 잔여 항목에 유지한다. 전체 소스/그림자의 증분 갱신 또는 비동기 합성이 다음 최적화 후보이다. 실제 Mac 왕복은 사용자 지시에 따라 보류다.
