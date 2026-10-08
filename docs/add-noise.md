# Add Noise 전환

2026-10-08. 원본 Document/Filters.swift 및 Rendering/NoisePixels.c를 대조했다. native/kernels/NoisePixels.c와 원본 파일의 SHA-256 해시가 같다. 커널 변경 없이 고정 폭 int32/uint32 C ABI 내보내기를 추가했다.

## 구현
- Filter → Add Noise. Amount 0.1–400%(기본 10), 슬라이더/정밀 입력, Gaussian distribution/Monochromatic 체크, 비교/Reset/Apply/Cancel. Gaussian을 체크하지 않으면 Uniform이다.
- 원본 C 커널의 픽셀 좌표 해시, Uniform/Box–Muller Gaussian, 단색/채널별 변화, unpremultiply → 색상 변화 → premultiply를 사용한다. 알파와 투명 픽셀은 보존한다.
- 패널별 한 번 생성한 uint32 시드를 유지해 Amount/분포/단색을 바꿔도 패턴이 새로 섞이지 않는다. 새 적용 패널에서는 새 시드다. 원본처럼 노이즈는 전체 해상도로 준비하여 축소 미리보기의 입자 크기 변경을 피한다.
- PixelFilterWindow로 Gaussian/Motion/Noise의 직렬 worker·40ms 입력 대기·최신 요청·트랜잭션 세대·표시 전용 문서·비교/닫기 취소를 공유한다. Session.Document는 원본을 유지하며 Noise Apply는 준비된 전체 해상도 결과를 한 번의 Undo로 게시한다.
- 선택은 문서 좌표 커버리지로 제한한다. 원본 이미지/마스크·변형·효과/합성/불투명도·계층을 보존하고 픽셀이 바뀌면 liveShape를 해제한다. 무변경 타일은 공유하며 전체 무변경은 문서 참조와 Undo를 유지한다. 기존 PNG 기반 .comp로 저장하므로 새 저장 필드는 없다.

## 검증
Release 경고/오류 0, 코어 613개와 실제 숨김 WPF 회귀 통과. Uniform 전체 픽셀 좌표/시드 해시를 별도 C# 참조 계산과 비교해 256픽셀 경계·반투명·투명 영역의 전체 바이트 일치를 확인했다. Gaussian 평균/편차·단색·동일/다른 시드, 알파/premultiplied, 선택 밖 픽셀·마스크/메타데이터·Undo/Redo·무변경 공유·범위/취소를 검사한다.
WPF는 설정/비교·원본 불변·Amount 변경 후 되돌리기의 동일 패턴·준비 결과 그대로 Apply·단일 Undo/Redo·.comp 출력 왕복·닫기 취소를 검사한다. artifacts/ui-smoke.filters.noise.png의 패널 캡처를 확인했다. 기존 Gaussian/Motion 회귀도 통과했다.

첫 전체 WPF 실행은 30초 제한으로 종료됐으며 통과로 표시하지 않는다. 마지막 산출물은 메뉴 캡처 단계였다. 후속 전체 실행은 같은 제한에서 통과했고 제한을 늘리거나 검사를 생략하지 않았다. 타임아웃의 원인은 확정되지 않아 기존 간헐적 WPF 검사 안정성 항목을 유지한다.

--add-noise 코어 벤치마크 1회(4000×4000 불투명 회색, 10%, 단색, seed 7): Uniform 563ms, Gaussian 1077ms, 누적 managed 약 125.3MiB/회. 전체 알파/치수 보존을 함께 검사한다. WPF 연속 입력·피크 메모리·실제 Mac 픽셀 왕복의 증거가 아니다.

## 잔여
원본 C 구현을 재사용하지만 플랫폼별 부동소수 연산 및 실제 Mac 왕복은 미검증이다. 대형 연속 입력/화면 캐시·전체 이미지 버퍼·피크 메모리/256MiB Undo 제한의 품질은 잔여다. 단일 C noise_add 내부 중간 취소는 없고 호출 전후/타일 사이에서 검사한다. 취소 시 문서는 즉시 원본으로 돌아가지만 worker 종료는 다음 체크까지 기다릴 수 있다. 별도 Windows PC 실기는 사용자 보류를 유지한다. 다음 공간 필터는 Lens Correction이다.

배포본 artifacts/publish-shape-ui 갱신 및 verify-portable.ps1 -Runs 1 통과. 새 네이티브 노이즈 심볼을 포함한 공유 런타임 분리 실행이며 별도 PC 실기는 보류다.
