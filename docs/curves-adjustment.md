# Curves 비파괴 조정 레이어

2026-09-29 구현. Image → New Curves adjustment layer…에서 만들고 Edit Curves…로 다시 편집한다. 선택한 픽셀 레이어 위 또는 선택한 그룹 안에 추가하며, 아래에 보이는 합성 결과에 적용한다. 원본 픽셀과 위 레이어는 보존한다.

## 편집

- RGB/Red/Green/Blue마다 2~32개의 점을 가진다. 그래프 클릭으로 추가·선택, 드래그로 이동한다. Input/Output 숫자로도 편집할 수 있다.
- 양 끝점의 입력은 0/255로 고정하고 출력은 바꿀 수 있다. 내부 점을 삭제할 수 있고 입력 순서를 뒤집거나 중복시킬 수 없다. Add midpoint는 선택한 점 오른쪽 구간에 점을 추가한다(마지막 점 선택 시 왼쪽 구간).
- 채널 전환/점 선택 직전 입력을 반영한다. 잘못된 입력은 전환과 Apply를 막고 필드에 남긴다. Reset channel/Reset all, 원본 비교, 180ms 입력 대기 후 미리보기를 지원한다.
- 그래프 드래그 중 Esc나 마우스 캡처 상실은 해당 드래그를 되돌린다. 창 Cancel/Esc는 전체 편집을 되돌린다. 새 조정 취소는 레이어 생성·선택도 되돌린다.
- Apply 한 번에 Undo 한 항목이다. 동일한 점을 다시 적용하면 Undo가 생기지 않으며, 취소하면 기존 Redo를 보존한다. 설정은 값 동등성이 있는 불변 점 목록으로 보관한다.
- 7개 합성 모드, 그룹의 표시·불투명도, 연결 마스크 및 마스크 페인팅을 지원한다. 조정 레이어에 원본 픽셀을 직접 칠할 수는 없다. 레이어 변환은 마스크 범위에 적용한다.

## 계산과 파일

기준은 `macOS/Compositor/Document/Curves.swift`다. 구간 양 끝에서는 해당 구간 기울기를 사용하고, 내부 점에서는 양쪽 기울기의 부호가 다르거나 0이면 접선 기울기를 0으로 만든다. 같은 부호이면 조화평균 `2 / (1/left + 1/right)`를 사용한다. cubic Hermite 보간 후 0~255로 제한한다. 개별 색 채널을 먼저 계산하고 RGB 곡선을 나중에 적용한다.

세 채널의 256개 float 테이블을 원본 `levels_apply` C 커널에 전달한다. Exposure·Levels와 같은 채널별 알파/색상 조회표를 재사용해 premultiplied alpha를 보존한다. sRGB 인코딩 값에서 계산한다.

`.comp` 버전 7~8의 `adjustment.kind: "Curves"`를 읽고 버전 8로 저장한다. `curves.channel`은 RGB/Red/Green/Blue 중 하나이며, `curves.channels`는 그 순서의 정확히 네 점 목록이다. 점의 `x`, `y`는 0~255 유한 수이고 x는 엄격히 증가해야 한다. 첫/마지막 x는 0/255다. 안전한 부동소수점 보간이 불가능한 극단적으로 가까운 입력도 거부한다.

imageFile 없이 점·선택 채널을 기록하며 마스크 PNG는 기존 방식을 따른다. 비활성 Levels/HSV 등 설정이 기본값이 아니거나 미지원 필드·중복 필드가 있으면 열기와 덮어쓰기를 거부한다. 비활성 Exposure는 없음/null 또는 중립값만 허용한다. 실제 Mac 앱 왕복은 미검증이며, 로컬 Swift 소스와 독립 fixture를 기준으로 확인했다. 히스토그램·자동 곡선·스포이트·프리셋은 포함하지 않는다.

## 검증과 성능

Release 빌드 경고/오류 0, 전체 xUnit **226개 통과**. 수작업으로 계산한 Hermite 기준값, 평탄/극대/역전 곡선, 끝점, 잘못된 점 수·범위·순서·극단적 간격, 불변성과 값 동등성, 65,536개 알파/채널 조합의 C 커널과 최적화 결과를 검증했다. Exposure·Levels와 혼합 순서, 7개 합성 모드·마스크·그룹, 100/150/200% 화면과 출력, 저장 실패 원본 보존, 독립 Swift 형식 fixture를 포함한다.

숨김 WPF 창 검증은 그래프 추가/이동/취소/삭제, 끝점 이동 제한, 가까운 점 사이의 midpoint 추가, 숫자 입력/채널 전환, 현재/전체 초기화, 미리보기/취소/Undo/Redo, 마스크, 저장 후 재편집과 PNG 출력을 포함한다. 물리 마우스/키보드와 다중 모니터 DPI는 미검증이다.

2026-09-29 14:29 UTC, Windows x64, 12 logical processors에서 4000×4000 반투명 원본 1개와 Curves 1개, 1000×1000 CPU 뷰포트, 설정 변경 3회씩 측정했다.

| 조건 | 최초 합성 | 설정 변경+화면 | 캐시된 이동 | Working set |
|---|---:|---:|---:|---:|
| 마스크 없음 | 209ms | 112~124ms | 2.8~2.9ms | 358~360MiB |
| 전체 문서 1×1 회색 마스크 | 206ms | 161~173ms | 2.8~3.8ms | 362~364MiB |

회당 관리 메모리 할당 약 0.53MiB, 설정 Undo의 독점 타일 메모리 0이다. 180ms 입력 대기와 WPF 표시 시간은 제외한다. 기존 조정과 같이 전체 문서 합성을 캐시하며 설정·원본·마스크 변경 시 다시 합성한다. UI에서 CPU 합성을 수행하므로 대형 문서에서 지연될 수 있다. 다중 조정·비균일 마스크·연속 브러시 성능은 이 측정으로 보장하지 않는다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test
dotnet run --project Compositor.Benchmarks/Compositor.Benchmarks.csproj -c Release --no-build -- --curves artifacts/curves-benchmark.json
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Publish
```

검증 출력은 `artifacts/ui-smoke.curves.json`, 창 캡처는 `artifacts/ui-smoke.curves.dialog.png`, 배포본 검증은 `artifacts/published-curves-smoke.curves.json`이다.

2026-10-06에 [색상 오버레이 효과](color-overlay.md)를 추가했다. 다음 단계는 그림자 효과이며, 이후 외곽선 등으로 확대한다.
