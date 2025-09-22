# DbCop Shield Icon Creator 🛡️
# Creates a professional shield-based icon representing security and database protection

Add-Type -AssemblyName System.Drawing

Write-Host "🛡️ Creating DbCop Shield Icon..."
Write-Host "Inspired by the shield emoji to represent safety and security features"

# Create a 64x64 icon
$size = 64
$bitmap = New-Object System.Drawing.Bitmap($size, $size)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = 'AntiAlias'
$graphics.TextRenderingHint = 'AntiAliasGridFit'
$graphics.Clear([System.Drawing.Color]::Transparent)

# Professional color scheme for security/database theme
$shieldBlue = [System.Drawing.Color]::FromArgb(41, 128, 185)     # Primary shield blue
$darkBlue = [System.Drawing.Color]::FromArgb(52, 73, 94)        # Dark border/shadow
$accentGreen = [System.Drawing.Color]::FromArgb(39, 174, 96)    # Success/safe green
$white = [System.Drawing.Color]::White
$lightBlue = [System.Drawing.Color]::FromArgb(174, 214, 241)    # Light accent

# Create brushes and pens
$shieldBrush = New-Object System.Drawing.SolidBrush($shieldBlue)
$darkBrush = New-Object System.Drawing.SolidBrush($darkBlue)
$accentBrush = New-Object System.Drawing.SolidBrush($accentGreen)
$whiteBrush = New-Object System.Drawing.SolidBrush($white)
$lightBrush = New-Object System.Drawing.SolidBrush($lightBlue)
$borderPen = New-Object System.Drawing.Pen($darkBlue, 2)

# Shield dimensions and positioning
$centerX = $size / 2
$centerY = $size / 2
$shieldWidth = 36
$shieldHeight = 44
$shieldTop = 8
$shieldLeft = $centerX - $shieldWidth/2

# Create shield shape points (classic shield outline)
$shieldPoints = @(
    [System.Drawing.Point]::new($centerX, $shieldTop),                           # Top point
    [System.Drawing.Point]::new($shieldLeft + $shieldWidth * 0.8, $shieldTop + 8),   # Upper right
    [System.Drawing.Point]::new($shieldLeft + $shieldWidth, $shieldTop + 20),         # Right side
    [System.Drawing.Point]::new($shieldLeft + $shieldWidth, $shieldTop + 32),         # Right bottom
    [System.Drawing.Point]::new($centerX, $shieldTop + $shieldHeight),               # Bottom point
    [System.Drawing.Point]::new($shieldLeft, $shieldTop + 32),                       # Left bottom
    [System.Drawing.Point]::new($shieldLeft, $shieldTop + 20),                       # Left side
    [System.Drawing.Point]::new($shieldLeft + $shieldWidth * 0.2, $shieldTop + 8)    # Upper left
)

# Draw shield shadow (slight offset for depth)
$shadowPoints = @()
foreach ($point in $shieldPoints) {
    $shadowPoints += [System.Drawing.Point]::new($point.X + 2, $point.Y + 2)
}
$graphics.FillPolygon($darkBrush, $shadowPoints)

# Draw main shield body
$graphics.FillPolygon($shieldBrush, $shieldPoints)

# Draw shield border
$graphics.DrawPolygon($borderPen, $shieldPoints)

# Add database symbol inside shield (cylinder representing database)
$dbWidth = 20
$dbHeight = 14
$dbX = $centerX - $dbWidth/2
$dbY = $centerY - 2

# Database cylinder top
$graphics.FillEllipse($whiteBrush, $dbX, $dbY, $dbWidth, 4)
# Database cylinder body  
$graphics.FillRectangle($whiteBrush, $dbX, $dbY + 2, $dbWidth, $dbHeight - 4)
# Database cylinder bottom
$graphics.FillEllipse($whiteBrush, $dbX, $dbY + $dbHeight - 4, $dbWidth, 4)

# Add database data lines
$graphics.FillRectangle($lightBrush, $dbX + 3, $dbY + 4, $dbWidth - 6, 1)
$graphics.FillRectangle($lightBrush, $dbX + 3, $dbY + 6, $dbWidth - 6, 1)
$graphics.FillRectangle($lightBrush, $dbX + 3, $dbY + 8, $dbWidth - 6, 1)
$graphics.FillRectangle($lightBrush, $dbX + 3, $dbY + 10, $dbWidth - 6, 1)

# Add security checkmark or sync indicator in upper right of shield
$checkSize = 6
$checkX = $centerX + 8
$checkY = $shieldTop + 12
$graphics.FillEllipse($accentBrush, $checkX, $checkY, $checkSize, $checkSize)

# Add small white checkmark inside the green circle
$graphics.FillRectangle($whiteBrush, $checkX + 1, $checkY + 2, 1, 2)
$graphics.FillRectangle($whiteBrush, $checkX + 2, $checkY + 3, 1, 2)
$graphics.FillRectangle($whiteBrush, $checkX + 3, $checkY + 1, 1, 3)

# Save the icon
$bitmap.Save("DbCop.ico", [System.Drawing.Imaging.ImageFormat]::Icon)

Write-Host ""
Write-Host "✅ Shield-based DbCop.ico created successfully!"
Write-Host ""
Write-Host "🛡️ Icon Design Features:"
Write-Host "   • Professional shield shape (representing security & protection)"
Write-Host "   • Database cylinder symbol (representing database operations)"
Write-Host "   • Success checkmark (representing safe operations)"
Write-Host "   • Blue security theme with professional colors"
Write-Host "   • Clean, modern design suitable for Windows applications"
Write-Host ""
Write-Host "Perfect for DbCop's safety-first approach to database synchronization!"

# Cleanup
$graphics.Dispose()
$bitmap.Dispose()
$shieldBrush.Dispose()
$darkBrush.Dispose()
$accentBrush.Dispose()
$whiteBrush.Dispose()
$lightBrush.Dispose()
$borderPen.Dispose()