using System.Text.Json;

namespace HollowKnightPet;

internal sealed class PetControls
{
    readonly HashSet<Keys> held = [];
    bool jump, attack;
    public bool Armed { get; private set; }
    public int Direction => Armed ? (held.Contains(Keys.D)?1:0)-(held.Contains(Keys.A)?1:0) : 0;
    public bool JumpHeld => Armed && held.Contains(Keys.Space);
    public void Activate() { Stop(); Armed=true; }
    public void Stop() { Armed=false; held.Clear(); jump=attack=false; }
    public void CheckFocus(bool petIsForeground) { if (!petIsForeground) Stop(); }
    public void KeyDown(Keys key)
    {
        if (!Armed || !held.Add(key)) return;
        if(key==Keys.Space) jump=true;
        if(key==Keys.J) attack=true;
    }
    public void KeyUp(Keys key) => held.Remove(key);
    public bool TakeJump() { bool value=jump; jump=false; return value; }
    public bool TakeAttack() { bool value=attack; attack=false; return value; }
}

internal sealed class PetSettings
{
    public bool MonstersEnabled { get; set; } = true;
    public int SpawnSeconds { get; set; } = 15;
    public int ActivationMouseButton {get;set;}=1;
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"HollowKnightPet","settings.json");
    public void Normalize()
    {
        SpawnSeconds=Math.Clamp(SpawnSeconds,2,300);
        if(ActivationMouseButton is <0 or >2) ActivationMouseButton=1;
    }
    public static PetSettings Load()
    {
        try { var value=JsonSerializer.Deserialize<PetSettings>(File.ReadAllText(FilePath))??new(); value.Normalize(); return value; }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public bool Save()
    {
        try { Normalize(); Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!); File.WriteAllText(FilePath,JsonSerializer.Serialize(this,new JsonSerializerOptions {WriteIndented=true})); return true; }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException) { return false; }
    }
}
