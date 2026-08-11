import { describe, expect, it } from 'vitest';
import type { AiProvider } from '../../../../src/guide-api/AiProvider';
import { SemanticKnowledgeStore } from '../../../../src/semantic/knowledgeStore';
import { guideRequestFixture } from '../testFixtures';
import { KnowledgeGroundedProvider } from './KnowledgeGroundedProvider';

describe('KnowledgeGroundedProvider', () => {
  it('resolves a model semantic target to a current runtime candidate ID', async () => {
    const inner: AiProvider = { async decideNextAction() {
      return { status: 'in_progress', action: 'highlight', semanticTarget: 'windows.settings', message: '설정을 누르세요.', confidence: 0.95 };
    } };
    const store = {
      async concepts() { return [{ schemaVersion: 'showwhere-concept-v1' as const, conceptId: 'windows.settings', aliases: ['Settings'], preferredRoles: ['button'], relatedConcepts: [], description: 'Settings' }]; },
      async taskGraphs() { return []; }, async goldSteps() { return []; },
    } as unknown as SemanticKnowledgeStore;
    const provider = new KnowledgeGroundedProvider(inner, store);

    await expect(provider.decideNextAction(guideRequestFixture)).resolves.toMatchObject({
      semanticTarget: 'windows.settings', targetId: 'candidate-settings',
    });
  });

  it('asks instead of inventing a candidate when semantic target is absent on screen', async () => {
    const inner: AiProvider = { async decideNextAction() {
      return { status: 'in_progress', action: 'highlight', semanticTarget: 'missing', message: 'Go', confidence: 0.95 };
    } };
    const store = { async concepts() { return []; }, async taskGraphs() { return []; }, async goldSteps() { return []; } } as unknown as SemanticKnowledgeStore;
    const decision = await new KnowledgeGroundedProvider(inner, store).decideNextAction(guideRequestFixture) as { action: string };
    expect(decision.action).toBe('ask_user');
  });
});
