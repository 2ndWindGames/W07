# 런비트 Android

Unity 6000.3.23f1, Android 8.0 이상, ARM64, IL2CPP, target SDK 36, 세로 화면으로 구성했습니다. 기본 패키지 이름은 `com.secondwindgames.runbeat`, 버전은 `1.0.0 (1)`입니다. 첫 배포 전에 실제 사용할 패키지 이름과 업로드 키를 확정해야 합니다.

## 실행과 빌드

1. Unity에서 `Assets/RunBeat/Scenes/RunBeat.unity`를 엽니다.
2. `RunBeat > Setup Android Portrait`로 플랫폼·스플래시·아이콘 설정을 적용합니다.
3. `RunBeat > Build Android APK`로 `Builds/Android/RunBeat-1.0.0.apk`를 만듭니다.
4. 설치 후 첫 운동 시작 시 알림 권한을 허용하면 알림창에서 운동 제어가 가능합니다. 거부해도 앱 내 제어는 동작합니다.

스토어 AAB는 Unity를 실행하는 프로세스에 다음 환경 변수를 설정하고 `RunBeat > Build Store AAB`로 만듭니다. 비밀번호를 소스 파일이나 버전 관리에 적지 않습니다.

- `RUNBEAT_KEYSTORE`: 게시자의 업로드 keystore 절대 경로
- `RUNBEAT_KEY_ALIAS`: 업로드 키 별칭
- `RUNBEAT_STORE_PASSWORD`: keystore 비밀번호
- `RUNBEAT_KEY_PASSWORD`: 키 비밀번호

업로드 키가 없으면 AAB 빌드는 명시적으로 실패합니다. 기본 APK는 로컬 설치·검증용 서명을 사용하므로 Google Play에 게시할 수 있는 최종 서명본으로 취급하지 않습니다. 서명된 배포용 APK가 필요하면 게시자 키 설정을 적용한 뒤 APK를 다시 빌드합니다.

## 구현 범위

| PPT | 구현 |
| --- | --- |
| 6: 화면 시안 | 차콜·라임 홈, 실행 다이얼, 음악 카드, 한글 UI 및 생성한 전용 아트 |
| 7–8: 목표와 시작 | 100–220 step/min, 1단위 조절, 3초 카운트다운, 3가지 음색, 음량 미리듣기 |
| 8: 박자와 시간 | 매 걸음·두 걸음, 10·20·30분·자유, 시간 종료 시 자동 완료 |
| 8–9: 백그라운드 | 네이티브 mediaPlayback 포그라운드 서비스, AudioTrack, MediaSession, 잠금 화면 일시정지·재개·종료 |
| 9: 중단 처리 | 오디오 포커스 상실·duck 요청·이어폰 분리 시 일시정지, 사용자 입력으로만 재개 |
| 9–10: 목표·기록 | 다음 박자 경계에서 목표 적용, 목표 구간·실행 시간 기록, 상태 전환 및 10초 체크포인트 |
| 9: 강제 종료 | 마지막 저장 시간까지만 중단 기록으로 복구, 자동 재생 없음 |
| 10: 보존·삭제 | 원자적 저장, 설정 백업 복구, 즐겨찾기 즉시 저장, 개별·전체 기록 삭제 확인 |
| 12: 음악 | 링크 24개, 장르·즐겨찾기 필터, BPM=목표 또는 BPM×2=목표, 일치 후보가 없을 때만 ±5 후보 |
| 12: 외부 재생 | YouTube 외부 열기 전에 메트로놈 정지, 열기 실패 안내, 영상 정보에서 출처 확인 수준 표시 |
| W05 splash | 원본 SecondWindGames 로고, 흰 배경, 정적 표시 2초, Unity 로고 숨김 |

기록은 설정한 목표와 실제 박자 출력의 실행 시간입니다. GPS 거리나 실제 평균 케이던스를 추정해 보여주지 않습니다. Android 화면 재개가 시간 계산을 소유하지 않습니다. 프레임 기반 타이머로 박자를 재생하지 않습니다.

## 배포 전에 남은 검증과 자료

현재 구현을 실제 기기의 상용 배포 검증을 끝낸 제품으로 표현하지 않습니다. 이 작업 환경에는 adb에 연결된 Android 기기가 없었습니다. 아래 작업을 게시 전에 완료해야 합니다.

- 게시자 업로드 키로 AAB 서명 및 Play Console 내부 테스트 설치
- Android 8/13/15/16 실제 기기에서 30분 재생: 180·매 걸음 5,400박을 출력 녹음으로 측정
- 유선·Bluetooth 이어폰 분리, 실제 수신 전화, 음성 도우미, 다른 플레이어의 duck 요청, 오디오 경로 변경, 화면 잠금·배터리 절약 모드 시험
- 알림 권한 거부·허용, 잠금 화면과 미디어 버튼 제어, 앱 강제 종료·기기 재부팅 후 복구, 저장 공간 부족 시험
- 360×640·390×844 외 실제 기기 안전 영역, 큰 글자, TalkBack 및 실제 터치 접근성 검사
- 24개 음악의 직접 청취·탭 측정과 변속 구간, 한국 지역 재생 가능 여부 확인. 현재는 영상·게시자 자료를 확인한 목록이며 청취 검수 완료로 표시하지 않습니다. 한 항목은 논문 부록의 보고 BPM입니다.
- 운영자의 실제 고객지원 연락처와 공개 개인정보처리방침 URL, Play Console 앱 콘텐츠·데이터 보안·포그라운드 서비스 선언, 스토어 스크린샷 등록

PPT 11쪽의 4,900원 일회성 훈련 기록 결제는 제품 검증안입니다. 현재 배포 후보는 기록을 포함한 무료 앱이며, Play Billing 결제·영수증 검증·구매 복원은 구현하지 않았습니다. 결제 상품을 실제로 운영할 때는 Play 상품 설정과 결제 검증 흐름을 추가해야 합니다. 앱에 동작하지 않는 결제 버튼은 없습니다.

## 검증 방법

`Tools/Test-Native.ps1`은 실제 Android 소스의 RhythmSynth를 javac로 컴파일해 테스트합니다. 100·160·170·180·190·220 목표, 걸음 간격 1·2에서 각각 30분 분량을 합성하고 모든 박자 위치를 검사합니다. 실시간 30분 기기 재생 검증과 구분합니다.

Unity `RunBeatChecks.Run`은 범위 검사, 박자 간격, 시간 표기, 손상 저장 복구, 24개 링크 형식·중복, 스플래시·세로 설정을 확인합니다. `RunBeatVisualQA`는 Editor에서 실제 UI 클릭 콜백과 운동 흐름을 실행하며 화면을 캡처합니다. QA 기록은 `BuildArtifacts/QA`에 저장하고 배포 빌드에 포함하지 않습니다.

## 기술 근거

- [Android 오디오 포커스](https://developer.android.com/media/optimize/audio-focus): foreground 서비스 시작 후 포커스 요청, duck 시 자동 재개 대신 일시정지
- [포그라운드 서비스 유형](https://developer.android.com/develop/background-work/services/fgs/service-types#media): mediaPlayback 및 해당 권한 선언
- [Android AudioTrack](https://developer.android.com/reference/android/media/AudioTrack): PCM 프레임과 playback head를 이용한 리듬 출력·실행 시간
- [음악 자료](../Assets/RunBeat/Resources/RunBeat/MusicCatalog.json): 각 링크의 자료 출처와 확인 수준

서드파티 음악의 오디오·썸네일은 앱에 복제하지 않습니다. 앱 안의 두 음악 커버는 런비트 전용 장식 이미지입니다.
