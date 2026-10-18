# Database Schema

## Background

The app was first built on **Azure Cosmos DB** (NoSQL). Circuits and lap times were stored as JSON documents:

- **Circuit documents** held the circuit's metadata, an embedded `availableInGames` array, and two embedded lap records: `lapRecordDry` and `lapRecordWet`.
- **Lap time documents** held one lap each: circuit, total time, a `sectorTimes` array, date, weather, and game.

Cosmos has no cheap joins, so the lap record was **denormalized**: the fastest lap was copied into the circuit document so that one read returned everything. This made reads fast but had downsides:

- Each new lap meant comparing it to the stored record and rewriting the circuit document when it was faster.
- The same lap lived in two places, and the copies could drift apart (for example, when a lap was edited or deleted).

## Why this schema in SQLite

In SQLite, joins and window functions are cheap, so the data can be normalized:

- **Circuits and lap times are separate tables.** A circuit's lap record is never stored. It is computed when queried as the fastest lap per circuit, game and weather. This leaves one source of truth and nothing to keep in sync.
- **Records are per game.** Handling and physics differ between games, so a lap in one game does not compete with a lap in another. Cosmos stored a single dry and wet record per circuit, so per-game records would have meant embedding one record per game in every circuit document. Here, they only need `game` in the grouping.
- **Sector times are columns on the lap time.** A lap always has exactly three sectors, so `sector1`, `sector2` and `sector3` columns are simpler than a JSON array or a separate table.
- **Available games is a junction table.** `availableInGames` was a list on the circuit document. In a relational model, a list belongs in its own table (`circuit_games`) with one row per circuit–game pair.
- **The race date belongs to a circuit in a game, not to the circuit.** A circuit used to have a single `date`, the day its race was held, and circuits were ordered by it. That gives the calendar of only one season, but each game follows its own year's calendar. So the date is on `circuit_games`, and a game's circuits are ordered by it. It is a circuit's position when the game's circuits are sorted by date, so adding a race never means renumbering the others.
- **Lap times can only be set in available games.** `laptimes (circuit_id, game)` references `circuit_games`, so the database rejects a lap for a game the circuit isn't in. Cosmos could not enforce this. Circuits and their games are always seeded before any lap times, so this order is never a problem.
- **Lap time is stored, not derived from the sectors.** The game rounds each value on its own, so the three sectors don't always add up to the lap time (e.g. 24.540 + 27.809 + 40.466 = 1:32.815, while the game shows 1:32.816). `time` stays the value the game reported.
- **Lap times get an integer id; circuits keep their text id.** Cosmos needs every document to have a string `id`, so lap ids were built by hand (`lap-jeddah-20260205`). Two laps at the same circuit on the same day would get the same id. In SQLite, `INTEGER PRIMARY KEY` fills itself in automatically, so the old lap ids are dropped. A circuit id (`albert-park`) is already the slug in its URL (`/circuit/albert-park`), so it stays the primary key and the circuit has only one identifier.
- **Cosmos system fields are dropped.** So is the circuit's `type` field (`"circuit"`). It only labelled the kind of document in Cosmos and said nothing about the track itself.

## Tables

### `circuits`

| Column          | Type    | Constraints | Notes                                 |
| --------------- | ------- | ----------- | ------------------------------------- |
| `id`            | TEXT    | PRIMARY KEY | URL slug, e.g. `albert-park`          |
| `name`          | TEXT    | NOT NULL    | e.g. `Jeddah Street Circuit`          |
| `official_name` | TEXT    |             | e.g. `Saudi Arabian GP`               |
| `image_url`     | TEXT    |             | Blob storage URL of the circuit image |
| `rating`        | INTEGER |             | User rating                           |

### `laptimes`

| Column       | Type    | Constraints                                     | Notes                                                               |
| ------------ | ------- | ----------------------------------------------- | ------------------------------------------------------------------- |
| `id`         | INTEGER | PRIMARY KEY                                     | Filled in automatically                                             |
| `circuit_id` | TEXT    | NOT NULL, FK → `circuits.id`, ON DELETE CASCADE |                                                                     |
| `time`       | INTEGER | NOT NULL                                        | Lap time in ms as shown by the game; may differ from the sector sum |
| `sector1`    | INTEGER | NOT NULL                                        | Milliseconds                                                        |
| `sector2`    | INTEGER | NOT NULL                                        | Milliseconds                                                        |
| `sector3`    | INTEGER | NOT NULL                                        | Milliseconds                                                        |
| `date`       | TEXT    | NOT NULL                                        | ISO 8601 date (`YYYY-MM-DD`)                                        |
| `weather`    | TEXT    | NOT NULL, CHECK IN (`dry`, `wet`)               |                                                                     |
| `game`       | TEXT    | NOT NULL                                        | e.g. `F1 2021`                                                      |

Foreign key: `(circuit_id, game)` → `circuit_games (circuit_id, game)`, without a cascade. A game can't be removed from a circuit while that circuit still has lap times for it.

Index: `(circuit_id, game, weather, time)`. It lets the database find the fastest lap per circuit, game and weather without scanning every lap.

### `circuit_games`

| Column       | Type | Constraints                                     | Notes                                                      |
| ------------ | ---- | ----------------------------------------------- | ---------------------------------------------------------- |
| `circuit_id` | TEXT | NOT NULL, FK → `circuits.id`, ON DELETE CASCADE |                                                            |
| `game`       | TEXT | NOT NULL                                        | e.g. `F1 2021`                                             |
| `date`       | TEXT |                                                 | Race date on that game's calendar, ISO 8601 (`YYYY-MM-DD`) |

Primary key: `(circuit_id, game)`.

Unique: `(game, date)`. Two races in one game never share a date, so a clash is a typo. SQLite allows more than one NULL in a unique column, so several circuits in a game can have no date.

## DDL

```sql
PRAGMA foreign_keys = ON;

CREATE TABLE circuits (
  id             TEXT PRIMARY KEY,
  name           TEXT NOT NULL,
  official_name  TEXT,
  image_url      TEXT,
  rating         INTEGER
);

CREATE TABLE circuit_games (
  circuit_id  TEXT NOT NULL REFERENCES circuits(id) ON DELETE CASCADE,
  game        TEXT NOT NULL,
  date        TEXT,
  PRIMARY KEY (circuit_id, game),
  UNIQUE (game, date)
);

CREATE TABLE laptimes (
  id          INTEGER PRIMARY KEY,
  circuit_id  TEXT NOT NULL REFERENCES circuits(id) ON DELETE CASCADE,
  time        INTEGER NOT NULL,
  sector1     INTEGER NOT NULL,
  sector2     INTEGER NOT NULL,
  sector3     INTEGER NOT NULL,
  date        TEXT NOT NULL,
  weather     TEXT NOT NULL CHECK (weather IN ('dry', 'wet')),
  game        TEXT NOT NULL,
  FOREIGN KEY (circuit_id, game) REFERENCES circuit_games (circuit_id, game)
);

CREATE INDEX idx_laptimes_record ON laptimes (circuit_id, game, weather, time);
```

## Notes

- SQLite only enforces foreign keys when `PRAGMA foreign_keys = ON` is set, and it must be set on every connection.
- Seed order: `circuits`, then `circuit_games`, then `laptimes`. When importing from Cosmos, a lap whose game is missing from its circuit's `availableInGames` will be rejected. Add that game to the circuit or fix the lap. Leave the Cosmos lap `id` out of the insert so SQLite assigns a new one.
- When importing, a circuit's old `date` goes on the `circuit_games` row of the game whose calendar it came from. That circuit's rows for other games start without a date.
- A game's calendar is its circuits sorted by `circuit_games.date`. Circuits that are in the game but not on its calendar have no date and come last. The round number is the position in that order:

  ```sql
  SELECT c.*, cg.date,
         ROW_NUMBER() OVER (ORDER BY cg.date) AS round
  FROM circuit_games cg
  JOIN circuits c ON c.id = cg.circuit_id
  WHERE cg.game = ? AND cg.date IS NOT NULL
  ORDER BY cg.date;
  ```

- A circuit has up to two lap records per game, one dry and one wet. Each is the lap with the lowest `time` for that circuit, game and weather. When a circuit has no laps for a game and weather combination, it has no record for it (the old `lapRecordWet: null`).
