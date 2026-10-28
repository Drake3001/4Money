// TLS is terminated locally; the production backend still validates HTTPS.
const https = require('node:https');
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const directory = process.env.REFRESH_CERT_DIR || '.cache/banking-refresh';
const port = Number(process.env.REFRESH_HTTPS_PORT || 7443);
const targetPort = Number(process.env.REFRESH_SIMULATION_PORT || 4900);
const server = https.createServer({ key: fs.readFileSync(path.join(directory, 'localhost.key')),
  cert: fs.readFileSync(path.join(directory, 'localhost.crt')) }, (req, res) => {
  const upstream = http.request({ hostname: '127.0.0.1', port: targetPort, path: req.url,
    method: req.method, headers: req.headers }, response => {
    res.writeHead(response.statusCode, response.headers);
    response.pipe(res);
  });
  upstream.on('error', () => { if (!res.headersSent) res.writeHead(502); res.end(); });
  res.on('close', () => upstream.destroy());
  req.pipe(upstream);
});
server.listen(port, '127.0.0.1', () => console.log(`Local Tink TLS fixture listening on port ${port}`));
for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, () => { server.closeAllConnections(); server.close(); });
