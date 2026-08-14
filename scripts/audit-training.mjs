import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import process from 'node:process';

const generic = new Set([
  'settings', 'setting', 'window', 'application', 'chrome', 'home', 'page',
  'button', 'menu', 'start', 'unknown', 'unknown_target', 'visual_target',
]);
const normalize = (value) => String(value ?? '').trim().toLowerCase().replace(/[\s.]+/gu, '_');
const hasTag = (record, tag) => Array.isArray(record.issueTags)
  && record.issueTags.some((value) => String(value).toLowerCase() === tag);
const isNegative = (record) => hasTag(record, 'wrong_target')
  || record.learningLabels?.outcomeLabel === 'wrong_target';
const hasCorrectNextStep = (record) => record.correctTarget != null || record.normalizedVisualTarget != null;
const runtimeStatus = (record) => record.knowledgeStatus ?? 'active';
const invalidGoldReason = (record) => {
  if (runtimeStatus(record) !== 'active') return record.humanGold ? 'inactive_contains_human_gold' : null;
  if (isNegative(record) && !hasCorrectNextStep(record)) return 'wrong_target_without_correct_next_step';
  if (!record.humanGold) return null;
  if (generic.has(normalize(record.humanGold.targetConcept))) return 'generic_or_unknown_target';
  if (!record.developerVerified || record.humanGold.developerVerified !== true) return 'unverified_gold';
  return null;
};
const isPositiveCandidate = (record) => {
  if (runtimeStatus(record) !== 'active' || !record.developerVerified) return false;
  const positive = hasTag(record, 'positive_feedback') || record.learningLabels?.outcomeLabel === 'correct_target';
  if (!positive || !hasCorrectNextStep(record)) return false;
  const target = record.correctTarget?.label ?? record.correctTarget?.description
    ?? record.correctTarget?.automationId ?? record.normalizedVisualTarget?.label
    ?? record.learningLabels?.targetConcept;
  if (!generic.has(normalize(target))) return true;
  return Boolean(record.normalizedVisualTarget && record.snapshotHash)
    || Boolean(record.correctTarget?.automationId && !generic.has(normalize(record.correctTarget.automationId)));
};

function parseJsonl(path) {
  if (!existsSync(path)) return [];
  return readFileSync(path, 'utf8').split(/\r?\n/u).filter(Boolean).map((line, index) => {
    try { return JSON.parse(line); }
    catch { return { __invalidJson: true, __line: index + 1, __raw: line }; }
  });
}

function auditDirectory(directory, fix) {
  const path = join(directory, 'corrections.jsonl');
  const records = parseJsonl(path);
  const invalidGold = [];
  let invalidJson = 0;
  let changed = 0;
  for (const record of records) {
    if (record.__invalidJson) { invalidJson += 1; continue; }
    const reason = invalidGoldReason(record);
    if (!reason) continue;
    invalidGold.push({ id: record.id, reason, targetConcept: record.humanGold?.targetConcept });
    if (fix) {
      delete record.humanGold;
      record.knowledgeStatus = 'invalid';
      record.invalidReason = reason;
      changed += 1;
    }
  }

  const validGold = records.filter((record) => !record.__invalidJson
    && runtimeStatus(record) === 'active' && !invalidGoldReason(record)
    && (record.humanGold || isPositiveCandidate(record)));
  const identityCounts = new Map();
  const conflictTargets = new Map();
  for (const record of validGold) {
    const gold = record.humanGold ?? {
      identity: record.id,
      siteOrApplication: record.context?.applicationName,
      normalizedIntent: record.effectiveGoal,
      semanticState: record.learningLabels?.stateId,
      targetConcept: record.learningLabels?.targetConcept,
    };
    identityCounts.set(gold.identity, (identityCounts.get(gold.identity) ?? 0) + 1);
    const key = [gold.siteOrApplication, gold.normalizedIntent, gold.semanticState].join('|');
    const targets = conflictTargets.get(key) ?? new Set();
    targets.add(gold.targetConcept);
    conflictTargets.set(key, targets);
  }
  const duplicates = [...identityCounts.values()].reduce((total, count) => total + Math.max(0, count - 1), 0);
  const conflicts = [...conflictTargets.entries()]
    .filter(([, targets]) => targets.size > 1)
    .map(([key, targets]) => ({ key, targets: [...targets] }));
  const genericGold = validGold.filter((record) => generic.has(normalize(
    record.humanGold?.targetConcept ?? record.learningLabels?.targetConcept)));

  if (fix && changed > 0) {
    const content = records.map((record) => record.__invalidJson ? record.__raw : JSON.stringify(record)).join('\n');
    writeFileSync(path, `${content}${content ? '\n' : ''}`, 'utf8');
  }
  return {
    directory,
    corrections: records.length,
    positiveHumanGold: validGold.length,
    negativeCorrections: records.filter((record) => !record.__invalidJson && isNegative(record)).length,
    semanticDuplicates: duplicates,
    conflictingGoldGroups: conflicts.length,
    conflicts,
    invalidOrGenericGold: invalidGold.length + genericGold.length,
    invalidGold,
    invalidJson,
    invalidated: changed,
  };
}

const repo = resolve(process.cwd(), 'training');
const localAppData = process.env.LOCALAPPDATA;
const candidates = [repo];
if (localAppData) candidates.push(resolve(localAppData, 'ShowWhere', 'training'));
if (process.env.SHOWWHERE_TRAINING_DIR) candidates.push(resolve(process.env.SHOWWHERE_TRAINING_DIR));
const directories = [...new Set(candidates)].filter(existsSync);
const fix = process.argv.includes('--fix');
const results = directories.map((directory) => auditDirectory(directory, fix));
process.stdout.write(`${JSON.stringify({ fix, results }, null, 2)}\n`);
