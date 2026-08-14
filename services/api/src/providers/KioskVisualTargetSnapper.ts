import type { GuideRequest, VisualTarget } from '../../../../src/contracts';

interface PixelTarget {
  aliases: string[];
  box: [x: number, y: number, width: number, height: number];
  kioskSpecific?: boolean;
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
    'small', 'medium', 'large', 'ice', 'flavor', 'whole milk', 'skim milk', 'half and half', 'oat milk', 'almond milk',
  ].map((label, index) => modifier(label, index)),
  ...[
    'cheese', 'avocado', 'bacon', 'boars head', 'extra topping', 'extra arugula', 'extra spinach',
    'arella cheese', 'on bagel', 'on hero',
  ].map((label, index) => modifier(label, index)),
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
  const columns = [[103, 144], [247, 143], [392, 146]] as const;
  const rows = [[165, 142], [309, 142], [453, 141], [596, 143]] as const;
  const [x, width] = columns[index % 3];
  const [y, height] = rows[Math.floor(index / 3)];
  return { aliases: [label], box: [x, y, width, height], kioskSpecific: true };
}

function modifier(label: string, index: number): PixelTarget {
  const columns = [[7, 127], [138, 127], [269, 128], [400, 129]] as const;
  const rows = [[432, 137], [572, 138], [713, 139]] as const;
  const [x, width] = columns[index % 4];
  const [y, height] = rows[Math.floor(index / 4)];
  return { aliases: [label], box: [x, y, width, height], kioskSpecific: true };
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
  const known = targetFor(visualTarget.label);
  if (!known || (!known.kioskSpecific && !hasKioskContext(request))) return visualTarget;
  const [x, y, width, height] = known.box;
  return {
    x: x / REFERENCE_WIDTH,
    y: y / REFERENCE_HEIGHT,
    width: width / REFERENCE_WIDTH,
    height: height / REFERENCE_HEIGHT,
    label: visualTarget.label,
  };
}
