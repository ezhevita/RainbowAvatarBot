# RainbowAvatarBot

Telegram bot for adding pride flags to avatars, stickers and images.

## Nix

Development shell:

```sh
nix develop path:.
cd RainbowAvatarBot
dotnet run
```

Regenerate the dependency hashes:

```sh
nix build path:.#default.fetch-deps --out-link /tmp/rainbow-avatar-bot-fetch-deps
/tmp/rainbow-avatar-bot-fetch-deps "$PWD/nix/deps.json"
nix build path:.
```

Update nixpkgs:

```sh
nix flake update --flake path:.
```
