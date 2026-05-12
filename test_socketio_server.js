const http = require("node:http");
const { Server } = require("socket.io");

function parseArgs(argv) {
  const args = {
    host: "0.0.0.0",
    port: 3000,
    path: "/socket.io",
    event: "siosub-test",
    interval: 5000,
  };

  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    const next = argv[i + 1];

    if (arg === "--host" && next) {
      args.host = next;
      i += 1;
    } else if (arg === "--port" && next) {
      args.port = Number.parseInt(next, 10);
      i += 1;
    } else if (arg === "--path" && next) {
      args.path = next.startsWith("/") ? next : `/${next}`;
      i += 1;
    } else if (arg === "--event" && next) {
      args.event = next;
      i += 1;
    } else if (arg === "--interval" && next) {
      args.interval = Number.parseFloat(next) * 1000;
      i += 1;
    } else if (arg === "--help" || arg === "-h") {
      printHelp();
      process.exit(0);
    }
  }

  if (!Number.isFinite(args.port) || args.port <= 0) {
    throw new Error("--port must be a positive number.");
  }

  if (!Number.isFinite(args.interval) || args.interval <= 0) {
    throw new Error("--interval must be a positive number.");
  }

  return args;
}

function printHelp() {
  console.log(`Local Socket.IO broadcast server for SioSub.

Options:
  --host <host>          Host to bind. Default: 0.0.0.0
  --port <port>          Port to bind. Default: 3000
  --path <path>          Socket.IO path. Default: /socket.io
  --event <event>        Event name to emit. Default: siosub-test
  --interval <seconds>   Broadcast interval. Default: 5
`);
}

const args = parseArgs(process.argv.slice(2));

const server = http.createServer((request, response) => {
  if (request.url === "/") {
    response.writeHead(200, { "content-type": "text/plain; charset=utf-8" });
    response.end("SioSub local Socket.IO server is running.\n");
    return;
  }

  response.writeHead(404, { "content-type": "text/plain; charset=utf-8" });
  response.end("Not found.\n");
});

const io = new Server(server, {
  path: args.path,
  cors: {
    origin: "*",
  },
});

let counter = 0;

io.on("connection", (socket) => {
  console.log(`[connect] ${socket.id}`);

  socket.onAny((event, ...payload) => {
    console.log(`[event] ${socket.id} ${event}: ${JSON.stringify(payload)}`);
  });

  socket.on("disconnect", (reason) => {
    console.log(`[disconnect] ${socket.id}: ${reason}`);
  });
});

setInterval(() => {
  counter += 1;
  const payload = {
    text: `Local test message #${counter}`,
    timestamp: new Date().toISOString(),
  };

  io.emit(args.event, payload);
  console.log(`[broadcast] ${args.event}: ${payload.text}; clients=${io.engine.clientsCount}`);
}, args.interval);

server.listen(args.port, args.host, () => {
  console.log(`Serving Socket.IO on http://${args.host}:${args.port} path=${args.path} event=${args.event}`);
  if (args.host === "0.0.0.0") {
    console.log(`Local access: http://127.0.0.1:${args.port}`);
  }
});

function shutdown() {
  console.log("Stopping Socket.IO server.");
  io.close();
  server.close(() => process.exit(0));
}

process.on("SIGINT", shutdown);
process.on("SIGTERM", shutdown);
