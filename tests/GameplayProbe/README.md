# Temporary live gameplay probe

This is an integration-test plugin, **not part of GK2Coop or its release package**.
It invokes actual game methods on Unity's main thread in two disposable new-game
processes, while the installed mod and real Netcode connection handle replication.
It checks world and inventory state, not just patch attachment or message counters.
It does not verify movement to an object, UI interaction, animation, or rendering.

It only enables when the installed co-op config has `AutoStartNewGame = true` at
startup. Never install it into a normal play session. Do not save the test world.
`capacity` deliberately changes backpack capacity for a rejection test.

## Running

The full set of multiplayer tests runs with `tests\Run-Regression.ps1` (`-Profile Full` for all of
them, `-Only <names>` for some). The older single run below still works:

One command does the whole run, including cleanup and restore after a failure:

```powershell
& '.\tests\GameplayProbe\Run-Verification.ps1' -OutputPath '.\artifacts\verification-<version>'
```

It refuses to start while the game is running or while a probe DLL is still installed, captures
both configs and the deployment manifest before touching anything, builds the plugin and the
probe, installs both, launches the two-instance session, runs `Verify-Live.ps1`, then archives
the logs, removes the probe, restores both configs and the manifest, and asserts by hash that
the restore actually happened. Do not save the test world.

`capacity` deliberately changes backpack capacity for a rejection test, and the last gate empties
`chest_home`. The probe only enables when the installed config has `AutoStartNewGame = true` at
startup, so it cannot run in a normal play session.

Use `Invoke-Probe.ps1 -Peer Host|Client -Command inspect` to inspect current state.
Other commands are `spawn|itemId|count`, `collect|dropGuid`,
`container-add|wgoGuid|itemId|count`, `container-remove|wgoGuid|itemId|count`,
`container-clear|wgoGuid`, `container-capacity|wgoGuid|slots`, `quest-await|questId`, `capacity|slots`, `resource|resName`, and
`items|idPrefix` (read real item ids from the installed catalogue rather than inventing one),
`move|dx|dz`, `players`, `bodies`, `wgos|n`, `wgo-kill|guid` and `wgo-alive|guid`.
`at|utcTicks|command` schedules an operation on a main-thread frame at or after a shared deadline. Scheduled pickups
use the real transport, with no synthetic packet interception or suppression.

## Multiple players

`Verify-MultiPlayer.ps1` runs the same probe across a host and every client clone that exists,
scaling its gates from the peer list rather than assuming a count. It checks that a client's
movement, drops, quest transitions, world-object deaths and container changes reach *every other*
peer — not just the host, which is what two players cannot distinguish.

Clones live at `D:\GK2Coop-LocalClient`, `-LocalClient2` and `-LocalClient3`; create more with
`scripts\Setup-LocalCoopClient.ps1 -ClientPath <path>`. Four players pass all nine gates.

Read the *body* position, not `playerData.position`, when checking whether a peer moved: only
the machine executing a command as authority writes that record.
