import { defineConfig } from '@hey-api/openapi-ts';

export default defineConfig({
  input: './openapi.json',
  output: {
    path: './src/client',
  },
  plugins: [
    {
      name: '@hey-api/typescript',
      enums: false,
    },
    {
      name: '@hey-api/sdk',
    },
    {
      name: '@hey-api/client-fetch',
    },
  ],
});
