import { loadApiConfig } from './config';
import { createApiServer } from './createApiServer';
import { createAiProvider } from './providers/createAiProvider';
import { loadRepositoryRootEnvironment } from './rootEnvironment';
import { TeachingService } from './teaching/TeachingService';

loadRepositoryRootEnvironment();
const config = loadApiConfig(process.env);
const provider = createAiProvider(config);
const server = createApiServer(config, provider, new TeachingService(config));

server.listen(config.port, config.host, () => {
  console.log(`ShowWhere API listening on http://${config.host}:${config.port} (${config.aiMode} mode)`);
});
