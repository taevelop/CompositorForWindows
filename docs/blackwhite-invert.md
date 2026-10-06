# Invert / Black & White 비파괴 조정

2026-10-06. Image → New Invert adjustment layer 또는 New Black & White adjustment layer…로 생성한다. 흑백 레이어를 선택한 뒤 Edit Black & White…로 재편집한다. 반전에는 별도 설정이 없다.

## 동작과 호환

- 반전은 premultiplied 채널을 alpha - channel로 바꾼다. 알파를 보존하고 두 번 적용하면 원본이 된다.
- 흑백은 원본 C adjust_black_white 커널을 재사용한다. Reds/Yellows/Greens/Cyans/Blues/Magentas 기본값은 40/60/40/60/20/80이며 범위는 -200..300이다. Tint, 색조0..360, 채도0..100을 지원한다.
- 여섯 색상군과 틴트는 슬라이더·정밀 숫자 입력을 제공한다. 창을 줄여도 적용·취소·비교 버튼을 하단에 유지하고 설정만 스크롤한다.
- 원본 이미지에 직접 쓰지 않고 아래 레이어들의 합성 결과에 적용한다. 조정 마스크, 레이어/부모 불투명도, 지원 합성 모드, 표시/숨김, 순서, Undo/Redo를 기존 경로로 처리한다.
- 조정 레이어 원본에는 그림을 그릴 수 없고 마스크에는 브러시·지우개를 사용할 수 있다.
- .comp adjustment.kind에 Invert 또는 Black & White를 저장한다. 흑백은 blackWhiteSettings의 원본 필드를 보존하며 없거나 null이면 기본값이다. Swift가 요구하는 비활성 기본 필드도 작성한다. 비지원 비활성 설정이나 알 수 없는 필드를 손실하며 열지 않는다.
- 색상 조정용 C 경계는 int32 픽셀 수를 받는 compositor_black_white다. 기존 C 커널 파일은 변경하지 않았다.

## 검증

Release 빌드 경고/오류0, xUnit 306개 및 확장 WPF 검사.
- 반전 모든 알파 값 보존과 이중 적용 복원.
- 흑백 여섯 원색/보색의 독립 기본값, 틴트의 알파·premultiplied 범위.
- 마스크·불투명도·변환, 원본 편집 거부 및 마스크 페인팅 허용.
- 두 종류의 저장 왕복, 원본 공유·Undo/Redo.
- 실제 WPF 생성·취소·비교·수정·잘못된 입력·마스크 편집·그룹 내부 생성·저장/재열기·PNG 일치. 반전 메뉴 생성과 Undo/Redo.
- 네이티브 빌드가 실패한 DLL을 최신 파일로 오인하지 않도록 성공 표식을 마지막에만 기록한다.

재현:
- ./build.ps1 -Test
- dotnet run --project Compositor.Benchmarks -c Release -- --blackwhite artifacts/blackwhite-benchmark.json
- ./build.ps1 -Publish -PublishDirectory artifacts/publish-blackwhite
- ./verify-portable.ps1 -Runs 1 -PackageDirectory (Join-Path $PWD artifacts/publish-blackwhite)

## 성능 잔여

4000×4000, 알파160 원본1개와 조정1개, 1000px CPU 뷰포트의 1회 측정: 반전 최초205ms/이동2.7ms, 흑백 최초1428ms/이동2.8ms. WPF 실제 표시와 입력 대기180ms는 제외한다. 원본·설정 변경 시 전체 문서 재합성 비용이 있으며 특히 흑백4K 입력 지연은 비동기/증분 갱신 개선 대상으로 남긴다. 실기 Mac 왕복은 사용자 지시대로 보류한다.

다음 추가 조정: Hue/Saturation, Gradient Map, Grain, Color Balance. 픽셀 필터 경로도 별도 잔여다.
