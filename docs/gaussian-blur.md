# Gaussian Blur 전환

2026-10-08. 원본 macOS/Compositor/Document/Filters.swift의 FilterSettings, PixelFilter.run/trimmed 및 FilterEdit.growForBlur를 기준으로 구현했다.

## 구현 범위
- Filter → Gaussian Blur. 반경은 레이어 픽셀의 표준편차 0.1–250, 기본 1이다. 슬라이더와 정밀 입력, Reset, Preview changes, Apply/Cancel을 제공한다.
- ceil(3×반경+2)픽셀의 투명 여백을 사방에 만들고 RGBA8 premultiplied sRGB에 Skia Gaussian Blur를 적용한다. 가장자리는 Clamp하지 않으며 바깥으로 번진 뒤 알파가 없는 경계를 잘라낸다. 회전/뒤집기·픽셀 크기·문서 위치를 보존한다. 패널 내 여백은 원본처럼 가장 큰 요청 반경을 기준으로 유지한다.
- 문서 좌표의 선택 커버리지로 원본과 흐림 결과를 섞는다. 선택 밖 픽셀과 원본 Raster는 보존한다. 선택이 레이어 밖에만 있거나 빈 이미지이면 무변경이다.
- 이미지 격자 변경과 별개로 원래 마스크 Raster를 공유하고 원래 배치를 고정해 가장자리 농도 합성 경로를 유지한다. 기존 독립 배치·연결·활성 상태와 레이어 계층/불투명도/합성/효과를 유지한다. 실제 픽셀 편집이므로 liveShape는 해제된다.
- 문서의 확장 이미지 치수/총 픽셀 예산은 이미지 할당 전에 검사한다. 최종 문서 검증 및 256MiB Undo 제한도 검사한다. 결과는 기존 .comp PNG/배치 형식으로 저장하므로 새로운 필드나 저장 버전이 필요하지 않다.
- 40ms 입력 대기, 직렬 worker 계산, 최신 요청만 게시, 패널 종료/실패 및 새 트랜잭션에 대한 늦은 결과 보호를 제공한다. 원본 기반 미리보기이며 Apply는 최신 준비 결과를 한 번의 Undo로 확정한다. 비교 체크박스와 Cancel은 원본 문서를 복원한다.

## 검증과 한계
코어 601개 및 실제 숨김 WPF 회귀 통과. 새 코어 검사는 투명 경계 확장·256픽셀 타일 경계·premultiplied 불변·선택 밖 픽셀·마스크 격자/회전/뒤집기·Undo/Redo·잘못된 반경/취소/예산·선택 외 무변경·빈 레이어의 예산 규칙을 다룬다. WPF는 연속 설정의 최신 결과·비교·Apply/Undo/Redo·저장 후 출력/마스크 보존·닫기 취소·새 트랜잭션 보호를 검사한다. artifacts/ui-smoke.filters.png의 실제 패널 캡처를 확인했다.

원본은 최대 2048픽셀의 표시용 필터 미리보기와 원본 크기 확정을 분리한다. 현재 Windows는 전체 크기 결과를 worker에서 준비하여 같은 결과를 표시/확정한다. 따라서 원본의 대형 미리보기 성능 수준은 아직 달성했다고 주장하지 않는다. 4K/100MP 메모리·연속 입력/화면 준비 성능과 작은 표시용 미리보기 분리, 실제 Mac의 Core Image 경계 픽셀 동등성은 잔여다. Skia 단일 Blur 및 Raster.FromRgba 내부 중간 취소는 제공하지 않으며 작업 사이에서 취소를 확인한다. 실제 Mac 왕복과 별도 Windows PC 실기는 사용자 보류를 유지한다.

다음 공간 필터는 Motion Blur, Add Noise, Lens Correction이다. Remove Background/Content-Aware Fill은 AI/별도 알고리즘 의존성을 포함하며 이번 구현으로 완료 처리하지 않는다.

배포본 artifacts/publish-shape-ui 갱신 및 verify-portable.ps1 -Runs 1 통과. 개발 PC에서 공유 런타임을 분리한 실행이며 별도 Windows PC 실기를 통과로 표시하지 않는다.
