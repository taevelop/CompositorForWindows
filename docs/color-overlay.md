# 레이어 색상 오버레이

2026-10-06 구현. **Effects → Color overlay…** 또는 레이어 속성의 **Color overlay…**로 일반 픽셀 레이어에 적용한다. 새 레이어를 만들지 않고 기존 레이어에 효과 설정을 추가한다. 그룹과 조정 레이어에는 아직 효과를 적용하지 않는다.

## 편집과 저장

- RGB와 효과 불투명도는 0~1 유한 수다. 색 견본, 활성화, 미리보기 비교, 흰색 초기화, 효과 제거를 지원한다. UI의 새 효과 기본색은 흰색이다.
- 180ms 입력 대기 후 미리보기를 갱신하며 Apply는 대기 중인 입력도 즉시 반영한다. 잘못된 값은 Apply를 막고 창을 유지한다.
- Apply/Remove 한 번에 Undo 한 항목이다. Cancel은 문서·선택·Redo를 보존하며, 같은 값을 다시 적용해도 Undo를 추가하지 않는다. 활성화 플래그가 원래 없었으면 단순 재편집 시 계속 생략한다.
- 원본 이미지와 마스크 타일은 변경하지 않는다. 효과가 있는 레이어에도 이미지/마스크 브러시를 사용할 수 있다. 원본 편집 시 효과 결과를 다시 계산한다.
- `.comp`의 `effects.colorOverlay`에 `red`, `green`, `blue`, `opacity`와 선택적인 `enabled`를 저장한다. enabled 없음/null은 true 의미이며 false인 효과의 색과 불투명도도 보존한다.
- 기존 버전 1~8 읽기/버전 8 저장 정책을 유지한다. Mac 저장 코드가 효과에 별도 최소 버전을 요구하지 않아 효과만을 이유로 하위 버전을 거부하지 않는다. 마스크·그룹·조정 등의 기존 버전 조건은 그대로 적용한다.
- 일반 픽셀 레이어의 effects 없음/null/빈 객체를 지원한다. 다른 알려진 효과의 null은 허용하지만 객체가 있으면 비활성 상태여도 거부한다. 알 수 없는 효과·필드, 중복 필드, 누락/잘못된 값, 그룹·조정 레이어 효과는 열기와 덮어쓰기를 거부한다.

## 합성과 캐시

Mac 기준 소스는 `macOS/Compositor/Document/LayerEffects.swift`의 ColorOverlayEffect/render, `macOS/Compositor/Rendering/MetalLayerEffects.swift`의 compose 연산이다.

**마스크 → 색상 오버레이 → 레이어 변환·불투명도·합성 모드 → 위의 조정 레이어** 순서다. 마스크 적용 후 원본 알파를 a, 효과 불투명도를 o, 효과색을 C, premultiplied 원본색을 P라고 하면 다음과 같다(모두 0~1).

```
coverage = a * o
P' = C * coverage + P * (1 - coverage)
a' = coverage + a * (1 - coverage)
```

원본 알파가 0이면 투명하고, 반투명 픽셀은 효과 적용 후 더 불투명해질 수 있다. 이는 알파를 보존하는 Exposure/Levels/Curves 조정과 다른 Mac 효과 합성 동작이다. 예를 들어 RGBA (64,32,16,128)에 빨강/불투명도 0.5를 적용하면 (112,24,12,160)이 된다.

화면과 내보내기는 같은 타일 합성기를 사용한다. 마스크를 적용한 258×258 타일(주변 픽셀 포함)에 효과를 계산하므로 타일 경계에서 샘플링 이음새를 방지한다. 효과 값도 타일 캐시 키에 포함하고 효과 변경 시 뷰포트를 갱신한다. 브러시 변경은 기존 손상 영역/인접 타일 방식으로 처리한다. 임의 회전이나 조정 레이어가 있는 경우 기존 전체 갱신 규칙을 따른다.

색상·알파 조회표는 설정별로 재사용하며 렌더러당 최대 8개(약 1.5MiB)로 제한한다. 타일마다 수백만 번 실수 연산을 반복하던 초기 방식보다 편집/브러시 비용을 줄였다. GPU 가속 구현은 아니다.

## 검증

Release 빌드 경고/오류 0, 전체 xUnit **260개 통과**.

- 모든 알파/채널 조합을 독립 decimal 수식과 비교(부동소수점 반올림 차이 최대 1byte), 투명 픽셀·premultiplied 범위와 알려진 계산값 검증.
- 마스크가 효과보다 먼저 적용되는지, 효과 뒤의 레이어/부모 불투명도·7개 합성 모드·Curves가 올바른 결과를 사용하는지 검증.
- 타일 경계, 변환·회전, 100/150/200% 뷰포트의 신선한 렌더 결과 비교, 설정 변경·활성화·제거·브러시·마스크·Undo/Redo 캐시 갱신 검증.
- 원본과 효과값의 저장/재열기, optional enabled와 빈 effects, Mac 형태의 효과 메타데이터, 저장 실패 시 원본 보존, 미지원 효과 및 잘못된 데이터 거부 검증.
- 숨김 WPF 창에서 생성/취소/비교/잘못된 입력/재편집/활성화/제거/Undo/Redo, 라이브 브러시, UI 저장/재열기와 PNG 일치 검증.

실제 Mac 앱 파일 왕복·Apple/Skia 변환 가장자리의 완전한 픽셀 동등성·물리 입력·다중 모니터 DPI는 미검증이다. RGB 숫자 입력 방식이며 색상 선택기·스포이트는 포함하지 않는다.

## 성능

2026-10-06 10:06 KST, Windows x64, 12 logical processors. 4000×4000 반투명 원본 한 레이어와 색상 오버레이, 1000×1000 CPU 뷰포트에서 설정 변경 3회 및 지름 800픽셀 브러시 입력 10회를 측정했다.

| 조건 | 최초 합성 | 효과 변경+표시 | 캐시된 이동 | 브러시 입력+표시 | 스트로크 Commit |
|---|---:|---:|---:|---:|---:|
| 마스크 없음 | 128ms | 78~99ms | 12~13ms | 12~26ms | 0.30ms |
| 1×1 회색 마스크 | 130ms | 109~119ms | 11~13ms | 15~23ms | 0.15ms |

효과 변경+이동의 회당 관리 메모리 할당은 약 0.38/0.41MiB, working set은 각각 약 298~300/361~362MiB였다. 설정 변경의 독점 Undo 타일 메모리는 0, 브러시 스트로크는 약 9.25MiB였다. 초기 실수 연산 방식의 효과 변경은 약 184~223/236~243ms였다.

180ms 입력 대기와 WPF 표시·모니터 지연은 제외한다. 10회 입력 샘플의 범위이며 장시간 p95나 다층·비균일 마스크·100MP 문서 성능을 보장하지 않는다. 전체 레이어의 효과 변경은 여전히 CPU 비용이 들고 UI 지연이 가능하다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test
dotnet run --project Compositor.Benchmarks/Compositor.Benchmarks.csproj -c Release --no-build -- --overlay artifacts/overlay-benchmark.json
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Publish
```

결과: `artifacts/ui-smoke.overlay.json`, `artifacts/ui-smoke.overlay.dialog.png`, `artifacts/overlay-benchmark.json`. 배포본 검증: `artifacts/published-overlay-smoke.overlay.json`.

다음 단계는 **Drop Shadow(그림자) 효과**다. 원본 밖으로 확장되는 효과 영역, 흐림, 마스크 적용 순서와 타일 경계 처리를 검증한 뒤 외곽선 등으로 확대한다.
