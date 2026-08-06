import { beforeEach, describe, expect, it, vi } from 'vitest';
import { resolveClickableTarget } from './resolveClickableTarget';

function rect(width: number, height: number, x = 0, y = 0): DOMRect {
  return new DOMRect(x, y, width, height);
}

describe('resolveClickableTarget', () => {
  beforeEach(() => {
    document.body.innerHTML = '';
    vi.spyOn(window, 'innerWidth', 'get').mockReturnValue(1_000);
    vi.spyOn(window, 'innerHeight', 'get').mockReturnValue(800);
  });

  it('resolves nested text to its clickable button parent', () => {
    document.body.innerHTML = '<button id="login"><span id="label">Login</span></button>';
    const button = document.querySelector<HTMLElement>('#login')!;
    const label = document.querySelector<HTMLElement>('#label')!;
    vi.spyOn(button, 'getBoundingClientRect').mockReturnValue(rect(120, 44));
    vi.spyOn(label, 'getBoundingClientRect').mockReturnValue(rect(40, 18));
    expect(resolveClickableTarget(label, 'Login').target).toBe(button);
  });

  it('finds the small option button on the right side of a conversation row', () => {
    document.body.innerHTML = `
      <article id="row" class="conversation-row">
        <span id="name">전문 콜로그 번역가</span>
        <button id="options" aria-label="전문 콜로그 번역가의 대화 옵션 열기">•••</button>
      </article>`;
    const row = document.querySelector<HTMLElement>('#row')!;
    const name = document.querySelector<HTMLElement>('#name')!;
    const options = document.querySelector<HTMLElement>('#options')!;
    vi.spyOn(row, 'getBoundingClientRect').mockReturnValue(rect(400, 70, 20, 20));
    vi.spyOn(name, 'getBoundingClientRect').mockReturnValue(rect(180, 30, 60, 40));
    vi.spyOn(options, 'getBoundingClientRect').mockReturnValue(rect(40, 40, 360, 35));
    const result = resolveClickableTarget(name, '전문 콜로그 번역가의 대화 옵션 열기');
    expect(result.target).toBe(options);
    expect(result.optionButtonFound).toBe(true);
  });

  it('does not select a parent that covers almost the full viewport', () => {
    document.body.innerHTML = '<div id="panel" role="button"><span id="child">Settings</span></div>';
    const panel = document.querySelector<HTMLElement>('#panel')!;
    const child = document.querySelector<HTMLElement>('#child')!;
    vi.spyOn(panel, 'getBoundingClientRect').mockReturnValue(rect(950, 500));
    vi.spyOn(child, 'getBoundingClientRect').mockReturnValue(rect(60, 20));
    expect(resolveClickableTarget(child, 'Settings').target).toBe(child);
  });
});
