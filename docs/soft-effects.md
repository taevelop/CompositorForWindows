# Inner Shadow / Outer Glow

2026-10-06. Effects 메뉴와 Selected layer에서 픽셀 레이어의 안쪽 그림자 및 외부 광선을 편집한다.

## 동작

- Inner shadow: 각도·거리·흐림·불투명도 슬라이더/숫자, 색상표. 원본 기본값은 90°/10px/10px/검정/50%.
- Outer glow: 크기·불투명도 슬라이더/숫자, 색상표. 원본 기본값은 20px/흰색/75%.
- 활성화·비교·초기화·제거·적용·취소와 Undo/Redo를 지원한다. RGB 정밀도와 optional enabled 값을 재편집 시 유지한다.
- 원본 알파 A에 대해 안쪽 그림자는 A*(1-shiftedBlur(A)), 외부 광선은 blur(A)*(1-A)다. 마스크를 먼저 적용하고 흐림 sigma는 blur/2다.
- 그림자 → 외부 광선 → 바깥 외곽선 → 원본/색상 오버레이 → 안쪽 그림자 → 안쪽 외곽선 순으로 합성한다. 레이어 불투명도·합성 모드는 합성한 결과에 한 번 적용한다.
- effects.innerShadow 및 effects.outerGlow의 원본 JSON 키를 보존한다. 그룹/조정 레이어 효과는 계속 거부한다. 마스크·원본 픽셀은 효과 편집으로 변경되지 않는다.
- 외부 광선은 크기의 3배까지 소스 영역을 확장한다. 문서 경계에서 자르며 반투명 원본에서는 내부에도 일부 광선이 나타나는 원본 수식을 따른다.
- 마지막 한 레이어의 효과 이미지와 소스를 캐시하며 설정·픽셀·마스크 변화와 문서 전환에서 갱신한다.

## 검증

xUnit 295개 및 확장 WPF 검사 통과. 65,536개 알파 조합의 독립 decimal 수식 비교, 안쪽 위치·광선 확장, 비활성 동등성, 마스크, 회전, 캐시/신선한 출력 일치, 다섯 효과의 저장·optional enabled 보존을 검사한다. UI는 미리보기·비교·적용/취소·no-op·잘못된 값·제거·오버레이 공존을 검사하며 두 설정 창을 렌더링했다.

명령:
- ./build.ps1 -Test
- dotnet run --project Compositor.Benchmarks -c Release -- --soft-effects artifacts/soft-effects-benchmark.json
- ./build.ps1 -Publish -PublishDirectory artifacts/publish-effects
- ./verify-portable.ps1 -Runs 1 -PackageDirectory (Join-Path $PWD artifacts/publish-effects)

## 성능 잔여

4000×4000 소스, 1000px CPU 뷰포트, 흐림20px 1회 측정: 안쪽 그림자 최초713ms/캐시 이동21ms, 외부 광선 최초556ms/캐시 이동19ms. WPF 표시와180ms 입력 대기 제외. 소스/설정 변경 시 다시 계산하므로 큰 문서에서 편집 지연이 있으며 비동기·증분 처리 개선 대상으로 유지한다. Mac CoreImage와 Skia 흐림의 완전한 픽셀 동일성은 주장하지 않는다. 실제 Mac 왕복은 사용자 지시에 따라 보류한다.
