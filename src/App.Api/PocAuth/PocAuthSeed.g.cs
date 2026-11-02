namespace App.Api.PocAuth;

public static class PocAuthSeed
{
    public static readonly IReadOnlyList<PocSeedUser> Users =
    [
        new PocSeedUser("pat@example.com", "Requester", ["user"], "9xyZLkIvOfMFiNnfvMKCHg==", "OMDzJp+hAPieriTqC+hZBhtxNLglIghKJ8EqjFU15GI=", 220000),
        new PocSeedUser("admin", "Admin", ["admin"], "LBSHqutAr4Yawb+rrNPxxg==", "ejdCula+4MlPGpyEv3f756GgTZlMokpj/h7yeuSvtUA=", 220000),
    ];
}
