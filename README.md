# SioSub

Dalamud API15 plugin for listening to Socket.IO events and printing received text messages to in-game chat.

## Build

```powershell
dotnet build -c Release
```

The plugin output is in `bin\Release`:

- `SioSub.dll`
- `SioSub.json`
- `SocketIOClient.dll`
- `SocketIO.Core.dll`
- `SocketIO.Serializer.Core.dll`
- `SocketIO.Serializer.SystemTextJson.dll`

## Usage

Open the config window with `/siosub`.

SioSub connects after a character has entered the game. Logging out disconnects active subscriptions, and logging in with another character reconnects them so `${DATACENTER}` and `${SERVER}` are refreshed.

Show current connection status:

```text
/siosub status
```

The default subscription points at the local test server:

- Server: `http://127.0.0.1:3000`
- Event: `siosub-test`
- Tag: `LOCAL`

Each subscription connects to one Socket.IO server. A subscription can contain multiple listened events, and each event has its own chat channel and tag. Messages are printed as:

```text
[TAG]text
```

Event and tag fields support variables:

- `${DATACENTER}`: current data center name
- `${SERVER}`: current server name

For example, `ProFate_${DATACENTER}` resolves to the current data-center-specific event name.

## Server Protocol

SioSub does not send any data to the server after connecting. It only registers local handlers with `client.On(eventName, ...)` and receives events from the server.

Connection lifecycle messages are written to Dalamud logs (`/xllog`) only. They are not printed to the game chat window unless you explicitly run `/siosub status`.

Each configured event name is resolved with variables before listening. For example:

```text
ProFate_${DATACENTER}
```

can become:

```text
ProFate_LuXingNiao
```

Payloads can be plain strings, numbers, booleans, or JSON objects. For JSON objects, SioSub displays the first available field from `text`, `message`, or `content`; otherwise it displays the whole object as text.

## Local Test Server

Install the Node.js helper dependencies:

```powershell
npm install
```

Start the local Socket.IO server:

```powershell
npm run test:server
```

Or run the script directly:

```powershell
node .\test_socketio_server.js
```

By default it binds to `0.0.0.0:3000`, so it listens on all local network interfaces. The plugin can still connect through `http://127.0.0.1:3000`.

The local server sends one `siosub-test` event every 5 seconds. It does not require or expect any client-side `join` event:

```json
{
  "text": "Local test message #1",
  "timestamp": "2026-05-10T00:00:00+00:00"
}
```
