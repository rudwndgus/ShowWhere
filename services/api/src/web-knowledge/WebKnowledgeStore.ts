import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { isAbsolute, join, resolve } from 'node:path';
import {
  CommonWebPatternsFileSchema,
  WebKnowledgeCatalogSchema,
  type CommonWebPattern,
  type WebKnowledgeCatalog,
} from '../../../../src/web-knowledge';

export interface WebKnowledgeSource {
  catalogs: WebKnowledgeCatalog[];
  patterns: CommonWebPattern[];
}
export function loadWebKnowledge(directory: string, debug = false): WebKnowledgeSource {
  const root = isAbsolute(directory) ? directory : resolve(process.cwd(), directory);
  const catalogs: WebKnowledgeCatalog[] = [];
  const catalogDirectory = join(root, 'catalogs');
  if (existsSync(catalogDirectory)) {
    for (const name of readdirSync(catalogDirectory).filter((value) => value.endsWith('.json')).sort()) {
      try {
        catalogs.push(WebKnowledgeCatalogSchema.parse(JSON.parse(readFileSync(join(catalogDirectory, name), 'utf8'))));
      } catch (error) {
        if (debug) console.warn(`[showwhere:web-knowledge] skipped ${name}: ${error instanceof Error ? error.message : String(error)}`);
      }
    }
  }
  const patternPath = join(root, 'patterns', 'common.json');
  let patterns: CommonWebPattern[] = [];
  if (existsSync(patternPath)) {
    try { patterns = CommonWebPatternsFileSchema.parse(JSON.parse(readFileSync(patternPath, 'utf8'))).patterns; }
    catch (error) {
      if (debug) console.warn(`[showwhere:web-knowledge] skipped common patterns: ${error instanceof Error ? error.message : String(error)}`);
    }
  }
  if (debug) console.log(`[showwhere:web-knowledge] catalogs=${catalogs.length} patterns=${patterns.length}`);
  return { catalogs, patterns };
}
