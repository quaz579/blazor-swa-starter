namespace App.Api.PocAuth;

public static class PocAuthSeed
{
    public static readonly IReadOnlyList<PocSeedUser> Users =
    [
        new PocSeedUser("probe@leadingedje.com", "probe@leadingedje.com", ["authenticated"], "aLRvEGttw7psnF9iGsAwAQ==", "A+dnm2J2Xbf6Q2hHeCRE2S3rWzEHX9E7k/TP3f8yBMM=", 220000),
        new PocSeedUser("admin", "admin", ["authenticated", "admin"], "LBSHqutAr4Yawb+rrNPxxg==", "ejdCula+4MlPGpyEv3f756GgTZlMokpj/h7yeuSvtUA=", 220000),
    ];
}
