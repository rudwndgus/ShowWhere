import type { GuideDecision, GuideRequest, VisualTarget } from '../../../../src/contracts';

const UI_TARS_MODEL_PATTERN = /(?:^|[/_-])ui[-_]?tars(?:$|[/_.-])/iu;

const UI_TARS_SYSTEM_PROMPT = `You are a visual grounding agent for ShowWhere.
Inspect the complete attached screenshot and locate exactly one visible control that the user must click NEXT.
Use the original goal, completed steps, current application, and window title to preserve the user's intent.
Do not execute anything. Do not select ShowWhere's own window, chat, tooltip, or highlight.
If the destination is already open, target the control inside that destination, not a browser address bar or unrelated search box.
If the requested control is clearly visible, respond using exactly this native UI-TARS format:
Thought: <brief reason>
Action: click(start_box='(x,y)')
Coordinates must be integers normalized from 0 to 1000 over the entire screenshot.
If no single safe target is visible, respond:
Thought: <why it cannot be located>
Action: call_user()`;

export function isUiTarsModel(model: string): boolean {
  return UI_TARS_MODEL_PATTERN.test(model);
}

export function createUiTarsMessages(request: GuideRequest) {
  const task = {
    originalUserMessage: request.session.originalUserMessage,
    goal: request.session.goal,
    completedSteps: request.session.completedSteps,
    knownFacts: request.session.knownFacts,
    currentStep: request.session.currentStep,
    application: request.context.applicationName,
    windowTitle: request.context.windowTitle,
    instruction: 'Locate only the single visible control to click next. Return a native UI-TARS action.',
  };

  return [
    { role: 'system' as const, content: UI_TARS_SYSTEM_PROMPT },
    {
      role: 'user' as const,
      content: [
        { type: 'text' as const, text: JSON.stringify(task) },
        { type: 'image_url' as const, image_url: { url: request.screenshot } },
      ],
    },
  ];
}

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.max(minimum, Math.min(maximum, value));
}

function normalizePair(x: number, y: number, request: GuideRequest): [number, number] | undefined {
  if (!Number.isFinite(x) || !Number.isFinite(y) || x < 0 || y < 0) return undefined;
  if (x <= 1 && y <= 1) return [x, y];
  if (x <= 1000 && y <= 1000) return [x / 1000, y / 1000];

  const bounds = request.screenshotBounds;
  if (!bounds || bounds.width <= 0 || bounds.height <= 0) return undefined;
  if (x > bounds.width || y > bounds.height) return undefined;
  return [x / bounds.width, y / bounds.height];
}

function createPointTarget(x: number, y: number): VisualTarget {
  const width = 0.04;
  const height = 0.06;
  return {
    x: clamp(x - width / 2, 0, 1 - width),
    y: clamp(y - height / 2, 0, 1 - height),
    width,
    height,
    label: '다음에 누를 항목',
  };
}

function createBoxTarget(
  first: [number, number],
  second: [number, number],
): VisualTarget | undefined {
  let x = Math.min(first[0], second[0]);
  let y = Math.min(first[1], second[1]);
  let width = Math.abs(second[0] - first[0]);
  let height = Math.abs(second[1] - first[1]);
  if (width < 0.005) {
    width = 0.04;
    x -= width / 2;
  }
  if (height < 0.005) {
    height = 0.06;
    y -= height / 2;
  }
  x = clamp(x, 0, 1 - width);
  y = clamp(y, 0, 1 - height);
  width = Math.min(width, 1 - x);
  height = Math.min(height, 1 - y);
  if (width < 0.005 || height < 0.005) return undefined;
  return { x, y, width, height, label: '다음에 누를 항목' };
}

function actionCoordinates(content: string): number[] | undefined {
  const action = content.match(/Action\s*:\s*click\s*\(([\s\S]*?)\)/iu)?.[1];
  if (!action) return undefined;

  const coordinateSource = action.match(/(?:start_box|point)\s*=\s*(['"])([\s\S]*?)\1/iu)?.[2]
    ?? action;
  const values = coordinateSource.match(/-?\d+(?:\.\d+)?/gu)?.map(Number);
  return values && values.length >= 2 ? values.slice(0, 4) : undefined;
}

export function parseUiTarsDecision(
  content: string,
  request: GuideRequest,
): GuideDecision | undefined {
  if (/Action\s*:\s*call_user\s*\(/iu.test(content)) return undefined;
  const coordinates = actionCoordinates(content);
  if (!coordinates) return undefined;

  const first = normalizePair(coordinates[0], coordinates[1], request);
  if (!first) return undefined;
  const second = coordinates.length >= 4
    ? normalizePair(coordinates[2], coordinates[3], request)
    : undefined;
  const visualTarget = second ? createBoxTarget(first, second) : createPointTarget(...first);
  if (!visualTarget) return undefined;

  return {
    status: 'in_progress',
    action: 'highlight_visual',
    message: '화면에서 다음에 누를 위치를 찾았어요. 표시된 곳을 눌러주세요.',
    confidence: 0.88,
    visualTarget,
  };
}
