# 편집 가능한 텍스트 전환

원본 기준은 `macOS/Compositor/Document/TypeTool.swift`, `Rendering/InlineTextEditor.swift`, `IO/ProjectStore.swift`다. 원본은 style과cached image를같은레이어에보관한다. 실제합성은cached image를사용하며image참조가바뀌는pixel편집은텍스트를rasterize하고transform/mask는편집정보를유지한다.

## 모델과 프로젝트 기반

LayerTextStyle에 content/fontName/fontSize/RGB/alignment/tracking/leading/optional boxSize를연결했다. 원본범위는UTF16content최대100000, fontSize1–2000, RGB0–1, tracking-100–1000, leading0–5000, box16–30000 및100MP다. Auto leading은fontSize×1.2, padding12다. .NETstring Length는UTF16길이이므로emoji도원본과같이센다. fontName이비어있거나Windows에없는경우도원본fallback계약에맞춰메타데이터를보존한다.

Layer.Text를추가하고새Pixels참조는Shape/Text를함께지운다. 동일Pixels참조·transform/mask/appearance변경은Text를유지한다. group/adjustment/shape와text공존은거부한다. TextJson은원본camelCase와Left/Center/Right·boxSize배열을읽고쓴다. duplicate/unknown fields와invalid범위는거부하며imageFile없는text는PNGdecode전에거부한다. optional box와missing leading은각각pointtext/Auto로처리한다.

ProjectStore는유효한text metadata와기존PNG를읽고저장한다. 읽을때재렌더하지않으므로없는Macfont도기존외관을유지한다. v8 JSON+PNG디렉터리형식은유지하며새텍스트렌더러는다음단계다. unknown text필드는계속거부하고손상된text객체는invalidmetadata로처리한다. 기존metadata거부/덮어쓰기원본보존테스트를이구분으로갱신했다.

코어718개통과:Unicode/unknown font/right alignment/tracking/leading/box와cached pixels왕복, transform/mask/samepixels정보유지·새pixels래스터화·UndoRedo, duplicate/unknown/null/범위·shape공존거부,손상metadata열기/덮어쓰기거부와원본manifest보존. 일반layer복제는기존record복사로Text도유지한다.

## 남은 작업

- 글꼴해결/부족한글꼴알림과실제텍스트렌더러:다국어/emoji/fallback·tracking/leading/alignment·point/paragraph wrapping과크기.
- Type(T)·텍스트생성/재편집·inline editor·선택/커서/키보드/확정/취소·폰트/크기/색상표/슬라이더.
- 텍스트재렌더명령·transform/boxresize/mask·단일Undo/Redo·저장전확정·선택복사와계층이동의편집정보보존검사.
- 실제WPF/PNG출력·원본font/sampling·대형/취소·새배포검증. 기존Mac외관의cachedPNG보존은새문자렌더링동등성검증이아니다.

현재는모델/저장기반이며WindowsUI에서문자내용을편집하는기능은없다. 현재publish-zoom-controls에는이변경도아직없다. 실제Mac왕복/별도PC실기는사용자지시로보류하며통과로표시하지않는다.
