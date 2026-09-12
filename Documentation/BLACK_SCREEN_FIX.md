# 1.0.1 Android 시작 화면 수정

## 원인과 수정

프로젝트 루트의 배포본 `W07.aab`에서 R8가 C# JNI 호출을 인식하지 못해 `RunBeatService`의 공개 연결 API 5개를 제거했다. `initialize(Context)`는 `onCreate()` 안으로 인라인됐고, `snapshot`, `command`, `openMusic`, `notificationSettings`는 제거됐다. C#의 `AndroidRunEngine` 생성자가 화면 생성 전에 이 초기화 함수를 호출했으므로 예외가 발생하면 로고 이후 화면이 생성되지 않았다.

- 네이티브 라이브러리의 `consumer-rules.pro`에 해당 JNI API 5개와 클래스 이름만 보존하는 규칙을 추가했다. 라이브러리 `defaultConfig.consumerProguardFiles`에 연결해 일반 Unity 빌드와 AAB 모두에 적용한다. R8 최적화는 유지한다.
- 화면을 먼저 생성한 뒤 네이티브 엔진을 초기화한다. 초기화 실패 시 안내와 재시도 버튼을 표시한다. 실패한 JNI 객체는 해제한다. 첫 상태 조회도 초기화 검증에 포함한다.
- 버전을 1.0.1 / 코드 2로 올렸다. 패키지와 업로드 키는 유지한다. 기존 `W07.aab`는 수정 전 파일이므로 재업로드하지 않는다.

## 재발 검사

`python Tools/Verify-AndroidBridge.py <APK 또는 AAB> --report <JSON 경로>`를 실제 배포 파일에 실행한다. DEX의 클래스 정의와 메서드 정의를 읽어 5개의 정확한 JNI 서명, public/static 접근, 실행 코드 존재를 확인한다. 단순히 문자열이나 호출 참조가 남아 있는지를 검사하지 않는다.

기존 `W07.aab`는 5개 검사 모두 실패했다. 원본 보고서: `BuildArtifacts/QA/black-screen/original-aab-bridge.json`.

수정 파일 빌드 후 같은 검사 및 `Tools/Verify-Apk.ps1`을 실행한다. PC 실행 검증은 Windows 플레이어를 `-runbeat-smoke <결과 폴더>` 인수로 실행한다. 실제 빌드의 홈/시작 버튼과 오류 로그, 화면 PNG를 기록하고 종료한다. Android 기기 재검증을 대신하지 않는다.

## 확인된 결과 (2026-09-11)

- 기존 검증 APK 1.0.0: JNI API 5/5 보존. 코드 축소가 적용된 배포용 `W07.aab`: 0/5. 같은 검사기로 APK/AAB 차이를 재현했다.
- 수정 APK 1.0.1(2): release `minifyEnabled true`를 유지한 상태에서 JNI API 5/5 보존. 소비자 보존 규칙이 최종 R8 configuration에 포함된 것도 확인했다.
- 패키지, SDK 26/36, ARM64, 세로 활동, 백그라운드 서비스 권한, APK 서명 및 ZIP/ELF 16KB 정렬 검사 통과.
- Unity 기능 검사 258개 통과.
- Windows 빌드 플레이어에서 실제 UI 패널을 390×844 텍스처에 렌더링했다. 홈/시작 버튼 표시, 오류 로그 없음, 한글 및 아이콘을 포함한 화면 PNG 육안 확인 통과. 결과: `BuildArtifacts/QA/black-screen/player-fixed`.
- Android 실기기는 연결되지 않았다. Play AAB는 현재 Unity 세션에 업로드 키 비밀번호가 없어 서명 대기 중이다. `Player Settings > Publishing Settings`에 기존 키 비밀번호를 입력한 뒤 `RunBeat > Build Store AAB`로 빌드한다.

## 비공개 트랙 업데이트

기존 게시자 키로 `Builds/Android/RunBeat-1.0.1.aab`를 서명해 새 출시로 올린다. 이미 코드 2가 사용됐다면 Player Settings에서 더 높은 코드를 지정한다. 테스트 기기에서 업데이트 후 로고 → 홈, 운동 시작/중지, 백그라운드 박자 재생을 확인한다.

```text
<ko-KR>
• 일부 Android 배포 환경에서 로고 이후 검은 화면이 표시되던 문제를 수정했습니다.
• 앱 시작 안정성을 개선하고 초기화 실패 시 재시도 안내를 추가했습니다.
</ko-KR>
```

근거: [Android JNI와 R8 보존 규칙](https://developer.android.com/topic/performance/app-optimization/keep-rule-examples).
