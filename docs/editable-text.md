# 편집 가능한 텍스트 전환

원본 기준은 `macOS/Compositor/Document/TypeTool.swift`, `Rendering/InlineTextEditor.swift`, `IO/ProjectStore.swift`다. 2026-10-14 현재 모델·저장·글꼴 해결·렌더러·텍스트 편집 명령 기반이 연결됐으며, Type/inline 편집 UI는 아직 없다.

## 모델과 프로젝트

LayerTextStyle은 content/fontName/fontSize/RGB/alignment/tracking/leading/optional boxSize를 보존한다. 원본과 같은 범위는 UTF-16 내용 최대 100000, 글꼴 크기 1–2000, RGB 0–1, 자간 -100–1000, 행간 0–5000, 문단 상자 16–30000 및 100MP다. 자동 행간은 글꼴 크기 × 1.2, 여백은 12픽셀이다. .NET 문자열 길이는 UTF-16 단위이므로 이모지도 같은 방식으로 센다.

Layer.Text의 원본 정보와 cached Pixels를 함께 보관한다. 새 Pixels 참조로 편집하면 Shape/Text를 지우며 동일 Pixels 참조·변환·마스크·외관 변경은 Text를 유지한다. 그룹·조정·도형과 텍스트의 공존은 거부한다. TextJson은 원본 camelCase 키와 Left/Center/Right, boxSize 배열을 읽고 쓴다. 중복·미지원 필드와 손상된 범위는 거부하며 imageFile 없는 텍스트도 PNG 디코딩 전에 거부한다. boxSize 생략은 포인트 텍스트, leading 생략은 자동 행간이다.

.comp는 버전 8 JSON+PNG 디렉터리 형식을 유지한다. 열 때 자동으로 텍스트를 렌더링하지 않으므로 Mac 글꼴이 없어도 저장된 외관을 유지한다. 요청한 fontName도 대체 이름으로 덮어쓰지 않는다. 저장된 PNG 보존은 Mac의 새 텍스트 렌더링과 동등하다는 증거가 아니다.

## 설치 글꼴 해결

OpenTypeFontNames는 SFNT(TTF/OTF) 및 TTC name table의 family/subfamily/full-name/PostScript/typographic family·face(ID 1/2/4/6/16/17)를 수집한다. Unicode big-endian(platform 0/3)과 Mac platform 1의 ASCII 이름을 읽는다. 비ASCII MacRoman은 근사 디코딩하지 않는다. 표·문자 offset/길이, UTF-16 짝수 길이, 컬렉션 크기를 검사하며 글꼴을 프로젝트에 복사하지 않는다.

TextFontResolver는 설치 family와 지역화 이름을 먼저 조회한다. 나머지 이름은 GlyphTypeface.GetFontStream으로 만든 full-name/PostScript 별칭 인덱스로 해결한다. TTC의 다른 face를 혼동하지 않도록 family와 face가 현재 GlyphTypeface와 일치할 때만 연결한다. weight/style/stretch도 유지한다. 없으면 시스템 글꼴을 사용하고 MissingFont와 RequestedFont를 반환한다. 손상된 글꼴은 별칭 조회에서 건너뛴다. 인덱스는 Lazy로 한 번 생성해 동시 요청이 부분 인덱스를 읽지 않도록 한다.

남음: 사용자에게 부족한 글꼴 안내, 글꼴 설치 변경 시 갱신, 큰 카탈로그 생성의 취소·성능, variation named instances/FVAR, 모든 지역화 이름과 컬렉션의 동등성.

## DirectWrite·Direct2D 렌더러

초기 WPF FormattedText 기반 렌더러는 자간을 거부하고 이모지를 단색으로 출력했다. 현재 TextRasterizer는 `native/text_raster.cpp`의 DirectWrite 배치와 Direct2D 컬러 글꼴 렌더링을 사용한다. UTF-16, 글꼴 family/weight/style/stretch, 자간·행간·정렬·상자와 RGB를 전달하고 UI 독립 Raster를 반환한다. 기존 C 커널은 C17로 유지하며 C++20 객체를 별도로 컴파일해 같은 DLL에 링크한다. 기존 export 및 ABI는 유지한다.

- 자간은 IDWriteTextLayout1.SetCharacterSpacing의 trailingSpacing으로 적용한다. 결합 문자·이모지 시퀀스를 문자열 단위로 분리해 그리는 근사를 사용하지 않는다. 최소 advance는 API가 허용하는 0이다. 아주 큰 음수 자간의 advance 제한과 원본 AppKit kern 동등성은 남은 비교 항목이다.
- 자동/사용자 행간은 균일한 baseline 간격으로 적용하며 선택 face의 ascent를 baseline에 사용한다. 폴백 글꼴의 혼합 행간 및 극단적으로 좁은 행간 품질은 추가 비교가 필요하다.
- Left/Center/Right와 문단 줄바꿈을 적용한다. 포인트 텍스트는 측정 폭 + 여백 24 + caret 여유 글꼴 크기 × 0.1, 최소 크기 16을 사용한다. 고정 상자는 올림한 크기를 유지하며 내부 영역을 클립한다. 30000픽셀 한 변·100MP 제한을 출력 메모리 할당 전에 검사한다.
- D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT로 컬러 이모지를 활성화한다. 96 DPI의 WIC 소프트웨어 비트맵에 회색조 안티앨리어싱으로 그린다. 투명 배경의 premultiplied BGRA8을 top-down RGBA8로 교환해 기존 불변 256픽셀 타일 Raster로 변환한다. GPU 가속 경로가 아니다.
- COM은 각 호출 스레드에서 초기화하며 이미 STA인 UI 스레드와 MTA 작업 스레드 모두 지원한다. 취소는 측정/렌더 전후와 행·타일 변환에서 검사한다. DirectWrite 배치나 Direct2D 렌더 호출 내부의 중간 취소는 아직 없다. 측정과 렌더를 두 번 수행하므로 대형 텍스트 메모리·시간 최적화는 남아 있다.

API 기준: Microsoft [문자 간격](https://learn.microsoft.com/en-us/windows/win32/api/dwrite_1/nf-dwrite_1-idwritetextlayout1-setcharacterspacing), [컬러 글꼴](https://learn.microsoft.com/en-us/windows/win32/directwrite/color-fonts), [OpenType name table](https://learn.microsoft.com/en-us/typography/opentype/otspec182/name), [컬렉션 offset](https://learn.microsoft.com/en-us/typography/opentype/otspec181/otff).

## 검증

렌더러 단계의 제품 코어 720개 검사 통과: 텍스트 Unicode/부족한 글꼴/style/cached pixels 저장 왕복, 변환·마스크·동일 픽셀 정보 유지, 픽셀 편집의 래스터화 및 Undo/Redo, 손상 metadata의 열기/덮어쓰기 거부와 원본 보존, SFNT/TTC 이름 및 잘못된 offset/길이 거부.

실제 `--text-render-smoke` 결과는 `artifacts/text-directwrite-final.json`이다. Latin/한글/아랍어/이모지/결합 문자/다중 행 출력과 premultiplied 알파, 문단 정렬·줄바꿈·고정 크기, 행간 24/60, 부족한 글꼴 표시, 빈 내용·사전 취소, 양수 12/음수 -4 자간, 컬러 이모지 및 Segoe UI에서의 이모지 폴백, STA/MTA 픽셀 일치, 너무 큰 포인트 텍스트의 사전 거부, 자간·이모지 metadata/PNG 왕복을 통과했다. ArialMT/Arial-BoldMT/Arial Bold와 설치 TTC face의 이름·스타일 검사도 유지한다. 굵은 face의 실제 알파 면적 증가 및 사용자 행간 1의 겹침도 검사했다. WPF에서 해결한 family가 DirectWrite 목록에 없으면 다른 글꼴로 조용히 치환하지 않고 오류를 반환한다.

검은 텍스트색으로 출력한 이모지에서 컬러 픽셀 4663개를 확인했고 PNG를 시각 확인했다. 아랍어 PNG도 확인했으나 모든 언어·복합 시퀀스의 shaping 정확성이나 Mac 픽셀 동등성을 입증하지 않는다. Release 빌드 경고·오류는 0개다. 일반 WPF 회귀는 첫 실행이 30초 제한에서 시간 초과됐다. 단계 기록의 마지막은 ToolControlsSmokeTest 시작(27.48초)이었다. 같은 제한으로 재실행한 결과는 통과했고 마지막 단계 종료 기록은 28.27초였다. 간헐적 시간 초과 원인이 해결됐다는 증거는 아니므로 안정성 잔여 항목을 유지한다.


## 텍스트 생성·재편집 명령 기반

`Compositor.Core/TextEdit.cs`의 TextDraft는 문서 ID, 선택적 기존 레이어 ID, 문서 좌표의 origin, style, 선택적 변환 초안을 보관한다. Capture는 UI 스레드에서 문서 참조·세션·선택 레이어·마스크 대상·transaction generation을 고정한다. Prepare는 불변 문서와 렌더된 Raster만 사용해 작업 스레드에서 새 문서를 만든다. Apply는 같은 편집 상태일 때만 UI 스레드에서 한 번의 Undo로 확정한다. Session.Load는 같은 문서 참조를 재사용하더라도 generation을 증가시켜 이전 준비 결과를 무효화한다.

새 텍스트는 활성 그룹 안 또는 활성 레이어의 형제 위치에 추가하고 기존 선택 윤곽을 유지한다. 이름은 공백·개행을 한 칸으로 정리한 첫 40개 grapheme이다. 빈 새 초안은 레이어를 만들지 않고, 내용과 변환이 그대로인 기존 텍스트는 다시 렌더링하지 않으므로 부족한 원본 글꼴의 cached PNG도 유지할 수 있다.

재편집은 ID·이름·외관·효과·계층을 유지한다. 새 픽셀 크기에 기존 픽셀당 배율을 적용한 뒤 회전·뒤집기를 반영한 좌상단 anchor가 움직이지 않도록 origin을 보정한다. 명시적 문단 상자 크기 조정에는 초안 변환을 그대로 사용한다. 위치가 암묵적이던 마스크는 이전 레이어 변환에 고정하고, 명시적인 마스크 위치와 연결 여부는 보존한다. 픽셀 참조를 교체한 뒤 Text metadata를 다시 설정해 텍스트 편집성을 유지한다. 손상된 대상·다른 문서·상자와 픽셀 크기 불일치·100MP/Undo 예산 초과·취소는 세션을 변경하지 않는다.

코어 733개 검사 통과: 새 그룹 내 생성·선택 보존·한 번의 Undo, 회전/뒤집기의 anchor·배율·마스크 유지, 명시적/암묵적 문단 크기 변경, 무변경·빈 초안, 다른 세션/선택/세대/재열기의 늦은 결과 거부, 취소·래스터화된 대상 거부·예산 초과, 이모지 시퀀스를 자르지 않는 이름. 실제 `--text-render-smoke`의 `artifacts/text-edit-command.json`에서는 작업 스레드 DirectWrite 렌더링 → 새 텍스트 생성 → 내용/크기/자간 재편집 → 두 번의 Undo/Redo → .comp 저장/재열기의 metadata/픽셀 일치를 통과했다.

이는 편집 명령 기반의 검증이며 사용자가 Type 도구로 직접 입력하는 기능 완료가 아니다. Type/inline 편집 UI·IME·미리보기·저장 전 확정과 새 배포본은 계속 남아 있다.
## 남은 작업

- Type(T), 클릭/드래그의 포인트·문단 텍스트 생성과 기존 레이어 재편집
- inline editor, 커서·선택·IME·키보드·확정/취소, 글꼴 선택·크기/자간/행간 슬라이더·색상표
- 편집 명령을 Type/inline UI에 연결, 상자 크기 핸들·저장 전 확정
- UI 작업 수명과 편집 초안 변경에 따른 취소·늦은 결과 폐기, 표시용 미리보기와 확정 경로 연결
- 샘플링/서체·언어·음수 자간·혼합 행간 품질, 대형 문서 피크·지연, 새로운 배포본 검증

현재 최신 배포본 publish-rail-verified에는 이 DirectWrite 변경이 없다. 실제 Mac 왕복과 별도 Windows PC 실기 검증은 사용자 지시로 보류하며 통과로 표시하지 않는다.
편집 명령 단계의 Release 빌드 경고·오류 0개, build.ps1 -Test의 코어 733개와 일반 WPF 30초 제한 검사도 통과했다. 이전 렌더러 단계의 간헐적 시간 초과는 원인이 규명되지 않았으므로 품질 작업표에 계속 남긴다.
