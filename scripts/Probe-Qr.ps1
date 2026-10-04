param([string]$OutputPath = "artifacts/qr-probe.json")
$ErrorActionPreference = 'Stop'
$randomBytes = New-Object byte[] 4
$randomGenerator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$randomGenerator.GetBytes($randomBytes)
$randomGenerator.Dispose()
$decimalValue = [BitConverter]::ToUInt32($randomBytes, 0).ToString([Globalization.CultureInfo]::InvariantCulture)
$headers = @{
    'iLink-App-Id' = 'bot'
    'iLink-App-ClientVersion' = '132105'
    'AuthorizationType' = 'ilink_bot_token'
    'X-WECHAT-UIN' = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($decimalValue))
}
$result = [ordered]@{
    checked_on = '2026-10-04'
    endpoint = 'https://ilinkai.weixin.qq.com/ilink/bot/get_bot_qrcode?bot_type=3'
    method = 'POST'
    authenticated = $false
    scan_performed = $false
    messages_sent = 0
    messages_received = 0
}
try {
    $response = Invoke-WebRequest -UseBasicParsing -Uri $result.endpoint -Method Post -Headers $headers -ContentType 'application/json' -Body '{"local_token_list":[]}' -TimeoutSec 20
    $responseText = if ($response.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($response.Content) } else { [string]$response.Content }
    $parsed = $responseText | ConvertFrom-Json
    $result.http_status = [int]$response.StatusCode
    $result.response_fields = @($parsed.PSObject.Properties.Name)
    foreach ($codeName in @('ret', 'errcode', 'code')) {
        if ($parsed.PSObject.Properties.Name -contains $codeName) { $result[$codeName] = $parsed.$codeName }
    }
    if ($parsed.PSObject.Properties.Name -contains 'data') { $result.data_fields = @($parsed.data.PSObject.Properties.Name) }
    $result.qrcode_present = -not [string]::IsNullOrWhiteSpace($parsed.qrcode)
    $result.qrcode_content_present = -not [string]::IsNullOrWhiteSpace($parsed.qrcode_img_content)
    $result.success = $result.qrcode_present -and $result.qrcode_content_present
} catch {
    # Never print a raw response, QR value or exception containing its URL/query.
    $result.success = $false
    $result.error_type = $_.Exception.GetType().FullName
    if ($_.Exception.Response) { $result.http_status = [int]$_.Exception.Response.StatusCode }
}
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
$result | ConvertTo-Json | Set-Content -LiteralPath $resolvedOutput -Encoding UTF8
$result | ConvertTo-Json
