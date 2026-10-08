# 배경 제거 전환

2026-10-08 원본 SubjectRemoval.swift/Filters.swift 대조. Apple Vision 피사체 마스크를 얻고 Basic 또는 Advanced(refine/contrast/shift edge)로 처리한다. 최종 적용은 원본 픽셀을 보존한 레이어 마스크다. 기존 마스크와 곱셈 결합하고 선택 밖은 기존 마스크 또는 흰색을 유지한다.

Windows 후보는 ONNX Runtime C# CPU 추론이다. 공식 지원: https://onnxruntime.ai/docs/get-started/with-csharp . U²-Net 저장소 LICENSE는 Apache 2.0: https://github.com/xuebinqin/U-2-Net/blob/master/LICENSE . 모델은 저장소 README에서 가중치를 별도로 다운로드하며, 배포용 ONNX 파일의 출처·변환·해시·조건은 아직 확인하지 않았다. 코드 라이선스만으로 특정 가중치 재배포를 승인된 것으로 간주하지 않는다. 런타임/모델 설치와 실제 추론은 아직 수행하지 않았다.

SubjectMask 연산 기반: 원본 해상도 불투명 grayscale 피사체 마스크, 기존 마스크 곱셈·선택 커버리지 제한, 배치된 기존 마스크를 이미지 격자로 해석, 원본 이미지 참조 보존, 문서/Undo 예산 및 취소. 선택 내부 128×128→64, 선택 밖 128 보존과 이미지 불변/Undo/범위/취소를 검사했다.

다음: 모델/런타임 및 입력 정규화·출력 확대, 실제 추론·품질·성능·라이선스 검증, Advanced 정제와 UI/비교/취소/저장, 배치된 기존 마스크/링크·큰 이미지 회귀. 연산 기반은 배경 제거 사용자 기능 완료가 아니다. 실제 Mac 및 별도 Windows PC 검증은 보류.

Release 경고/오류 0, 코어 624개 및 기존 실제 WPF 회귀 통과. AI 추론 검증이 아니라 마스크 적용 기반 검증이다.
