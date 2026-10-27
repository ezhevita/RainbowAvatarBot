{
  description = "RainbowAvatarBot Telegram bot";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixpkgs-unstable";

  outputs = { self, nixpkgs }:
    let
      systems = [ "x86_64-linux" "aarch64-linux" "aarch64-darwin" ];
      forAllSystems = nixpkgs.lib.genAttrs systems;
    in
    {
      packages = forAllSystems (system:
        let
          pkgs = import nixpkgs { inherit system; };
          bot = pkgs.callPackage ./nix/package.nix { };
        in
        {
          default = bot;
          rainbow-avatar-bot = bot;
        });

      apps = forAllSystems (system: {
        default = {
          type = "app";
          program = "${self.packages.${system}.default}/bin/RainbowAvatarBot";
          meta.description = "Run RainbowAvatarBot";
        };
      });

      devShells = forAllSystems (system:
        let pkgs = import nixpkgs { inherit system; };
        in {
          default = pkgs.mkShell {
            packages = [ pkgs.dotnetCorePackages.sdk_10_0 pkgs.ffmpeg-headless pkgs.postgresql_18 ];
            DOTNET_SYSTEM_GLOBALIZATION_INVARIANT = "false";
          };
        });
    };
}
