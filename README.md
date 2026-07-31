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
| `GET` | `/api/system/export` | Download ZIP backup (videos + system files) |
| `POST` | `/api/system/import` | Upload ZIP backup — clears library, extracts videos + system files |
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
- **Export/Import** — full library backup via ZIP archive (videos + metadata), restorable via import endpoint

## Build & Run

```powershell
dotnet build
dotnet run --project LCP.API
```

Profiles: `http` (5107), `https` (7162) — see `LCP.API/Properties/launchSettings.json`.

## Single-file EXE build (`build-single-exe.ps1`)

Builds the frontend, bundles it into the API and publishes a self-contained single-file `LCP.API.exe` (Windows x64) with a ready-to-edit `appsettings.json` next to it. The result runs without .NET or Node installed on the target machine.

```powershell
.\build-single-exe.ps1 `
    -BackendDir "D:\repos\LCP.BE" `
    -FrontendDir "D:\NodeProject\LCP.FE" `
    -LibraryRootPath "D:\Media" `
    -Password "secret" `
    -Port 5107 `
    -OutputDir "C:\Users\you\Downloads\LCP" `
    -Launch
```

| Parameter | Description | Default |
|---|---|---|
| `BackendDir` | Path to the LCP.BE repo (must contain `LCP.API`) | required |
| `FrontendDir` | Path to the LCP.FE project | required |
| `LibraryRootPath` | Root directory of the video library | required |
| `Password` | Frontend auth password (empty = no auth) | `""` |
| `SmartVideoGrouping` | Auto-group videos into collections by filename pattern | `$true` |
| `Port` | HTTP port to listen on | `5107` |
| `ListenAddress` | Bind address (`0.0.0.0` = all interfaces, `127.0.0.1` = local only) | `0.0.0.0` |
| `OutputDir` | Destination folder for the exe + config | user profile directory |
| `SkipFrontendBuild` | Reuse the existing frontend `dist` instead of rebuilding | `$false` |
| `Launch` | Start the built exe after publishing | `$false` |

Notes:

- **Always pass `-OutputDir`.** The script clears the output folder before copying artifacts into it.
- With an empty `Password` and a non-loopback `ListenAddress`, the script warns that anyone on the LAN can reach the server (including `/api/system/shutdown`).
- When run **as Administrator**, the script adds a Windows Firewall inbound rule for TCP `Port` automatically; otherwise it prints the manual `netsh` command. Note that on a **Public** network profile, Windows Firewall blocks inbound traffic for this app regardless, so use a **Private** profile or flip the app rules to Allow.
- After the build the script prints the local and LAN URLs (`http://<ip>:<port>`).
- The single-file exe extracts ffmpeg next to the executable on first use (falling back to `%LOCALAPPDATA%\LCP\ffmpeg` only if the exe folder is not writable), because NReco cannot resolve its bundled tools inside a single-file assembly.

## Dependencies

| Package | Version | Purpose |
|---|---|---|
| NReco.VideoConverter | 1.2.1 | ffmpeg/ffprobe wrapper for thumbnails, previews, streaming |
| Swashbuckle.AspNetCore | 7.3.1 | Swagger UI |
| Serilog.AspNetCore | 9.0.0 | Console logging |
| Microsoft.Extensions.Options | 9.0.3 | Configuration binding |
