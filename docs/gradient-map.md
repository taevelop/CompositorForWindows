# Gradient Map 비파괴 조정

2026-10-06. Image → New adjustment layer → Gradient Map…으로 만들고 Image → Edit adjustment → Gradient Map… 또는 선택 레이어 패널에서 재편집한다. 조정 종류가 늘어나 생성/편집 메뉴를 하위 메뉴로 정리했다. Invert는 설정이 없어 편집 메뉴에 없다.

## 동작

Shadows와 Highlights 버튼에서 기존 색상 영역·색조 막대·RGB/HEX·팔레트를 사용한다. 선택한 두 색을 그라디언트로 표시하며 Reverse gradient로 방향을 바꾼다. 원본에 있던 두 끝점 방식이며 여러 정지점 편집기는 아니다.

원본 macOS ImageAdjustments.swift의 GradientMapSettings와 AdjustPixels.c의 adjust_gradient_map을 기준으로 구현했다. 부동소수점 두 색을 보존하며 256개 RGB 표를 원본의 보간 순서와 반올림으로 만든다. C 커널은 알파를 풀고 0.2126R+0.7152G+0.0722B 밝기에 해당하는 색을 고른 뒤 다시 premultiplied 알파로 변환한다.

원본 픽셀은 유지하며 아래 합성에 적용한다. 연결 마스크, 그룹/레이어 불투명도, 지원 합성 모드, 생성/편집 취소·원본 비교·Undo/Redo를 지원한다. 창을 줄이면 설정만 스크롤하고 적용·취소는 고정한다. .comp의 gradientMapSettings.shadows/highlights(red/green/blue 0..1), reversed를 저장한다. 누락/null이면 흑백 기본 설정으로 읽으며 알 수 없는 필드는 거부한다. 실제 Mac 왕복은 사용자 지시대로 보류한다.

## 검증과 성능

Release 빌드 경고/오류 0, xUnit 324개 통과. 새 테스트는 원색의 밝기 결과, 반투명 알파, 두 끝점과 반전, 마스크·불투명도·원본 공유, 마스크 페인팅, 저장 왕복과 합성 일치, 비정상 색 입력 거부를 포함한다.

실제 WPF에서 색상표를 열어 파란색 선택, 생성/편집 취소, 비교 상태에서 Apply, 단일 Undo/Redo와 no-op, 창 렌더를 검사했다. 생성/편집 하위 메뉴의 팝업·다크 템플릿·비활성 색도 검사했다.

4000×4000, 알파160 원본 1개와 조정 1개, 1000px CPU 뷰포트: 정방향 최초315ms/캐시 이동2.7ms, 역방향 최초273ms/이동2.8ms. 1회씩 측정했으며 WPF 표시 시간은 제외한다. 전체 재계산 지연은 비동기/증분 갱신 최적화 대상이다.

재현:
- ./build.ps1 -Test
- dotnet run --project Compositor.Benchmarks -c Release -- --gradient-map artifacts/gradient-map-benchmark.json
- ./build.ps1 -Publish -PublishDirectory artifacts/publish-gradient-map
- ./verify-portable.ps1 -Runs 1 -PackageDirectory (Join-Path $PWD artifacts/publish-gradient-map)

추가 비파괴 조정 잔여는 Hue/Saturation이다. 색상군별 조절 범위·밴드·반전·Colorize를 원본과 대조해 이식해야 하며, 기존 단순 이미지 채도 조절로 대체하지 않는다. 픽셀 필터 경로와 전체 전환의 다른 영역도 계속 잔여다.