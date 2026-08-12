export interface RobotsPolicy {
  source?: string;
  respected: boolean;
  allows(url: string): boolean;
}
interface RobotsRule {
  allow: boolean;
  pattern: string;
}

function ruleExpression(pattern: string): RegExp {
  const anchored = pattern.endsWith('$');
  const body = (anchored ? pattern.slice(0, -1) : pattern)
    .replace(/[.+?^${}()|[\]\\]/gu, '\\$&')
    .replace(/\*/gu, '.*');
  return new RegExp(`^${body}${anchored ? '$' : ''}`, 'u');
}

function parseRules(contents: string, crawlerName: string): RobotsRule[] {
  const groups: Array<{ agents: string[]; rules: RobotsRule[] }> = [];
  let group: { agents: string[]; rules: RobotsRule[] } | undefined;
  let rulesStarted = false;
  for (const sourceLine of contents.split(/\r?\n/u)) {
    const line = sourceLine.replace(/#.*$/u, '').trim();
    if (!line) continue;
    const separator = line.indexOf(':');
    if (separator < 0) continue;
    const key = line.slice(0, separator).trim().toLowerCase();
    const value = line.slice(separator + 1).trim();
    if (key === 'user-agent') {
      if (!group || rulesStarted) {
        group = { agents: [], rules: [] };
        groups.push(group);
        rulesStarted = false;
      }
      group.agents.push(value.toLowerCase());
      continue;
    }
    if ((key === 'allow' || key === 'disallow') && group) {
      rulesStarted = true;
      if (value) group.rules.push({ allow: key === 'allow', pattern: value });
    }
  }
  const normalizedCrawler = crawlerName.toLowerCase();
  const exact = groups.filter((item) => item.agents.some((agent) => agent !== '*' && normalizedCrawler.includes(agent)));
  const selected = exact.length > 0 ? exact : groups.filter((item) => item.agents.includes('*'));
  return selected.flatMap((item) => item.rules);
}

export async function loadRobotsPolicy(
  seedUrl: string,
  crawlerName = 'ShowWhereCrawler',
  fetcher: typeof fetch = fetch,
): Promise<RobotsPolicy> {
  const robotsUrl = new URL('/robots.txt', seedUrl).toString();
  try {
    const response = await fetcher(robotsUrl, {
      headers: { 'User-Agent': `${crawlerName}/1.0 (+https://github.com/rudwndgus/ShowWhere)` },
      signal: AbortSignal.timeout(8_000),
    });
    if (!response.ok) return { respected: true, allows: () => true };
    const rules = parseRules(await response.text(), crawlerName);
    return {
      source: robotsUrl,
      respected: true,
      allows(value: string): boolean {
        const url = new URL(value);
        const path = `${url.pathname}${url.search}`;
        const matching = rules
          .filter((rule) => ruleExpression(rule.pattern).test(path))
          .sort((left, right) => right.pattern.length - left.pattern.length
            || Number(right.allow) - Number(left.allow));
        return matching[0]?.allow ?? true;
      },
    };
  } catch {
    return { respected: true, allows: () => true };
  }
}
