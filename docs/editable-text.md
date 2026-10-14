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

## WPF 레이아웃 렌더링 기반

TextRasterizer는WPFFormattedText의shaping/font fallback/wordlayout을사용해point/paragraph 텍스트를cachedRaster로변환한다. 설치font의family/source 및localizedfamilyname을찾고없으면SystemFonts.MessageFontFamily를사용하며RequestedFont/ResolvedFamily/MissingFont를결과에반환한다. 기존프로젝트를열때자동재렌더하거나원본fontName을바꾸지는않는다. PostScriptface명/weight/style 매핑은후속이다.

현재tracking0의left/center/right·leading/Auto·fixedboxwordwrap/clipping·pointsize를연결했다. 원본padding12·caret여유fontSize*.1·최소16px,30000side/100MP예산을적용한다. RenderTargetBitmap을96dpi/Pbgra32로만들어BGRA→RGBA교환후immutableRaster로변환한다. 저장RGB는sRGB byte로변환한다. 모델은tracking전체범위를보존하지만renderer는비영tracking을명시적으로거부한다. WPFglyphspacing 경로가완료되기전임의근사나설정무시는하지않는다.

actual --text-render-smoke 통과:Latin/한글/아랍어/emoji/결합문자/다중행 nonempty·premul,paragraph left/right차이·줄바꿈/fixedsize,leading24/60의높이차이,unknownfontflag,emptytext·사전취소·tracking거부,renderedstyle/PNG .comp왕복. 아랍어/emoji PNG도시각확인했다. nonempty확인은정확한문자shaping전체품질을입증하지않으며emoji는현재단색이다. 컬러emoji/정확한폰트metrics·bidi/긴word/leading극한/큰text·비영tracking은후속이다. RenderTarget작업자체는중간취소되지않고현재CLI는UI thread의기반검사다.

Release0경고/오류·제품core변경없음·718코어검증유지. Type/inline UI·font선택/fallback안내·새text렌더command·단일Undo/저장확정·backgroundSTA준비/성능과새패키지배포는계속잔여다. 이번코드는개발Release에만반영됐다.

API기준은 Microsoft [FormattedText](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.formattedtext?view=windowsdesktop-10.0), [LineHeight](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.formattedtext.lineheight?view=windowsdesktop-10.0) 문서다. 자간/추가glyph제어는 [GlyphRun](https://learn.microsoft.com/dotnet/api/system.windows.media.glyphrun) 경로를후속으로검토한다.

## 설치 글꼴 PostScript face 연결

OpenTypeFontNames는SFNT(TTF/OTF)와TTC 컬렉션의name table을읽는다. family/subfamily/full-name/PostScript/typographic family·face(ID1/2/4/6/16/17)를Unicode big-endian(platform0/3)에서수집한다. Mac platform1의ASCII이름도읽지만비ASCII MacRoman은근사decode하지않는다. 표/문자offset과길이·Unicode짝수길이·컬렉션size를검사하고glyph data나font file을프로젝트에복사하지않는다.

TextFontResolver는family/localizedfamily를먼저조회한다. 그외이름은설치Typeface의GlyphTypeface.GetFontStream에서읽은name을사용한다. TTC안의다른font를혼동하지않도록family와face가현재GlyphTypeface와맞는경우에만full-name/PostScript alias를연결한다. exact Typeface의weight/style/stretch를그대로FormattedText에전달한다. 없으면기존MissingFont flag/systemfallback을유지하고requested name/기존cachedPNG는자동변경하지않는다. unreadable/미지원font metadata는alias인덱스에서건너뛰고family경로는계속사용한다.

코어720개통과:합성SFNT Unicode/PostScript와독립TTC regular/bold face,잘린table·잘못된offset/collectioncount/홀수UTF16길이거부. actualWPF에서ArialMT/Arial-BoldMT/Arial Bold의family·굵기매핑/요청명유지,실제설치TTC face의weight/style유지및기존다국어·문단·fallback·cachedPNG회귀도통과했다. Release0경고/오류.

현재alias인덱스는첫비family요청때생성/캐시된다. 글꼴설치변경refresh·background/cancellation·large catalog·variation named instances/FVAR추가이름·모든locale/font collection의동등성은후속이다. 비영tracking/컬러emoji/Type·inline UI/재렌더명령/배포도계속잔여다. 이번변경은개발Release에만반영됐다.

필드기준은Microsoft [OpenType name table](https://learn.microsoft.com/en-us/typography/opentype/otspec182/name), collection offset기준은 [OpenType font file](https://learn.microsoft.com/en-us/typography/opentype/otspec181/otff) 문서다.
