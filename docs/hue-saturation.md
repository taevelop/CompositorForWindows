# Hue/Saturation 이식 진행

2026-10-06. 현재는 연산 기반 단계다. 사용자 메뉴·조정 레이어 저장/합성에는 아직 연결하지 않았으며, .comp의 Hue/Saturation 조정은 계속 열기를 거부한다. 이 문서는 기능 완료 선언이 아니다.

## 구현·검증된 기반

- 원본 macOS/Compositor/Document/HueSaturation.swift의 Master와 6개 색상군, 기본 밴드, 원형 falloff 가중치, 선택 범위 반전, 색상군별 독립 H/S/L 값.
- 밴드 핸들 이동의 순서 제한, 샘플링에 사용할 중심 이동/포함/제외 연산.
- 원본과 같은 361개 hue response 표와 33×33×33 HSL 색상 큐브. 빨강 축이 가장 빠르게 변하는 RGBA float 배열이다.
- 원본 CIColorCube 경로를 대신하는 CPU trilinear 보간. 알파를 풀고 큐브를 조회한 뒤 한 번만 premultiply하며 투명도는 유지한다. 중립 설정은 바이트를 건드리지 않는다.
- 설정은 불변 사전과 값 기반 동등성을 사용하여 이후 Undo/no-op 판정에 사용할 수 있다.
- 원본과 같이 로딩 가능한 Hue 범위는 ±360, Saturation/Lightness는 ±100이다. UI의 일반 Hue 슬라이더는 ±180, Colorize는 0..360으로 제한할 예정이다.
- NaN/무한대 및 차분에서 오버플로가 나는 밴드는 거부한다.

Release 빌드 경고/오류0, xUnit 총339개 통과 및 기존 WPF 회귀 통과. 추가15개 검사는 원본 테스트의 밴드 경계/어깨/랩, 색상군 독립 조정/반전, 샘플 포함·제외, 원색 회전/회색 보존, 흑백 극값, Colorize, 모든 알파 값, 8꼭짓점 보간 및 불변 설정을 검사한다.

4000×4000 RGBA 알파180 입력 1회 측정: 큐브 생성15.9ms, CPU 적용1211ms, 프로세스 working set93MiB. 합성기·WPF·저장 비용은 제외했다. 실시간 대형 문서 편집에 충분하다고 판정하지 않는다. Mac CIColorCube와 픽셀별 실기 비교는 보류 중이며 일치한다고 단정하지 않는다.

재현:
- ./build.ps1 -Test
- dotnet run --project Compositor.Benchmarks -c Release -- --hue-cube artifacts/hue-cube-benchmark.json

## 다음 필수 작업

1. Swift enum 키 사전의 Codable 형태 확인 및 범위 설정·밴드 저장 구현. hsvSettings 이전의 hue/saturation/lightness/colorize 필드도 읽고 보존해야 한다. 원본 편집은 hsvSettings만 바꾸므로 기존 중복 필드를 조용히 버리지 않는다.
2. 문서 모델·합성·마스크·저장·Undo 연결과 원본 형태 fixture 왕복 검사.
3. 색상군 전환·H/S/L 슬라이더·Colorize·범위 반전·전후 스펙트럼 및 밴드 핸들 UI.
4. 원본에 있는 캔버스 샘플/추가/제거와 대상 색상 드래그. 모달 창만 추가하고 이 기능을 누락한 상태로 완료하지 않는다.
5. 취소·비교·그룹·불투명도·저장 재열기·출력 및 실제 UI 검증, 대형 문서 갱신 지연 개선.
6. 동일 조정의 원본 픽셀 필터 경로는 별도 연결한다.

추가 발견: 원본 새 Gradient Map은 현재 foreground/background 색을 초기 끝점으로 사용한다. 현재 Windows는 흑백 기본값으로 시작한다. 두 색 자체의 편집·저장은 구현됐으나 foreground/background 상태 및 새 레이어 초기화의 동등성은 후속 도구 작업에서 맞춰야 한다.