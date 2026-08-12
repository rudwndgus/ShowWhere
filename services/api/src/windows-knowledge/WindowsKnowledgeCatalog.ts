export interface WindowsKnowledgeEntry {
  id: string;
  kind: 'setting' | 'troubleshooting';
  intents: string[];
  route: string[][];
  navigationDepth: number;
  completionEvidence: string[];
  msSettingsUri?: string;
}

const start = ['시작', '시작 메뉴', 'start'];
const settings = ['설정', 'settings'];
const route = (...steps: string[][]): string[][] => [start, settings, ...steps];
const setting = (
  id: string, intents: string[], msSettingsUri: string, steps: string[][], completionEvidence?: string[],
): WindowsKnowledgeEntry => ({
  id, kind: 'setting', intents, msSettingsUri, route: route(...steps),
  navigationDepth: 2 + steps.length,
  completionEvidence: completionEvidence ?? steps.at(-1) ?? [],
});
const troubleshoot = (
  id: string, intents: string[], msSettingsUri: string, steps: string[][], completionEvidence: string[],
): WindowsKnowledgeEntry => {
  const diagnosticStart = steps.findIndex((step) => step.some((label) => completionEvidence.includes(label)));
  const navigationSteps = diagnosticStart > 0 ? diagnosticStart : Math.min(2, steps.length);
  return {
    id, kind: 'troubleshooting', intents, msSettingsUri, route: route(...steps),
    navigationDepth: 2 + navigationSteps, completionEvidence,
  };
};

const system = ['시스템', 'system'];
const devices = ['Bluetooth 및 장치', '블루투스 및 장치', 'Bluetooth & devices'];
const network = ['네트워크 및 인터넷', 'Network & internet'];
const personalization = ['개인 설정', 'Personalization'];
const apps = ['앱', 'Apps'];
const accounts = ['계정', 'Accounts'];
const timeLanguage = ['시간 및 언어', 'Time & language'];
const gaming = ['게임', 'Gaming'];
const accessibility = ['접근성', 'Accessibility'];
const privacy = ['개인 정보 및 보안', '개인정보 및 보안', 'Privacy & security'];
const update = ['Windows 업데이트', '윈도우 업데이트', 'Windows Update'];

// Canonical navigation facts derived from Microsoft's ms-settings URI reference.
// Coordinates are deliberately absent: the live UI Automation tree resolves them per computer.
export const windowsKnowledgeCatalog: WindowsKnowledgeEntry[] = [
  setting('windows.system.display', ['디스플레이 설정', '화면 해상도', '화면 배율', '모니터 설정', 'display settings', 'screen resolution'], 'ms-settings:display', [system, ['디스플레이', 'Display']]),
  setting('windows.system.graphics', ['그래픽 설정', 'gpu 설정', '앱 그래픽 성능', 'graphics settings'], 'ms-settings:display-advancedgraphics', [system, ['디스플레이', 'Display'], ['그래픽', 'Graphics']]),
  setting('windows.system.sound', ['소리 설정', '출력 장치', '스피커 설정', 'sound settings', 'audio output'], 'ms-settings:sound', [system, ['소리', 'Sound']]),
  setting('windows.system.notifications', ['알림 설정', '알림 끄기', 'notification settings'], 'ms-settings:notifications', [system, ['알림', 'Notifications']]),
  setting('windows.system.focus', ['집중 설정', '방해 금지', '집중 지원', 'focus settings', 'do not disturb'], 'ms-settings:quiethours', [system, ['집중', 'Focus']]),
  setting('windows.system.power', ['전원 설정', '배터리 설정', '절전 설정', '화면 꺼짐 시간', 'power settings', 'battery settings', 'sleep settings'], 'ms-settings:powersleep', [system, ['전원 및 배터리', '전원', 'Power & battery', 'Power']]),
  setting('windows.system.storage', ['저장소 설정', '용량 확인', '디스크 공간', 'storage settings', 'disk space'], 'ms-settings:storagesense', [system, ['저장소', 'Storage']]),
  setting('windows.system.multitasking', ['멀티태스킹 설정', '창 맞춤', '스냅 창', 'multitasking', 'snap windows'], 'ms-settings:multitasking', [system, ['멀티태스킹', 'Multitasking']]),
  setting('windows.system.activation', ['윈도우 정품 인증', '제품 키 변경', 'activation', 'product key'], 'ms-settings:activation', [system, ['정품 인증', 'Activation']]),
  setting('windows.system.recovery', ['복구 설정', 'pc 초기화', '고급 시작 옵션', 'recovery settings', 'reset this pc'], 'ms-settings:recovery', [system, ['복구', 'Recovery']]),
  setting('windows.system.troubleshoot', ['문제 해결 설정', '문제 해결사', 'troubleshooter settings'], 'ms-settings:troubleshoot', [system, ['문제 해결', 'Troubleshoot']]),
  setting('windows.system.about', ['내 컴퓨터 사양', '윈도우 버전', '장치 사양', '시스템 정보', 'about this pc', 'windows version'], 'ms-settings:about', [system, ['정보', 'About']]),

  setting('windows.devices.bluetooth', ['블루투스 설정', 'bluetooth 설정', '블루투스 장치', 'bluetooth settings'], 'ms-settings:bluetooth', [devices, ['Bluetooth', '블루투스']]),
  setting('windows.devices.printers', ['프린터 설정', '프린터 상태', '연결된 프린터', '인쇄 장치', '스캐너 설정', 'printer settings', 'printers and scanners'], 'ms-settings:printers', [devices, ['프린터 및 스캐너', 'Printers & scanners']]),
  setting('windows.devices.mouse', ['마우스 설정', '포인터 속도', '마우스 버튼', 'mouse settings', 'pointer speed'], 'ms-settings:mousetouchpad', [devices, ['마우스', 'Mouse']]),
  setting('windows.devices.touchpad', ['터치패드 설정', 'touchpad settings'], 'ms-settings:devices-touchpad', [devices, ['터치 패드', '터치패드', 'Touchpad']]),
  setting('windows.devices.typing', ['키보드 설정', '입력 설정', '자동 수정', 'typing settings', 'keyboard settings'], 'ms-settings:typing', [timeLanguage, ['입력', 'Typing']]),
  setting('windows.devices.autoplay', ['자동 실행 설정', 'autoplay settings'], 'ms-settings:autoplay', [devices, ['자동 실행', 'AutoPlay']]),
  setting('windows.devices.usb', ['usb 설정', 'usb 알림', 'USB settings'], 'ms-settings:usb', [devices, ['USB']]),
  setting('windows.devices.camera', ['카메라 장치 설정', '카메라 기본 설정', 'camera device settings'], 'ms-settings:camera', [devices, ['카메라', 'Cameras']]),

  setting('windows.network.status', ['인터넷 설정', '네트워크 상태', '인터넷 상태', 'network status', 'internet settings'], 'ms-settings:network-status', [network]),
  setting('windows.network.wifi', ['와이파이 설정', 'wifi 설정', '무선 인터넷', 'wi fi settings'], 'ms-settings:network-wifi', [network, ['Wi-Fi', '와이파이']]),
  setting('windows.network.ethernet', ['이더넷 설정', '유선 인터넷', 'ethernet settings'], 'ms-settings:network-ethernet', [network, ['이더넷', 'Ethernet']]),
  setting('windows.network.vpn', ['vpn 설정', 'VPN settings'], 'ms-settings:network-vpn', [network, ['VPN']]),
  setting('windows.network.hotspot', ['모바일 핫스팟', '핫스팟 설정', 'mobile hotspot'], 'ms-settings:network-mobilehotspot', [network, ['모바일 핫스팟', 'Mobile hotspot']]),
  setting('windows.network.proxy', ['프록시 설정', 'proxy settings'], 'ms-settings:network-proxy', [network, ['프록시', 'Proxy']]),
  setting('windows.network.airplane', ['비행기 모드', '에어플레인 모드', 'airplane mode'], 'ms-settings:network-airplanemode', [network, ['비행기 모드', 'Airplane mode']]),

  setting('windows.personalization.background', ['배경 화면 변경', '바탕화면 배경', 'wallpaper', 'desktop background'], 'ms-settings:personalization-background', [personalization, ['배경', 'Background']]),
  setting('windows.personalization.colors', ['윈도우 색상', '다크 모드', '라이트 모드', 'colors settings', 'dark mode'], 'ms-settings:colors', [personalization, ['색', '색상', 'Colors']]),
  setting('windows.personalization.themes', ['테마 설정', 'windows theme', 'themes settings'], 'ms-settings:themes', [personalization, ['테마', 'Themes']]),
  setting('windows.personalization.lockscreen', ['잠금 화면 설정', 'lock screen settings'], 'ms-settings:lockscreen', [personalization, ['잠금 화면', 'Lock screen']]),
  setting('windows.personalization.start', ['시작 메뉴 설정', 'start menu settings'], 'ms-settings:personalization-start', [personalization, ['시작', 'Start']]),
  setting('windows.personalization.taskbar', ['작업 표시줄 설정', '태스크바 설정', 'taskbar settings'], 'ms-settings:taskbar', [personalization, ['작업 표시줄', 'Taskbar']]),
  setting('windows.personalization.fonts', ['글꼴 설정', '폰트 설치', 'font settings'], 'ms-settings:fonts', [personalization, ['글꼴', 'Fonts']]),

  setting('windows.apps.installed', ['설치된 앱', '앱 제거', '프로그램 삭제', 'installed apps', 'uninstall app'], 'ms-settings:appsfeatures', [apps, ['설치된 앱', 'Installed apps']]),
  setting('windows.apps.defaults', ['기본 앱 설정', '기본 브라우저', '파일 연결 프로그램', 'default apps', 'default browser'], 'ms-settings:defaultapps', [apps, ['기본 앱', 'Default apps']]),
  setting('windows.apps.startup', ['시작 프로그램', '부팅 앱', 'startup apps'], 'ms-settings:startupapps', [apps, ['시작 프로그램', 'Startup']]),
  setting('windows.apps.optional', ['선택적 기능', '윈도우 기능 추가', 'optional features'], 'ms-settings:optionalfeatures', [apps, ['선택적 기능', 'Optional features']]),

  setting('windows.accounts.info', ['내 계정 정보', '사용자 계정', 'your info', 'account info'], 'ms-settings:yourinfo', [accounts, ['사용자 정보', '내 정보', 'Your info']]),
  setting('windows.accounts.signin', ['로그인 옵션', 'pin 변경', '비밀번호 변경', '윈도우 헬로', 'sign in options', 'windows hello'], 'ms-settings:signinoptions', [accounts, ['로그인 옵션', 'Sign-in options']]),
  setting('windows.accounts.family', ['가족 계정', '다른 사용자 추가', 'family settings', 'other users'], 'ms-settings:otherusers', [accounts, ['가족 및 다른 사용자', '다른 사용자', 'Family & other users', 'Other users']]),
  setting('windows.accounts.backup', ['윈도우 백업', '동기화 설정', 'windows backup', 'sync settings'], 'ms-settings:backup', [accounts, ['Windows 백업', 'Windows backup']]),
  setting('windows.accounts.work', ['회사 계정 연결', '학교 계정 연결', '회사 또는 학교 액세스', 'access work or school'], 'ms-settings:workplace', [accounts, ['회사 또는 학교 액세스', 'Access work or school']]),

  setting('windows.time.date', ['날짜 시간 설정', '시간대 변경', 'date and time', 'time zone'], 'ms-settings:dateandtime', [timeLanguage, ['날짜 및 시간', 'Date & time']]),
  setting('windows.time.language', ['언어 설정', '윈도우 언어 변경', 'language settings', 'display language'], 'ms-settings:regionlanguage', [timeLanguage, ['언어 및 지역', 'Language & region']]),
  setting('windows.time.speech', ['음성 설정', 'speech settings', 'speech language'], 'ms-settings:speech', [timeLanguage, ['음성', 'Speech']]),

  setting('windows.gaming.gamebar', ['게임 바 설정', 'xbox game bar'], 'ms-settings:gaming-gamebar', [gaming, ['Game Bar', '게임 바']]),
  setting('windows.gaming.captures', ['게임 녹화 설정', '화면 녹화 저장 위치', 'captures settings'], 'ms-settings:gaming-gamedvr', [gaming, ['캡처', 'Captures']]),
  setting('windows.gaming.gamemode', ['게임 모드 설정', 'game mode'], 'ms-settings:gaming-gamemode', [gaming, ['게임 모드', 'Game Mode']]),

  setting('windows.accessibility.text', ['글자 크기', '텍스트 크기', 'text size'], 'ms-settings:easeofaccess-display', [accessibility, ['텍스트 크기', 'Text size']]),
  setting('windows.accessibility.narrator', ['내레이터 설정', '화면 읽기', 'narrator settings'], 'ms-settings:easeofaccess-narrator', [accessibility, ['내레이터', 'Narrator']]),
  setting('windows.accessibility.magnifier', ['돋보기 설정', '화면 확대', 'magnifier settings'], 'ms-settings:easeofaccess-magnifier', [accessibility, ['돋보기', 'Magnifier']]),
  setting('windows.accessibility.captions', ['자막 설정', '라이브 캡션', 'captions settings', 'live captions'], 'ms-settings:easeofaccess-closedcaptioning', [accessibility, ['캡션', '자막', 'Captions']]),

  setting('windows.privacy.location', ['위치 권한', '위치 서비스', 'location privacy'], 'ms-settings:privacy-location', [privacy, ['위치', 'Location']]),
  setting('windows.privacy.camera', ['카메라 권한', '앱 카메라 허용', 'camera privacy', 'camera permission'], 'ms-settings:privacy-webcam', [privacy, ['카메라', 'Camera']]),
  setting('windows.privacy.microphone', ['마이크 권한', '앱 마이크 허용', 'microphone privacy', 'microphone permission'], 'ms-settings:privacy-microphone', [privacy, ['마이크', 'Microphone']]),
  setting('windows.privacy.diagnostics', ['진단 데이터', '피드백 설정', 'diagnostic data'], 'ms-settings:privacy-feedback', [privacy, ['진단 및 피드백', 'Diagnostics & feedback']]),
  setting('windows.privacy.search', ['윈도우 검색 설정', '검색 권한', 'search permissions'], 'ms-settings:search-permissions', [privacy, ['검색 권한', 'Search permissions']]),
  setting('windows.privacy.developers', ['개발자 모드', 'developer mode', 'for developers'], 'ms-settings:developers', [privacy, ['개발자용', 'For developers']]),

  setting('windows.update.main', ['윈도우 업데이트', '업데이트 확인', 'windows update', 'check for updates'], 'ms-settings:windowsupdate', [update]),
  setting('windows.update.history', ['업데이트 기록', '설치된 업데이트', 'update history'], 'ms-settings:windowsupdate-history', [update, ['업데이트 기록', 'Update history']]),
  setting('windows.update.options', ['업데이트 고급 옵션', '업데이트 일시 중지', 'advanced update options'], 'ms-settings:windowsupdate-options', [update, ['고급 옵션', 'Advanced options']]),
  setting('windows.security.main', ['윈도우 보안', '바이러스 검사', 'windows security', 'virus scan'], 'ms-settings:windowsdefender', [privacy, ['Windows 보안', 'Windows Security']]),

  troubleshoot('windows.troubleshoot.no_sound', ['소리가 안 나', '소리가 안나', '음소거 아닌데 소리', '오디오가 안 나', 'no sound', 'audio not working'], 'ms-settings:sound', [system, ['소리', 'Sound'], ['출력', '출력 장치', 'Output', 'Choose where to play sound'], ['볼륨 믹서', 'Volume mixer'], ['문제 해결', 'Troubleshoot']], ['출력 장치', '볼륨', '음소거']),
  troubleshoot('windows.troubleshoot.microphone', ['마이크가 안 돼', '마이크가 안되', '마이크 소리 안 들어가', 'microphone not working'], 'ms-settings:sound', [system, ['소리', 'Sound'], ['입력', '입력 장치', 'Input', 'Choose a device for speaking or recording'], ['마이크 테스트', 'Test your microphone']], ['입력 장치', '마이크 테스트']),
  troubleshoot('windows.troubleshoot.printer', ['프린터가 안 돼', '프린터가 안되', '인쇄가 안 돼', '프린터를 못 찾아', 'printer not working', 'cannot print', 'printer not found'], 'ms-settings:printers', [devices, ['프린터 및 스캐너', 'Printers & scanners'], ['장치 추가', 'Add device'], ['프린터 문제 해결사', 'Run the troubleshooter']], ['프린터 목록', '장치 추가']),
  troubleshoot('windows.troubleshoot.wifi', ['와이파이가 안 돼', '인터넷이 안 돼', '인터넷 연결 안됨', 'wifi not working', 'no internet'], 'ms-settings:network-status', [network, ['Wi-Fi', '와이파이'], ['사용 가능한 네트워크 표시', 'Show available networks'], ['네트워크 문제 해결사', 'Network troubleshooter']], ['연결됨', '네트워크 상태']),
  troubleshoot('windows.troubleshoot.bluetooth', ['블루투스가 안 돼', '블루투스 연결 안됨', 'bluetooth not working', 'cannot pair bluetooth'], 'ms-settings:bluetooth', [devices, ['Bluetooth', '블루투스'], ['장치 추가', 'Add device'], ['문제 해결', 'Troubleshoot']], ['Bluetooth', '장치 추가']),
  troubleshoot('windows.troubleshoot.camera', ['카메라가 안 돼', '웹캠이 안 돼', 'camera not working', 'webcam not working'], 'ms-settings:privacy-webcam', [privacy, ['카메라', 'Camera'], ['카메라 액세스', 'Camera access'], ['앱에서 카메라에 액세스하도록 허용', 'Let apps access your camera']], ['카메라 액세스']),
  troubleshoot('windows.troubleshoot.update', ['윈도우 업데이트 안됨', '업데이트 실패', 'windows update failed', 'update error'], 'ms-settings:windowsupdate', [update, ['다시 시도', 'Retry'], ['문제 해결', 'Troubleshoot']], ['업데이트 확인', '다시 시도']),
  troubleshoot('windows.troubleshoot.storage', ['저장 공간 부족', '디스크 용량 부족', '용량이 없어', 'low disk space', 'storage full'], 'ms-settings:storagesense', [system, ['저장소', 'Storage'], ['임시 파일', 'Temporary files'], ['저장 공간 센스', 'Storage Sense']], ['임시 파일', '저장 공간 센스']),
  troubleshoot('windows.troubleshoot.display', ['화면이 이상해', '해상도가 이상해', '화면이 너무 커', 'display problem', 'wrong resolution'], 'ms-settings:display', [system, ['디스플레이', 'Display'], ['배율', 'Scale'], ['디스플레이 해상도', 'Display resolution']], ['배율', '디스플레이 해상도']),
  troubleshoot('windows.troubleshoot.login', ['로그인이 안 돼', 'pin 로그인이 안 돼', '윈도우 비밀번호 문제', 'cannot sign in', 'pin not working'], 'ms-settings:signinoptions', [accounts, ['로그인 옵션', 'Sign-in options'], ['PIN', 'Windows Hello PIN'], ['PIN을 잊음', 'I forgot my PIN']], ['로그인 옵션', 'PIN']),
  troubleshoot('windows.troubleshoot.battery', ['배터리가 너무 빨리 닳아', '배터리 소모가 심해', 'battery drains fast', 'poor battery life'], 'ms-settings:batterysaver', [system, ['전원 및 배터리', 'Power & battery'], ['배터리 사용량', 'Battery usage'], ['배터리 절약 모드', 'Battery saver', 'Energy saver']], ['배터리 사용량', '배터리 절약 모드']),
  troubleshoot('windows.troubleshoot.keyboard', ['키보드가 안 돼', '키보드 입력 안됨', 'keyboard not working', 'keyboard input problem'], 'ms-settings:typing', [timeLanguage, ['입력', 'Typing'], ['고급 키보드 설정', 'Advanced keyboard settings']], ['입력', '고급 키보드 설정']),
  troubleshoot('windows.troubleshoot.mouse', ['마우스가 안 돼', '마우스 클릭 안됨', 'mouse not working', 'mouse click problem'], 'ms-settings:mousetouchpad', [devices, ['마우스', 'Mouse'], ['마우스 포인터 속도', 'Mouse pointer speed'], ['추가 마우스 설정', 'Additional mouse settings']], ['마우스 포인터 속도', '추가 마우스 설정']),
  troubleshoot('windows.troubleshoot.usb', ['usb 인식 안됨', 'usb 장치를 못 찾아', 'usb not recognized', 'usb device not detected'], 'ms-settings:usb', [devices, ['USB'], ['USB 알림', 'USB notifications']], ['USB 알림']),
  troubleshoot('windows.troubleshoot.external_display', ['두 번째 모니터가 안 나와', '외부 모니터 인식 안됨', 'second monitor not detected', 'external display not working'], 'ms-settings:display', [system, ['디스플레이', 'Display'], ['여러 디스플레이', 'Multiple displays'], ['검색', '감지', 'Detect']], ['여러 디스플레이', '감지']),
  troubleshoot('windows.troubleshoot.app', ['앱이 실행 안 돼', '프로그램이 자꾸 꺼져', '앱이 멈춰', 'app not opening', 'app keeps crashing'], 'ms-settings:appsfeatures', [apps, ['설치된 앱', 'Installed apps'], ['고급 옵션', 'Advanced options'], ['복구', 'Repair'], ['초기화', 'Reset']], ['고급 옵션', '복구', '초기화']),
  troubleshoot('windows.troubleshoot.startup', ['부팅이 느려', '시작이 너무 느려', 'windows startup slow', 'slow boot'], 'ms-settings:startupapps', [apps, ['시작 프로그램', 'Startup'], ['시작 영향', 'Startup impact']], ['시작 프로그램', '시작 영향']),
  troubleshoot('windows.troubleshoot.activation', ['정품 인증이 안 돼', '윈도우 인증 오류', 'activation failed', 'windows is not activated'], 'ms-settings:activation', [system, ['정품 인증', 'Activation'], ['문제 해결', 'Troubleshoot'], ['제품 키 변경', 'Change product key']], ['정품 인증 상태', '문제 해결']),
  troubleshoot('windows.troubleshoot.time', ['컴퓨터 시간이 틀려', '시간이 안 맞아', 'wrong windows time', 'clock is wrong'], 'ms-settings:dateandtime', [timeLanguage, ['날짜 및 시간', 'Date & time'], ['자동으로 시간 설정', 'Set time automatically'], ['지금 동기화', 'Sync now']], ['자동으로 시간 설정', '지금 동기화']),
  troubleshoot('windows.troubleshoot.network_reset', ['네트워크 초기화', '인터넷 설정 초기화', 'reset network', 'network reset'], 'ms-settings:network-advancedsettings', [network, ['고급 네트워크 설정', 'Advanced network settings'], ['네트워크 초기화', 'Network reset']], ['네트워크 초기화']),
  troubleshoot('windows.troubleshoot.blue_screen', ['블루스크린', '파란 화면 오류', 'blue screen', 'bsod'], 'ms-settings:recovery', [system, ['복구', 'Recovery'], ['고급 시작 옵션', 'Advanced startup'], ['지금 다시 시작', 'Restart now']], ['고급 시작 옵션']),
];

export const windowsKnowledgeSources = [
  'https://learn.microsoft.com/windows/apps/develop/launch/launch-settings-app',
  'https://support.microsoft.com/windows/windows-help-and-learning-1a3d47f6-9d74-4d2f-8a7a-0950471d9b7c',
] as const;
