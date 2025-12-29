$chromeDir = [Environment]::GetFolderPath('LocalApplicationData') + '\Google\Chrome\User Data'
@('Default', 'Profile 1', 'Profile 2') | ForEach-Object {
    $prefFile = Join-Path $chromeDir "$_\Preferences"
    if (Test-Path $prefFile) {
        $json = Get-Content $prefFile -Raw | ConvertFrom-Json
        $name = $json.profile.name
        $window = $json.browser.window_placement
        Write-Host "=== $_ (Name: $name) ==="
        Write-Host "  left=$($window.left), top=$($window.top)"
        Write-Host "  right=$($window.right), bottom=$($window.bottom)"
        Write-Host "  maximized=$($window.maximized)"
        Write-Host ""
    }
}
