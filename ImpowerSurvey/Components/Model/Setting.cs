namespace ImpowerSurvey.Components.Model;

public class Setting
{
    public Guid Id { get; set; }
    public string Key { get; set; }
    public string Value { get; set; }
    public SettingType Type { get; set; }
    public string Description { get; set; }
    public string Category { get; set; }
    public bool IsVisible { get; set; } = true;
    public int DisplayOrder { get; set; } = 0;
}