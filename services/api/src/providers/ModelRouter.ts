export type ModelPurpose =
  | 'guide'
  | 'reasoning'
  | 'vision'
  | 'fast'
  | 'embedding'
  | 'learning_generator'
  | 'learning_judge';

export interface ModelRouteConfig {
  guideModel: string;
  guideFallbackModel?: string;
  reasoningModel?: string;
  visionModels: readonly string[];
  fastModel?: string;
  embeddingModel?: string;
  learningGeneratorModel?: string;
  learningJudgeModels?: readonly string[];
}

function unique(models: Array<string | undefined>): string[] {
  return [...new Set(models.map((model) => model?.trim()).filter((model): model is string => Boolean(model)))];
}

export class ModelRouter {
  readonly #config: ModelRouteConfig;

  constructor(config: ModelRouteConfig) {
    this.#config = config;
  }

  guide(): string[] {
    return unique([this.#config.guideModel, this.#config.guideFallbackModel]);
  }

  reasoning(): string[] {
    return unique([this.#config.reasoningModel, ...this.guide()]);
  }

  vision(): string[] {
    return unique([...this.#config.visionModels]);
  }

  fast(): string[] {
    return unique([this.#config.fastModel, this.#config.guideModel]);
  }

  embedding(): string[] {
    return unique([this.#config.embeddingModel]);
  }

  learningGenerator(): string[] {
    return unique([this.#config.learningGeneratorModel, this.#config.guideFallbackModel, this.#config.guideModel]);
  }

  learningJudge(): string[] {
    return unique([...(this.#config.learningJudgeModels ?? [])]);
  }

  forPurpose(purpose: ModelPurpose): string[] {
    switch (purpose) {
      case 'guide': return this.guide();
      case 'reasoning': return this.reasoning();
      case 'vision': return this.vision();
      case 'fast': return this.fast();
      case 'embedding': return this.embedding();
      case 'learning_generator': return this.learningGenerator();
      case 'learning_judge': return this.learningJudge();
    }
  }

  configuredModels(): Array<{ purpose: ModelPurpose; model: string }> {
    const purposes: ModelPurpose[] = [
      'guide', 'reasoning', 'vision', 'fast', 'embedding', 'learning_generator', 'learning_judge',
    ];
    return purposes.flatMap((purpose) => this.forPurpose(purpose).map((model) => ({ purpose, model })));
  }
}
