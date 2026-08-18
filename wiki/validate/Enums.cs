namespace MtExtract;

/// <summary>Options.cs parses these enums, which live in the main extractor's TileGenerator.cs;
/// the standalone parts tool defines them locally.</summary>
internal enum TileFormat { Png, Jpeg, Webp, Avif }

internal enum ResampleKernel { Nearest, Linear, Cubic, Mitchell, Lanczos2, Lanczos3 }
