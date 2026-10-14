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
