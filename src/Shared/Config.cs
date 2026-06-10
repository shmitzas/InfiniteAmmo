namespace InfiniteAmmo;

public partial class InfiniteAmmo
{
    public class Config
    {
        public bool DetailedLogging { get; set; } = false;

        // Infinite Reserve Ammo (Clip2)
        public bool InfiniteReserveAmmo { get; set; } = true;
        public string InfiniteReserveAmmoFlag { get; set; } = "";

        // Infinite Chamber Ammo (Clip1)
        public bool InfiniteChamberAmmo { get; set; } = false;
        public string InfiniteChamberAmmoFlag { get; set; } = "";
    }
}
