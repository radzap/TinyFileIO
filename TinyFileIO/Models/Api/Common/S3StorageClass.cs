namespace TinyFileIO.Models.Api.Common;

/// <summary>Well-known x-amz-storage-class values.</summary>
public static class S3StorageClass
{
    public const string Standard = "STANDARD";
    public const string ReducedRedundancy = "REDUCED_REDUNDANCY";
    public const string StandardIa = "STANDARD_IA";
    public const string OnezoneIa = "ONEZONE_IA";
    public const string IntelligentTiering = "INTELLIGENT_TIERING";
    public const string Glacier = "GLACIER";
    public const string GlacierIr = "GLACIER_IR";
    public const string DeepArchive = "DEEP_ARCHIVE";
}
