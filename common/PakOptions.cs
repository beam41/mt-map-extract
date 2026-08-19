using CUE4Parse.UE4.Versions;

namespace MtExtract;

/// <summary>The minimal mount configuration every pak consumer needs (pak path, AES key,
/// usmap path, engine version). The amc-web extractor's Options maps onto it; the wiki
/// generator and explore tools build their own from their own CLI flags.</summary>
public sealed record PakOptions(string PakPath, string AesKey, string UsmapPath, EGame Game);
