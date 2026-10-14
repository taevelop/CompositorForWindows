# Magic Wand 전환

원본 MagicWand.swift 및 WandPixels.c/.h 대조. 기본 Tolerance 32(0–255), Point/3×3/5×5 평균(radius 0/1/2), Contiguous true, Sample All Layers false다. RGBA premultiplied의 모든 채널(알파 포함)을 기준색과 비교한다. 평균은 이미지 경계에서 clip하고 정수 반올림한다. Contiguous는 seed를 포함한 4-connected run fill이며 seed가 평균색과 맞지 않으면 비어 있다.

원본 C 파일과 Windows 커널 SHA256은 BF0783B4DF7373B060C966E88CF6291AC177C313CD4B39CDD91FDD0C168762DB로 동일하다. Windows bridge는 고정 폭 int32 dimensions/settings 및 int64 count를 내보내며 원본 long/size_t는 내부에 둔다. 허용 100MP count는 Windows long 범위에 들어간다. native outline points/loop counts는 원본 C allocator에서 해제한다. 취소/실패에도 finally에서 버퍼를 해제한다.

MagicWand.Select 연산 기반은 원본 매칭과 native 경계 추적을 사용한다. 타일 경계 263px·contiguous/global 별도 섬·평균/seed 불일치·alpha tolerance 경계·구멍 winding·원본 불변·범위/사전 취소를 검사했다. native 8M edge 상세 제한 및 Windows 선택 SVG 길이 제한을 유지하며 복잡도 초과는 오류로 원본을 보존한다.

아직 사용자 도구 완료가 아니다. active-layer 원본 이미지를 캔버스 좌표로 렌더링하는 sample 경로(마스크/효과를 제외하는 원본 규칙), all-visible composite, 문서 선택 결합/Undo, W 도구·Tolerance slider/sample/contiguous UI, click/최신 결과/취소·대형 성능·배포 검증은 후속이다. C match/trace 내부 중간 취소는 아직 없고 호출 전후/managed 루프에서 검사한다. 실제 Mac 및 별도 Windows PC 검증은 보류한다.

Release 경고/오류0·코어650개·기존WPF회귀통과. Magic Wand UI 자체는 아직 미연결이다.
