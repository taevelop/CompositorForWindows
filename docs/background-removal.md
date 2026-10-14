# 배경 제거 전환

2026-10-08 원본 SubjectRemoval.swift/Filters.swift 대조. Apple Vision 피사체 마스크를 얻고 Basic 또는 Advanced(refine/contrast/shift edge)로 처리한다. 최종 적용은 원본 픽셀을 보존한 레이어 마스크다. 기존 마스크와 곱셈 결합하고 선택 밖은 기존 마스크 또는 흰색을 유지한다.

Windows 후보는 ONNX Runtime C# CPU 추론이다. 공식 지원: https://onnxruntime.ai/docs/get-started/with-csharp . U²-Net 저장소 LICENSE는 Apache 2.0: https://github.com/xuebinqin/U-2-Net/blob/master/LICENSE . 모델은 저장소 README에서 가중치를 별도로 다운로드하며, 배포용 ONNX 파일의 출처·변환·해시·조건은 아직 확인하지 않았다. 코드 라이선스만으로 특정 가중치 재배포를 승인된 것으로 간주하지 않는다. 런타임/모델 설치와 실제 추론은 아직 수행하지 않았다.

SubjectMask 연산 기반: 원본 해상도 불투명 grayscale 피사체 마스크, 기존 마스크 곱셈·선택 커버리지 제한, 배치된 기존 마스크를 이미지 격자로 해석, 원본 이미지 참조 보존, 문서/Undo 예산 및 취소. 선택 내부 128×128→64, 선택 밖 128 보존과 이미지 불변/Undo/범위/취소를 검사했다.

다음: 모델/런타임 및 입력 정규화·출력 확대, 실제 추론·품질·성능·라이선스 검증, Advanced 정제와 UI/비교/취소/저장, 배치된 기존 마스크/링크·큰 이미지 회귀. 연산 기반은 배경 제거 사용자 기능 완료가 아니다. 실제 Mac 및 별도 Windows PC 검증은 보류.

Release 경고/오류 0, 코어 624개 및 기존 실제 WPF 회귀 통과. AI 추론 검증이 아니라 마스크 적용 기반 검증이다.

ONNX CPU 연결: Microsoft.ML.OnnxRuntime 1.30.0 고정 및 종속 잠금 파일 갱신. U2NetPredictor는 1×3×320×320 입력/첫 마스크 출력 검증, RGB 최대값 및 mean/std 정규화, cubic 축소/확대, min-max 마스크, 원본 해상도 opaque grayscale, RunOptions.Terminate 취소를 사용한다. rembg 공식 참조 https://raw.githubusercontent.com/danielgatis/rembg/main/rembg/sessions/u2netp.py 및 base.py를 확인했다. PIL Lanczos 대신 Skia cubic을 사용하므로 전처리/출력 보간 차이의 품질 비교는 잔여다.
검증 모델은 rembg v0.0.0 u2netp.onnx 공개 릴리스, MD5 8e83ca70e441ab06c318d82300c84806 일치, SHA256 309c8469258dda742793dce0ebea8e6dd393174f89934733ecc8b14c76f4ddd8. artifacts/models에 검증용으로 다운로드했으며 저장소/배포본에 포함하지 않았다. --subject-probe 실제 CPU 추론은 합성 320px 타원에서 199.5ms, 출력 320×320/0–255를 확인했다. 자연 이미지 품질 증거는 아니다. 기존 코어 624개/WPF 회귀 및 빌드 통과. 모델 배포/라이선스 고정·자연 이미지·취소 전용 검사·Advanced/UI·배포는 후속이다.

모델 무결성: U2NetPredictor는 검증한 u2netp SHA256을 요구하고 16MiB 크기 제한 후 동일 byte[]를 해시·로드하여 파일 교체 경합을 피한다. 다른 모델 지원은 별도 검증 후 추가한다.
자연 이미지 probe: rembg examples/plants-1.jpg(https://raw.githubusercontent.com/danielgatis/rembg/main/examples/plants-1.jpg) 검증용 다운로드. 987×1481 실제 CPU 추론 281.6ms(모델 로드/출력 인코딩 제외), mask와 cutout PNG 확인. 주요 화분 일부는 잡지만 잎/작은 식물을 많이 놓쳐 품질 통과로 처리하지 않는다. cutout의 낮은 농도 경계에 색상 이상도 보여 마스크 premultiplied 합성/PNG/뷰어 경로 조사가 필요하다. 처음 518ms 값은 출력 작업 포함이어서 순수 추론 시간으로 사용하지 않는다. 품질 문제가 해결되기 전 배경 제거 전체 완료로 계산하지 않는다. 코어 624개/기존 WPF 회귀 통과, UI/Advanced/모델 배포와 라이선스 고정은 계속 잔여다.

2026-10-14 중단 후 진단 확인: --subject-probe에 명시적 RGBA premultiplied sRGB ReadPixels와 전체 source×mask 참조 검사를 추가했다. 식물 사진 전체 합성 최대 채널 오차 0, PNG 재디코딩 오차 0. 흰 배경 JPEG도 시각 확인해 앞선 투명 뷰의 강한 색상 이상은 실제 합성/PNG 데이터 손상으로 재현되지 않았다. 원인을 특정 뷰어로 확정하지 않으며, 작은 피사체 누락과 흐린 마스크 품질은 여전히 미통과다. 처음 SKBitmap.FromImage의 암묵적 형식 비교는 오차 138이었으나 명시적 형식에서는 0이므로 진단에는 명시적 형식을 사용한다. 오차 1 초과 시 probe를 실패 처리한다.

정규 U2Net 비교(2026-10-14): rembg 공식 u2net.py의 릴리스 다운로드/MD5(https://raw.githubusercontent.com/danielgatis/rembg/main/rembg/sessions/u2net.py) 대조. MD5 60024c5c889badc19c04ad937298a77b 일치, SHA256 8d10d2f3bb75ae3b6d527c77944fc5e7dcd94b29809d47a739a7a728a912b491. 해시가 고정된 소형/정규 두 모델만 허용하며 크기 상한 192MiB. 정규 모델도 검증 artifacts에만 두고 배포하지 않는다. 동일 식물 사진 CPU 515.2ms(모델 로드 제외), 합성 및 PNG 최대 오차 0. 흰 배경 시각 비교에서 아래 화분은 개선되나 위 식물 일부는 더 누락돼 전체 품질 개선이나 제품 품질 통과로 주장하지 않는다. Advanced 정제와 모델/전처리 후보 검증은 후속이며 실제 Mac/별도 PC 보류 유지.

정규 모델 허용 후 Release 경고/오류 0, 코어 624개 및 전체 WPF 회귀 통과.

Advanced 연산 기반: 원본 GuidedMatte.swift의 두 축 running-sum box 평균과 scalar guided filter를 C#으로 구현했다. clamp 경계, variance/covariance, epsilon 1e-4 및 0–1 clamp를 유지한다. 행/열/65,536픽셀 단위 취소를 추가한다. 반경 1/4/250의 독립 2차원 평균 계산, 상수 마스크 보존·가이드 경계·범위/취소 검사가 통과했다. 큰 이미지에서 여러 float 배열이 필요하므로 피크 메모리/1400px 표시 축소/원본 해상도 적용은 후속 검증이다. Raster→gray 색공간·정규화·정제 마스크 변환·contrast/shift edge·UI는 아직 연결하지 않았다. 자연 이미지의 피사체 누락을 해결했다고 주장하지 않는다.

Release 경고/오류 0, 코어 628개 및 전체 WPF 회귀 통과.

MatteRefinement 연결: Basic은 기존 마스크 참조를 반환한다. Advanced Refine 0–40/Contrast 0–100/Shift Edge -10–10 및 기본 12/25/0을 원본에 맞췄다. Skia Gray8 가이드/마스크 변환, guided epsilon 1e-4, 미리보기 최대 1400px 정제 후 원본 크기 확대, 전체 적용은 축소 없이 처리한다. Shift Edge는 abs(edge)/2 blur와 음수 .75/양수 .25 임계값·.001 폭을, Contrast는 slope=1/max(.02,1-strength*.98)를 사용한다. 불투명 회색/타일 padding을 보존한다. grayscale 색공간 및 cubic 확대는 Apple DeviceGray/high interpolation과 실제 비교가 아직 없다. 상수 대비 참조/Basic 공유·원본 불변·범위/취소와 Shift 방향 검사를 추가했다. 자연 이미지 품질·피크 메모리·UI 및 추론 캐시 연결은 후속이다.

Release 경고/오류 0, 코어 631개 및 전체 WPF 회귀 통과. 사용자 배경 제거 패널 자체는 아직 미연결이다.

Basic UI 및 캐시 1차 연결: Filter → Remove Background는 검증된 소형/정규 ONNX 파일 선택 후 실행한다. 모델 자동 설치/배포는 아직 없다. BackgroundRemovalJob는 원본 문서/레이어에 고정된 단일 추론 마스크를 직렬 worker에서 캐시한다. 표시 전용 비교 및 전체 적용에서 같은 원본 마스크를 사용하며 이미지 픽셀을 보존한다. 실제 WPF 회귀는 주입한 고정 회색 마스크로 추론 횟수 1·원본 픽셀 보존·비교·Undo/Redo·저장 출력 왕복·닫기 취소를 검사한다. 이 검사는 실제 ONNX 이미지 품질 검증이 아니다. Advanced 연산은 Job API로 준비됐지만 사용자 설정 UI는 아직 연결하지 않았다. 모델 자동 설치·패널 시각/실제 ONNX UI/늦은 세대/메모리 및 Advanced UI는 후속이다.

최종 Release 경고/오류 0, 코어 631개 및 확장 WPF 회귀 통과.

Advanced UI 연결: Advanced edge refinement 체크, Refine 0–40/Contrast 0–100/Shift Edge -10–10 슬라이더와 정밀 입력·Reset(12/25/0)·비교. Queue에서 설정 스냅샷을 worker로 전달하고 Apply 중 설정 입력을 막는다. 표시 정제와 최종 전체 정제 모두 단일 BackgroundRemovalJob의 추론 마스크 캐시를 사용한다. 실제 WPF 고정 마스크 검사에서 Advanced 전환/Contrast 75/Refine 0 출력과 참조 합성 일치·추론 1회·Apply/Undo/Redo/저장/취소를 확인했다. artifacts/ui-smoke.filters.background.png의 블루 슬라이더·입력 정렬·버튼을 시각 확인했다. Release 경고/오류 0, 코어 631개/확장 WPF 통과. 실제 ONNX UI/대형 전체 정제의 참조·피크 메모리/모델 설치·배포·품질은 아직 남아 있다.

실제 모델 WPF 전용 검증: --background-smoke report model image 진입점을 추가해 일반 30초 smoke와 분리했다. 검증용 u2netp 및 987×1481 식물 사진에서 추론 1회, Basic/Advanced 비교·원본 불변·전체 해상도 Refine 12/Contrast 25 적용 참조 일치·단일 Undo/Redo·.comp 마스크 출력 왕복을 확인했다. 보고 artifacts/background-model-wpf.json: 전체 검증 2031ms, 누적 managed 624.6MiB, 작업 후 WS 494.3MiB. 모델 로드·미리보기·참조 계산·Apply·저장/출력 포함이며 피크 메모리/사용자 입력 지연이 아니다. 실제 패널/흰 배경 결과를 시각 확인했으나 작은 피사체 누락 및 흐린 경계가 남아 품질 미통과 유지. 검사 결과는 모델 파일 선택 다이얼로그·자동 설치·별도 PC·Mac 실기를 검증하지 않는다. 다음은 대형 정제 메모리와 취소/세대 검사, 설치·배포 및 품질 검증이다.

전용 실제 모델 검사 이후 Release 경고/오류 0, 코어 631개 및 일반 WPF 회귀 통과.

대형 guided filter 분할: 65,536픽셀 초과는 256×256 출력 단위 및 2r 주변 halo로 처리한다. 두 차례 box 평균의 영향 범위를 포함하므로 내부 경계를 이미지 가장자리로 clamp하지 않는다. 원본 전체 입력/출력 float 배열은 유지하지만 다수 중간 통계 배열은 구간 크기로 제한한다. 반경 1/12/40, 519×271 무작위 guide/mask의 전체 계산과 각 채널 2e-5 이하 차이를 검사했다. Release 경고/오류 0, 코어 634개/전체 WPF 회귀 통과.
실제 u2netp WPF 재검사(987×1481)는 추론 1회·전체 정제 참조·Undo·저장 통과. 전체 1709ms, 누적 managed 773.7MiB, 작업 후 WS 415.8MiB. 이전 2031ms/624.6MiB/494.3MiB와 각각 단 1회이므로 속도·피크 개선을 확정하지 않는다. 누적 할당은 증가했다. 중간 배열 재사용/전체 gray·RGBA 버퍼/4K와100MP 피크·취소 지연은 후속이다. 모델 누락 품질 및 설치·배포도 계속 잔여다.

분할 작업 배열 재사용: 2r halo를 포함한 최대 구간 크기로 10개 private float 배열을 한 번만 생성해 모든 구간에서 재사용한다. 동일한 실행 안에서 scratch pass와 squares/products 저장소를 slope/offset 및 평균 결과로 재사용하며 반환 출력은 독립 배열이다. 다른 호출과 배열을 공유하지 않는다. 반경별 전체 계산 비교 회귀 및 실제 모델 WPF 전체 적용/Undo/저장 검사 통과.
동일 987×1481 u2netp WPF 검사 1회: 전체 1928ms, 누적 managed 376.8MiB, 작업 후 WS 335.8MiB. 앞선 구간별 신규 배열 1709ms/773.7MiB/415.8MiB 대비 누적 할당 감소를 관측했다. 시간은 증가했고 반복/피크 측정이 아니므로 성능 전체 개선·100MP 안전성을 확정하지 않는다. 전체 mask/guide/output/gray/RGBA 버퍼와 대형 취소·설치/배포·품질은 잔여다.

Release 경고/오류 0, 코어 634개 및 일반 WPF 회귀 통과.

실행 중 취소/세대 보호: WPF injected predictor를 진입 신호까지 실행한 후 창을 닫고 worker가 토큰을 관측하도록 검사했다. 원본 문서/Undo/표시 복원 확인. 취소를 무시하고 기다리는 predictor는 문서 Load/새 Begin 뒤 해제하여 늦은 결과가 새 트랜잭션을 덮거나 취소하지 않음을 확인했다. 기존 사전 취소 검사와 구분한다. ONNX Run에서 취소 토큰 요청으로 발생한 OnnxRuntimeException은 OperationCanceledException으로 변환한다.
--subject-cancel 실제 정규 모델 검사: 세션 생성 후 320px 타원 Predict에 50ms 후 취소 요청, 약 60.1ms에 OperationCanceledException 관측·원본 바이트 보존. 준비/ORT/후처리 중 정확한 취소 단계와 모든 대형 작업 지연은 아직 미검증이다. Release 경고/오류 0, 코어 634개 및 확장 WPF 회귀 통과. 대형 피크/모델 설치·배포·피사체 품질 및 Mac/별도 PC 보류는 유지한다.

대형 분리 프로세스 검증: --background-scale model report side, 실제 u2netp/합성 타원/Advanced 12·25·0에서 추론 1회·원본 Raster 참조/최종 mask 크기·문서 Validate를 검사한다. OS PeakWorkingSet은 시작/원본 생성과 preview/full 문서 보존을 포함한 프로세스 전체 high-water다. 4K(4000²) 1회: 추론 783ms, 모델 로드/추론 포함 미리보기1852ms, 최종1149ms, 누적 managed1583.7MiB, 피크1082.6MiB. 100MP(10000²) 1회: 추론3415ms, 미리보기8145ms, 최종7184ms, 누적9525.3MiB, 피크6228.2MiB. 당시 가용 약33GiB/설치64GiB 개발 PC에서 완료됐다. 전체 해상도 100MP 실행 증거지만 자연 이미지/복합 문서·WPF 입력/지속 안정성/별도 PC 통과가 아니다. 6GiB 이상 피크는 해결된 최적화로 처리하지 않으며 전체 gray/RGBA/문서 복사/마스크 처리 개선이 잔여다. 모델 설치·배포 및 피사체 품질도 잔여다.

중복 RGBA 배열 제거: RasterBuffer.CopyTo는 기존 packed 비트맵 span을 초기화하고 불변 타일을 직접 복사한다. 부분 타일과 누락 타일·투명 영역·토큰 취소를 검사하며 U2Net 입력 및 MatteRefinement Gray8 입력에서 ToRgba 중간 byte[]를 제거했다. 코어 635개 및 전체 WPF 회귀, Release 경고/오류 0.
동일 분리 프로세스 1회 재측정: 4K 누적 managed1278.5MiB/피크1023.3MiB/최종1254ms(이전1583.7/1082.6/1149); 100MP 누적7618.0MiB/피크5965.6MiB/최종6701ms(이전9525.3/6228.2/7184). 추론1회·원본참조/마스크크기·문서 검사 통과. 큰 누적 할당을 제거했지만 피크는 여전히 약5.83GiB이며 광범위 안정성이나 속도 개선 완료로 처리하지 않는다. gray/float/최종 RGBA·마스크 복제는 여전히 남는다. 모델 설치·배포·품질 및 Mac/별도 PC 보류 유지.

모델 로컬 설치: BackgroundModelStore는 선택한 파일을 private 임시 파일로 스트리밍·192MiB 상한·SHA256 알려진 두 모델 검증·Flush 후 background.onnx로 원자 이동한다. 실패/취소는 기존 모델을 보존하고 임시 파일을 제거한다. 일회 setup은 LocalApplicationData/CompositorForWindows/Models에 설치하고 다음 실행은 설치 파일을 재사용한다. Change background model 메뉴로 교체/복구하며 설치 작업은 worker에서 수행한다. 파일 선택 UI 직접 조작/자동 다운로드는 아직 검증/구현하지 않았다. 모델 변경 전에 활성 Transform 확정 정책을 유지한다.
검증: 잘못된 모델/사전 취소 시 이전 파일·임시 파일 정리 검사, 실제 u2netp를 artifacts/installed-model-check에 설치해 합성 입력 추론 약200ms/320px/0–255 성공. 패키지에 가중치를 포함하거나 재배포한 것은 아니다. U²-Net 저장소 Apache-2.0(https://raw.githubusercontent.com/xuebinqin/U-2-Net/master/LICENSE) 및 rembg 릴리스 출처는 유지하며 자동 설치/배포에는 출처/공지·검증 파일을 연결해야 한다. 모델 품질/대형 메모리 및 Mac/별도 PC 보류 유지.

최종 Release 경고/오류 0, 코어 636개 및 전체 WPF 회귀 통과.

모델 setup 다운로드 UI: 소형(~5MB)/정규(~176MB) 다운로드 및 검증 파일 가져오기, 진행 MB·Cancel·오류 표시를 제공한다. 고정 rembg v0.0.0 HTTPS 릴리스만 다운로드하며 응답/스트리밍 크기 192MiB 상한·알려진 SHA256·원자 설치를 사용한다. 이미지 전송 없이 로컬 추론이다. 잘못된 응답 체크섬/Content-Length 초과가 이전 설치 파일을 보존하고 임시 파일을 제거하는 검사를 추가했다. 실제 앱과 같은 DownloadAsync로 소형 모델을 artifacts/downloaded-model-check에 설치해 합성 320px 추론약205ms 성공. 정규 모델의 같은 다운로드 버튼 경로·직접 클릭/다운로드 도중 취소 실기는 아직 미검증이다. 가중치는 패키지에 포함하지 않고 첫 실행에서 사용자가 선택해 받는다.

다운로드 setup 이후 Release 경고/오류 0, 코어638개 및 WPF회귀 통과. 배포본 artifacts/publish-shape-ui 갱신·portable1/1 및 다운로드한 설치 모델을 사용하는 실제 배포 앱 --background-smoke 성공: 추론1회·Advanced전체참조·Undo/저장, 전체1662ms/누적managed337.9MiB(개발PC1회). 배포본 공지에 옵션 모델 출처·상위라이선스/해시정책·가중치미포함을 반영했다. 별도PC/Mac·직접setup다운로드클릭/취소·정규버튼·모델품질과 높은100MP피크는 잔여다.
