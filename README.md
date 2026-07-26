# LCP.BE — Local Cinema Player (Backend)

REST API for serving local video files to [LCP.FE](https://github.com/anomalyco/LCP.FE). Manages video metadata, streaming, thumbnails, previews, and collections — all backed by JSON files on disk (no database required).

ASP.NET Core 9.0 with Clean Architecture.

## Quick Start

```bash
dotnet build
dotnet run --project LCP.API
```

API available at `http://localhost:5107`. Swagger UI at `/swagger`.

## Configuration

Edit `LCP.API/appsettings.json`:

```json
{
  "LibrarySettings": {
    "LibraryRootPath": "D:\\Media",
    "Password": "",
    "SmartVideoGrouping": false
  }
}
```

| Setting | Description |
|---|---|
| `LibraryRootPath` | Root directory containing your video files |
| `Password` | Optional password for frontend auth gate (plain-text) |
| `SmartVideoGrouping` | Auto-group videos into collections by filename pattern |

## Architecture

```
LCP.API      → Presentation (controllers, middleware, background services)
LCP.BLL      → Business logic (services, DTOs, helpers)
LCP.DAL      → Data access (JSON file repositories)
LCP.Domain   → Entities (VideoMetadata, SiteSettings, VideoType)
```

Dependency flow: `API → BLL → DAL → Domain` (no reverse dependencies).

## API Endpoints

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/videos/paged` | Paginated video list with tag/type/search filtering |
| `GET` | `/api/videos/random` | Random video |
| `GET` | `/api/videos/{id}` | Single video |
| `PATCH` | `/api/videos/{id}` | Update metadata (partial update) |
| `POST` | `/api/videos/new` | Upload a video file |
| `GET` | `/api/videos/{id}/stream` | Stream video (Range request support) |
| `GET` | `/api/videos/{id}/thumbnail` | JPEG thumbnail (ETag/304) |
| `GET` | `/api/videos/{id}/preview` | 25s MP4 preview clip (144p/360p) |
| `GET` | `/api/videos/{id}/similar` | Similar videos by tag overlap |
| `POST` | `/api/videos/{id}/regenerate-slices` | Randomize preview slices |
| `GET` | `/api/collections` | List collections with video counts |
| `GET` | `/api/collections/{id}/videos` | Videos in a collection |
| `GET` | `/api/tags` | List master tags |
| `POST` | `/api/tags` | Add a tag |
| `DELETE` | `/api/tags/{tag}` | Remove a tag (strips from all videos) |
| `GET` | `/api/production-info` | List studios |
| `POST` | `/api/production-info` | Add a studio |
| `DELETE` | `/api/production-info/{studio}` | Remove a studio |
| `GET` | `/api/settings` | Get site settings |
| `PUT` | `/api/settings` | Update site settings |
| `POST` | `/api/settings/check-password` | Validate password |
| `POST` | `/api/sync` | Trigger library sync |
| `GET` | `/api/system/export` | Download ZIP backup |
| `POST` | `/api/system/shutdown` | Graceful server shutdown |

## Data Storage

All metadata lives in JSON files under `{LibraryRootPath}/SYSTEMFILES/`:

| File | Content |
|---|---|
| `library.json` | Video metadata array |
| `tags.json` | Master tag list |
| `productionInfo.json` | Studio list |
| `settings.json` | Site settings |

No database or ORM — repositories use in-memory caching with `SemaphoreSlim` for thread safety.

## Features

- **Thumbnail & preview generation** — on-demand via ffmpeg, cached in memory (max 100 entries, FIFO eviction)
- **Warm cache** — optionally pre-generates thumbnails/previews in background on list endpoints
- **Trigram search** — fuzzy matching on video names, not substring comparison
- **Similar videos** — scored by tag overlap with dual-rank interleaving
- **Smart grouping** — auto-assigns collections based on filename patterns
- **Deterministic random sort** — stable shuffle per server start for consistent pagination
- **Streaming** — `PhysicalFile` with range processing for browser seek support
- **Global error handling** — unhandled exceptions return structured JSON error responses

## Build & Run

```powershell
dotnet build
dotnet run --project LCP.API
```

Profiles: `http` (5107), `https` (7162) — see `LCP.API/Properties/launchSettings.json`.

## Dependencies

| Package | Version | Purpose |
|---|---|---|
| NReco.VideoConverter | 1.2.1 | ffmpeg/ffprobe wrapper for thumbnails, previews, streaming |
| Swashbuckle.AspNetCore | 7.3.1 | Swagger UI |
| Serilog.AspNetCore | 9.0.0 | Console logging |
| Microsoft.Extensions.Options | 9.0.3 | Configuration binding |
