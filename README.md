# Graveyard Keeper 2 Co-op

An unofficial co-op mod for **Graveyard Keeper 2** (Windows, Steam): two to four players share one
world, the host's, and play it together. Not made or endorsed by the game's developers or publisher.

**Beta.** Back up saves you care about, and please [report problems](../../issues).

## For players

Download the zip from [Releases](../../releases), unpack it into the game folder next to
`GraveyardKeeper2.exe`, and start the game: the main menu has a new **Co-op** button. The full
guide, in all eleven of the game's languages, is in the zip (`INSTALL-GK2COOP.txt` and
`GK2Coop-Guides\`). Everyone who plays together needs the same version of the mod.

The host presses Co-op > Host a game; friends join over Steam (invite with **F10**, or "Join game"
in the friends list) or by address. Joiners play in a copy of the host's world; **their own saves
are never touched.**

**Shared by everyone:** felled, mined and harvested objects; items on the ground; chests; graves
and the bodies in them; quests; crafting stations and what they make; repairs, cleared paths and
doors; gardens; vendors and their stock; the tech tree and recipes; reputation and the
congregation; buildings; zombies and conveyors; the weather; the action gems; chat. The night
passes when everyone sleeps (or when the host sleeps, if the host chooses "I sleep"). Story scenes
and sermons are offered to the others, who can watch them together.

**Your own:** inventory, tool belt, money, energy, health, look, talents and position.

**Fights are one player's.** One fight at a time per world. While someone fights, the others are
told who is fighting and how it ended, cannot start a fight of their own, and the clock stops for
everyone. The level's progress and the story steps it triggers are shared.

**Known limits:**

- No fighting together, and no watching another player's fight.
- The answers another player picks in a conversation are not shown (their lines are, nearby).
- Burial and autopsy work in progress is not shared; one player at a time works a grave or table.
- Trade with a vendor one player at a time: two deals with the same vendor at the same moment can
  both get its last item.
- When the host leaves, the session ends for everyone.
- Less tested between two homes over the internet, and in late-game worlds.
- After a game update, wait for a mod version made for it.

**Keys:** T chat · F6 status · F7 name tags · F10 invite Steam friends · F11 change your look.
With a controller, the co-op menu works directly, and Pause > Co-op has what the F keys do.

### Reporting a problem

Open an [issue](../../issues) with what you were doing, the mod version (shown in the Co-op menu),
and the log of each player's game: `BepInEx\LogOutput.log` in the game folder. It is replaced at
every start, so copy it right after the session.

## Building from source

- .NET SDK (the project targets .NET Framework 4.7.2), Windows, and the game installed.
- BepInEx 5.4.23.5 for Windows x64, unpacked into `artifacts\bepinex-5.4.23.5\`.
- `dotnet build src\GK2Coop\GK2Coop.csproj -c Release` (pass `-p:GamePath=...` if the game is not
  in the default Steam folder). `scripts\Build-Package.ps1` makes the release zip.

`tests\` holds the multiplayer tests: a test plugin (`tests\GameplayProbe`) drives two to four
copies of the game on one PC, and `tests\Run-Regression.ps1` runs them (they expect test copies of
the game on `D:\`; see the scripts).

## License

MIT, see [LICENSE](LICENSE). The release zip includes BepInEx and its libraries under their own
licenses; see [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
