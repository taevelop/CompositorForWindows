# Canvas Size

## 원본 기준과 구현

macOS/Compositor/Document/CanvasSize.swift, UI/CanvasSizeSheet.swift, IO/CanvasResizer.swift를 대조했다.

Image > Canvas size…에서 캔버스 크기를 변경한다. Pixels / Percent / Inches / Centimeters, 상대 크기 입력, 원본 종횡비 잠금 및 9개 기준점을 제공한다. 물리 단위는 문서 해상도를 사용하며 해상도를 변경하지 않는다. 중심 기준의 홀수 크기 차이는 원본처럼 내림하여 오른쪽/아래쪽에 확장 여분을 놓는다.

새 캔버스와 기존 캔버스 영역을 미리보기로 비교한다. 이미지·마스크 픽셀은 재샘플링하지 않고 참조를 유지하며 모든 레이어의 절대 위치를 이동한다. 그룹 자식 좌표는 한 번만 변경한다. 효과와 조정 등 메타데이터를 유지한다. 변경 시 선택을 해제하며 같은 크기는 무변경으로 처리한다.

확장 배경은 Transparent / Foreground / Black / White / Custom을 제공하고 Custom은 색상표로 고른다. Foreground는 현재 브러시 색상이다. 원본의 별도 Background 전역 색상 슬롯은 아직 Windows에 없으므로 해당 선택은 후속 팔레트 작업으로 남긴다. 이를 White로 잘못 대응하지 않는다.

색이 있는 확장은 Canvas Extension이라는 독립된 최하단 레이어를 추가한다. 기존 캔버스와 겹치는 영역은 투명하게 유지하여 기존 이미지의 투명한 구멍을 채우지 않는다. 확장 타일만 할당하고 100MP 문서·소스 이미지/10,000 레이어 한도를 할당 전에 검사한다. 계산은 백그라운드에서 수행하며 취소/실패 시 원본 문서를 변경하지 않는다. 완료한 결과만 한 번의 Undo로 적용한다.

## 검증

- ./build.ps1 -Test: 경고/오류 0, 코어 443개 및 실제 WPF 스모크 통과.
- 기준점 9개, 양/음의 홀수 오프셋, 단위·상대·비율 잠금·반올림, 잘못된 입력, 타일 경계, 확장/축소 혼합, 문서 예산 및 취소.
- 확장 색상과 투명 구멍 보존, 마스크·효과·좌표 및 활성 레이어 저장 왕복.
- 실제 메뉴와 모달 창 적용/취소, 색상표 결과, 단일 Undo/Redo, 비동기 계산 중 창 닫기와 늦은 결과 폐기.
- artifacts/ui-smoke.canvas-size.png 화면 확인. 기준점 버튼의 기본 밝은 스타일을 제거하고 다크·블루 테마를 적용했다.

## 잔여와 실행

[Image Size](image-size.md)의 재샘플링 대화상자를 연결했다. 선택 자동 스크롤·픽셀 변형·대용량 성능, 전역 배경색 팔레트도 남아 있다. 실제 Mac 왕복과 별도 Windows PC 검증은 사용자 지시로 보류한다.

실행 패키지: artifacts/publish-image-size/Compositor.Windows.exe.
verify-portable.ps1 -Runs 1 격리 실행 통과. 공백 경로·제한 PATH·공유 런타임 미사용 조건의 개발 PC 검사이며 별도 PC 실기 검증을 대체하지 않는다.
