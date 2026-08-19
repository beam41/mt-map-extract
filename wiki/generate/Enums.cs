namespace MtExtract;

// Options.cs references these map-extractor enums; the generator never uses them.
internal enum TileFormat { Png, Jpeg, Webp, Avif }
internal enum ResampleKernel { Nearest, Linear, Cubic, Mitchell, Lanczos2, Lanczos3 }
