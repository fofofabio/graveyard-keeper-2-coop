[CmdletBinding()]
param([string]$OutputPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'artifacts\verification-live'))
$ErrorActionPreference = 'Stop'
$invoke = Join-Path $PSScriptRoot 'Invoke-Probe.ps1'
$report = [Collections.Generic.List[string]]::new()
function Probe([string]$Peer, [string]$Command) {
    $result = & $invoke -Peer $Peer -Command $Command
    if ($result -match '(?m)^ERROR ') { throw $result }
    return $result
}
function Snapshot([string]$Peer, [string]$Label) {
    $state = Probe $Peer 'inspect'
    [IO.File]::WriteAllText((Join-Path $OutputPath "$Label-$Peer.txt"), $state)
    return $state
}
function Check([bool]$Passed, [string]$Label) {
    $line = $(if ($Passed) { 'PASS ' } else { 'FAIL ' }) + $Label
    $report.Add($line)
    Write-Host $line
    [IO.File]::WriteAllLines((Join-Path $OutputPath 'results.txt'), $report)
}
function DropId([string]$Peer, [string]$Item, [int]$Count) {
    $result = Probe $Peer "spawn|$Item|$Count"
    if ($result -notmatch 'SPAWN ([0-9a-f-]+)') { throw $result }
    Start-Sleep -Milliseconds 700
    return $Matches[1]
}
function BerryCount([string]$State) {
    $playerLine = ($State -split "`n" | Where-Object { $_ -like 'PLAYER *' }) -join ''
    if ($playerLine -match 'berryx(\d+)') { return [int]$Matches[1] }
    return 0
}
function ResourceCount([string]$Peer, [string]$Resource) {
    $result = Probe $Peer "resource|$Resource"
    if ($result -notmatch "RESOURCE $Resource=(\d+)") { throw $result }
    return [int]$Matches[1]
}
New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null
$container = 'a5d8e32d-a635-4c44-a836-1bf9e8d5fb12'
$beforeHost = Snapshot Host 'before'
$beforeClient = Snapshot Client 'before'
Probe Client "container-add|$container|berry|2" | Out-Null
Start-Sleep -Milliseconds 700
$addedHost = Snapshot Host 'container-add'
Check ($addedHost -match 'chest_home size=20 items=berryx7') 'Client chest deposit reaches host: berries 5 -> 7'
Probe Host "container-remove|$container|berry|3" | Out-Null
Start-Sleep -Milliseconds 700
$removedClient = Snapshot Client 'container-remove'
Check ($removedClient -match 'chest_home size=20 items=berryx4') 'Host chest removal reaches client: berries 7 -> 4'
Probe Host 'quest-await|1_intro_prison_find_key' | Out-Null
Start-Sleep -Milliseconds 700
$questHost = Snapshot Host 'quest-await'
$questClient = Snapshot Client 'quest-await'
Check ($beforeHost -match '1_intro_prison_find_key Available' -and $beforeClient -match '1_intro_prison_find_key Available' -and $questHost -match '1_intro_prison_find_key Awaiting' -and $questClient -match '1_intro_prison_find_key Awaiting') 'Host quest AwaitQuest changes actual status on both peers'
$id = DropId Client berry 2
$spawnHost = Snapshot Host 'drop-spawn'
Check ($spawnHost -match "DROP $id berry count=2") "Client drop reaches host with matching identity: $id"
Probe Client "collect|$id" | Out-Null
Start-Sleep -Milliseconds 700
$takenHost = Snapshot Host 'drop-taken'
$takenClient = Snapshot Client 'drop-taken'
Check ($takenHost -notmatch "DROP $id" -and $takenClient -notmatch "DROP $id" -and (BerryCount $takenClient) -eq 2 -and (BerryCount $takenHost) -eq 0) 'Sequential client pickup conserves two berries and removes both drops'

# Take the resource item id from the installed catalogue. A fabricated id (the earlier
# 'game_res_money') is rejected by the game before mod code runs, which is not a mod result.
$catalogue = Probe Client 'items|game_res_'
if ($catalogue -notmatch 'ITEMS (\S+)') { throw "No game_res_ item exists in this build: $catalogue" }
$resourceItem = ($Matches[1] -split ',')[0]
$resourceName = $resourceItem -replace '^game_res_', ''
$techBefore = ResourceCount Client $resourceName
$resourceId = DropId Client $resourceItem 2
Probe Client "collect|$resourceId" | Out-Null
Start-Sleep -Milliseconds 700
$resourceHost = Snapshot Host 'resource-taken'
$resourceClient = Snapshot Client 'resource-taken'
Check ($resourceHost -notmatch "DROP $resourceId" -and $resourceClient -notmatch "DROP $resourceId" -and (ResourceCount Client $resourceName) -eq ($techBefore + 2)) "Resource pickup ($resourceItem) is host-authoritative and credits the collecting client once"

# The ordinary method must reject collection when there is no inventory capacity.
Probe Host 'capacity|0' | Out-Null
$fullId = DropId Client bread 3
$fullBeforeHost = Snapshot Host 'full-backpack-before'
$fullBeforeClient = Snapshot Client 'full-backpack-before'
if ($fullBeforeHost -notmatch "DROP $fullId bread count=3" -or $fullBeforeClient -notmatch "DROP $fullId bread count=3") { throw 'Full-backpack test drop missing before collection' }
Probe Host "collect|$fullId" | Out-Null
Start-Sleep -Milliseconds 700
$fullHost = Snapshot Host 'full-backpack'
$fullClient = Snapshot Client 'full-backpack'
Check ($fullHost -match "DROP $fullId bread count=3" -and $fullClient -match "DROP $fullId bread count=3") 'Failed pickup with a full backpack preserves the drop on both peers'
Probe Host 'capacity|40' | Out-Null

# Both main threads invoke the real CollectDrop at the same UTC deadline.
# No packet interception: the shipped Netcode path remains active throughout.
for ($round = 1; $round -le 5; $round++) {
    $raceBeforeHost = Snapshot Host "race-$round-before"
    $raceBeforeClient = Snapshot Client "race-$round-before"
    $countBefore = (BerryCount $raceBeforeHost) + (BerryCount $raceBeforeClient)
    $raceId = DropId Client berry 1
    $due = [DateTime]::UtcNow.AddSeconds(3).Ticks
    Probe Host "at|$due|collect|$raceId" | Out-Null
    Probe Client "at|$due|collect|$raceId" | Out-Null
    Start-Sleep -Seconds 4
    foreach ($install in @($env:GK2COOP_TEST_HOST_PATH,$env:GK2COOP_TEST_CLIENT_PATH)) {
        $completion = [IO.File]::ReadAllText((Join-Path $install 'BepInEx\GK2Coop.Probe.command.result'))
        [IO.File]::AppendAllText((Join-Path $OutputPath "race-$round-collection.txt"), "$install`n$completion`n")
    }
    $raceAfterHost = Snapshot Host "race-$round-after"
    $raceAfterClient = Snapshot Client "race-$round-after"
    $countAfter = (BerryCount $raceAfterHost) + (BerryCount $raceAfterClient)
    Check ($countAfter -eq $countBefore + 1) "Contested pickup round ${round}: one berry spawned; combined inventory delta=$($countAfter - $countBefore)"
    if ($countAfter -ne $countBefore + 1) { break }
}

# Since 0.17.0 a client sends what changed rather than the whole contents, so the host applies
# it on top of its own concurrent edit. Both edits must survive, not merely converge: from four
# berries, host +1 and client +2 is seven. Convergence on six would mean an edit was discarded.
$due = [DateTime]::UtcNow.AddSeconds(3).Ticks
Probe Host "at|$due|container-add|$container|berry|1" | Out-Null
Probe Client "at|$due|container-add|$container|berry|2" | Out-Null
Start-Sleep -Seconds 4
$chestHost = Snapshot Host 'container-race'
$chestClient = Snapshot Client 'container-race'
$hostChest = ($chestHost -split "`n" | Where-Object { $_ -match "^CONTAINER $container " }) -join ''
$clientChest = ($chestClient -split "`n" | Where-Object { $_ -match "^CONTAINER $container " }) -join ''
Check (($hostChest -eq $clientChest) -and ($hostChest -match 'items=berryx7')) "Concurrent chest edits conserve both: host=[$hostChest], client=[$clientChest]"

# An impossible pair of removals: four berries, host takes 2 and client takes 3 at the same
# moment. The client's own removal succeeds locally against its stale view, so its delta cannot
# be satisfied on the host. The host must send its contents back rather than leave the client
# holding a number that never existed — a rejection has to correct, not just refuse.
Probe Host "container-remove|$container|berry|3" | Out-Null
Start-Sleep -Milliseconds 900
$oversubBefore = Snapshot Host 'oversubscribe-before'
if ($oversubBefore -notmatch 'chest_home size=20 items=berryx4') { throw "Setup: chest is not at four berries. $oversubBefore" }
$due = [DateTime]::UtcNow.AddSeconds(3).Ticks
Probe Host "at|$due|container-remove|$container|berry|2" | Out-Null
Probe Client "at|$due|container-remove|$container|berry|3" | Out-Null
Start-Sleep -Seconds 5
$oversubHost = Snapshot Host 'oversubscribe'
$oversubClient = Snapshot Client 'oversubscribe'
$hostLine = ($oversubHost -split "`n" | Where-Object { $_ -match "^CONTAINER $container " }) -join ''
$clientLine = ($oversubClient -split "`n" | Where-Object { $_ -match "^CONTAINER $container " }) -join ''
$remaining = if ($hostLine -match 'berryx(\d+)') { [int]$Matches[1] } else { 0 }
Check (($hostLine -eq $clientLine) -and ($remaining -ge 0) -and ($remaining -le 4)) "Over-subscribed removal leaves both peers agreeing on a possible count: host=[$hostLine], client=[$clientLine]"

# The rejection branch itself: the host's chest is shrunk to one occupied slot while the client
# still believes there is room, so the client's addition succeeds locally and cannot be applied on
# the host. The host must answer with its own contents so the client gives the item back up —
# otherwise the client keeps bread the host never had.
Probe Host "container-add|$container|berry|1" | Out-Null
Start-Sleep -Milliseconds 900
Probe Host "container-capacity|$container|1" | Out-Null
$rejectBefore = Snapshot Client 'reject-before'
if ($rejectBefore -notmatch "CONTAINER $container chest_home size=20") { throw "Setup: the client's chest should still look roomy. $rejectBefore" }
Probe Client "container-add|$container|bread|1" | Out-Null
Start-Sleep -Seconds 3
$rejectHost = Snapshot Host 'reject-host'
$rejectClient = Snapshot Client 'reject-client'
$rejectHostLine = ($rejectHost -split "`n" | Where-Object { $_ -match "^CONTAINER $container " }) -join ''
$rejectClientLine = ($rejectClient -split "`n" | Where-Object { $_ -match "^CONTAINER $container " }) -join ''
Check (($rejectHostLine -notmatch 'bread') -and ($rejectClientLine -notmatch 'bread')) "A delta the full host container cannot accept is taken back off the client: host=[$rejectHostLine], client=[$rejectClientLine]"
Probe Host "container-capacity|$container|20" | Out-Null

# Game logic empties containers through Inventory.Clear directly (cargo lifts, selling
# palettes, containers tipped out as drops), never through RemoveItemById. Run last: it
# destroys the chest contents the earlier gates depend on.
Probe Client "container-clear|$container" | Out-Null
Start-Sleep -Milliseconds 700
$clearedHost = Snapshot Host 'container-clear'
$clearedLine = ($clearedHost -split "`n" | Where-Object { $_ -match "^CONTAINER $container " }) -join ''
Check ($clearedLine -match 'chest_home size=20 items=\s*$') "Client clearing a container empties it on the host: [$clearedLine]"
if ($report | Where-Object { $_ -like 'FAIL *' }) { throw 'Gameplay verification failed; see results.txt and per-peer snapshots.' }
