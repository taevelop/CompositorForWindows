# Stroke (외곽선)

2026-10-06. Effects → Stroke… 또는 Selected layer의 Stroke…에서 편집한다.

- 크기 0–500px, 불투명도 슬라이더·정밀 입력, Inside edge 선택, 색상표, 활성화, 비교, 초기화, 제거·적용·취소.
- 마스크 적용 후 알파를 원본처럼 사각 범위로 확장/축소한다. 양수 크기는 정수 반올림(최소 1)이며 크기 0은 표시하지 않는다.
- 바깥쪽 = 확장 알파 - 원본 알파, 안쪽 = 원본 알파 - 축소 알파. 영상 밖 알파는 0이다. 두 방향 sliding-window min/max를 사용해 계산량이 반경에 비례하지 않는다.
- 합성 순서: 그림자 → 바깥 외곽선 → 원본/색상 오버레이 → 안쪽 외곽선 → 레이어 불투명도/합성 모드.
- effects.stroke의 size/red/green/blue/opacity/inside 및 optional enabled를 보존한다. enabled 없음/null은 true이며 비활성 효과의 설정도 저장한다. 원본 픽셀은 변경하지 않는다.
- 픽셀 레이어 지원. 그룹/조정 레이어 효과는 여전히 거부한다. Inner Shadow/Outer Glow는 다음 작업이다.
- 효과를 변경/제거해도 다른 효과를 유지한다. Apply/Remove 한 번은 Undo 한 항목, 취소/no-op는 이력을 보존한다.

## 검증

Release 빌드, xUnit 및 WPF 검사에 포함한다. 독립 사각 범위 순회 수식과 무작위 알파 비교(반경 1/4/500, 안쪽/바깥쪽), 링 위치·불투명도·마스크·회전·캐시 변경·원본 보존·세 효과 저장 왕복·Undo/Redo를 검증한다. UI는 미리보기·비교·취소·적용·no-op·잘못된 입력·제거 및 오버레이 공존을 검증하고 실제 창을 렌더링한다.

재현:
- ./build.ps1 -Test
- dotnet run --project Compositor.Benchmarks -c Release -- --stroke artifacts/stroke-benchmark.json
- ./build.ps1 -Publish -PublishDirectory artifacts/publish-stroke
- ./verify-portable.ps1 -Runs 1 -PackageDirectory (Join-Path $PWD artifacts/publish-stroke)

## 성능과 잔여

4000×4000 소스, 1000px CPU 뷰포트에서 두께 4/100/500px 각각 1회 측정. 최초 합성 583/454/615ms, 캐시된 이동 24/22/21ms. 워밍업·시스템 부하가 포함되어 두께별 속도의 직접 비교 지표는 아니다. WPF 표시·180ms 입력 대기는 제외한다.

큰 문서에서는 효과 값 또는 원본/마스크 변경 후 전체 링을 재계산하므로 입력이 지연될 수 있다. GPU 또는 비동기/증분 갱신은 전체 전환의 성능 잔여 항목이다. 마지막 한 레이어만 캐시하지만 확장 알파·이미지 크기에 비례하는 메모리가 필요하다. 실제 Mac 왕복은 사용자 지시로 보류하며 완전한 픽셀 동등성을 주장하지 않는다.
