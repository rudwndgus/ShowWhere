# ShowWhere 2.0

ShowWhere is an AI-powered visual guide that understands what a user wants to do, observes the current Windows or web interface, and shows exactly where to click next.

화면을 설명하지 않아도 됩니다. 하고 싶은 일을 말하면 ShowWhere가 현재 화면을 확인하고 **다음에 눌러야 할 정확한 위치를 화면 위에 표시**합니다.

## Download ShowWhere

[![Download ShowWhere.exe](https://img.shields.io/badge/Windows-ShowWhere.exe%20다운로드-472323?style=for-the-badge&logo=windows11&logoColor=white)](https://github.com/rudwndgus/ShowWhere/releases/latest/download/ShowWhere.exe)

공식 Windows 프로그램은 `ShowWhere.exe` 하나입니다. 별도의 설치 프로그램, Node.js, .NET SDK, Python, 프로젝트 복제, API 키 또는 로컬 백엔드가 필요하지 않습니다.

요구 환경: Windows 10/11 x64와 인터넷 연결. 현재 시험 서명 단계에서는 Windows SmartScreen 경고가 나타날 수 있습니다.

## 사용 방법

1. 위 링크에서 `ShowWhere.exe`를 다운로드합니다.
2. 파일을 더블클릭합니다.
3. 화면에 나타난 고릴라 assistant를 누릅니다.
4. “프린터 설정은 어디서 해?”, “아마존에서 로그인하고 싶어”처럼 목표를 입력합니다.
5. ShowWhere가 화면 위에 표시한 위치를 누릅니다.

ShowWhere는 사용자를 대신해 자동 클릭하지 않습니다. 고릴라를 마우스 오른쪽 버튼으로 누른 뒤 `종료`를 선택하면 종료됩니다. 중복 실행은 자동으로 차단됩니다.

## Mobile Remote

ShowWhere 상단의 **모바일 연결**을 누르면 QR과 6자리 인증번호가 표시됩니다.

1. 휴대폰으로 QR을 스캔합니다.
2. PC에 표시된 6자리 번호를 휴대폰에 입력합니다.
3. 휴대폰에서 텍스트를 보내거나 마이크로 말합니다.
4. 질문과 PC의 답변이 휴대폰과 PC에 함께 표시됩니다.

휴대폰과 PC가 같은 Wi-Fi에 있을 필요는 없습니다. 두 장치는 [Railway의 ShowWhere HTTPS 서비스](https://api-production-6901.up.railway.app/mobile/)를 통해 Pairing API와 WebSocket으로 연결됩니다. 음성 입력도 같은 서버의 STT 경로를 사용합니다.

## Learning

ShowWhere remembers verified solutions and reuses them instead of solving the same problem from scratch.

- 개발자 모드의 `O`는 검증된 Human Gold 정답으로 저장됩니다.
- `X`와 코멘트는 잘못된 안내의 부정 증거와 교정으로 분리됩니다.
- `끝`은 사용자의 목표가 달성된 상태로 저장됩니다.
- 같은 질문은 로컬 캐시에서 즉시 재사용하고, 표현이 달라도 의미가 비슷하면 semantic intent로 재사용합니다.
- 로컬 데이터는 먼저 즉시 반영되고 Central Knowledge와 동기화되어 다른 PC에서도 사용할 수 있습니다.
- 인터넷이 잠시 끊겨도 이미 동기화된 로컬 지식과 빠른 재생은 유지됩니다.

## Production architecture

```text
User
  ↓
ShowWhere.exe ── Local Knowledge / Human Gold cache
  ↓ HTTPS
Railway ── Guide AI / Central Knowledge / Pairing / STT
  ↑ WSS
Mobile web
```

- `ShowWhere.exe`: Windows UI, 화면 관찰, UI Automation, overlay, 개발자 교정, 로컬 우선 캐시
- Railway: Guide API, AI fallback, 중앙 지식 동기화, QR pairing, WebSocket, 모바일 웹, STT
- Central Knowledge: 여러 PC가 공유하는 검증된 학습 기록
- Local Knowledge: 네트워크 왕복 없이 먼저 사용하는 빠른 런타임 기억

서버 로직과 Knowledge가 갱신되면 기존 EXE가 Railway와 중앙 동기화를 통해 새 내용을 사용합니다. Windows 런타임 자체가 바뀔 때만 새 EXE가 필요합니다.

## Automatic updates

ShowWhere는 시작 후 GitHub의 최신 공식 Release를 확인합니다. 새 버전이 있으면 사용자가 `지금 업데이트` 또는 `나중에`를 선택할 수 있습니다.

업데이트는 새 EXE를 임시 위치에 다운로드하고 GitHub Release의 SHA-256 digest와 비교한 뒤에만 교체합니다. 새 실행본이 정상 시작하지 않으면 이전 EXE를 복원하고 다시 실행합니다.

## Development

일반 사용자는 이 절의 명령을 실행할 필요가 없습니다.

```powershell
git clone https://github.com/rudwndgus/ShowWhere.git
cd ShowWhere
git switch ShowWhere2.0
npm ci
npm test
npm run test:windows
```

- `services/api`: Railway에서 실행되는 Guide, Knowledge, Pairing, WebSocket, mobile web, STT 서버
- `apps/windows`: WPF desktop, overlay, UI Automation, API client와 Windows 테스트
- `training`: Git으로 공유하는 개발자 Human Gold/O/X/완료 데이터
- `knowledge`: crawler 결과와 정규화된 웹 지식
- `scripts`: 검증, production publish와 공식 EXE 다운로드 도구

개발용 로컬 서버는 명시적으로 `npm run dev:api`를 실행한 경우에만 사용합니다. 공식 EXE에는 Railway production HTTPS 주소와 GitHub Actions Secret의 클라이언트 인증값이 빌드 메타데이터로 들어가며, secret은 저장소·로그·Release 설명에 기록하지 않습니다.

Windows 코드가 `ShowWhere2.0`에 push되면 GitHub Actions가 테스트, 공개 모바일 pairing 종단 테스트, self-contained single-file 빌드와 새 공식 Release를 자동으로 수행합니다. 기준 버전은 [`VERSION`](VERSION)이며, 이미 같은 버전이 배포된 경우 patch 버전을 자동 증가시킵니다.

개발 PC에서 최신 공식 산출물을 한 위치로 받으려면 다음 명령을 사용합니다.

```powershell
./scripts/download-production-windows.ps1
```

검증된 공식 로컬 경로는 `build/production/ShowWhere.exe` 하나입니다.
