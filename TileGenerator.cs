using System.Diagnostics;
using NetVips;

namespace MtExtract;

internal enum TileFormat { Png, Jpeg, Webp, Avif }

internal enum ResampleKernel { Nearest, Linear, Cubic, Mitchell, Lanczos2, Lanczos3 }

/// <summary>
/// Slices the world map into Leaflet/OpenLayers tiles (<c>{z}_{x}_{y}.{ext}</c>), the job the
/// standalone tilegen used to do.
///
/// Zoom levels: z0 is a 1x1 grid, zN is 2^N x 2^N. Native zoom is the smallest N where
/// 2^N * tileSize covers the image - at that level nothing is scaled.
/// </summary>
internal static class TileGenerator
{
    public static void Generate(byte[] png, Options opts)
    {
        using var image = Image.NewFromBuffer(png);
        var tileSize = (int)opts.TileSize;
        var maxDimension = Math.Max(image.Width, image.Height);

        var nativeZoom = 0;
        while ((1 << nativeZoom) * tileSize < maxDimension) nativeZoom++;
        var maxZoom = opts.MaxZoom ?? nativeZoom;

        Console.WriteLine($"Tiling {image.Width}x{image.Height} to {opts.TilesOut}/ " +
                          $"(z0-z{maxZoom}, native z{nativeZoom}, {tileSize}px {Extension(opts.Format)})");
        Directory.CreateDirectory(opts.TilesOut);

        var total = Stopwatch.StartNew();
        for (var zoom = 0; zoom <= maxZoom; zoom++)
        {
            var grid = 1 << zoom;
            var canvasSize = grid * tileSize;
            var scale = (double)canvasSize / maxDimension;

            // vscale has to be passed explicitly - the default 0.0 goes through literally and
            // collapses the height to nothing.
            using var scaled = Math.Abs(scale - 1.0) < 1e-6
                ? image.Copy()
                : image.Resize(scale, kernel: Kernel(scale > 1.0 ? opts.Upscale : opts.Downscale), vscale: scale);

            // Square the canvas out so edge tiles exist even when the map is not a square.
            using var canvas = scaled.Embed(0, 0, canvasSize, canvasSize);

            var coordinates = Enumerable.Range(0, grid)
                .SelectMany(x => Enumerable.Range(0, grid).Select(y => (X: x, Y: y)))
                .ToArray();

            var sw = Stopwatch.StartNew();
            Parallel.ForEach(coordinates, new ParallelOptions { MaxDegreeOfParallelism = opts.Threads }, tile =>
            {
                using var cropped = canvas.ExtractArea(tile.X * tileSize, tile.Y * tileSize, tileSize, tileSize);
                Save(cropped, Path.Combine(opts.TilesOut, $"{zoom}_{tile.X}_{tile.Y}.{Extension(opts.Format)}"), opts);
            });

            Console.WriteLine($"  z={zoom} {grid}x{grid} = {coordinates.Length} tiles in {sw.Elapsed:mm\\:ss\\.ff}");
        }

        Console.WriteLine($"  tiles took {total.Elapsed:mm\\:ss}");
    }

    private static void Save(Image tile, string path, Options opts)
    {
        var quality = opts.Quality;
        switch (opts.Format)
        {
            case TileFormat.Png:
                tile.Pngsave(path, compression: Math.Clamp(opts.Effort, 0, 9));
                break;
            case TileFormat.Jpeg:
                tile.Jpegsave(path, q: quality);
                break;
            case TileFormat.Webp:
                tile.Webpsave(path, q: quality, effort: Math.Clamp(opts.Effort, 0, 6));
                break;
            case TileFormat.Avif:
                tile.Heifsave(path, q: quality, effort: Math.Clamp(opts.Effort, 0, 9),
                    compression: Enums.ForeignHeifCompression.Av1);
                break;
        }
    }

    public static string Extension(TileFormat format) => format switch
    {
        TileFormat.Png => "png",
        TileFormat.Jpeg => "jpg",
        TileFormat.Webp => "webp",
        TileFormat.Avif => "avif",
        _ => "png",
    };

    private static Enums.Kernel Kernel(ResampleKernel kernel) => kernel switch
    {
        ResampleKernel.Nearest => Enums.Kernel.Nearest,
        ResampleKernel.Linear => Enums.Kernel.Linear,
        ResampleKernel.Cubic => Enums.Kernel.Cubic,
        ResampleKernel.Mitchell => Enums.Kernel.Mitchell,
        ResampleKernel.Lanczos2 => Enums.Kernel.Lanczos2,
        _ => Enums.Kernel.Lanczos3,
    };
}
