const http = require("node:http");
const { URL } = require("node:url");
const { Server } = require("socket.io");

const HOST = process.env.HOST || "0.0.0.0";
const PORT = Number.parseInt(process.env.PORT || "23000", 10);
const SOCKET_PATH = process.env.SOCKET_PATH || "/socket.io";

function sendJson(response, statusCode, payload) {
  const body = JSON.stringify(payload);
  response.writeHead(statusCode, {
    "content-type": "application/json; charset=utf-8",
    "content-length": Buffer.byteLength(body),
  });
  response.end(body);
}

function collectBody(request) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    request.on("data", (chunk) => chunks.push(chunk));
    request.on("end", () => resolve(Buffer.concat(chunks).toString("utf8")));
    request.on("error", reject);
  });
}

function parseForm(body) {
  const params = new URLSearchParams(body);
  return Object.fromEntries(params.entries());
}

async function readPushPayload(request, requestUrl) {
  const query = Object.fromEntries(requestUrl.searchParams.entries());
  if (request.method === "GET" || request.method === "HEAD") {
    return query;
  }

  const bodyText = await collectBody(request);
  const contentType = request.headers["content-type"] || "";
  if (contentType.includes("application/json")) {
    return { ...query, ...JSON.parse(bodyText || "{}") };
  }

  if (contentType.includes("application/x-www-form-urlencoded")) {
    return { ...query, ...parseForm(bodyText) };
  }

  return { ...query, ...parseForm(bodyText) };
}

const server = http.createServer(async (request, response) => {
  const requestUrl = new URL(request.url || "/", `http://${request.headers.host || "localhost"}`);

  if (requestUrl.pathname === "/") {
    response.writeHead(200, { "content-type": "text/plain; charset=utf-8" });
    response.end("SioSub broadcast server is running.\n");
    return;
  }

  if (requestUrl.pathname !== "/push") {
    sendJson(response, 404, { success: false, error: "Not found" });
    return;
  }

  try {
    const payload = await readPushPayload(request, requestUrl);
    const key = payload.key;
    const value = payload.value;

    if (!key || value === undefined || value === null || value === "") {
      sendJson(response, 400, { success: false, error: "Missing key or value" });
      return;
    }

    io.emit(key, String(value));
    console.log(`[push] event=${key} value=${value} clients=${io.engine.clientsCount}`);
    sendJson(response, 200, { success: true, event: key, value: String(value), clients: io.engine.clientsCount });
  } catch (error) {
    console.error("[push] error", error);
    sendJson(response, 500, { success: false, error: error.message || String(error) });
  }
});

const io = new Server(server, {
  path: SOCKET_PATH,
  cors: {
    origin: "*",
  },
});

io.on("connection", (socket) => {
  console.log(`[connect] ${socket.id}`);

  socket.onAny((event, ...payload) => {
    console.log(`[event] ${socket.id} ${event}: ${JSON.stringify(payload)}`);
  });

  socket.on("disconnect", (reason) => {
    console.log(`[disconnect] ${socket.id}: ${reason}`);
  });
});

server.listen(PORT, HOST, () => {
  console.log(`SioSub broadcast server running on http://${HOST}:${PORT} path=${SOCKET_PATH}`);
  console.log("Push example:");
  console.log(`  http://127.0.0.1:${PORT}/push?key=ProFate_LuXingNiao&value=TestText`);
});

function shutdown() {
  console.log("Stopping broadcast server.");
  io.close();
  server.close(() => process.exit(0));
}

process.on("SIGINT", shutdown);
process.on("SIGTERM", shutdown);
