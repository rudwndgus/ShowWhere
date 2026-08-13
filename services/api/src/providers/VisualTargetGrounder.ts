import type { Bounds, GuideDecision, GuideRequest, UiCandidate, VisualTarget } from '../../../../src/contracts';

function normalizedText(value: string | undefined): string {
  return (value ?? '').normalize('NFKC').toLocaleLowerCase().replace(/[^\p{L}\p{N}]+/gu, ' ').trim();
}

function compactText(value: string): string {
  return value.replace(/\s+/gu, '');
}

function textSimilarity(target: string, candidate: string): number {
  if (!target || !candidate) return 0;
  if (target === candidate) return 1;
  const compactTarget = compactText(target);
  const compactCandidate = compactText(candidate);
  if (compactTarget.length >= 3 && compactCandidate.length >= 3
      && (compactTarget.includes(compactCandidate) || compactCandidate.includes(compactTarget))) {
    const lengthRatio = Math.min(compactTarget.length, compactCandidate.length)
      / Math.max(compactTarget.length, compactCandidate.length);
    return 0.82 + 0.16 * lengthRatio;
  }
  const targetTokens = new Set(target.split(' ').filter(Boolean));
  const candidateTokens = new Set(candidate.split(' ').filter(Boolean));
  const overlap = [...targetTokens].filter((token) => candidateTokens.has(token)).length;
  return overlap === 0 ? 0 : (2 * overlap) / (targetTokens.size + candidateTokens.size);
}

function absoluteVisualBounds(screenshot: Bounds, target: VisualTarget): Bounds {
  const width = Math.min(screenshot.width, Math.max(8, target.width * screenshot.width));
  const height = Math.min(screenshot.height, Math.max(8, target.height * screenshot.height));
  return {
    x: Math.max(screenshot.x, Math.min(screenshot.x + screenshot.width - width, screenshot.x + target.x * screenshot.width)),
    y: Math.max(screenshot.y, Math.min(screenshot.y + screenshot.height - height, screenshot.y + target.y * screenshot.height)),
    width,
    height,
  };
}

function intersectionCoverage(left: Bounds, right: Bounds): number {
  const width = Math.max(0, Math.min(left.x + left.width, right.x + right.width) - Math.max(left.x, right.x));
  const height = Math.max(0, Math.min(left.y + left.height, right.y + right.height) - Math.max(left.y, right.y));
  const intersection = width * height;
  const smallerArea = Math.min(left.width * left.height, right.width * right.height);
  return smallerArea <= 0 ? 0 : intersection / smallerArea;
}

function proximity(left: Bounds, right: Bounds, screenshot: Bounds): number {
  const leftX = left.x + left.width / 2;
  const leftY = left.y + left.height / 2;
  const rightX = right.x + right.width / 2;
  const rightY = right.y + right.height / 2;
  const distance = Math.hypot(leftX - rightX, leftY - rightY);
  const diagonal = Math.max(1, Math.hypot(screenshot.width, screenshot.height));
  return Math.max(0, 1 - distance / diagonal);
}

interface RankedCandidate {
  candidate: UiCandidate;
  text: number;
  coverage: number;
  areaRatio: number;
  geometry: number;
  score: number;
}

export function matchVisualTargetToCandidate(request: GuideRequest, target: VisualTarget): UiCandidate | undefined {
  if (!request.screenshotBounds) return undefined;
  const targetLabel = normalizedText(target.label);
  const visualBounds = absoluteVisualBounds(request.screenshotBounds, target);
  const ranked: RankedCandidate[] = request.candidates
    .filter((candidate) => candidate.visible && candidate.enabled && candidate.clickable
      && candidate.bounds.width > 1 && candidate.bounds.height > 1)
    .map((candidate) => {
      const label = normalizedText(`${candidate.label ?? ''} ${candidate.description ?? ''}`);
      const text = textSimilarity(targetLabel, label);
      const coverage = intersectionCoverage(visualBounds, candidate.bounds);
      const near = proximity(visualBounds, candidate.bounds, request.screenshotBounds!);
      const visualArea = visualBounds.width * visualBounds.height;
      const candidateArea = candidate.bounds.width * candidate.bounds.height;
      const browserContentBonus = candidate.attributes?.sourceScope === 'browser_content' ? 0.025 : 0;
      return {
        candidate,
        text,
        coverage,
        areaRatio: visualArea <= 0 ? Number.POSITIVE_INFINITY : candidateArea / visualArea,
        geometry: Math.max(coverage, near * 0.55),
        score: text * 0.78 + coverage * 0.14 + near * 0.08 + browserContentBonus,
      };
    })
    .sort((left, right) => right.score - left.score
      || left.candidate.bounds.width * left.candidate.bounds.height
        - right.candidate.bounds.width * right.candidate.bounds.height);

  const best = ranked[0];
  if (!best) return undefined;
  const second = ranked[1];
  const exactLabel = compactText(normalizedText(best.candidate.label)) === compactText(targetLabel);
  const strongText = best.text >= 0.76;
  const strongGeometry = best.geometry >= 0.72 && best.text >= 0.45;
  const unambiguous = exactLabel || !second || best.score - second.score >= 0.045
    || best.text - second.text >= 0.12;
  const comparableVisualBox = best.areaRatio >= 0.2 && best.areaRatio <= 5;
  const uniqueGeometricHit = best.coverage >= 0.65 && comparableVisualBox
    && (!second || second.coverage < 0.5 || best.coverage - second.coverage >= 0.2);
  return ((unambiguous && (strongText || strongGeometry)) || uniqueGeometricHit)
    ? best.candidate
    : undefined;
}

export function groundVisualDecision(request: GuideRequest, decision: GuideDecision): GuideDecision {
  if (decision.action !== 'highlight_visual' || !decision.visualTarget) return decision;
  const candidate = matchVisualTargetToCandidate(request, decision.visualTarget);
  if (!candidate) return decision;
  const { visualTarget: _visualTarget, ...withoutVisualTarget } = decision;
  return { ...withoutVisualTarget, action: 'highlight', targetId: candidate.id };
}
