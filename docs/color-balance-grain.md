# Color Balance / Grain 비파괴 조정

2026-10-06. Image 메뉴에서 New Color Balance / New Grain adjustment layer로 생성하고, 선택 후 같은 종류의 Edit 메뉴 또는 선택 레이어 패널에서 재편집한다.

- Color Balance: Shadows/Midtones/Highlights 각각 Cyan–Red, Magenta–Green, Yellow–Blue 축 -100..100, Preserve luminosity. 원본 C 커널의 채널별 tonal weighting과 밝기 보존 계산을 사용한다.
- Grain: Amount 0..100, Size 0.5..20, Roughness 0..100, UInt32 Seed. 세 연속 값은 슬라이더와 정밀 입력을 제공하고 New pattern으로 무늬를 바꾼다. 새 레이어는 무작위 시드를 갖고, Reset은 기본값과 시드 0으로 돌아간다.
- 생성/재편집의 미리보기·비교·초기화·취소·Undo/Redo, 아래 합성에 대한 비파괴 조정, 연결 마스크와 그룹/레이어 불투명도를 지원한다. 원본 이미지 대신 마스크에만 칠할 수 있다.
- 문서 좌표와 저장된 시드로 그레인을 계산하여 뷰포트 이동·저장 재열기 때 무늬를 유지한다.
- .comp에 원본 colorBalanceSettings의 9개 축/preserveLuminosity, grainSettings의 amount/size/roughness/seed를 저장한다. 누락/null 설정은 모델 기본값이며, 지원하지 않는 비활성 설정·알 수 없는 필드는 거부한다. 실제 Mac 왕복은 보류 중이다.

## 검증

Release 빌드 경고/오류 0, xUnit 318개 통과. 새로운 검사에는 원본 커널 채널 방향/밝기 보존, premultiplied 알파, 그레인 분할 계산의 문서 좌표 일치, 시드 차이, 마스크·중립값, UInt32 최대값 저장 왕복과 합성 일치가 포함된다.

실제 WPF 창에서 두 종류의 생성/편집/취소/비교/Apply/Undo/Redo/no-op, 범위 초과 시드 거부를 검증했다. 창 렌더 이미지에서 체크박스 대비와 그레인 하단 버튼 잘림을 수정했다. 설정 영역은 스크롤하며 적용/취소는 고정한다.

재현:
- ./build.ps1 -Test
- dotnet run --project Compositor.Benchmarks -c Release -- --color-grain artifacts/color-grain-benchmark.json
- ./build.ps1 -Publish -PublishDirectory artifacts/publish-color-grain
- ./verify-portable.ps1 -Runs 1 -PackageDirectory (Join-Path $PWD artifacts/publish-color-grain)

## 성능 잔여

4000×4000, 알파160 원본 1개와 조정 1개, 1000px CPU 뷰포트의 1회 측정: 색상 균형 최초 3931ms/캐시 이동 3.2ms, 그레인 최초 574ms/캐시 이동 2.6ms. 프로세스 working set 약 418/480MiB. WPF 표시와 180ms 입력 대기 시간은 제외한다.

색상 균형의 전체 재합성은 실시간 편집에 느리며 성능 완료로 판정하지 않는다. 원본 C 커널의 반복 계산과 동기 전체 문서 갱신을 최적화해야 한다. 비동기/증분 합성도 품질·성능 잔여다.

추가 조정 잔여는 Hue/Saturation, Gradient Map이며 원본 픽셀 필터 경로는 별도다.