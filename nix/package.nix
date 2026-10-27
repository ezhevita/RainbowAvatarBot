{ lib, buildDotnetModule, dotnetCorePackages, ffmpeg-headless }:

buildDotnetModule rec {
  pname = "rainbow-avatar-bot";
  version = "4.1";

  src = lib.fileset.toSource {
    root = ../.;
    fileset = lib.fileset.unions [
      ../Directory.Build.props
      (lib.fileset.difference
        (lib.fileset.fileFilter (file: file.name != ".DS_Store") ../RainbowAvatarBot)
        (lib.fileset.unions (map lib.fileset.maybeMissing [
          ../RainbowAvatarBot/bin
          ../RainbowAvatarBot/obj
          ../RainbowAvatarBot/images
        ])))
    ];
  };

  projectFile = "RainbowAvatarBot/RainbowAvatarBot.csproj";
  nugetDeps = ./deps.json;
  dotnet-sdk = dotnetCorePackages.sdk_10_0;
  dotnet-runtime = dotnetCorePackages.runtime_10_0;
  executables = [ "RainbowAvatarBot" ];

  makeWrapperArgs = [
    "--prefix PATH : ${lib.makeBinPath [ ffmpeg-headless ]}"
    "--set DOTNET_SYSTEM_GLOBALIZATION_INVARIANT false"
    "--add-flags --contentRoot"
    "--add-flags ${placeholder "out"}/lib/${pname}"
  ];

  meta = {
    description = "Telegram bot for adding pride flags to avatars and stickers";
    license = lib.licenses.gpl3Only;
    mainProgram = "RainbowAvatarBot";
    platforms = lib.platforms.linux ++ lib.platforms.darwin;
  };
}
