# Zoom 전용 도구

원본은 `macOS/Compositor/Rendering/EditorCanvas.swift`의 zoomDrag와 `UI/NavigationToolHeader.swift`다. 클릭은2배, Option 클릭은절반, 좌우drag는100point마다2배/절반이다. 3point이동부터scrub으로판정하고release때다시클릭확대를하지않는다. 퍼센트범위는0.1–3200이다.

Zoom(Z)를Windowsrail17번째에추가했다. Alt는원본Option에대응한다. 클릭/드래그anchor의document좌표를유지하며Esc/cancel은시작zoom/pan을복원한다. Middle pan은기존경로를유지한다. 퍼센트slider/정밀input을동기화하고Enter·focus loss에서유효입력을적용한다. 잘못된draft는현재값으로돌리고범위밖유한값은clamp한다. Fit/wheel의하한도0.1%로맞췄다. Zoom은pixel/document/Undo를변경하지않는다.

17tool을기본높이에배치하도록railitem높이28·세로margin1·padding2로조정했다. 아이콘21px공간을유지하며작은창의실제overflow스크롤은그대로다. 기존title/canvas origin도고정한다. tool17화면을시각확인했다.

Smear(R)의Tab모드순환도원본EditorSession.cycleToolMode대로Liquify→Blur→Smudge→Liquify로연결했다. busy/interaction/transaction중에는변경하지않고textbox편집은기존shortcut가드에따른다.

실제WPF Zoom 검사통과:클릭/Alt배율·anchor,100px양방향scrub·release중복확대없음·cancel뷰복원,slider/input/%문자/0.1–3200/clamp/invaliddraft,Middlepan취소,모드순환/transaction가드,document참조/Undo0/saved불변. 17tool startup스크롤숨김/compactscroll/복원·title/canvas고정회귀와기존Smear실제Save/reopen/취소도통과했다. Release0경고/오류·제품코어변경없음·이전716코어검증유지.

최신실행패키지는 `artifacts/publish-zoom-ui/Compositor.Windows.exe`다. 기존패키지는유지한다. 배포검증은아래에기록한다. 원본의커스텀확대/축소cursor,physical/DPI복합입력·길게drag하는성능과실제Mac왕복은별도품질범위다. Mac/별도PC실기는사용자지시로보류한다.

원본퍼센트input의Up/Down1%·Shift10%조정및Enter/Esc적용도연결하고실제WPF1.00→1.01→.91배율검사를통과했다. 최종패키지경로는 `artifacts/publish-zoom-controls/Compositor.Windows.exe`로갱신한다. 앞선publish-zoom-ui는키보드step추가전패키지다.

최종배포본Zoom/keyboardstep·Smear모드순환검사와portable45초gate1회도통과했다. 공백경로copy/제한PATH·패키지내CLR/Skia/C로드관측·전체WPF회귀는동일개발PC결과다. 보류실기나일반30초gate안정화로확대해해석하지않는다.
