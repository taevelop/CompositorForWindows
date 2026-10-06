# 이미지 픽셀에 조정 적용

2026-10-06. Image → Apply to image pixels에서 Hue/Saturation, Exposure, Levels, Curves, Invert, Black & White, Color Balance, Grain, Gradient Map을 선택한다.

## 사용자 동작

조정 레이어를 추가하는 New adjustment layer와 별도 경로다. 선택한 표시 이미지의 원본 픽셀에 결과를 반영하며 레이어를 추가하지 않는다. 그룹·조정 레이어·마스크 편집 상태·숨긴 레이어는 사용할 수 없다.

설정 창에는 “image pixels” 제목과 적용 시 픽셀이 바뀐다는 안내를 표시한다. 기존 슬라이더·곡선·색상표·스펙트럼 UI를 재사용한다. Invert는 별도 설정 없이 바로 적용한다.

미리보기는 매번 편집 시작 시 원본에서 계산하며 누적하지 않는다. 비교를 끈 상태에서도 Apply는 입력한 설정을 적용한다. Cancel/Esc/창 닫기는 원본을 복원한다. 적용은 한 번의 Undo이며, 결과가 같으면 이력을 만들거나 Redo를 지우지 않는다. 변환·레이어 불투명도·합성 모드·마스크·효과는 유지한다.

저장 후에는 바뀐 이미지 PNG가 원본 자산으로 기록된다. 조정 파라미터를 별도로 저장하지 않으므로 재열기 후 설정을 다시 편집할 수 없다. 재편집 가능성이 필요하면 New adjustment layer를 사용한다.

## 구현과 검증

픽셀 크기의 임시 문서에서 기존 조정 창을 구동하고 실제 문서에는 픽셀 변경만 미리보기한다. 조정 레이어와 같은 픽셀 연산 경로를 사용한다. Grain 좌표는 원본 이미지 픽셀 기준이다. 알파를 보존하므로 빈 타일은 유지하고, 바뀌지 않은 타일은 공유한다. 이 변경 자체의 Undo가 256MiB 제한을 넘으면 적용하지 않는다.

Release 빌드 경고/오류0, xUnit359개 통과. 9종 조정과 동일 원본의 합성 결과 일치, 0..255 알파 보존, 타일 경계·공유·중립값, 취소 토큰·비정상 입력, 적용 후 프로젝트 저장/재열기·마스크/효과/출력 일치를 검사한다.

실제 WPF에서 9종 적용, 생성 레이어 없음, 미리보기 취소·비교 상태 Apply, 단일 Undo/Redo, 메타데이터 보존, no-op의 Redo 보존을 검사했다. 렌더 오류를 주입해 원본 복구·창 유지·재시도도 확인했다. 배포본 검사에 포함된다.

## 성능과 남은 범위

4000×4000 RGBA 입력, 포장·필터·불변 타일 재구성까지 각1회 측정:

| 조정 | 처리 시간 | 관리 메모리 누적 할당 |
|---|---:|---:|
| Exposure | 169ms | 126MiB |
| Hue/Saturation | 1313ms | 187MiB |
| Color Balance | 3596ms | 187MiB |
| Grain | 524ms | 187MiB |

각 결과의 256개 타일이 변경됐다. 표의 할당량은 최대 생존 메모리가 아니며 네이티브 메모리도 포함하지 않는다. WPF 표시·실제 문서 최종 합성 비용은 제외한다. 현재 동기 처리이므로 특히 4K Hue/Color Balance의 입력 지연 개선이 필요하다.

선택 영역 한정 적용과 마스크 픽셀 조정은 후속 범위다. Hue/Saturation의 픽셀 모드 미리보기·샘플링은 분리된 원본 이미지 기준이며 메인 합성 화면의 효과/마스크가 반영된 색 샘플링과 다를 수 있다. 원본의 Gaussian Blur, Motion Blur, Add Noise, Lens Correction, Content-Aware Fill, Remove Background는 이번 9종 색상 조정 경로와 별개로 남아 있다. 실제 Mac 비교와 별도 Windows PC 검증은 보류한다.

재현:
- ./build.ps1 -Test
- dotnet run --project Compositor.Benchmarks -c Release -- --pixel-adjustments artifacts/pixel-adjustments-benchmark.json
- ./build.ps1 -Publish -PublishDirectory artifacts/publish-pixel-adjustments
- ./verify-portable.ps1 -Runs 1 -PackageDirectory (Join-Path $PWD artifacts/publish-pixel-adjustments)