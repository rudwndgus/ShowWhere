import 'dotenv/config';
import { loadApiConfig } from './config';
import { createApiServer } from './createApiServer';
import { createAiProvider } from './providers/createAiProvider';

const config = loadApiConfig(process.env);
const provider = createAiProvider(config);
const server = createApiServer(config, provider);

server.listen(config.port, config.host, () => {
  console.log(
    `ShowWhere API listening on http://${config.host}:${config.port} `
    + `(OpenAI fast=${config.openai.fastModel}, balanced=${config.openai.model}, strong=${config.openai.strongModel})`,
  );
});
