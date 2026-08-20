# Three of Spades Backend

ASP.NET Core API + SignalR + **PostgreSQL**, with a pure **game engine** library (rules v1.0). JWT login/signup, optional Google/GitHub OAuth, and a display **UserName** used at the table.

## Run

```bash
docker compose up -d postgres
dotnet test
dotnet run --project src/ThreeOfSpades.Api
```

- API: http://localhost:5203  
- Swagger: http://localhost:5203/swagger  
- SignalR hub: `/hubs/game?access_token=<jwt>`

## Auth

| Method | Endpoint |
|--------|----------|
| Register | `POST /api/auth/register` `{ email, password, userName }` |
| Login | `POST /api/auth/login` `{ email, password }` |
| Current user | `GET /api/auth/me` |
| Set display name | `PUT /api/auth/username` `{ userName }` |
| Google OAuth | `GET /api/auth/google` |
| GitHub OAuth | `GET /api/auth/github` |

OAuth redirects to `http://localhost:5173/oauth?token=...`. Set `Authentication:Google:*` and `Authentication:GitHub:*` in `appsettings.json`. Callback URLs:

- `http://localhost:5203/signin-google`
- `http://localhost:5203/signin-github`

`userName` is what other players see. Password signup requires it up front; OAuth users get a suggested name they can change.

## Rooms & game (matches the UI)

All room routes need `Authorization: Bearer <token>`.

- `GET /api/rooms` list  
- `POST /api/rooms` create  
- `POST /api/rooms/join` `{ code }`  
- `GET /api/rooms/{id}` lobby, history, stats  
- `POST /api/rooms/{id}/ready`  
- `POST /api/rooms/{id}/bots` fill dummy players (owner)  
- `POST /api/rooms/{id}/kick` / `transfer` / `archive` / `leave`  
- `POST /api/rooms/{id}/start` deal + bidding  
- `POST /api/rooms/{id}/game/bid` `{ amount }`  
- `POST /api/rooms/{id}/game/pass`  
- `POST /api/rooms/{id}/game/select` `{ trump, conditions }`  
- `POST /api/rooms/{id}/game/play` `{ cardId }`  
- `GET /api/rooms/{id}/game` private snapshot (your hand only)

Hub methods: `JoinRoom`, `PlaceBid`, `PassBid`, `Select`, `PlayCard`, `Heartbeat`. Events: `gameUpdated`, `roomUpdated`, `notice`.

Hands and trick-point totals stay hidden in snapshots until the hand is complete. Partners reveal when the named card is played. After a raise, the high bidder wins when everyone else passes.

## Layout

- `src/ThreeOfSpades.Engine` — deck, bidding, trump, partners, tricks, scoring (no I/O)  
- `src/ThreeOfSpades.Api` — JWT, OAuth, rooms, live games, SignalR  
- `tests/ThreeOfSpades.Engine.Tests`
