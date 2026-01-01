namespace CS2_AntiCamp;

public class PluginConfig
{
    public bool Enabled { get; set; } = true;
    public int PunishCooldownSeconds { get; set; } = 5;
    public int PunishDamage { get; set; } = 2;
    public int MaxCampingDistance { get; set; } = 60;
    public int MinCampingDurationSeconds { get; set; } = 5;
    public string SlapSound { get; set; } = "UI.Guardian.TooFarWarning";
}

