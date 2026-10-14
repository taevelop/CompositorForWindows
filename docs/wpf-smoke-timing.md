# WPF 전체 회귀 시간 진단

2026-10-14. `build.ps1 -Test`의 30초 프로세스 제한을 유지했다. 검사별 start/passed/failed 및 단계/누적 시간을 `artifacts/ui-smoke.stages.jsonl`에 즉시 기록한다. 종료되지 않은 마지막 start는 timeout 시 실행 단계의 증거이며 해당 단계 자체의 실패를 의미하지 않는다. 각 실행마다 로그를 초기화한다.

측정1: Filter3313ms, BlackWhite2502ms, PixelAdjustment2092ms, Levels1554ms, Curves1502ms. InnerShadow740ms와 OuterGlow694ms는 완료했고 ToolControls가 누적28536ms에 시작한 후 전체30초 제한에서 종료됐다. 이전 inner-shadow screenshot만으로 그 단계가 멈췄다고 판단할 수 없음을 확인했다.

ToolControls는 이전 효과 검사의 최종 문서를 모든 도구/메뉴 screenshot에서 계속 그렸다. 독립800×600 문서를 사용해 검사 간 fixture 의존성을 제거했다. 기존 도구별 좌표/레이아웃/팔레트/메뉴 검사와 screenshot을 유지하고 MagicWand(index12)도 포함했다. 실제 편집/레이어opacity 검사는 이 문서에서 진행한다.

측정2에서도 OuterGlow가 완료되고 ToolControls가 누적27458ms에 시작했으나 전체30초 제한에서 종료됐다. 따라서 fixture 격리만으로 해결된 것으로 기록하지 않는다. Release 경고/오류0 및 코어657개는 통과했다. WPF 전체는 미통과 유지.

다음은 렌더링/PNG 인코딩/모달 및 dispatcher 대기 비용의 상세 측정과 병목 최적화다. 테스트 삭제·검사 축소·30초 제한 연장 없이 진행한다. 실제 Mac 왕복/별도 Windows PC 검증과도 별개다.

## 캡처 및 단독 검사 측정

--tools-smoke report CLI를 추가했다. 기존 full smoke의 ToolControls 전체검사를 동일하게 실행하고, 진단 fixture만 MarkSaved/Close하여 숨겨진 저장확인을 방지한다. 초기 진단은 보고서 작성 후 수정fixture 저장확인 때문에 timeout됐으며 정상 사용자 종료 동작을 변경한 것은 아니다.

수정 후 단독프로세스2965ms/exit0. 35개 캡처 render268ms+PNG encode211ms로 캡처만의 주된 병목은 입증되지 않았다. 각 capture renderMs/encodeMs를 보고서에 기록한다. core657개통과/빌드0경고·오류. 이후 full smoke는 기존30초 제한 내 exit0: ToolControls2057ms, 누적28368ms에 마지막 단계 완료. 이전 timeout 대비 같은 조건의 여러단계 시간이 달라졌으므로 완전한 원인해결/안정성 개선으로 주장하지 않는다. 단독 저장확인 문제와 full smoke 누적실행시간을 구분한다. 반복/상세 rendering/modal 대기 진단은 품질잔여다.

## 시작 시 도구 막대 스크롤 수정

ToolPicker의 기본 ListBox 내부ScrollViewer와 바깥 도구rail ScrollViewer가 중첩돼 있었다. 도구ItemsPresenter/비가상화StackPanel을 명시해 스크롤소유자를 ToolRailScroll 하나로 만들었다. 13개 도구와 팔레트가 기본860px 높이에 들어가도록 도구높이34·세로margin2로 조정했다. 작은창의 실제overflow에는 Auto스크롤을 유지한다.

--tools-smoke에서 최초layout(창 크기변경전) scrollbarCollapsed/ScrollableHeight0, 최소1000×650에서 scrollbarVisible 및 palette도달, 원래높이복원후Collapsed를 검증했다. startup screenshot도 시각확인했다. 코어657개통과. fullWPF30초timeout재발(마지막toolstage시작28.59초)과는구분한다.

수정배포본 artifacts/publish-shape-ui 갱신, portable fullWPF45초기존한도1/1통과. 30초개발회귀timeout문제는미해결이며 portable통과로대체하지 않는다.
