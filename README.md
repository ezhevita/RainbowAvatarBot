# RainbowAvatarBot

Telegram bot for adding pride flags to avatars, stickers and images.

## PostgreSQL

User settings are stored in PostgreSQL. Create a database and role for the bot,
then configure the connection string through the environment (for Nix, Docker,
and `dotnet run`):

```sh
export ConnectionStrings__PostgreSQL='Host=localhost;Database=rainbow_avatar_bot;Username=rainbow_avatar_bot;Password=your-password'
```

The bot creates its `user_settings` table before connecting to Telegram. The role
needs permission to create tables in its schema and read/write the table. Missing
configuration or an unavailable database stops startup. New users retain the
defaults: LGBT flag, 50% opacity, and HardLight blending. Every settings change is
committed before the bot confirms it; concurrent changes to different settings
do not overwrite each other.

### Import existing settings

Stop the old bot and back up `data/config.json`. Before starting the new bot, use
`psql` to import that JSON into the new database. Run from the repository root,
adjusting the database connection and JSON file path to your installation:

```sh
psql 'host=localhost dbname=rainbow_avatar_bot user=rainbow_avatar_bot' \
  --set=ON_ERROR_STOP=1 \
  --set=settings_json="$(cat data/config.json)" \
  --file=RainbowAvatarBot/Database/import-json.sql
```

The import creates the table if needed and runs in a transaction. Existing
database rows are preserved, so rerunning it does not overwrite newer settings.
The bot no longer reads or writes `data/config.json`; keep the backup until you
have verified the imported settings.

## Nix

The flake provides a package, runnable app, and development shell for x86-64 and
ARM64 Linux and Apple Silicon macOS. It uses .NET 10 and FFmpeg from the pinned
nixpkgs input;
FFmpeg includes the libvpx VP9 codec and filters used by the Docker build.

Build and run with Nix (with `nix-command` and `flakes` enabled):

```sh
nix build path:.
Bot__Token='your-telegram-bot-token' Bot__OwnerId='your-telegram-user-id' nix run path:.
```

Set `ConnectionStrings__PostgreSQL` as described above before running the bot.
Run from a writable directory: generated flag images are saved in `images/`,
relative to the working directory. The package
loads its bundled `appsettings.json` and sticker asset from the Nix store.
Override configuration with .NET environment variables such as `Bot__Token`,
`Bot__OwnerId`, and `Logging__LogLevel__Default`, or command-line arguments:

```sh
nix run path:. -- --Bot:EnableBlendModeSettings=false
```

For development:

```sh
nix develop path:.
cd RainbowAvatarBot
dotnet run
```

The development shell also includes PostgreSQL 18 (`psql`, `initdb`, and
`pg_ctl`). PostgreSQL runs separately; the bot package does not start a database
server.

After changing NuGet package references, regenerate the dependency hashes:

```sh
nix build path:.#default.fetch-deps --out-link /tmp/rainbow-avatar-bot-fetch-deps
/tmp/rainbow-avatar-bot-fetch-deps "$PWD/nix/deps.json"
nix build path:.
```

Update nixpkgs with `nix flake update --flake path:.` and regenerate the NuGet
dependency hashes afterward if the SDK changes. The explicit `path:.` references
also work before the new flake files have been added to Git; once tracked, the
usual `nix build`, `nix run`, and `nix develop` commands work as well.
