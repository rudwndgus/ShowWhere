# ShowWhere

ShowWhere 2.0 Brain v2 is documented in [docs/brain-v2.md](docs/brain-v2.md). The legacy AI remains the default while `legacy`, `shadow`, and `v2` modes are evaluated.

The developer-feedback workflow lives in [`training/`](training/README.md). The synthetic generation, independent judging, human review, dataset splitting, and benchmark tools live in [`learning/`](learning/README.md).

화면을 설명하지 않아도 하고 싶은 일만 말하면, Windows 전체 화면에서 다음에 눌러야 할 위치를 직접 표시하는 내비게이션 도우미입니다.

ShowWhere는 Windows 10/11용 .NET 8 WPF 프로그램입니다. Microsoft UI Automation으로 실제 버튼을 찾고, 필요한 경우 Featherless 비전 모델이 전체 화면을 분석합니다. 버튼은 자동으로 누르지 않으며 사용자가 표시된 위치를 직접 선택합니다.

## 구조

```text
ShowWhere/
├─ apps/windows/
│  ├─ ShowWhere.Desktop/            WPF UI와 안내 반복
│  ├─ ShowWhere.WindowsAutomation/  UI Automation과 화면 캡처
│  ├─ ShowWhere.Overlay/            DPI 대응 최상단 표시
│  ├─ ShowWhere.ApiClient/          백엔드 HTTP 클라이언트
│  ├─ ShowWhere.Core/               계약과 상태 검증
│  └─ ShowWhere.Windows.Tests/      Windows 테스트
├─ services/api/                    Featherless 백엔드
├─ src/contracts/                   TypeScript 요청/응답 계약
├─ src/guide-api/                   API 안전 검증과 문맥 보호
├─ scripts/                         Windows 실행 스크립트
└─ docs/                            개발 및 설계 문서
```

## 동작 순서

```text
사용자 목표 입력
→ Windows UI Automation 후보 수집
→ Windows 설정 작업은 공식 설정 분류를 반영한 로컬 카탈로그로 경로 결정
→ 일반 후보는 DeepSeek가 다음 targetId 선택
→ 후보를 찾지 못하면 전체 화면 캡처
→ UI-TARS가 다음 클릭 좌표 탐색
→ 응답과 좌표 검증
→ 실제 화면에 click-through 오버레이 표시
→ 사용자 클릭 감지
→ 새 화면을 관찰해 다음 단계 반복
```

Chrome과 Edge도 Windows 프로그램이 UI Automation 및 화면 비전으로 관찰합니다. 브라우저 확장 프로그램은 사용하지 않습니다.

Windows 설정 안내는 표시 언어가 한국어 또는 영어인 Windows 10/11의 실제 UI Automation 후보를 확인한 뒤 한 단계씩 표시합니다. 최소화·최대화·복원·닫기 같은 창 제목 표시줄 버튼은 설정 후보에서 제외하며, 해당 Windows 버전이나 장치에 존재하지 않는 항목을 임의의 좌표로 표시하지 않습니다.

`ai-learning` 개발 모드에서는 모든 assistant 답변에 O/X 평가가 표시됩니다. O와 X 평가는 즉시 영구 저장되고, X를 선택하면 의미·코멘트·정답 영역을 수정할 수 있습니다. 드래그는 미리보기만 만들며 명시적으로 `저장`을 눌러야 교정 스크린샷과 좌표가 기록됩니다. 저장된 의미와 UI Automation 서명은 다음 같은 질문에 즉시 재사용하며, 학습용 JSONL로도 내보낼 수 있습니다. 자세한 형식은 [개발자 교정 모드](docs/developer-corrections.md)를 참고하세요.

배포용 `window-back-kyung`과 학습용 `ai-learning` 사이의 개선 승격 절차는 [학습 브랜치 운영 방식](docs/learning-branch-workflow.md)에 정리돼 있습니다.

## 개발 환경

필수 항목:

- Windows 10/11
- Node.js
- .NET 8 SDK

```powershell
npm install
npm test
npm run typecheck
npm run lint
npm run build:all
```

API 개발 실행:

```powershell
npm run dev:api
```

Windows 앱 개발 실행:

```powershell
npm run run:windows
```

배포용 Windows 빌드:

```powershell
npm run build:api
npm run publish:windows
```

결과는 `build/windows/ShowWhere.exe`에 생성됩니다.

## AI 설정

실제 API 키는 Git에서 제외되는 루트 `.env`에만 저장합니다.

```dotenv
SHOWWHERE_AI_MODE=featherless
FEATHERLESS_API_KEY=your-server-only-key
FEATHERLESS_BASE_URL=https://api.featherless.ai/v1
FEATHERLESS_GUIDE_MODEL=deepseek-ai/DeepSeek-V3.2
FEATHERLESS_VISION_MODELS=ByteDance-Seed/UI-TARS-1.5-7B,Qwen/Qwen3-VL-30B-A3B-Instruct,Qwen/Qwen3-VL-8B-Instruct
SHOWWHERE_API_HOST=127.0.0.1
SHOWWHERE_API_PORT=8787
```

API 키는 Node 백엔드만 읽습니다. Windows 실행 파일에는 키가 포함되지 않습니다.

## 테스트

```powershell
npm test
npm run typecheck
npm run lint
npm run test:windows
```

자세한 내용은 [개발 문서](docs/development.md), [구조 문서](docs/architecture.md), [안전 원칙](docs/safety.md)을 참고하세요.
