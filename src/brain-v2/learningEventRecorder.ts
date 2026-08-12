import { mkdir, appendFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { LearningEventV2Schema, TrajectoryV2Schema, type LearningEventV2, type TrajectoryV2 } from './contracts';
import type { LearningEventRecorder } from './interfaces';
import { scrubForLearning } from './scrubber';

export class JsonlLearningEventRecorder implements LearningEventRecorder {
  private readonly eventsPath: string;
  private readonly trajectoriesPath: string;
  private readonly outcomesPath: string;

  constructor(root: string) {
    const absoluteRoot = resolve(root);
    this.eventsPath = resolve(absoluteRoot, 'events.jsonl');
    this.trajectoriesPath = resolve(absoluteRoot, 'trajectories.jsonl');
    this.outcomesPath = resolve(absoluteRoot, 'outcome-updates.jsonl');
  }

  private async write(path: string, value: unknown): Promise<void> {
    await mkdir(dirname(path), { recursive: true });
    await appendFile(path, `${JSON.stringify(scrubForLearning(value))}\n`, 'utf8');
  }

  async append(event: LearningEventV2): Promise<void> {
    await this.write(this.eventsPath, LearningEventV2Schema.parse(event));
  }

  async appendTrajectory(trajectory: TrajectoryV2): Promise<void> {
    await this.write(this.trajectoriesPath, TrajectoryV2Schema.parse(trajectory));
  }

  async updateOutcome(eventId: string, patch: Partial<LearningEventV2>): Promise<void> {
    await this.write(this.outcomesPath, {
      schemaVersion: 'showwhere-learning-outcome-v2',
      eventId,
      timestamp: new Date().toISOString(),
      patch,
    });
  }
}

