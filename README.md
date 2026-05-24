# TinyFileIO

**TinyFileIO** is a lightweight, self-hosted, S3-compatible object storage server built with ASP.NET Core. It is designed for developers and teams who want full control over their file storage without depending on cloud providers.

## Features

- **S3-compatible API** — drop-in replacement for Amazon S3; works with any S3 client library, SDK, or tool (AWS CLI, boto3, s3cmd, rclone, and others)
- **Plain filesystem storage** — objects are stored as ordinary files in a regular directory tree, with no proprietary sidecar files or metadata blobs alongside them. This means your data is always directly accessible: browse it in a file manager, copy it with `robocopy` or `rsync`, open it in any application, or share the directory over a network share — no special tooling required
- **Web management UI** — built-in Blazor interface for managing buckets, browsing objects, and administering users
- **User management** — create and manage multiple users, each with their own S3 access key and secret; supports a static single-account mode for simple deployments
- **Multipart upload** — supports large file uploads via the S3 multipart upload protocol
- **Background jobs** — async background processing with job history, including remote S3 download tasks
- **SQLite by default** — zero-dependency metadata storage; no external database required
- **Docker-ready** — official image available on Docker Hub; single command to get started

## Docker

### Pull and run

Image is available on Docker Hub as [`radzap/tinyfileio`](https://hub.docker.com/r/radzap/tinyfileio).

- **Port:** `8080` (HTTP) — map to any port on the host; S3 clients typically use `9000` or `4566`
- **Volume:** `/datastore` — persistent storage for uploaded files and the SQLite database
- **Static account:** controlled via `UseStaticAccount`, `StaticUser`, and `StaticPassword` environment variables

#### Basic run (dynamic accounts, data volume)

```bash
docker run -d --name TinyFileIO --restart unless-stopped -p 9000:8080 -v tinyfileio-data:/datastore radzap/tinyfileio
```

#### With static credentials `iouser` / `iopassword`

```bash
docker run -d --name TinyFileIO --restart unless-stopped -p 9000:8080 -v tinyfileio-data:/datastore -e UseStaticAccount=true -e StaticUser=iouser -e StaticPassword=iopassword radzap/tinyfileio
```

### Data & database

By default, SQLite is stored in the `/datastore` folder inside the container (`/datastore/TinyFileIO.db`). Mount the volume to persist data between container restarts.

The database connection string can be overridden with the `ConnectionStrings__DefaultConnection` environment variable:

```bash
-e ConnectionStrings__DefaultConnection="Data Source=/some/other/path/TinyFileIO.db"
```

## Development

The solution targets **.NET 10** and is built with:

- **ASP.NET Core 10** — web framework and middleware pipeline
- **Blazor Server** — interactive web management UI with server-side rendering
- **Entity Framework Core 10** with **SQLite** — metadata persistence and migrations
- **xUnit** — unit test framework

### Build

```cmd
dotnet build TinyFileIO/TinyFileIO.csproj -c Release
```

### Run locally

```cmd
dotnet run --project TinyFileIO/TinyFileIO.csproj
```

### Test

```cmd
dotnet test TinyFileIO.Tests/TinyFileIO.Tests.csproj
```

### Database migrations (EF Core)

Migrations are applied automatically on startup. To apply them manually without starting the server, pass the `--migrate` flag:

```cmd
dotnet run --project TinyFileIO/TinyFileIO.csproj -- --migrate
```

This is useful in CI/CD pipelines or container init steps where you want to migrate the database as a separate step before the application starts.

To create a new migration during development:

```cmd
dotnet ef migrations add <MigrationName> --project TinyFileIO
dotnet ef database update --project TinyFileIO
```

## License

TinyFileIO is licensed under the **GNU Affero General Public License v3.0 (AGPL-3.0)**. See [LICENSE.txt](LICENSE.txt) for the full text.

**TL;DR:** You are free to use, modify, and distribute this software even commercially. If you run a modified version as a network service (e.g., a hosted server), you must make the modified source code available to users of that service also under AGPL-3.0.
