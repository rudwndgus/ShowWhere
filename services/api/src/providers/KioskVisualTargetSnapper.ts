import type { GuideRequest, VisualTarget } from '../../../../src/contracts';

interface PixelTarget {
  aliases: string[];
  box: [x: number, y: number, width: number, height: number];
  kioskSpecific?: boolean;
  kind?: 'product' | 'modifier';
}

const REFERENCE_WIDTH = 538;
const REFERENCE_HEIGHT = 956;

const targets: PixelTarget[] = [
  { aliases: ['eat in', 'dine in', '매장', '매장에서'], box: [83, 800, 177, 81], kioskSpecific: true },
  { aliases: ['take out', 'to go', '포장'], box: [270, 800, 170, 81], kioskSpecific: true },
  ...['coffee', 'breakfast', 'sandwiches', 'pastry', 'salad', 'soup', 'food to go'].map((label, row) => ({
    aliases: [label], box: [0, 163 + row * 57, 95, 56] as PixelTarget['box'],
  })),
  ...[
    'drip coffee', 'americano', 'cafe mocha', 'cappuccino', 'espresso', 'latte', 'chai latte', 'matcha latte',
    'macchiato', 'hot chocolate', 'tea',
  ].map((label, index) => product(label, index)),
  ...['bagel', 'blt', 'egg sandwich', 'bacon cheese omelette', 'omelette 2 veggie toppings', 'roll']
    .map((label, index) => product(label, index)),
  ...[
    'caesar salad wrap', 'cheese sandwich', 'chicken sandwich', 'egg salad sandwich', 'ham sandwich',
    'mixed sandwich', 'panini caprese 1', 'panini chicken 2', 'panini cuban 3', 'panini italian 4',
    'ready to go sandwich',
  ].map((label, index) => product(label, index)),
  ...[
    'apple turnover', 'banana nut muffins', 'blueberry muffins', 'cappuccino muffins', 'chocolate muffin',
    'tiramisu', 'croissants almond', 'croissants chocolate', 'croissants reg', 'danish pastry', 'fruit danish',
  ].map((label, index) => product(label, index)),
  ...['pre made salad', 'no meat salad'].map((label, index) => product(label, index)),
  ...['small 8 oz', 'large 16 oz'].map((label, index) => product(label, index)),
  ...[
    'bulgogi box', 'chicken box', 'dumpling 10pc', 'glass noodle box', 'glass noodle single', 'pancakes 8pc',
    'salmon box', 'single bulgogi',
  ].map((label, index) => product(label, index)),
  ...[
    [['small', '스몰', '작은 사이즈'], 0],
    [['medium', '미디엄', '중간 사이즈'], 1],
    [['large', '라지', '큰 사이즈'], 2],
    // Index 3 is an intentionally empty tile in the captured Latte modifier screen.
    [['ice', '아이스'], 4],
    [['flavor', '플레이버', '맛 추가'], 5],
    [['whole milk', 'wholemilk', '홀 밀크', '홀밀크'], 6],
    [['skim milk', 'skimmilk', '스킴 밀크', '스킴밀크', '무지방 우유'], 7],
    [['half and half', 'half & half', '하프 앤 하프', '하프앤하프'], 8],
    [['oat milk', 'oatmilk', '오트 밀크', '오트밀크'], 9],
    [['almond milk', 'almondmilk', '아몬드 밀크', '아몬드밀크'], 10],
  ].map(([aliases, index]) => modifier(aliases as string[], index as number)),
  ...[
    ['cheese', '치즈'], ['avocado', '아보카도'], ['bacon', '베이컨'], ['boars head', '보어스 헤드'],
    ['extra topping', '토핑 추가'], ['extra arugula', '아루굴라 추가'], ['extra spinach', '시금치 추가'],
    ['arella cheese', '모짜렐라 치즈'], ['on bagel', '베이글'], ['on hero', '히어로'],
  ].map((aliases, index) => modifier(aliases, index)),
  { aliases: ['add to cart', '장바구니 담기', '담기'], box: [449, 886, 89, 70], kioskSpecific: true },
  { aliases: ['clear all', '전체 삭제'], box: [355, 898, 63, 58], kioskSpecific: true },
  { aliases: ['home', '홈'], box: [357, 898, 61, 58] },
  { aliases: ['credit', '신용 카드', '카드'], box: [418, 898, 61, 58] },
  { aliases: ['others', 'other tender', '기타 결제'], box: [478, 898, 60, 58], kioskSpecific: true },
  { aliases: ['other amount', '직접 입력'], box: [92, 481, 350, 69], kioskSpecific: true },
  { aliases: ['no tip', '팁 없음'], box: [181, 570, 172, 61], kioskSpecific: true },
  { aliases: ['apply tender', 'apply and tender', '결제 적용'], box: [288, 815, 201, 60], kioskSpecific: true },
  { aliases: ['cash', '현금'], box: [150, 215, 236, 61] },
  { aliases: ['gift', 'gift card', '기프트'], box: [150, 288, 236, 60] },
  { aliases: ['point', '포인트'], box: [150, 360, 236, 61] },
];

function product(label: string, index: number): PixelTarget {
  // Measured from the latest native 538x956 capture. Keep the full colored
  // card (image, name and price) inside the guidance rectangle.
  const columns = [[107, 143], [251, 143], [395, 143]] as const;
  const rows = [[168, 142], [312, 142], [456, 142], [600, 143]] as const;
  const [x, width] = columns[index % 3];
  const [y, height] = rows[Math.floor(index / 3)];
  return { aliases: [label], box: [x, y, width, height], kioskSpecific: true, kind: 'product' };
}

function modifier(aliases: string[], index: number): PixelTarget {
  // Full white option tiles, including icon, surcharge and modifier name.
  const columns = [[8, 128], [140, 129], [271, 129], [402, 129]] as const;
  const rows = [[432, 137], [572, 137], [712, 139]] as const;
  const [x, width] = columns[index % 4];
  const [y, height] = rows[Math.floor(index / 4)];
  return { aliases, box: [x, y, width, height], kioskSpecific: true, kind: 'modifier' };
}

function normalize(value: string | undefined): string {
  return (value ?? '').normalize('NFKC').toLocaleLowerCase()
    .replace(/&/gu, ' and ').replace(/#/gu, ' ').replace(/[^\p{L}\p{N}]+/gu, ' ').trim();
}

function targetFor(label: string): PixelTarget | undefined {
  const normalized = normalize(label);
  if (!normalized) return undefined;
  return targets.find((target) => target.aliases.some((alias) => {
    const candidate = normalize(alias);
    return normalized === candidate || (candidate.length >= 5 && normalized.includes(candidate));
  }));
}

function targetFromUserIntent(request: GuideRequest, kind?: PixelTarget['kind']): PixelTarget | undefined {
  const intent = normalize(JSON.stringify({
    originalUserMessage: request.session.originalUserMessage,
    goal: request.session.goal,
    completedSteps: request.session.completedSteps,
    knownFacts: request.session.knownFacts,
  }));
  const matches = targets.filter((target) => !kind || target.kind === kind).flatMap((target) => target.aliases
    .map((alias) => ({ target, alias: normalize(alias) }))
    .filter(({ alias }) => {
      if (!alias) return false;
      if (` ${intent} `.includes(` ${alias} `)) return true;
      // Korean particles can be attached directly to an English menu name,
      // for example "TEA를". startsWith is token-local, so TEA never matches LATTE.
      return !alias.includes(' ') && /^[a-z0-9]+$/u.test(alias)
        && intent.split(' ').some((token) => token === alias || token.startsWith(alias));
    }));
  return matches.sort((left, right) => right.alias.length - left.alias.length)[0]?.target;
}

function hasKioskContext(request: GuideRequest): boolean {
  const context = normalize(`${request.context.applicationName} ${request.context.windowTitle ?? ''}`);
  const goal = normalize(`${request.session.originalUserMessage} ${request.session.goal ?? ''}`);
  return /kiosk|bluu|upr|up solution|키오스크/u.test(`${context} ${goal}`);
}

/** Snaps vision output to measured controls in the user-provided 538x956 BLUU DELI kiosk captures. */
export function snapKioskVisualTarget(request: GuideRequest, visualTarget: VisualTarget): VisualTarget {
  if (!request.screenshotBounds) return visualTarget;
  const ratio = request.screenshotBounds.width / request.screenshotBounds.height;
  if (ratio < 0.50 || ratio > 0.62) return visualTarget;
  // Keep the current screen type selected by vision, but resolve the exact
  // item within that type from the user's words. This prevents both TEA ->
  // LATTE and OAT MILK -> a tiny, unnamed visual region.
  const visualMatch = targetFor(visualTarget.label);
  const known = visualMatch?.kind
    ? targetFromUserIntent(request, visualMatch.kind) ?? visualMatch
    : targetFromUserIntent(request, 'modifier') ?? targetFromUserIntent(request, 'product') ?? visualMatch;
  if (!known || (!known.kioskSpecific && !hasKioskContext(request))) return visualTarget;
  const [x, y, width, height] = known.box;
  return {
    x: x / REFERENCE_WIDTH,
    y: y / REFERENCE_HEIGHT,
    width: width / REFERENCE_WIDTH,
    height: height / REFERENCE_HEIGHT,
    label: known.aliases[0].toLocaleUpperCase(),
  };
}
