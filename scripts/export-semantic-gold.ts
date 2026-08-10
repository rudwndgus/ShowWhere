import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { SemanticKnowledgeStore } from '../src/semantic/knowledgeStore';

const store = new SemanticKnowledgeStore();
const records = await store.goldSteps();
const approved = records.filter((record) => record.verification.status === 'human_verified');
const stamp = new Date().toISOString().replaceAll(':', '-').replaceAll('.', '-');
const output = path.join(store.trainingRoot, 'exports', 'semantic-v2', stamp);
await mkdir(output, { recursive: true });

const examples = approved.map((record) => ({
  id: record.id,
  input: {
    userGoal: record.source.userQuestion,
    intent: record.intent,
    task: record.task,
    currentState: record.state,
  },
  output: {
    action: record.decision.action,
    semanticTarget: record.decision.target?.conceptId ?? null,
    instruction: record.decision.instruction,
    expectedTransition: record.expectedTransition,
    successCondition: record.successCondition,
    safety: record.safety,
  },
  provenance: record.verification,
}));
await writeFile(path.join(output, 'guidance-semantic-sft.jsonl'), examples.map((item) => JSON.stringify(item)).join('\n') + (examples.length ? '\n' : ''), 'utf8');
await writeFile(path.join(output, 'manifest.json'), `${JSON.stringify({
  schemaVersion: 'showwhere-semantic-export-v1', exportedAtUtc: new Date().toISOString(),
  humanApprovedRecords: approved.length, evaluationRecordsIncluded: 0,
  coordinatesIncluded: false,
  note: 'This is fine-tuning-ready data only. No model training was performed.',
}, null, 2)}\n`, 'utf8');
console.log(`Exported ${approved.length} human-approved Semantic v2 steps to ${output}`);
