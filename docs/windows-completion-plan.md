# Windows 전환 완료 작업표

2026-10-06 사용자 지시: 전체 전환 완료를 목표로 단계별 구현·검증·주제별 커밋·푸시한다.

현재 60/100 완료, 40/100 잔여는 기능 규모에 따른 초기 관리용 추정이며 실측 공수가 아니다. 완료 판정은 아래 기능별 증거를 기준으로 한다. 작업 중 세부 범위·제약이 확인되면 기록한다.

| 영역 | 잔여 가중치 | 작업 |
|---|---:|---|
| 효과·조정 | 5 | Drop Shadow → Stroke → Inner Shadow / Outer Glow → 원본 추가 조정 대조 |
| 선택·크롭 | 6 | 선택 모양·연산·이동, 선택 복사/붙여넣기, 크롭 |
| 추가 도구 | 5 | 도형, 그라디언트, 흐림, 채우기, 스포이트 |
| 텍스트 | 4 | 편집 가능한 텍스트, 글꼴·배치·저장 |
| 작업 공간 | 3 | 그룹 기능·다중 선택·계층 드래그·문서 탭 |
| PSD | 4 | 원본 구조 대조, 지원 요소 가져오기, 미지원 안내; PSD 저장 제외 |
| RAW·AI | 5 | 대체 기술과 라이선스/실행 의존성 검토 후 RAW·객체 선택·배경 제거 |
| 품질·성능 | 4 | 브러시 농도/입력, 샘플링, 대형 문서; 측정 후 GPU 필요 여부 판단 |
| 배포·검증 | 4 | 설치·버전·업데이트·접근성·서명; 인증서 등 외부 자원 필요 사항은 명시 |
| 합계 | 40 | 완료 항목만 차감 |

MVP M1 실제 Mac 왕복 및 M4 별도 Windows PC 장시간 사용은 사용자 지시로 보류한다. 4%는 MVP의 가중치이며 위 40에 추가하지 않는다. 서명 인증서나 AI 모델 다운로드 등 외부 의존성이 없는 것으로 가정하지 않는다.

각 기능은 모델/저장 → 합성/명령 → 직관적 UI → Undo/Redo/취소 → 저장 왕복/출력/회귀 → 문서·커밋·푸시까지 하나의 단위로 완료한다. 숫자만 입력하는 UI 대신 슬라이더와 색상표를 우선하며 정밀 입력도 제공한다.

## 진행 기록

- 착수: Drop Shadow 모델·원본 저장 필드·확장 합성 영역·오버레이 공존 검토.

- Drop Shadow 기능 구현: 색상표/슬라이더, 미리보기/취소/이력, 마스크/오버레이 공존, 저장 왕복. 4K 브러시 지연은 성능 잔여 항목이며 외부 Mac 검증은 보류. 상세는 [Drop Shadow](drop-shadow.md). 효과 영역 전체가 완료된 것은 아니므로 5점 일괄 차감은 하지 않는다.

- Stroke 구현: 안쪽/바깥쪽, 색상표·슬라이더, 마스크·그림자·오버레이 공존, 저장·이력·회귀. 4K 최초 합성 0.45–0.61초는 성능 잔여로 유지. 상세는 [Stroke](stroke-effect.md). 다음은 Inner Shadow/Outer Glow.

- Inner Shadow/Outer Glow 구현: 5종 효과 합성·저장·편집 연결. [검증과 성능 잔여](soft-effects.md).
- 원본 LayerAdjustment.swift 대조: 추가 비파괴 조정은 Hue/Saturation, Gradient Map, Grain, Invert, Black & White, Color Balance 6종. Filters.swift의 Gaussian Blur, Motion Blur, Add Noise, Lens Correction, Remove Background, Content-Aware Fill도 후속 범위로 명시한다. 기존 Exposure/Levels/Curves와 같은 이름이라도 픽셀 필터 경로는 별도 대조가 필요하다.

- Invert/Black & White 조정 구현: 원본 C 커널, 6색 가중치·틴트, 마스크/이력/저장. [검증 및 4K 지연](blackwhite-invert.md). 조정 잔여는 Hue/Saturation, Gradient Map, Grain, Color Balance 4종이며 픽셀 필터 경로는 별도다.

- Color Balance/Grain 비파괴 조정 구현: 슬라이더·원본 C 커널·문서 좌표/시드·마스크·저장·이력 연결. [검증과 성능 잔여](color-balance-grain.md). 추가 조정 잔여는 Hue/Saturation, Gradient Map. 색상 균형 4K 최초 합성 3.93초는 최적화 잔여로 유지한다.

- Gradient Map 구현: 원본 두 끝점 보간/C 밝기 매핑, 색상표·반전·그라디언트 미리보기, 저장·마스크·이력 및 메뉴 정리. [검증](gradient-map.md). 비파괴 조정 잔여는 Hue/Saturation이며 픽셀 필터 경로는 별도다.

- Hue/Saturation 연산 기반 구현: 범위·밴드·샘플링 연산·33차원 색상 큐브·CPU 알파 보간. 아직 메뉴/저장에 연결하지 않았으며 완료 점수는 차감하지 않는다. 저장의 legacy 필드, 스펙트럼/샘플링/대상 드래그 UI, 4K 갱신과 픽셀 필터 경로를 포함한 [남은 작업](hue-saturation.md)을 기록했다. 원본 Gradient Map의 foreground/background 초기 색 연결도 추가 확인 사항이다.
