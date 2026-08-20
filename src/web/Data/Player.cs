using Newtonsoft.Json;

namespace McServerMgmnt.Data;

public record Player
{
    [JsonProperty("uuid")]
    public string Id { get; set; }
    [JsonProperty("name")]
    public string Name { get; set; }
}

public record MojangPlayer
{
    [JsonProperty("id")]
    public string Id { get; set; }
    [JsonProperty("name")]
    public string Name { get; set; }
}

public static class PlayerExtension 
{
    public static Player ToPlayer(this MojangPlayer player)
    {
        return new Player
        {
            Id = player.Id,
            Name = player.Name,
        };
    }
}