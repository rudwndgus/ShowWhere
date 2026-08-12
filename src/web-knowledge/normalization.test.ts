import { describe, expect, it } from 'vitest';
import {
  classifyInteractionRisk,
  normalizeUiName,
  normalizeWebRole,
  safeUrlForStorage,
  semanticLabelFromRules,
} from './normalization';

describe('web knowledge normalization', () => {
  it('normalizes volatile counts and equivalent roles', () => {
    expect(normalizeUiName('  Your Orders (12) ')).toBe('your orders');
    expect(normalizeWebRole('hyperlink')).toBe('link');
    expect(normalizeWebRole('edit')).toBe('textbox');
  });

  it('maps Korean and English UI names to common semantic labels', () => {
    expect(semanticLabelFromRules('Returns & Orders')).toBe('order_history');
    expect(semanticLabelFromRules('Track Package')).toBe('order_tracking');
    expect(semanticLabelFromRules('배송 조회')).toBe('order_tracking');
    expect(semanticLabelFromRules('Account & Lists')).toBe('account');
  });

  it('removes private URL components and blocks side-effecting controls', () => {
    expect(safeUrlForStorage('https://user:pass@example.com/orders?q=secret#item'))
      .toBe('https://example.com/orders');
    expect(classifyInteractionRisk('Place order', 'button')).toBe('blocked');
    expect(classifyInteractionRisk('Orders', 'link', undefined, 'https://example.com/orders')).toBe('safe');
  });
});
