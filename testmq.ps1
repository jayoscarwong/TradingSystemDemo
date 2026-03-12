# RabbitMQ credentials from your docker-compose
$Username = "guest"
$Password = "guest"
$BaseUri = "http://localhost:15672/api"

# Create Basic Auth Header
$AuthString = [System.Convert]::ToBase64String([System.Text.Encoding]::ASCII.GetBytes("${Username}:${Password}"))
$Headers = @{
    "Authorization" = "Basic $AuthString"
}

Write-Host "=== RabbitMQ & MassTransit Verification ===" -ForegroundColor Cyan

try {
    # 1. Check overall node health
    $Overview = Invoke-RestMethod -Uri "$BaseUri/overview" -Headers $Headers -Method Get
    Write-Host "`n[+] Node Status: OK" -ForegroundColor Green
    Write-Host "    RabbitMQ Version: $($Overview.rabbitmq_version)"
    Write-Host "    Total Queued Messages: $($Overview.queue_totals.messages)"

    # 2. Check Active Connections
    $Connections = Invoke-RestMethod -Uri "$BaseUri/connections" -Headers $Headers -Method Get
    Write-Host "`n[+] Active MassTransit Connections ($($Connections.Count)):" -ForegroundColor Green
    if ($Connections.Count -eq 0) {
        Write-Host "    No active connections found." -ForegroundColor Yellow
    }
    foreach ($conn in $Connections) {
        $clientApp = $conn.client_properties.connection_name
        if (-not $clientApp) { $clientApp = "Unknown MassTransit Client" }
        Write-Host "    - Client: $clientApp ($($conn.peer_host):$($conn.peer_port))"
    }

    # 3. Check Queues
    $Queues = Invoke-RestMethod -Uri "$BaseUri/queues" -Headers $Headers -Method Get
    Write-Host "`n[+] Active Queues ($($Queues.Count)):" -ForegroundColor Green
    if ($Queues.Count -eq 0) {
        Write-Host "    No queues found. (MassTransit creates these automatically when a Consumer is registered or a message is published)." -ForegroundColor Yellow
    }
    foreach ($queue in $Queues) {
        Write-Host "    - Queue: $($queue.name)"
        Write-Host "      Messages Ready: $($queue.messages_ready)"
        Write-Host "      Active Consumers: $($queue.consumers)"
    }
}
catch {
    Write-Host "`n[-] Failed to connect to RabbitMQ Management API. Is the container running on port 15672?" -ForegroundColor Red
    Write-Host $_.Exception.Message
}