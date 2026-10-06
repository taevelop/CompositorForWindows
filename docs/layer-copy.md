# Layer via Copy와 레이어 복제

원본 Document/SelectionClipboard.swift의 layerViaCopy/duplicateActiveLayer를 대조했다.

- Ctrl+J 또는 Layer > Duplicate / Layer via copy: 선택이 있으면 해당 이미지/마스크 픽셀을 문서 위치에 새 이미지 레이어로 만든다. 선택이 없으면 활성 레이어 전체를 복제한다. 원본처럼 그룹은 이 명령에서 제외한다.
- Layer > Duplicate whole layer / group: 픽셀 선택 여부와 관계없이 선택된 레이어 및 그룹의 모든 자식을 복제한다. 부모와 자식이 동시에 선택되면 부모 루트만 복제한다. 자식의 부모 ID를 새 그룹 ID로 치환하며 외부 부모는 유지한다. 그룹 접힘 상태도 처음 복제할 때 전달한다.
- 전체 복제는 픽셀·마스크·효과·조정·변환·불투명도 등 메타데이터를 공유/유지하고 새 ID를 부여한다. 선택 픽셀 복사는 기존 raw copy 규칙을 사용하여 효과/불투명도/마스크를 별도로 굽지 않는다. 마스크 편집 중에는 그레이스케일 픽셀을 복사한다.
- 선택 복사는 선택을 해제하고 새 레이어를 활성화한다. 전체 복제는 픽셀 선택을 유지하고 새로 복제한 루트 레이어들을 선택한다. 문서/레이어 한도 검사 뒤 한 번의 Undo에 적용하며 시스템 클립보드를 변경하지 않는다.

## 검증

./build.ps1 -Test: 코어 460개 및 실제 WPF 검사. 공유 픽셀/마스크, 효과 보존, 그룹 내부/외부 부모 연결, 선택 복사 위치와 raw 알파, 마스크 복사, 빈 선택, 문서 소스 예산 초과 시 무변경, 활성 레이어/선택을 포함한 Undo/Redo를 검사했다. 실제 명령 처리와 복제 그룹의 접힘 상태도 확인했다.

현재 선택 픽셀 복사 연산은 UI 스레드의 기존 복사 렌더 경로를 사용한다. 큰 선택 영역의 지연 측정/비동기화는 품질 잔여다. Alt-drag 배치는 후속 작업 공간 범위다. 전환 전체 완료로 계산하지 않는다.

실행 패키지: artifacts/publish-layer-copy/Compositor.Windows.exe.

verify-portable.ps1 -Runs 1 격리 실행 통과. 개발 PC에서 공백 경로·제한 PATH·공유 런타임 미사용 조건으로 검사했으며 별도 Windows PC 검증을 대체하지 않는다.

## 다중 선택 복제

명시적 Duplicate whole layer / group 명령은 선택한 최상위 루트들을 문서 순서대로 복제한다. 원본의 beginDuplicateTransform이 선택 루트를 추리는 규칙을 적용했으며, 아직 Alt-drag 제스처를 구현한 것은 아니다. 새 루트는 각각 원본 바로 위의 같은 부모에 배치한다. 그룹 내부 ID는 치환하고 접힘 상태를 전달한다. 마지막 루트 복사본이 활성 항목이 되며 모든 루트 복사본이 선택된다. Ctrl+J는 원본의 활성 레이어/선택 픽셀 복사 규칙을 유지한다.

문서 예산과 레이어 수 제한을 변경 전에 검사하며 전체 복제는 한 Undo로 취소한다. 부모/자식 중복, 서로 다른 외부 부모, 숨긴 레이어, 이미지/마스크/효과 공유, 실패 시 문서/선택 불변, Undo/Redo 선택 복원과 실제 WPF 복제 메뉴 경로를 검사한다.

다중 복제 최종 검증: ./build.ps1 -Test 경고/오류 0, 코어 502개와 확장한 실제 WPF 검사 통과. artifacts/publish-multicopy/Compositor.Windows.exe 생성 및 verify-portable.ps1 -Runs 1 통과. 별도 Windows/Mac 실기 검증을 대체하지 않는다.
