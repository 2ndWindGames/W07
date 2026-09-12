# 런비트

PPT의 런비트 화면과 기능을 구현한 Android 세로형 Unity 앱입니다. `Assets/RunBeat/Scenes/RunBeat.unity`에서 시작합니다.

- 목표 100–220, 3가지 박자음, 매 걸음·두 걸음, 10·20·30분·자유 러닝
- 네이티브 Android 백그라운드 오디오와 잠금 화면 제어
- 기기 내 운동 기록·구간 저장·중단 복구·즐겨찾기
- 출처 자료를 확인한 YouTube 링크 24개와 목표·장르 필터
- W05와 같은 SecondWindGames 스플래시

Unity 메뉴의 **RunBeat > Setup Android Portrait**, **Build Android APK**를 사용합니다.

1.0.1(코드 2)은 비공개 트랙에서 로고 이후 검은 화면이 나오던 JNI/R8 문제를 수정합니다. [수정 내용과 재발 검사](Documentation/BLACK_SCREEN_FIX.md)를 참고하세요. 루트의 `W07.aab`는 이전 배포본입니다.

[빌드 방법과 배포 전 남은 항목](Documentation/RELEASE.md) · [검증 기록](Documentation/VALIDATION.md) · [리소스 출처 및 생성 프롬프트](Documentation/ASSET_PROVENANCE.md)

게시자 서명과 실기기 검증, 음악 청취 검수, 스토어 운영 자료가 완료되어야 최종 상용 배포본이 됩니다.
