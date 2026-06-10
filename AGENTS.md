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

## Data Model

### `VideoMetadata` (LCP.Domain/Entities/)
```csharp
Id            : string   (GUID, auto-generated)
RelativePath  : string   (relative to LibraryRootPath)
SystemName    : string   (file name without extension)
NameEn        : string   (English title, empty until filled via PATCH)
NameLocal     : string   (local language title, empty until filled via PATCH)
CollectionId  : string?  (optional grouping, like a series/collection)
EpisodeNumber : int      (default -1)
Type          : VideoType (enum: Anime=0, Film=1)
Tags          : List<string>
IsDeleted     : bool     (soft delete flag)
Duration      : double   (total seconds, set via ffmpeg on seed/sync)
PreviewSlices : List<PreviewSlice> (5 × 5s segments spread across video for preview compilation)
```

### `PreviewSlice` (LCP.Domain/Entities/)
```csharp
class PreviewSlice {
    Start    : double  (seconds)
    Duration : double  (seconds)
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
    "tags": ["sci-fi"],
    "isDeleted": false
  }
]
```

**`tags.json`** — master tag list array:
```json
["sci-fi", "thriller"]
```

## Configuration (`appsettings.json`)

```json
{
  "LibrarySettings": {
    "JsonFilePath": "library.json",
    "TagsFilePath": "tags.json",
    "LibraryRootPath": "D:\\Media"
  }
}
```

- `JsonFilePath` / `TagsFilePath` — if relative, resolved under `{LibraryRootPath}\SYSTEMFILES\`
- `LibraryRootPath` — root directory for video files. Full paths resolved as `LibraryRootPath + video.RelativePath`.

## API Endpoints

| Method | Route | Description |
|---|---|---|
| GET | `/api/videos` | List all videos (including deleted, marked with `isDeleted`) |
| GET | `/api/videos/paged?page=1&pageSize=20` | Paginated list (non-deleted only) |
| GET | `/api/videos/{id}` | Get single video (even if deleted) |
| PATCH | `/api/videos/{id}` | Update metadata fields (NameEn, NameLocal, CollectionId, EpisodeNumber, Type, Tags, ThumbnailTimecode) |
| DELETE | `/api/videos/{id}` | Soft delete (sets IsDeleted=true, returns 204) |
| GET | `/api/videos/{id}/stream` | Stream video file (supports HTTP Range for seeking) |
| GET | `/api/videos/{id}/thumbnail?t=30&noCache=false` | Return JPEG thumbnail frame (image/jpeg). `t` seeks to a specific second; omit for stored ThumbnailTimecode |
| GET | `/api/videos/{id}/preview?resolution=144` | Return 25s MP4 preview clip (video/mp4, supports Range). Resolution: `Preview144` or `Preview360` |
| GET | `/api/tags` | List all master tags |
| POST | `/api/tags` | Add a tag (body: plain string) |
| DELETE | `/api/tags/{tag}` | Remove a tag |

## Startup Jobs (`LCP.API/BackgroundServices/`)

| Service | Order | Description |
|---|---|---|
| `LibrarySeedService` | 1st | Creates `SYSTEMFILES` folder if missing; if JSON file is empty, scans `LibraryRootPath` for video files and populates it; seeds tags file from existing video tags |
| `LibrarySyncService` | 2nd | Bidirectional sync: marks JSON entries `IsDeleted=true` when file is missing from disk; adds new JSON entries for files found on disk |

Both run once on startup. `IVideoRepository` is Singleton so the in-memory cache is shared across all consumers.

## Key Conventions

- **No create endpoint** — JSON file is managed via seed/sync services
- **Thumbnails** — generated on demand via `NReco.VideoConverter` frame extraction; cached in memory (`ConcurrentDictionary`). Cache invalidated on PATCH (ThumbnailTimecode) or `?noCache=true`. Supports `?t=` for frame-at-timecode query without caching.
- **Previews** — generated on demand via `NReco.VideoConverter.ConvertMedia` (25s clip, 144p/360p, no audio, ultrafast preset); cached in memory keyed by `{id}_{resolution}`.
- **Soft delete only** — `DELETE` sets `IsDeleted = true`, never removes from file
- **Thread safety** — `JsonVideoRepository` and `JsonTagRepository` use `SemaphoreSlim` per instance
- **Video streaming** — uses ASP.NET Core `PhysicalFile` with `enableRangeProcessing: true` for seek support; maps file extensions to MIME types
- **CORS** — configured to allow any origin (for local web player)
- **Global error handling** — `ExceptionHandlingMiddleware` catches unhandled exceptions, logs them, returns 500
- **Logging** — Serilog to console only (no file output)
- **Nullable enabled** — follow `?` annotations for nullable reference types
- **No comments in code** — keep source files clean

## Build & Run

```powershell
dotnet build
dotnet run --project LCP.API
```

Profiles: `http` (port 5107), `https` (port 7162) — see `Properties/launchSettings.json`.

Swagger UI at `/swagger`.

## Package Dependencies

- `Microsoft.AspNetCore.OpenApi` 9.0.6 (LCP.API)
- `Swashbuckle.AspNetCore` 7.3.1 (LCP.API)
- `Serilog.AspNetCore` 9.0.0 (LCP.API)
- `Microsoft.Extensions.Options` 9.0.3 (LCP.DAL)
- `Microsoft.Extensions.Logging.Abstractions` 9.0.3 (LCP.BLL)
- `NReco.VideoConverter` 1.2.1 (LCP.BLL)

## Project References

```
LCP.API → LCP.BLL, LCP.DAL
LCP.BLL → LCP.DAL, LCP.Domain
(also NuGet: NReco.VideoConverter bundles ffmpeg/ffprobe binaries)
LCP.DAL → LCP.Domain
```
