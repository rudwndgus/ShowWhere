import type { SearchIntent } from '../types/search';

export const INTENT_DICTIONARY: SearchIntent[] = [
  {
    id: 'LOGIN', label: '로그인',
    userPhrases: ['로그인', '접속', '계정 들어가기', 'log in', 'login', 'sign in', 'signin', 'account login'],
    targetTerms: ['로그인', 'log in', 'login', 'sign in', 'signin'],
    preferredRoles: ['button', 'link'],
  },
  {
    id: 'SIGN_UP', label: '회원가입',
    userPhrases: ['회원가입', '가입', '계정 만들기', '새 계정', 'register', 'registration', 'sign up', 'signup', 'create account', 'join'],
    targetTerms: ['회원가입', '가입', 'register', 'sign up', 'signup', 'create account', 'join'],
    preferredRoles: ['button', 'link'],
  },
  {
    id: 'PASSWORD_RESET', label: '비밀번호 찾기',
    userPhrases: ['비밀번호 찾기', '비밀번호 잊음', '비밀번호 잊어버렸어요', '암호 찾기', '패스워드', 'forgot password', 'reset password', 'recover password', 'password help'],
    targetTerms: ['비밀번호 찾기', '암호 찾기', 'forgot password', 'reset password', 'recover password', 'password help'],
    preferredRoles: ['button', 'link'],
  },
  {
    id: 'SETTINGS', label: '설정',
    userPhrases: ['설정', '환경설정', '세팅', '옵션', 'preferences', 'settings', 'setting', 'options', 'configuration', 'config'],
    targetTerms: ['설정', '환경설정', 'preferences', 'settings', 'setting', 'options', 'configuration'],
    preferredRoles: ['button', 'menuitem'],
  },
  {
    id: 'SEARCH', label: '검색',
    userPhrases: ['검색', '찾아보기', 'search', 'find', 'lookup'],
    targetTerms: ['검색', 'search', 'find', 'lookup'],
    preferredRoles: ['searchbox', 'textbox', 'input:search'],
  },
  {
    id: 'SAVE', label: '저장',
    userPhrases: ['저장', 'save', 'download', '다운로드'],
    targetTerms: ['저장', 'save', 'download', '다운로드'],
    preferredRoles: ['button', 'link'],
  },
  {
    id: 'PRINT', label: '인쇄',
    userPhrases: ['인쇄', '출력', '프린트', 'print'],
    targetTerms: ['인쇄', '출력', '프린트', 'print'],
    preferredRoles: ['button', 'menuitem'],
  },
  {
    id: 'PDF', label: 'PDF 저장',
    userPhrases: ['pdf', 'pdf 저장', 'pdf로 저장', 'export pdf', 'download pdf', 'save as pdf'],
    targetTerms: ['pdf', 'pdf 저장', 'download pdf', 'export pdf', 'save as pdf'],
    preferredRoles: ['button', 'link', 'menuitem'],
  },
  {
    id: 'CLOSE', label: '닫기',
    userPhrases: ['닫기', 'close', 'dismiss', 'cancel', '취소'],
    targetTerms: ['닫기', 'close', 'dismiss', 'cancel', '취소'],
    preferredRoles: ['button'],
  },
  {
    id: 'NEXT', label: '다음',
    userPhrases: ['다음', '계속', '진행', 'next', 'continue', 'proceed'],
    targetTerms: ['다음', '계속', '진행', 'next', 'continue', 'proceed'],
    preferredRoles: ['button', 'link'],
  },
  {
    id: 'BACK', label: '이전',
    userPhrases: ['뒤로', '이전', 'back', 'previous'],
    targetTerms: ['뒤로', '이전', 'back', 'previous'],
    preferredRoles: ['button', 'link'],
  },
  {
    id: 'SUBMIT', label: '제출',
    userPhrases: ['제출', '보내기', '확인', '완료', 'submit', 'send', 'confirm', 'done'],
    targetTerms: ['제출', '보내기', '확인', '완료', 'submit', 'send', 'confirm', 'done'],
    preferredRoles: ['button', 'input:submit'],
  },
  {
    id: 'MENU', label: '메뉴',
    userPhrases: ['메뉴', '더보기', '햄버거 메뉴', 'menu', 'more', 'navigation', 'nav'],
    targetTerms: ['메뉴', '더보기', 'menu', 'more', 'navigation', 'nav'],
    preferredRoles: ['button', 'menuitem'],
  },
  {
    id: 'PROFILE', label: '프로필',
    userPhrases: ['프로필', '내 계정', '마이페이지', 'account', 'profile', 'my page', 'user menu'],
    targetTerms: ['프로필', '내 계정', '마이페이지', 'account', 'profile', 'my page', 'user menu'],
    preferredRoles: ['button', 'link', 'menuitem'],
  },
  {
    id: 'LOGOUT', label: '로그아웃',
    userPhrases: ['로그아웃', '나가기', 'sign out', 'logout', 'log out'],
    targetTerms: ['로그아웃', 'sign out', 'logout', 'log out'],
    preferredRoles: ['button', 'link', 'menuitem'],
  },
  {
    id: 'HELP', label: '도움말',
    userPhrases: ['도움말', '고객센터', '지원', 'help', 'support', 'customer service'],
    targetTerms: ['도움말', '고객센터', '지원', 'help', 'support', 'customer service'],
    preferredRoles: ['button', 'link'],
  },
  {
    id: 'REFUND', label: '환불',
    userPhrases: ['환불', '결제 취소', 'refund', 'return payment', 'reverse payment'],
    targetTerms: ['환불', '결제 취소', 'refund', 'return payment', 'reverse payment'],
    preferredRoles: ['button', 'link'],
  },
  {
    id: 'DELETE', label: '삭제',
    userPhrases: ['삭제', '지우기', 'remove', 'delete'],
    targetTerms: ['삭제', '지우기', 'remove', 'delete'],
    preferredRoles: ['button', 'menuitem'],
  },
  {
    id: 'EDIT', label: '수정',
    userPhrases: ['수정', '편집', '변경', 'edit', 'modify', 'change'],
    targetTerms: ['수정', '편집', '변경', 'edit', 'modify', 'change'],
    preferredRoles: ['button', 'link'],
  },
  {
    id: 'ORDER_HISTORY', label: '주문 내역',
    userPhrases: ['주문 내역', '주문내역', '내 주문', 'orders', 'order history', 'my orders'],
    targetTerms: ['주문 내역', '주문내역', 'orders', 'order history', 'my orders'],
    preferredRoles: ['button', 'link', 'menuitem'],
  },
];
