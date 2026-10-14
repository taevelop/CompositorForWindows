# Smudge와 Liquify 전환

원본 기준은 `macOS/Compositor/Document/SmudgeLiquify.swift`다. Smear(R)의 모드는 Liquify, Blur, Smudge이며 이미지 픽셀만 변형한다. 원본 beginWarp는 마스크 선택을 거부한다.

## 문서 좌표 연산

`WarpRasterStroke`는 렌더링된 활성 레이어의 문서 크기 Raster를 받아 private RGBA8 premultiplied 버퍼에서 작업한다. 원본과 같이 diameter 최소 2, hardness 최대 .98, strength 최소 .01을 사용한다. 첫 입력은 시작점이며 Smudge만 색을 집는다. 이동 간격은 Smudge에서 max(1, diameter*.08), Liquify에서 max(1, diameter*.025)다. 간격에 못 미치는 입력은 마지막 시작점을 갱신하지 않는다.

Smudge는 정수로 반올림한 중심 주변의 색을 float 배열에 운반하고 smoothstep falloff로 칠한다. strength는 초기 dab의 직접 불투명도가 아니라 칠한 뒤 운반색을 유지하는 비율이다. Liquify는 dab 전의 주변 영역을 scratch에 복사하고 strength만큼 이동한 역방향 위치에서 bilinear sample한다. 색과 alpha를 같은 방식으로 처리한다. 원본과 같이 한 행/열밖에 없는 scratch에서는 2차원 보간을 건너뛴다.

dab 중심 목록을 유지하고 Append가 성공하면 immutable Raster 스냅샷을 게시한다. 원본과 이전 스냅샷은 변경하지 않는다. row/dab/변환 경계에 취소 확인을 추가했으며 실패·취소된 stroke는 재사용을 거부한다. 취소 도중 private pixels가 부분 수정돼도 마지막 게시 스냅샷은 유지한다.

코어 705개 통과: Smudge의 반투명 red/alpha128 운반·입력 간격·원본/이전 스냅샷 불변·premul, Liquify의 타일 경계 중심에서 독립 0.5px 선형 보간 기준120→110, 범위 밖 픽셀, 취소 후 게시 없음/재사용 거부, 한 행 및 설정·pointer 검사. 이는 몇 가지 독립 수치 기준이며 실제 Mac 전체 픽셀 왕복이나 일반 품질 통과가 아니다.

## 남은 연결

- 활성 레이어 raw document sample 준비와 작업 중 렌더링·좌표/메타데이터 유지.
- 원본 finishWarp처럼 diameter+4의 hard/opacity1 tip과 dab 경로로 layer grid에 최종 반영. 원본 `replacesWithClone=true`는 알파를 포함한 copy 계약이며 기존 Clone source-over와 구분해야 한다.
- selection/feather의 최종 clipping, 레이어 확장/crop, mask 배치 및 이미지/Undo 예산.
- Smear의 세 모드 selector, 탭 상태, 비동기 준비·입력 큐·취소/늦은 결과 보호, 단일 Undo/Redo 및 저장/내보내기 대기.
- 실제 WPF·프로젝트/출력 검증, 자연 이미지·회전/flip·대형/연속 입력 성능 및 배포 갱신.

현재 Append마다 문서 전체 스냅샷을 변환하므로 대형 문서 비용은 아직 높을 수 있다. 대형/장시간 성능을 측정하기 전 완료로 표시하지 않는다. 이번 단계는 연산 기반이며 현재 배포본의 Blur UI에는 Smudge/Liquify 모드가 없다. 실제 Mac 왕복과 별도 PC 실기는 사용자 지시로 보류다.
