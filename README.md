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
- Normal, Multiply, Screen, Overlay, Darken, Lighten, Difference 합성
- 이동 도구, 숫자 기반 크기·회전·위치 조정, 가로·세로 뒤집기
- 크기·경도·불투명도·RGB 색상을 지정하는 브러시와 지우개
- 256×256 불변 타일 기반 Undo/Redo; 드래그·스트로크당 한 번의 히스토리
- `.comp` 프로젝트 폴더 저장·열기, PNG 투명도 보존 내보내기, 흰 배경 JPEG 내보내기(품질 92)
- PNG/JPEG 가져오기의 EXIF 방향 보정과 sRGB 변환, 내보내기 해상도 메타데이터
- 포인터 기준 휠 확대/축소, 중간 버튼 드래그 또는 Hand 도구로 이동

메뉴에 표시된 Ctrl 단축키를 사용합니다. V/B/E/H는 이동/브러시/지우개/Hand입니다. Esc는 진행 중인 편집을 취소합니다. 텍스트 필드에 포커스가 있으면 텍스트 편집 단축키를 우선합니다. 레이어 속성은 **Apply layer properties**로 적용합니다.

## 프로젝트 호환성과 저장 보호

`.comp`는 `manifest.json`과 `images/<UUID>.png`를 담은 **폴더**입니다. Open project folder에서 `.comp` 폴더 자체를 선택합니다. Save as는 상위 폴더와 새 프로젝트 이름을 차례로 지정합니다.

- 버전 1–8을 읽고 버전 8로 저장합니다. CGPoint/CGSize는 Swift Codable과 같은 2원소 숫자 배열이며, 좌표는 왼쪽 위 원점·시계 방향 회전입니다.
- 기본 픽셀 레이어만 지원합니다. 그룹, 마스크, 조정, 효과, 도형, 텍스트, 가이드, 미지원 합성 모드 및 알 수 없는 필드는 **프로젝트 전체 열기를 거부**합니다. 기능을 조용히 버리고 덮어쓰지 않습니다.
- 자산 경로·중복 ID·형식 버전·크기·심볼릭 링크와 junction을 검사합니다. 디스크 자산은 외부 원본 이미지에 의존하지 않습니다.
- 완전한 임시 패키지를 쓰고 다시 읽어 검증한 다음 기존 폴더를 `.comp.recovery`로 이동하고 새 폴더를 게시합니다. 게시 실패 시 기존 폴더를 복구합니다. 같은 경로의 동시 저장은 `.write-lock`으로 차단합니다.
- **폴더 두 번의 이름 변경 전체가 하나의 원자적 연산은 아닙니다.** 전원 중단 시 `.comp.recovery`가 남을 수 있습니다. 원본 또는 복구 폴더를 열어 내용을 확인한 뒤 보존할 사본을 결정하십시오. 복구 사본이 있으면 다음 저장은 차단됩니다. 잠금 파일은 빈 파일로 남으며, 실제 잠금은 프로세스의 파일 핸들입니다.
- 캔버스/이미지 30,000px/변 및 100MP 한도, 총 소스 100MP, 레이어 10,000개, manifest 4MiB, 자산당 512MiB를 적용합니다. 대형 프로젝트에서는 상당한 RAM이 필요합니다.

저장 필드와 읽기 제한은 [Windows 프로젝트 규격](docs/project-format.md)에 정리했습니다. 실제 Mac에서 생성한 대표 문서의 양방향 열기는 아직 검증하지 않았습니다. 현재 호환성 근거는 원본 코드, Swift 형태의 명시적 fixture, 저장 왕복 테스트입니다.

## 구조

```text
Compositor.App         WPF UI, 입력, 대화상자
Compositor.Core        UI 독립 문서·변환·불변 RGBA 타일·히스토리
Compositor.Imaging     Skia 합성·증분 화면 캐시·코덱·.comp·브러시
native                 Windows 고정 크기 ABI/브러시와 독립 C 커널 사본
Compositor.Tests       픽셀·히스토리·입출력·실패 복구 회귀 테스트
Compositor.Benchmarks  반복 가능한 4K 브러시 측정
docs                   Windows 규격·계획·검증 문서
macOS                  최초 클론한 macOS 소스·빌드·문서
```

`native/kernels/`의 C/H 파일은 macOS 커널 8개를 독립 복사한 Windows 전용 소스입니다. 현재 내용은 원본과 같으며, Windows 빌드는 `macOS/`의 소스를 참조하지 않습니다. `long` 기반 원본 함수는 직접 노출하지 않으며, 공개 DLL 경계는 `int32_t`를 사용합니다. 원본 alpha-bounds와 premultiplied clamp 함수 실행을 테스트하며, 이미지 가져오기에서 clamp를 사용합니다.

렌더러는 premultiplied RGBA8/sRGB와 top-down 행을 사용합니다. 필터링 타일 가장자리에 이웃 픽셀을 넣어 경계 이음새를 방지합니다. 화면은 수정된 타일 영역만 다시 합성하고, 내보내기는 같은 합성기를 전체 해상도에서 실행합니다. UI와 내보내기는 모두 CPU 경로이며 GPU 가속을 주장하지 않습니다.

## 검증과 현재 제한

```powershell
dotnet test Compositor.Tests/Compositor.Tests.csproj -c Release
dotnet run --project Compositor.Benchmarks/Compositor.Benchmarks.csproj -c Release -- artifacts/benchmark.json
```

벤치마크는 4000×4000 문서, 800px/0% 경도 브러시, 빈 레이어와 불투명 레이어, 각 120회 입력의 스트로크 2개를 측정합니다. 1000×1000 Skia 화면 합성을 포함하되 WPF 표시 지연이나 input-to-photon 측정은 아닙니다. 초기 내부 기준은 p95 33.3ms 이하, 스트로크 종료 100ms 이하입니다. 장비와 부하에 따라 달라집니다.

`build.ps1 -Test`는 숨김 WPF 창에서 브러시·Undo/Redo·속성·이동을 실행하고 `artifacts/ui-smoke.png`를 만듭니다. 실제 포인터 입력 장치, 압력, 여러 모니터 DPI 전환, 접근성은 별도의 실기 검증이 필요합니다.

현재 제한:

- 브러시는 연속 원형 경로의 최대 커버리지 방식입니다. 입력 빈도와 반복 flush에 독립적이지만, 원본 Mac의 optical-density soft brush와 교차부 농도가 다릅니다. 압력·경로 smoothing은 미지원입니다.
- 브러시는 현재 레이어의 소스 영역과 캔버스 안에서 동작합니다. 레이어 밖으로 자동 확장하지 않습니다.
- Nearest 외 Smooth/High는 현재 bilinear 샘플링입니다. Mac의 고품질 축소와 동일한 필터는 아닙니다.
- 선택·크롭·다중 문서 탭, 마스크·그룹·조정·효과·PSD는 후속 구현 대상입니다. RAW·AI·편집 가능한 텍스트·자동 업데이트도 이번 MVP에 포함하지 않습니다.
- GPU, 설치/서명, 전체 Mac 기능 동등성은 아직 구현하지 않았습니다.

후속 순서는 마스크 → 그룹 → 조정 → 효과 → 제한적 PSD 가져오기입니다. 각 단계는 저장 규격과 회귀 테스트를 함께 확장해야 합니다.
