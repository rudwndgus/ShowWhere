import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';

const localAppData = process.env.LOCALAPPDATA;
if (!localAppData) throw new Error('LOCALAPPDATA is required on Windows.');

const trainingDirectory = path.join(localAppData, 'ShowWhere', 'training');
const readJsonl = async (fileName) => {
  try {
    return (await readFile(path.join(trainingDirectory, fileName), 'utf8'))
      .split(/\r?\n/u)
      .filter(Boolean)
      .map((line) => JSON.parse(line));
  } catch (error) {
    if (error?.code === 'ENOENT') return [];
    throw error;
  }
};

const records = (await readJsonl('corrections.jsonl'))
  .filter((record) => record.schemaVersion === 1 && record.developerVerified === true);
const feedback = (await readJsonl('answer-feedback.jsonl'))
  .filter((record) => record.schemaVersion === 1 && ['correct', 'incorrect'].includes(record.rating));
if (records.length === 0 && feedback.length === 0) {
  console.log(`No developer learning data found at ${trainingDirectory}`);
  process.exit(0);
}

const answerExamples = feedback.map((record) => ({
  id: record.id,
  prompt: record.originalGoal || record.effectiveGoal,
  answer: record.answerText,
  label: record.rating,
  context: record.context,
  action: record.action,
  targetId: record.targetId,
  targetLabel: record.targetLabel,
  targetBounds: record.targetBounds,
}));

const intentExamples = records
  .filter((record) => typeof record.correctedIntent === 'string' && record.correctedIntent.trim())
  .map((record) => ({
    id: record.id,
    input: record.originalGoal,
    output: record.correctedIntent,
    context: record.context,
  }));

const groundingExamples = records
  .filter((record) => record.screenshotPath && record.normalizedVisualTarget)
  .map((record) => ({
    id: record.id,
    image: path.resolve(trainingDirectory, record.screenshotPath),
    instruction: record.correctedIntent || record.effectiveGoal,
    context: record.context,
    target: record.normalizedVisualTarget,
    accessibilityTarget: record.correctTarget,
    rejectedTarget: {
      action: record.previousAction,
      targetId: record.previousTargetId,
      label: record.previousTargetLabel,
      bounds: record.previousBounds,
    },
  }));

const stamp = new Date().toISOString().replaceAll(':', '-').replaceAll('.', '-');
const outputDirectory = path.join(trainingDirectory, 'exports', stamp);
await mkdir(outputDirectory, { recursive: true });
const writeJsonl = (name, examples) => writeFile(
  path.join(outputDirectory, name),
  examples.map((example) => JSON.stringify(example)).join('\n') + (examples.length ? '\n' : ''),
  'utf8',
);
await Promise.all([
  writeJsonl('answer-preferences.jsonl', answerExamples),
  writeJsonl('intent-corrections.jsonl', intentExamples),
  writeJsonl('visual-grounding.jsonl', groundingExamples),
  writeFile(
    path.join(outputDirectory, 'summary.json'),
    `${JSON.stringify({
      schemaVersion: 1,
      exportedAtUtc: new Date().toISOString(),
      verifiedRecords: records.length,
      answerRatings: answerExamples.length,
      correctAnswers: answerExamples.filter((example) => example.label === 'correct').length,
      incorrectAnswers: answerExamples.filter((example) => example.label === 'incorrect').length,
      intentExamples: intentExamples.length,
      groundingExamples: groundingExamples.length,
    }, null, 2)}\n`,
    'utf8',
  ),
]);

console.log(`Exported ${feedback.length} answer ratings and ${records.length} verified corrections to ${outputDirectory}`);
