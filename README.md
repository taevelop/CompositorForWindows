# Compositor for Windows — 핵심 편집 MVP

Windows 11 x64용 C#/.NET 10 WPF 앱입니다. 원본 macOS 앱의 파일 규격과 편집 설계를 바탕으로 구현했으며, Mac 앱과 전체 기능·픽셀 결과가 동등한 버전은 아닙니다.

Windows 구현은 저장소 루트에, 최초 클론한 macOS 원본은 `macOS/`에 분리되어 있습니다. macOS 빌드와 사용법은 [macOS README](macOS/README.md)를 참고하십시오.

## 빌드와 실행

개발 환경:

- .NET 10 SDK
- Visual Studio 2022 또는 Build Tools의 **Desktop development with C++**, MSVC x64, Windows SDK
- 첫 복원 시 NuGet 연결

저장소 루트에서 PowerShell로 실행합니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test
dotnet run --project Compositor.App/Compositor.App.csproj -c Release
```

솔루션은 `Compositor.Windows.slnx`입니다. 네이티브 DLL은 C# 빌드 과정에서 자동 빌드·복사됩니다. SkiaSharp 버전은 4.152.1로 고정하며, NuGet lock 파일을 포함합니다.

사용자 PC에 .NET 설치 없이 실행할 폴더를 만들려면:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test -Publish
```

`artifacts/publish/Compositor.Windows.exe`를 실행합니다. **publish 폴더 전체**를 함께 배포해야 합니다. 아직 설치 프로그램·코드 서명·자동 업데이트는 포함하지 않습니다.

## 사용할 수 있는 기능

- 새 캔버스, PNG/JPEG 가져오기와 파일 드롭
- 레이어 추가·삭제·순서·이름·표시·불투명도
- 중첩 그룹과 접기·펼치기 계층 패널, 그룹 생성·해제·부모 이동·전체 이동, 그룹 표시·불투명도
- Normal, Multiply, Screen, Overlay, Darken, Lighten, Difference 합성
- 이동 도구, 숫자 기반 크기·회전·위치 조정, 가로·세로 뒤집기
- 크기·경도·불투명도·RGB 색상을 지정하는 브러시와 지우개
- 256×256 불변 타일 기반 Undo/Redo; 드래그·스트로크당 한 번의 히스토리. 변화 없는 이동·속성 적용은 이력을 만들지 않으며 Redo를 유지합니다. 이력은 최대 100단계 및 현재 문서 외 고유 타일 256MiB로 제한합니다.
- 픽셀 레이어 색상 오버레이 효과: RGB·효과 불투명도·활성화, 미리보기·제거·Undo/Redo, 마스크 적용 후 합성, 원본과 별도 저장
- 비파괴 Exposure·Levels·Curves·Invert·Black & White·Color Balance·Grain 조정 레이어: 노출·오프셋·감마 및 RGB/개별 채널 Levels·곡선, 미리보기·재편집·Undo/Redo, 합성 모드·그룹 불투명도·연결 마스크, 저장 후 설정 복원
- 이미지 밝기·대비·채도 조정: 미리보기, 원본 비교, 초기화, 적용·취소 및 Undo/Redo. 조정된 픽셀을 저장하며 독립 조정 레이어는 아닙니다.
- 레이어에 연결된 회색조 마스크: 전체 표시/숨김 추가, 활성화·삭제, 마스크 브러시·지우개, Undo/Redo
- `.comp` 프로젝트 폴더 저장·열기, PNG 투명도 보존 내보내기, 흰 배경 JPEG 내보내기(품질 92)
- PNG/JPEG 가져오기의 EXIF 방향 보정과 sRGB 변환, 내보내기 해상도 메타데이터
- 포인터 기준 휠 확대/축소, 중간 버튼 드래그 또는 Hand 도구로 이동

메뉴에 표시된 Ctrl 단축키를 사용합니다. V/B/E/H는 이동/브러시/지우개/Hand입니다. Esc는 진행 중인 편집과 Hand 이동을 취소합니다. 창 비활성화·마우스 캡처 상실도 미완료 편집을 되돌립니다. 드래그 중 휠 확대/축소는 무시합니다. 텍스트 필드에 포커스가 있으면 텍스트 편집 단축키를 우선합니다. 레이어 속성은 **Apply properties**로 적용합니다.

왼쪽 세로 레일의 **Move / Brush / Eraser / Hand** 아이콘으로 도구를 선택합니다. 상단에는 도구별 슬라이더·숫자 옵션, 오른쪽에는 합성 모드·불투명도·레이어 목록을 배치했습니다. 상세 설정은 **Selected layer**를 펼치며 레이어 속성은 **Apply properties**로 적용합니다. 색상 견본에서는 색상 영역·세로 색조 막대·RGB/HEX·팔레트로 색을 고릅니다. UI 강조색은 차분한 블루이며 그림의 색상과 독립적입니다. [공식 화면 분석과 UI 사용법](docs/tool-controls.md)을 참고하십시오.

마스크는 오른쪽 **LAYER MASK**의 **+ Reveal**(전체 표시) 또는 **+ Hide**(전체 숨김)로 추가합니다. **Edit mask**를 선택하고 **Mask gray %**를 0으로 칠하면 숨기고, 100으로 칠하면 다시 표시합니다. 중간 값과 브러시 불투명도는 부분 표시입니다. 마스크에서 지우개는 검정으로 칠해 숨깁니다. **Edit image**로 원본 편집을 선택하며, 마스크를 끄거나 제거해도 원본 픽셀은 유지됩니다. Move와 레이어 변환은 연결된 마스크도 함께 움직입니다. 크기·위치·회전 입력은 **Position, size and rotation**을 펼칩니다.

그룹은 **+ Group**으로 만들거나 **Group**으로 선택 항목을 감쌉니다. 그룹을 선택한 상태에서 새 레이어나 이미지를 추가하면 그 안에 들어갑니다. **Move into a group**을 펼쳐 부모를 선택한 뒤 **Move here**를 누르며, **Move out**은 한 단계 밖으로 이동합니다. ↑/↓는 같은 부모 안에서 순서를 바꿉니다. **Ungroup**은 자식을 유지하고 표시·불투명도 효과를 자식에 반영합니다. 그룹 삭제(−/Delete)는 자식도 함께 삭제하며 Undo로 복원할 수 있습니다.

이미지 색상은 **Image → Adjust colors…** 또는 오른쪽 **Adjust image colors…**에서 조정합니다. 세 값은 −100~100이며 0은 변화 없음입니다. **Preview changes**를 끄면 원본과 비교하고, **Reset**은 세 값을 초기화합니다. **Apply**는 한 번의 Undo로 묶이며 **Cancel/Esc/창 닫기**는 원본을 유지합니다. 그룹이나 **Edit mask**가 선택돼 있으면 색상 조정을 사용할 수 없습니다. 저장 후 다시 열면 조정값을 재편집하거나 이전 픽셀로 Undo할 수 없습니다.

**Image → New Exposure adjustment layer…**는 선택한 일반 레이어 바로 위에 Exposure 조정 레이어를 만듭니다. 그룹 선택 시 그 그룹의 첫 자식으로 추가합니다. 선택 후 **Edit Exposure…**에서 노출·오프셋·감마를 다시 편집할 수 있습니다. 아래에 보이는 합성 결과를 조정하며 위 레이어와 원본 이미지 픽셀은 변경하지 않습니다. 연결 마스크는 적용 범위를 제한하고, 저장 후에도 설정을 다시 편집할 수 있습니다. 새 조정 창에서 Cancel/Esc로 닫으면 레이어 생성도 취소합니다.

**Image → New Levels adjustment layer…**에서 RGB·Red·Green·Blue의 입력 검정/흰색, 감마, 출력 검정/흰색을 조절합니다. **Edit Levels…**에서 재편집하며, 개별 색 채널 계산 후 RGB 계산을 적용합니다. 채널별 값은 전환 중에도 유지됩니다. Reset channel은 현재 채널, Reset all은 전체를 초기화합니다. 히스토그램·자동 레벨·스포이트는 아직 제공하지 않습니다.

**Effects → Color overlay…**에서 선택한 픽셀 레이어에 색을 덧입힙니다. RGB와 효과 불투명도는 0~1로 입력하며, 활성화·원본 비교·제거·Undo/Redo를 지원합니다. 원본과 마스크를 보존하고 저장 후 재편집할 수 있습니다. Mac과 같은 source-over 합성이므로 반투명 경계는 더 불투명해질 수 있습니다.

**Image → New Curves adjustment layer…**에서 RGB·개별 채널 곡선을 편집합니다. 그래프를 클릭해 점을 추가·선택하고 끌어서 이동하거나 Input/Output 숫자를 입력합니다. 채널당 2~32점, 내부 점 삭제, 채널/전체 초기화, 미리보기 비교를 지원합니다. **Edit Curves…**로 저장 후에도 다시 편집할 수 있습니다.

## 프로젝트 호환성과 저장 보호

`.comp`는 `manifest.json`과 `images/<UUID>.png`를 담은 **폴더**입니다. Open project folder에서 `.comp` 폴더 자체를 선택합니다. Save as는 상위 폴더와 새 프로젝트 이름을 차례로 지정합니다.

- 버전 1–8을 읽고 버전 8로 저장합니다. CGPoint/CGSize는 Swift Codable과 같은 2원소 숫자 배열이며, 좌표는 왼쪽 위 원점·시계 방향 회전입니다.
- 기본 픽셀 레이어, 통과 방식 그룹, 연결된 회색조 마스크와 지원 범위의 7종 조정 레이어와 일반 픽셀 레이어의 Color Overlay·Drop Shadow·Stroke·Inner Shadow·Outer Glow 효과를 지원합니다. 그룹 마스크, 분리 이동·독립 배치·다른 레이어 참조 마스크, Hue/Saturation·Gradient Map 등 미지원 조정 또는 보존할 수 없는 비활성 조정 설정, 미지원 효과와 그룹·조정 레이어의 효과, 도형, 텍스트, 가이드, 미지원 합성 모드 및 알 수 없는 필드는 **프로젝트 전체 열기를 거부**합니다. 기능을 조용히 버리고 덮어쓰지 않습니다.
- 자산 경로·중복 ID·형식 버전·크기·심볼릭 링크와 junction을 검사합니다. 디스크 자산은 외부 원본 이미지에 의존하지 않습니다.
- 완전한 임시 패키지를 쓰고 다시 읽어 검증한 다음 기존 폴더를 `.comp.recovery`로 이동하고 새 폴더를 게시합니다. 게시 실패 시 기존 폴더를 복구합니다. 같은 경로의 동시 저장은 `.write-lock`으로 차단합니다.
- **폴더 두 번의 이름 변경 전체가 하나의 원자적 연산은 아닙니다.** 전원 중단 시 `.comp.recovery`가 남을 수 있습니다. File → **Open recovery copy…**에서 `.comp.recovery` 폴더를 선택하면 수정된 새 문서로 열립니다. Save에서 새 `.comp` 이름으로 저장하십시오. 원본과 복구 폴더는 자동으로 덮어쓰거나 삭제하지 않습니다. 복구 사본이 있으면 다음 저장은 차단됩니다. 잠금 파일은 빈 파일로 남으며, 실제 잠금은 프로세스의 파일 핸들입니다.
- 캔버스/이미지 30,000px/변 및 100MP 한도, 총 소스 100MP와 별도 마스크 합계 100MP, 레이어 10,000개, manifest 4MiB, 자산당 512MiB를 적용합니다. 대형 프로젝트에서는 상당한 RAM이 필요합니다.

저장 필드와 읽기 제한은 [Windows 프로젝트 규격](docs/project-format.md)에 정리했습니다. 실제 Mac에서 생성한 대표 문서의 양방향 열기는 아직 검증하지 않았습니다. 현재 호환성 근거는 원본 코드, Swift 형태의 명시적 fixture, 저장 왕복 테스트입니다.

## 구조

```text
Compositor.App         WPF UI, 입력, 대화상자
Compositor.Core        UI 독립 문서·변환·불변 RGBA 타일·히스토리
Compositor.Imaging     Skia 합성·증분 화면 캐시·코덱·.comp·브러시
native                 Windows 고정 크기 ABI/브러시와 독립 C 커널 사본
Compositor.Tests       픽셀·히스토리·입출력·실패 복구 회귀 테스트
Compositor.Benchmarks  반복 가능한 4K 브러시·마스크·그룹·색상 조정 측정
docs                   Windows 규격·계획·검증 문서
macOS                  최초 클론한 macOS 소스·빌드·문서
```

`native/kernels/`의 C/H 파일은 macOS 커널 8개를 독립 복사한 Windows 전용 소스입니다. 현재 내용은 원본과 같으며, Windows 빌드는 `macOS/`의 소스를 참조하지 않습니다. `long` 기반 원본 함수는 직접 노출하지 않으며, 공개 DLL 경계는 `int32_t`를 사용합니다. 원본 alpha-bounds와 premultiplied clamp 함수 실행을 테스트하며, 이미지 가져오기에서 clamp를 사용합니다. Exposure·Levels·Curves는 원본 `levels_apply` C 커널로 모든 알파/채널 조합의 조회표를 계산해 재사용합니다.

렌더러는 premultiplied RGBA8/sRGB와 top-down 행을 사용합니다. 필터링 타일 가장자리에 이웃 픽셀을 넣어 경계 이음새를 방지합니다. 화면은 수정된 타일 영역을 다시 합성합니다. 임의 각도(90도 배수 제외)로 회전된 레이어가 있으면 부분 클립의 픽셀 오차를 피하려고 전체 뷰포트를 다시 합성하며, 내보내기는 같은 합성기를 전체 해상도에서 실행합니다. Exposure·Levels·Curves가 있는 문서는 문서 해상도로 전체 합성한 이미지를 캐시합니다. 조정/하위 이미지/마스크 변경 시 전체를 다시 합성하고, 확대·이동 시에는 그 이미지를 최근접 샘플링으로 표시합니다. UI와 내보내기는 모두 CPU 경로이며 GPU 가속을 주장하지 않습니다.

## 검증과 현재 제한

```powershell
dotnet test Compositor.Tests/Compositor.Tests.csproj -c Release
dotnet run --project Compositor.Benchmarks/Compositor.Benchmarks.csproj -c Release -- artifacts/benchmark.json
```

벤치마크는 4000×4000 문서, 800px/0% 경도 브러시, 빈 레이어와 불투명 레이어, 각 120회 입력의 스트로크 2개를 측정합니다. 1000×1000 Skia 화면 합성을 포함하되 WPF 표시 지연이나 input-to-photon 측정은 아닙니다. 초기 내부 기준은 p95 33.3ms 이하, 스트로크 종료 100ms 이하입니다. 장비와 부하에 따라 달라집니다.

`build.ps1 -Test`는 숨김 WPF 창에서 입력 취소·혼합 버튼·PNG/JPEG 가져오기·브러시/지우개·Undo/Redo·저장/재열기·복구·종료 확인 경로를 실행하고 `artifacts/ui-smoke.png`와 `artifacts/ui-smoke.checks.json`을 만듭니다. 실제 포인터 입력 장치, 압력, 여러 모니터 DPI 전환, 접근성은 별도의 실기 검증이 필요합니다.

다중 레이어와 반복 편집 검증은 `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Test -Stability`로 실행합니다. 결과는 `artifacts/stability-benchmark.json`에 저장됩니다. [실사용 체크리스트](docs/usability-checklist.md)와 [안정화 검증 결과](docs/windows-stability-validation.md)를 참고하십시오.

현재 제한:

- 브러시는 연속 원형 경로의 최대 커버리지 방식입니다. 입력 빈도와 반복 flush에 독립적이지만, 원본 Mac의 optical-density soft brush와 교차부 농도가 다릅니다. 압력·경로 smoothing은 미지원입니다.
- 브러시는 현재 레이어의 소스 영역과 캔버스 안에서 동작합니다. 레이어 밖으로 자동 확장하지 않습니다.
- Nearest 외 Smooth/High는 현재 bilinear 샘플링입니다. Mac의 고품질 축소와 동일한 필터는 아닙니다.
- 연결된 마스크는 원본과 같은 픽셀 크기 또는 균일한 1×1만 지원합니다. 내부 회색조 값은 RGBA 타일에 담고 파일은 8비트 회색조 PNG로 저장합니다. Mac 앱에서의 실제 양방향 열기와 동일 필터 결과는 아직 검증하지 않았습니다.
- 그룹은 자식별로 배경과 합성하고 그룹 불투명도를 곱합니다. 그룹을 하나의 이미지로 합성하는 모드, 그룹 마스크·단위 회전/크기 변경, 다중 선택·드래그로 계층 이동은 미지원입니다. 그룹 전체 이동은 Move 도구, 계층 이동은 부모 선택을 사용합니다.
- Exposure·Levels·Curves·Invert·Black & White를 지원하며, 다른 종류의 비파괴 조정은 아직 지원하지 않습니다. 조정 레이어가 있는 문서의 편집은 전체 문서 해상도 합성 비용이 발생합니다. 선택·크롭·다중 문서 탭·PSD는 후속 구현 대상입니다. RAW·AI·편집 가능한 텍스트·자동 업데이트도 이번 MVP에 포함하지 않습니다.
- GPU, 설치/서명, 전체 Mac 기능 동등성은 아직 구현하지 않았습니다.

마스크 구현·성능 결과와 사용법은 [마스크 검증 문서](docs/layer-masks.md)에 있습니다. 마스크 성능 측정은 `dotnet run --project Compositor.Benchmarks/Compositor.Benchmarks.csproj -c Release -- --masks artifacts/mask-benchmark.json`으로 실행합니다.

그룹 사용법·호환성·중첩 성능은 [그룹 검증 문서](docs/layer-groups.md)에 있습니다. `build.ps1 -Test`에 그룹 WPF 검증도 포함되며 `artifacts/ui-smoke.groups.json`에 결과를 남깁니다.

기본 색상 조정의 사용법·수식·성능·저장 제한은 [색상 조정 검증 문서](docs/color-adjustments.md)에 있습니다. `build.ps1 -Test`에 조정 대화상자 검증이 포함되며 `artifacts/ui-smoke.adjustments.json`에 결과를 남깁니다.

Exposure 구현·파일 호환 범위·성능은 [Exposure 검증 문서](docs/exposure-adjustment.md)에 있습니다. `build.ps1 -Test`는 Exposure WPF 검증과 `artifacts/ui-smoke.exposure.json`도 생성합니다. [Levels 검증 문서](docs/levels-adjustment.md)는 채널별 계산·저장·4K 성능을 설명하며, 같은 명령으로 `artifacts/ui-smoke.levels.json`도 생성합니다.

[Curves 검증 문서](docs/curves-adjustment.md)에 보간·그래프 편집·호환 범위·성능을 정리했습니다. `build.ps1 -Test`는 `artifacts/ui-smoke.curves.json`도 생성합니다.

[색상 오버레이 검증 문서](docs/color-overlay.md)에 적용 순서·캐시·호환 범위·4K/브러시 성능을 정리했습니다. `build.ps1 -Test`는 `artifacts/ui-smoke.overlay.json`도 생성합니다.

현재 상태는 **핵심 MVP 기능 개발 완료, 검증 96%, 외부 검증 4% 보류**입니다. 2026-10-06 사용자 결정에 따라 원본의 다섯 레이어 효과를 연결했으며 추가 조정·선택 도구를 이어서 구현합니다. `powershell -NoProfile -ExecutionPolicy Bypass -File verify-mvp.ps1`로 회귀·실제 저장 프로세스 중단·1,200회 반복 편집·4K 성능·복사한 배포본 WPF 검사를 실행합니다. 최신 결과와 실제 Mac·마우스·다중 DPI·별도 Windows PC의 잔여 절차는 [MVP 완료 판정](docs/mvp-acceptance.md)에 있습니다. 실제 Mac 왕복과 별도 Windows PC 검증은 장비 확보 후 재개하며, 보류 중에는 그림자 등 효과 확대와 후속 편집 기능을 진행합니다.

그림자 기능과 성능 제한은 [Drop Shadow](docs/drop-shadow.md), 전체 후속 작업은 [Windows 전환 완료 작업표](docs/windows-completion-plan.md)에 기록합니다.

외곽선 사용법과 검증은 [Stroke](docs/stroke-effect.md)에 기록합니다.

안쪽 그림자와 외부 광선은 [Soft effects](docs/soft-effects.md)에 기록합니다. 큰 문서의 효과 재계산 지연은 성능 개선 대상입니다.

반전·흑백 비파괴 조정의 사용법과 검증은 [Invert / Black & White](docs/blackwhite-invert.md)에 있습니다.


색상 균형·그레인의 사용법, 검증 및 4K 성능 잔여는 [Color Balance / Grain](docs/color-balance-grain.md)에 있습니다.
