# LCP.BE — Backend for Local Cinema Player

## Overview

.NET 9 ASP.NET Core web API that serves video files from a local disk to a web-based video player. Uses a JSON file as the metadata store.

## Architecture (Clean Architecture)

| Layer | Project | Subfolders | Purpose |
|---|---|---|---|
| Presentation | `LCP.API` | `Controllers/`, `Middleware/`, `BackgroundServices/` | REST API controllers, middleware, config |
| Business Logic | `LCP.BLL` | `Interfaces/`, `Services/`, `DTOs/` | Services, DTOs, mapping |
| Data Access | `LCP.DAL` | `Interfaces/`, `Repositories/`, `Configuration/` | Repositories, JSON file I/O |
| Domain | `LCP.Domain` | `Entities/` | Entities only |

**Dependency flow:** `API → BLL → DAL → Domain` (no reverse dependencies)

**Notes:**
- `LCP.BLL/Helpers/` contains `FFProbeHelper.cs`, `SearchHelper.cs`
- No test project exists yet

## Data Model

### `VideoMetadata` (LCP.Domain/Entities/)
```csharp
Id              : string   (GUID, auto-generated)
RelativePath    : string   (relative to LibraryRootPath)
SystemName      : string   (file name without extension)
NameEn          : string   (English title, empty until filled via PATCH)
NameLocal       : string   (local language title, empty until filled via PATCH)
CollectionId    : string?  (optional grouping, like a series/collection)
EpisodeNumber   : int      (default -1)
Type            : VideoType (enum: Anime=0, Film=1)
Tags            : List<string>
ThumbnailTimecode : double (default -1; seek position for thumbnail generation)
Duration        : double   (total seconds, set via ffmpeg on seed/sync)
LastTimeWatched : DateTime? (tracked via PATCH for StatisticsMode ordering)
PreviewSlices   : List<PreviewSlice> (5 × 5s segments spread across video for preview compilation)
```

### `PreviewSlice` (LCP.Domain/Entities/)
```csharp
class PreviewSlice {
    Start    : double  (seconds)
    Duration : double  (seconds)
}
```

Static methods:
- `CalculateSlices(double duration)` — evenly distributes 5 × 5s segments with 10s margin at start, 5s at end
- `CalculateRandomSlices(double duration)` — same count but randomizes each slice start within zones; used by `POST /regenerate-slices`

### `VideoType` (LCP.Domain/Entities/)
```csharp
enum VideoType { Anime = 0, Film = 1 }
```

### `SiteSettings` (LCP.Domain/Entities/)
```csharp
class SiteSettings {
    Theme          : string (default "dark")
    AnimeSpeedUp   : bool
    WarmCache      : bool   (pre-generate thumbnails/previews on list fetches)
    RandomSort     : bool   (randomize video order for list endpoints; stable seed per server start + setting toggle)
    Debug          : bool
    StatisticsMode : bool   (order videos by LastTimeWatched ascending)
    VideoTypeFilter : List<VideoType> (empty = show all; filters GET /api/videos and GET /api/videos/paged)
}
```

### JSON Files (stored in `{LibraryRootPath}\SYSTEMFILES\`)

**`library.json`** — video metadata array:
```json
[
  {
    "id": "a1b2c3d4-...",
    "relativePath": "Movies\\Inception\\inception.mkv",
    "systemName": "inception",
    "nameEn": "Inception",
    "nameLocal": "",
    "collectionId": "Sci-Fi",
    "episodeNumber": -1,
    "type": 1,
    "tags": ["sci-fi"]
  }
]
```

**`tags.json`** — master tag list array:
```json
["sci-fi", "thriller"]
```

**`productionInfo.json`** — studio/studio list array:
```json
["Studio A", "Studio B"]
```

**`settings.json`** — site settings object:
```json
{
  "theme": "dark",
  "animeSpeedUp": false,
  "warmCache": false,
  "randomSort": false,
  "debug": false,
  "statisticsMode": false
}
```

## Configuration (`appsettings.json`)

```json
{
  "LibrarySettings": {
    "LibraryRootPath": "D:\\Media",
    "PasswordHash": "",
    "PasswordSalt": "",
    "SmartVideoGrouping": false,
    "MaxSyncDeletionRatio": 0.5
  }
}
```

- System file names (`library.json`, `tags.json`, `settings.json`) are hardcoded constants in `LibrarySettings.cs` — resolved under `{LibraryRootPath}\SYSTEMFILES\` via `ResolveSystemFilePath()`
- `LibraryRootPath` — root directory for video files. Full paths resolved as `LibraryRootPath + video.RelativePath`.
- `PasswordHash` / `PasswordSalt` — optional; base64 PBKDF2-SHA256 hash (100000 iterations, 32-byte output) and its base64 16-byte salt, checked by `POST /api/settings/check-password`. When either is empty the password gate is disabled: the fallback policy is satisfied without a session, `check-password` and `session` return `true`, and the frontend skips the prompt. Set both to require a login. Generate them with `POST /api/settings/hash-password` (Development only) and paste the result into `appsettings.json`. When `SHARED_CONFIG_PATH` is used, `ValidateSharedConfig` requires both to be non-empty
- `SmartVideoGrouping` — when `true`, automatically groups videos by common system name prefix on seed/sync (see Smart Video Grouping below)
- `MaxSyncDeletionRatio` — optional (default `0.5`); fraction of entries sync may prune in one pass. Above it, pruning is skipped and logged at error level. Ignored for libraries with fewer than 10 entries and for values outside `(0, 1)`. Exempt from the `ValidateSharedConfig` presence check

## DTOs (LCP.BLL/DTOs/)

| DTO | Fields | Purpose |
|---|---|---|
| `VideoDto` | mirrors `VideoMetadata` | API response for video data |
| `UpdateVideoRequest` | all fields nullable (string?, int?, VideoType?, List&lt;string&gt;?, double?) | PATCH body — only non-null fields are applied |
| `PagedResult<T>` | `Items`, `Page`, `PageSize`, `TotalCount`, `TotalPages` (computed) | Generic paginated response |
| `CollectionDto` | `Id` (string), `Count` (int) | Collection listing |
| `SettingsDto` | mirrors `SiteSettings` | Site settings response |
| `PreviewResolution` | enum `Preview144`, `Preview360` | Preview quality selector |
| `PreviewResult` | `record(byte[] Data, DateTime LastModified)` | Preview clip with etag support |
| `ThumbnailResult` | `record(byte[] Data, DateTime LastModified)` | Thumbnail frame with etag support |

## API Endpoints

| Method | Route | Description |
|---|---|---|
| GET | `/api/videos` | List all videos (including deleted, marked with `isDeleted`) |
| GET | `/api/videos/paged?page=1&pageSize=20&tags=sci-fi&tags=thriller` | Paginated list (non-deleted only). Optional `tags` param filters by any matching tag |
| GET | `/api/videos/{id}` | Get single video (even if deleted) |
| PATCH | `/api/videos/{id}` | Update metadata fields (NameEn, NameLocal, CollectionId, EpisodeNumber, Type, Tags, ThumbnailTimecode, LastTimeWatched) |
| GET | `/api/videos/{id}/similar?page=1&pageSize=20` | Paginated similar videos by tag overlap (see Scoring Algorithm below) |
| GET | `/api/videos/{id}/stream` | Stream full video file with range processing support (Content-Type mapped by extension) |
| GET | `/api/videos/{id}/thumbnail?t=30&noCache=false` | Return JPEG thumbnail frame (image/jpeg). `t` seeks to a specific second; omit for stored ThumbnailTimecode |
| POST | `/api/videos/{id}/regenerate-slices` | Regenerate random preview slices for a video |
| GET | `/api/videos/{id}/preview?resolution=144` | Return 25s MP4 preview clip (video/mp4, supports Range). Resolution: `Preview144` or `Preview360` |
| GET | `/api/tags` | List all master tags |
| POST | `/api/tags` | Add a tag (body: plain string) |
| DELETE | `/api/tags/{tag}` | Remove a tag (also strips from all videos) |
| GET | `/api/collections?page=1&pageSize=20` | Paginated list of collection IDs with video count |
| GET | `/api/collections/{collectionId}/videos?page=1&pageSize=20` | Paginated videos in a collection |
| GET | `/api/settings` | Get site settings (Theme, AnimeSpeedUp, WarmCache, Debug, StatisticsMode) |
| PUT | `/api/settings` | Update site settings |
| GET | `/api/settings/gate-enabled` | Whether a password gate is configured; never returns the hash, salt or its length. `[AllowAnonymous]` |
| POST | `/api/settings/check-password` | Log in: verifies the password against `PasswordHash`/`PasswordSalt` and issues the session cookie; returns `true` without a cookie when the gate is disabled. `[AllowAnonymous]` |
| POST | `/api/settings/hash-password` | Development only: returns `PasswordHash`/`PasswordSalt` for a submitted password; `404` in other environments. Requires authentication |
| POST | `/api/settings/logout` | Clear the session cookie |
| GET | `/api/settings/session` | Whether the caller is authenticated, or `true` when the gate is disabled. `[AllowAnonymous]` |
| GET | `/api/videos/random` | Return a random video |
| POST | `/api/videos/new` | Upload a new video file (`IFormFile`, no size limit) |
| GET | `/api/production-info` | List all studios |
| GET | `/api/production-info/info` | Detailed production info with video counts |
| POST | `/api/production-info` | Add a studio (body: plain string) |
| DELETE | `/api/production-info/{studio}` | Remove a studio |
| POST | `/api/sync` | Trigger library synchronization (re-runs sync logic) |
| GET | `/api/system/export/info` | Backup metadata (JSON with byte/video counts) |
| GET | `/api/system/export` | Download full backup as ZIP archive |
| POST | `/api/system/shutdown` | Graceful server shutdown (via `IHostApplicationLifetime`) |

## Startup Jobs (`LCP.API/BackgroundServices/`)

| Service | Order | Description |
|---|---|---|
| `LibrarySeedService` | 1st | Creates `SYSTEMFILES` folder if missing; if JSON file is empty, scans `LibraryRootPath` for video files and populates it; seeds tags file from existing video tags; creates default settings.json if missing; sets default ThumbnailTimecode (2s) for each video; runs Smart Video Grouping when enabled |
| `LibrarySyncService` | 2nd | Backs up `library.json` to `SYSTEMFILES\backups\library-{yyyyMMdd-HHmmss}.json` (last 10 kept; skipped when the file is empty or malformed); bidirectional sync: removes entries whose file is missing from disk unless the share of missing entries exceeds `MaxSyncDeletionRatio`; adds new JSON entries for files found on disk; fills missing `PreviewSlices`; strips orphaned tags (not in master tag list); runs Smart Video Grouping when enabled |

Both run once on startup. `IVideoRepository` is Singleton so the in-memory cache is shared across all consumers.

## Similar Videos Scoring Algorithm

Uses `ScoreAndInterleave` in `VideoService.cs:202`:
1. For each other video, count matching tags and compute overlap percentage: `matchCount / max(queryTagCount, videoTagCount)`
2. Sort by `matchCount` descending then by `percent` descending → list A
3. Sort by `percent` descending then by `matchCount` descending → list B
4. Round-robin interleave from A and B, deduplicating by ID

## Warm Cache Behavior

When `WarmCache` is enabled in site settings, `VideosController.GetAll()` and `CollectionsController.GetVideos()` asynchronously pre-generate thumbnails and 144p previews for returned videos using `Parallel.ForEachAsync` with `MaxDegreeOfParallelism = 2`. This runs in the background (fire-and-forget with `_ = WarmCacheAsync(...)`).

## Smart Video Grouping

When `SmartVideoGrouping: true` in `appsettings.json`, the seed and sync jobs automatically assign `CollectionId` to videos based on their `SystemName`:

1. Videos with an existing `CollectionId` are **never touched**
2. For each video without a `CollectionId`, a "clean name" is derived by:
   - Lowercasing
   - Stripping `ep` / `ep{NUMBER}` / `ep {NUMBER}` patterns (case insensitive, word-boundary aware)
   - Stripping trailing numbers
   - Trimming whitespace
3. Videos with the same clean name form a **group**; the clean name becomes the group's `CollectionId`
4. Single-video groups have their `CollectionId` set to `"default"`
5. Videos with empty or unparsable `SystemName` also go into `"default"`
6. **Prefix matching**: a solo video whose clean name starts with a multi-video group's key is absorbed into that group

**Example:**
```
funny video cat 1        → clean: "funny video cat"    → group "funny video cat"
funny video cat ep 5     → clean: "funny video cat"    → group "funny video cat"
funny video dog ep1      → clean: "funny video dog"    → group "funny video dog"
funny video dog and puppet → clean: "funny video dog and puppet" → prefix match → group "funny video dog"
funny video bird         → clean: "funny video bird"   → group "default"
```

All videos are included in grouping logic.

## Key Conventions

- **Create endpoint** — `POST /api/videos/new` uploads video files; JSON file is also managed via seed/sync services
- **Trigram search** — `GET /api/videos?search=` uses trigram fuzzy matching on video names (not substring comparison)
- **Thumbnails** — generated on demand via `FFMpegConverter.GetVideoThumbnail()`; cached in memory (`ConcurrentDictionary<string, byte[]>` keyed by video ID, max 100 entries, FIFO eviction). Cache invalidated on PATCH (ThumbnailTimecode) or `?noCache=true`. Supports `?t=` for frame-at-timecode query without caching.
- **Previews** — generated on demand via `FFMpegConverter.ConvertMedia` (segments) + `ConcatMedia` compilation (25s clip, 144p/360p, no audio, ultrafast preset); cached in memory keyed by `{id}_{resolution}`. Single-slice previews use direct conversion without temp files.
- **Thread safety** — `JsonVideoRepository`, `JsonTagRepository`, `JsonSettingsRepository` use `SemaphoreSlim(1,1)` per instance. Reads return detached deep copies (`VideoMetadata.Clone()`, `SiteSettings.Clone()`), so caller mutations never reach the cache until `SaveAllAsync`/`UpdateAsync`
- **Video streaming** — uses ASP.NET Core `PhysicalFile` with `enableRangeProcessing: true` for seek support; maps file extensions to MIME types
- **CORS** — restricted to an explicit origin list from `Cors:AllowedOrigins` (defaults to `http://localhost:4200` when absent or empty); any header/method allowed, credentials allowed. Both supported deployments are same-origin (dev proxy via `LCP.FE/proxy.conf.json`, prod SPA served from `wwwroot`), so no wildcard origin is needed
- **Global error handling** — `ExceptionHandlingMiddleware` catches unhandled exceptions, logs them with path/method, returns JSON `{ error, statusCode }` with 500. If the response has already started (streaming endpoints: export, video stream/preview), it logs and calls `context.Abort()` instead of writing — the client sees a broken transfer rather than a truncated body with `200 OK`. Client disconnects (`OperationCanceledException` with `RequestAborted`) are logged at information level, not as errors
- **Logging** — Serilog to console only (no file output)
- **Nullable enabled** — follow `?` annotations for nullable reference types
- **No comments in code** — keep source files clean
- **RandomSort** — when enabled, videos in `GET /api/videos`, `GET /api/videos/paged`, and `GET /api/collections/{id}/videos` are shuffled deterministically using a seed that persists per server start and regenerates when RandomSort is toggled off→on. This guarantees no duplicates or gaps across pagination requests since the order is stable for the same seed.
- **Authentication** — cookie scheme (`lcp_session`, HttpOnly, SameSite=Lax, 7-day sliding expiration). A fallback authorization policy requires an authenticated user on every MVC endpoint, so new controllers are protected by default; only `gate-enabled`, `check-password` and `session` opt out with `[AllowAnonymous]`. Unauthenticated API calls get `401`/`403` instead of a login redirect. Static files, the SPA fallback and Swagger are middleware and stay reachable
- **Password check** — PBKDF2-SHA256 derivation compared with `CryptographicOperations.FixedTimeEquals` (`LCP.BLL/Helpers/PasswordHasher.cs`); returns `401` when it does not match. Never store or compare a plain-text password
- **Password gate** — `PasswordGate.IsEnabled` (`LCP.BLL/Helpers/PasswordGate.cs`) is the single source of truth for "is a password configured". The fallback policy is `PasswordGateRequirement`, satisfied by an authenticated user or by a disabled gate, so an unlocked UI and an open API always agree. Never disable the gate in the UI alone

## Build & Run

```powershell
dotnet build
dotnet run --project LCP.API
```

Profiles: `http` (port 5107), `https` (port 7162) — see `Properties/launchSettings.json`.

Swagger UI at `/swagger` — development environment only (`ASPNETCORE_ENVIRONMENT=Development`); returns 404 in production.

## Package Dependencies

- `Microsoft.AspNetCore.OpenApi` 9.0.6 (LCP.API)
- `Swashbuckle.AspNetCore` 7.3.1 (LCP.API)
- `Serilog.AspNetCore` 9.0.0 (LCP.API)
- `Serilog.Sinks.File` 6.0.0 (LCP.API — declared but not used in code; console-only)
- `Microsoft.Extensions.Options` 9.0.3 (LCP.DAL)
- `Microsoft.Extensions.Logging.Abstractions` 9.0.3 (LCP.BLL)
- `NReco.VideoConverter` 1.2.1 (LCP.BLL — bundles ffmpeg/ffprobe binaries)

## Project References

```
LCP.API → LCP.BLL, LCP.DAL
LCP.BLL → LCP.DAL, LCP.Domain
LCP.DAL → LCP.Domain
LCP.Domain → (none)
```

## Service and Repository Layer Overview

### DAL Interfaces (`LCP.DAL/Interfaces/`)

| Interface | Methods |
|---|---|
| `IVideoRepository` | `GetAllRawAsync()`, `GetByIdAsync(id)`, `GetByCollectionIdAsync(id)`, `GetAllCollectionIdsAsync()` → List&lt;(string Id, int Count)&gt;, `GetPagedAsync(page, pageSize)`, `SaveAllAsync(videos)` |
| `ITagRepository` | `GetAllAsync()`, `AddAsync(tag)`, `RemoveAsync(tag)` |
| `ISettingsRepository` | `GetAsync()`, `UpdateAsync(settings)` |

### BLL Interfaces (`LCP.BLL/Interfaces/`)

| Interface | Methods |
|---|---|
| `IVideoService` | `GetAllAsync()`, `GetPagedAsync(page, pageSize, tags?)`, `GetByIdAsync(id)`, `GetByCollectionIdAsync(id, page, pageSize)`, `GetAllCollectionIdsAsync(page, pageSize)`, `UpdateAsync(id, request)`, `ResolveFilePathAsync(id)`, `RegenerateSlicesAsync(id)`, `GetSimilarAsync(id, page, pageSize)` |
| `ITagService` | `GetAllAsync()`, `AddAsync(tag)`, `RemoveAsync(tag)`, `ExistsAllAsync(tags)` — validates all tags exist in master list |
| `ISettingsService` | `GetAsync()`, `UpdateAsync(settings)` |
| `IThumbnailService` | `GetThumbnailAsync(videoId)` — cached, `GetThumbnailPreviewAsync(videoId, timecode)` — uncached, `InvalidateCache(videoId)` |
| `IPreviewService` | `GetPreviewAsync(videoId, resolution)` — cached, `InvalidateCache(videoId)` — clears all resolutions |
| `ISmartGroupingService` | `GroupVideosAsync()` |
