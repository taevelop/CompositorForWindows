# 비파괴 Exposure 조정 레이어

2026-09-28. Mac Exposure의 저장 키·수식·아래 합성에 대한 적용 방식을 Windows로 옮겼다. macOS 원본 파일은 변경하지 않았다. 실제 Mac 앱과 전체 픽셀 동등성 또는 양방향 실행 검증을 마쳤다는 뜻은 아니다.

## 사용법

- **Image → New Exposure adjustment layer…**: 선택한 일반 레이어의 바로 위, 같은 부모 안에 추가한다. 그룹 선택 시 그 그룹의 첫 자식으로 추가한다. 선택이 없으면 최상단이다.
- Exposure(−20~20 stops), Offset(−0.5~0.5, linear light), Gamma(0.01~9.99)를 입력한다. 입력은 소수점 `.`을 사용하며 유한 수만 허용한다.
- 값 입력 후 180ms 대기하여 미리보기를 갱신한다. **Preview changes**를 끄면 편집 시작 전 문서와 비교한다. **Reset**은 0/0/1이다.
- **Apply**는 한 단계 Undo로 저장한다. 원본 비교 중에도 입력한 설정을 적용한다. **Cancel/Esc/창 닫기**는 시작 상태로 되돌리며, 새 레이어를 추가 중이었다면 생성도 취소한다.
- 레이어 선택 후 **Edit Exposure…**로 다시 편집한다. 저장 후 열어도 설정이 남는다.
- 이름·표시·불투명도·합성 모드·순서·부모·삭제는 기존 레이어 편집을 사용한다. 마스크 추가 후 **Edit mask**에서 검정은 숨김, 흰색은 표시다. 조정 레이어의 이미지에 직접 브러시/지우개 또는 기본 색상 조정을 적용하지는 못한다.

이전 **Adjust image colors…**는 이미지 픽셀을 변경하는 기능이고, Exposure는 이미지와 별도로 저장되는 조정 레이어다.

## 합성 의미와 구현

렌더 순서상 아래에 보이는 모든 픽셀의 합성 결과에 적용한다. 위 레이어에는 영향을 주지 않으며 아래 이미지나 브러시가 바뀌면 결과가 다시 계산된다. 그룹은 pass-through이므로 조정의 범위를 같은 그룹 내부로 제한하지 않는다. 그룹의 표시와 불투명도는 조정 레이어에도 상속한다.

Mac [ExposureSettings](../macOS/Compositor/Document/ImageAdjustments.swift), [LayerAdjustment](../macOS/Compositor/Document/LayerAdjustment.swift), [LiveMaskRenderer](../macOS/Compositor/Rendering/LiveMaskRenderer.swift)를 기준으로 한다.

1. sRGB 채널을 선형광으로 디코딩한다.
2. `pow(max(0, linear × pow(2, exposure) + offset), 1 / gamma)`를 계산한다.
3. sRGB로 다시 인코딩하고 0~1로 제한해 256개 float lookup table을 만든다.
4. Mac에서 복사한 [levels_apply C 커널](../native/kernels/LevelsPixels.c)의 비결합 채널 보간과 반올림을 사용한다. 고정 크기 ABI인 `compositor_levels`를 통해 호출한다.
5. Normal/Multiply/Screen/Overlay/Darken/Lighten/Difference로 색상을 합성한 뒤 유효 불투명도와 마스크 커버리지로 원래 색상과 보간한다. **원래 알파는 변경하지 않는다.**

성능을 위해 가능한 256개 알파 × 256개 채널 바이트의 C 커널 결과를 미리 계산한다. 원본 커널을 모든 이미지 픽셀에 다시 실행하는 것과 동일한 byte lookup을 사용한다. 원본 C 코드를 수정하거나 Swift/Apple API에 의존하지 않는다.

마스크가 활성화되면 그 픽셀을 레이어 변환으로 문서 좌표에 렌더해 커버리지로 사용한다. 크기·위치·회전·뒤집기는 마스크에만 영향을 준다. 마스크가 없거나 비활성화되면 변환과 관계없이 아래 합성 전체를 조정한다. 기본 전체 문서 범위의 1×1 균일 마스크는 상수 커버리지로 처리한다. 그룹 마스크·참조/클리핑 마스크·분리 배치는 기존과 같이 거부한다.

## 저장과 호환 범위

`.comp` 버전 7–8의 Exposure를 읽고 8로 저장한다. 별도 `imageFile`은 없고 `adjustment.kind`, `exposureSettings`와 선택적 마스크 PNG를 저장한다. Mac의 synthesized Codable에 필요한 기본 hue/saturation/lightness/colorize/levels/curves 필드도 기록한다.

Exposure에 다른 조정의 비기본 설정이 들어 있거나 알 수 없는 필드가 있으면 문서 전체를 거부한다. 미지원 비활성 설정을 조용히 삭제하지 않는다. Exposure 외 조정 종류, 7 미만 버전, 조정+이미지 자산, 조정+그룹, 잘못된 수치와 중복 필드도 거부한다. 정확한 제한은 [프로젝트 규격](project-format.md)을 참고한다.

기존 저장 실패 보호·Undo/Redo 정책을 유지한다. 설정만 바꾸면 이미지 타일이 공유되어 해당 편집이 추가로 보존하는 이미지 타일은 0바이트다. 마스크 페인팅은 기존 타일 이력 한도를 따른다. Undo 이력 자체는 파일에 저장하지 않는다.

## 화면과 성능

Exposure가 하나라도 있는 문서는 **문서 해상도에서 전체 합성한 이미지 하나를 캐시**한다. 조정·아래 이미지·마스크·구조 변경 시 다시 합성하며, 확대/이동은 캐시된 이미지를 최근접 샘플링한다. 문서 해상도의 효과를 모든 배율과 내보내기에 동일하게 적용하기 위한 초기 CPU 구현이다. Exposure가 없는 문서는 기존 증분 타일 렌더러를 사용한다.

2026-09-28 01:35 UTC, Windows x64, Intel Core 5 210H/12 logical processors. 4000×4000 반투명 이미지 1개와 Exposure 1개, 1000×1000 Skia 뷰포트, 설정 변경 3회씩 측정했다.

| 조건 | 최초 합성 | 설정 변경+화면 | 캐시된 이동 | working set |
|---|---:|---:|---:|---:|
| 마스크 없음 | 228ms | 120~138ms | 3.0~3.8ms | 358~361MiB |
| 전체 문서 1×1 회색 마스크 | 206ms | 166~183ms | 3.3~4.3ms | 362~363MiB |

회당 관리 메모리 할당은 약 0.40MiB이며 native bitmap 메모리는 working set에 포함한다. 최초 픽셀별 C 커널/보간 구현의 1.1~1.2초에서 개선했다. 최초 비용은 실행 순서 영향이 있으므로 마스크가 더 빠르다는 뜻은 아니다. 결과는 `artifacts/exposure-benchmark.json`이다.

입력 대기 180ms와 WPF 표시·실제 모니터 지연은 제외한다. 합성은 현재 UI 렌더 경로에서 CPU로 수행하므로 큰 문서에서 잠시 입력이 지연될 수 있다. 임의 변환/페인팅된 마스크, 여러 조정 레이어, 100MP 최대 크기와 다층 대형 문서의 비용은 이 표로 보장하지 않는다. Exposure 아래의 연속 브러시 입력이 기존 33.3ms 기준을 충족한다는 뜻도 아니다. 필요 시 조정 문서의 비동기 렌더링 또는 GPU 경로가 후속 성능 과제다.

## 검증

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test
dotnet run --project Compositor.Benchmarks/Compositor.Benchmarks.csproj -c Release --no-build -- --exposure artifacts/exposure-benchmark.json
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Publish
```

- Release 빌드 경고/오류 0, xUnit **157개 통과**.
- 5종 파라미터 조합에서 모든 알파/채널 바이트의 원본 C 커널과 최적화 결과 일치, 독립 선형광 수식 비교, 알파 0 포함 원본 투명도 보존.
- 아래/위 합성 순서, 원본 변경의 동적 반영, 모든 지원 합성 모드, 부모 불투명도·표시, 회색/비활성 마스크, 변환된 마스크·타일 경계·브러시, 그룹 순서/삭제/해제와 Undo.
- 100/150/200% 배율에서 캐시된 화면과 canonical export 일치, 다중 조정·값/원본 변경·삭제·Undo의 캐시 갱신.
- 독립적으로 작성한 Swift 키 형태 fixture, 버전 7/8 왕복, 설정 재편집, 마스크 격자 보존, PNG 출력 일치, 저장 게시 실패 시 원본 보존, 미지원/손상 문서 덮어쓰기 거부.
- 숨김 WPF: 새 레이어·편집·취소·원본 비교·잘못된 값·Undo/Redo, 이미지 직접 칠하기 거부, 마스크 편집, 저장/재열기 후 다시 편집, 그룹 안 생성.

자동 실행 결과는 `artifacts/ui-smoke.exposure.json`, 대화상자 캡처는 `artifacts/ui-smoke.exposure.dialog.png`다. self-contained 실행 파일 결과는 `artifacts/published-exposure-smoke.exposure.json`에 기록한다. 물리 입력·다중 모니터 DPI·실제 Mac 앱 왕복 및 Apple/Skia 변환 가장자리의 완전한 픽셀 동등성은 미검증이다.

2026-09-29에 [Levels 비파괴 조정 레이어](levels-adjustment.md)를 추가했다. 현재 합성기는 Exposure·Levels를 함께 처리하며 채널별 조회표를 사용한다. 위 수치는 2026-09-28 Exposure 구현 당시의 기록이다. 다음 기능은 Curves다.
