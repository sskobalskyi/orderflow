<#
.SYNOPSIS
  Moves messages from MassTransit "*_error" queues back to their original queues.

.DESCRIPTION
  After retries are exhausted MassTransit parks a message in "<queue>_error". Once the cause is fixed,
  this script re-publishes each parked message (body and headers unchanged) to "<queue>" through the
  RabbitMQ management API. Consumers are idempotent, so replaying is safe.

.EXAMPLE
  ./scripts/replay-errors.ps1                      # replay every error queue
  ./scripts/replay-errors.ps1 -Queue OrderOutcomeNotifier_error
  ./scripts/replay-errors.ps1 -WhatIf              # only list what would be replayed
#>
[CmdletBinding(SupportsShouldProcess)]
param(
  [string] $Queue,
  [string] $ManagementUrl = 'http://localhost:15672',
  [pscredential] $Credential = [pscredential]::new('guest', (ConvertTo-SecureString 'guest' -AsPlainText -Force))
)

$ErrorActionPreference = 'Stop'
$vhost = '%2F'

function Invoke-Rabbit($Method, $Path, $Body) {
  $pair = '{0}:{1}' -f $Credential.UserName, $Credential.GetNetworkCredential().Password
  $args = @{ Uri = "$ManagementUrl/api/$Path"; Method = $Method; Headers = @{ Authorization = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($pair)) } }
  if ($Body) { $args.Body = ($Body | ConvertTo-Json -Depth 10 -Compress); $args.ContentType = 'application/json' }
  Invoke-RestMethod @args
}

$queues = Invoke-Rabbit GET "queues/$vhost" | Where-Object { $_.name -like '*_error' -and $_.messages -gt 0 }
if ($Queue) { $queues = $queues | Where-Object { $_.name -eq $Queue } }

if (-not $queues) { Write-Host 'No error messages to replay.'; return }

foreach ($q in $queues) {
  $target = $q.name -replace '_error$', ''
  if (-not $PSCmdlet.ShouldProcess($q.name, "replay $($q.messages) message(s) to $target")) { continue }

  $replayed = 0
  while ($true) {
    # ack_requeue_false removes the message from the error queue as it is read.
    $batch = Invoke-Rabbit POST "queues/$vhost/$($q.name)/get" @{ count = 50; ackmode = 'ack_requeue_false'; encoding = 'auto' }
    if (-not $batch -or $batch.Count -eq 0) { break }
    foreach ($m in $batch) {
      $props = @{ delivery_mode = 2 }
      if ($m.properties.content_type) { $props.content_type = $m.properties.content_type }
      if ($m.properties.headers) {
        # Drop the fault details MassTransit added; the replayed message starts a fresh attempt.
        $headers = [ordered]@{}
        $m.properties.headers.PSObject.Properties | Where-Object { $_.Name -notlike 'MT-Fault-*' -and $_.Name -notlike 'MT-Reason' -and $_.Name -notlike 'MT-Host-*' } |
          ForEach-Object { $headers[$_.Name] = $_.Value }
        $props.headers = $headers
      }
      $r = Invoke-Rabbit POST "exchanges/$vhost/amq.default/publish" @{
        routing_key = $target; payload = $m.payload; payload_encoding = $m.payload_encoding; properties = $props }
      if (-not $r.routed) { throw "Message was not routed to '$target'." }
      $replayed++
    }
  }
  Write-Host "Replayed $replayed message(s): $($q.name) -> $target"
}
