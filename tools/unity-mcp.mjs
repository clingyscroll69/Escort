#!/usr/bin/env node
// Minimal MCP stdio client for the mcp-unity server (the server isn't loaded as a native tool in this session).
// Usage:
//   node unity-mcp.mjs list                         -> list tools
//   node unity-mcp.mjs call <tool> '<json-args>'    -> call one tool
//   node unity-mcp.mjs resource <uri>               -> read a resource
//   node unity-mcp.mjs batch '<json [{tool,args}]>' -> several calls over one connection
import { pathToFileURL } from 'node:url';
const PKG = '/Users/sapnagoel/Documents/coding/Game/Escort/Library/PackageCache/com.gamelovers.mcp-unity@382a43a30f4d/Server~';
const sdk = (p) => import(pathToFileURL(`${PKG}/node_modules/@modelcontextprotocol/sdk/dist/esm/${p}`).href);
const { Client } = await sdk('client/index.js');
const { StdioClientTransport } = await sdk('client/stdio.js');

const transport = new StdioClientTransport({
  command: 'node',
  args: [`${PKG}/build/index.js`],
  env: {
    ...process.env,
    MCP_UNITY_SETTINGS_PATH: '/Users/sapnagoel/Documents/coding/Game/Escort/ProjectSettings/McpUnitySettings.json',
    MCP_UNITY_AUTH_TOKEN_PATH: '/Users/sapnagoel/Documents/coding/Game/Escort/Library/McpUnity/bridge-token',
    UNITY_REQUEST_TIMEOUT: process.env.UNITY_REQUEST_TIMEOUT || '120',
  },
  stderr: 'ignore',
});
const client = new Client({ name: 'claude-code-cli', version: '1.0.0' });
await client.connect(transport);

const out = (o) => console.log(typeof o === 'string' ? o : JSON.stringify(o, null, 2));
const fmt = (res) => {
  if (res?.content) return res.content.map((c) => (c.type === 'text' ? c.text : JSON.stringify(c))).join('\n') + (res.isError ? '\n[isError]' : '');
  return res;
};
const [cmd, a1, a2] = process.argv.slice(2);
try {
  if (cmd === 'list') {
    const r = await client.listTools();
    for (const t of r.tools) out(`${t.name}: ${t.description?.split('\n')[0]}\n   params: ${JSON.stringify(t.inputSchema?.properties ? Object.keys(t.inputSchema.properties) : [])}`);
  } else if (cmd === 'schema') {
    const r = await client.listTools();
    out(r.tools.find((t) => t.name === a1));
  } else if (cmd === 'call') {
    out(fmt(await client.callTool({ name: a1, arguments: a2 ? JSON.parse(a2) : {} }, undefined, { timeout: 600000 })));
  } else if (cmd === 'resource') {
    const r = await client.readResource({ uri: a1 });
    out(r.contents.map((c) => c.text ?? JSON.stringify(c)).join('\n'));
  } else if (cmd === 'resources') {
    out((await client.listResources()).resources.map((r) => r.uri + '  ' + (r.name || '')).join('\n'));
  } else if (cmd === 'batch') {
    for (const { tool, args } of JSON.parse(a1)) {
      out(`### ${tool}`);
      out(fmt(await client.callTool({ name: tool, arguments: args || {} }, undefined, { timeout: 600000 })));
    }
  } else {
    out('unknown command');
  }
} catch (e) {
  out('ERROR: ' + (e?.message || e));
  process.exitCode = 1;
} finally {
  await client.close();
}
