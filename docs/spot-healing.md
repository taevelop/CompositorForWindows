# Spot Healing 전환

## 원본 규칙과 연산 기반

BrushStroke.swift의 SpotHealingMode 순서는 Content-Aware(0), Create Texture(1), Proximity Match(2)다. source 원본 pixels를 읽고 stroke 끝에서 heal하며, 입력 중 dark wash는 결과 계산에 사용하지 않는다. kernel 결과는 coverage×opacity로 원본과 섞고 마지막에 selection clip을 적용한다. 검색 영역은 painted bounds를 (max(width,height)+32)×3.2만큼 확장하고 이미지 경계로 clip한다.

HealPixels.c SHA256은 macOS/Windows 모두 098D25186D80A0C923019393AC4FC398EEDDDF5EEF7103A890636C81B56318AE다. 고정폭 int32 dimensions/mode, uint32 seed, float opacity bridge를 추가했다. C 내부 long는 MSVC32bit이며 허용100MP count/stride에 들어간다. 원본 커널은 변경하지 않았다.

SpotHealing.Apply는 premultiplied RGBA Raster와 동일격자의 gray coverage를 받는다. 원본과 동일한 ROI를 찾아 tile에서 ROI buffer로 복사하고 native kernel을 호출한 뒤 변경tile만 교체한다. 비변경tile과 원본snapshot을 유지한다. native할당실패는 오류로 반환하며 caller의 pixels/coverage는 변경하지 않는다. 취소는 스캔/복사/결과tile 및 native 호출 전후에서 확인한다. native 내부 중간 취소는 아직없다.

## 검증

코어672개 및 Release0경고오류·기존Clone 단독WPF 회귀통과. 513×17의 x255 tile경계 defect를 세모드로 검사하고 coverage 밖 모든채널/원본/비변경tile공유·seed재현성을 확인했다. Content-Aware/Proximity는 일정 주변tone을 복원한다. Create Texture는 검정hole에 인접한 ring pixel의 neighbour detail을 포함해 grain을 만들므로 상수주변이어도 결과가 흔들린다. 고정seed/hash/Box-Muller 및 24pixel ring의 독립수식으로 이 값을 1LSB이내 대조했다. 반투명 RGBA의 coverage128×opacity0.5 적용·zero/no-op·잘못된mode·사전취소를 검사했다.

## 남은 연결

- 문서좌표 stroke coverage·회전/flip 및 원본raster 확장·selection 최종blend·image-only guard.
- 입력 중 dark wash·J 도구/세모드·Size/Hardness/Opacity slider·단일Undo/Redo.
- 취소 가능한 끝연산 worker·document/transaction ownership·Esc/캡처상실/탭전환.
- 저장/출력·패널/물리입력·자연texture 품질·대형ROI 성능/메모리·portable.

현재는 연산 기반이며 사용자 Spot Healing 도구가 완성된 상태가 아니다. 실제 Mac 왕복 및 별도Windows PC 검증은 보류한다. 원본soft tip 밀도와 Windows max swept coverage 동등성도 품질잔여다.

## 문서 명령 연결

SpotHealing.Compute는image layer만허용하고원본pixels에서커널결과를만든뒤 document selection을layer.Transform좌표로최종blend한다. kernel coverage를selection으로미리축소하지않아원본patch검색/edge tone규칙을유지한다. mask/effects/opacity/blend/transform은with Pixels로보존한다. same-result는원본문서참조를반환하고doc검증·256MiB Undo예산·취소검사후결과를반환한다.

코어673개/전체Release0경고오류통과. 지정pixel선택clip/선택밖no-op·원본pixels/metadata공유·단일UndoRedo/문서참조복원·.comp재열기pixel일치를검사했다. 아직stroke coverage수집/변형복합·raster확장·J UI/wash·끝연산worker/Esc·WPF/출력·대형품질은후속이다. source 및 coverage는현재layer raster격자이며사용자도구완료로판정하지않는다.
