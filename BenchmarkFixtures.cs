using SkiaSharp;

namespace ViewerRendererBenchmark;

internal static class BenchmarkFixtures
{
    internal static string[] Create(string directory)
    {
        Directory.CreateDirectory(directory);
        using var surface = SKSurface.Create(new SKImageInfo(4000, 3000));
        var canvas = surface.Canvas;
        using var gradient = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(4000, 3000),
            [new SKColor(20, 50, 160), new SKColor(210, 170, 90), new SKColor(40, 160, 100)], null, SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = gradient, IsAntialias = true };
        canvas.DrawRect(new SKRect(0, 0, 4000, 3000), paint);
        paint.Shader = null;
        for (var row = 0; row < 180; row++)
        {
            paint.Color = new SKColor((byte)(row * 29), (byte)(row * 13), (byte)(row * 41), 100);
            canvas.DrawCircle((row * 173) % 4000, (row * 227) % 3000, 15 + row % 90, paint);
        }
        paint.Color = new SKColor(220, 240, 245, 180);
        paint.StrokeWidth = 1.5f;
        for (var x = 0; x < 4000; x += 37) canvas.DrawLine(x, 0, x + 400, 3000, paint);
        using var image = surface.Snapshot();
        var jpeg = Path.Combine(directory, "photo-12mp.jpg");
        var png = Path.Combine(directory, "photo-12mp.png");
        using (var data = image.Encode(SKEncodedImageFormat.Jpeg, 90)) using (var output = File.Create(jpeg)) data.SaveTo(output);
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100)) using (var output = File.Create(png)) data.SaveTo(output);
        using var pixels = SKSurface.Create(new SKImageInfo(256, 256));
        for (var y = 0; y < 16; y++) for (var x = 0; x < 16; x++)
        {
            paint.Color = (x + y) % 2 == 0 ? SKColors.OrangeRed : SKColors.DodgerBlue;
            pixels.Canvas.DrawRect(new SKRect(x * 16, y * 16, (x + 1) * 16, (y + 1) * 16), paint);
        }
        var pixel = Path.Combine(directory, "pixel-256.png");
        using (var bitmap = pixels.Snapshot()) using (var data = bitmap.Encode(SKEncodedImageFormat.Png, 100))
        using (var output = File.Create(pixel)) data.SaveTo(output);
        return [jpeg, png, pixel];
    }
}

