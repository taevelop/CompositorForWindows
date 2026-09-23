# Windows 기본 색상 조정

2026-09-23. 그룹·마스크 구현 위에 선택 이미지의 밝기·대비·채도 편집을 추가했다. macOS 원본 소스는 수정하지 않았다.

## 사용법과 범위

1. 이미지 레이어를 선택한다. 마스크가 있다면 **Edit image**를 선택한다.
2. **Image → Adjust colors…** 또는 속성 패널의 **Adjust image colors…**를 연다.
3. Brightness·Contrast·Saturation을 −100~100 범위에서 조정한다. 0은 변화 없음이며 슬라이더에 포커스를 두고 방향키로 1씩 조절할 수 있다.
4. **Preview changes**를 끄면 시작 시점의 원본과 비교한다. **Reset**은 세 값을 0으로 되돌린다.
5. **Apply**는 한 번의 Undo로 적용한다. 원본 비교가 켜져 있어도 적용 대상은 설정한 조정 결과다. **Cancel/Esc/창 닫기**는 변경을 취소한다.

조정 창을 여는 동안 다른 편집을 차단한다. 슬라이더는 입력이 180ms 멈추면 원본에서 새 결과를 계산한다. 계산 중 Apply는 비활성화하며, 이전 요청은 취소하고 최신 결과만 반영한다. Reset은 기다리지 않고 계산한다. 창을 닫으면 늦게 끝난 작업도 문서를 변경하지 않는다.

이 기능은 **이미지 픽셀을 변경하는 편집**이다. 별도 비파괴 조정 레이어가 아니며, 아래 레이어 합성 결과에 필터를 적용하지 않는다. 마스크에 가려진 부분을 포함한 선택 이미지 전체를 조정한다. 소스의 크기·알파·변환·부모·합성·불투명도·연결 마스크는 보존한다. 그룹과 마스크 편집 대상에서는 조정 버튼을 사용할 수 없다.

현재 세션에서는 Undo로 시작 이미지와 수정 상태를 복원한다. 프로젝트 저장 시 조정된 픽셀을 기존 PNG에 쓰며, 새 JSON 필드를 추가하지 않는다. **재열기 후에는 조정 전 원본이나 슬라이더 값을 복원하지 않는다.** 취소·Reset 후 변화 없는 적용은 Undo를 만들지 않고 Redo를 유지한다.

전체 이미지 변경도 기존 이력 한도(100단계·현재 문서 외 고유 타일 256MiB)를 따른다. 이번 변경 하나의 Undo에 필요한 고유 타일이 256MiB를 넘으면 결과 적용을 차단하고 더 작은 소스 이미지를 사용하도록 안내한다. 현재 문서에서 공유 중인 타일은 중복 계산하지 않는다. 이후 편집으로 이력 한도를 초과하면 오래된 Undo는 기존 정책에 따라 제거된다.

## 계산 정의

입력과 출력은 premultiplied RGBA8/sRGB다. 알파가 0인 픽셀과 누락된 투명 타일은 그대로 유지한다. 각 비투명 픽셀은 RGB를 알파로 나눠 0~1의 비결합 sRGB 색상으로 계산한다.

1. 회색 기준 `L = 0.2126 R + 0.7152 G + 0.0722 B`.
2. 채도 `S = 1 + saturation / 100`, 각 채널 `Cs = L + (C - L) × S`.
3. 대비 계수 `K = 1 + contrast / 100`(대비 ≤0), `K = 1 + contrast / 25`(대비 >0).
4. 최종 채널 `clamp((Cs - 0.5) × K + 0.5 + brightness / 100, 0, 1)`.
5. 원래 알파를 곱하고 가장 가까운 바이트로 반올림(정확히 중간이면 0에서 먼 값). 알파 바이트 자체는 변경하지 않는다.

채도 −100은 회색, 대비 −100은 중간 회색, 밝기 −100/+100은 각각 검정/흰색이다. 세 설정은 위 순서로 함께 계산하므로 복합 설정에서의 끝값은 단독 설정과 다를 수 있다. 선형광 노출이나 Mac Hue/Saturation·Exposure 조정과 같은 수식을 주장하지 않는다. 각 미리보기는 시작 이미지에서 계산하므로 이전 미리보기의 양자화 오차가 누적되지 않는다.

계산은 백그라운드 CPU 작업이며 각 타일 행에서 취소를 확인한다. 바뀐 256×256 타일만 복사하고 변화 없는 타일은 공유한다. 합성·출력은 기존 공통 렌더러를 사용한다. GPU 가속이나 색 관리 모니터의 실제 표시 일치를 검증한 것은 아니다.

## 검증

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test
dotnet run --project Compositor.Benchmarks/Compositor.Benchmarks.csproj -c Release --no-build -- --adjustments artifacts/adjustment-benchmark.json
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Publish
```

- Release 빌드: 경고 0개·오류 0개. 기존 안정화·마스크·그룹 검증을 포함한 xUnit **119개 통과**.
- 유한 값/범위 검사, 단독 조정 끝값, 모든 알파 값의 보존, premultiplied 범위, 완전 투명 픽셀, 타일 경계와 부분 타일의 패딩.
- 원본 불변성·변화 없는 타일 공유·누락 타일 보존, 취소, Undo 보존량의 공유 타일 제외.
- 미리보기 취소·Reset no-op·Redo 유지, 마스크·변환·그룹·합성 보존, 한 단계 Undo/Redo.
- 100%/150%/200% 배율의 증분/전체 합성 비교, 회전·그룹·마스크가 있는 조정 이미지의 저장/재열기·PNG와 합성 일치, 별도 adjustment 필드가 저장되지 않음.
- 숨김 WPF 실제 모달 창: 빠른 값 변경의 최신 결과, 원본 비교, 취소·닫기·계산 중 닫기, Reset 후 Apply, 미리보기 OFF 상태의 Apply, Undo/Redo, UI 저장/재열기와 PNG 출력.

결과는 `artifacts/ui-smoke.adjustments.json`, 조정 창은 `artifacts/ui-smoke.adjustments.dialog.png`, 전체 화면은 `artifacts/ui-smoke.png`에 남는다. self-contained 실행 파일 검증은 `artifacts/published-adjustment-smoke.adjustments.json`과 같은 이름의 `.dialog.png`에 기록한다. 물리 슬라이더 드래그·다중 모니터 DPI·실제 Mac 앱 열기는 별도 실기 검증 대상이다.

## 4K 성능

2026-09-23 01:56 UTC, Intel Core 5 210H(12 logical processors), Windows x64, Release. 4000×4000 반투명 색상 패턴과 균일 마스크, 세 조정 동시 적용, 1000×1000 Skia 뷰포트. 같은 원본에서 설정을 바꿔 5회 측정했다.

| 항목 | 측정 범위 |
|---|---:|
| 픽셀 계산 | 170.5~195.8ms |
| Skia 화면 갱신 | 81.0~141.9ms |
| Commit | 0.13~2.16ms |
| 계산+화면+Commit | 251.7~335.5ms |
| 회당 관리 메모리 할당 | 약 64.5MiB |
| 작업 중 working set | 약 368~445MiB |
| 해당 변경의 Undo 타일 | 64MiB |

입력 대기 180ms, WPF dispatcher·실제 모니터 표시는 포함하지 않는다. 브러시의 33.3ms 갱신 기준을 충족하는 기능이라는 뜻이 아니며, 큰 이미지에서 슬라이더를 움직인 뒤 결과가 표시되기까지 잠시 기다릴 수 있다. 최대 100MP와 여러 대형 레이어의 실제 UI 지연은 별도 측정이 필요하다.

## 다음 작업

다음은 **Mac 호환 비파괴 Exposure 조정 레이어**다. Mac의 `adjustment` JSON, 아래 레이어 합성에 대한 적용 범위, 그룹·마스크·알파·불투명도 의미를 먼저 맞추고 저장 왕복·렌더링 검증을 추가한다. 이번 기본 조정만으로 Mac의 조정 레이어를 지원하는 것은 아니므로 해당 문서는 계속 열기를 거부한다. 효과와 제한적 PSD 가져오기는 그 이후 범위다.
