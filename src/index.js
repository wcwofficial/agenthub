const http = require('http');

const port = process.env.PORT || 8080;

const send = (res, status, data) => {
  res.writeHead(status, { 'Content-Type': 'application/json' });
  res.end(JSON.stringify(data, null, 2));
};

const server = http.createServer((req, res) => {
  if (req.method === 'GET' && req.url === '/health') {
    return send(res, 200, { ok: true, service: 'agenthub', version: '0.1.0' });
  }

  if (req.method === 'GET' && req.url === '/') {
    return send(res, 200, {
      name: 'AgentHub',
      status: 'bootstrapped',
      docs: '/docs/mvp-spec.md',
      next: [
        'Add real API routes',
        'Add persistence',
        'Add auth',
        'Add task polling flow'
      ]
    });
  }

  return send(res, 404, { error: 'Not found' });
});

server.listen(port, () => {
  console.log(`AgentHub listening on :${port}`);
});
