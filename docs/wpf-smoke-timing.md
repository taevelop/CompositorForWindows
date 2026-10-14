# WPF 전체 회귀 시간 진단

2026-10-14. `build.ps1 -Test`의 30초 프로세스 제한을 유지했다. 검사별 start/passed/failed 및 단계/누적 시간을 `artifacts/ui-smoke.stages.jsonl`에 즉시 기록한다. 종료되지 않은 마지막 start는 timeout 시 실행 단계의 증거이며 해당 단계 자체의 실패를 의미하지 않는다. 각 실행마다 로그를 초기화한다.

측정1: Filter3313ms, BlackWhite2502ms, PixelAdjustment2092ms, Levels1554ms, Curves1502ms. InnerShadow740ms와 OuterGlow694ms는 완료했고 ToolControls가 누적28536ms에 시작한 후 전체30초 제한에서 종료됐다. 이전 inner-shadow screenshot만으로 그 단계가 멈췄다고 판단할 수 없음을 확인했다.

ToolControls는 이전 효과 검사의 최종 문서를 모든 도구/메뉴 screenshot에서 계속 그렸다. 독립800×600 문서를 사용해 검사 간 fixture 의존성을 제거했다. 기존 도구별 좌표/레이아웃/팔레트/메뉴 검사와 screenshot을 유지하고 MagicWand(index12)도 포함했다. 실제 편집/레이어opacity 검사는 이 문서에서 진행한다.

측정2에서도 OuterGlow가 완료되고 ToolControls가 누적27458ms에 시작했으나 전체30초 제한에서 종료됐다. 따라서 fixture 격리만으로 해결된 것으로 기록하지 않는다. Release 경고/오류0 및 코어657개는 통과했다. WPF 전체는 미통과 유지.

다음은 렌더링/PNG 인코딩/모달 및 dispatcher 대기 비용의 상세 측정과 병목 최적화다. 테스트 삭제·검사 축소·30초 제한 연장 없이 진행한다. 실제 Mac 왕복/별도 Windows PC 검증과도 별개다.
