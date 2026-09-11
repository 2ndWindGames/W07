# 런비트 Google Play 등록 자료

- 한글 이름: **런비트**
- 영문 이름: **RunBeat**
- 한글 등록명: **런비트 - 러닝 메트로놈** (13자)
- 영문 등록명: **RunBeat - Running Metronome** (27자)
- 패키지 이름: `com.secondwindgames.runbeat`

## 바로 업로드할 파일

| 등록 항목 | 파일 | 사양 |
|---|---|---|
| 앱 아이콘 | `GooglePlay/icon/runbeat-icon-512.png` | 512×512, 32-bit RGBA PNG, 1MB 미만 |
| 그래픽 이미지 | `GooglePlay/feature-graphic/runbeat-feature-1024x500.png` | 1024×500, 24-bit RGB PNG |
| 휴대전화 스크린샷 | `GooglePlay/screenshots/ko-KR/*.png` | 7장, 각 1080×1920, 24-bit RGB PNG |
| 등록명·소개 | `Listing/ko-KR.txt`, `Listing/en-US.txt` | 등록명 30자 이내, 짧은 설명 80자 이내 |
| 스크린샷 대체 텍스트 | `Listing/screenshot-alt-text.json` | 한글·영문 설명 |

스크린샷 추천 등록 순서: 홈 → 러닝 → 음악 → 기록 → 상세 기록 → 운동 시간 → 사운드.
`preview.html`을 브라우저에서 열면 전체 자료를 한 번에 볼 수 있습니다.

## 제작 기준

스크린샷은 현재 Unity 런비트 UI에서 직접 렌더링했습니다. AI로 UI를 재생성하거나 화면 위에 기능을 추가하지 않았습니다. 시간·날짜·기록은 설명을 위한 예시 데이터이며 별도의 메모리 엔진을 사용해 사용자 저장 기록을 변경하지 않았습니다. 현재 앱 UI는 한글입니다. 영문 파일은 등록명과 설명 문구이며 앱 UI의 영어 번역본을 뜻하지 않습니다.

아이콘과 배너는 내장 ImageGen으로 생성했습니다. 생성 원본과 프롬프트는 `Sources/`에 보관했습니다. 스토어 아이콘은 기존 앱의 신발 심볼을 참고한 별도 등록용 파일입니다. 기존 APK의 런처 아이콘은 덮어쓰지 않았습니다.

등록용 파일의 픽셀 크기, PNG 모드, 용량, 문구 길이는 `validation.json`에 기록했습니다.
실제 Play Console 업로드는 수행하지 않았습니다.

## 다시 촬영

Unity 메뉴 **RunBeat > Capture Google Play Screenshots**를 실행합니다. 진행 중인 에디터 운동이 없는 상태에서 실행하세요. 촬영 도구는 `UNITY_EDITOR` 안에 있어 Android 빌드에 포함되지 않습니다.

## 규격 출처

2026-09-11 확인:
- [Google Play 미리보기 자료](https://support.google.com/googleplay/android-developer/answer/9866151?hl=en)
- [Google Play 아이콘 규격](https://developer.android.com/distribute/google-play/resources/icon-design-specifications)
- [앱 등록명과 설명 길이](https://support.google.com/googleplay/android-developer/answer/9859152?hl=en)

