import 'dotenv/config';
import process from 'node:process';
import { loadApiConfig } from '../services/api/src/config';
import { ModelRouter, type ModelPurpose } from '../services/api/src/providers/ModelRouter';

const config = loadApiConfig(process.env);
if (!config.featherless) {
  console.error('Model check requires SHOWWHERE_AI_MODE=featherless and server-only Featherless settings.');
  process.exitCode = 1;
} else {
  const router = new ModelRouter({
    guideModel: config.featherless.model,
    guideFallbackModel: config.featherless.guideFallbackModel,
    reasoningModel: config.featherless.reasoningModel,
    visionModels: config.featherless.visionModels,
    fastModel: config.featherless.fastModel,
    embeddingModel: config.featherless.embeddingModel,
    learningGeneratorModel: config.featherless.learningGeneratorModel,
    learningJudgeModels: config.featherless.learningJudgeModels,
  });
  const configured = router.configuredModels();
  const seen = new Map<string, Set<ModelPurpose>>();
  for (const { model, purpose } of configured) {
    const roles = seen.get(model) ?? new Set<ModelPurpose>();
    roles.add(purpose);
    seen.set(model, roles);
  }

  let unavailable = 0;
  for (const [model, purposes] of seen) {
    const endpoint = `${config.featherless.baseUrl.replace(/\/$/u, '')}/models?q=${encodeURIComponent(model)}&per_page=100`;
    try {
      const response = await fetch(endpoint, {
        headers: { Authorization: `Bearer ${config.featherless.apiKey}`, 'User-Agent': 'ShowWhere/0.2' },
      });
      if (!response.ok) {
        unavailable += 1;
        console.log(`[UNAVAILABLE] ${model} roles=${[...purposes].join(',')} status=${response.status}`);
        continue;
      }
      const body = await response.json() as { data?: Array<{
        id?: string;
        status?: string;
        available_on_current_plan?: boolean;
        availability?: { tier?: string; is_hot_live?: boolean };
      }> };
      const exact = body.data?.find((candidate) => candidate.id === model);
      if (!exact) {
        unavailable += 1;
        console.log(`[UNAVAILABLE] ${model} roles=${[...purposes].join(',')} exact catalog ID not found`);
        continue;
      }
      const plan = exact.available_on_current_plan === false ? 'plan=no' : 'plan=yes-or-unknown';
      const availability = exact.availability?.tier ?? exact.status ?? 'unknown';
      console.log(`[OK] ${model} roles=${[...purposes].join(',')} ${plan} availability=${availability}`);
    } catch {
      unavailable += 1;
      console.log(`[ERROR] ${model} roles=${[...purposes].join(',')} catalog request failed`);
    }
  }
  if (unavailable > 0) process.exitCode = 1;
}
