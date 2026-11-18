# C# Networking: TCP Group Chat

[![Build & Test](https://github.com/miguelrg14/VS_CSharp-Networking/actions/workflows/dotnet.yml/badge.svg)](https://github.com/miguelrg14/VS_CSharp-Networking/actions/workflows/dotnet.yml)

Coursework from the Networks and Communications subject of my Computer Science & Video Game Engineering degree
at ESNE, refactored into a single .NET 10 solution: a multi-client **TCP group chat** (server and client) whose
history is stored in a **SQLite** database, plus the exercises that led to it: threads, a raw-socket echo server
and shared state protected with locks.
Everything is covered by unit and end-to-end tests that run on every push.

Part 3 of my university C# work. Also see
[VS_CSharp-Fundamentals](https://github.com/miguelrg14/VS_CSharp-Fundamentals) (console and data structures) and
[VS_CSharp-WinForms-Games](https://github.com/miguelrg14/VS_CSharp-WinForms-Games) (OOP and Windows Forms).

## The chat

```
$ Chat.Client                         $ Chat.Client
Welcome! Choose a nickname:           Welcome! Choose a nickname:
ana                                   ana
Hi ana! Online: ana.                  The nickname 'ana' is already in use. Try another one:
(no messages yet)                     ben
* ben joined the chat *               Hi ben! Online: ana, ben.
hola a todos                          --- last 2 messages ---
                                      * ana joined the chat *
                                      ana: hola a todos
```

- **Any number of clients**: each connection is served by its own asynchronous task, so a slow client never blocks the rest.
- **Nicknames** are validated and unique (case-insensitive); a taken one is rejected and the client can try again.
- **History in SQLite**: every message is saved in `chat-history.db`, so the conversation survives server restarts.
  New members receive the last messages, like opening a group in a messaging app; `/history` shows them again.
- **Search and statistics** run as SQL queries: `/search <text>` finds old messages and `/stats` lists the most
  active users (`GROUP BY` + `COUNT`).
- **Commands**: `/who` (who is online), `/history`, `/search <text>`, `/stats`, `/help`, `/quit`. Joins and departures
  are announced to everyone.
- **Robust**: abrupt disconnections are detected and announced, and one broken connection never stops a broadcast.

## Projects

| Project | Type | What it shows |
| --- | --- | --- |
| [`src/Chat.Core`](src/Chat.Core) | Class library | The protocol, the thread-safe chat room and history, the async `TcpListener` server and the client |
| [`src/Chat.Storage.Sqlite`](src/Chat.Storage.Sqlite) | Class library | `SqliteMessageStore`: schema creation and versioning, parameterized queries, search and statistics with Microsoft.Data.Sqlite |
| [`src/Chat.Server`](src/Chat.Server) | Console app | Runs the server: `Chat.Server [port] [--db <file> \| --no-db]` (default port 12000, database `chat-history.db`), stops cleanly with Ctrl+C |
| [`src/Chat.Client`](src/Chat.Client) | Console app | Connects to it: `Chat.Client [host] [port]` (default localhost 12000) |
| [`src/NetworkingBasics`](src/NetworkingBasics) | Console app | The steps before the chat, in course order: threads running in parallel, one short-lived thread per name you type (stopped early with a `CancellationToken`), and an echo server written with the raw `Socket` API (`Bind`, `Listen`, `Accept`, `Receive`, `Send`) |
| [`tests/*`](tests) | xUnit | Unit tests plus end-to-end tests that start a real server on a free port and connect real clients |

## Download

Ready-to-run builds for Windows, Linux and macOS are on the
[Releases page](https://github.com/miguelrg14/VS_CSharp-Networking/releases/latest).
Extract the archive, start `Chat.Server`, then open `Chat.Client` in two or more terminals. To chat between computers
on the same network, run `Chat.Client <server-ip>` (the server's firewall must allow port 12000).
No .NET installation is needed. On macOS the apps are not signed: the first time, right-click the file and choose *Open*.

## Getting started

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/miguelrg14/VS_CSharp-Networking.git
cd VS_CSharp-Networking

dotnet run --project src/Chat.Server          # terminal 1
dotnet run --project src/Chat.Client          # terminals 2, 3...
dotnet run --project src/NetworkingBasics     # the earlier exercises

dotnet test
```

In Visual Studio, open `Networking.sln`, then *Configure Startup Projects* → *Multiple startup projects* to start the
server and a client together.

## Design notes

- **Message framing**: TCP is a byte stream, so one `Receive` can return half a message or two messages glued
  together. Every message is a line of UTF-8 text; a line reader always splits them correctly. The raw-socket echo
  server does the same by hand with a small buffer (`SocketLineReader`).
- **Thread safety**: the member list and the history are shared by every connection, so they are only touched under
  a lock. Messages are sent outside the lock, and writes to one connection go through a semaphore so two broadcasts
  never mix their bytes.
- **Encoding**: UTF-8 on both sides, so accents and emoji arrive intact between Windows, Linux and macOS.
- **Storage behind an interface**: the server only knows `IMessageStore`. The in-memory and the SQLite stores pass
  the same contract tests, so either can be used (`--no-db` switches to memory).
- **Safe SQL**: every query uses parameters, never text glued into the SQL, so a message such as
  `'; DROP TABLE messages; --` is stored and searched as plain text. `LIKE` wildcards (`%`, `_`) are escaped, so
  searching "100%" finds that exact text.
- **Schema versioning**: the database records its schema version (`PRAGMA user_version`); the server creates the
  tables on first run and refuses a database made by a newer version instead of corrupting it.
- **Testable**: the chat room works with an `IChatMember` interface, so tests use fake members; the server accepts
  port 0, so tests run a real server on whatever port is free, and one test restarts it to check the history is kept.

## Continuous integration

- Every push and pull request builds the solution and runs the tests.
- Pushing a version tag (`git tag v1.0.0 && git push origin v1.0.0`) publishes trimmed, self-contained executables
  for Windows, Linux and macOS to Releases, after checking that the published chat actually works and that the
  history survives a server restart.

## License

[MIT](LICENSE)
