import { describe, expect, it } from 'vitest';
import {
  classifyInteractionRisk,
  normalizeUiName,
  normalizeWebRole,
  redactWebText,
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
    expect(semanticLabelFromRules('Delivering to Lyndhurst Update location')).toBe('delivery_location');
    expect(semanticLabelFromRules('Track Package')).toBe('order_tracking');
    expect(semanticLabelFromRules('배송 조회')).toBe('order_tracking');
    expect(semanticLabelFromRules('Account & Lists')).toBe('account');
    expect(semanticLabelFromRules('Hello, sign in Account & Lists')).toBe('login');
  });

  it('removes private URL components and blocks side-effecting controls', () => {
    expect(safeUrlForStorage('https://user:pass@example.com/orders?q=secret#item'))
      .toBe('https://example.com/orders');
    expect(safeUrlForStorage('https://example.com/orders/123-1234567-1234567?token=secret'))
      .toBe('https://example.com/orders/:id');
    expect(classifyInteractionRisk('Place order', 'button')).toBe('blocked');
    expect(classifyInteractionRisk('Orders', 'link', undefined, 'https://example.com/orders')).toBe('safe');
  });

  it('redacts authenticated commerce identifiers without removing functional labels', () => {
    expect(redactWebText('Order 123-1234567-1234567')).toBe('Order [order-id]');
    expect(redactWebText('Visa ending in 1234')).toBe('Visa [payment]');
    expect(redactWebText('Deliver to Jane Doe | Account')).toBe('Deliver to [name] | Account');
    expect(redactWebText('123 Main Street')).toBe('[address]');
    expect(redactWebText('Track package')).toBe('Track package');
  });
});
