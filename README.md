# Graveyard Keeper 2 Co-op

This mod adds co-op play to Graveyard Keeper 2 (Windows, Steam). Two to four players play in the
same world. This world is the world of the host.

This mod is not official. The developers and the publisher of the game did not make it.

This version is a beta version. Please [report problems](../../issues).

> **CAUTION:** Make a backup of your saves before you use this mod.

> **NOTE:** Your friends can join only after your first sleep in the game. When you sleep for the
> first time, the game makes its first save. The mod copies the world of the host from this save.

## Install the mod

1. Close the game.
2. Download the zip file from [Releases](../../releases).
3. Open the game folder. In Steam, right-click the game and select **Manage > Browse local files**.
4. Extract all files from the zip file into the game folder, next to `GraveyardKeeper2.exe`.
5. If Windows asks about files with the same name, replace the files.
6. Start the game. The main menu shows a new button: **Co-op**.

All players must use the same version of the mod. If the versions are different, the mod shows a
message with the two version numbers.

The zip file contains an installation guide in eleven languages: `INSTALL-GK2COOP.txt` and the
folder `GK2Coop-Guides`.

## Host a game

1. In the main menu, select **Co-op > Host a game**.
2. Select the number of players (2, 3 or 4).
3. Select **Host on Steam**.
4. Load a save, or start a new game.
5. In a new game, sleep one time. Then the game has its first save.
6. Push **F10** to invite your Steam friends.

## Join a game

1. Accept the invite in Steam. Or, in your Steam friends list, select **Join game** for the host.
2. Wait until the mod copies the world of the host. Then the game loads this copy.

You play in a copy of the world of the host. The mod does not change your own saves.

## Play without Steam

1. The host selects **Co-op > Host a game > Host without Steam > Host**.
2. The host selects **Show my addresses** and gives one address to the other players.
3. Each other player selects **Co-op > Join a game > Join by address**.
4. Each other player types the address and selects **Join**.

> **NOTE:** Over the internet, the router of the host must forward UDP port 8889. Or use a VPN,
> for example Tailscale.

## Shared data

All players share the world:

- Objects that the players cut, mine or harvest
- Items on the ground
- Chests
- Graves and the bodies in them
- Quests
- Crafting stations and their products
- Repairs, cleared paths and doors
- Gardens
- Vendors and their stock
- The tech tree and the recipes
- The reputation and the congregation
- Buildings
- Zombies and conveyors (the host controls them)
- The weather, the action gems and the chat

Each player has their own inventory, tool belt, money, energy, health, appearance, talents and
position.

The night passes when all players sleep. The host can change this rule: with **I sleep**, the
night passes when the host sleeps.

When a story scene or a sermon starts for one player, the mod asks the other players if they want
to watch it.

## Fights

One player fights. Only one fight at a time is possible in a world. During a fight:

- The other players get a message about the fight and about its result.
- The other players cannot start a fight.
- The time stops for all players.

All players share the progress of the fight level.

## Limits of this version

- Your friends can join only after the first save of the game (the first sleep).
- Players cannot fight together.
- Players cannot watch the fight of a different player.
- The mod does not show the answers that a different player selects in a conversation. You can
  see the lines of the conversation near that player.
- The mod does not share burials and autopsies that are in progress. Only one player at a time
  can work at a grave or at an autopsy table.
- Only one player at a time must trade with a vendor. If two players trade with the same vendor
  at the same time, both players can get the last item of the vendor.
- When the host stops the game, the session stops for all players.
- Most tests used a local network. There are fewer tests between two homes over the internet and
  with late-game worlds.
- After an update of the game, wait for a version of the mod for that update.

## Keys

| Key | Function |
| --- | --- |
| T | Write in the chat |
| F6 | Show or hide the status |
| F7 | Show or hide the name tags |
| F10 | Invite Steam friends |
| F11 | Change your appearance |

You can use the **Co-op** menu with a controller. In a game, the menu **Pause > Co-op** has the
functions of the F keys.

## Report a problem

Open an [issue](../../issues). Give this data:

1. What you did, and what occurred.
2. The version of the mod. The **Co-op** menu shows the version.
3. The log file of each player: `BepInEx\LogOutput.log` in the game folder.

> **NOTE:** The game replaces the log file when it starts. Copy the log file immediately after
> the session.

## Uninstall the mod

1. Close the game.
2. In the game folder, delete the folder `BepInEx\plugins\GK2Coop`.
3. Delete the file `INSTALL-GK2COOP.txt` and the folder `GK2Coop-Guides`.
4. To also remove BepInEx, delete the folder `BepInEx` and these files: `winhttp.dll`,
   `doorstop_config.ini`, `.doorstop_version` and `changelog.txt`.

> **CAUTION:** Do step 4 only if no other mod uses BepInEx.

## Build from the source code

You need:

- Windows
- The .NET SDK (the project uses .NET Framework 4.7.2)
- Graveyard Keeper 2
- BepInEx 5.4.23.5 for Windows x64, extracted into the folder `artifacts\bepinex-5.4.23.5`

Do these steps:

1. Run `dotnet build src\GK2Coop\GK2Coop.csproj -c Release`.
2. If the game is not in the default Steam folder, add `-p:GamePath=<game folder>`.
3. To make the zip file for a release, run `scripts\Build-Package.ps1`.

The folder `tests` contains the multiplayer tests. A test plugin (`tests\GameplayProbe`) controls
two to four copies of the game on one PC. The script `tests\Run-Regression.ps1` runs the tests.
The tests use copies of the game on drive D:.

## License

The mod has the MIT license (see [LICENSE](LICENSE)). The zip file also contains BepInEx and its
libraries. They have their own licenses (see [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)).
