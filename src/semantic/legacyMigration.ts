import { z } from 'zod';

export const LegacySemanticDraftSchema = z.object({
  schemaVersion: z.literal('showwhere-legacy-semantic-draft-v1'),
  legacyRecordId: z.string().min(1),
  source: z.object({
    userQuestion: z.string().min(1),
    currentStepDescription: z.string().min(1),
    developerCorrection: z.string().min(1),
  }).strict(),
  proposedTaskId: z.string().nullable(),
  proposedIntent: z.object({ action: z.string(), object: z.string() }).nullable(),
  evidenceAvailable: z.object({ semanticTargetSignature: z.boolean(), screenshotReference: z.boolean() }).strict(),
  status: z.literal('needs_human_review'),
  reason: z.string().min(1),
}).strict();

export type LegacySemanticDraft = z.infer<typeof LegacySemanticDraftSchema>;

export function createLegacyMigrationDraft(value: unknown): LegacySemanticDraft | undefined {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return undefined;
  const record = value as Record<string, unknown>;
  if (record.schemaVersion !== 1 || typeof record.id !== 'string') return undefined;
  const question = String(record.originalGoal ?? record.effectiveGoal ?? '').trim();
  if (!question) return undefined;
  const correction = String(record.correctedIntent ?? record.developerComment ?? record.refinedComment ?? '기존 교정의 의미를 사람이 확인해야 합니다.').trim();
  const context = record.context && typeof record.context === 'object' && !Array.isArray(record.context)
    ? record.context as Record<string, unknown> : {};
  const currentStepDescription = `앱: ${String(context.applicationName ?? 'unknown')}; 기존 화면 상태는 새 Teaching Mode에서 다시 확인 필요`;
  const text = `${question} ${correction}`.toLocaleLowerCase();
  const proposal = text.includes('프린터') || text.includes('printer')
    ? { taskId: 'windows.printer.check_status', action: 'check', object: 'printer_connection' }
    : text.includes('인터넷') || text.includes('와이파이') || text.includes('wi-fi') || text.includes('wifi')
      ? { taskId: 'windows.network.check_wifi_status', action: 'check', object: 'wifi_status' }
      : text.includes('배송') || text.includes('주문') || text.includes('order')
        ? { taskId: 'shopping.track_order', action: 'track', object: 'order' } : undefined;
  return LegacySemanticDraftSchema.parse({
    schemaVersion: 'showwhere-legacy-semantic-draft-v1',
    legacyRecordId: record.id,
    source: { userQuestion: question, currentStepDescription, developerCorrection: correction },
    proposedTaskId: proposal?.taskId ?? null,
    proposedIntent: proposal ? { action: proposal.action, object: proposal.object } : null,
    evidenceAvailable: {
      semanticTargetSignature: Boolean(record.correctTarget),
      screenshotReference: Boolean(record.screenshotPath),
    },
    status: 'needs_human_review',
    reason: 'Legacy coordinate-oriented corrections are never promoted to Gold automatically.',
  });
}
