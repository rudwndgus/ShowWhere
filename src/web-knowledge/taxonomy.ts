export interface WebSemanticLabel {
  id: string;
  description: string;
  aliases: string[];
}

export const webSemanticTaxonomy: WebSemanticLabel[] = [
  { id: 'home', description: 'website home or main page', aliases: ['home', 'homepage', 'main', 'main page', '홈', '홈페이지', '메인'] },
  { id: 'menu', description: 'open the primary navigation menu', aliases: ['menu', 'navigation', 'nav', 'more', '메뉴', '전체 메뉴', '더보기'] },
  { id: 'search', description: 'search for products, content, or records', aliases: ['search', 'find', 'lookup', '검색', '찾기', '조회'] },
  { id: 'account', description: 'account overview and account menu', aliases: ['account', 'my account', 'account & lists', 'account and lists', '계정', '내 계정', '마이', '마이페이지'] },
  { id: 'profile', description: 'personal profile and user information', aliases: ['profile', 'your profile', 'personal info', '프로필', '내 정보', '개인 정보'] },
  { id: 'login', description: 'sign in or log in to an account', aliases: ['sign in', 'log in', 'login', '로그인', '계정으로 로그인'] },
  { id: 'logout', description: 'sign out or log out of an account', aliases: ['sign out', 'log out', 'logout', '로그아웃'] },
  { id: 'settings', description: 'site or application settings', aliases: ['settings', 'preferences', 'options', '설정', '환경설정', '옵션'] },
  { id: 'order_history', description: 'view previous and current orders or purchase history', aliases: ['your orders', 'orders', 'order history', 'purchase history', 'purchases', 'returns & orders', '주문', '주문 내역', '구매 내역', '구매내역'] },
  { id: 'order_detail', description: 'view the details of one order', aliases: ['order detail', 'order details', 'view order', '주문 상세', '주문 상세보기'] },
  { id: 'delivery_location', description: 'set or change the shopping delivery location', aliases: ['update location', 'change location', 'deliver to', 'delivering to', 'delivery location', '배송 위치', '배송지 설정', '위치 변경'] },
  { id: 'order_tracking', description: 'track a shipment, delivery, or package', aliases: ['track package', 'track order', 'tracking', 'shipment tracking', 'delivery status', '배송 조회', '배송조회', '배송 현황', '주문 추적', '택배 조회', '내 배송'] },
  { id: 'returns', description: 'return, refund, or exchange an order', aliases: ['returns', 'return items', 'refund', 'exchange', '반품', '환불', '교환', '반품 신청'] },
  { id: 'cart', description: 'shopping cart or basket', aliases: ['cart', 'shopping cart', 'basket', 'bag', '장바구니', '쇼핑백'] },
  { id: 'checkout', description: 'checkout and place an order', aliases: ['checkout', 'place order', 'buy now', 'purchase', '결제', '주문하기', '바로 구매'] },
  { id: 'product_detail', description: 'product or item detail page', aliases: ['product detail', 'item detail', 'details', '상품 상세', '제품 상세', '상세보기'] },
  { id: 'notifications', description: 'notifications, alerts, or inbox', aliases: ['notifications', 'alerts', 'inbox', '알림', '알림함', '소식'] },
  { id: 'help', description: 'help center, customer service, or support', aliases: ['help', 'help center', 'support', 'customer service', 'contact us', '도움말', '고객센터', '고객 지원', '문의하기'] },
  { id: 'security', description: 'password, login, and account security', aliases: ['security', 'password', 'two factor', '2fa', '보안', '비밀번호', '2단계 인증'] },
  { id: 'privacy', description: 'privacy, consent, and data controls', aliases: ['privacy', 'privacy settings', 'data controls', '개인정보', '개인 정보 설정', '데이터 설정'] },
  { id: 'addresses', description: 'saved shipping or billing addresses', aliases: ['addresses', 'your addresses', 'shipping address', 'billing address', '주소', '배송지', '주소록'] },
  { id: 'payment_methods', description: 'saved cards and payment methods', aliases: ['payment methods', 'payments', 'wallet', 'cards', '결제 수단', '카드', '지갑'] },
  { id: 'subscriptions', description: 'memberships, plans, or subscriptions', aliases: ['subscriptions', 'membership', 'plans', 'billing', '구독', '멤버십', '요금제'] },
  { id: 'device_management', description: 'manage digital content, registered devices, apps, or ebooks', aliases: ['manage your content and devices', 'content and devices', 'devices', 'digital content', 'registered devices', '기기 관리', '콘텐츠 및 기기', '디지털 콘텐츠'] },
  { id: 'communication_preferences', description: 'manage email, notification, advertising, or communication preferences', aliases: ['communication preferences', 'email preferences', 'advertising preferences', 'notification settings', '알림 설정', '이메일 설정', '광고 설정', '수신 설정'] },
  { id: 'recommendations', description: 'manage recommendations, interests, or personalization', aliases: ['recommendations', 'improve your recommendations', 'your interests', 'personalization', '추천', '관심사', '개인화'] },
  { id: 'browsing_history', description: 'view or edit browsing and recently viewed history', aliases: ['browsing history', 'recently viewed', 'viewing history', '검색 기록', '최근 본 상품', '방문 기록'] },
  { id: 'household', description: 'manage household, family, teen, or child profiles', aliases: ['amazon household', 'household', 'family', 'family library', '가족', '가족 계정', '하우스홀드'] },
  { id: 'reviews', description: 'manage product reviews, ratings, or community profile', aliases: ['your reviews', 'reviews', 'ratings', 'community profile', '리뷰', '평점', '내 리뷰'] },
  { id: 'gift_cards', description: 'gift card balance, redemption, reload, and gift card settings', aliases: ['gift cards', 'gift card balance', 'redeem gift card', 'reload your balance', '기프트 카드', '상품권', '기프트카드 잔액'] },
  { id: 'language', description: 'language or locale selection', aliases: ['language', 'locale', 'region', '언어', '지역', '언어 설정'] },
  { id: 'filter', description: 'filter a list or search result', aliases: ['filter', 'filters', 'refine', '필터', '결과 좁히기'] },
  { id: 'sort', description: 'sort a list or search result', aliases: ['sort', 'sort by', 'order by', '정렬', '정렬 기준'] },
  { id: 'download', description: 'download a file or exported data', aliases: ['download', 'export', 'save file', '다운로드', '내보내기', '파일 저장'] },
  { id: 'upload', description: 'upload or attach a file', aliases: ['upload', 'attach file', 'choose file', '업로드', '파일 첨부', '파일 선택'] },
];
