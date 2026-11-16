#requires -Version 7
<#
.SYNOPSIS
  Helper for the game-store-research skill. Reads and writes the "Game stores" tables in
  documents/CONVENTIONS_KB.md (the text between the <!-- ...:begin/end --> marker comments only).

  Plan   List the search areas in hubs.csv that have no row in the coverage table yet, nearest first.
  Stale  Print table rows in the worker line format below: rows whose Checked date is older than -Days,
         or with -Incomplete the rows whose Events / play or Contact cell is still empty.
  Merge  Read worker result files (*.txt) from -Incoming, geocode the addresses with the US Census
         batch geocoder, compute straight-line miles from West Lafayette, drop rows outside -Radius,
         upsert the rest into the table (sorted by miles), fill driving distances, and update the
         coverage table. -DryRun does everything except the network calls for driving distance and
         writing the file.
  Drive  Fill the Drive cell (driving miles and time from West Lafayette, OSRM public routing server)
         for rows that lack one. Merge does this too. -DryRun reports without writing.

  Table columns: Store | Address | Miles | Drive | Website | Type | Events / play | Contact | Outreach | Checked | Notes
  Outreach is the user's column: the script never sets it and always keeps what is there. A table in the
  older 7-column layout is read and rewritten in this layout.

  Worker line format (pipe-separated, 11 fields; Hub, ZIP, Events, Contact and Notes may be empty;
  the geocoder supplies a missing ZIP):
    Hub | Store | Street | City | ST | ZIP | Website | Type | Events | Contact | Notes
  Events is one of: Events + play space | Events | Play space | Not listed. (Older 9-field lines without
  Events and Contact are still accepted.) A non-empty Events or Contact replaces the stored value; an
  empty one keeps it.
  Other lines:  "# hubs: A; B"   areas searched (even if nothing was found)
                "# capped: A"    the per-area cap was reached, so more stores likely exist
  Type CLOSED removes the matching row (matched on street number + first street word + ZIP).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Plan', 'Stale', 'Merge', 'Drive')][string]$Mode,
    [string]$Kb = (Join-Path $PSScriptRoot '..\..\..\documents\CONVENTIONS_KB.md'),
    [string]$Incoming,
    [double]$Radius = 400,
    [int]$Days = 180,
    [string]$Today = (Get-Date -Format 'yyyy-MM-dd'),
    [switch]$Incomplete,
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'

$CenterLat = 40.4259; $CenterLon = -86.9081          # West Lafayette, IN
$Types = 'Game store', 'Board game store', 'Card/TCG store', 'Miniatures/wargaming', 'Comics + games', 'Hobby shop', 'Game cafe'
$EventsVals = 'Events + play space', 'Events', 'Play space', 'Not listed'
$Reject = 'GameStop|Game Stop|EB Games|Walmart|Target|Barnes|Best Buy|Hobby Lobby|Michaels|Toys.?R.?Us'
$Dash = [string][char]0x2014

$Kb = (Resolve-Path $Kb).Path
$kbText = [IO.File]::ReadAllText($Kb)
$nl = $kbText.Contains("`r`n") ? "`r`n" : "`n"

function Get-Miles([double]$Lat, [double]$Lon) {
    $rad = [math]::PI / 180
    $dLat = ($Lat - $CenterLat) * $rad
    $dLon = ($Lon - $CenterLon) * $rad
    $a = [math]::Sin($dLat / 2) * [math]::Sin($dLat / 2) +
         [math]::Cos($CenterLat * $rad) * [math]::Cos($Lat * $rad) * [math]::Sin($dLon / 2) * [math]::Sin($dLon / 2)
    2 * 3958.8 * [math]::Asin([math]::Sqrt($a))
}

function Get-Block([string]$Name) {
    $m = [regex]::Match($kbText, "<!-- ${Name}:begin -->(.*?)<!-- ${Name}:end -->", 'Singleline')
    if (-not $m.Success) { throw "Marker comments for '$Name' not found in $Kb" }
    $m
}

# Rows of a markdown table: skips the header and |---| separator lines, returns arrays of cells.
function Read-Table([string]$Body, [int[]]$Cols) {
    $lines = @($Body -split '\r?\n' | ForEach-Object { $_.Trim() } | Where-Object { $_.StartsWith('|') })
    foreach ($t in ($lines | Select-Object -Skip 2)) {
        $cells = @(($t -replace '^\|\s*', '' -replace '\s*\|$', '') -split '\s*\|\s*')
        if ($cells.Count -notin $Cols) { throw "Table row has $($cells.Count) cells, expected $($Cols -join ' or '): $t" }
        , $cells
    }
}

function Split-Address([string]$Address) {
    if ($Address -match '^(?<street>.+?),\s*(?<city>[^,]+),\s*(?<st>[A-Z]{2})\s+(?<zip>\d{5})') {
        [pscustomobject]@{ Street = $Matches.street; City = $Matches.city; St = $Matches.st; Zip = $Matches.zip }
    }
}

# Same store = same ZIP + street number + first street word (ignoring N/S/E/W).
function Get-Key([string]$Street, [string]$Zip) {
    $t = @(($Street.ToLower() -replace '[^a-z0-9 ]', ' ').Split(' ', [StringSplitOptions]::RemoveEmptyEntries))
    $num = $t | Where-Object { $_ -match '^\d+$' } | Select-Object -First 1
    $word = $t | Where-Object { $_ -match '^[a-z]{2,}$' -and $_ -notin 'north', 'south', 'east', 'west' } | Select-Object -First 1
    "$Zip|$num|$word"
}

function Read-Stores {
    foreach ($c in @(Read-Table (Get-Block 'game-stores').Groups[1].Value @(7, 11))) {      # @() keeps a one-row table an array of rows
        if ($c.Count -eq 7) {   # older layout: Store, Address, Miles, Website, Type, Checked, Notes
            $c = @($c[0], $c[1], $c[2], '', $c[3], $c[4], '', '', '', $c[5], $c[6])
        }
        $addr = Split-Address $c[1]
        if (-not $addr) { throw "Cannot read the address of '$($c[0])': $($c[1])" }
        $m = $c[2] -replace '[^\d.]', ''
        [pscustomobject]@{
            Key = Get-Key $addr.Street $addr.Zip; Store = $c[0]; Address = $c[1]; Addr = $addr
            Miles = $c[2]; MilesNum = $(if ($m) { [double]$m } else { 0.5 })
            Drive = $c[3]; Website = $c[4]; Type = $c[5]; Events = $c[6]; Contact = $c[7]; Outreach = $c[8]
            Checked = $c[9]; Notes = $c[10]
        }
    }
}

function Read-Coverage { Read-Table (Get-Block 'game-store-coverage').Groups[1].Value 4 }

function Read-Hubs {
    Import-Csv (Join-Path $PSScriptRoot 'hubs.csv') | ForEach-Object {
        $_ | Add-Member MilesNum (Get-Miles ([double]$_.lat) ([double]$_.lon)) -PassThru
    } | Sort-Object MilesNum
}

function Invoke-Geocode([object[]]$Rows) {
    $csvPath = Join-Path ([IO.Path]::GetTempPath()) "stores-$([guid]::NewGuid().ToString('N')).csv"
    try {
        $lines = for ($i = 0; $i -lt $Rows.Count; $i++) {
            $r = $Rows[$i]
            $street = $r.Street -replace '(?i),?\s*(suite|ste|unit|bldg|building|space|#)\s*[\w-]+\s*$', '' -replace '"', ''
            '"{0}","{1}","{2}","{3}","{4}"' -f $i, $street, ($r.City -replace '"', ''), $r.St, $r.Zip
        }
        [IO.File]::WriteAllLines($csvPath, $lines)
        $resp = Invoke-RestMethod -Uri 'https://geocoding.geo.census.gov/geocoder/locations/addressbatch' -Method Post `
            -Form @{ addressFile = (Get-Item $csvPath); benchmark = 'Public_AR_Current' } -TimeoutSec 300
    }
    finally { Remove-Item $csvPath -ErrorAction SilentlyContinue }
    $found = @{}
    foreach ($g in ($resp | ConvertFrom-Csv -Header id, input, match, matchtype, matched, coords, tiger, side)) {
        if ($g.match -ne 'Match') { continue }
        $lon, $lat = $g.coords -split ','
        $zip = if ($g.matched -match '(\d{5})(?:-\d{4})?\s*$') { $Matches[1] } else { '' }   # the matched address carries the ZIP
        $found[[int]$g.id] = @([double]$lat, [double]$lon, $zip)
    }
    $found
}

# Fills the Drive cell ("62 mi, 1h05m") of rows that lack one: geocode, then one OSRM table request per 90 stores.
# Returns how many rows were filled. A routing failure only warns: the rows stay empty and are retried next run.
function Add-Drive([object[]]$Rows) {
    $need = @($Rows | Where-Object { -not $_.Drive })
    if (-not $need.Count) { return 0 }
    $geo = Invoke-Geocode @($need | ForEach-Object { $_.Addr })
    $pts = @(for ($i = 0; $i -lt $need.Count; $i++) { if ($geo[$i]) { [pscustomobject]@{ Row = $need[$i]; Lat = $geo[$i][0]; Lon = $geo[$i][1] } } })
    $done = 0
    for ($o = 0; $o -lt $pts.Count; $o += 90) {
        $chunk = @($pts[$o..([math]::Min($o + 89, $pts.Count - 1))])
        $coords = "$CenterLon,$CenterLat;" + (($chunk | ForEach-Object { '{0},{1}' -f $_.Lon, $_.Lat }) -join ';')
        try {
            $r = Invoke-RestMethod -Uri "https://router.project-osrm.org/table/v1/driving/${coords}?sources=0&annotations=distance,duration" -TimeoutSec 90
        }
        catch { Write-Warning "Driving distances unavailable (OSRM): $($_.Exception.Message)"; return $done }
        for ($i = 0; $i -lt $chunk.Count; $i++) {
            $m = $r.distances[0][$i + 1]; $s = $r.durations[0][$i + 1]
            if ($null -eq $m -or $null -eq $s) { continue }
            $min = [int][math]::Round($s / 60); $h = [int][math]::Floor($min / 60)
            $chunk[$i].Row.Drive = '{0:N0} mi, {1}' -f ($m / 1609.344), $(if ($h) { '{0}h{1:00}m' -f $h, ($min % 60) } else { "${min}m" })
            $done++
        }
        Start-Sleep -Seconds 1
    }
    $done
}

function Read-Incoming([string]$Dir) {
    $r = @{ Rows = [Collections.Generic.List[object]]::new(); Hubs = [Collections.Generic.List[string]]::new()
            Capped = [Collections.Generic.List[string]]::new(); Bad = [Collections.Generic.List[string]]::new() }
    foreach ($file in (Get-ChildItem $Dir -Filter *.txt)) {
        foreach ($line in [IO.File]::ReadLines($file.FullName)) {
            $t = $line.Trim()
            if (-not $t) { continue }
            if ($t -match '^#\s*hubs:\s*(.*)$') { $Matches[1] -split ';' | Where-Object { $_.Trim() } | ForEach-Object { $r.Hubs.Add($_.Trim()) }; continue }
            if ($t -match '^#\s*capped:\s*(.*)$') { $Matches[1] -split ';' | Where-Object { $_.Trim() } | ForEach-Object { $r.Capped.Add($_.Trim()) }; continue }
            if ($t.StartsWith('#')) { continue }
            $f = @($t -split '\s*\|\s*', 11)
            if ($f.Count -lt 8) { $r.Bad.Add("malformed line: $t"); continue }
            # 11 fields = current format; a 9-field line whose last field is not an Events value is the older format
            $current = ($f.Count -ge 10) -or ($f.Count -eq 9 -and ($EventsVals | Where-Object { $_ -ieq $f[8] }))
            while ($f.Count -lt 11) { $f += '' }
            $row = [pscustomobject]@{
                Hub = $f[0]; Store = $f[1]; Street = $f[2]; City = $f[3]; St = $f[4].ToUpper(); Zip = ($f[5] -replace '^(\d{5}).*', '$1')
                Website = $f[6].TrimEnd('/'); Type = $f[7]
                Events = $(if ($current) { $f[8] } else { '' }); Contact = $(if ($current) { $f[9] } else { '' })
                Notes = $(if ($current) { $f[10] } else { $f[8] })
            }
            $canon = @($Types + 'CLOSED') | Where-Object { $_ -ieq $row.Type } | Select-Object -First 1
            if ($canon) { $row.Type = $canon }          # tolerate case differences
            $ev = $EventsVals | Where-Object { $_ -ieq $row.Events } | Select-Object -First 1
            if ($ev) { $row.Events = $ev }
            if ($row.Type -ne 'CLOSED') {
                $why = if ($row.Store -match $Reject) { 'not a game store' }
                elseif ($row.Type -notin $Types) { "unknown type '$($row.Type)'" }
                elseif ($row.Events -and $row.Events -notin $EventsVals) { "unknown events value '$($row.Events)'" }
                elseif (-not $row.Street -or -not $row.City -or $row.St -notmatch '^[A-Z]{2}$' -or ($row.Zip -and $row.Zip -notmatch '^\d{5}$')) { 'incomplete address' }
                elseif ($row.Website -and $row.Website -ne $Dash -and $row.Website -notmatch '^https?://\S+$') { 'website is not a URL' }
                if ($why) { $r.Bad.Add("rejected ($why): $($row.Store) [$($row.Hub)]"); continue }
                if (-not $row.Website) { $row.Website = $Dash }
            }
            $r.Rows.Add($row)
        }
    }
    $r
}

function Format-StoreTable($Rows) {
    '| Store | Address | Miles | Drive | Website | Type | Events / play | Contact | Outreach | Checked | Notes |'
    '|---|---|---:|---:|---|---|---|---|---|---|---|'
    foreach ($r in $Rows) {
        "| $($r.Store) | $($r.Address) | $($r.Miles) | $($r.Drive) | $($r.Website) | $($r.Type) | $($r.Events) | $($r.Contact) | $($r.Outreach) | $($r.Checked) | $($r.Notes) |"
    }
}

function Set-Block([string]$Name, [string[]]$Lines) {
    $m = Get-Block $Name
    $new = "<!-- ${Name}:begin -->" + $nl + ($Lines -join $nl) + $nl + "<!-- ${Name}:end -->"
    $script:kbText = $kbText.Substring(0, $m.Index) + $new + $kbText.Substring($m.Index + $m.Length)
}

switch ($Mode) {
    'Plan' {
        $covered = @(Read-Coverage | ForEach-Object { $_[0] })
        $hubs = @(Read-Hubs)
        $todo = @($hubs | Where-Object { $_.name -notin $covered })
        "$($hubs.Count - $todo.Count) of $($hubs.Count) areas have been searched. Not yet searched (nearest first):"
        foreach ($h in $todo) { '{0,4:N0} mi | {1} | {2}' -f $h.MilesNum, $h.name, $h.towns }
    }
    'Stale' {
        $cutoff = [datetime]::ParseExact($Today, 'yyyy-MM-dd', $null).AddDays(-$Days)
        foreach ($s in (Read-Stores)) {
            if ($Incomplete) { if ($s.Events -and $s.Contact) { continue } }
            else {
                $d = [datetime]::MinValue
                if ([datetime]::TryParseExact($s.Checked, 'yyyy-MM-dd', $null, 'None', [ref]$d) -and $d -ge $cutoff) { continue }
            }
            $web = if ($s.Website -eq $Dash) { '' } else { $s.Website }
            " | $($s.Store) | $($s.Addr.Street) | $($s.Addr.City) | $($s.Addr.St) | $($s.Addr.Zip) | $web | $($s.Type) | $($s.Events) | $($s.Contact) | $($s.Notes)"
        }
    }
    'Drive' {
        $rows = @(Read-Stores)
        $need = @($rows | Where-Object { -not $_.Drive }).Count
        $n = Add-Drive $rows
        "Driving distance filled for $n of $need rows that lacked one ($($rows.Count) rows in the table)"
        if ($DryRun) { 'DRY RUN: nothing written.'; return }
        Set-Block 'game-stores' (Format-StoreTable $rows)
        [IO.File]::WriteAllText($Kb, $kbText, [Text.UTF8Encoding]::new($false))
        "Wrote $Kb"
    }
    'Merge' {
        if (-not $Incoming) { throw '-Incoming <folder with *.txt worker files> is required for Merge' }
        $in = Read-Incoming $Incoming
        $table = [ordered]@{}
        foreach ($s in (Read-Stores)) { $table[$s.Key] = $s }
        $before = $table.Count

        $live = @($in.Rows | Where-Object { $_.Type -ne 'CLOSED' })
        $geo = if ($live.Count) { Invoke-Geocode $live } else { @{} }

        $added = @(); $updated = @(); $removed = @(); $far = @(); $nomatch = @(); $dups = @(); $notfound = @()
        $seen = @{}; $kept = @{}
        foreach ($row in $in.Rows) {
            if ($row.Type -eq 'CLOSED') {
                $key = Get-Key $row.Street $row.Zip
                if (-not $row.Zip) {                      # no ZIP given: match on street number + first street word alone
                    $tail = $key.Substring($key.IndexOf('|'))
                    $key = $table.Keys | Where-Object { $_.EndsWith($tail) } | Select-Object -First 1
                }
                if ($key -and $table.Contains($key)) { $removed += $table[$key].Store; $table.Remove($key) } else { $notfound += $row.Store }
                continue
            }
            $g = $geo[[array]::IndexOf($live, $row)]
            if (-not $g -or (-not $row.Zip -and -not $g[2])) { $nomatch += "$($row.Store), $($row.Street), $($row.City) $($row.St) [$($row.Hub)]"; continue }
            if (-not $row.Zip) { $row.Zip = $g[2] }       # ZIP not printed on the store's site: take it from the geocoder
            $key = Get-Key $row.Street $row.Zip
            if ($seen.ContainsKey($key)) { $dups += "$($row.Store) [$($row.Hub)]"; continue }
            $seen[$key] = $true
            $mi = Get-Miles $g[0] $g[1]
            if ($mi -gt $Radius) { $far += ('{0} ({1:N0} mi) [{2}]' -f $row.Store, $mi, $row.Hub); continue }
            $old = $table[$key]
            $address = "$($row.Street), $($row.City), $($row.St) $($row.Zip)"
            $notes = $row.Notes
            if ($old) {
                if (-not $notes -or ($old.Notes -and $old.Notes.Contains($notes))) { $notes = $old.Notes }
                elseif ($old.Notes -and -not $notes.Contains($old.Notes)) { $notes = "$($old.Notes); $notes" }
            }
            $notes = (@($notes -split ';\s*' | Where-Object { $_ } | Select-Object -Unique) -join '; ')      # never repeat a segment
            $rounded = [math]::Round($mi)
            $table[$key] = [pscustomobject]@{
                Key = $key; Store = $row.Store; Address = $address
                Miles = $(if ($rounded -lt 1) { '<1' } else { "$rounded" }); MilesNum = $(if ($rounded -lt 1) { 0.5 } else { $rounded })
                Drive = $(if ($old -and $old.Address -ceq $address) { $old.Drive } else { '' })      # recomputed when the address changed
                Website = $row.Website; Type = $row.Type
                Events = $(if ($row.Events) { $row.Events } elseif ($old) { $old.Events } else { '' })
                Contact = $(if ($row.Contact) { $row.Contact } elseif ($old) { $old.Contact } else { '' })
                Outreach = $(if ($old) { $old.Outreach } else { '' })                                 # the user's column: never overwritten
                Checked = $Today; Notes = $notes
            }
            if ($old) { $updated += $row.Store } else { $added += $row.Store }
            if ($row.Hub) { $kept[$row.Hub] = 1 + [int]$kept[$row.Hub] }
        }

        $sorted = @($table.Values | Sort-Object MilesNum, Store)
        $hubOrder = @(Read-Hubs | ForEach-Object { $_.name })
        $cov = [ordered]@{}
        foreach ($c in @(Read-Coverage)) { $cov[$c[0]] = $c }
        foreach ($h in (@($in.Hubs) + @($in.Rows | Where-Object Hub | ForEach-Object Hub) | Select-Object -Unique)) {
            $note = @()
            if ($in.Capped -contains $h) { $note += 'cap reached, more stores likely' }
            $cov[$h] = @($h, $Today, "$([int]$kept[$h])", ($note -join '; '))
        }
        $covRows = @($cov.Values | Sort-Object { $i = $hubOrder.IndexOf($_[0]); if ($i -lt 0) { 9999 } else { $i } })

        "Incoming: $($in.Rows.Count) usable lines from $((Get-ChildItem $Incoming -Filter *.txt).Count) file(s)"
        "Table: $before -> $($sorted.Count) rows (added $($added.Count), updated $($updated.Count), removed $($removed.Count))"
        foreach ($pair in @(@('Outside the radius, not added', $far), @('Not geocoded, not added (fix the address and resubmit)', $nomatch),
                @('Duplicate within this run, skipped', $dups), @('CLOSED but not in the table', $notfound), @('Rejected / malformed', @($in.Bad)))) {
            if ($pair[1].Count) { "$($pair[0]): $($pair[1].Count)"; $pair[1] | ForEach-Object { "  - $_" } }
        }
        if ($removed.Count) { "Removed: $($removed -join '; ')" }

        if ($DryRun) { 'DRY RUN: nothing written (driving distances not requested).'; return }
        $nd = Add-Drive $sorted
        if ($nd) { "Driving distance filled for $nd rows" }
        Set-Block 'game-stores' (Format-StoreTable $sorted)
        Set-Block 'game-store-coverage' (@('| Area | Last searched | Stores listed (last search) | Notes |', '|---|---|---:|---|') +
            @($covRows | ForEach-Object { "| $($_[0]) | $($_[1]) | $($_[2]) | $($_[3]) |" }))
        [IO.File]::WriteAllText($Kb, $kbText, [Text.UTF8Encoding]::new($false))
        "Wrote $Kb"
    }
}
