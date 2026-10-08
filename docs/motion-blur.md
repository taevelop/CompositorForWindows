# Motion Blur 전환

2026-10-08. 원본 Document/Filters.swift의 FilterSettings/PixelFilter.run/FilterEdit.blurMargin 및 CompositorTests/FilterTests.swift의 방향 검사를 대조했다.

## 구현
- Filter → Motion Blur. 거리 1–2000 레이어 픽셀(기본 10), 각도 −90–90°(기본 0), 두 슬라이더와 정밀 입력, Reset/비교/Apply/Cancel. 이미지 픽셀 대상에서만 Filter 메뉴를 활성화한다.
- 양의 각도는 화면의 오른쪽 위 방향이다. 원본의 distance/√12 변환을 사용한 방향성 Gaussian 분포로 흐림을 만든다. Skia와 Core Image의 커널/보간 픽셀 동등성은 확인 전에는 주장하지 않는다.
- 수평/수직은 Skia 한 축 흐림이다. 대각선은 양선형 회전 → 수평 흐림 → 역회전이며, 회전된 전체 격자 대신 128행 스트립과 양쪽 1행 halo를 사용한다. 역회전 시 겹치는 경계를 일관되게 잘라 스트립 이음새를 피한다. 스트립 사이에 취소를 확인한다.
- ceil(distance/2+2)의 투명 여백, 알파 경계 정리, 문서 좌표 선택 제한, 마스크 격자/배치 보존, 원본 불변, 한 번의 Undo, 저장 왕복을 Gaussian과 공통 SpatialBlur 경로로 처리한다. 픽셀 편집이므로 도형 스타일은 해제하며 기존 계층/불투명도/합성/효과는 유지한다.
- 최대 2048px 표시 전용 미리보기와 원본 크기 Apply를 분리한다. worker 직렬화·최신 요청·트랜잭션 세대 검사·비교·닫기/적용 중 취소를 기존 필터 흐름과 공유한다. 문서 및 Undo/저장에는 작은 표시 픽셀을 게시하지 않는다.

## 검증
Release 경고/오류 0, 코어 610개와 실제 숨김 WPF 회귀 통과. 원본의 0/±90/±45° 점 스트릭 방향, premultiplied, 원본 불변, 선택/마스크/Undo, 잘못된 설정/취소, 거리 2000의 2048px 표시 제한을 검사한다. 320px 입력/37°의 스트립 결과를 회전 전체 격자의 별도 참조 구현과 비교해 채널 오차 최대 2단계 이내를 확인했다. 이것은 Windows 구현의 스트립 경계 검사이며 Core Image 비교 증거는 아니다.

WPF는 거리/각도 설정·표시 문서 불변·준비 결과·Apply/Undo/Redo·.comp 출력 왕복·닫기 취소를 검사한다. artifacts/ui-smoke.filters.motion.png의 실제 패널을 확인했다. Gaussian 기존 회귀도 함께 통과했다.

--motion-blur 코어 벤치마크 1회(4000×4000 둥근 사각형, 거리 40/45°): 표시 준비 581ms/누적 managed 64.0MiB, 원본 적용 1830ms/446.6MiB. working set은 완료 후 표본이며 피크 메모리·WPF 연속 입력·물리 화면 지연을 증명하지 않는다.

## 잔여
실제 Mac의 커널/회전 보간 동등성, 작은 거리의 회전 보간 품질, 4K 연속 미리보기·큰 거리/100MP·복합 문서·전체 적용 메모리와 지연 최적화는 남아 있다. 원본 크기의 입력/출력 비트맵은 여전히 필요하며 스트립 처리로 전체 메모리 상한을 주장하지 않는다. 축 방향의 단일 Skia 흐림과 Raster.FromRgba 내부 중간 취소는 제공하지 않는다. 실제 Mac 왕복 및 별도 Windows PC 실기는 사용자 보류를 유지한다.

다음 공간 필터는 Add Noise, Lens Correction이며 AI 배경 제거/Content-Aware Fill은 별도 범위다.

배포본 artifacts/publish-shape-ui 갱신 및 공유 런타임을 분리한 WPF 실행 1회 통과. 별도 PC 실기는 보류다.
