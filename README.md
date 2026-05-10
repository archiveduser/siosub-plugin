# SioSub

Dalamud API15 plugin for subscribing to Socket.IO rooms and printing received text messages to in-game chat.

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

Each subscription connects to one Socket.IO server. A subscription can contain multiple rooms, and each room has its own chat channel and tag. Messages are printed as:

```text
[TAG]text
```

Room and tag fields support variables:

- `${DATACENTER}`: current data center name
- `${SERVER}`: current server name

For example, `room-${SERVER}` resolves to the current server-specific room name.

## Server Protocol

SioSub emits the configured join event, default `join`, once for every resolved room name after connecting.

It receives messages in either of these forms:

- A shared message event, default `message`, with payload `{ "room": "room-name", "text": "hello" }`.
- A room-named event, for example event `room-Titan`, with payload `"hello"` or `{ "text": "hello" }`.

If a subscription has exactly one room, a plain payload on the shared message event is routed to that room.
