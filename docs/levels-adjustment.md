# Levels 비파괴 조정 레이어

2026-09-29 구현. **Image → New Levels adjustment layer…**, 선택 후 **Edit Levels…**에서 편집한다. RGB/Red/Green/Blue의 입력 검정·감마·입력 흰색·출력 검정·출력 흰색을 저장하며, 아래에 보이는 합성 결과에 적용한다. 위 레이어와 원본 이미지 픽셀은 보존한다.

## 편집과 계산

- 채널 전환 직전 입력을 반영하고 네 채널의 값을 유지한다. 잘못된 범위는 전환·적용을 막고 입력값을 그대로 남긴다.
- 180ms 입력 대기 후 미리보기, 원본 비교, 현재 채널/전체 초기화, Apply 시 한 번의 Undo를 지원한다. 새 창 취소는 레이어 생성도 취소하고 선택과 Redo를 복원한다. 값이 같은 재적용은 Undo를 추가하지 않는다.
- 입력 검정은 0~254, 흰색은 검정+1~255, 감마는 0.1~9.99, 출력 두 값은 각각 0~255다. 출력 검정이 흰색보다 높으면 색조를 반전한다. 유한한 수만 허용한다.
- `macOS/Compositor/Document/Levels.swift`의 LevelRange/LevelsSettings와 같은 계산이다. 개별 Red/Green/Blue 범위를 먼저 적용한 다음 공통 RGB 범위를 적용한다. sRGB 인코딩 값에 작용하며 Exposure의 선형광 계산과 다르다.
- 각 범위는 `input = clamp((value*255-black)/(white-black), 0, 1)`, `output = (outputBlack + pow(input, 1/gamma)*(outputWhite-outputBlack))/255`다.
- 원본 `levels_apply` C 커널로 알파/채널 바이트 조합별 결과를 계산한다. 서로 다른 색 채널에 별도 조회표를 사용하며 premultiplied alpha를 보존한다.
- 기존 7개 합성 모드, 그룹의 표시/불투명도, 연결 마스크를 지원한다. 조정 레이어에는 원본 픽셀을 그릴 수 없고 마스크만 그릴 수 있다. 변환은 마스크 범위에 적용한다.

## 저장과 호환성

`.comp` 버전 7~8의 `adjustment.kind: "Levels"`를 읽고 버전 8로 저장한다. `levels.channel`은 RGB/Red/Green/Blue 중 하나이며 편집기 선택 상태다. `levels.ranges`는 RGB/Red/Green/Blue 순서의 정확히 네 객체이며 각 객체에 `black`, `gamma`, `white`, `outputBlack`, `outputWhite`가 필요하다.

Swift Codable의 비활성 필수 필드는 기본값으로 기록한다. 비활성 `exposureSettings`는 없음/null/중립값(0/0/1)만 허용한다. 지원하지 않는 설정·필드, 중복 필드, 손상된 범위는 열기 및 덮어쓰기를 거부한다. Levels는 imageFile을 갖지 않으며 설정과 선택 채널은 저장 후에도 편집 가능하다. 마스크 PNG와 좌표는 기존 방식을 유지한다.

실제 Mac 앱 왕복은 미검증이다. 로컬 Swift 소스, 독립적으로 작성한 Swift 키 형식 fixture, 원본 C 커널 및 Windows 저장/열기 검증을 기준으로 한다. 히스토그램·자동 레벨·스포이트와 Mac의 모든 편집 UI를 포함하지 않는다.

## 검증과 성능

Release 빌드 경고/오류 0, 전체 xUnit **189개 통과**. 서로 다른 채널 값으로 65,536개 알파/채널 바이트 조합에서 C 커널과 최적화 결과를 비교했다. 채널 계산 순서·역출력·알파 보존, Exposure와 혼합 순서, 그룹/마스크/합성 모드, Undo 불변성, 100/150/200% 화면과 출력, 저장 실패 시 원본 보존과 미지원 데이터 거부를 검증했다.

숨김 WPF 컨트롤 검증은 생성·취소·재편집·채널 전환·잘못된 입력·즉시 Apply·원본 비교·Undo/Redo·마스크 페인팅·저장/재열기·PNG 출력·그룹 안 생성을 포함한다. 실제 마우스/키보드와 다중 모니터 DPI는 미검증이다.

2026-09-29 14:10 UTC, Windows x64, 12 logical processors에서 4000×4000 반투명 원본 1개와 Levels 1개, 1000×1000 Skia CPU 뷰포트로 설정 변경 3회씩 측정했다.

| 조건 | 최초 합성 | 설정 변경+화면 | 캐시된 이동 | Working set |
|---|---:|---:|---:|---:|
| 마스크 없음 | 201ms | 113~123ms | 2.6~3.0ms | 359~361MiB |
| 전체 문서 1×1 회색 마스크 | 233ms | 161~167ms | 2.7~3.1ms | 363~364MiB |

회당 관리 메모리 할당은 약 0.53MiB, 설정 Undo의 독점 타일 메모리는 0이다. native bitmap 메모리는 working set에 포함된다. 입력 대기 180ms와 WPF 표시 시간은 제외한다. 전체 문서 해상도로 합성한 이미지를 캐시하는 CPU 구현이며, 조정/원본/마스크 변경 시 전체를 다시 합성한다. 대형 문서에서 UI 지연이 가능하고 여러 조정·비균일 마스크·연속 브러시 성능은 이 수치로 보장하지 않는다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test
dotnet run --project Compositor.Benchmarks/Compositor.Benchmarks.csproj -c Release --no-build -- --levels artifacts/levels-benchmark.json
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Publish
```

결과: `artifacts/ui-smoke.levels.json`, `artifacts/ui-smoke.levels.dialog.png`, `artifacts/levels-benchmark.json`. self-contained 배포본 검증은 `artifacts/published-levels-smoke.levels.json`에 기록한다.

다음 기능은 **Curves 비파괴 조정 레이어**다. 채널별 곡선 편집·Mac 곡선 보간·파일 호환성을 구현하고 현재 조정 레이어의 마스크/Undo/합성 검증을 확장한다.
